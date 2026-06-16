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
  Create issue + **always** add to project #3: `gh project item-add 3 --owner Unthred --url <issue-url>` (see docs/ops/github-workflow.md).
  Propose branch: feature/<id>-kebab
  Agent fills Tests and Docs rows in kickoff plan after issue exists.
```
