# Health, logs, and restart behaviour

How BuildMonitor decides what failed and what the tray, status panel, and log viewer show.

## Behaviour

| Step | What | Where |
|------|------|--------|
| 1 | Build output appends lines and marks health dirty (no per-line parse) | `ProjectRuntime.OnBuildOutputLine` → `HealthCoalescer` |
| 2 | Coalescer parses counts + progress on a background loop (~250 ms) | `HealthCoalescer`, `ProjectRuntime.CoalesceHealthCore`, `BuildLogParser` |
| 3 | Tray receives immutable snapshot list at bounded rate | `ProjectOrchestrator.HealthUpdated` → `App.OnHealthUpdated` (`DispatcherPriority.Normal`, coalesced) |
| 4 | Run/watch output updates `runErrorCount` / `runWarningCount` | `DotNetRunOutputParser`, `OnRunProcessOutputLine` (coalesced same as build) |
| 5 | Snapshot picks display counts by lifecycle state; a failed current build (`lastBuildExitCode != 0`) dominates even while lifecycle stays `Watching` because the watch host is still alive | `HealthIssueCountsFormatter.SelectPrimaryCounts`, `ProjectHealthEvaluator` |
| 5b | Tray, log store, and log viewer all use the **current** MSBuild summary (`BuildIssueCountResolver` / `ParseErrorCount` / `ParseWarningCount`) — no carry-forward from previous builds | `BuildIssueCountResolver`, `BuildLogStore`, `ProjectRuntime.Build` |
| 5c | When **Force complete warning counts** is on (default), every `dotnet build` passes `--no-incremental`. When off, only startup / Rebuild / Rebuild & restart do (file-change builds may report 0/0) | `DotNetBuildArguments`, `ProjectRunOptions.ForceCompleteWarningCounts` |
| 5d | Post-build tests no longer clear build warning/error counts used for tray health | `ProjectRuntime.TestAsync` |
| 5d2 | `--no-build` tests: VSTest `Test run for` is not “executed”; missing/stale assemblies get one full-build retry | `DotNetTestOutputParser`, `TestRunRecoveryCoordinator`, `ProjectRuntime.Test` |
| 5e | **Agent-aware build suppression** — defer startup until edits settle; cancel superseded startup/file-change builds; optional agent-transcript activity signal | `EditActivityEvaluator`, `BuildSuppressionPolicy`, `ProjectRuntime` |
| 5f | Edit gating auto-shows status panel with hold detail + countdown | `App.AutoShowStatusPanelForEditGating`, `HoverStatusPanel` |
| 6 | Tray hover opens the status panel (project health, counts, gating detail, actions); native shell tooltip is suppressed. Panel stays open while the cursor is on the icon or panel — no Closing countdown on hover | `HoverStatusPanel`, `TrayIconShellInterop`, `App.OnNotifyIconMouseMove` |
| 7 | Status panel shows `IssueCountsText` (build vs run context) | `HoverStatusPanel` |
| 7b | **AI working?** (header only) — extends rebuild wait when countdown is active; marks in-flight build **Unexpected** while building. Countdown auto-extends on agent tooling activity and resets on meaningful source saves. | `HoverStatusPanel`, `ProjectRuntime.HandleStillEditingClick`, `EditGatingQuietUntilResolver` |
| 8 | Log viewer parses Build / Run / Test tabs with matching parsers | `BuildLogViewerWindow.ParseIssuesForCurrentLog` |
| 8b | Log viewer footer uses the same MSBuild summary counts as the tray (`BuildIssueCountResolver`) | `BuildLogViewerWindow.RefreshResolvedIssueCounts` |
| 9 | Auto-open log per project (`Never` / `Errors` / `Warnings` / `Always`). **Errors** opens on a newly completed failed build result, including watch rebuilds that stay `Watching` | `App.AutoOpenLogsOnTransition`, `AutoOpenLogSession`, `AutoOpenLogTransitionEvaluator`, `BuildResultTransitionEvaluator` |
| 9b | Auto-show status panel while Local/Azure build activity (app-level, default **on** for both); edit-gating flows respect Local toggle | `App.SyncBuildActivityStatusPanelVisibility`, `StatusPanelVisibilityPolicy`, `StatusPanelBuildVisibilityEvaluator` |
| 9c | Build-failure toasts use the same completed-build-result transition (not `Building → BuildFailed` alone) | `BuildLifecycleToastEvaluator`, `BuildLifecycleToastNotifier` |
| 10 | **Restart app** stops run/watch and starts with `--no-build` | `ProjectRuntime.RestartAppCoreAsync(rebuildFirst: false)` |
| 11 | **Rebuild & restart** runs full build then starts app | `ProjectRuntime.RestartAppCoreAsync(rebuildFirst: true)` |
| 12 | Hot-reload “requires restart/rebuild” lines trigger auto-restart when enabled | `HotReloadRestartDetector`, `ProjectRuntime.TryHandleHotReloadRestartRequest` |

