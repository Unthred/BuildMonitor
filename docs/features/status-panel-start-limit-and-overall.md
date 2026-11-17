# Status panel: start limit and Overall reason

Issue [#167](https://github.com/Unthred/BuildMonitor/issues/167). Two things the
status panel must always explain: why an active project is idle because of the
start limit, and why Overall says **Attention**.

## Start limit (`monitor.maxConcurrentActiveProjects`)

Every **automatic** start applies the same limit. A project held back is never
silent: its card, `GET /projects` and a tray notification say why.

### Behaviour

| Step | What | Where |
|------|------|-------|
| 1 | Count projects already started this session (automatic or user Start) | `ProjectOrchestrator.startedProjectIds` |
| 2 | Tray launch: take StartOnLaunch projects in settings order that fit; hold back the rest | `ProjectOrchestrator.StartActiveProjectsAsync`, `ProjectStartConcurrencyLimit.Split` |
| 3 | Settings Save cold start of a newly activated project (`MountFresh`) | `ProjectOrchestrator.RemountLocalProjectsWithoutBuildAsync` |
| 4 | Derived-worktree cold start on `POST /projects/register-worktree` | `ProjectOrchestrator.RegisterDerivedWorktreeAsync` |
| 5 | Held-back runtime gets `NotStartedReason` ("Not started: limit of 5 active projects reached") | `ProjectRuntime.MarkNotStarted` |
| 6 | One warning notification per batch naming the held-back projects | `ProjectOrchestrator.DeferForConcurrencyLimit` |
| 7 | Card: current action shows the reason, Overall shows **Not started**, **Start** button | `StatusPanelPresentationBuilder`, `StatusPanelCardActionRules.ShowStart` |
| 8 | **Start** runs the normal cold start (build, then run/watch) and ignores the limit | `ProjectOrchestrator.StartProjectAsync` |

Control plane: `local.notStartedReason` on `GET /projects`, and
`notStartedReason` on the register-worktree response, are set while the project
is held back. `local.lifecycleState` stays `idle`.

**Extension points:** `ProjectOrchestrator.SetRuntimeStarter` (test seam that
replaces `ProjectRuntime.StartAsync`).

**Failure and fallback:** a stopped or unregistered project frees its slot, but
held-back projects do not start on their own when a slot frees; use **Start** or
restart the tray. Raising the limit in Settings takes effect on the next
automatic start.

## Overall reason

The Overall headline stays **Attention** for amber health (never "Warnings").
`StatusPanelOverallFormatter.Summarize` adds a short cause and a tooltip.

| Cause (highest first) | Reason text |
|-----------------------|-------------|
| Azure sign-in required | `sign-in required` |
| Azure unreachable | `CI unreachable` |
| Local build warnings (no errors) | `14 warnings` (summed across projects on the rail) |
| Azure run partially succeeded | `CI partially succeeded` |

- Card footer: `Overall ● Attention · 14 warnings`; tooltip lists each cause.
- Side rail: `Attention` with the reason on a smaller second line; the rail
  tooltip lists `Project: cause` for every amber project.
- Building or CI activity still shows **Building** unless sign-in or
  reachability is the problem.
- `FormatLabelFromHealth` (health only, no snapshots) still returns plain
  `Attention`.
