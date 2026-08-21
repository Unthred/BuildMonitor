# Architecture

## Projects

- `TrayApp` — WPF shell, tray icon, hover panel, settings, log viewer
- `Core` — models, validation, local tray rollup rules, settings schema (v21 attachments)
- `Infrastructure` — dotnet CLI runner, process supervisor, build logs, project orchestrator
- `Infrastructure/AzureDevOps` — connection test + discovery client (`AzureDevOpsDiscoveryClient`, REST API **7.1**); parked legacy polling (`AzureDevOpsMonitorClient` / `MonitoringCoordinator`) remains **unwired**
- `Infrastructure/Security` — `AzureConnectionSecretStore` (DPAPI PAT files under `%LocalAppData%/BuildMonitor/secrets/`)

## Logical project model (schema v21)

A BuildMonitor **project** is a logical software product with optional **Local** and/or **Azure DevOps** attachments (at least one required). Azure DevOps **connections** (org URL) are top-level; PATs are not in `settings.json`. Azure association is repository-centric with 0..N pipelines.

**Connection + discovery (Slice 2):** Settings can store an Azure organisation connection and test a PAT. Discovery APIs list projects, repositories, and candidate pipelines for a repository. **Continuous polling, Azure status rows, notifications, and Add/Attach wizards are not enabled yet.**

## Flow

1. User configures projects and marks active session projects in settings.
2. `ProjectOrchestrator` starts active projects that have a **Local** attachment (build, then run/watch). Azure-only projects are ignored by local orchestration until later slices.
3. Optional loopback **control plane** (`http://127.0.0.1:{port}/`) lets agents signal busy/idle and run ship-check — see [ops/control-plane.md](ops/control-plane.md).
4. `DotNetCliRunner` captures stdout/stderr; `BuildLogStore` persists last logs.
5. `ProjectRuntime` updates health snapshots on state transitions.
6. Tray icon and hover panel subscribe to `HealthUpdated`.
7. User opens `BuildLogViewerWindow` for full log + error navigation.

## Status panel (tray)

Borderless WPF window near the tray icon.

- **Left-click** tray icon: show or hide status panel (toggle)
- **Right-click** tray icon: context menu only (panel is hidden so it does not cover the menu)
- Panel auto-hides when the pointer leaves the panel (short delay)

## Process supervision

`SupervisedProcess` tracks long-running `dotnet run` / `dotnet watch` child processes. Exit triggers restart policy when enabled.
