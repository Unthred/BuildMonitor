---
name: security-pass
description: >-
  Security review for BuildMonitor: subprocess env, local settings, PAT storage,
  path handling. Use when user asks for security pass.
disable-model-invocation: true
---

# Security pass (BuildMonitor)

Run after implementation or when a change touches security-sensitive surfaces.

## Checklist

| Area | Check |
|------|-------|
| Secrets | No PATs, tokens, or real `settings.json` in diff or docs |
| Subprocess | `DotNetProcessConfigurator` strips host watch pollution; no secret env vars logged |
| Paths | Project root/folder from settings cannot escape intended directories trivially |
| PAT storage | `PatSecretStore` / ProtectedData — no plaintext secrets on disk in repo |
| Logs | Build logs may contain project output — do not exfiltrate or commit user log files |
| Dependencies | New NuGet packages called out per `third-party-dependencies.mdc` |

## Report

```markdown
## Security pass

| Check | Pass / Fail / N/A | Notes |
|-------|-------------------|-------|
| … | | |

**Blockers:** …
```
