# GenerateTrayIcons — tray health-ring packager (#129)

Offline tool that composes **pre-rendered** multi-size tray ICOs/PNGs from the recovered badge-less duck.

**Duck authority:** `docs/assets/tray-duck-base-badge-less.png`  
**Do not** use `BuilderDuckRenderer.cs` (rejected).  
**Do not** regenerate runtime assets from the legacy glyph master unless explicitly requested (`--legacy-glyph-master`).

## Visual rules (post-QA fix)

- Duck source is often 24bpp RGB; generator **chroma-keys** near-white / near-black to true alpha (source file bytes unchanged).
- Rings are **hard opaque pixel annuli** (no GDI AA pens, no glow, no low-alpha track fill).
- 16px: duck inset 3 (10×10), 1px ring at canvas limit; corners of every frame must be A=0 (asserted during generate + unit test).

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
