# BuildMonitor

Windows tray app for personal .NET development: authoritative local build / test / run visibility, governed agent control over watched repos, and Azure DevOps CI state in the same status surface.

**Project status:** [Feature-complete / maintenance mode](PROJECT_STATUS.md)

Repo: [github.com/Unthred/BuildMonitor](https://github.com/Unthred/BuildMonitor)

## What it does

- Monitors one or more configured .NET projects from the system tray (traffic-light health)
- Runs local `dotnet build` / `test` / `run` / `watch` with last-log capture
- **File Watching** vs **AI Controlled** modes so agents can edit without surprise auto-builds
- Loopback **control plane** (`http://127.0.0.1:7700/`) for busy/idle, rebuild, tests, ship-check, and explicit cancel
- Optional **Azure DevOps** monitoring (primary run, tray + `/projects`, stage/job while a run is active)
- Status panel: activity (“what now?”), failure details (“why unhealthy?”), recent operational history (“what happened?”)

## Build and run

```powershell
dotnet build BuildMonitor.slnx
dotnet test src/BuildMonitor.Tests/BuildMonitor.Tests.csproj
dotnet watch run --project src/TrayApp/BuildMonitor.TrayApp.csproj
```

Or use `watch.ps1` from the repo root.

## Local release deploy

Default install folder: **`C:\Utils\BuildMonitor`**. See [docs/ops/local-deploy.md](docs/ops/local-deploy.md).

```powershell
.\scripts\Deploy-BuildMonitor.ps1
```

## Settings

User settings: `%LocalAppData%/BuildMonitor/settings.json` (not committed). See [docs/SETTINGS.md](docs/SETTINGS.md).

## Documentation

- [PROJECT_STATUS.md](PROJECT_STATUS.md) — completeness, contracts, maintenance policy
- [docs/README.md](docs/README.md) — full index
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — projects and flow
- [docs/ops/control-plane.md](docs/ops/control-plane.md) — agent HTTP API
- [docs/LOGS.md](docs/LOGS.md) — log storage
- [docs/ops/github-workflow.md](docs/ops/github-workflow.md) — Issues, Projects board, and PRs
