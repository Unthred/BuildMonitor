# GitHub workflow

Repo: [github.com/Unthred/BuildMonitor](https://github.com/Unthred/BuildMonitor)

Every change is tracked as a **card on the BuildMonitor project board** ([#3](https://github.com/users/Unthred/projects/3)), backed by a GitHub Issue and delivered through a **pull request** merged to `main`.

## Project board

| Field | Value |
|-------|-------|
| Title | **BuildMonitor** |
| Number | **3** |
| Owner | **Unthred** |
| URL | [github.com/users/Unthred/projects/3](https://github.com/users/Unthred/projects/3) |
| Repo link | `Unthred/BuildMonitor` |
| Config | [.github/project.json](../../.github/project.json) |

### Recommended automations (Project → ⚙ → Workflows)

If not already set:

- **Item added to project** → Status **Todo**
- **Pull request linked to issue** → Status **In Progress** (optional)
- **Issue closed** → Status **Done**

Default **Status** field should include **Todo**, **In Progress**, **Done**.

## Lifecycle

| Phase | Git | Issue | Project Status |
|-------|-----|-------|----------------|
| Planned | — | open | **Todo** |
| In progress | `feature/<id>-...` | open | **In Progress** |
| In review | PR open | open | **In Progress** |
| Shipped | merged to `main` | closed (`Closes #N`) | **Done** |

**Ready for review** = PR open, issue open, Status **In Progress**.

**Ship it** = squash-merge PR, issue closed, Status **Done** (automation or manual).

## Issues → project board (mandatory)

Every change gets an issue **and that issue is always added to project #3**. The board is the primary view; repo issues alone are not enough.

```powershell
$url = gh issue create --repo Unthred/BuildMonitor `
  --title "Add live port probe" `
  --body "Summary and acceptance criteria" `
  --assignee @me `
  --json url -q .url

gh project item-add 3 --owner Unthred --url $url   # required — do not skip
```

**GitHub UI:** after creating an issue, use **Projects** on the right sidebar → add to **BuildMonitor**, or run `gh project item-add` as above.

**Existing issue not on the board:**

```powershell
gh project item-add 3 --owner Unthred --url "https://github.com/Unthred/BuildMonitor/issues/42"
```

```powershell
gh issue list --repo Unthred/BuildMonitor --state open
gh issue view 42 --repo Unthred/BuildMonitor
```

Use the [feature issue template](../../.github/ISSUE_TEMPLATE/feature.yml) when creating from the GitHub UI.

### Manual Status update (when automation is not enough)

```powershell
gh project field-list 3 --owner Unthred
gh project item-list 3 --owner Unthred --format json
# gh project item-edit ... (field and option IDs from field-list)
```

Prefer **issue closed** + **Issue closed → Done** automation for shipped work.

## Branches

```text
feature/<issue-id>-short-kebab-name
```

Example: `feature/42-live-build-log`

Do not land feature work directly on `main` except explicit hotfixes.

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

`Closes #N` in the PR body links the work item and closes the issue on merge.

## Agent: resolve issue before commit

When no `#` was supplied, resolve in order:

1. Branch name `feature/<id>-...`
2. Conversation + files changed
3. **Project board #3** — **Todo** / **In Progress** cards (preferred)
4. `gh issue list --repo Unthred/BuildMonitor` — if matched, ensure the issue is on project #3 before commit

If none match, create an issue, **`gh project item-add 3`**, agree the id, then commit.

## Ship it (agent)

Full ship: commit → push → PR → squash merge → issue closed → project **Done**.

Load `.cursor/skills/feature-ship/SKILL.md` when the user says **ship** or **ship it**.

## Board sync (backlog)

The board must list **all shipped work (Done)** and **planned work (Todo)**. Run after creating retrospective issues or when the board drifts:

```powershell
.\scripts\github\Sync-ProjectBoard.ps1
```

Dry run: `.\scripts\github\Sync-ProjectBoard.ps1 -WhatIf`

The script is idempotent (skips issues whose titles already exist) and ensures closed issues **#2**, **#4**, **#6**–**#8**, **#10** are on project #3 with Status **Done**.

### Planned work (Todo on board)

| Theme | Issue title (created by sync script if missing) |
|-------|--------------------------------------------------|
| Tests | Run tests after permitted file-triggered build (`OnFileChange`; not a watcher-side trigger) |
| Tray UX | Open log viewer from tray context menu |
| Tray UX | WPF tray context menu (Phase 2, if #8 insufficient) |
| Optional module | Azure DevOps polling (shipped — see SETTINGS / ARCHITECTURE; notifications still deferred) |
| Diagnostics | Verdict feedback loop for adaptive debounce |

Update this table when adding new planned issues.

## Enforce issue on every commit

**Local (required for agents and developers):**

```powershell
.\scripts\install-githooks.ps1
```

The `commit-msg` hook rejects commits whose message lacks `#<issue>` unless the branch is `feature/<id>-...` (merge/revert commits are exempt).

**CI:** [`.github/workflows/pr-issue-link.yml`](../../.github/workflows/pr-issue-link.yml) fails PRs that do not reference an issue (`Closes #N` or `#N` in title/body).

## CI build and test

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on every push to `main` and on every pull request targeting `main`:

| Step | Command |
|------|---------|
| Build | `dotnet build BuildMonitor.slnx --configuration Release` |
| Test | `dotnet test src/BuildMonitor.Tests/BuildMonitor.Tests.csproj --configuration Release --no-build` |

Runner: **windows-latest** (WPF / `net10.0-windows`). SDK: **10.0.x** (`include-prerelease: true` until .NET 10 GA).

**PRs should pass CI before merge.** Agents do not run build/test locally by default; use CI status on the PR.

Optional: branch protection on `main` → require status check **build-and-test**.

## Record of intent

Chat and Cursor plans are not the system of record. The **project board (#3)**, **Issues**, **PR descriptions**, and **`docs/`** are.
