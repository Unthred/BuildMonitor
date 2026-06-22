# Syncs BuildMonitor project board #3 with retrospective (Done) and planned (Todo) issues.
# Run from repo root: .\scripts\github\Sync-ProjectBoard.ps1
# Idempotent: skips issues whose titles already exist (open or closed).

param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Continue"
$gh = if (Test-Path "C:\Program Files\GitHub CLI\gh.exe") { "C:\Program Files\GitHub CLI\gh.exe" } else { "gh" }
$repo = "Unthred/BuildMonitor"
$projectOwner = "Unthred"
$projectNumber = 3
$projectId = "PVT_kwHOAFM-l84BaXQL"
$statusFieldId = "PVTSSF_lAHOAFM-l84BaXQLzhVPZxY"
$statusDoneId = "98236657"
$statusTodoId = "f75ad846"

function Get-ExistingIssueTitles {
    $json = & $gh issue list --repo $repo --state all --limit 200 --json title 2>$null | ConvertFrom-Json
    $set = @{}
    foreach ($row in $json) {
        $set[$row.title.ToLowerInvariant()] = $true
    }
    return $set
}

function Add-IssueToProject {
    param([string]$Url)
    if ($WhatIf) { Write-Host "[WhatIf] project item-add $Url"; return $null }
    & $gh project item-add $projectNumber --owner $projectOwner --url $Url 2>&1 | Out-Null
    Start-Sleep -Milliseconds 400
    $items = & $gh project item-list $projectNumber --owner $projectOwner --format json --limit 200 | ConvertFrom-Json
    foreach ($item in $items.items) {
        if ($item.content.url -eq $Url) { return $item.id }
    }
    return $null
}

function Set-ProjectItemStatus {
    param([string]$ItemId, [string]$StatusOptionId)
    if (-not $ItemId) { return }
    if ($WhatIf) { Write-Host "[WhatIf] item-edit $ItemId -> $StatusOptionId"; return }
    & $gh project item-edit `
        --project-id $projectId `
        --id $ItemId `
        --field-id $statusFieldId `
        --single-select-option-id $StatusOptionId 2>&1 | Out-Null
}

function New-BoardIssue {
    param(
        [string]$Title,
        [string]$Body,
        [ValidateSet("Done", "Todo")]
        [string]$BoardStatus
    )

    if ($existingTitles.ContainsKey($Title.ToLowerInvariant())) {
        Write-Host "Skip (exists): $Title"
        return
    }

    Write-Host "Create: $Title [$BoardStatus]"
    if ($WhatIf) { return }

    $url = (& $gh issue create --repo $repo --title $Title --body $Body --assignee "@me" | Out-String).Trim()
    $existingTitles[$Title.ToLowerInvariant()] = $true

    if ($BoardStatus -eq "Done") {
        $num = ($url -split "/")[-1]
        & $gh issue close $num --repo $repo --reason completed 2>&1 | Out-Null
    }

    $itemId = Add-IssueToProject -Url $url
    $optionId = if ($BoardStatus -eq "Done") { $statusDoneId } else { $statusTodoId }
    Set-ProjectItemStatus -ItemId $itemId -StatusOptionId $optionId
}

function Ensure-IssueOnBoard {
    param(
        [int]$Number,
        [ValidateSet("Done", "Todo")]
        [string]$BoardStatus
    )

    $url = "https://github.com/$repo/issues/$Number"
    Write-Host "Ensure on board: #$Number"
    if ($WhatIf) { return }

    $items = & $gh project item-list $projectNumber --owner $projectOwner --format json --limit 200 | ConvertFrom-Json
    $itemId = ($items.items | Where-Object { $_.content.number -eq $Number } | Select-Object -First 1).id
    if (-not $itemId) {
        $itemId = Add-IssueToProject -Url $url
    }
    $optionId = if ($BoardStatus -eq "Done") { $statusDoneId } else { $statusTodoId }
    Set-ProjectItemStatus -ItemId $itemId -StatusOptionId $optionId
}

$existingTitles = Get-ExistingIssueTitles

# --- Retrospective (shipped) ---

New-BoardIssue -Title "Foundation: GitHub workflow, tests, and no-downtime test runs" -BoardStatus Done -Body @"
## Summary
Initial delivery after repo bootstrap: Cursor rules/skills, GitHub workflow docs, xUnit test project, manual and automatic ``dotnet test``, ``--no-build`` tests while watch/run stays up, test project discovery, restart-app settings.

