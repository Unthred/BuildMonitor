# Failure details (Local build / tests)

Authoritative **current** failure context answers “why is this unhealthy now, and what can I do?”  
Distinct from activity (#112) and operational history (#110).

Feature: [#111](https://github.com/Unthred/BuildMonitor/issues/111) — slice **#111a** (Local build + Local tests only).

## Purpose

| Concern | Mechanism |
|---------|-----------|
| **What is happening now?** | `ProjectActivitySet` (#112) |
| **Why unhealthy now?** | `ProjectFailureDetails` (#111) |
| **What happened?** | `OperationalEvent` stream (#110) |
| **Health / rollup** | `MonitorHealth` + composers (unchanged) |

## Current-state authority

Failure reasons are selected **only** from current Local authoritative state:

| Source | Included when |
|--------|----------------|
| Local build | `LastBuildExitCode` is a failed build **and** state is not Building / WaitingForEdits / Testing |
| Local tests | `ProjectLifecycleState.TestFailed` |

A healthy project with old Failed history shows **no** Failure details card — history stays under Recent activity only.

## History enrichment

Operational History may **enrich** a current reason when identifiers match:

| Source | Match keys (prefer in order) |
|--------|------------------------------|
| Build | `LocalBuildNumber`, `BuildTriggerId`, `OperationId` |
| Tests | `OperationId` only (no loose “latest failed test” heuristic) |

Unmatched stale Failed events never become the primary card.

## Local test structured data

`ProjectHealthSnapshot.LastTestFailure` (`LocalTestFailureSnapshot`) carries:

- failed / skipped counts
- up to 3 failing test names
- optional first assertion/message (no stacks)
- `OperationId` for history match

Cleared when a new test run starts or tests succeed.

## Build error compactifier

`BuildErrorCompactFormatter` formats known `CS` / `MSB` / `NU` lines as:

`CS1061 · Foo.cs:42 · message`

Unknown shapes keep a trimmed raw one-line preview. Not a general log summarizer.

## Presentation order (#111a)

1. Local build (Primary when both current)
2. Local tests (additional)

This is presentation order only — not a claim about root cause. Azure / run-host reasons are deferred (#111b / #111c).

## Status UI

- Compact **Failure details** block on the status panel card, **above** Recent activity.
- Primary reason always visible; additional concurrent reasons behind a short expander.
- Context actions sit with each reason (not a second generic toolbar).
- Legacy raw `ErrorPreview` is suppressed when Failure details are present (avoids duplication).
- Activity / accent rail (#112) and overall health footer are unchanged.

## Actions

| Reason | Actions |
|--------|---------|
| Build | Open build log, Copy errors, Rebuild, Rebuild & restart (when run host supported) |
| Tests | Open test log, Run tests |

## Fallbacks

| Source | Fallback |
|--------|----------|
| Build, no preview | `Build failed` / `Open build log for details` |
| Tests, no names/counts | `Tests failed` / `Open test log for details` |

Never show blank cards or raw enum names.

## Visual QA (#111a)

Manual status-panel checks (prefer non-destructive failures):

| Scenario | Expect |
|----------|--------|
| Local build failure | Failure details above Recent activity; compact CS/MSB line when available; Open build log / Copy errors / Rebuild |
| Test failure | `N tests failed` + up to 3 names; Open test log / Run tests |
| Concurrent build+test | Build primary; Tests under “Also …” |
| Fallback / no preview | `Open build log for details` / `Open test log for details` |
| Healthy + old Failed history | No Failure details; history only under Recent activity |
| Min width / 2-project density | Card stays usable; secondary reasons collapsed |
| Activity + failure + history | All three visible; activity not overwritten |

Do not force risky production failures solely for QA.

## Deferred

- Azure CI / availability failure cards (#111b)
- Run-host crash cards (#111c)
- Tray tooltip one-liner polish beyond existing `LastErrorPreview`
