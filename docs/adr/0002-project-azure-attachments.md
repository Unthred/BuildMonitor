# ADR 0002: Project attachments and Azure DevOps association

**Status:** Accepted  
**Date:** 2026-08-21

## Context

BuildMonitor evolved into a project-centric local build/run/watch tray app (`MonitoredProjectSettings` / formerly flat `LocalProjectDefinition`, `ProjectRuntime`, `ProjectHealthSnapshot`, `HealthCoalescer`). Early Azure DevOps polling lived as an unwired, pipeline-list module under `Infrastructure/AzureDevOps` (see issue #30). Product direction requires Local-only, Azure-only, or combined projects, shared org connections, and repository-centric Azure identity — without a parallel Azure tray authority or mock-healthy CI when auth is missing.

## Decision

1. A BuildMonitor **Project** is a logical software project with optional **Local** and optional **Azure DevOps** attachments; **at least one** attachment is required.
2. Azure DevOps **connections** (organisation URL + credential reference) are top-level settings. Credentials are **never** stored in `settings.json` (CurrentUser DPAPI under `%LocalAppData%/BuildMonitor/secrets/ado-{connectionId}.dpapi`).
3. An Azure attachment is **repository-centric**: connection id, ADO project, repository, and **0..N** selected pipelines (children of the attachment, not separate BM projects).
4. **Empty pipeline selection** means **Connected / Not monitored** (`AzureCiMonitoringState.NotMonitored`). It is valid settings, not CI failure or monitoring failure.
5. **Current Git branch** (PATH `git`) controls **presentation focus only**, not health eligibility. Detached/unavailable HEAD is not an error.
6. Azure health **merges** into the existing `ProjectHealthSnapshot` / coalescer / tray rollup path. There is **no** parallel Azure-only tray authority.
7. Missing auth / poll failure must **never** produce mock healthy CI. **CI state** and **monitoring availability** are distinct (`AzureCiMonitoringState` vs `AzureMonitoringAvailability`).
8. **Cancelled** Azure builds are **neutral** for project/tray health.
9. Repository **default branch** comes from Azure metadata (last-known retained on refresh failure); no manual override in v1.
10. Schema **v21** nests former flat local fields under `Local` and introduces `Connections` + `Azure`.
11. **Session gate (Slice 3B):** Azure polling runs only when `IsActiveInSession` and ≥1 pipeline is selected (same session flag as Local). Azure-only projects use that flag; there is no separate “monitor Azure” toggle.
12. **Polling state** for Slice 3B is **in-memory** (no `JsonStateStore` for facets); notifications remain deferred.

## Consequences

**Positive:**

- Matches local + cloud workflows and multi-pipeline repos as one BM project.
- Multi-org remains structurally possible (list of connections).
- Local-only installs migrate without behaviour change.
- Clear semantics for Not monitored vs auth loss vs CI failed.
- Continuous polling + status panel without a second tray publisher.

**Trade-offs:**

- Settings UI and orchestrator must understand optional Local (Azure-only has no `ProjectRuntime`; health comes from Azure facets).
- Azure build notifications and stage/job timeline remain deferred.
- In-memory facets reset on restart until the first poll cycle.

## References

- Issue: [#30](https://github.com/Unthred/BuildMonitor/issues/30)
- Code: `src/Core/Settings/LocalAppSettings.cs`, `src/Core/Models/AzureMonitoringSemantics.cs`, `src/Core/Rules/AzureHealthContribution.cs`, `src/Infrastructure/AzureDevOps/AzureMonitoringService.cs`
- Docs: `docs/SETTINGS.md`, `docs/ARCHITECTURE.md`
