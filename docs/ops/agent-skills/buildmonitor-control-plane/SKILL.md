---
name: buildmonitor-control-plane
description: >-
  Handshake with the local BuildMonitor tray app over loopback HTTP before
  multi-file edits and before claiming build/test success. Use when editing a
  .NET repo that BuildMonitor may be watching, when starting an edit burst,
  finishing edits, or running a ship build/test check. Probes
  http://127.0.0.1:7700 (or %LocalAppData%\BuildMonitor\control-plane.json).
---

# BuildMonitor control plane handshake

BuildMonitor is a separate tray app that watches configured folders.
Talk to it over **loopback HTTP only**. **Do not invent MCP** — HTTP only.

Projects have an explicit **build-control mode**:

| Mode | Wire value | Auto-build on file change |
|------|------------|---------------------------|
| File Watching | `file-watching` | Yes (debounced; held while busy) |
| AI Controlled | `ai-controlled` | **Never** — observe only |

For agent work, set **AI Controlled** so idle / busy timeout cannot start a build.

## Chat announcements (required)

After each successful control-plane call (or when skipping), put **one short line** in the user-visible reply so the human can follow BuildMonitor activity. Use this exact prefix and shape:

| Event | Announce |
|-------|----------|
| Mode set / confirmed | `BuildMonitor: AI Controlled` (include project display name if known) |
| Busy | `BuildMonitor: busy — editing` |
| Idle | `BuildMonitor: idle — awaiting explicit build` |
| Starting rebuild | `BuildMonitor: /run/rebuild…` |
| Rebuild finished | `BuildMonitor: /run/rebuild — pass` or `… — fail (exit N)` |
| Starting ship-check | `BuildMonitor: /run/ship-check…` |
| Ship-check finished | `BuildMonitor: /run/ship-check — pass` or `… — fail` (mention build vs tests if known) |
| Tests only | `BuildMonitor: /run/tests…` then `… — pass` / `… — fail` |
| Unreachable / no project | `BuildMonitor: handshake skipped (unreachable)` or `(no project for this folder)` |

Do **not** stay silent on handshake or `/run/*`. Do **not** invent extra MCP or pretend BuildMonitor streamed into chat — these lines are the signal.

## When to use

| Moment | Action |
|--------|--------|
| Start of task | Discover project → `GET /mode` → if not `ai-controlled`, `POST /mode` with `ai-controlled` → `POST /session/busy` |
| Still editing after a pause | `POST /session/busy` again (extends the hold) |
| Edit burst finished | `POST /session/idle` — **does not build** in AI Controlled mode |
| Iterative verify | `POST /run/rebuild` when a rebuild is actually required |
| Final verification | `POST /run/ship-check` |
| Run tests only | `POST /run/tests` — optional `"filter"`; does not rebuild first |
| Stop running app | `POST /run/stop` |
| Quit BuildMonitor tray (before deploy) | `POST /app/quit` |
| After task | Leave mode as `ai-controlled` (do **not** auto-switch back) |

**Normal AI workflow:**

```text
discover project
GET /mode
POST /mode ai-controlled   (if needed)   → announce
POST /session/busy                       → announce
edit files
POST /session/idle                       → announce
POST /run/rebuild or /run/ship-check     → announce start + result
```

Do **not** treat `/session/idle` as “build now”.
Do **not** rely on busy timeout to resume builds in AI Controlled mode.
Do **not** call `/run/rebuild` after every edit burst — only when verification needs a compile.

If the control plane is unreachable, continue editing and announce that the handshake was skipped.

## Efficient workflows (pick the smallest call)

| Scenario | Workflow |
|----------|----------|
| Edit burst (AI Controlled) | ensure mode → `busy` → edit → `idle` → explicit `/run/rebuild` if needed |
| One or a few tests | `/run/tests` with `filter` — missing/stale assemblies get one recovery rebuild; otherwise no compile first |
| Full verification | `/run/ship-check` — before claiming tests pass |
| Locked DLLs / bad incremental | `/run/rebuild` |
| Still editing after a pause | `busy` again before more writes |

**Test filters:** `FullyQualifiedName=Ns.Class.Method` (one), `FullyQualifiedName~Ns.Class` (class/range), omit `filter` (all).

