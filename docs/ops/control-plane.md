# Localhost control plane

Loopback HTTP API so a Cursor agent in a watched .NET repo can signal edit bursts and request an explicit ship build/test. **No MCP.** Bind is **127.0.0.1 only**.

## Settings

Under **Settings → Monitor**:

| Key | Default | Meaning |
|-----|---------|---------|
| `controlPlaneEnabled` | `true` | Start the HTTP listener with the tray app |
| `controlPlanePort` | `7700` | `http://127.0.0.1:{port}/` |
| `controlPlaneBusyTimeoutSeconds` | `120` | Busy with no idle → treat as idle (agent crash) |
| `suppressAutoBuildTests` | `true` | Skip `OnBuildSuccess` and `OnFileChange` automatic post-build tests after auto-builds; ship-check and tray **Run tests** still run |

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
| GET | `/projects` | List configured projects with authoritative Local/Azure health (see below) |
| POST | `/app/quit` | Graceful BuildMonitor tray exit (same as tray **Exit**). **202** `{ ok, quitting }` — accept-and-exit: response returns as soon as quit is scheduled (failsafe armed first); teardown continues asynchronously. **503** when quit cannot be scheduled. Must **not** return **500** for thread-affinity/UI errors on the HTTP thread. Hard-exit failsafe (~20s) is armed before UI teardown so a later hang still terminates the process; a second quit/Exit forces immediate hard exit. |
| GET | `/mode?projectId=` | `{ "projectId", "mode": "file-watching"\|"ai-controlled" }` |
| POST | `/mode` | `{ "projectId", "mode" }` → `{ "projectId", "previousMode", "mode" }` |
| POST | `/session/busy` | Body: `{ "projectId": "…" }` — agent editing |
| POST | `/session/idle` | Edit burst done — **File Watching** may auto-build; **AI Controlled** does **not** |
| GET | `/session?projectId=` | `{ "state": "busy"\|"idle", "since", "idleCause": "none"\|"agent"\|"timeout", "lastActivity" }` |
| POST | `/run/stop` | Explicit stop: sets **desired host state Stopped**; watch reports **stopped** (not paused). Ship-check/rebuild/tests must not auto-resume |
| POST | `/run/rebuild` | Mark idle → pause watch (exit run host) → build → resume watch **only if desired state is Running** |
| POST | `/run/tests` | Mark idle → run tests (`filter` optional) — no full ship-check |
| POST | `/run/ship-check` | Pause watch → build → test (if any) → resume **only if desired state is Running** |
| GET | `/watch?projectId=` | `{ "watch": "running"\|"paused"\|"stopped", "pid": n\|null }` |
| POST | `/watch/pause` | Temporary operational pause (desired state unchanged) |
| POST | `/watch/resume` | Resume host only when desired state is Running |

Optional ship-check body: `{ "projectId", "configuration": "Debug", "filter": null, "suppressAutoBuildTests": true }`.

Optional rebuild body: `{ "projectId", "configuration": "Debug" }`.
Optional tests body: `{ "projectId", "configuration": "Debug", "filter": "FullyQualifiedName~MyTest" }`.

## `GET /projects` — authoritative Local + Azure state

`/projects` returns the **same** project snapshot facets that drive the tray / hover status panel. The handler is a **snapshot read only** — it does **not** call Azure HTTP, spawn git, or trigger a poll.

### Terminology

| Field | Meaning |
|-------|---------|
| **Run ID** (`azure.runId`) | Azure DevOps `Build.id` (e.g. `458`). Same primary/current run shown in the status UI. |
| **Build number** (`azure.buildNumber`) | Azure `buildNumber` string (e.g. `20260826.3`). Never use this as the run id. |
| **PR number** (`azure.pullRequestNumber`) | Pull request id when the primary run is PR-scoped; separate from run id / build number. |
| **Poll timestamp** (`azure.polledAtUtc`) | When BuildMonitor last refreshed this Azure facet. Optional `ageSeconds` is derived; freshness is only as good as the poll cadence. |
| **Overall health** (`overallHealth`) | Composite Local + Azure tray health (`green` / `amber` / `red` / `unknown`). |

