#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes BuildMonitor (Release) and deploys to the local release folder.

.DESCRIPTION
    Default deploy target: C:\Utils\BuildMonitor
    Override with -DeployPath or BUILDMONITOR_DEPLOY_PATH.

.EXAMPLE
    .\scripts\Deploy-BuildMonitor.ps1
.EXAMPLE
    .\scripts\Deploy-BuildMonitor.ps1 -DeployPath D:\Tools\BuildMonitor
#>
[CmdletBinding()]
param(
    [string]$DeployPath = $(if ($env:BUILDMONITOR_DEPLOY_PATH) { $env:BUILDMONITOR_DEPLOY_PATH } else { 'C:\Utils\BuildMonitor' }),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$projectPath = Join-Path $repoRoot 'src\TrayApp\BuildMonitor.TrayApp.csproj'
$staging = Join-Path $repoRoot "artifacts\publish\$Configuration"

# Build identity (publish-time + deploy-time).
$gitCommit = (git rev-parse --short HEAD).Trim()
$gitBranch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($gitBranch -eq 'HEAD') {
    $gitBranch = 'detached'
}
$gitPorcelain = (git status --porcelain).Trim()
$gitDirty = -not [string]::IsNullOrWhiteSpace($gitPorcelain)
$gitDirtyText = if ($gitDirty) { 'true' } else { 'false' }
$builtUtc = (Get-Date).ToUniversalTime().ToString('o')
$commitDisplay = if ($gitDirty) { "$gitCommit-dirty" } else { $gitCommit }

$csprojText = Get-Content $projectPath -Raw
$version = '0.1.0'
$m = [regex]::Match($csprojText, '<VersionPrefix>([^<]+)</VersionPrefix>')
if ($m.Success) {
    $version = $m.Groups[1].Value.Trim()
}

Write-Host "Publishing $Configuration to $staging ..."
dotnet publish $projectPath -c $Configuration -o $staging `
    -p:GitCommitShort=$gitCommit `
    -p:GitBranch=$gitBranch `
    -p:BuiltUtc=$builtUtc `
    -p:GitDirty=$gitDirtyText
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $DeployPath)) {
    Write-Host "Creating deploy folder: $DeployPath"
    New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null
}

Write-Host "Deploying to $DeployPath ..."
$robocopyArgs = @(
    $staging,
    $DeployPath,
    '/MIR',
    '/NFL', '/NDL', '/NJH', '/NJS', '/NC', '/NS', '/NP'
)
& robocopy @robocopyArgs | Out-Null
$robocopyExit = $LASTEXITCODE
if ($robocopyExit -ge 8) {
    throw "robocopy failed with exit code $robocopyExit"
}

$deployedUtc = (Get-Date).ToUniversalTime().ToString('o')
$versionFile = Join-Path $DeployPath 'deploy-info.txt'
@"
BuildMonitor local release deploy
Version: $version
Commit: $commitDisplay
CommitBranch: $gitBranch
BuiltUtc: $builtUtc
DeployedUtc: $deployedUtc
Configuration: $Configuration
Source: $repoRoot
PublishOutput: $staging
Dirty: $gitDirtyText
"@ | Set-Content -Path $versionFile -Encoding utf8

Write-Host "Done. Run: $DeployPath\BuildMonitor.TrayApp.exe"
