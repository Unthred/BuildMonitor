---
name: performance-pass
description: >-
  Performance review for BuildMonitor: orchestrator hot paths, log I/O, subprocess pipes.
  Use when user asks for performance pass or slowness review.
disable-model-invocation: true
---

# Performance pass (BuildMonitor)

Run when a change risks tray lag, slow startup of monitored projects, or log viewer stutter.

## When to run

| Change | Run? |
|--------|------|
| `ProjectOrchestrator`, `SupervisedProcess`, log save timing | Yes |
| Port probe / listen URL readiness | Yes |
| `DotNetProcessConfigurator` env flags | Yes |
| Pure TrayApp label text | Usually N/A |

## Checklist

| Area | Check |
|------|-------|
| Stdout handler | No full-log disk write or normalize on every line |
| Port probe | Not on every output line; debounced/timer-based |
| Watch start | `--no-build` when build already succeeded |
| MSBuild reuse | Not disabled on long-running watch process |
| File watcher | Debounce/coalesce file-change rebuilds |
| UI thread | No blocking network I/O on WPF dispatcher for probes |

## Report

```markdown
## Performance pass

| Check | Pass / Fail / N/A | Notes |
|-------|-------------------|-------|
| … | | |

**Blockers:** …
```
