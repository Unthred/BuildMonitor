# Local release deploy

Default folder for a **Release** publish on this machine:

`C:\Utils\BuildMonitor`

## Deploy

From the repo root (after `dotnet build` / `dotnet test` if you want a quick check):

```powershell
.\scripts\Deploy-BuildMonitor.ps1
```

The script:

1. Calls `POST http://127.0.0.1:{port}/app/quit` when the control plane is up (graceful tray exit — same as tray **Exit**)
2. Waits until the control plane port is closed so deploy files unlock
3. Runs `dotnet publish` (Release) to `artifacts\publish\Release`
4. Mirrors files into the deploy folder and writes `deploy-info.txt`

If quit is unavailable (build without `/app/quit`, or tray already stopped), exit BuildMonitor from the tray menu once, then re-run deploy. The script does **not** kill the process.

Override the folder:

```powershell
.\scripts\Deploy-BuildMonitor.ps1 -DeployPath D:\Tools\BuildMonitor
# or
$env:BUILDMONITOR_DEPLOY_PATH = 'D:\Tools\BuildMonitor'
.\scripts\Deploy-BuildMonitor.ps1
```

## Run deployed build

```powershell
C:\Utils\BuildMonitor\BuildMonitor.TrayApp.exe
```

User settings remain under `%LocalAPPDATA%\BuildMonitor\` — deploy does not touch them.
