# Architecture

BuildMonitor as **shipped** (see [PROJECT_STATUS.md](../PROJECT_STATUS.md)). Prefer this document over issue chronology.

## Projects

- `TrayApp` — WPF shell, tray icon, hover panel, settings, log viewer
- `Core` — models, validation, local tray rollup rules, settings schema (v21 attachments), activity / failure / outcome rules
- `Infrastructure` — dotnet CLI runner, process supervisor, build logs, project orchestrator, control plane, Azure polling
- `Infrastructure/AzureDevOps` — connection test + discovery + association (`AzureAssociationCoordinator`) + **continuous polling** (`AzureMonitoringService` / `AzureBuildPollClient`) + timeline client for active primary runs
- `Infrastructure/Git` — `LocalGitContextReader` (+ short TTL cache for poll loops)
- `Infrastructure/Security` — `AzureConnectionSecretStore` (DPAPI PAT files under `%LocalAppData%/BuildMonitor/secrets/`)

## Major boundaries

```text
Local runtime (ProjectRuntime)
  ↓
Project health / activity snapshot
  ↓
Status UI / tray / control plane GET /projects

Azure polling (Builds + timeline while primary run active)
  ↓
Azure facet
  ↓
health + activity

Control-plane operation lease
  ↓
rebuild / tests / ship-check execution
  ↓
terminal typed outcome

Operational history
  ← observes events only (never control flow)
```

| Concern | Authority |
|---------|-----------|
| What is happening now? | Activity (#112) |
| Why unhealthy now? | Failure details (#111) |
| What happened? | Operational history (#110+) |
| How did `/run/*` finish? | Typed `outcome` |
| Was HTTP accepted? | Status code (distinct from outcome) |
| Should run host be up? | `DesiredRunHostState` (#106) |
| Cancel / operation identity | `ControlPlaneOperationLease` (#140) |

**History is observability, never control flow.**

## Logical project model (schema v21)

A BuildMonitor **project** is a logical software product with optional **Local** and/or **Azure DevOps** attachments (at least one required). Azure DevOps **connections** (org URL) are top-level; PATs are not in `settings.json`. Azure association is repository-centric with 0..N pipelines.

**Association UX:** Settings can Add from Azure, Attach/Change/Detach Azure on projects, and read local Git remotes for attach suggestions.

**Azure monitoring:** Active-in-session projects with Azure + ≥1 pipeline + valid connection are polled continuously. One Builds list request per selected pipeline per cycle (`api-version=7.1`, `$top=25`, `queryOrder=queueTimeDescending`). **Pipeline current state** (display + health): any active run (all branches), else newest completed overall — PR failures are real Red. Branch relevance is presentation focus / watched-branch attention only; it must not hide a newer PR failure behind an older default-branch success. Newest-run selection also prevents ancient feature failures from permanent Red. While the **PrimaryRun** is active, the same poll cycle may also `GET …/builds/{id}/timeline` once (shared `AzureBuildTimelineClient` / parser with lazy failure navigation) to project stage/job into `ProjectAzureHealthFacet.ExecutionDetail` → #112 Azure activity (`AzureRunExecutionProjector`). Settled runs do not fetch timeline. Facets merge into `ProjectHealthSnapshot.Azure` through `HealthCoalescer` / `ProjectHealthComposer` (Azure failure overrides Local green). Auth/network → Amber; cancelled/NoRun → Neutral; zero pipelines → Connected / Not monitored (no HTTP). In-memory facets only (OS notifications deferred). Cadence ≈15s settled, ≈8s while Azure active, 15→45s failure backoff (auth/network only).

## Flow

1. User configures projects and marks active session projects in settings.
2. `ProjectOrchestrator` starts active projects that have a **Local** attachment (build, then run/watch). Azure-only active projects get health snapshots from Azure polling (no `ProjectRuntime`).
3. Optional loopback **control plane** (`http://127.0.0.1:{port}/`) lets agents signal busy/idle, switch File Watching / AI Controlled, run rebuild / tests / ship-check, and cancel in-flight exclusive ops — see [ops/control-plane.md](ops/control-plane.md).
4. Each exclusive `/run/rebuild|tests|ship-check` holds a **lease** (`operationId` + cancel CTS). Terminal finalization retires cancellability first, then releases exclusivity after resume / history / completion cleanup.
5. `DotNetCliRunner` captures stdout/stderr; `BuildLogStore` persists last logs.
6. `ProjectRuntime` updates health snapshots on state transitions; Azure facets refresh on the poll loop.
7. Tray icon and hover panel subscribe to `HealthUpdated` (shared BUILDS table for Local + Azure sources, plus DETAIL for runtime).
8. User opens `BuildLogViewerWindow` for full log + error navigation. Azure run rows open the Azure DevOps build results page when a run URL exists.
9. **Operational history** (`OperationalHistoryStore`) is a bounded JSONL event stream for “what happened?” — separate from raw logs and from build-trigger / control-plane journals. Emitters observe Local lifecycle, explicit agent actions, and Azure/composite transitions; they do not drive execution. Timeline UI: status-panel **Recent activity** + Diagnostics **Operational history**. See [features/operational-history.md](features/operational-history.md).
10. **Activity / progress** (`ProjectActivityBuilder`, #112) is the ephemeral “what is it doing right now?” model derived from the same Local/Azure/control-plane snapshots — not history and not health. Agent activity may carry lease `operationId` and a Cancelling phase during `#140` cancel. See [features/activity-and-progress.md](features/activity-and-progress.md).
11. **Failure details** (`ProjectFailureDetailsBuilder`, #111) answers “why unhealthy now?” from current Local build/test, RunHost crash (`DesiredRunHostState` + #106), and Azure facet state (history enrichment by id only). Intentional agent cancel is not failure-details Red. See [features/failure-details.md](features/failure-details.md).

## Status panel (tray)

Borderless WPF window near the tray icon.

- **Left-click** tray icon: show or hide status panel (toggle)
- **Right-click** tray icon: context menu only (panel is hidden so it does not cover the menu)
- Panel auto-hides when the pointer leaves the panel (short delay)

## Process supervision

`SupervisedProcess` tracks long-running `dotnet run` / `dotnet watch` child processes. Exit triggers restart policy when enabled. Control-plane `/run/stop` sets desired host state **Stopped**; temporary pause for rebuild/ship-check does not permanently stop a Running host (resume after operation per #106). `/run/cancel` cancels exclusive agent build/test work only — it does not mean stop.
