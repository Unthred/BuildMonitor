# Agent skills (BuildMonitor control plane)

## buildmonitor-control-plane

Teaches a Cursor agent in a **watched** .NET repo to:

1. Discover the loopback API (`control-plane.json` or probe `127.0.0.1:7700`)
2. Match `projectId` by `rootFolder`
3. Call busy → edit → idle → optional ship-check

### Install from BuildMonitor (recommended)

With the project selected in **Settings → Projects**, click **Install Cursor agent skill in this folder**.

Or tray menu: **Install Cursor agent skill → &lt;project&gt;** (or under the project submenu when layout is By project).

That copies:

- `{rootFolder}\.cursor\skills\buildmonitor-control-plane\SKILL.md`
- `{rootFolder}\.cursor\rules\buildmonitor-control-plane.mdc` (always-on — agents handshake without paste)

Settings shows **Cursor agent integration** status for the selected project: Not installed / Partially installed / Outdated / Ready.

### Install via script

```powershell
# Personal skill (all Cursor workspaces on this machine)
.\scripts\Install-ControlPlaneAgentSkill.ps1

# Or one watched repo
.\scripts\Install-ControlPlaneAgentSkill.ps1 -TargetRepoRoot "C:\src\YourApp"
```

### Source of truth

Canonical skill text: [buildmonitor-control-plane/SKILL.md](buildmonitor-control-plane/SKILL.md)  
Canonical always-on rule: [buildmonitor-control-plane/RULE.mdc](buildmonitor-control-plane/RULE.mdc)

**Shell / `AwaitShell` wait rules** live only in the canonical skill (§ Shell wait rules). Product repos must not duplicate that generic contract; reinstall from BuildMonitor when the skill changes.

Installed copies under a repo’s `.cursor/skills/buildmonitor-control-plane/` and `.cursor/rules/buildmonitor-control-plane.mdc` are **generated** (installer overwrite). In the BuildMonitor repo those paths are gitignored so self-install does not dirty `git status`; other tracked `.cursor` rules/skills are unchanged.

API details: [../control-plane.md](../control-plane.md)
