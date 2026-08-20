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
| After task | Leave mode as `ai-controlled` (do **not** auto-switch back) |

**Normal AI workflow:**

```text
discover project
GET /mode
POST /mode ai-controlled   (if needed)
POST /session/busy
edit files
POST /session/idle
POST /run/rebuild          (or /run/ship-check for final)
```

Do **not** treat `/session/idle` as “build now”.
Do **not** rely on busy timeout to resume builds in AI Controlled mode.
Do **not** call `/run/rebuild` after every edit burst — only when verification needs a compile.

If the control plane is unreachable, continue editing; say briefly that the handshake was skipped.

## Efficient workflows (pick the smallest call)

| Scenario | Workflow |
|----------|----------|
| Edit burst (AI Controlled) | ensure mode → `busy` → edit → `idle` → explicit `/run/rebuild` if needed |
| One or a few tests | `/run/tests` with `filter` — rebuild first if binaries may be stale |
| Full verification | `/run/ship-check` — before claiming tests pass |
| Locked DLLs / bad incremental | `/run/rebuild` |
| Still editing after a pause | `busy` again before more writes |

**Test filters:** `FullyQualifiedName=Ns.Class.Method` (one), `FullyQualifiedName~Ns.Class` (class/range), omit `filter` (all).

**Anti-patterns:** `idle` mid-edit; rebuild every burst; assuming idle means tests passed; overlapping `/run/*` calls (409); leaving File Watching mode during agent edits.

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
|--------|------|----------------|
| GET | `/projects` | — |
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

## Rules

- Prefer AI Controlled for agent edit sessions; leave it set after the task.
- In AI Controlled, file changes are observed but never auto-build.
- `/session/idle` never means “build now” in AI Controlled.
- Prefer `/run/tests` with a filter over a full ship-check when only a subset matters.
- Prefer `/run/rebuild` only when a clean rebuild is needed; prefer `/run/ship-check` for final verification.
- Never invent MCP tools for BuildMonitor.
