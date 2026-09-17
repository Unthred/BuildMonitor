# Build logs

## Files per project

| File | Kind |
|------|------|
| `last-build.log` | `dotnet build` |
| `last-test.log` | `dotnet test` |
| `last-run.log` | failed `dotnet run` / `watch` exit; also captures output while the log window is closed. The first line of a start is a diagnostic banner (`Starting '…' root=… startup=… profile=… environment=… url=…`) with no environment-variable values. |
| `*.meta.json` | metadata (command, exit code, error lines) |

## Viewing

- Hover / status panel → **Log** on a project card (opens the local BuildMonitor log viewer)
- **Local BUILDS Status** when amber (**Warnings**) or red (**Failed** / **Build failed** / etc.) — opens/reuses that project's log viewer with the matching issue filter when available
- Tray context menu → **View log**
  - **By project** layout: under each Local project's submenu (after Stop; before Clean build output)
  - **By operation** layout: **View log** → project name
- Reuses a single log window per project (second open activates the existing window)
- Azure-only projects are not listed for **View log** — the viewer is for local BuildMonitor logs; use Azure run links for cloud builds

## Error parsing

Lines matching MSBuild/dotnet failure patterns are listed in the viewer sidebar for jump-to-line navigation.