**Anti-patterns:** `idle` mid-edit; rebuild every burst; assuming idle means tests passed; overlapping `/run/*` calls (409); leaving File Watching mode during agent edits; silent handshake/`/run/*` with no chat line; long `AwaitShell` after `/run/*` or `dotnet` already finished (see Shell wait rules).

## Shell wait rules (authoritative)

Cursor’s UI string **“Waiting up to Xm for shell”** is the agent **Shell / `AwaitShell` `block_until_ms` countdown**. It is **not** BuildMonitor holding an HTTP call open after work finishes.

These rules are the **reusable** wait/monitoring contract for every watched repo. Do not restate them as long duplicated sections in product repos; point here (or reinstall this skill) instead.

### Facts

- `POST /run/rebuild`, `/run/tests`, and `/run/ship-check` return JSON **as soon as** the local build/test process exits (**pass or fail**).
- Azure tray polling is a **separate** background loop (~**8s** while a run is active, ~**15s** settled; auth/network failure backoff capped ~**15–45s**). It does **not** gate `/run/*` responses.
- Successful and failed commands must both surface promptly — do not wait longer on failure.
- Expected detection delay after a known terminal state is normally **≤15 seconds** (one short poll), never multi-minute.

### AwaitShell semantics (critical)

`AwaitShell.block_until_ms` is a **maximum wait**, not a requested sleep duration.

- Do **not** use a very large `AwaitShell` (e.g. 5–10 minutes / `600000`) as a substitute for monitoring process state.
- Prefer **repeated short waits** when monitoring is required: about **5–15 seconds** per poll.
- Do **not** use multi-minute polling intervals for an active local BuildMonitor command.
- When exit code / footer / `"ok"` JSON / Azure `status=completed` is known, **return immediately** — never wait for the remainder of a timeout budget.

### Required workflow

```text
command
→ wait briefly (foreground Shell sized to expected runtime)
→ if complete, return
→ if still running, short poll (~5–15s)
→ repeat until exit / terminal state
```

**Not:**

```text
command
→ background (default ~30s Shell budget)
→ AwaitShell 600000
→ sit for several minutes after work already finished
```

### Local `/run/*` commands

For `/run/ship-check`, `/run/tests`, and `/run/rebuild`:

1. Prefer a Shell that **stays attached** until normal completion.
2. Set Shell `block_until_ms` to a **realistic** expected runtime (e.g. a few minutes for filtered tests; often **5–15 min** for full ship-check) so the call stays foreground until JSON arrives.
3. Do **not** automatically background a BuildMonitor command and then call `AwaitShell` with a 5–10 minute timeout.
4. If backgrounding happens because the initial Shell wait was exceeded: poll with **short** waits (`AwaitShell` ~5–15s, or `block_until_ms: 0` / read the terminal file); detect process exit promptly; return as soon as exit code is available.
5. Treat **exit code 0 and non-zero** as terminal states.
6. Never wait for the remainder of a timeout budget once process completion is known.

| Do | Do not |
|----|--------|
| One foreground Shell with realistic `block_until_ms` until `/run/*` JSON returns | Background with default ~30s, then `AwaitShell` for minutes |
| On process exit / HTTP response, read `ok` or exit code and announce immediately | Keep waiting after `"ok"` JSON, `Passed!`, non-zero exit, or Azure `status=completed` |
| If backgrounded: short polls (~5–15s); stop on exit | Use a long `AwaitShell` as a substitute for monitoring state |

### Azure DevOps monitoring (agent watchers)

When the agent itself watches an Azure build (separate from tray polling):

- Poll at about **5–15 seconds**.
- When Azure reports `status=completed`, **stop immediately** and report succeeded / failed / cancelled.
- Do **not** keep a Shell / `AwaitShell` alive after a terminal Azure result.
- Auth/API errors may use modest backoff, but **terminal build state always wins**.

## Discover base URL and projectId (probe)

Do this once per chat (or again if the workspace root changes).

1. **Discovery file (preferred)** — if it exists, read:

   `%LocalAppData%\BuildMonitor\control-plane.json`

   Use `baseUrl` when `enabled` is true. Match a project whose `rootFolder` is the workspace root or a parent/child of it (case-insensitive path compare). Use that project's `id` as `projectId`.

2. **Probe (fallback)** — if the file is missing or `enabled` is false:

