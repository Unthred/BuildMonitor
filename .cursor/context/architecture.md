# Architecture overview

Imperative rules: `.cursor/rules/architecture.mdc`. Universal defaults: `.cursor/rules/core.mdc`.

Human-facing detail: [`docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md).

## Stack

- WPF tray application on **.NET 10** (Windows)
- Child `dotnet build` / `dotnet watch` / `dotnet run` via `DotNetCliRunner` and `SupervisedProcess`
- Settings in `%LocalAppData%/BuildMonitor/settings.json`
- Build logs in `%LocalAppData%/BuildMonitor/logs/<projectId>/`

## Solution layout

| Project | Path | Responsibility |
|---------|------|----------------|
| **BuildMonitor.TrayApp** | `src/TrayApp/` | WPF UI, tray icon, settings window, log viewer, hover panel |
| **BuildMonitor.Core** | `src/Core/` | Models, settings, health/tray rollup rules |
| **BuildMonitor.Infrastructure** | `src/Infrastructure/` | Orchestrator, process config, log store, port probe |
| **BuildMonitor.Tests** | `src/BuildMonitor.Tests/` | Unit tests for Core + Infrastructure |

## Engineering rules

- **No god-classes:** `.cursor/rules/code-structure.mdc` — extract from `ProjectOrchestrator` / `App.xaml.cs`; line limits.
- **Orchestration tests:** `.cursor/rules/testing.mdc` Tier 2 — orchestrator/runtime changes require tests in the same PR.
- **CI:** `.github/workflows/ci.yml` — build + test on every PR to `main`.

## Goals

- Monitor multiple local dotnet projects from the system tray
- Isolate child processes from BuildMonitor's own `dotnet watch` host environment
- Persist last build/run/test logs for quick diagnosis
- Debounce file changes and avoid blocking child stdout
