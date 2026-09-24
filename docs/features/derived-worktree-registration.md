# Derived worktree registration

Control-plane support for registering Cursor-created Git worktrees as isolated
BuildMonitor projects, derived from an already-claimed parent.

## Behaviour

| Step | What | Where |
|------|------|--------|
| 1 | Validate parent Local project + worktree path + same `git` common-dir | `ProjectOrchestrator.DerivedWorktree.cs`, `GitWorktreeIdentityReader` |
| 2 | Clone settings; remap paths; `startOnLaunch=false`; set `derivedFromProjectId` | `DerivedWorktreeProjectFactory` |
| 3 | Allocate non-colliding `applicationUrl` | `ProjectPortIsolation` |
| 4 | Persist settings + `ApplySettings` (mount runtime; **no** host start) | `ProjectOrchestrator` |
| 5 | Unregister: refuse if exclusive `/run/*` busy → `/run/stop` → remove derived only | `UnregisterDerivedWorktreeAsync` |

**Extension points:** `IGitWorktreeIdentityReader` (test seam).  
**Failure / fallback:** validation → HTTP 400; busy unregister → 409; unknown id unregister → `alreadyRemoved`.

## Agent contract

See [ops/control-plane.md](../ops/control-plane.md#derived-worktrees-cursor-git-worktrees)
and the install-source skill under `docs/ops/agent-skills/buildmonitor-control-plane/`.

Unregister never deletes the Git worktree directory.
