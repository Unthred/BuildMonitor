# Health, logs, and restart behaviour

How BuildMonitor decides what failed and what the tray, status panel, and log viewer show.

## Behaviour

| Step | What | Where |
|------|------|--------|
| 1 | Build output appends lines and marks health dirty (no per-line parse) | `ProjectRuntime.OnBuildOutputLine` → `HealthCoalescer` |
| 2 | Coalescer parses counts + progress on a background loop (~250 ms) | `HealthCoalescer`, `ProjectRuntime.CoalesceHealthCore`, `BuildLogParser` |
| 3 | Tray receives immutable snapshot list at bounded rate | `ProjectOrchestrator.HealthUpdated` → `App.OnHealthUpdated` (`ApplicationIdle`, coalesced) |
| 4 | Run/watch output updates `runErrorCount` / `runWarningCount` | `DotNetRunOutputParser`, `OnRunProcessOutputLine` (coalesced same as build) |
| 5 | Snapshot picks display counts by lifecycle state | `HealthIssueCountsFormatter.SelectPrimaryCounts` |
| 6 | Tray tooltip uses headline project + failure phase + error preview | `App.FormatTrayTooltip` |
| 7 | Status panel shows `IssueCountsText` (build vs run context) | `HoverStatusPanel` |
| 8 | Log viewer parses Build / Run / Test tabs with matching parsers | `BuildLogViewerWindow.ParseIssuesForCurrentLog` |
| 9 | Failure auto-opens log on correct tab + Errors filter | `App.AutoOpenLogsOnFailureTransition` |
| 10 | **Restart app** stops run/watch and starts with `--no-build` | `ProjectRuntime.RestartAppCoreAsync(rebuildFirst: false)` |
| 11 | **Rebuild & restart** runs full build then starts app | `ProjectRuntime.RestartAppCoreAsync(rebuildFirst: true)` |
| 12 | Hot-reload “requires restart/rebuild” lines trigger auto-restart when enabled | `HotReloadRestartDetector`, `ProjectRuntime.TryHandleHotReloadRestartRequest` |

## Tray health coalescing (build / watch)

During large builds MSBuild can emit thousands of lines. Parsing issue counts and refreshing the tray on every line starves the UI thread.

**Builds and runs execute on thread-pool / process output threads**, not the WPF dispatcher. Auto-start and settings apply call `ApplySettingsAndStartAsync` via `Task.Run` so `dotnet build` continuations do not marshal back to the UI thread (manual **Rebuild** from the tray already used this pattern).

The tray uses WinForms `NotifyIcon` with `ContextMenuStrip` assigned directly (same as `main`). Health snapshots are coalesced in `HealthCoalescer` (~250 ms) on a background thread. The UI applies them via a single coalesced `Dispatcher.BeginInvoke(ApplicationIdle)` pass: tray icon always updates; hover panel updates only when visible; toasts and sounds are skipped while the tray menu is open. `HealthCoalescer` also pauses publish while the menu is open.

| Piece | Behaviour |
|-------|-----------|
| Hot path | `OnBuildOutputLine` / `OnRunProcessOutputLine` / `OnTestOutputLine` append output and set `healthDirty`; progress-step changes enqueue a coalesce signal |
| Worker | `HealthCoalescer` — one background loop per `ProjectOrchestrator`, drains dirty runtimes every ~250 ms |
| Forced flush | State transitions (build finished, test done, watch compile done, crash) call `Request(immediate: true)` |
| UI apply | `App.OnHealthUpdated` → `Dispatcher.BeginInvoke(Normal)` — icon, status panel, toasts |

Per-project dirty flags; up to `MaxConcurrentActiveProjects` (default 3) share one worker and one combined `HealthUpdated` event.

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

## Corrupted build output trees

MSBuild output can become poisoned when something builds with a custom **`BaseOutputPath`** (e.g. `artifacts/build/`) while **`dotnet watch`** is also running — nested `artifacts\build\...\bin\...\artifacts\build\...` paths and copy failures for `appsettings.json` and similar.

| Mechanism | Behaviour |
|-----------|-----------|
| Detection | `CorruptedOutputTreeDetector` — nested path patterns in logs + optional disk check under `artifacts/build` |
| Auto-repair | Per-project `autoRepairCorruptedOutput` (default on) — stop, delete `artifacts/` / `bin/` / `obj/`, retry build |
| Manual | Tray **Clean build output** |
| Prevention | Warn when `extraDotNetArgs` contains `BaseOutputPath` with Watch mode |

BuildMonitor never passes `BaseOutputPath` to child `dotnet` processes.

## Build Monitor Health

Tray → **Build Monitor Health…** opens a live grid of worker heartbeats. With **`monitor.autoOpenBuildMonitorHealthOnStartup`** (default on after schema v7), the window opens automatically at launch **before** projects with **`startOnLaunch`** begin building.

| Worker | What it measures |
|--------|------------------|
| **Health coalescer loop** | Background ~250 ms parse/publish loop |
| **Health publish to tray** | When coalesced snapshots are raised to the UI |
| **HealthUpdated event** | Background thread invoking the tray subscriber |
| **WPF UI dispatcher** | Thread-pool ping round-trip to the UI thread (timeouts ⇒ UI blocked) |
| **Tray health UI callback** | Duration of one `OnHealthUpdated` pass (icon, hover panel, toasts) |
| **Current activity** (footer card) | Three always-visible tray workers (dispatcher, health UI callback, coalescer loop — `Idle` when quiet), plus one lifecycle line per project. |
| **Per-project rows** | Build/run/test output, file watcher, lifecycle state |

Open **Build Monitor Health** while the tray is still responsive, then trigger a build. If **WPF UI dispatcher** goes **Blocked** or **Tray health UI callback** shows multi-second **Last work** while coalescer rows stay **OK**, the UI thread is saturated — not the right-click menu alone.

To isolate a heavy project: **Settings → Projects** → uncheck **Active in session** for that project (e.g. Vessel Compliance) → **Save**. Unchecked projects stay listed but are not built or run until re-enabled.
