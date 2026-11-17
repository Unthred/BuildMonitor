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
| Startup / `StartOnLaunch` | Initial build on **cold** BuildMonitor/session start | Only when the last build is not current (see [Startup build](#startup-build)). Settings HardRestart remounts never compile, including AI Controlled + StartOnLaunch. |
| Manual Rebuild / Restart / Tests | Explicit | Allowed |
| `/run/rebuild`, `/run/ship-check`, `/run/tests`, `/run/stop` | Explicit | Allowed |

## Watch/run host behaviour

Desired run-host state (**Running** | **Stopped**) is separate from temporary operational pause:

| Action | Desired state | Process |
|--------|---------------|---------|
| Cold `StartOnLaunch` / `StartAsync` | → Running | Start from the existing output when the last build is current; otherwise startup build, then start when it succeeds |
| Explicit `/run/stop` | → Stopped | Host stopped; rebuild/ship-check/tests must not restore it |
| Explicit Run/Restart | → Running | Starts (or rebuilds then starts) once |
| `/watch/pause` or ship-check/rebuild pause | unchanged | Temporary unlock; resume only if desired is Running |
| Unexpected crash | unchanged (Running) | `RestartOnCrash` / `MaxRestartRetries` may restart |

- **Enter AI Controlled** while a `dotnet watch` process is running: stop it and start `dotnet run --no-build` (no compile). File watcher keeps observing.
- **Leave AI Controlled**: if project Run mode is Watch and coalescing is off, migrate back to `dotnet watch` with `--no-build` (no surprise compile of AI edits).
- Coalesced watch rebuilds (`CoalesceWatchRebuilds`) also stay off the watch process in AI Controlled; BuildMonitor’s watcher observes only.
- AI Controlled source observation never changes desired host state or starts a stopped host.

## Startup build

Restarting the tray (deploy, Settings change, Windows logon) must not recompile every AI Controlled project when nothing changed.

### Behaviour
| Step | What | Where |
|------|------|-------|
| 1 | Restore the saved last build (exit code, start time, counts) | `ProjectRuntime.HydrateLastBuildFromStoreAsync` |
| 2 | File Watching projects always build (`--no-incremental`) | `StartupBuildPolicy.Decide` |
| 3 | AI Controlled: find the oldest `<ProjectName>.dll` under the startup project's `bin`, and the newest meaningful source file under `rootFolder` (same ignore rules as the file watcher; excluded folders are not walked) | `StartupBuildFreshnessProbe` |
| 4 | Skip the build when the last build succeeded, the output exists, and no source was written at or after the earlier of the build's start and the output's write time | `StartupBuildPolicy.Decide` |
| 5 | Skipped: host starts with `dotnet run --no-build` and the saved warning/error counts. Otherwise the normal startup build runs (deferred until edits settle when configured) | `ProjectRuntime.StartAsync` |

**Failure and fallback:** anything uncertain builds — no saved build, a failed build, missing output (including a custom assembly name or output path), or any newer source. Using the oldest assembly copy and the earlier timestamp means a Release ship-check cannot hide a stale Debug output.

## Status presentation

No quiet/debounce countdown. Prefer:

- `CHANGES … detected` / `Awaiting agent` or `Awaiting explicit build`
- `BUILD` remains last-passed / agent rebuild when explicit work runs
