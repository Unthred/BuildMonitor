# Feature ticket prompt (GitHub Issue)

```text
Title: <imperative short title>

Body:
## Problem
<what is wrong or missing>

## Acceptance criteria
- [ ] …
- [ ] …

## Surfaces
- <TrayApp / Orchestrator / Settings / docs>

## Out of scope
- …

Instruction:
  Create issue: gh issue create --title "..." --body-file issue-body.md
  Propose branch: feature/<id>-kebab
  Agent fills Tests and Docs rows in kickoff plan after issue exists.
```