### Example (conceptual)

```json
{
  "id": "1e6b255b-…",
  "displayName": "WitherbyConnect (main)",
  "rootFolder": "C:\\src\\WitherbyConnectDotNet9",
  "projectFile": "WitherbyConnect.csproj",
  "isActiveInSession": true,
  "overallHealth": "red",
  "overallHealthLabel": "Failed",
  "sessionState": "idle",
  "local": {
    "status": "green",
    "branch": "master",
    "lastBuildAtUtc": "2026-08-26T06:40:00+00:00",
    "errors": 0,
    "warnings": 0,
    "lifecycleState": "buildOk"
  },
  "azure": {
    "availability": "available",
    "ciState": "failed",
    "pipeline": "WitherbyConnect",
    "status": "Failed",
    "branch": "PR #168",
    "runId": 458,
    "buildNumber": "20260826.3",
    "pullRequestNumber": 168,
    "runUrl": "https://dev.azure.com/…/_build/results?buildId=458",
    "polledAtUtc": "2026-08-26T07:00:00+00:00",
    "ageSeconds": 5
  }
}
```

### Semantics

- **Primary run** = `ProjectAzureHealthFacet.PrimaryRun` (status-panel current run). `/projects` never independently picks “newest”.
- **Zero pipelines:** Azure attached, `ciState: notMonitored`, `runId` null — no fake run.
- **Auth / network:** `availability: authRequired` or `unavailable`; do not treat as healthy CI. Last-known attention text may appear in `attentionSummary` but does not replace availability.
- Secrets (PATs, Authorization) are never serialized.

### Agent guidance

When BuildMonitor exposes Azure state for a monitored project, treat `GET /projects` as the **authoritative current Azure run/status**. Prefer it over independently querying Azure or inferring “latest” from history. Only query Azure independently if BuildMonitor has no Azure facet for that project, or the user asks for deeper history/details.

## Build-control modes (per project)

Each project stores `buildControlMode` in settings (`file-watching` default):

| Mode | Wire | File-change auto-build |
|------|------|------------------------|
| File Watching | `file-watching` | Yes — debounced; held while `/session/busy` |
| AI Controlled | `ai-controlled` | **Never** — watcher observes and counts only |

**AI Controlled invariant:** the file watcher may observe source changes but must never initiate build work. Busy timeout and `/session/idle` do **not** start builds. Use `/run/rebuild` or `/run/ship-check` (or tray Rebuild).

Switching File Watching → AI Controlled cancels pending file-triggered schedules (not an in-flight build). Switching AI Controlled → File Watching clears held pending triggers without a surprise build.

Settings → Projects → **Build control**. Agents: `GET`/`POST /mode`.

Isolation details (watch host, timers, hot reload): [ai-controlled-build-isolation.md](ai-controlled-build-isolation.md).

## Agent capability matrix (efficient build control)

Use this table to pick the **smallest** call that achieves the goal. Avoid redundant rebuilds and premature `idle`.

### Goals → endpoints