```powershell
try { Invoke-RestMethod "http://127.0.0.1:7700/projects" } catch { $null }
```

3. **Cache** `baseUrl` and `projectId` for the rest of the session.

4. If no matching project: skip the handshake and tell the user BuildMonitor has no project for this folder.

## API (all scoped calls need projectId)

Base example: `http://127.0.0.1:7700`

| Method | Path | Body / query |
|--------|------|-------------|
| GET | `/projects` | Authoritative Local + Azure project status (same primary Azure run as the tray/status panel). Prefer this over independently querying Azure. |
| GET | `/mode` | `?projectId=` → `{ "mode": "file-watching" \| "ai-controlled" }` |
| POST | `/mode` | `{ "projectId": "…", "mode": "ai-controlled" }` → includes `previousMode` |
| POST | `/session/busy` | `{ "projectId": "…" }` |
| POST | `/session/idle` | `{ "projectId": "…" }` |
| GET | `/session` | `?projectId=` |
| POST | `/run/stop` | `{ "projectId": "…" }` |
| POST | `/run/rebuild` | `{ "projectId": "…", "configuration": "Debug" }` optional |
| POST | `/run/tests` | `{ "projectId": "…", "filter": "…", "configuration": "Debug" }` optional |
| POST | `/run/ship-check` | `{ "projectId": "…", "configuration": "Debug" }` optional |
| GET | `/watch` | `?projectId=` |

### PowerShell

```powershell
$base = "http://127.0.0.1:7700"
$projectId = "<id>"

$mode = Invoke-RestMethod -Uri "$base/mode?projectId=$projectId"
if ($mode.mode -ne "ai-controlled") {
  Invoke-RestMethod -Method Post -Uri "$base/mode" -ContentType "application/json" `
    -Body (@{ projectId = $projectId; mode = "ai-controlled" } | ConvertTo-Json)
}

Invoke-RestMethod -Method Post -Uri "$base/session/busy" -ContentType "application/json" `
  -Body (@{ projectId = $projectId } | ConvertTo-Json)

# ... edit files ...

Invoke-RestMethod -Method Post -Uri "$base/session/idle" -ContentType "application/json" `
  -Body (@{ projectId = $projectId } | ConvertTo-Json)

# Explicit rebuild when needed (idle does NOT build in AI Controlled):
$rebuild = Invoke-RestMethod -Method Post -Uri "$base/run/rebuild" -ContentType "application/json" `
  -Body (@{ projectId = $projectId; configuration = "Debug" } | ConvertTo-Json)

# Before claiming tests passed:
$result = Invoke-RestMethod -Method Post -Uri "$base/run/ship-check" -ContentType "application/json" `
  -Body (@{ projectId = $projectId; configuration = "Debug" } | ConvertTo-Json)
```

Treat `ok: false` on ship-check as a failed verification — read `failures` / `log` and fix before claiming success.

## Authoritative Azure / Local status

When BuildMonitor exposes Azure state on `GET /projects` for a monitored project, treat it as the **authoritative current Azure run/status** (same primary run as the hover status UI). Do **not** independently infer “latest” from Azure history or stale chat context.

| Field | Use as |
|-------|--------|
| `azure.runId` | Azure Build.id (e.g. `458`) — current primary run |
| `azure.buildNumber` | Azure buildNumber string — **not** the run id |
| `azure.pullRequestNumber` | PR id when present |
| `azure.polledAtUtc` / `ageSeconds` | Freshness of BuildMonitor’s poll (not stronger than poll cadence) |
| `overallHealth` | Composite Local + Azure tray health |

Only query Azure independently if `/projects` has no `azure` facet for that project, or the user asks for deeper Azure history/details.

## Rules

- Prefer AI Controlled for agent edit sessions; leave it set after the task.
- In AI Controlled, file changes are observed but never auto-build.
- `/session/idle` never means “build now” in AI Controlled.
- Prefer `/run/tests` with a filter over a full ship-check when only a subset matters.
- Prefer `/run/rebuild` only when a clean rebuild is needed; prefer `/run/ship-check` for final verification.
- Prefer `GET /projects` for current Azure run/status over independent Azure inference.
- Always announce handshake and `/run/*` in chat (see table above).
- Never invent MCP tools for BuildMonitor.
