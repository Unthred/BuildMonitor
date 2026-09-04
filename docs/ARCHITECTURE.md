# Architecture

## Projects

- `TrayApp` — WPF shell, tray icon, hover panel, settings, log viewer
- `Core` — models, validation, local tray rollup rules, settings schema (v21 attachments)
- `Infrastructure` — dotnet CLI runner, process supervisor, build logs, project orchestrator
- `Infrastructure/AzureDevOps` — connection test + discovery + association (`AzureAssociationCoordinator`) + **continuous polling** (`AzureMonitoringService` / `AzureBuildPollClient`)
- `Infrastructure/Git` — `LocalGitContextReader` (+ short TTL cache for poll loops)
- `Infrastructure/Security` — `AzureConnectionSecretStore` (DPAPI PAT files under `%LocalAppData%/BuildMonitor/secrets/`)

## Logical project model (schema v21)

A BuildMonitor **project** is a logical software product with optional **Local** and/or **Azure DevOps** attachments (at least one required). Azure DevOps **connections** (org URL) are top-level; PATs are not in `settings.json`. Azure association is repository-centric with 0..N pipelines.

**Association UX:** Settings can Add from Azure, Attach/Change/Detach Azure on projects, and read local Git remotes for attach suggestions.

**Azure monitoring:** Active-in-session projects with Azure + ≥1 pipeline + valid connection are polled continuously. One Builds list request per selected pipeline per cycle (`api-version=7.1`, `$top=25`, `queryOrder=queueTimeDescending`). **Pipeline current state** (display + health): any active run (all branches), else newest completed overall — PR failures are real Red. Branch relevance is presentation focus / watched-branch attention only; it must not hide a newer PR failure behind an older default-branch success. Newest-run selection also prevents ancient feature failures from permanent Red. Facets merge into `ProjectHealthSnapshot.Azure` through `HealthCoalescer` / `ProjectHealthComposer` (Azure failure overrides Local green). Auth/network → Amber; cancelled/NoRun → Neutral; zero pipelines → Connected / Not monitored (no HTTP). In-memory facets only (notifications deferred). Cadence ≈15s settled, ≈8s while Azure active, 15→45s failure backoff (auth/network only).

## Flow

1. User configures projects and marks active session projects in settings.
2. `ProjectOrchestrator` starts active projects that have a **Local** attachment (build, then run/watch). Azure-only active projects get health snapshots from Azure polling (no `ProjectRuntime`).
3. Optional loopback **control plane** (`http://127.0.0.1:{port}/`) lets agents signal busy/idle and run ship-check — see [ops/control-plane.md](ops/control-plane.md).
4. `DotNetCliRunner` captures stdout/stderr; `BuildLogStore` persists last logs.
5. `ProjectRuntime` updates health snapshots on state transitions; Azure facets refresh on the poll loop.
6. Tray icon and hover panel subscribe to `HealthUpdated` (shared BUILDS table for Local + Azure sources, plus DETAIL for runtime).
7. User opens `BuildLogViewerWindow` for full log + error navigation. Azure run rows open the Azure DevOps build results page when a run URL exists.
8. **Operational history** (`OperationalHistoryStore`, `#110` / `#113`–`#116`) is a bounded JSONL event stream for “what happened?” — separate from raw logs and from build-trigger / control-plane journals. Local lifecycle + explicit-action emitters are wired through `ProjectOrchestrator` → `ProjectRuntime` (`OperationalHistoryEmitter`). Azure run + composite-health transitions are observed on tray health publish (`AzureHealthHistoryObserver`). Timeline UI: status-panel **Recent activity** + Diagnostics **Operational history** (`OperationalHistoryPresentationMapper`). See [features/operational-history.md](features/operational-history.md).

## Status panel (tray)

Borderless WPF window near the tray icon.

- **Left-click** tray icon: show or hide status panel (toggle)
- **Right-click** tray icon: context menu only (panel is hidden so it does not cover the menu)
- Panel auto-hides when the pointer leaves the panel (short delay)

## Process supervision

`SupervisedProcess` tracks long-running `dotnet run` / `dotnet watch` child processes. Exit triggers restart policy when enabled.
