<#
.SYNOPSIS
  Installs the BuildMonitor control-plane Cursor skill (probe + busy/idle/ship-check).

.PARAMETER TargetRepoRoot
  Optional path to a watched product repo. When set, installs as a project skill under
  <TargetRepoRoot>\.cursor\skills\buildmonitor-control-plane\.
  When omitted, installs as a personal skill under %USERPROFILE%\.cursor\skills\.
#>
[CmdletBinding()]
param(
    [string] $TargetRepoRoot
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$source = Join-Path $repoRoot "docs\ops\agent-skills\buildmonitor-control-plane\SKILL.md"
if (-not (Test-Path $source)) {
    throw "Skill source not found: $source"
}

if ($TargetRepoRoot) {
    $destDir = Join-Path (Resolve-Path $TargetRepoRoot) ".cursor\skills\buildmonitor-control-plane"
} else {
    $destDir = Join-Path $env:USERPROFILE ".cursor\skills\buildmonitor-control-plane"
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item -Path $source -Destination (Join-Path $destDir "SKILL.md") -Force
Write-Host "Installed: $(Join-Path $destDir 'SKILL.md')"
Write-Host "Restart Cursor or start a new agent chat so the skill is picked up."
