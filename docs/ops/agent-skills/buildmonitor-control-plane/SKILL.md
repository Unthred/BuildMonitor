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

BuildMonitor is a separate tray app that watches configured folders and auto-builds.
Talk to it over **loopback HTTP only** so it does not rebuild mid-edit and so you can
request an explicit ship build/test. **Do not invent MCP** — HTTP only.

## When to use

| Moment | Action |
|--------|--------|
| About to edit several files / a burst | `POST /session/busy` |
| Edit burst finished | `POST /session/idle` (auto-build may run after debounce; do **not** expect a callback) |
| Before claiming the change builds / tests | `POST /run/ship-check` only — do not assume idle ran tests |
| Agent crash / forgotten idle | Busy auto-expires after ~2 minutes |

If the control plane is unreachable, continue editing; BuildMonitor falls back to its own debounce. Say briefly that the handshake was skipped.

## Discover base URL and projectId (probe)

Do this once per chat (or again if the workspace root changes).

1. **Discovery file (preferred)** — if it exists, read:

   `%LocalAppData%\BuildMonitor\control-plane.json`

   Use `baseUrl` when `enabled` is true. Match a project whose `rootFolder` is the workspace root or a parent/child of it (case-insensitive path compare). Use that project's `id` as `projectId`.

2. **Probe (fallback)** — if the file is missing or `enabled` is false:

```powershell
try { Invoke-RestMethod "http://127.0.0.1:7700/projects" } catch { $null }
```

   If that fails, try nothing else unless the user gave another port. Match `rootFolder` to the workspace the same way.

3. **Cache** `baseUrl` and `projectId` for the rest of the session.

4. If no matching project: skip the handshake and tell the user BuildMonitor has no project for this folder (they may need to add it in Settings).

## API (all scoped calls need projectId)

Base example: `http://127.0.0.1:7700`

| Method | Path | Body / query |
|--------|------|----------------|
| GET | `/projects` | — |
| POST | `/session/busy` | `{ "projectId": "…" }` |
| POST | `/session/idle` | `{ "projectId": "…" }` |
| GET | `/session` | `?projectId=` |
| POST | `/run/ship-check` | `{ "projectId": "…", "configuration": "Debug" }` optional |
| GET | `/watch` | `?projectId=` |

### PowerShell

```powershell
$base = "http://127.0.0.1:7700"   # or discovery baseUrl
$projectId = "<id>"

Invoke-RestMethod -Method Post -Uri "$base/session/busy" -ContentType "application/json" `
  -Body (@{ projectId = $projectId } | ConvertTo-Json)

# ... edit files ...

Invoke-RestMethod -Method Post -Uri "$base/session/idle" -ContentType "application/json" `
  -Body (@{ projectId = $projectId } | ConvertTo-Json)

$result = Invoke-RestMethod -Method Post -Uri "$base/run/ship-check" -ContentType "application/json" `
  -Body (@{ projectId = $projectId; configuration = "Debug" } | ConvertTo-Json)
```

Treat `ok: false` on ship-check as a failed verification — read `failures` / `log` and fix before claiming success.

## Rules

- Bind is loopback only; no auth.
- Never require WitherbyConnect or a hard-coded product path — match `rootFolder` only.
- Idle must **not** be treated as “tests passed”.
- Prefer pause/ship-check over killing watch processes yourself.
- Keep calls short; do not poll forever.
