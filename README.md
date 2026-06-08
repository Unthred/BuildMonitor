# BuildMonitor

Windows tray app that monitors local .NET projects — build, run, watch, logs, and health at a glance.

Repo: [github.com/Unthred/BuildMonitor](https://github.com/Unthred/BuildMonitor)

## Build and run

```powershell
dotnet build BuildMonitor.slnx
dotnet test src/BuildMonitor.Tests/BuildMonitor.Tests.csproj
dotnet watch run --project src/TrayApp/BuildMonitor.TrayApp.csproj
```

Or use `watch.ps1` from the repo root.

## Settings

User settings: `%LocalAppData%/BuildMonitor/settings.json` (not committed). See [docs/SETTINGS.md](docs/SETTINGS.md).

## Documentation

- [docs/README.md](docs/README.md) — full index
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — projects and flow
- [docs/LOGS.md](docs/LOGS.md) — log storage
- [docs/ops/github-workflow.md](docs/ops/github-workflow.md) — Issues and PRs
