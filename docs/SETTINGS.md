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
      "startOnLaunch": true,
      "runOptions": {
        "runMode": "Watch",
        "restartOnCrash": true,
        "maxRestartRetries": 5,
        "autoRestartOnWatchChanges": true,
        "restartAppAfterRebuild": true,
        "runTests": "Off",
        "fileChanges": "WatchOnly",
        "forceCompleteWarningCounts": true,
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

- **Projects** — per-project folder, csproj/sln, launch profile, run/watch options, **start build when app launches**, and **active in session** checkbox (left of each project name). Unchecked projects remain in the list but are not built or run until checked and settings are saved.
- **Monitor** — concurrency, debounce, **batch watch-mode rebuilds**, health refresh, **auto-open Build Monitor Health on startup**, max log bytes.
- **App** — theme (`System`, `Light`, `Dark`) and startup behavior. **Run when Windows starts** adds/removes an entry under `HKCU\...\Run` named `LocalBuildMonitor`.

Per project (**Projects** tab → **File watching**):

- **`autoOpenLog`**: `Never` (default), `Errors`, `Warnings`, or `Always` — when to open the log viewer automatically after a build or test. `Warnings` opens on amber health (build succeeded with warnings) as well as failures. `Always` opens after every build or test completes. Replaces the old global `monitor.autoOpenLogOnFailure` flag (schema v11).

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
- **`fileChangeDebounceMode`**: `Manual` (default) or `Auto`. **Auto** learns per project from save burst length (time from first to last file change before rebuild), using p90 × 1.25 smoothed into **1500–12000 ms**. The manual ms value is used until **five** bursts are recorded. Stats persist in `%LOCALAPPDATA%/BuildMonitor/debounce-stats.json`.
- **Agent session coalescing** — after the first file-triggered build in a 90-second window, further saves wait for a full quiet period since the **last** change (not a fixed 3 s post-build cooldown). Debounce increases up to **2×** when multiple file-triggered builds happen in that window. Turn on **Auto** debounce mode for longer agent sessions.
- **`coalesceWatchRebuilds`** (default **true**) — in **Watch** run mode, BuildMonitor watches the project folder, waits for edits to settle, then runs one `dotnet build` and restarts the app. This replaces per-save `dotnet watch` rebuilds during agent sessions. Turn off to use `dotnet watch` hot reload instead (more rebuilds, faster feedback on single-file edits).
- **`deferStartupBuildUntilQuiet`** (default **true**) — when starting Build Monitor while an agent is still saving, wait for the quiet period before the first `dotnet build`.
- **`cancelSupersededBuilds`** (default **true**) — cancel in-flight **startup** or **file-change** builds when newer saves arrive; coalesce into one rebuild after edits settle. Manual tray rebuilds are never cancelled.
- **`useAgentTranscriptActivity`** (default **true**) — treat writes under `agent-transcripts` / `.cursor` as “agent still active” for gating (signal only; does not trigger rebuilds).
- **`learnFromDiagnosticsVerdicts`** (default **true**) — when you mark a build trigger **Unexpected** in **Build diagnostics**, suggest watch-ignore folders and apply debounce feedback for file-change triggers. Learned excludes persist in `build-training.json`.

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

## Warning counts

- **`forceCompleteWarningCounts`** (per project, default **true**) — when enabled, every build (including file-change) passes `--no-incremental` so MSBuild re-emits the full warning/error summary. Turn off under **Settings → Projects** for faster file-change builds; those may show `0 Warning(s)` when nothing recompiled. **Startup**, **Rebuild**, and **Rebuild & restart** always force a full compile regardless of this setting.

## Build output repair

- **`autoRepairCorruptedOutput`** (default **true**) — when build output indicates a poisoned MSBuild tree (nested `artifacts\build\...\artifacts\build`, copy failures under phantom paths), BuildMonitor stops watch/run, deletes **`artifacts/`**, **`bin/`**, and **`obj/`** under the project root only, then retries the build once.
- **Tray → Clean build output** (operation menu) or per-project submenu (project-centric menu) runs the same cleanup manually and restarts watch/run if it was running.
- Avoid **`BaseOutputPath`** in **extra dotnet args** while **Watch** mode is enabled — BuildMonitor warns at watch start. Never combine custom output paths with an active watch (common cause of corrupted trees when external tools build the same repo).

## Tray menu layout

- **`appBehavior.trayMenuLayout`**: `ByOperation` (default) — Rebuild / Restart / … each with a project list; `ByProject` — one submenu per active project with all actions underneath. Toggle in **Settings → App → Tray menu layout**. Both layouts include **Clean build output**.

## Virtual desktops (Windows)

- **`appBehavior.followStatusPanelToVirtualDesktop`** (default **true**) — when the hover **status panel** opens, move it onto the virtual desktop you are currently viewing (foreground window / cursor).
- **`appBehavior.followBuildLogToVirtualDesktop`** (default **true**) — when the **build log** window opens or is activated, move it onto your current virtual desktop. Useful when auto-open log fires while you are on another desktop.

Toggle both under **Settings → App → Virtual desktops**.

## File changes

- `Off`
- `TriggerRebuild` — debounced `dotnet build`
- `WatchOnly` — with coalesced watch (default), BuildMonitor’s debounced watcher drives rebuilds; with coalescing off, rely on `dotnet watch`

## Build diagnostics

Tray → **Build diagnostics…** opens **one tab per active project**: a compact **rebuild timing** panel (metric tiles, save-burst and build-duration charts, rebuild countdown) and today's build triggers for that project.

| Column | Meaning |
|--------|---------|
| **Kind** | Session start, file watcher, manual rebuild, hot reload, `dotnet watch`, etc. |
| **Files** | Paths that triggered a debounced file-watcher rebuild (relative to project root) |
| **Detail** | Extra context — file-watcher debounce/hold timing, or a `dotnet watch` / hot-reload output line |
| **Verdict** | Mark **Expected** or **Unexpected** to track spurious rebuilds |

Persisted at `%LOCALAPPDATA%/BuildMonitor/diagnostics/build-triggers.jsonl` (**today's entries only**, local calendar day; up to 500 per day). Mark **Unexpected** triggers to spot spurious rebuilds during agent sessions.

**Learning over time:** with **File change debounce → Auto**, BuildMonitor records save-burst lengths per project in `debounce-stats.json` and raises the quiet period (1500–12000 ms) after five bursts. **Agent session coalescing** (in memory) backs off further when several file-triggered builds happen within 90 seconds.

**Train from diagnostics:** with **Monitor → Learn from Unexpected build diagnostics verdicts** (default on), marking a trigger **Unexpected** in **Build diagnostics** can:
- **Suggest folders to ignore** — when changed files point at tooling/docs paths (e.g. `docs/`, `.cursor/`), a prompt offers to add them to per-project learned excludes (persisted in `%LOCALAPPDATA%/BuildMonitor/build-training.json`, merged with settings excludes at runtime).
- **Raise debounce learning** — file-watcher / `dotnet watch` file-change triggers bump the learned quiet period in `debounce-stats.json` (+15%, min +250 ms, capped at 12000 ms).

Turn learning off to keep verdicts and notes for review only.

**Likely cause** is a heuristic from trigger kind and changed file paths (e.g. Cursor/agent tooling folders vs source edits). **Your note** is free text — use it to record what you were doing (e.g. “Cursor ask mode chat”) when marking unexpected rebuilds. Status panel **AI working?** appears in the **header** during a rebuild countdown (extends the wait) or while a build is running (marks that trigger **Unexpected**). The countdown auto-extends when Cursor/agent tooling is active (`useAgentTranscriptActivity`) and resets when meaningful source files (e.g. `.cs`) are saved.

Window size and position are saved in `%LOCALAPPDATA%/BuildMonitor/windows-layout.json` (Settings, build log, diagnostics — including trigger grid column widths — and status panel width — height auto-fits content up to 460 px).

## Watch / file-watcher excludes

- **`watchExcludeSegments`** — semicolon-separated folder names ignored by BuildMonitor’s debounced file watcher (`TriggerRebuild` mode). Defaults include `.cursor`, `agent-transcripts`, `docs`, `templates`, `.github`, `logs`, `bin`, `obj`, and similar tooling/output folders.
- Noisy file types (`.log`, `.dll`, `.pdb`, `.tmp`, `.md`, `.mdc`, common image formats under `wwwroot`, etc.) are also ignored so build output, documentation, and static assets are less likely to trigger rebuilds.
- **`wwwroot/Images`** — image saves are ignored by default (`.png`, `.jpg`, `.gif`, `.webp`, `.svg`, `.ico`, …). **`wwwroot/Files`** (PDFs, Office docs, etc.) still trigger rebuilds unless you add `Files` or `wwwroot` to **`watchExcludeSegments`**.
- For **dotnet watch**, also add `<Watch Remove="**/.cursor/**" />` (and similar) to the monitored `.csproj`. Defaults and behaviour: [features/health-and-logs.md](features/health-and-logs.md).

## Developer environment (not in settings UI)

- **`BUILDMONITOR_SKIP_PROJECT_START=1`** (or `true`) — skips starting active projects when the tray app launches. Used for idle tray/menu testing; not saved in `settings.json`. Remove the variable and restart the app. Settings → **Monitor** shows a notice when this is set.
- **`BUILDMONITOR_AUTO_BUILD_MONITOR_HEALTH=0`** (or `false`) — suppresses Build Monitor Health even when `monitor.autoOpenBuildMonitorHealthOnStartup` is true. Set to `1` / `true` to force it on. **`BUILDMONITOR_AUTO_THREAD_HEALTH`** is accepted as a legacy alias.

## Monitor — Build Monitor Health

- **`autoOpenBuildMonitorHealthOnStartup`** (default **true** after schema v7) — opens **Build Monitor Health** when the tray app starts. Turn off under Settings → **Monitor** → *Auto-open Build Monitor Health on startup* when you no longer need the diagnostics window.

## Projects — start on launch

- **`startOnLaunch`** (default **true** for new projects; migrated from global `monitor.autoStartActiveProjectsOnLaunch` in schema v10) — per project. When **true** and **active in session**, the project builds and runs automatically when the app starts or after you save settings. When **false**, the project stays monitored but idle until you use **Rebuild** / **Restart** from the tray. Settings → **Projects** → select project → *Start build when app launches*.

## App restart

- **Restart on crash** — retry run/watch after a non-zero exit (up to max retries).
- **Auto-restart on file changes (watch mode)** — `dotnet watch --non-interactive` when enabled; turn off to restart manually from the tray or status panel.
- **Auto-restart when output requires it** (default on) — scans build and run logs for hot-reload messages such as `requires restarting the application`, `unable to apply hot reload`, or `requires a rebuild`, then runs **Restart app** or **Rebuild & restart** automatically. Skips rude-edit lines when `dotnet watch` non-interactive auto-restart is already enabled.
- **Restart app after rebuild** — when run mode is Watch or Run, start (or restart) the app after a successful rebuild, including the first successful build after a prior failure.
- **Restart app** — stop and start run/watch with `--no-build` (no full rebuild).
- **Rebuild & restart** — full `dotnet build`, then start run/watch (shows build progress in status panel).
- **Show status panel while building** (default **off**) — per project. Opens the hover status panel when a build starts and hides it when the build finishes. Does not auto-hide if you already had the panel open before the build started.
