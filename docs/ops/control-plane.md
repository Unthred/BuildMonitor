# Localhost control plane

Loopback HTTP API so a Cursor agent in a watched .NET repo can signal edit bursts and request an explicit ship build/test. **No MCP.** Bind is **127.0.0.1 only**.

## Settings

Under **Settings → Monitor**:

| Key | Default | Meaning |
|-----|---------|---------|
| `controlPlaneEnabled` | `true` | Start the HTTP listener with the tray app |
| `controlPlanePort` | `7700` | `http://127.0.0.1:{port}/` |
| `controlPlaneBusyTimeoutSeconds` | `120` | Busy with no idle → treat as idle (agent crash) |
| `suppressAutoBuildTests` | `true` | Skip `OnBuildSuccess` tests after auto-builds; ship-check and tray **Run tests** still run |

Override `suppressAutoBuildTests` per project via session/ship-check JSON (`suppressAutoBuildTests`).

## How the target project is chosen

BuildMonitor is multi-project. Every scoped call requires **`projectId`** (query or JSON body).

1. Discover IDs: `GET http://127.0.0.1:7700/projects`
2. Store the matching `id` (and optionally match `rootFolder` to the repo you are editing)
3. Pass that `projectId` on session / watch / ship-check calls

The build target is the project's configured **Project file** (same as the tray monitor). Tests use **Test project / solution** or auto-discovery; if none, ship-check omits `tests` and `ok` follows build only.

## Endpoints

Base: `http://127.0.0.1:{controlPlanePort}`

| Method | Path | Notes |
|--------|------|--------|
| GET | `/projects` | List configured projects (`id`, `displayName`, `rootFolder`, …) |
| POST | `/session/busy` | Body: `{ "projectId": "…" }` — do not auto-build |
| POST | `/session/idle` | Edit burst done — auto-build may run after debounce |
| GET | `/session?projectId=` | `{ "state": "busy"\|"idle", "since", "idleCause": "none"\|"agent"\|"timeout", "lastActivity" }` |
| POST | `/run/rebuild` | Mark idle → pause watch (exit run host) → build → resume watch |
| POST | `/run/tests` | Mark idle → run tests (`filter` optional) — no full ship-check |
| POST | `/run/ship-check` | Pause watch → build → test (if any) → resume |
| GET | `/watch?projectId=` | `{ "watch": "running"\|"paused"\|"stopped", "pid": n\|null }` |
| POST | `/watch/pause` | Stop run/watch child (unlock DLLs) |
| POST | `/watch/resume` | Start watch again if it was paused |

Optional ship-check body: `{ "projectId", "configuration": "Debug", "filter": null, "suppressAutoBuildTests": true }`.

Optional rebuild body: `{ "projectId", "configuration": "Debug" }`.
Optional tests body: `{ "projectId", "configuration": "Debug", "filter": "FullyQualifiedName~MyTest" }`.

## Behaviour

- Auto-build on file change only when that project's session is **idle** (after `/session/busy` has been used at least once this process lifetime). Until then, existing debounce / agent-transcript gating remains the fallback.
- Idle does **not** push results to the agent and does not run the full suite when `suppressAutoBuildTests` is effective.
- **`POST /run/rebuild`** marks the session **idle**, pauses the watch/run host so DLLs unlock, runs one explicit build, then resumes watch if it was running. Build-only — no tests. Use when you need a clean rebuild without ship-check. **409** if rebuild or ship-check is already running.
- **`POST /run/tests`** marks idle and runs tests (optional `filter` / `configuration`). Does not rebuild first. **409** if tests, rebuild, or ship-check is already running.
- Busy timeout (default **120s**) is measured from the last **busy POST or file-change while busy**, not from the original busy start. If timeout fires, the status card says **Agent busy timed out · build allowed** (as opposed to **Agent finished editing** when `/session/idle` arrived).
- Ship-check cancels an in-flight build for that project, then runs; **409** if a ship-check is already running for that project.
- Pause = stop the supervised `dotnet run`/`watch` process (preferred over kill-as-default).

## Status panel visibility

After the session API has been used for a project this process lifetime, the hover **Build status** card shows control-plane state from the same `ProjectHealthSnapshot` model as the tray and diagnostics (not a separate HTTP poll):