## Tray health coalescing (build / watch)

During large builds MSBuild can emit thousands of lines. Parsing issue counts and refreshing the tray on every line starves the UI thread.

**Builds and runs execute on thread-pool / process output threads**, not the WPF dispatcher. Auto-start and **Local-affecting** settings apply call `ApplySettingsAndStartAsync` via `Task.Run` so `dotnet build` continuations do not marshal back to the UI thread (manual **Rebuild** from the tray already used this pattern).

Settings Save is classified by `SettingsApplyImpactClassifier` using the exhaustive
`SettingsApplyImpactCatalog` (every persisted leaf path under `AppSettings`):

| Impact | Example | Local action |
|--------|---------|--------------|
| Presentation | Tray menu layout, theme, toasts, VD follow | No |
| SoftRuntime | Monitor, Azure, display name, test/restart/build-control policies, Local UI prefs | No (orchestrator `UpdateDefinition` only) |
| HardRestart | Local Id/active, RootFolder/ProjectFile/launch/args, RunMode, WatchExcludeSegments | Remount **affected** Local runtimes **without** `BuildAsync` |
| None | Identical save / schema version only | No |

Azure-only project add/active toggles are **SoftRuntime** (not HardRestart). Presentation-only saves still refresh the tray menu immediately; they must not schedule a Local rebuild.

**HardRestart invalidates live Local process/watcher context for the changed project(s) only** when practical. Remount recreates the watcher and may restart the app with `--no-build`; it never compiles solely because Settings were saved. Cold BuildMonitor startup (`before == null`) still uses `StartAsync` / StartOnLaunch startup builds. Policy knobs read on the next crash/build/test (RunTests, TestProjectFile, restart flags, BuildControlMode, FileChanges, lock/repair) remain SoftRuntime.

Coverage: `SettingsApplyImpactClassifierTests.Catalog_covers_every_discovered_persisted_leaf_path` fails if a new persisted property is added without a catalog entry. Mutation theories assert each catalog path yields its declared impact.

The tray uses WinForms `NotifyIcon` with `ContextMenuStrip` assigned directly (same as `main`). The tray icon is the **builder-duck** asset family (#95): presentation state comes from `TrayIconPresentationMapper` (Failed > Building > Attention > Healthy > Neutral) and static multi-size ICOs (`16/20/24/32`) via `TrayIconFactory` (embedded from `src/TrayApp/Assets/tray/runtime/`, sourced from the accepted `docs/assets/tray-icon-family-final/` family). Building is a **stable** mascot icon — the old 350 ms traffic-light build-pulse is not used for mascot presentation. Legacy `TrafficLightIconFactory` remains as an obsolete fallback if mascot resources fail to load. Health snapshots are coalesced in `HealthCoalescer` (~250 ms) on a background thread. The UI applies them via a single coalesced `Dispatcher.BeginInvoke(Normal)` pass so the tray stays in step with build toasts: tray icon always updates; hover panel updates only when visible; toasts and sounds are skipped while the tray menu is open. `HealthCoalescer` also pauses publish while the menu is open. Agent-tooling folder activity marks health dirty for the next coalesce tick (not an immediate publish) so Cursor writes do not flood the UI; lifecycle and meaningful source saves still request immediate coalesce.

| Piece | Behaviour |
|-------|-----------|
| Hot path | `OnBuildOutputLine` / `OnRunProcessOutputLine` / `OnTestOutputLine` append output and set `healthDirty`; progress-step changes enqueue a coalesce signal |
| Worker | `HealthCoalescer` — one background loop per `ProjectOrchestrator`, drains dirty runtimes every ~250 ms |
| Forced flush | State transitions (build finished, test done, watch compile done, crash) call `Request(immediate: true)` |
| UI apply | `App.OnHealthUpdated` → `Dispatcher.BeginInvoke(Normal)` — icon, status panel, toasts |

Per-project dirty flags; up to `MaxConcurrentActiveProjects` (default 3) share one worker and one combined `HealthUpdated` event.

**Extension points:** add run-error heuristics in `DotNetRunOutputParser`; adjust status formatting in `HealthIssueCountsFormatter`.

**Failure / fallback:** native shell tooltip is suppressed via empty `NotifyIcon.Text` (custom hint only). `TrayIconShellInterop` resolves icon bounds for hint dismiss. Issue scroll uses text-match fallback when line index drifts after log truncation.

**Watch rebuild vs process-alive:** `dotnet watch` can stay running after a compile failure. Lifecycle may remain `Watching` (host alive) while `lastBuildExitCode != 0`. Health must be **Failed/red**, `FailurePhase` **Build failed**, and current build error counts visible. A newly completed failed watch rebuild raises the normal build-failure toast and satisfies **Auto-open log = Errors** once per completed result (`LastBuildFinishedAtUtc` change). A later successful watch rebuild may return to healthy/warning as appropriate. Do not treat “process is alive” as “build is healthy”.

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
