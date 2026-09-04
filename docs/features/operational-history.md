# Operational history

Typed, bounded **operational history** records meaningful BuildMonitor observations so the tray/status UI can answer “what happened?” without mining raw logs.

Parent epic: [#110](https://github.com/Unthred/BuildMonitor/issues/110).  
Infrastructure slice: [#113](https://github.com/Unthred/BuildMonitor/issues/113) (`#110a`).

## Purpose

| Concern | Mechanism |
|---------|-----------|
| **What is happening now?** | Live runtime / Azure snapshots (#112 activity model — separate) |
| **What happened?** | `OperationalEvent` stream (#110) |
| **Raw compiler/test output** | `last-build.log` / `last-test.log` / log viewer ([LOGS.md](../LOGS.md)) |
| **Build *trigger* diagnostics** | `BuildTriggerJournal` (`diagnostics/build-triggers.jsonl`) |
| **Control-plane session/actions** | `ControlPlaneEventJournal` (`diagnostics/control-plane-events.jsonl`) |

Operational history is **observability only**. Recording or failing to persist events must never gate build, test, run, or policy decisions.

### `TryRecord` contract

`TryRecord` returns **`true` when the event was accepted into current-session history** (visible via `GetRecent` / `GetRecentForProject` in this process).

Disk persistence is **best-effort**:

- a disk write failure must not throw and must not fail runtime work;
- the event may remain available in memory for the current process;
- that event may be **absent after restart** if persistence failed;
- warning/diagnostic callbacks are the signal that durability was not achieved.

Do not treat disk as the authoritative acceptance criteria for `TryRecord`.

Existing journals are **not** replaced or migrated by #113; later slices may dual-write or view-compose.

## Model

Immutable `OperationalEvent` (schema version **1**):

- Identity: `Id`, `ProjectId`, `OccurredAtUtc`
- Semantics: `Source`, `Kind`, `Outcome`, `Summary`
- Optional correlation: `OperationId`, `BuildTriggerId`, `LocalBuildNumber`, `AzureRunId`, `AzureBuildNumber`, `Branch`
- Optional transitions: `PreviousValue`, `NewValue`
- Optional sparse `Detail` (`ExitCode`, `ErrorPreview`, `LogKind`, failing-test hints capped at **5** names, Azure stage, hold reason, action name). Typed fields only — no dictionary/blob schema.

Enums (`OperationalEventSource` / `Kind` / `Outcome`) are authoritative; UI display strings are derived later.

## Persistence

| Item | Value |
|------|--------|
| Directory | `%LocalAppData%/BuildMonitor/diagnostics/` |
| File | `operational-history.jsonl` |
| Format | One JSON object per line (camelCase), `schemaVersion: 1` |
| Quarantine | Truncated/malformed **trailing** line may be copied to `operational-history.corrupt-tail.txt` |

### Retention (V1 constants)

- Age: **3 days** (`OperationalHistoryStore.DefaultMaxAgeDays`)
- Count: **250 events per project** (`DefaultMaxEventsPerProject`)

Both bounds apply. Compaction rewrites the JSONL oldest→newest without duplicating ids.

### Load / crash tolerance

- Valid preceding lines are restored on startup.
- A truncated or malformed **final** line is skipped and quarantined; history is not discarded.
- A malformed line in the **middle** of the file is skipped; neighbouring valid records are kept; the file is rewritten after load so the bad line is not re-read forever.
- Unknown/other `schemaVersion` lines are skipped (V1 does not migrate older schemas).

### Memory and concurrency

- In-memory newest-first ring for fast queries (`GetRecent` / `GetRecentForProject`).
- `TryRecord`: accept into memory first (session-authoritative), then best-effort append (or rewrite after retention). Compaction uses `File.WriteAllLines` like the existing diagnostics journals.
- Duplicate `Id` → reject (`false`), including ids restored from disk then re-appended.
- Default/`OccurredAtUtc == default` timestamps and blank ids/project/summary are rejected.
- Unknown future `schemaVersion` lines are skipped on load (not reinterpreted as V1).
- Persistence I/O failures are swallowed and optionally reported via a warning callback — callers must not treat history as critical path.
- A single lock serializes append/query/compaction.

## Emitters and UI

Not in #113:

- Runtime/Azure emitters → #114 / #115  
- Timeline UI → #116  

Semantic transition detection (coalescing) belongs to emitters, not the store.

## API

`IOperationalHistoryStore`:

- `TryRecord(OperationalEvent)` → `bool`
- `GetRecent(limit?)` → newest-first
- `GetRecentForProject(projectId, limit?)` → newest-first