## Shipped
- PR #1
- Initial commit: local build monitor tray app

## Acceptance criteria
- [x] ``BuildMonitor.Tests`` project with unit tests
- [x] Tray **Run tests** with live Test tab output
- [x] ``docs/ops/github-workflow.md`` and work-tracking rules
"@

$uxPlanShippedIn2 = @(
    @{
        Title = "Bug: Status panel and log viewer disagree on error/warning counts"
        Body  = "Shipped in **#2** (PR #3). Context-aware build vs run counts via ``HealthIssueCountsFormatter``."
    },
    @{
        Title = "Bug: Tray tooltip does not distinguish run failure or show error detail"
        Body  = "Shipped in **#2** (PR #3). Headline project, failure phase, and error preview in tray tooltip."
    },
    @{
        Title = "Enhancement: Surface runtime errors in log viewer and auto-open Run tab"
        Body  = "Shipped in **#2** (PR #3). Auto-open correct tab and Errors filter on failure transition."
    },
    @{
        Title = "Bug: Clicking an issue in log viewer scrolls to wrong line"
        Body  = "Shipped in **#2** (PR #3). Line index + text-match fallback after log truncation."
    },
    @{
        Title = "Enhancement: Copy errors only in log viewer"
        Body  = "Shipped in **#2** (PR #3). **Copy errors** copies compiler errors only."
    },
    @{
        Title = "Enhancement: Split Restart app vs Rebuild and restart with clear progress"
        Body  = "Shipped in **#2** (PR #3). Separate tray actions and progress during rebuild."
    },
    @{
        Title = "Bug: Restart app sometimes does not restart watch/build as expected"
        Body  = "Shipped in **#2** (PR #3). Reliable watch restart and live log re-attach."
    },
    @{
        Title = "Bug: dotnet watch rebuilds on Cursor/IDE file activity - limit watch scope"
        Body  = "Shipped in **#2** (PR #3). Watch exclude segments and documented ``Watch Remove`` defaults."
    },
    @{
        Title = "Docs: Feature doc for health/log/restart behaviour"
        Body  = "Shipped in **#2** (PR #3). ``docs/features/health-and-logs.md`` and SETTINGS cross-links."
    }
)

foreach ($item in $uxPlanShippedIn2) {
    New-BoardIssue -Title $item.Title -BoardStatus Done -Body $item.Body
}

New-BoardIssue -Title "Enhancement: Per-project output lock release and MSB lock retry" -BoardStatus Done -Body @"
## Summary
Opt-in per-project **Stop processes locking build output** with graceful stop, delay, and single retry on MSB3021/3027. Non-elevated fallback when access denied.

## Shipped
- PR #3 (#2 epic)

## Acceptance criteria
- [x] Per-project toggle in Settings
- [x] Scoped process termination under project output paths
- [x] Toasts instead of blocking dialogs for lock-release failures
"@

New-BoardIssue -Title "Enhancement: Live log viewer with follow output and single window per project" -BoardStatus Done -Body @"
## Summary
Build log viewer streams live output during builds; **Follow output** checkbox; one log window per project (focus existing instead of opening duplicates).

## Shipped
- PR #3 (#2 epic)

## Acceptance criteria
- [x] Live tail while build/watch runs
- [x] Follow output persisted in window state
- [x] Reuse/focus existing viewer from tray and auto-open
"@

New-BoardIssue -Title "Enhancement: Toast lifecycle notifications and settings" -BoardStatus Done -Body @"
## Summary
Configurable toasts for build start/end/error, file-change rebuild, lock release; position and duration settings; click-to-copy on error toasts. Polished card UI in #4.

## Shipped
- PR #3 (#2), PR #5 (#4)

