# DEPRECATED — use Sync-ProjectBoard.ps1 (creates UX plan items as Done + planned Todo).
# Creates UX plan GitHub issues and adds them to BuildMonitor project #3.
# Run: .\scripts\github\Create-UxPlanIssues.ps1

$gh = "C:\Program Files\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = "gh" }

$issues = @(
    @{
        Title = "Bug: Status panel and log viewer disagree on error/warning counts"
        Body  = @"
## Problem
Status panel shows build-time error/warning counts while log viewer on Run tab may show different runtime error counts.

## Acceptance criteria
- [ ] Status panel shows context-aware counts (build vs run)
- [ ] When run has errors, stale build warnings do not mask run failures
- [ ] Counts align with log viewer for the relevant tab

## Surfaces
TrayApp, Infrastructure
"@
    },
    @{
        Title = "Bug: Tray tooltip does not distinguish run failure or show error detail"
        Body  = @"
## Problem
Tray tooltip only says ``Build monitor - Failed`` without run vs build phase or error text.

## Acceptance criteria
- [ ] Tooltip names headline project and failure phase
- [ ] LastErrorPreview shown when space allows (63 char limit)

## Surfaces
TrayApp
"@
    },
    @{
        Title = "Enhancement: Surface runtime errors in log viewer and auto-open Run tab"
        Body  = @"
## Problem
On runtime failure, log viewer opens on Build tab.

## Acceptance criteria
- [ ] Auto-open Run tab when Crashed
- [ ] Errors filter when errors exist
- [ ] Error indicator in log viewer header

## Surfaces
TrayApp
"@
    },
    @{
        Title = "Bug: Clicking an issue in log viewer scrolls to wrong line"
        Body  = @"
## Problem
Issue list selection scrolls to wrong log line.

## Acceptance criteria
- [ ] Click scrolls to correct line
- [ ] Text-match fallback after truncation
- [ ] Unit test

## Surfaces
TrayApp
"@
    },
    @{
        Title = "Enhancement: Copy errors only in log viewer"
        Body  = @"
## Problem
Copy issues copies warnings when filter is All.

## Acceptance criteria
- [ ] Button renamed to Copy errors
- [ ] Always copies errors only

## Surfaces
TrayApp
"@
    },
    @{
        Title = "Enhancement: Split Restart app vs Rebuild and restart with clear progress"
        Body  = @"
## Problem
Single Restart app is ambiguous; no build progress on restart.

## Acceptance criteria
- [ ] Restart app (no rebuild) and Rebuild & restart actions
- [ ] Clear status during each

## Surfaces
TrayApp, Infrastructure
"@
    },
    @{
        Title = "Bug: Restart app sometimes does not restart watch/build as expected"
        Body  = @"
## Problem
Restart occasionally fails to restart watch.

## Acceptance criteria
- [ ] Reliable watch restart
- [ ] Live log attaches after restart

## Surfaces
Infrastructure
"@
    },
    @{
        Title = "Bug: dotnet watch rebuilds on Cursor/IDE file activity — limit watch scope"
        Body  = @"
## Problem
dotnet watch rebuilds on IDE file activity.

## Acceptance criteria
- [ ] Watch exclude globs setting
- [ ] Document default excludes

## Surfaces
Settings, Infrastructure, docs
"@
    },
    @{
        Title = "Docs: Feature doc for health/log/restart behaviour"
        Body  = @"
## Problem
Health/log/restart behaviour undocumented.

## Acceptance criteria
- [ ] docs/features/health-and-logs.md
- [ ] SETTINGS.md and docs/README.md updated

## Surfaces
docs
"@
    }
)

foreach ($issue in $issues) {
    $json = & $gh issue create --repo Unthred/BuildMonitor `
        --title $issue.Title `
        --body $issue.Body `
        --assignee "@me" `
        --project "BuildMonitor" `
        --format json | ConvertFrom-Json
    $url = $json.url
    & $gh project item-add 3 --owner Unthred --url $url | Out-Null
    Write-Host "Created $url"
}
