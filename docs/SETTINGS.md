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
      "testProjectFile": "",
      "extraDotNetArgs": "",
      "isActiveInSession": true,
      "runOptions": {
        "runMode": "Watch",
        "restartOnCrash": true,
        "maxRestartRetries": 5,
        "autoRestartOnWatchChanges": true,
        "restartAppAfterRebuild": true,
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
- `OnBuildSuccess` — run `dotnet test` automatically after a successful build
- `OnFileChange` (planned; debounced rebuild path)

**Tray menu → Run tests** runs tests on demand and opens the log viewer on the **Test** tab with live output while tests run. Completed output is saved to `last-test.log`.

When run/watch is active and the last build succeeded, **Run tests** keeps the site up: tests run with `dotnet test --no-build` against existing binaries (no app exe copy, no stop/restart).

If test assemblies are missing or stale, Build Monitor stops run/watch briefly, rebuilds, runs tests, then restarts watch (with `--no-build`). The same brief stop happens when the last build failed and a full test build is required.

`TestResults` and similar output are ignored by file watchers during and after test runs so they do not trigger spurious rebuilds.

**Project file** is used for build/run/watch (usually the app `.csproj`). **Test project / solution** (optional) targets `dotnet test` — leave blank to auto-detect a `.sln`/`.slnx` in the repo root or `*Tests.csproj` files. Running tests against the app `.csproj` only restores packages and does not execute tests.

Output uses `--verbosity normal` and a detailed console logger (per-test pass/fail lines plus a summary in the finish banner).

**Stop processes locking build output** applies before builds and when a full test rebuild is needed (or on lock-error retry). It is not used for the normal `--no-build` test path while the site stays up. Enable it when the app is started outside Build Monitor and locks `bin` output during rebuilds.

## File changes

- `Off`
- `TriggerRebuild` — debounced `dotnet build`
- `WatchOnly` — rely on `dotnet watch`

## App restart

- **Restart on crash** — retry run/watch after a non-zero exit (up to max retries).
- **Auto-restart on file changes (watch mode)** — `dotnet watch --non-interactive` when enabled; turn off to restart manually from **Restart app** in the tray or status panel.
- **Restart app after rebuild** — when run/watch was active, start it again after a successful rebuild.
- **Restart app** (tray menu / status panel) — stop and start run/watch without a full rebuild when the last build succeeded; rebuilds first if the last build failed.