| State | Card headline / lines |
|-------|------------------------|
| Busy | **Agent editing — builds paused** · Agent: Busy · busy duration · automatic builds held · queued file changes |
| Idle (recent) | **Agent finished editing · build allowed** · Agent: Connected · Idle |
| Idle (steady) | Agent: Connected · Idle |
| Ship-check | **Ship check — preparing / building / testing / resuming watch** |
| Ship-check result | **Ship check passed** or **Ship check failed** (shown briefly after completion) |
| Agent rebuild | **Rebuild — preparing / building / resuming watch** (watch host paused) |
| Rebuild result | **Rebuild passed** or **Rebuild failed** (shown briefly after completion) |
| Agent tests | **Tests — running** then **Tests passed** / **Tests failed** |
| Busy timeout | **Agent busy timed out · build allowed** (distinct from agent `/session/idle`) |

Build health (Green/Failed), watch host activity, and agent session state are independent dimensions on the same card.

## PowerShell examples

```powershell
$port = 7700
$base = "http://127.0.0.1:$port"

# Discover projectId
Invoke-RestMethod "$base/projects"

$projectId = "<paste-id>"

Invoke-RestMethod -Method Post -Uri "$base/session/busy" -ContentType "application/json" `
  -Body (@{ projectId = $projectId } | ConvertTo-Json)

# ... agent edits files ...

Invoke-RestMethod -Method Post -Uri "$base/session/idle" -ContentType "application/json" `
  -Body (@{ projectId = $projectId } | ConvertTo-Json)

Invoke-RestMethod "$base/session?projectId=$projectId"

# Build-only (exit watch host, rebuild, resume watch — no tests)
$rebuild = Invoke-RestMethod -Method Post -Uri "$base/run/rebuild" -ContentType "application/json" `
  -Body (@{ projectId = $projectId; configuration = "Debug" } | ConvertTo-Json)

$result = Invoke-RestMethod -Method Post -Uri "$base/run/ship-check" -ContentType "application/json" `
  -Body (@{ projectId = $projectId; configuration = "Debug" } | ConvertTo-Json)
$result | ConvertTo-Json -Depth 5
```

## Behaviour map

| Step | What | Where |
|------|------|--------|
| 1 | HttpListener on 127.0.0.1 | `LocalControlPlaneHost` |
| 2 | Routes | `ControlPlaneHttpRouter` |
| 3 | Session busy/idle + timeout | `ControlPlaneSessionStore`, `ControlPlaneSessionPolicy` |
| 4 | Gate file-change builds | `ProjectRuntime` + session store |
| 5 | Ship-check | `ProjectRuntime.RunShipCheckAsync` |
| 6 | Agent rebuild | `ProjectRuntime.RunAgentRebuildAsync` |

**Failure / fallback:** if the port cannot bind, the tray shows a warning; monitoring continues without the API.

## Agent onboarding (A + C)

Agents in a watched product repo do **not** see BuildMonitor’s docs by default. Install the skill from the tray:

- **Settings → Projects** → select project → status line + **Install / Update** (also **Refresh**)
- Or tray: **Install Cursor agent skill → &lt;project&gt;**

Install writes both the skill and an **always-on** Cursor rule so agents use busy/idle/ship-check without the user pasting instructions.

**Discovery:**

1. Tray writes `%LocalAppData%\BuildMonitor\control-plane.json` when the control plane binds (port, `baseUrl`, project list).
2. Skill probes that file, else `GET http://127.0.0.1:7700/projects`, matches `rootFolder` to the workspace, then busy → edit → idle → ship-check.

## Metrics (Build diagnostics)

Per-project tiles under **Build diagnostics** (process lifetime, in memory — reset when the tray app exits):

| Metric | Meaning |
|--------|---------|
| Session | Current busy/idle (after timeout rules) |
| Busy / idle calls | `POST /session/busy` and `/idle` counts |
| Time busy | Sum of busy intervals (including timeout→idle) |
| Builds blocked | File-change rebuilds held while busy |
| Ship-check | Pass rate, pass/total, average duration |
| Call rate | HTTP calls in the last hour for that project |
| HTTP | Request count and 4xx / 5xx |

Open **Tray → Build diagnostics…** and select the project tab.