| Agent goal | Endpoint | Notes |
|------------|----------|--------|
| Take ownership of builds | `POST /mode` `ai-controlled` | Persist; do not auto-revert after the task |
| Inspect build-control mode | `GET /mode?projectId=` | `file-watching` or `ai-controlled` |
| Pause auto-build (File Watching) | `POST /session/busy` | Holds automatic rebuilds; watcher may still **queue** changes |
| Signal editing finished | `POST /session/idle` | File Watching: may debounce-build. AI Controlled: **no** auto-build |
| Extend the hold while still editing | `POST /session/busy` again | Resets the busy timeout clock |
| Build only (no tests) | `POST /run/rebuild` | Required path in AI Controlled; also works in File Watching |
| Build only (File Watching, lightest) | `POST /session/idle` | Debounced auto-build — **not** available in AI Controlled |
| Run one unit test | `POST /run/tests` | `"filter": "FullyQualifiedName=Namespace.Class.Method"` |
| Run a class or namespace of tests | `POST /run/tests` | `"filter": "FullyQualifiedName~Namespace.Class"` or `"FullyQualifiedName~Namespace"` |
| Run tests by category / trait | `POST /run/tests` | `"filter": "Category=Unit"` (or any trait your tests expose) |
| Run all unit tests | `POST /run/tests` | Omit `filter`, or use ship-check if you also need a fresh build |
| Build + all tests (verification) | `POST /run/ship-check` | Preferred before claiming “builds and tests pass” |
| Stop BuildMonitor tray (before deploy) | `POST /app/quit` | Graceful exit; wait until port closes, then replace binaries |
| Pause watch/run host (unlock DLLs) | `POST /watch/pause` | Temporary operational pause — **desired host state stays Running**; pair with `/watch/resume` |
| Resume watch/run host | `POST /watch/resume` | Restores host only when desired state is Running (after pause). Does **not** override an explicit `/run/stop` |
| Explicit stop run host | `POST /run/stop` | Sets **desired host state to Stopped**; ship-check / rebuild / tests must not auto-resume |
| Read session state | `GET /session?projectId=` | `idleCause`: `agent` (you sent idle) vs `timeout` (120s expired) |
| Read watch host state | `GET /watch?projectId=` | `running` / `paused` / `stopped` |
| Discover project + port | `GET /projects` or `%LocalAppData%\BuildMonitor\control-plane.json` | Required once per chat |

**Busy vs watch pause:** `/session/busy` marks agent editing. In **File Watching** it also holds auto-rebuilds. In **AI Controlled** auto-rebuilds are already off. `/watch/pause` stops the supervised `dotnet run`/`watch` child.

**Tests without rebuild:** `/run/tests` does **not** compile first. If the previous build succeeded it uses `dotnet test --no-build`. Missing or stale test assemblies (including VSTest `Test run for …` + `test source file … was not found`) trigger **one** full-build recovery and a single retry. Genuine executed-test failures do not rebuild. Prefer `/run/rebuild` or `/run/ship-check` when you already know binaries are stale.

### Cost-aware workflows

| Scenario | Workflow | Why |
|----------|----------|-----|
| Agent multi-file edit | mode→`ai-controlled` → `busy` → edit → `idle` → `/run/rebuild` | No race with debounce/timeout |
| Human File Watching edit | save files (or `busy`/`idle`) | Debounce coalesces saves |
| One failing test to iterate | `/run/tests` with narrow `filter` | Faster than full suite; rebuild only if compile errors |
| Class-level test focus | `/run/tests` with `FullyQualifiedName~MyClass` | Same as above, broader slice |
| Before claiming PR-ready | `idle` → `/run/ship-check` | Single authoritative build + full test run |
| Locked output / bad incremental | `/run/rebuild` | Watch host must exit so MSBuild can overwrite DLLs |
| Agent still editing after a pause | `busy` again **before** more writes | Prevents mid-edit confusion |

### Test filter examples (`dotnet test` syntax)

Pass `filter` on `POST /run/tests` (and optionally on ship-check):

```text
FullyQualifiedName=BuildMonitor.Tests.MyTests.MyMethod          # one test
FullyQualifiedName~BuildMonitor.Tests.ControlPlane              # namespace/class substring
FullyQualifiedName~MyTests&Category=Unit                        # combine with &
FullyQualifiedName~TestA|FullyQualifiedName~TestB               # either test (OR)
```

Omit `filter` to run the full configured test project/solution.

### Not exposed (workarounds)

| Desired | Today | Workaround |
|---------|-------|------------|
| Disable file watcher entirely | Not available | `busy` holds builds; changes may still queue |
| Cancel in-flight build/test | Not available | Wait for 409 conflict to clear; avoid overlapping `/run/*` |
| Stream live build log over HTTP | Not available | Read `log` path from ship-check/rebuild/tests JSON response |
| Build without pausing watch (explicit) | No `/run/build` | Use `idle` + debounced auto-build |
| Guaranteed fresh build + filtered tests in one call | Not combined | `/run/ship-check` (full suite) or `/run/rebuild` then `/run/tests` |

### Anti-patterns