## Acceptance criteria
- [x] Per-event toast toggles in Settings
- [x] Position, duration, click-to-copy
- [x] Themed toast cards (#4)
"@

New-BoardIssue -Title "Enhancement: Listen URL discovery, HTTPS profile, and clickable status link" -BoardStatus Done -Body @"
## Summary
Detect listening URL from run/watch output; prefer HTTPS launch profile; clickable link in status panel; reduced run-log overhead for faster startup.

## Shipped
- PR #3 (#2 epic)

## Acceptance criteria
- [x] Launch profile resolution matches cmdline HTTPS usage
- [x] Clickable URL in hover status panel
- [x] Port/listen URL in project health snapshot
"@

New-BoardIssue -Title "Enhancement: Build progress, duplicate-build fixes, and hammer tray animation" -BoardStatus Done -Body @"
## Summary
Failed step in build progress list; prevent double build (watch + file watcher, failed rebuild restart); hammer overlay on traffic-light icon during builds; last build timestamp in status and log header.

## Shipped
- PR #3 (#2 epic)

## Acceptance criteria
- [x] Failed project marked in progress steps
- [x] No duplicate builds from overlapping triggers
- [x] Hammer animation on building state
"@

# --- Planned (Todo) ---

New-BoardIssue -Title "Enhancement: Run tests on file change (OnFileChange mode)" -BoardStatus Todo -Body @"
## Problem
``RunTests`` only supports ``Off`` and ``OnBuildSuccess``; ``OnFileChange`` is documented as planned.

## Acceptance criteria
- [ ] ``OnFileChange`` runs debounced ``dotnet test`` after file-triggered rebuild
- [ ] Respects same watcher debounce as builds
- [ ] Documented in ``docs/SETTINGS.md``

## Surfaces
Infrastructure, Settings, docs
"@

New-BoardIssue -Title "Enhancement: Open log viewer from tray context menu" -BoardStatus Todo -Body @"
## Problem
Log viewer is reachable from hover status panel; tray context menu does not yet expose **View log** per project.

## Acceptance criteria
- [ ] **View log** under each project in tray menu (both layout modes)
- [ ] Reuses single window per project
- [ ] ``docs/LOGS.md`` updated

## Surfaces
TrayApp, docs
"@

New-BoardIssue -Title "Enhancement: WPF tray context menu (Phase 2)" -BoardStatus Todo -Body @"
## Problem
Phase 1 health coalescing (#8) fixed most tray menu stalls. If menu remains sticky, migrate WinForms ``ContextMenuStrip`` to WPF ``ContextMenu``.

## Acceptance criteria
- [ ] Only implement if Phase 1 insufficient in production use
- [ ] Menu responsive during heavy builds
- [ ] Exit and dismiss behave reliably

## Surfaces
TrayApp

## Related
#8 (Done)
"@

New-BoardIssue -Title "Enhancement: Wire optional Azure DevOps polling module" -BoardStatus Todo -Body @"
## Problem
``Infrastructure/AzureDevOps/`` exists but is not wired into tray startup (optional future module per ``BUILD_STATUS_MONITOR_PLAN.md``).

## Acceptance criteria
- [ ] Settings schema for org URL and pipelines (or remove dead code)
- [ ] Tray integration behind explicit opt-in
- [ ] ADR if persistence/auth approach is chosen

## Surfaces
Infrastructure, TrayApp, docs
"@

New-BoardIssue -Title "Enhancement: Diagnostics verdict feedback loop for adaptive debounce" -BoardStatus Todo -Body @"
## Problem
Adaptive debounce (#7) learns from save bursts; diagnostics verdicts could refine learning (noted out of scope in #7).

## Acceptance criteria
- [ ] Verdict (helpful / noisy rebuild) stored per trigger journal entry
- [ ] Optional influence on debounce learning weights
- [ ] Unit tests for feedback calculator

## Surfaces
Infrastructure, TrayApp, docs

## Related
#7 (Done), #10 (Done)
"@

# --- Ensure existing issues are on the board ---

foreach ($n in @(2, 4, 6, 7, 8, 10, 12)) {
    Ensure-IssueOnBoard -Number $n -BoardStatus Done
}

# Any other closed issue missing from the board (e.g. partial sync) → Done
if (-not $WhatIf) {
    $allIssues = & $gh issue list --repo $repo --state all --limit 200 --json number,state | ConvertFrom-Json
    $boardItems = (& $gh project item-list $projectNumber --owner $projectOwner --format json --limit 200 | ConvertFrom-Json).items
    $onBoard = @{}
    foreach ($item in $boardItems) {
        if ($item.content.number) { $onBoard[$item.content.number] = $item.id }
    }
    foreach ($issue in $allIssues) {
        if ($onBoard.ContainsKey($issue.number)) { continue }
        $url = "https://github.com/$repo/issues/$($issue.number)"
        Write-Host "Backfill board: #$($issue.number)"
        $itemId = Add-IssueToProject -Url $url
        $status = if ($issue.state -eq "OPEN") { $statusTodoId } else { $statusDoneId }
        Set-ProjectItemStatus -ItemId $itemId -StatusOptionId $status
    }
}

Write-Host ""
Write-Host "Done. Board: https://github.com/users/Unthred/projects/3"
