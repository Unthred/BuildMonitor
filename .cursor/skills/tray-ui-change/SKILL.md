---
name: tray-ui-change
description: >-
  WPF/XAML handoff for BuildMonitor tray app (build/watch commands, tray verification).
  Use after editing TrayApp UI or when user asks for UI handoff.
disable-model-invocation: true
---

# Tray UI change (BuildMonitor)

**Authority:** `no-unapproved-runtime-execution.mdc`, `ui-design.mdc`.

After editing `*.xaml`, `src/TrayApp/**`, or theme files:

1. **Stop** — list files changed; do not run build/watch.
2. **Commands for user:**

```powershell
dotnet build BuildMonitor.slnx
dotnet watch run --project src/TrayApp/BuildMonitor.TrayApp.csproj
```

3. **Verify** — after user confirms app is running, describe what to check:
   - Tray icon / traffic-light state
   - Left-click status panel (health, URL link, progress)
   - Settings window fields saved correctly
   - Build log viewer tabs and follow-output checkbox

4. **No browser URL** — BuildMonitor monitors other projects; only mention listen URLs for *monitored* projects from their launch profiles.

Pair with **feature-ship** before PR; with **feature-kickoff** before large UI features.
