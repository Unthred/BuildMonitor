# Architecture

## Projects

- `TrayApp` — WPF shell, tray icon, hover panel, settings, log viewer
- `Core` — models, validation, local tray rollup rules
- `Infrastructure` — dotnet CLI runner, process supervisor, build logs, project orchestrator
- `Infrastructure/AzureDevOps` — optional parked module

## Flow

1. User configures projects and marks active session projects in settings.
2. `ProjectOrchestrator` starts active projects (build, then run/watch).
3. `DotNetCliRunner` captures stdout/stderr; `BuildLogStore` persists last logs.
4. `ProjectRuntime` updates health snapshots on state transitions.
5. Tray icon and hover panel subscribe to `HealthUpdated`.
6. User opens `BuildLogViewerWindow` for full log + error navigation.

## Status panel (tray)

Borderless WPF window near the tray icon.

- **Left-click** tray icon: show or hide status panel (toggle)
- **Right-click** tray icon: context menu only (panel is hidden so it does not cover the menu)
- Panel auto-hides when the pointer leaves the panel (short delay)

## Process supervision

`SupervisedProcess` tracks long-running `dotnet run` / `dotnet watch` child processes. Exit triggers restart policy when enabled.
