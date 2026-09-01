# BuildMonitor tray icons (#95)

## Status

**Production tray visual:** `TrafficLightIconFactory` (active on deployed builds until external mascot artwork lands).

**Semantic layer (preserved on branch):** `TrayIconPresentationMapper` + `TrayIconPresentationState` in Core — precedence Failed > Building > Attention > Healthy > Neutral, including Local + Azure build activity → Building. Wired to tray icons when supplied PNG/ICO assets are ready.

**Rejected for production:** programmatic `BuilderDuckRenderer` output and generated `runtime/*.ico` / `png/*` previews (failed manual tray QA). Do not merge mascot artwork from the generator.

## Visual authority (future)

Approved concept: [`docs/assets/tray-icon-concept.jpg`](../../../docs/assets/tray-icon-concept.jpg)

Builder duck + yellow hard hat. Status badge **bottom-left**. Static icons; no globe overlay in v1.

## Expected supplied assets

Five states: Neutral, Healthy, Building, Attention, Failed.

Expect transparent PNG masters and/or explicit micro-size variants at **16, 20, 24, 32** px. Do not assume one large master will be blindly downscaled.

Cursor will build multi-resolution ICO resources from supplied artwork and wire them into a factory (replacing traffic-light at runtime only after visual sign-off). **Do not redraw the mascot in code.**

## Folders (placeholder)

| Folder | Purpose |
|--------|---------|
| `runtime/` | Committed multi-size ICOs per state (empty until external artwork) |
| `png/` | Optional lossless previews for review/diff (not loaded at runtime) |

## Code

- `TrayIconPresentationMapper` (Core) — state precedence (keep)
- `TrafficLightIconFactory` (TrayApp) — active production tray visual
- `tools/GenerateTrayIcons/` — **rejected** programmatic generator; retained for reference only
