# ADR 0004: Derived worktrees start on launch by default

**Status:** Proposed  
**Date:** 2026-10-01

## Context

[ADR 0003](0003-derived-worktree-registration.md) forced `startOnLaunch: false` on
control-plane-registered Git worktrees so registration would not start hosts or
bind ports for every derived folder. With unregister cleanup in place, finished
worktrees no longer accumulate indefinitely. Active feature worktrees still
required a manual Rebuild & restart (or tray cold start of only non-derived
projects) before a listen URL appeared.

Simon wants derived registrations to behave like other Start-on-launch projects:
easy to open in the browser while working on that worktree.

## Decision

1. `DerivedWorktreeProjectFactory` sets `startOnLaunch: true`.
2. Successful `POST /projects/register-worktree` cold-starts that derived project
   when StartOnLaunch and RunMode are runnable (same `StartAsync` path as tray
   cold start).
3. Schema **v26** migrates existing projects with `derivedFromProjectId` to
   `startOnLaunch: true` on settings load.
4. Manual/main projects without `derivedFromProjectId` are not changed by the
   migration.

This supersedes ADR 0003 decision item 3 (`force startOnLaunch: false`).

## Consequences

**Positive:**

- Newly registered and migrated derived worktrees show a site URL after register
  or tray launch without a manual Rebuild & restart.
- Matches the MountFresh cold-start behaviour for StartOnLaunch projects.

**Trade-offs:**

- Several active derived worktrees can start hosts on tray launch (still gated
  by `maxConcurrentActiveProjects` for cold `StartActiveProjectsAsync`; register
  starts the newly registered project immediately).
- Unregister remains required after merge so finished worktrees do not keep
  starting.

## References

- Code: `src/Core/Rules/DerivedWorktreeProjectFactory.cs`,
  `src/Core/Rules/SettingsSchemaV26.cs`,
  `src/Infrastructure/Services/ProjectOrchestrator.DerivedWorktree.cs`
- Docs: `docs/features/derived-worktree-registration.md`, `docs/ops/control-plane.md`
- Supersedes: [0003-derived-worktree-registration](0003-derived-worktree-registration.md) § Decision item 3
