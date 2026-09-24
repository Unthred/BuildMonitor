# ADR 0003: Control-plane derived Git worktree registration

**Status:** Proposed  
**Date:** 2026-09-24

## Context

Cursor often creates isolated Git worktrees for feature work. BuildMonitor claims
only **exact** configured `rootFolder` paths. A new worktree therefore falls back
to `direct-dotnet` until configured, which can let Cursor manage ports and
`dotnet` processes alongside an already-claimed parent checkout.

Requirements:

- Opt-in registration from an already-claimed parent (not magic claim).
- Persist only in `%LocalAppData%/BuildMonitor/settings.json`.
- Never modify the product repository or require consumer-repo BuildMonitor files.
- Isolate ports via existing `ProjectPortIsolation` / `applicationUrl`.
- Unregister must stop the managed host and remove BuildMonitor state only —
  never `git worktree remove`.

## Decision

1. Add `POST /projects/register-worktree` and `POST /projects/unregister-worktree`
   to the loopback control plane.
2. Validate same-repository identity with absolute `git rev-parse --git-common-dir`
   (not folder naming).
3. Derive Local (+ Azure) settings from the parent; force `startOnLaunch: false`
   so registration does not start the host.
4. Persist schema **v25** optional `local.derivedFromProjectId` so unregister
   refuses to delete manually configured projects.
5. Allocate ports with `ProjectPortIsolation.Decide` against configured peers and
   live runtime listen URLs; persist on the derived project only.

## Consequences

**Positive:**

- Feature worktrees get the same exclusive BuildMonitor lifecycle as main clones.
- Safe unregister for ship cleanup without touching Git directories.
- Idempotent register/unregister for agent retries.

**Trade-offs:**

- Agents must call register explicitly after creating a worktree.
- Derived projects count toward Active-in-session lists; `MaxConcurrentActiveProjects`
  still only gates cold `StartOnLaunch` starts.
- Requires a working `git` on PATH for identity checks.

## References

- [ops/control-plane.md](../ops/control-plane.md#derived-worktrees-cursor-git-worktrees)
- [features/derived-worktree-registration.md](../features/derived-worktree-registration.md)
- Issue #152
