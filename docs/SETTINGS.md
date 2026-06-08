# Settings schema (v2)

File: `%LOCALAPPDATA%/BuildMonitor/settings.json`

```json
{
  "schemaVersion": 2,
  "projects": [
    {
      "id": "abc123",
      "displayName": "My App",
      "rootFolder": "C:\\src\\MyApp",
      "projectFile": "MyApp.csproj",
      "launchProfile": "https",
      "extraDotNetArgs": "",
      "isActiveInSession": true,
      "runOptions": {
        "runMode": "Watch",
        "restartOnCrash": true,
        "maxRestartRetries": 5,
        "runTests": "Off",
        "fileChanges": "WatchOnly"
      }
    }
  ],
  "monitor": {
    "healthRefreshSeconds": 5,
    "fileChangeDebounceMs": 1500,
    "maxConcurrentActiveProjects": 3,
    "autoOpenLogOnFailure": false,
    "maxLogDisplayBytes": 2097152
  },
  "appBehavior": {
    "runOnLogon": false,
    "startMinimizedToTray": true,
    "theme": "System"
  }
}
```

## Settings UI tabs

- **Projects** — per-project folder, csproj/sln, launch profile, run/watch options, and **active in session** checkbox.
- **Monitor** — concurrency, debounce, health refresh, auto-open log on failure, max log bytes.
- **App** — theme (`System`, `Light`, `Dark`) and startup behavior. **Run when Windows starts** adds/removes an entry under `HKCU\...\Run` named `LocalBuildMonitor`.

## Health colors

- **Green (Success)** — last build succeeded with no errors or warnings.
- **Amber (Warnings)** — build succeeded but the log contains warnings.
- **Red (Failed)** — build or tests failed, or errors were parsed from the log.

## Run mode

- `None` — build only
- `Run` — `dotnet run`
- `Watch` — `dotnet watch run`

## Run tests

- `Off`
- `OnBuildSuccess`
- `OnFileChange` (planned; debounced rebuild path)

## File changes

- `Off`
- `TriggerRebuild` — debounced `dotnet build`
- `WatchOnly` — rely on `dotnet watch`
