# Build logs

## Files per project

| File | Kind |
|------|------|
| `last-build.log` | `dotnet build` |
| `last-test.log` | `dotnet test` |
| `last-run.log` | failed `dotnet run` / `watch` exit |
| `*.meta.json` | metadata (command, exit code, error lines) |

## Viewing

- Hover panel → **View log** on a project card
- Tray → run app and use log viewer from hover (context menu expansion planned)

## Error parsing

Lines matching MSBuild/dotnet failure patterns are listed in the viewer sidebar for jump-to-line navigation.
