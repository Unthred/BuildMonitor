# Tray icon family — final visual checkpoint (#95)

Accepted 16px language carried to 20/24/32 with **size-specific** artwork (not mechanical scale of 16).

## Locked 16px

| State | Glyph |
|-------|--------|
| Healthy | white+dk check |
| Building | A1-R1 diagonal asymmetric hammer |
| Attention | white+dk `!` |
| Failed | red+wt X |
| Neutral | greyscale only (no glyph) |

## Production sources

| Asset | Path |
|-------|------|
| PNG per state/size | `png/tray-{state}-{16,20,24,32}.png` |
| ICO per state | `ico/tray-{state}.ico` — frames **16, 20, 24, 32** (PNG-compressed) |

## Sheets

- `sheets/FINAL-complete-dark.png` / `FINAL-complete-light.png`
- `sheets/FINAL-family-actual-dark.png` / `-light.png`
- `sheets/FINAL-family-nn-dark.png` / `-light.png`

## Size-specific deviations

- **16:** exact accepted pixel-authored rasters.
- **20/24/32:** same semantics; glyphs drawn vector-style in lower-left ~45%; Building hammer uses A1-R1 asymmetric proportions (long left face, short peen, narrow handle) with slightly more geometric clarity as pixels allow. No badges, no translucent full-face overlays.

## Runtime sync

Accepted ICOs are copied into `src/TrayApp/Assets/tray/runtime/` for embedding by `TrayIconFactory`. Traffic-light remains obsolete fallback only. Physical tray acceptance notes live under `docs/assets/tray-physical-qa/`.

## Neutral contrast fix

Neutral was re-authored for light-taskbar visibility: darker silhouette contour, darker eyes/beak/helmet lines, mid-grey body (not pale wash). Still greyscale, no glyph. HEA/BUI/ATT/FAI unchanged.
