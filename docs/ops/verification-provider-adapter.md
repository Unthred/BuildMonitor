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

Otherwise it declines. The product repository's `direct-dotnet` fallback runs. **Do not auto-configure** the worktree.

Once claimed, BuildMonitor exclusively owns rebuild, tests, status, and final verification. Final local verification is `/run/ship-check` (fresh build and tests). HTTP 409 means busy: wait, recheck status, retry. Do not overlap `/run/*`.

## Fallback

| Situation | Provider name | Agent does |
|-----------|---------------|------------|
| Exact claim + reachable + supported | `buildmonitor-control-plane` | Control-plane `/run/*` only |
| Unreachable, no exact root, or unsupported op | `direct-dotnet` | Product-repo documented `dotnet build` / `dotnet test` |
| More than one exact+available adapter | `direct-dotnet` | Decline; do not guess |

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Adapter never claims a sibling worktree | Intended. Configure that exact folder in BuildMonitor, or use fallback. Do not point the adapter at a parent checkout. |
| `Verification: direct-dotnet` while the main clone is watched | The edited worktree path is different. Unconfigured is valid. |
| Installed files look old | Re-run the installer; Inspect status is Missing / Partial / Outdated / Ready. Confirm `adapterVersion: 1.0.0` matches the repo skill. |
| 409 on `/run/*` | Another exclusive operation is in flight. Wait, `GET /projects` / `GET /session`, retry once. |
| Handshake skipped | Control plane disabled or port not bound. Tray warning; product fallback applies. |
| Product repo still has a copied skill | Remove it from that repo. The adapter belongs in the user Cursor profile only. |

HTTP request journaling is out of scope for this adapter change.
