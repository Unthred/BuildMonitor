---
name: feature-ship
description: >-
  Ship checklist + GitHub PR/merge. Use for ship, ship it, ready for review,
  open PR, merge, or definition of done.
disable-model-invocation: true
---

# Ship (BuildMonitor)

**Authority:** `feature-delivery.mdc`, `work-tracking.mdc`, `no-unapproved-runtime-execution.mdc`, `build-warnings.mdc`, `documentation.mdc`, `.github/pull_request_template.md`.

## Mode

| User said | End state |
|-----------|-----------|
| **ship** / **ship it** / **merge** | PR **merged** to `main` + issue closed (`Closes #N`) + project **Done** |
| **ready for review** / **open PR** | Commit + push + active PR; issue linked; project **In Progress** |

No commit, push, or merge unless the user triggered one of the phrases above.

## Workflow

1. **`git status` / diff** — map changes; flag out-of-scope work.
2. **Quality (diff-scoped)** — apply `feature-delivery.mdc` §1–6 only where the diff applies; one-line **N/A** per skipped section.
   - **Security (inline):** if diff touches subprocess env, settings paths, PAT storage — no secrets in diff. Else N/A.
   - **Performance (inline):** if diff touches orchestrator output handling, log saves, port probe — no obvious hot-path blocking. Else N/A.
3. **User gates** — agent does **not** run `dotnet build` or `dotnet test`. Print commands; require user confirmation or pasted output before PR/merge.
4. **Git + GitHub** — per `work-tracking.mdc`: resolve `#<id>`; confirm issue is on **project #3** (add with `gh project item-add 3` if missing); commit `#<id>: …`; push; PR body `Closes #<id>`.
5. **Merge + Done** — only for full **ship it**: `gh pr merge --squash` after user confirms build/test; issue closes via `Closes #N`; project Status **Done** (automation or manual per [docs/ops/github-workflow.md](../../docs/ops/github-workflow.md)).

```powershell
gh pr create --title "#42: Short title" --body "Closes #42`n`n## Summary`n- ...`n`n## Test plan`n- [x] dotnet build`n- [x] dotnet test"
gh pr merge <n> --squash
```

## Ship report (required)

```markdown
## Ship checklist

| Area | Status | Notes |
|------|--------|-------|
| Build (user) | pass / fail / pending | no new warnings in diff |
| Tests | pass / fail / N/A | |
| Docs / ADR | pass / N/A | paths |
| Security inline | pass / N/A | |
| Performance inline | pass / N/A | |
| PR | pass / fail / N/A | URL |
| Merged to main | pass / fail / N/A | |
| Issue #N | open / closed / N/A | |
| Project Status | Todo / In Progress / Done / N/A | BuildMonitor board |

**Shipped (merged + closed):** yes / no  
**Blockers:** …
```

Fix blockers in scope or report them — do not claim shipped until merge succeeds (unless user only asked for review).
