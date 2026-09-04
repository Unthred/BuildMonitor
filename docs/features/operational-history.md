# Operational history

Typed, bounded **operational history** records meaningful BuildMonitor observations so the tray/status UI can answer “what happened?” without mining raw logs.

Parent epic: [#110](https://github.com/Unthred/BuildMonitor/issues/110).  
Infrastructure slice: [#113](https://github.com/Unthred/BuildMonitor/issues/113) (`#110a`).  
Local/action emitters: [#114](https://github.com/Unthred/BuildMonitor/issues/114) (`#110b`).  
Azure / composite-health emitters: [#115](https://github.com/Unthred/BuildMonitor/issues/115) (`#110c`).

## Purpose

| Concern | Mechanism |
|---------|-----------|
| **What is happening now?** | Live runtime / Azure snapshots (#112 activity model — separate) |
| **What happened?** | `OperationalEvent` stream (#110) |
| **Raw compiler/test output** | `last-build.log` / `last-test.log` / log viewer ([LOGS.md](../LOGS.md)) |
| **Build *trigger* diagnostics** | `BuildTriggerJournal` (`diagnostics/build-triggers.jsonl`) |
| **Control-plane session/actions** | `ControlPlaneEventJournal` (`diagnostics/control-plane-events.jsonl`) |

Operational history is **observability only**. Recording or failing to persist events must never gate build, test, run, or policy decisions.

### Best-effort recording invariant (#114)

Every emitter calls `OperationalHistoryRecorder.TryRecord` / `IOperationalHistoryStore.TryRecord` in a way that:

- never throws into build/test/run/action paths;
- continues primary work when `TryRecord` returns `false`;
- leaves existing persistence warning diagnostics as the durability signal.

Do not wrap primary runtime behaviour around successful history persistence.

### `TryRecord` contract

`TryRecord` returns **`true` when the event was accepted into current-session history** (visible via `GetRecent` / `GetRecentForProject` in this process).

Disk persistence is **best-effort**:

- a disk write failure must not throw and must not fail runtime work;
- the event may remain available in memory for the current process;
- that event may be **absent after restart** if persistence failed;
- warning/diagnostic callbacks are the signal that durability was not achieved.

Do not treat disk as the authoritative acceptance criteria for `TryRecord`.

Existing journals are **not** replaced or migrated by #113/#114; V1 dual-observability is intentional.

## Store lifetime / wiring (#114)

| Item | Choice |
|------|--------|
| Ownership | Single app-level instance on `ProjectOrchestrator` (same lifetime as `BuildTriggerJournal`) |
| Construction | `OperationalHistoryStore` under `%LocalAppData%/BuildMonitor/` (or orchestrator `appDataDirectory`) |
| Init failure | Caught at orchestrator construction — store is `null`; tray still starts |
| Consumers | Passed into each `ProjectRuntime` as `IOperationalHistoryStore?` |
| Helper | Per-runtime `OperationalHistoryEmitter` + static `OperationalHistoryRecorder` |

## Correlation: `OperationId`

`OperationId` is a per-project work-unit id (`Guid` “N” format). It correlates explicit actions with the build/test/host lifecycle that follows.

| Work unit | Who creates `OperationId` | Ownership |
|-----------|---------------------------|-----------|
| Tray rebuild / tests / restart | `ProjectOrchestrator` via `TryBeginHistoryOperation` | Caller-owned (cleared with matching id) |
| `/run/rebuild`, `/run/tests`, `/run/ship-check`, `/run/stop` | `ProjectRuntime` control-plane methods | Caller-owned (begun after prior build idle where applicable) |
| File-triggered auto-build | Runtime when the build is actually scheduled/started | Runtime-owned |
| Ambient startup build | Runtime if no active operation | Runtime-owned (no explicit action) |

**Rules:**

- Reuse `BuildTriggerRecord.Id` / local build number alongside `OperationId` — do not replace them.
- Prefer explicit propagation through the runtime’s scoped emitter field. Build/test gates serialize per project, so one active id is safe; overlapping work must not invent a global mutable cross-project id.
- **Overwrite safety:** `TryBeginHistoryOperation` / `TryBeginCallerOwnedOperation` refuses to replace an active slot (returns `false`, keeps the existing id). `EndHistoryOperation(operationId)` only clears when the id still matches, so a rejected overlap’s `finally` cannot end another unit. Control-plane rebuild/ship-check begin correlation only after waiting for any in-flight build to finish.
- Explicit `/run/tests` gets its own operation. Tests caused by rebuild/ship-check inherit the parent operation.
- Semantic dedupe belongs at authoritative emitter points (not in the store).

## V1 Local / action events (#114)

### Explicit actions (`Kind = ExplicitAction`)

| ActionName | Source | When |
|------------|--------|------|
| `rebuild` | User / Agent | Tray rebuild or `/run/rebuild` accepted |
| `tests` | User / Agent | Tray tests or `/run/tests` accepted |
| `ship-check` | Agent | `/run/ship-check` accepted (+ completion outcome) |
| `run-start` / `run-restart` | User | Tray start/restart accepted |
| `run-stop` | Agent | `/run/stop` accepted |
| `file-triggered-build` | System | File-triggered build scheduled/started |

### Workflow mode (`Kind = WorkflowMode`)

Recorded on authoritative `SetBuildControlMode` transitions (control-plane path → `Source = Agent`).

### Build lifecycle (`Kind = Build`, `Source = Local`)

Started / Succeeded / Failed / Cancelled (when the runtime distinguishes cancellation). Includes `OperationId`, `BuildTriggerId`, local build number, exit code / short error preview / log kind when already known. No extra raw-log parsing for history. No progress-step spam.

### Test lifecycle (`Kind = Tests`, `Source = Local`)

Started / Succeeded / Failed. Failed count and up to **5** failing test names when already parseable from the test log. Cancelled only if the runtime adds a true cancel path later.

### Run-host lifecycle (`Kind = RunHost`, `Source = Local`)

| Edge | Notes |
|------|--------|
| Host started | Normal start (not during intentional restart suppression) |
| Host stopped | Explicit `/run/stop` when a host was running |
| Host restarted | One completion event for intentional restart (stop/start pair suppressed) |
| Host crashed | Crash / fatal startup; recovery start is a separate Host started |

Desired-run-host semantics (#106) are unchanged.

### WaitingForEdits (`Kind = WaitingForEdits`, `Source = System`)

Enter (and leave when state exits WaitingForEdits). Not every debounce tick or filesystem event.

### Noise rules

Do **not** record:

- every file-system event / debounce reset / quiet-window tick;
- AI Controlled observe-only edits as “work started” (no auto build/test lifecycle);
- progress-step or watch-compile chatter as operational history;
- coincidental Stop+Start as `Restarted` — only authoritative intentional restart.

## V1 Azure / composite-health events (#115)

Observers hang off the **tray publish path** (`ProjectOrchestrator.PublishHealthFromCoalescer`), not a second Azure poller or second health evaluator. Input is the same coalesced `ProjectHealthSnapshot` list already used for UI.

### Wiring

| Item | Choice |
|------|--------|
| Observer | `AzureHealthHistoryObserver` (app-level, owned by `ProjectOrchestrator`) |
| Authority | Published snapshot `Azure` facet + `Health` rollup only |
| Dedupe | In-memory per project; **resets on process restart** |
| History as state | **Never** — JSONL is not re-read to reconstruct current Azure/health |

### Azure events (`Source = Azure`, `Kind = AzureRun`)

Correlation key: **Azure `RunId`** (plus build number / branch when already on the primary run). Do **not** invent Local `OperationId` links.

| Edge | When | Outcome | Detail.ActionName |
|------|------|---------|-------------------|
| Current run established / replaced | `PrimaryRun.RunId` changes (including first discovery after startup) | `Started` / `Succeeded` / `Failed` / `Cancelled` / `Changed` from run state+result | `azure-run-current` |
| Same-run transition | Same `RunId`, but `State` and/or `Result` changes | Same mapping; one combined event if both change in one poll | `azure-run-transition` |

`PreviousValue` / `NewValue` use `State/Result` strings (e.g. `InProgress/Unknown` → `Completed/Failed`).

### Azure poll dedupe / noise rules

Do **not** emit when:

- unchanged poll (same `RunId` + same `State` + same `Result`);
- `Availability` is `AuthRequired` or `Unavailable` (keep prior Azure dedupe; do not invent a run-state change from a missing `PrimaryRun`);
- facet is `NotMonitored` with no primary run (clear Azure dedupe silently);
- project drops out of the published set (inactive / removed) — clear dedupe silently.

Transport / PAT failures are **not** converted into fake Azure run results.

### Composite health (`Source = System`, `Kind = HealthTransition`, `Outcome = Changed`)

Emit only when **effective** published `snapshot.Health` changes for an active project.

| Field | Content |
|-------|---------|
| PreviousValue / NewValue | `MonitorHealth` names (`Green`, `Amber`, …) |
| Summary | `Health {prev} → {next}` |
| Detail.ActionName | `composite-health` |

No speculative failure “reason” (#111). Local/Azure activity must not alter health semantics merely because history is observing — the observer never re-composes health.

### Startup / baseline (#115)

| Stream | Behaviour |
|--------|-----------|
| **Azure** | First observed current `RunId` **emits** one `azure-run-current` (useful after restart). |
| **Health** | First observed effective health is a **silent baseline**; only later changes emit `HealthTransition` (avoids Unknown/Green spam every tray restart). |

### Invariant: no second health evaluator

History consumes `ProjectHealthComposer` / `HealthCoalescer` output already published to the tray. Do not add parallel Azure CI evaluation for #115.

## Distinction from existing journals

| Journal | Question it answers |
|---------|---------------------|
| Operational history | What meaningful Local/Azure/health/action edges happened (timeline-ready) |
| BuildTriggerJournal | Why a build was *triggered* (paths, inference, verdicts) |
| ControlPlaneEventJournal | Control-plane session/metrics telemetry |

## Model / persistence

Immutable `OperationalEvent` (schema version **1**) — see #113. Path: `%LocalAppData%/BuildMonitor/diagnostics/operational-history.jsonl`. Retention: **3 days** + **250/project**.

## Later slices

- Timeline UI → [#116](https://github.com/Unthred/BuildMonitor/issues/116)

## API

`IOperationalHistoryStore`:

- `TryRecord(OperationalEvent)` → `bool`
- `GetRecent(limit?)` → newest-first
- `GetRecentForProject(projectId, limit?)` → newest-first
