# GenerateTrayIcons — tray health-ring packager (#129)

Offline tool that composes **pre-rendered** multi-size tray ICOs/PNGs from the recovered badge-less duck.

**Duck authority:** `docs/assets/tray-duck-base-badge-less.png`  
**Do not** use `BuilderDuckRenderer.cs` (rejected).  
**Do not** regenerate runtime assets from the legacy glyph master unless explicitly requested (`--legacy-glyph-master`).

## Usage

```powershell
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj -- --inspect
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj --
```

Output:

- `src/TrayApp/Assets/tray/png/tray-*.png`
- `src/TrayApp/Assets/tray/runtime/tray-*.ico` (4 static + 36 active frames)
- `docs/assets/tray-ring-129/*` acceptance sheets

Rebuild TrayApp after regenerating so embedded resources refresh.
