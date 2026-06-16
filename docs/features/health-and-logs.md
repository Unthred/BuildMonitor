# Health, logs, and restart behaviour

How BuildMonitor decides what failed and what the tray, status panel, and log viewer show.

## Behaviour

| Step | What | Where |
|------|------|--------|
| 1 | Build output updates `buildErrorCount` / `buildWarningCount` | `ProjectRuntime.OnBuildOutputLine` |
| 2 | Run/watch output updates `runErrorCount` / `runWarningCount` | `DotNetRunOutputParser`, `OnRunProcessOutputLine` |
| 3 | Snapshot picks display counts by lifecycle state | `HealthIssueCountsFormatter.SelectPrimaryCounts` |
| 4 | Tray tooltip uses headline project + failure phase + error preview | `App.FormatTrayTooltip` |
| 5 | Status panel shows `IssueCountsText` (build vs run context) | `HoverStatusPanel` |
| 6 | Log viewer parses Build / Run / Test tabs with matching parsers | `BuildLogViewerWindow.ParseIssuesForCurrentLog` |
| 7 | Failure auto-opens log on correct tab + Errors filter | `App.AutoOpenLogsOnFailureTransition` |
| 8 | **Restart app** stops run/watch and starts with `--no-build` | `ProjectRuntime.RestartAppCoreAsync(rebuildFirst: false)` |
| 9 | **Rebuild & restart** runs full build then starts app | `ProjectRuntime.RestartAppCoreAsync(rebuildFirst: true)` |
| 10 | Hot-reload “requires restart/rebuild” lines trigger auto-restart when enabled | `HotReloadRestartDetector`, `ProjectRuntime.TryHandleHotReloadRestartRequest` |

**Extension points:** add run-error heuristics in `DotNetRunOutputParser`; adjust status formatting in `HealthIssueCountsFormatter`.

**Failure / fallback:** tray tooltip truncated to 63 characters; issue scroll uses text-match fallback when line index drifts after log truncation.

## Watch excludes (dotnet watch)

BuildMonitor’s file watcher (non-watch run modes) ignores segments from `RunOptions.WatchExcludeSegments` (see [SETTINGS.md](../SETTINGS.md)).

For **dotnet watch**, add `Watch Remove` items to the monitored `.csproj`. Example snippet (defaults):

```xml
<ItemGroup>
  <Watch Remove="**/.cursor/**" />
  <Watch Remove="**/agent-transcripts/**" />
  <Watch Remove="**/terminals/**" />
</ItemGroup>
```

Use `WatchExcludeSnippetFormatter` in Infrastructure for the full default list.
