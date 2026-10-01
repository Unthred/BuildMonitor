---
name: buildmonitor-control-plane
verificationProvider: true
verificationProviderContractVersion: "1"
adapterVersion: "1.0.0"
adapterSource: Unthred/BuildMonitor
adapterSourcePath: docs/ops/agent-skills/buildmonitor-control-plane
description: >-
  Optional verification provider. Claims a worktree only when BuildMonitor is
  reachable, the configured project root exactly matches the folder being
  edited, and that project can run the requested operation. Owns rebuild,
  tests, status, and ship-check for that folder. Probe
  http://127.0.0.1:7700 or %LocalAppData%\BuildMonitor\control-plane.json.
---

# BuildMonitor verification provider

This is the **canonical** optional adapter for the generic verification-provider
contract (`verificationProvider: true`, contract version **1**). Product repos
define required outcomes; this skill maps them onto BuildMonitor.

Install at **user** Cursor config only. Do not copy this into WitherbyConnect or
other product repositories.

## Claim

Handshake once at the start of work **and again whenever the edited root changes**.

1. Resolve the **exact** Git worktree / folder being edited. Do not substitute
   the chat's original workspace, a parent checkout, a sibling worktree, or a
   similarly named folder.
2. Discover BuildMonitor: `%LocalAppData%\BuildMonitor\control-plane.json` when
   `enabled`, else `GET http://127.0.0.1:7700/projects`.
3. Claim **yes** only when **all** are true:
   - BuildMonitor is reachable;
   - a project `rootFolder` equals that exact path (full path; trailing
     separators ignored; case-insensitive);
   - the project can run the requested operation (`/run/rebuild`, `/run/tests`,
     `/run/ship-check`, or `/projects` for status).
