<#
.SYNOPSIS
  Installs the BuildMonitor control-plane Cursor skill and always-on rule.

.PARAMETER TargetRepoRoot
  Optional path to a watched product repo. When set, installs under that repo's
  .cursor\skills and .cursor\rules folders.
  When omitted, installs the skill as a personal skill under %USERPROFILE%\.cursor\skills\
  and the always-on rule under %USERPROFILE%\.cursor\rules\.
#>
[CmdletBinding()]
param(
    [string] $TargetRepoRoot
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

if ($TargetRepoRoot) {
    $root = Resolve-Path $TargetRepoRoot
    $skillDestDir = Join-Path $root ".cursor\skills\buildmonitor-control-plane"
    $ruleDestDir = Join-Path $root ".cursor\rules"
} else {
    $skillDestDir = Join-Path $env:USERPROFILE ".cursor\skills\buildmonitor-control-plane"
    $ruleDestDir = Join-Path $env:USERPROFILE ".cursor\rules"
}

New-Item -ItemType Directory -Force -Path $skillDestDir | Out-Null
New-Item -ItemType Directory -Force -Path $ruleDestDir | Out-Null
Copy-Item -Path $skillSource -Destination (Join-Path $skillDestDir "SKILL.md") -Force
Copy-Item -Path $ruleSource -Destination (Join-Path $ruleDestDir "buildmonitor-control-plane.mdc") -Force
Write-Host "Installed skill: $(Join-Path $skillDestDir 'SKILL.md')"
Write-Host "Installed rule:  $(Join-Path $ruleDestDir 'buildmonitor-control-plane.mdc')"
Write-Host "Start a new agent chat in that workspace so Cursor picks up the rule/skill."
