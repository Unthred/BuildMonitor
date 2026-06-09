# Documentation-only prompt

```text
Docs: <topic>

Goal:
  <what readers should know after reading>

Files:
  <docs/features/... or docs/ARCHITECTURE.md — or leave blank for agent to propose>

Instruction:
  Edit docs only unless a typo fix in code is required for accuracy.
  Update docs/README.md index if adding a top-level page.
  Run link sweep mindset: all relative markdown links must resolve.
  No secrets in the diff.
  No GitHub issue required if user said docs-only (work-tracking.mdc exception).
```
