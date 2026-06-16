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
        "fileChanges": "WatchOnly",
        "watchExcludeSegments": ".cursor;agent-transcripts;terminals;mcps;.idea;.vscode"
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
- **Monitor** — concurrency, debounce, **batch watch-mode rebuilds**, health refresh, auto-open log on failure, max log bytes.
- **App** — theme (`System`, `Light`, `Dark`) and startup behavior. **Run when Windows starts** adds/removes an entry under `HKCU\...\Run` named `LocalBuildMonitor`.

## Health colors

- **Green (Success)** — build/run healthy; no errors in the active context.
- **Amber (Warnings)** — build succeeded but the log contains warnings (when run has no errors).
- **Red (Failed)** — build, test, or **run** failed; run-time errors use the Run log counts when the app has crashed.

See [features/health-and-logs.md](features/health-and-logs.md) for how build vs run counts are chosen.

## Run mode

- `None` — build only
- `Run` — `dotnet run`
- `Watch` — `dotnet run` with debounced rebuilds when **Batch watch-mode rebuilds** is on (default), or `dotnet watch run` when that option is off

## Monitor — file change batching

- **`fileChangeDebounceMs`** (default **3000**) — quiet period after the last detected save before a coalesced rebuild starts. Increase (e.g. **5000–8000**) when an AI agent edits many files over several seconds.
- **`coalesceWatchRebuilds`** (default **true**) — in **Watch** run mode, BuildMonitor watches the project folder, waits for edits to settle, then runs one `dotnet build` and restarts the app. This replaces per-save `dotnet watch` rebuilds during agent sessions. Turn off to use `dotnet watch` hot reload instead (more rebuilds, faster feedback on single-file edits).

Restart the project from the tray after changing this option so the run process switches between `dotnet run` and `dotnet watch`.

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
- `WatchOnly` — with coalesced watch (default), BuildMonitor’s debounced watcher drives rebuilds; with coalescing off, rely on `dotnet watch`

## Build diagnostics

Tray → **Build diagnostics…** shows a log of what started each build or `dotnet watch` compile.

| Column | Meaning |
|--------|---------|
| **Kind** | Session start, file watcher, manual rebuild, hot reload, `dotnet watch`, etc. |
| **Files** | Paths that triggered a debounced file-watcher rebuild (relative to project root) |
| **Detail** | Extra context (e.g. a `dotnet watch` output line) |
| **Verdict** | Mark **Expected** or **Unexpected** to track spurious rebuilds |

Persisted at `%LOCALAPPDATA%/BuildMonitor/diagnostics/build-triggers.jsonl` (last 500 entries).

**Likely cause** is a heuristic from trigger kind and changed file paths (e.g. Cursor/agent tooling folders vs source edits). **Your note** is free text — use it to record what you were doing (e.g. “Cursor ask mode chat”) when marking unexpected rebuilds.

Window size and position are saved in `%LOCALAPPDATA%/BuildMonitor/windows-layout.json` (Settings, build log, diagnostics, and status panel size).

## Watch / file-watcher excludes

- **`watchExcludeSegments`** — semicolon-separated folder names ignored by BuildMonitor’s debounced file watcher (`TriggerRebuild` mode). Defaults include `.cursor`, `agent-transcripts`, `logs`, `bin`, `obj`, and similar tooling/output folders.
- Noisy file types (`.log`, `.dll`, `.pdb`, `.tmp`, etc.) are also ignored so build output and tooling writes are less likely to trigger rebuilds.
- For **dotnet watch**, also add `<Watch Remove="**/.cursor/**" />` (and similar) to the monitored `.csproj`. Defaults and behaviour: [features/health-and-logs.md](features/health-and-logs.md).

## App restart

- **Restart on crash** — retry run/watch after a non-zero exit (up to max retries).
- **Auto-restart on file changes (watch mode)** — `dotnet watch --non-interactive` when enabled; turn off to restart manually from the tray or status panel.
- **Auto-restart when output requires it** (default on) — scans build and run logs for hot-reload messages such as `requires restarting the application`, `unable to apply hot reload`, or `requires a rebuild`, then runs **Restart app** or **Rebuild & restart** automatically. Skips rude-edit lines when `dotnet watch` non-interactive auto-restart is already enabled.
- **Restart app after rebuild** — when run/watch was active, start it again after a successful rebuild.
- **Restart app** — stop and start run/watch with `--no-build` (no full rebuild).
- **Rebuild & restart** — full `dotnet build`, then start run/watch (shows build progress in status panel).
