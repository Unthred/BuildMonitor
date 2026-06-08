# Local Build Control Tray

Windows 11 WPF tasktray app for monitoring and controlling local .NET projects you are actively working on.

## Capabilities (MVP)

- Configure multiple projects (folder, csproj/sln, launch profile, dotnet args)
- Select which projects are active in the current session
- Run `dotnet build`, `dotnet run`, or `dotnet watch` per active project
- Restart on crash (configurable retries)
- Optional tests after successful build
- Optional file-change triggered rebuild
- Traffic-light tray icon (green / amber / red) across active projects
- Hover status panel (stable show/hide, no flicker)
- Full last build/test log viewer with error list and jump-to-line
- Settings apply immediately without restart

## Traffic light

- **Green**: all active projects healthy
- **Amber**: building/testing/watching or partial degradation (e.g. tests failed)
- **Red**: build failed or unrecoverable crash

## Data locations

- Settings: `%LOCALAPPDATA%/BuildMonitor/settings.json`
- Logs: `%LOCALAPPDATA%/BuildMonitor/logs/{projectId}/last-build.log` (and test/run variants)

## Optional future module

Azure DevOps polling code remains under `src/Infrastructure/AzureDevOps/` but is not wired into the tray app startup.

## Run from repo root

`dotnet watch run` must target the **tray app project**, not the `.slnx` file:

```powershell
dotnet watch run --project src\TrayApp\BuildMonitor.TrayApp.csproj
```

Or use the convenience scripts:

```powershell
.\watch.ps1
.\run.ps1
```

## Phases

See Cursor plan `local_build_tray_pivot` for delivery history and backlog.