- **`idle` after the first file** — debounced auto-build starts while you still edit. Stay `busy` until every file for the turn is written.
- **`/run/rebuild` after every burst** — use `idle` and debounce unless DLLs are locked or incremental state is unreliable.
- **`/run/tests` after compile errors** — fix build first (auto-build or rebuild), then filter tests.
- **Treating `idle` as “tests passed”** — idle only resumes auto-build; use ship-check or `/run/tests` for verification.
- **Overlapping `/run/rebuild`, `/run/tests`, `/run/ship-check`** — second call returns **409** until the first finishes.
- **Cursor “Waiting up to Xm for shell” after tests already finished** — agent Shell/`AwaitShell` countdown, not BuildMonitor. See [agent skill § Shell wait rules](agent-skills/buildmonitor-control-plane/SKILL.md#shell-wait-rules-authoritative).

## Behaviour

- Auto-build on file change only when that project's session is **idle** (after `/session/busy` has been used at least once this process lifetime). Until then, existing debounce / agent-transcript gating remains the fallback.
- Idle does **not** push results to the agent and does not run the full suite when `suppressAutoBuildTests` is effective. That suppress gate applies to both **`OnBuildSuccess`** and **`OnFileChange`** (`After file-triggered build`) automatic post-build tests.
- **`POST /run/rebuild`** marks the session **idle**, pauses the watch/run host so DLLs unlock, runs one explicit build, then resumes watch if it was running. Build-only — no tests. Use when you need a clean rebuild without ship-check. **409** if rebuild or ship-check is already running.
- **`POST /run/tests`** marks idle and runs tests (optional `filter` / `configuration`). Does not rebuild first unless `--no-build` hits missing/stale test assemblies, in which case it does **one** full-build recovery and retries tests once. **409** if tests, rebuild, or ship-check is already running.
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
| 7 | `/run/tests` `--no-build` then at most one stale-assembly recovery | `TestRunRecoveryCoordinator`, `DotNetTestOutputParser`, `ProjectRuntime.Test` |

**Failure / fallback:** if the port cannot bind, the tray shows a warning; monitoring continues without the API.

## Agent onboarding (A + C)

Agents in a watched product repo do **not** see BuildMonitor’s docs by default. Install the skill from the tray:

- **Settings → Projects** → select project → status line + **Install / Update** (also **Refresh**)
- Or tray: **Install Cursor agent skill → &lt;project&gt;**

Install writes both the skill and an **always-on** Cursor rule so agents use busy/idle/ship-check without the user pasting instructions.

**Discovery:**

1. Tray writes `%LocalAppData%\BuildMonitor\control-plane.json` when the control plane binds (port, `baseUrl`, project list).
2. Skill probes that file, else `GET http://127.0.0.1:7700/projects`, matches `rootFolder` to the workspace, then mode → busy → edit → idle → explicit `/run/rebuild` or `/run/ship-check`.

**Chat announcements:** the installed skill and always-on rule require a short `BuildMonitor:` line in the agent reply for mode/busy/idle and each `/run/*` start/result (and when handshake is skipped), so Cursor output shows control-plane activity without a live log stream.

## Metrics (Build diagnostics)

Per-project tiles under **Build diagnostics** (process lifetime, in memory — reset when the tray app exits):

| Metric | Meaning |
|--------|---------|
| Session | Current busy/idle (after timeout rules) |
| Busy / idle calls | `POST /session/busy` and `/idle` counts |
| Time busy | Sum of busy intervals (including timeout→idle) |
| Builds blocked | File-change rebuilds held while busy |
| Agent workflow | **Healthy** / **Busy** / **Extra builds** / **Build during busy** — correlates busy → idle → builds |
| Agent events | Today’s busy/idle/blocked/rebuild/tests timeline (persisted) |
| Ship-check | Pass rate, pass/total, average duration |
| Call rate | HTTP calls in the last hour for that project |
| HTTP | Request count and 4xx / 5xx |

Open **Tray → Build diagnostics…** and select the project tab. The **Agent workflow** panel shows whether the last agent cycle behaved as expected (one build after idle, builds blocked while busy). **Recent agent events** lists today’s `/session/busy`, `/session/idle`, blocked file changes, and explicit rebuild/test calls.
