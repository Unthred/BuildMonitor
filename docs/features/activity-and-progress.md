# Activity and progress model

Authoritative **ephemeral** per-project activity answers “what is it doing right now?”  
Distinct from operational history (“what happened?” — [#110](https://github.com/Unthred/BuildMonitor/issues/110)).

Feature: [#112](https://github.com/Unthred/BuildMonitor/issues/112).

## Purpose

| Concern | Mechanism |
|---------|-----------|
| **What is happening now?** | `ProjectActivitySet` / `ProjectActivitySnapshot` (#112) |
| **Why unhealthy now?** | `ProjectFailureDetails` (#111) — see [failure-details.md](failure-details.md) |
| **What happened?** | `OperationalEvent` stream (#110 / #113–#116) |
| **Health / rollup** | `MonitorHealth` + composers (unchanged precedence) |
| **Tray mascot** | Coarse state only — no progress on the 16px icon |

## Principles

- Never invent fake percentages or elapsed-time estimates.
- Numeric progress (`ActivityProgress`) only when the underlying operation exposes trustworthy current **and** total counts.
- Local and Azure activity may coexist; Local/Agent is primary for the accent rail; coexistence text can list both.
- Activity must not hide Red/Failed health.
- Reuse existing Local lifecycle, control-plane phases, and Azure poll facets — no parallel polling.
- Azure stage/job names require timeline fetch (failure navigation only). Until that data is already on the facet, Azure activity uses pipeline + run state (`queued` / `in progress` / `canceling`), not invented stage labels.

## Model

| Type | Role |
|------|------|
| `ActivitySourceKind` | Local / Azure / Agent / System |
| `ActivityPhaseKind` | Building, Testing, WaitingForEdits, ShipCheck, AzureInProgress, … |
| `ActivityProgress` | Optional `(Current, Total)` with derived fraction only when `Total > 0` |
| `ProjectActivitySnapshot` | One active (or cleared) activity for a source |
| `ProjectActivitySet` | All activities for a project + `Primary` + optional coexistence summary |

Builder: `ProjectActivityBuilder.Build(snapshot, utcNow)`.

## Status UI

- Side-rail accent label: `ProjectActivityBuilder.FormatRailLabel` (via `StatusPanelAccentFormatter`).
- Card presentation carries `Activity` (`ProjectActivitySet`).
- Coexistence / Azure-only summaries may populate `CurrentActionText` when no stronger transient action exists.
- Progress charts (`BuildProgressStep`) remain the detailed Local build step UI; the activity model supplies the short “now” text.

## Test progress (`N / Total`)

| Source | Mid-run | End of assembly |
|--------|---------|-----------------|
| VSTest console result lines (`Passed` / `Failed` / `Skipped` … `[N ms]`) | Authoritative **completed** count | Continues |
| VSTest/legacy summary line (`Passed! - … Total: N`) | Rarely before completion | Sets `Total` + aggregate completed |
| Discovery total before execution | **Not available** via current `dotnet test --logger console;verbosity=detailed` | — |
| Previous-run totals / elapsed-time estimates | Rejected | Rejected |

`FormatTestingStatus`:

- `Running tests · 318 / 940` — only when `ActivityProgress.TryCreate` has a trustworthy total
- `Running tests · 318 completed` — when completed count is known but total is not
- `Running tests` — phase-only

Live counters flow: `OnTestOutputLine` → `DotNetTestLiveProgressTracker` → `ProjectHealthSnapshot.TestProgress` → `ProjectActivityBuilder` (via existing `RequestHealthCoalesce(immediate: false)`). No operational-history events per progress tick.


## Non-goals

- Changing health rollup or tray-icon semantics.
- Timeline polling for live Azure stages.
- Duplicating operational history.
- Source-label polish (`L Local` / `U User`) — optional later with history UI.
