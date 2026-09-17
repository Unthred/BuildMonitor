<#
.SYNOPSIS
  Installs or updates the BuildMonitor verification-provider adapter into a
  Cursor user profile. Never writes into product repositories.

.PARAMETER DestinationRoot
  Folder that contains `.cursor` (default: current user profile). Use a temp
  directory to test the installer.

.PARAMETER NoBackup
  Skip backing up existing skill/rule files.
#>
[CmdletBinding()]
param(
    [string] $DestinationRoot,
    [switch] $NoBackup
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$skillSource = Join-Path $repoRoot "docs\ops\agent-skills\buildmonitor-control-plane\SKILL.md"
$ruleSource = Join-Path $repoRoot "docs\ops\agent-skills\buildmonitor-control-plane\RULE.mdc"
if (-not (Test-Path $skillSource)) {
    throw "Skill source not found: $skillSource"
}
if (-not (Test-Path $ruleSource)) {
    throw "Rule source not found: $ruleSource"
}

if (-not $DestinationRoot) {
    $DestinationRoot = $env:USERPROFILE
}

$root = [IO.Path]::GetFullPath($DestinationRoot)
foreach ($marker in @("WitherbyConnect.csproj", "WitherbyConnect.sln", "WitherbyConnect.slnx")) {
    if (Test-Path (Join-Path $root $marker)) {
        throw "Refusing to install into a product repository: $root"
    }
}

$skillDestDir = Join-Path $root ".cursor\skills\buildmonitor-control-plane"
$ruleDestDir = Join-Path $root ".cursor\rules"
$skillDest = Join-Path $skillDestDir "SKILL.md"
$ruleDest = Join-Path $ruleDestDir "buildmonitor-control-plane.mdc"

if (-not $NoBackup -and ((Test-Path $skillDest) -or (Test-Path $ruleDest))) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupDir = Join-Path $root ".cursor\buildmonitor-adapter-backup\$stamp"
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    if (Test-Path $skillDest) {
        Copy-Item -Path $skillDest -Destination (Join-Path $backupDir "SKILL.md") -Force
    }
    if (Test-Path $ruleDest) {
        Copy-Item -Path $ruleDest -Destination (Join-Path $backupDir "buildmonitor-control-plane.mdc") -Force
    }
    Write-Host "Backup: $backupDir"
}

New-Item -ItemType Directory -Force -Path $skillDestDir | Out-Null
New-Item -ItemType Directory -Force -Path $ruleDestDir | Out-Null
Copy-Item -Path $skillSource -Destination $skillDest -Force
Copy-Item -Path $ruleSource -Destination $ruleDest -Force
Write-Host "Installed skill: $skillDest"
Write-Host "Installed rule:  $ruleDest"
Write-Host "Adapter source:  docs/ops/agent-skills/buildmonitor-control-plane (contract v1, adapter 1.0.0)"
Write-Host "Start a new agent chat so Cursor picks up the user-level adapter."
