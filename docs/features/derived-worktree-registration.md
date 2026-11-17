# Derived worktree registration

Control-plane support for registering Cursor-created Git worktrees as isolated
BuildMonitor projects, derived from an already-claimed parent.

## Behaviour

| Step | What | Where |
|------|------|--------|
| 1 | Validate parent Local project + worktree path + same `git` common-dir | `ProjectOrchestrator.DerivedWorktree.cs`, `GitWorktreeIdentityReader` |
| 2 | Clone settings; remap paths; `startOnLaunch=true`; set `derivedFromProjectId` | `DerivedWorktreeProjectFactory` |
| 3 | Allocate non-colliding `applicationUrl` | `ProjectPortIsolation` |
| 4 | Persist settings + `ApplySettings` + cold-start host when StartOnLaunch | `ProjectOrchestrator` |
| 5 | Unregister: refuse if exclusive `/run/*` busy → `/run/stop` → remove derived only | `UnregisterDerivedWorktreeAsync` |
| 6 | Stale scan (30 s after start, then every 5 min): folder gone, no live `.git`, or parent id missing | `DerivedWorktreeStaleness`, `DerivedWorktreeFolderProber`, `ProjectOrchestrator.StaleWorktrees.cs` |
| 7 | Toast once per newly stale project; tray **Finished worktrees (N)** offers Unregister / Unregister all after confirmation | `TrayContextMenuBuilder`, `App.UnregisterStaleWorktrees` |
| 8 | User-confirmed unregister re-checks staleness, then calls step 5 with the registered `rootFolder` | `UnregisterStaleDerivedWorktreesAsync` |

**Extension points:** `IGitWorktreeIdentityReader`, `SetDerivedWorktreeFolderProbe` (test seams).  
**Failure / fallback:** validation → HTTP 400; busy unregister → 409; unknown id unregister → `alreadyRemoved`. A folder that cannot be read is treated as healthy, so it is never offered for removal. Merged or completed pull requests are not detected (Azure DevOps squash merges defeat Git ancestry checks).

## Agent contract

See [ops/control-plane.md](../ops/control-plane.md#derived-worktrees-cursor-git-worktrees)
and the install-source skill under `docs/ops/agent-skills/buildmonitor-control-plane/`.

Unregister never deletes the Git worktree directory. The stale scan never unregisters
by itself and never considers manual projects (no `derivedFromProjectId`).
