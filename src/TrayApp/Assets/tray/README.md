# BuildMonitor tray icons (#95)

## Production authority

**Master sheet:** [`docs/assets/tray-icon-production-masters.png`](../../../docs/assets/tray-icon-production-masters.png) — externally supplied artwork; do not redraw in code.

**Concept reference:** [`docs/assets/tray-icon-concept.jpg`](../../../docs/assets/tray-icon-concept.jpg)

Builder duck + yellow hard hat. Status badge **bottom-left**. Static icons; no globe overlay in v1.

## Runtime assets (`runtime/`)

| File | State |
|------|--------|
| `tray-neutral.ico` | Neutral / monitoring |
| `tray-healthy.ico` | Healthy |
| `tray-building.ico` | Local or Azure build activity |
| `tray-attention.ico` | Amber attention |
| `tray-failed.ico` | Failed |

Each ICO embeds **16, 20, 24, 32** px PNG frames extracted from the production master (high-quality downscale).

`../AppIcon.ico` — unchanged traffic-light era application icon (master sheet has no badge-less duck).

## PNG previews (`png/`)

Lossless previews at each size for visual review and diffing. Not loaded at runtime.

## Regeneration

```powershell
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj -- --inspect
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj --
```

Uses `ProductionMasterExtractor.cs` — crop + normalize + downscale only. **Do not use `BuilderDuckRenderer.cs`.**

## Code

- `TrayIconPresentationMapper` (Core) — state precedence
- `TrayIconFactory` (TrayApp) — loads embedded ICOs (cached)
- `TrafficLightIconFactory` — obsolete fallback if mascot resources fail to load
