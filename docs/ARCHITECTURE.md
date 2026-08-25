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

**Association UX (Slice 3A):** Settings can Add from Azure, Attach/Change/Detach Azure on projects, and read local Git remotes for attach suggestions.

**Azure monitoring (Slice 3B):** Active-in-session projects with Azure + ≥1 pipeline + valid connection are polled continuously. One Builds list request per selected pipeline per cycle (`api-version=7.1`, `$top=25`, `queryOrder=queueTimeDescending`). **Display** picks any active run (all branches), else newest completed overall; **health** uses active-as-Activity else completed runs on relevant branches only (so PR failures do not permanently Red the tray). Facets merge into `ProjectHealthSnapshot.Azure` through `HealthCoalescer` / `ProjectHealthComposer` (single tray rollup). Current Git branch is presentation focus only. Auth/network → Amber; cancelled/NoRun → Neutral; zero pipelines → Connected / Not monitored (no HTTP). In-memory facets only (notifications deferred). Cadence ≈15s settled (was 45s — faster discovery of newly queued runs), ≈8s while Azure active, 15→45s failure backoff (auth/network only).

## Flow

1. User configures projects and marks active session projects in settings.
2. `ProjectOrchestrator` starts active projects that have a **Local** attachment (build, then run/watch). Azure-only active projects get health snapshots from Azure polling (no `ProjectRuntime`).
3. Optional loopback **control plane** (`http://127.0.0.1:{port}/`) lets agents signal busy/idle and run ship-check — see [ops/control-plane.md](ops/control-plane.md).
4. `DotNetCliRunner` captures stdout/stderr; `BuildLogStore` persists last logs.
5. `ProjectRuntime` updates health snapshots on state transitions; Azure facets refresh on the poll loop.
6. Tray icon and hover panel subscribe to `HealthUpdated` (Local + Azure sections on cards).
7. User opens `BuildLogViewerWindow` for full log + error navigation. Azure run rows open the Azure DevOps build results page when a run URL exists.

## Status panel (tray)

Borderless WPF window near the tray icon.

- **Left-click** tray icon: show or hide status panel (toggle)
- **Right-click** tray icon: context menu only (panel is hidden so it does not cover the menu)
- Panel auto-hides when the pointer leaves the panel (short delay)

## Process supervision

`SupervisedProcess` tracks long-running `dotnet run` / `dotnet watch` child processes. Exit triggers restart policy when enabled.
