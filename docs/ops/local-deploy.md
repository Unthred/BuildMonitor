# Local release deploy

Default folder for a **Release** publish on this machine:

`C:\Utils\BuildMonitor`

## Deploy

From the repo root (after `dotnet build` / `dotnet test` if you want a quick check):

```powershell
.\scripts\Deploy-BuildMonitor.ps1
```

Override the folder:

```powershell
.\scripts\Deploy-BuildMonitor.ps1 -DeployPath D:\Tools\BuildMonitor
# or
$env:BUILDMONITOR_DEPLOY_PATH = 'D:\Tools\BuildMonitor'
.\scripts\Deploy-BuildMonitor.ps1
```

The script runs `dotnet publish` (Release) to `artifacts\publish\Release`, mirrors files into the deploy folder, and writes `deploy-info.txt` with the deploy timestamp.

Close any running `BuildMonitor.TrayApp.exe` before deploying if files are locked.

## Run deployed build

```powershell
C:\Utils\BuildMonitor\BuildMonitor.TrayApp.exe
```

User settings remain under `%LocalAPPDATA%\BuildMonitor\` — deploy does not touch them.
