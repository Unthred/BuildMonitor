# GenerateTrayIcons — production asset packager (#95)

Offline tool that turns the **externally supplied** production master sheet into committed tray ICO/PNG assets.

**Source authority:** `docs/assets/tray-icon-production-masters.png` — do not redraw, trace, or procedurally recreate the mascot.

`BuilderDuckRenderer.cs` is **rejected** and unused; retained for history only.

## Usage

Inspect layout and extraction bounds:

```powershell
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj -- --inspect
```

Generate tray PNG previews (16/20/24/32) and multi-size ICOs:

```powershell
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj --
```

Output:

- `src/TrayApp/Assets/tray/png/tray-{state}-{size}.png`
- `src/TrayApp/Assets/tray/runtime/tray-{state}.ico`

Rebuild TrayApp after regenerating so embedded resources refresh.
