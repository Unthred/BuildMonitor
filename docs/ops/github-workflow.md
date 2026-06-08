# GitHub workflow

Repo: [github.com/Unthred/BuildMonitor](https://github.com/Unthred/BuildMonitor)

## Issues

```powershell
gh issue create --title "Add live port probe" --body "Summary and acceptance criteria"
gh issue list --state open
gh issue view 42
```

Use **moderate** tracking: features and bugs get an issue; docs/rules-only chores can skip when the user opts out.

## Branches

```text
feature/<issue-id>-short-kebab-name
```

Example: `feature/42-live-build-log`

## Commits

```text
#42: Short imperative summary
```

## Pull requests

```powershell
git push -u origin HEAD
gh pr create --title "#42: Live port probe" --body "Closes #42`n`n## Summary`n- ...`n`n## Test plan`n- [x] dotnet build`n- [x] dotnet test"
gh pr view --web
gh pr merge --squash
```

PR template: [.github/pull_request_template.md](../../.github/pull_request_template.md)

## Ship it (agent)

Full ship: commit → push → PR → merge → close issue (via `Closes #N` on merge).

Load `.cursor/skills/feature-ship/SKILL.md` when the user says **ship** or **ship it**.
