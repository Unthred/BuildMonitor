---
name: feature-ship
description: >-
  Ship checklist + GitHub PR. Use for ship, ship it, ready for review,
  open PR, merge, complete the ship, or definition of done.
disable-model-invocation: true
---

# Ship (BuildMonitor)

**Authority:** `feature-delivery.mdc`, `work-tracking.mdc`, `no-unapproved-runtime-execution.mdc`, `build-warnings.mdc`, `documentation.mdc`, `.github/pull_request_template.md`.

## Publishing states

**Authority:** this section. Apply to the **current** user instruction (not earlier chat).

| Current instruction | Agent may |
|---------------------|-----------|
| Ordinary implementation (no publish phrase) | Edit as requested. **No** pull request and **no** merge unless this instruction asks. |
| **open PR** / **prepare a PR** / **ready for review** | Commit if needed, push, open the PR, wait for GitHub checks, **stop**. Issue stays open; project **In Progress**. Do not merge, do not enable auto-merge, do not close the issue. |
| **merge** / **complete the ship** / explicit complete-and-merge wording | After validation is green: squash-merge. Close the issue (`Closes #N`) and set project **Done** unless the instruction says to leave it In Progress. |

The word **ship** or **ship it** alone is **not merge authorization**. If a PR is implied, open it, wait for checks, then **ask or stop** at the green pull request.

A green local build, green PR validation, or an agent observing CI success is **not human merge approval**.

No commit, push, or merge unless the current instruction includes a publish phrase above (or the user already authorized that step).

## Workflow

1. **`git status` / diff** — map changes; flag out-of-scope work.
2. **Quality (diff-scoped)** — apply `feature-delivery.mdc` §1–6 only where the diff applies; one-line **N/A** per skipped section.
   - **Security (inline):** if diff touches subprocess env, settings paths, PAT storage — no secrets in diff. Else N/A.
   - **Performance (inline):** if diff touches orchestrator output handling, log saves, port probe — no obvious hot-path blocking. Else N/A.
3. **Verification** — follow `no-unapproved-runtime-execution.mdc`. If the user-installed verification provider claims this exact worktree, it exclusively owns rebuild / tests / ship-check. Otherwise use the permitted `direct-dotnet` fallback and report why. Do not also run extra `dotnet build` / `dotnet test` once a provider has claimed the folder.
4. **Git + GitHub** — per `work-tracking.mdc`: resolve `#<id>`; confirm issue is on **project #3** (add with `gh project item-add 3` if missing); commit `#<id>: …`; push. PR body must reference `#<id>`. Use `Related to #<id>` when the issue must stay open; use `Closes #<id>` only when merge is authorized and the issue should close.
5. **CI** — wait for [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml). Report pass/fail. Green checks are not merge approval.
6. **Merge** — only when the current instruction is **merge** or **complete the ship**: `gh pr merge --squash` after green checks. Do not enable auto-merge unless asked.

```powershell
gh pr create --title "#42: Short title" --body "Related to #42`n`n## Summary`n- ...`n`n## Test plan`n- [x] verification"
# Merge only when currently authorized:
# gh pr merge <n> --squash
```

## Ship report (required)

```markdown
## Ship checklist

| Area | Status | Notes |
|------|--------|-------|
| Verification provider | claimed / direct-dotnet | reason |
| Build | pass / fail / pending | no new warnings in diff |
| Tests | pass / fail / N/A | |
| Docs / ADR | pass / N/A | paths |
| Security inline | pass / N/A | |
| Performance inline | pass / N/A | |
| PR | pass / fail / N/A | URL; still open unless merge authorized |
| Merged to main | pass / fail / N/A | only if merge authorized |
| Issue #N | open / closed / N/A | stay open unless merge authorized |
| Project Status | Todo / In Progress / Done / N/A | BuildMonitor board |

**Merge authorized this instruction:** yes / no  
**Green CI is not human merge approval.**  
**Blockers:** …
```

Do not claim shipped-as-merged until merge succeeds under a current merge authorization.
