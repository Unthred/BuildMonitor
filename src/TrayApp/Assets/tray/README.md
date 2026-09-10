# BuildMonitor tray icons (#129 health ring)

## Production authority

**Badge-less duck base:** [`docs/assets/tray-duck-base-badge-less.png`](../../../docs/assets/tray-duck-base-badge-less.png)

Provenance: recovered from the #95 overlay-experiment source (`tray-overlay-exp-duck-base.png`) that matches the production-master duck **before** check/hammer/`!`/X glyph composition. Not redrawn; not inpainted from badge-bearing PNGs.

**Legacy glyph master** (historical only): [`docs/assets/tray-icon-production-masters.png`](../../../docs/assets/tray-icon-production-masters.png) — no longer used for runtime tray states.

## Visual language

| Signal | Encoding |
|--------|----------|
| Duck | Product identity (unchanged artwork) |
| Ring colour | Health (green / amber / red / grey) |
| Ring motion | Activity (faint track + 120° clockwise arc, 12 frames ≈ 125ms) |

**Failed always suppresses animation** (static red ring).

## Runtime assets (`runtime/`)

| Pattern | Count | Meaning |
|---------|-------|---------|
| `tray-{healthy\|attention\|failed\|neutral}.ico` | 4 | Static solid rings |
| `tray-{healthy\|attention\|neutral}-a00.ico` … `a11.ico` | 36 | Active animation frames |

Each ICO embeds **16, 20, 24, 32** px PNG frames.

`../AppIcon.ico` — unchanged application icon.

## PNG previews (`png/`)

Lossless previews per size (and per animation frame). Not loaded at runtime.

## Acceptance sheets

[`docs/assets/tray-ring-129/`](../../../docs/assets/tray-ring-129/) — static / anim / size-ladder on light and dark backgrounds.

## Regeneration

```powershell
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj -- --inspect
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj --
```

Uses `HealthRingAssetComposer.cs` + badge-less duck base.  
`BuilderDuckRenderer.cs` remains rejected. Legacy glyph pack: `--legacy-glyph-master`.

## Code

- `TrayIconPresentationMapper` — health rollup + `HasTrayBuildingActivity` → `TrayIconPresentation`
- `TrayIconFactory` — cached multi-size Icons (static + frames)
- `TrayIconRingAnimator` — 125ms frame advance only; no health evaluation on tick
- `TrafficLightIconFactory` — obsolete fallback if mascot resources fail
