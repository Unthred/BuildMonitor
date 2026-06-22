# Point this repo at .githooks/ (commit-msg enforces issue #N on every commit).
# Run once from repo root: .\scripts\install-githooks.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

git -C $root config core.hooksPath .githooks
Write-Host "Installed hooks from $root\.githooks (core.hooksPath=.githooks)"
Write-Host "Commits must include #<issue> or be on branch feature/<id>-..."
