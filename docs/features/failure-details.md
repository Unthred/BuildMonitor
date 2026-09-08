# Failure details (Local + Azure)

Authoritative **current** failure context answers “why is this unhealthy now, and what can I do?”  
Distinct from activity (#112) and operational history (#110).

Feature: [#111](https://github.com/Unthred/BuildMonitor/issues/111) — slices **#111a** (Local) and **#111b** (Azure).

## Purpose

| Concern | Mechanism |
|---------|-----------|
| **What is happening now?** | `ProjectActivitySet` (#112) |
| **Why unhealthy now?** | `ProjectFailureDetails` (#111) |
| **What happened?** | `OperationalEvent` stream (#110) |
| **Health / rollup** | `MonitorHealth` + composers (unchanged) |

## Current-state authority

Failure reasons are selected **only** from current authoritative state:

| Source | Included when |
|--------|----------------|
| Local build | `LastBuildExitCode` is a failed build **and** state is not Building / WaitingForEdits / Testing |
| Local tests | `ProjectLifecycleState.TestFailed` |
| Azure CI | `ProjectAzureHealthFacet` is **Available** and `PrimaryRun` is completed **Failed** / **PartiallySucceeded** matching `CiState` |
| Azure availability | Facet `Availability` is **AuthRequired** or **Unavailable** |

A healthy project with old Failed history shows **no** Failure details card — history stays under Recent activity only.

**Do not** infer Azure failure from Operational History alone. History may enrich a reason only after the facet selects it.

### Azure availability vs CI

Auth / network problems are **not** CI failure:

| Availability | Title | Severity |
|--------------|-------|----------|
| AuthRequired | `Azure sign-in required` | Warning |
| Unavailable | `Azure monitoring unavailable` | Warning |

Do not fabricate a failed run, stage, or pipeline when Azure is merely unavailable.

### PartiallySucceeded

Matches existing health semantics (`AzureCiMonitoringState.Warning`):

- Severity **Warning**
- Wording **`Azure build partially succeeded`** (not simply “failed”)

## History enrichment

Operational History may **enrich** a current reason when identifiers match:

| Source | Match keys (prefer in order) |
|--------|------------------------------|
| Build | `LocalBuildNumber`, `BuildTriggerId`, `OperationId` |
| Tests | `OperationId` only (no loose “latest failed test” heuristic) |
| Azure | `OperationalEvent.AzureRunId == PrimaryRun.RunId` only |

Unmatched stale Failed events (including old Azure runs for a now-successful current run) never become the primary card.

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

## Presentation order

1. Local build (Primary when present)
2. Local tests
3. Azure CI
4. Azure availability

This is presentation order only — not a claim about root cause. Run-host reasons are deferred (#111c).

When Local and Azure are both unhealthy, **both** appear. Local stays Primary under the locked order; Azure is an additional compact reason (e.g. `Azure · #553 failed`). Do not hide Azure merely because Local failed.

### Attention runs

`AttentionRuns` must not become a second fake primary. When the PrimaryRun already owns the Azure CI reason, attention is a compact detail line such as `1 other pipeline needs attention`. If Primary is healthy/active but `CiState` is still Failed/Warning from other pipelines, a single Warning reason may surface that attention line.

## Status UI

- Compact **Failure details** block on the status panel card, **above** Recent activity — same chrome for Local and Azure (no separate Azure failure card).
- Primary reason always visible; additional concurrent reasons behind a short expander.
- Failure details keep lightweight links only.
- Rebuild / Restart / Rebuild & restart / Tests live on the **card action row** (capability-driven) so recovery buttons are not duplicated.
- Legacy raw `ErrorPreview` is suppressed when Failure details are present (avoids duplication).
- Activity / accent rail (#112) and overall health footer are unchanged.
- Existing Azure BUILDS row / Open in Azure DevOps navigation stays unchanged; Failure details uses explicit labels (`Open Azure run` / `Open failure logs`).

## Azure actions and lazy navigation

| Action | When |
|--------|------|
| Open Azure run | Run URL known (facet `RunUrl` or deep-link from navigation context) |
| Open failure logs | Only when `AzureBuildSourceNavigationBuilder` supplies a `FailureRequest` (Failed / PartiallySucceeded) |

**Navigation-context contract:** `FailureRequest` is only built when `NavigationContext` is present. That same context always yields a valid run-results deep-link when `RunUrl` is missing — BuildMonitor does not invent a broken URL. Conversely, a Failed run with `RunUrl` but **no** `NavigationContext` shows **Open Azure run** only (no failure-log action).

**Lazy timeline rule:** rendering Failure details must **not** call Azure timeline / stage APIs. Timeline fetch and stage/job/task resolution happen only when the user clicks **Open failure logs**, via the existing `AzureFailureNavigationResolver` / `IBuildSourceLinkOpener.OpenFailureDetailsAsync` path.

If deeper failure resolution cannot identify a stage/job/task, the resolver already falls back to the run / logs page. Never show a dead action — hide **Open failure logs** when unsupported; keep **Open Azure run** when a URL exists.

**No extra Azure poller** and **no timeline fetch** on normal status refresh for this card.

## Card toolbar actions

| Action | When shown |
|--------|------------|
| Rebuild | Active Local project (does not require a run host) |
| Restart | Active + supervised run host (`SupportsAppRestart` / RunMode ≠ None) |
| Rebuild & restart | Rebuild + Restart both available |
| Tests | Active Local project |

Self-host note: Rebuild for BuildMonitor.TrayApp builds the watched source tree; it does **not** replace the deployed `C:\Utils\BuildMonitor` binaries. Restart is hidden when RunMode is None.

## Fallbacks

| Source | Fallback |
|--------|----------|
| Build, no preview | `Build failed` / `Open build log for details` |
| Tests, no names/counts | `Tests failed` / `Open test log for details` |
| Azure CI | Run id + branch / pipeline when available |
| Azure availability | Facet `StatusMessage` when present |

Never show blank cards or raw enum names.

## Visual QA (#111a / #111b)

Manual status-panel checks (prefer non-destructive failures; use fixture presentation when live Azure failure is unavailable):

| Scenario | Expect |
|----------|--------|
| Local build failure | Failure details above Recent activity; compact CS/MSB line when available; Open build log / Copy errors |
| Test failure | `N tests failed` + up to 3 names; Open test log |
| Concurrent build+test | Build primary; Tests under “Also …” |
| Azure-only failed | `Azure build failed` + run/branch; Open Azure run / Open failure logs |
| Local healthy + Azure failed | Azure primary under Failure details |
| Local failed + Azure failed | Local primary; compact Azure additional |
| AuthRequired | Warning availability; no fake CI run |
| Unavailable | Warning availability; no fake CI run |
| PartiallySucceeded | Warning wording (not “failed”) |
| Healthy Azure + historical failed Azure event | No Failure details; history under Recent activity |
| Min width / 2-project density | Card stays usable; secondary reasons collapsed |
| Activity + Azure Failure details + Recent activity | All three visible; activity not overwritten |

Do not force risky production pipeline failures solely for QA.

## Deferred

- Run-host crash cards (#111c)
- Tray tooltip one-liner polish beyond existing `LastErrorPreview`
