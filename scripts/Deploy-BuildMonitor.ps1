#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes BuildMonitor (Release) and deploys to the local release folder.

.DESCRIPTION
    Default deploy target: C:\Utils\BuildMonitor
    Override with -DeployPath or BUILDMONITOR_DEPLOY_PATH.

    Asks a running tray to quit via POST /app/quit (control plane) before
    publishing/copying so binaries are unlocked. Does not kill the process.

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

function Get-ControlPlaneBaseUrl {
    $discovery = Join-Path $env:LOCALAPPDATA 'BuildMonitor\control-plane.json'
    if (Test-Path $discovery) {
        try {
            $json = Get-Content $discovery -Raw | ConvertFrom-Json
            if ($json.enabled -and $json.baseUrl) {
                return ([string]$json.baseUrl).TrimEnd('/')
            }
            if ($json.enabled -and $json.port) {
                return "http://127.0.0.1:$($json.port)"
            }
        }
        catch {
            # fall through
        }
    }

    return 'http://127.0.0.1:7700'
}

function Request-BuildMonitorQuit {
    $base = Get-ControlPlaneBaseUrl
    Write-Host "Requesting tray quit via $base/app/quit ..."

    $status = $null
    $transportFailed = $false
    try {
        $response = Invoke-WebRequest -Method Post -Uri "$base/app/quit" -UseBasicParsing -TimeoutSec 5
        $status = [int]$response.StatusCode
        Write-Host "Quit HTTP $status body=$($response.Content)"
    }
    catch {
        $transportFailed = $true
        try {
            if ($_.Exception.Response) {
                $status = [int]$_.Exception.Response.StatusCode
                $transportFailed = $false
                try {
                    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                    Write-Host "Quit HTTP $status body=$($reader.ReadToEnd())"
                }
                catch {
                    Write-Host "Quit HTTP $status (body unavailable)"
                }
            }
            else {
                Write-Host "Quit transport failure: $($_.Exception.Message)"
            }
        }
        catch {
            Write-Host "Quit transport failure: $($_.Exception.Message)"
        }
    }

    # Disposition mirrors AppQuitHttpDispositionClassifier (Infrastructure).
    if ($status -eq 202) {
        Write-Host "Quit accepted (HTTP 202). Waiting for control plane to stop ..."
        $deadline = (Get-Date).AddSeconds(45)
        while ((Get-Date) -lt $deadline) {
            try {
                Invoke-WebRequest -Uri "$base/projects" -UseBasicParsing -TimeoutSec 2 | Out-Null
                Start-Sleep -Milliseconds 400
            }
            catch {
                Write-Host "Tray control plane is down."
                return $true
            }
        }

        Write-Warning "Timed out waiting for tray quit after HTTP 202. Exit BuildMonitor from the tray menu (or approve a force-stop), then re-run deploy."
        return $false
    }

    if ($status -eq 404 -or $status -eq 503) {
        Write-Warning "Quit unavailable (HTTP $status). Exit BuildMonitor from the tray menu, then re-run deploy."
        return $false
    }

    if ($null -ne $status -and $status -ge 500) {
        Write-Warning "Quit failed with HTTP $status - tray is NOT treated as already stopped. Exit BuildMonitor from the tray menu (or approve a force-stop), then re-run deploy."
        return $false
    }

    if ($transportFailed -or $null -eq $status) {
        # Connection refused / already stopped is fine.
        Write-Host "Control plane not reachable (tray already stopped?). Continuing."
        return $true
    }

    Write-Warning "Unexpected quit HTTP status $status. Exit BuildMonitor from the tray menu, then re-run deploy."
    return $false
}

# Unlock deploy folder before publish/copy.
if (-not (Request-BuildMonitorQuit)) {
    throw "BuildMonitor is still running. Quit it (tray Exit or POST /app/quit), then re-run deploy."
}

# Build identity (publish-time + deploy-time).
$gitCommit = ([string](git rev-parse --short HEAD)).Trim()
$gitBranch = ([string](git rev-parse --abbrev-ref HEAD)).Trim()
if ($gitBranch -eq 'HEAD') {
    $gitBranch = 'detached'
}
$gitPorcelain = [string](git status --porcelain)
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
