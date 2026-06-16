---
name: feature-kickoff
description: >-
  Plan a feature before coding: GitHub issue, branch, scope, acceptance criteria, docs/tests/ADR.
  Use for kickoff, plan the feature, before we code, or issue scope.
disable-model-invocation: true
---

# Kickoff (BuildMonitor)

**Plan only** — no source edits unless the user asks to create the GitHub issue or branch this turn.

**Authority:** `work-tracking.mdc`, `feature-delivery.mdc`, `adr.mdc`, `documentation.mdc`. Record of intent: **project board #3** (issue + card) + `docs/` + PR, not chat.

## Steps

1. **Ask** — problem, in-scope / out-of-scope (one sentence each), surfaces (TrayApp UI, orchestrator, settings). If vague: **at most three** questions. No implementation.
2. **Issue + project board** — use supplied `#N` (confirm it is on project #3), or create **both** in one flow:

```powershell
$url = gh issue create --repo Unthred/BuildMonitor --title "..." --body "..." --assignee @me --json url -q .url
gh project item-add 3 --owner Unthred --url $url   # mandatory — board is the primary view
```

Project **#3**: [BuildMonitor board](https://github.com/users/Unthred/projects/3) — see [.github/project.json](../../.github/project.json). Never create a repo issue without adding it to the board.
3. **Branch** — propose `feature/<id>-<kebab>`; do not checkout until kickoff approved.
4. **Plan table** — fill only rows that apply:

| Row | Content |
|-----|---------|
| Acceptance criteria | 3–7 testable bullets |
| Tests | `BuildMonitor.Tests` class + cases, or "N/A — manual tray check" |
| Security / performance | One line each; full pass skills only if warranted |
| Docs | `docs/features/…`, `SETTINGS.md`, `ARCHITECTURE.md`, `LOGS.md` per `feature-delivery.mdc` |
| ADR | yes/no per lasting decisions |
| Out of scope | explicit |

5. **Stop** — present plan; wait for user approval before coding.
