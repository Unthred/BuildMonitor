# Verification-provider adapter

Optional Cursor adapter so agents can map a generic verification contract onto BuildMonitor. Product repositories keep their own outcomes and `direct-dotnet` fallback. This adapter is **not** copied into WitherbyConnect or other product repos.

Canonical source in this repository:

- Skill: [`docs/ops/agent-skills/buildmonitor-control-plane/SKILL.md`](agent-skills/buildmonitor-control-plane/SKILL.md)
- Always-on rule: [`docs/ops/agent-skills/buildmonitor-control-plane/RULE.mdc`](agent-skills/buildmonitor-control-plane/RULE.mdc)
- Installer: [`scripts/Install-ControlPlaneAgentSkill.ps1`](../../scripts/Install-ControlPlaneAgentSkill.ps1)

Frontmatter identity (contract version **1**, adapter **1.0.0**, source `Unthred/BuildMonitor`). Drift is detected by comparing installed files to this source (byte-normalized) and by parsing `adapterVersion` / `adapterSourcePath`.

## Install / update

Test against a temporary folder first:

```powershell
$dest = Join-Path $env:TEMP "bm-adapter-test"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
.\scripts\Install-ControlPlaneAgentSkill.ps1 -DestinationRoot $dest
```

Then install or update the current user profile (backs up existing files under `%USERPROFILE%\.cursor\buildmonitor-adapter-backup\<timestamp>\`):

```powershell
.\scripts\Install-ControlPlaneAgentSkill.ps1
```

Writes only:

- `%USERPROFILE%\.cursor\skills\buildmonitor-control-plane\SKILL.md`
- `%USERPROFILE%\.cursor\rules\buildmonitor-control-plane.mdc`

The tray **Install / Update** button and **Install Cursor agent skill** menu call the same user-level copy (they do not write into the selected project). The installer refuses a destination that looks like a WitherbyConnect checkout.

Start a **new** agent chat after install so Cursor loads the user-level files.

## Removal

Delete those two installed paths (and empty parent folders if you want). Backups under `.cursor\buildmonitor-adapter-backup\` can be removed separately. Product repositories are unchanged.

## Claim and capability

The adapter claims **yes** only when all of the following are true:

1. BuildMonitor is reachable (`control-plane.json` or `GET http://127.0.0.1:7700/projects`).
2. A project `rootFolder` equals the **exact** folder being edited (full path; trailing separators ignored; case-insensitive). Parent, child, sibling, similarly named, and the chat's original workspace are not matches.
3. That project can run the requested operation (`/run/rebuild`, `/run/tests`, `/run/ship-check`, or status).

Otherwise the adapter checks for an already-claimed **manual** parent (`derivedFromProjectId` empty) that shares the folder's Git common directory.

| Situation | What the adapter does |
|-----------|------------------------|
| Same repository as a claimed parent, exact path not registered | Registration required before build: `POST /projects/register-worktree`, then re-claim the exact path. Own project id, build directory, and port. `startOnLaunch` stays false. |
| That register fails | Report `BuildMonitor: register failed`. **No silent direct-dotnet fallback.** |
| No claimed parent | Decline. Product `direct-dotnet` fallback. **Do not auto-configure.** BuildMonitor remains optional. |
| Pull request still active | No unregister and no `git worktree remove`, even if validation is green. |
| Pull request merged/completed and validation green | `POST /run/stop`, then unregister that derived project only. After unregister succeeds, remove the Git worktree from another checkout and prune. |
| Unregister fails | Report it. **Failed unregister blocks worktree removal.** |
| Main / manual project (no `derivedFromProjectId`) | Never automatically unregister. |

BuildMonitor does not scan Git. `VerificationProviderClaim` still declines an unregistered exact path until register has succeeded.

Once claimed, BuildMonitor exclusively owns rebuild, tests, status, and final verification. Final local verification is `/run/ship-check` (fresh build and tests). HTTP 409 means busy: wait, recheck status, retry. Do not overlap `/run/*`.

## Fallback

| Situation | Provider name | Agent does |
|-----------|---------------|------------|
| Exact claim + reachable + supported | `buildmonitor-control-plane` | Control-plane `/run/*` only |
| Same Git repo as a claimed parent, path not registered yet | `buildmonitor-control-plane` after register | Register first. Register failure stops the task. No silent direct-dotnet fallback |
| No claimed parent, unreachable, or unsupported op | `direct-dotnet` | Product-repo documented `dotnet build` / `dotnet test`. **Do not auto-configure** |
| More than one exact+available adapter | `direct-dotnet` | Decline; do not guess |

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Adapter never claims a sibling worktree before register | Intended until `POST /projects/register-worktree` succeeds. Do not point `/run/*` at the parent checkout. |
| `Verification: direct-dotnet` while the main clone is watched | The edited path is a different worktree. If a claimed parent shares that Git common directory, register was required and a silent fallback is a rule miss. If no parent is claimed, unconfigured is valid. |
| Installed files look old | Re-run the installer; Inspect status is Missing / Partial / Outdated / Ready. Confirm `adapterVersion: 1.0.0` matches the repo skill. |
| 409 on `/run/*` | Another exclusive operation is in flight. Wait, `GET /projects` / `GET /session`, retry once. |
| Handshake skipped | Control plane disabled or port not bound. Tray warning; product fallback applies. |
| Product repo still has a copied skill | Remove it from that repo. The adapter belongs in the user Cursor profile only. |

HTTP request journaling is out of scope for this adapter change.
