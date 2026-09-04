# AI Controlled — autonomous build isolation

When `buildControlMode` is **AI Controlled**, source-file changes must never initiate compilation, rebuild, restart, or tests. Observation and status counts remain allowed.

## Inventory of autonomous initiators

| Initiator | Normal File Watching | AI Controlled prevention |
|-----------|----------------------|---------------------------|
| `DebouncedFileWatcher` → `OnFileWatcherChanged` → `BuildAsync` | Schedules after debounce when idle | `BuildTriggerPolicy.ShouldAutoBuildFromFileChange` is always false; observe-only path; no `WaitForEditQuietThenBuildAsync` |
| `WaitForEditQuietThenBuildAsync` | Debounced quiet then build | Not started; mid-flight cancelled via schedule generation; early-return if mode is AI |
| `SchedulePendingRebuildWhenReady` | After cooldown/tests | No-ops when auto-build disabled by mode |
| `BuildAsync` finally re-queue | Pending file-change after build | Skips scheduling when AI Controlled |
| `ExtendRebuildQuietPeriod` / Still Editing | Extends quiet + reschedules | Returns false immediately in AI Controlled |
| Adaptive debounce / edit-gating quiet UI | Countdown to rebuild | `GetEditGatingQuietUntilUtc` null; `IsEditGatingActive` false; status secondary suppressed |
| Agent transcript activity | Extends quiet semantics | Cannot schedule builds in AI; no quiet UI |
| `dotnet watch` host | Compiles/restarts on edits | **Never hosted** in AI Controlled (`UsesDotNetWatchProcess` false → `dotnet run --no-build`); mode switch migrates host |
| `DOTNET_WATCH_RESTART_ON_RUDE_EDIT` | Watch restarts | N/A — watch not started |
| Watch output “file changed / building” | Status + notifications | `HandleWatchProcessOutputLine` ignored if mode is AI; host should already be non-watch |
| `TryHandleHotReloadRestartRequest` | May rebuild/restart | Blocked unless explicit agent rebuild/ship-check in progress |
| Busy timeout → idle | May resume auto-build | Policy still false; no schedule started |
| `/session/idle` | May resume debounce build | Does not schedule in AI Controlled |
| `RunTests=OnFileChange` | Post-build tests after permitted file-triggered build | No file-triggered builds → preference never fires; not a watcher-side trigger |
| Startup / `StartOnLaunch` | Initial build on **cold** BuildMonitor/session start | Allowed (not a source-change schedule). Settings HardRestart remounts never compile, including AI Controlled + StartOnLaunch. |
| Manual Rebuild / Restart / Tests | Explicit | Allowed |
| `/run/rebuild`, `/run/ship-check`, `/run/tests`, `/run/stop` | Explicit | Allowed |

## Watch/run host behaviour

Desired run-host state (**Running** | **Stopped**) is separate from temporary operational pause:

| Action | Desired state | Process |
|--------|---------------|---------|
| Cold `StartOnLaunch` / `StartAsync` | → Running | Startup freshness build, then start when build succeeds |
| Explicit `/run/stop` | → Stopped | Host stopped; rebuild/ship-check/tests must not restore it |
| Explicit Run/Restart | → Running | Starts (or rebuilds then starts) once |
| `/watch/pause` or ship-check/rebuild pause | unchanged | Temporary unlock; resume only if desired is Running |
| Unexpected crash | unchanged (Running) | `RestartOnCrash` / `MaxRestartRetries` may restart |

- **Enter AI Controlled** while a `dotnet watch` process is running: stop it and start `dotnet run --no-build` (no compile). File watcher keeps observing.
- **Leave AI Controlled**: if project Run mode is Watch and coalescing is off, migrate back to `dotnet watch` with `--no-build` (no surprise compile of AI edits).
- Coalesced watch rebuilds (`CoalesceWatchRebuilds`) also stay off the watch process in AI Controlled; BuildMonitor’s watcher observes only.
- AI Controlled source observation never changes desired host state or starts a stopped host.

## Status presentation

No quiet/debounce countdown. Prefer:

- `CHANGES … detected` / `Awaiting agent` or `Awaiting explicit build`
- `BUILD` remains last-passed / agent rebuild when explicit work runs