4. If there is no exact `rootFolder`, and the folder shares a Git common
   directory with an already-claimed **manual** parent (`derivedFromProjectId`
   empty), registration is required before build. Follow
   [Worktree lifecycle](#worktree-lifecycle). After `POST /projects/register-worktree`
   succeeds, re-claim that exact path. If that register fails, announce
   `BuildMonitor: register failed — <error>` and stop. **No silent direct-dotnet
   fallback.**
5. If no claimed parent exists for that repository, **decline cleanly**.
   Announce `Verification: direct-dotnet` plus why (unreachable / no exact
   claim / operation unsupported). **Do not auto-configure.** Unconfigured is
   valid when this machine has no claimed parent. BuildMonitor remains optional
   for developers without a claimed parent.

## Exclusive ownership

When claimed, announce `Verification: buildmonitor-control-plane`. You
**exclusively** own compilation, tests, status, and final verification.
Do not also run direct `dotnet build` or `dotnet test`.

| Contract operation | This adapter |
|--------------------|--------------|
| begin-session | `POST /mode` `ai-controlled` + `POST /session/busy` |
| rebuild | `POST /run/rebuild` |
| tests | `POST /run/tests` (optional `filter`) |
| ship-check (final local verification) | `POST /run/ship-check` — fresh build **and** tests |
| status | `GET /projects` |
| end-session | `POST /session/idle` (does **not** mean tests passed) |

Wait for each HTTP JSON body. Treat `ok: false` or `outcome` other than
`succeeded` as failure. Do not overlap `/run/*`. HTTP **409** means busy: wait,
recheck `GET /projects` / `GET /session`, then retry once.

## Lifecycle

```text
discover + exact claim
GET /mode → POST /mode ai-controlled if needed
POST /session/busy
edit files
POST /session/idle
POST /run/rebuild | /run/tests | /run/ship-check
inspect ok / outcome
```

- Confirm **AI Controlled** for agent edits; leave it set.
- `/session/idle` does not build.
- Final local verification is **`/run/ship-check`**, not a second `dotnet test`.

## Chat announcements

| Event | Announce |
|-------|----------|
| Selected | `Verification: buildmonitor-control-plane` |
| Declined | `Verification: direct-dotnet` plus reason |
| Mode | `Verification: AI Controlled` |
| Busy / idle | `Verification: busy — editing` / `idle — awaiting explicit build` |
| Starting | `Verification: rebuild…` / `tests…` / `ship-check…` |
| Finished | `Verification: <op> — pass` or `— fail` |

Do **not** stay silent on handshake or `/run/*`. Do **not** invent MCP.

# BuildMonitor control plane handshake

BuildMonitor is a separate tray app that watches configured folders.
Talk to it over **loopback HTTP only**. **Do not invent MCP** — HTTP only.

Projects have an explicit **build-control mode**:

| Mode | Wire value | Auto-build on file change |
|------|------------|---------------------------|
| File Watching | `file-watching` | Yes (debounced; held while busy) |
| AI Controlled | `ai-controlled` | **Never** — observe only |

## Efficient workflows (pick the smallest call)

| Scenario | Workflow |
|----------|----------|
| Edit burst (AI Controlled) | ensure mode → `busy` → edit → `idle` → `/run/rebuild` if needed |
| One or a few tests | `/run/tests` with `filter` |
| Full verification | `/run/ship-check` |
| Locked DLLs / bad incremental | `/run/rebuild` |

**Test filters:** `FullyQualifiedName=Ns.Class.Method` (one), `FullyQualifiedName~Ns.Class` (class/range), omit `filter` (all).

**Anti-patterns:** `idle` mid-edit; rebuild every burst; assuming idle means tests passed; overlapping `/run/*` (409); File Watching during agent edits; silent handshake; long `AwaitShell` after `/run/*` finished.

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

Do this once per chat (or again if the worktree root changes).

1. **Discovery file (preferred)** — if it exists, read:

   `%LocalAppData%\BuildMonitor\control-plane.json`

   Use `baseUrl` when `enabled` is true. Claim a project only when `rootFolder`
   **exactly** matches the folder being edited. Parent/child/sibling paths are
   **not** a claim.

2. **Probe (fallback)** — if the file is missing or `enabled` is false:

```powershell
try { Invoke-RestMethod "http://127.0.0.1:7700/projects" } catch { $null }
```

3. **Cache** `baseUrl` and `projectId` for this worktree. Recheck when the root changes.

4. If no exact project: run the [worktree lifecycle](#worktree-lifecycle)
   register step when a claimed manual parent shares the Git common directory.
   If no such parent exists, decline. Do not handshake. Product-repo
   `direct-dotnet` fallback applies only in that case.

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
| POST | `/projects/register-worktree` | `{ "parentProjectId", "worktreePath" }` — see Derived worktrees |
| POST | `/projects/unregister-worktree` | `{ "projectId" }` (+ optional `worktreePath`) — see Derived worktrees |

Treat `ok: false` as failed verification — read `failures` / `log` / `outcome`. HTTP **409**: wait and recheck status; do not start a parallel `dotnet` command.

## Worktree lifecycle

BuildMonitor is **local personal tooling** for this machine's user settings
(`%LocalAppData%\BuildMonitor\settings.json`). It is **not** a repository
capability. Do **not** add BuildMonitor files, rules, or skills to
WitherbyConnect or any other product repo. The product does not scan Git.
`VerificationProviderClaim` still declines an unregistered exact path; this
adapter registers first, then claims again.

Normal shape on a configured machine: **main + genuinely active worktrees**.
Finished issues are unregistered after merge. Do not bulk-delete old worktrees
merely because they are old.

### Start — registration required before build

When the folder being edited is a Git worktree of the same common directory as
an already-claimed manual parent:

1. Parent id: the claimed project with empty `local.derivedFromProjectId` whose
   root shares `git rev-parse --git-common-dir`. If several manual projects
   match, use the primary checkout (`.git` is a directory).
2. Skip register when that exact path is already claimed.
3. Otherwise, **before** the first build, test, or launch:

```powershell
Invoke-RestMethod -Method Post -Uri "$base/projects/register-worktree" -ContentType "application/json" `
  -Body (@{ parentProjectId = $parentProjectId; worktreePath = $exactWorktreePath } | ConvertTo-Json)
```

BuildMonitor derives Local (and Azure) settings from the parent, allocates a
non-colliding `applicationUrl` port, marks `derivedFromProjectId`, and persists
**only** in local user settings. The derived project has its own project id,
its own build directory under that `rootFolder`, and its own localhost port.
Registration does **not** start the app host (`startOnLaunch` is forced false).
Announce `BuildMonitor: registered worktree` (or note `alreadyRegistered`).

4. Re-claim by the **new** `projectId` / exact `rootFolder`. The exact path
   becomes claimable only after that register succeeds. Then use mode → busy →
   edit → idle → `/run/*`. Do not reuse another worktree's build directory,
   running host, or port.

If registration was expected but the call fails: announce
`BuildMonitor: register failed — <error>`. **No silent direct-dotnet fallback.**

If no claimed parent exists, do not register. Direct-dotnet remains valid.
**Do not auto-configure.**

### During work

Always `/run/*` against the exact registered worktree `projectId`.

### Finish — unregister only after merge

Do **not** unregister or `git worktree remove` because of a commit, a push, or
green validation while the pull request is still **active**. An active PR means
no unregister and no worktree removal.

Cleanup gate: pull request **merged/completed** and required validation/build
green. Then, for that derived project only:

```powershell
Invoke-RestMethod -Method Post -Uri "$base/run/stop" -ContentType "application/json" `
  -Body (@{ projectId = $derivedProjectId } | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri "$base/projects/unregister-worktree" -ContentType "application/json" `
  -Body (@{ projectId = $derivedProjectId; worktreePath = $exactWorktreePath } | ConvertTo-Json)
```

Confirm `ok` with `removed` or `alreadyRemoved`. Announce
`BuildMonitor: unregistered worktree`.

Unregister removes the derived BuildMonitor project only. It does **not** run
`git worktree remove`. A project with no `derivedFromProjectId` (main / manual)
**must never be automatically unregistered**. The API refuses those.

**Failed unregister blocks worktree removal.** Announce
`BuildMonitor: unregister failed — <error>` and do not delete the folder.

Only after unregister succeeds, from another checkout:

```powershell
git worktree remove <exact-worktree-path>
git worktree prune
```

If `git worktree remove` fails because something still holds the folder: do not
kill arbitrary processes. Report the lock. Leave folder removal pending.

A pull request closed or abandoned without merge: ask Simon before deleting
the worktree.

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
- Always announce handshake and `/run/*` in chat.
- Never invent MCP tools for BuildMonitor.
- When a claimed manual parent shares the Git common directory, registration
  is required before build. Do not register any other repository.
- Unregister only after the pull request is merged/completed, and only for that
  derived project. Unregister never deletes the Git worktree directory.
- **Do not auto-configure** when no claimed parent exists.
