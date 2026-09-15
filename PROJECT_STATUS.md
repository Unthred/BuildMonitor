# BuildMonitor Project Status

## Status

**Feature-complete / maintenance mode**

As of **2026-09-15**, `main` at merge SHA
`59cfadcdcf199633ec4e755024e351192d87d925` (#140 cancel in-flight `/run/*`)
is the shipped baseline.

- There are **no currently planned implementation issues**.
- An **empty Todo column** on [project board #3](https://github.com/users/Unthred/projects/3) is **intentional**, not missing planning.
- New work should be driven by observed usage pain or genuinely new requirements — not speculative architecture.

## Purpose

BuildMonitor is a personal Windows developer companion that gives **authoritative visibility** and **governed control** over local .NET build / test / run workflows, plus Azure DevOps CI state — especially for AI/agent-driven editing of watched repos.

It is not a general CI product, not a team dashboard, and not a substitute for Azure DevOps itself.

## Completed capability map

| Area | What exists |
|------|-------------|
| Local build / watch lifecycle | Debounced file builds, edit-gating, supervised `dotnet run` / `watch` |
| AI Controlled mode | Explicit agent mode; no auto-build on idle / busy timeout |
| Run-host desired state (#106) | Temporary pause vs explicit stop; resume rules after rebuild / tests / ship-check / cancel |
| Status / tray presentation | Traffic-light tray, hover status panel, Local + Azure BUILDS peers |
| Operational history (#110+) | Bounded JSONL “what happened?” stream + Recent activity UI |
| Activity / progress (#112) | Ephemeral “what is happening now?” from Local / Agent / Azure |
| Actionable failure details (#111) | “Why unhealthy now?” with Local / Azure / RunHost precedence |
| Azure monitoring | Continuous Builds polling, primary-run selection, tray + `/projects` facets |
| Azure live stage / job (#138) | Timeline fetch only while primary run active; projected into #112 Azure activity |
| Control plane | Loopback HTTP: session, mode, `/projects`, `/run/*`, `/app/quit` |
| Typed operation outcomes (#136) | `outcome` on rebuild / tests / ship-check; `ok == true` iff succeeded |
| Operation identity (#134 / #140) | Lease-owned `operationId` on Agent activity |
| Explicit in-flight cancel (#140) | `POST /run/cancel`; terminal `outcome:"cancelled"`; failure-before-cancel precedence; two-phase lease retirement |
| Deploy / ship discipline | Release deploy to `C:\Utils\BuildMonitor` with provenance; ship-check as verification gate |

Issue numbers above are historical anchors, not a backlog.

## Authoritative concepts

| Question | Authority |
|----------|-----------|
| **What is happening now?** | Activity (`ProjectActivitySet` / `/projects.activities`) |
| **Why is it unhealthy now?** | Failure details |
| **What happened?** | Operational history |
| **How did requested work finish?** | Typed `outcome` on `/run/*` **200** bodies |
| **Was the request accepted / dispatched?** | HTTP status (`200` / `409` / …) — distinct from terminal outcome |
| **Should the supervised host be running?** | `DesiredRunHostState` (#106) |
| **Who owns in-flight `/run/*` identity and cancel?** | Per-project `ControlPlaneOperationLease` |

**History is observability, never control flow.** History may correlate to a lease `operationId`; it must not own cancellation, exclusivity, or execution decisions.

Intentional agent cancellation is **not** a health failure and must not appear as Red / failure-details solely because of cancel.

## Current agent control lifecycle

```text
start
→ observe          (GET /projects — health + activities)
→ identify         (activities[].operationId when Agent work is active)
→ optionally cancel (POST /run/cancel { projectId, operationId? })
→ classify terminal result (outcome on original /run/* 200)
→ recover / settle (resume host per DesiredRunHostState; clear activity)
```

After settlement, the next `/run/*` gets a **fresh** lease and `operationId`. Prior cancellation must not poison the next operation.

Cancel signal acceptance (`cancelRequested: true` on `/run/cancel`) is **not** the same as operation success. The long-lived `/run/*` response carries the authoritative terminal `outcome`.

## Intentional non-goals / future-triggered ideas

These are **not required for current completeness**. Potential future work **only if real usage justifies it** — do not file issues preemptively:

- UI Cancel button on the status panel
- Cancelling Azure DevOps runs from BuildMonitor
- Full Azure timeline / stage-tree UI
- Live log streaming over HTTP
- Richer history browser / filters
- Toast / OS notifications
- Additional presentation polish (tooltips, tray menu log shortcut, etc.)
- Speculative debounce / diagnostics learning loops

Deliberate “not available” control-plane gaps that remain by design:

- Disable file watcher entirely via API (`busy` holds builds instead)
- Stream live build log over HTTP (read `log` path from `/run/*` responses)

## Maintenance policy

Create new work only when one of these is true:

1. A bug or regression is observed
2. Current workflow produces repeated friction
3. A new external dependency or API requires adaptation
4. A genuinely new user requirement appears

Avoid speculative architecture work, drive-by refactors, and “nice to have” board cards.

## Verification baseline (closeout)

| Item | Value |
|------|-------|
| Latest merge SHA | `59cfadcdcf199633ec4e755024e351192d87d925` |
| Branch | clean `main` |
| Deploy | Release → `C:\Utils\BuildMonitor` (`Dirty: false`, `CommitBranch: main`) |
| Most recent full ship-check | **1193** passed (pre-merge verification for #140) |
| Control plane | Operational on loopback `:7700` |
| Tray | Operational |

If later merges supersede this SHA, treat this table as the **closeout snapshot**, not a live CI badge.

## Where to read next

| Doc | Role |
|-----|------|
| [README.md](README.md) | Quick start |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System as shipped |
| [docs/ops/control-plane.md](docs/ops/control-plane.md) | Agent HTTP contracts |
| [docs/features/activity-and-progress.md](docs/features/activity-and-progress.md) | Activity authority |
| [docs/features/failure-details.md](docs/features/failure-details.md) | Failure authority |
| [docs/features/operational-history.md](docs/features/operational-history.md) | History (observability only) |
| [docs/SETTINGS.md](docs/SETTINGS.md) | Settings keys |
| [docs/ops/local-deploy.md](docs/ops/local-deploy.md) | Release deploy |
