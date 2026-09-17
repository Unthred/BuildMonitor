# Agent skills (BuildMonitor control plane)

## buildmonitor-control-plane

Optional **verification provider** for Cursor. It claims a worktree only when BuildMonitor is reachable and a configured `rootFolder` **exactly** matches the folder being edited. Product repos keep their own `direct-dotnet` fallback.

Install at the **user** Cursor profile. Do not copy these files into WitherbyConnect or other product repositories.

### Install from BuildMonitor (recommended)

Settings → Projects → **Install / Update**, or tray **Install Cursor agent skill**. Both write the current user's Cursor config, not the selected project's `.cursor` folder.

### Install via script

```powershell
# Dry-run / test destination (a folder that contains .cursor after install)
.\scripts\Install-ControlPlaneAgentSkill.ps1 -DestinationRoot "$env:TEMP\bm-adapter-test"

# Current user profile (default). Existing files are backed up.
.\scripts\Install-ControlPlaneAgentSkill.ps1
```

Installed paths:

- `%USERPROFILE%\.cursor\skills\buildmonitor-control-plane\SKILL.md`
- `%USERPROFILE%\.cursor\rules\buildmonitor-control-plane.mdc`

### Source of truth

Canonical skill: [buildmonitor-control-plane/SKILL.md](buildmonitor-control-plane/SKILL.md)  
Canonical always-on rule: [buildmonitor-control-plane/RULE.mdc](buildmonitor-control-plane/RULE.mdc)  
Adapter guide: [../verification-provider-adapter.md](../verification-provider-adapter.md)

**Shell / `AwaitShell` wait rules** live only in the canonical skill. Product repos must not duplicate that generic contract.

In the BuildMonitor repo, generated copies under `.cursor/skills/buildmonitor-control-plane/` and `.cursor/rules/buildmonitor-control-plane.mdc` are gitignored if someone installs there by mistake. The supported location is the user profile.

API details: [../control-plane.md](../control-plane.md)
