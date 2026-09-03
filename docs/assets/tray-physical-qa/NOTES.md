# Tray physical QA (#95)

Runtime acceptance on deployed tray (`b49d0c0-dirty` with accepted ICO sync), 2026-09-03.

## Results

| Check | Result |
|-------|--------|
| Healthy at real 16px | PASS — yellow duck + check; no traffic-light |
| Building at real 16px | PASS — diagonal hammer distinct from check |
| Healthy → Building → Healthy | PASS |
| Building steady (no 350 ms pulse) | PASS — identical samples during rebuild |
| Failed | PASS — prior physical check (WC Azure Red) |
| Failed beats concurrent Building | PASS — prior physical check |
| Traffic-light fallback | Not observed during acceptance |

## Captures (canonical)

| File | State |
|------|--------|
| `10-healthy-tray-capture.png` / `10-healthy-nn12x.png` | Healthy |
| `11-building-tray-capture.png` / `11-building-nn12x.png` | Building |
| `12-settled-after-build-tray-capture.png` / `12-settled-after-build-nn12x.png` | Settled Healthy after rebuild |
| `13-restored-wc-current-tray-capture.png` / `13-restored-wc-current-nn12x.png` | After WC Active restore (Azure was Building → Building icon) |

## Notes

- WitherbyConnect was temporarily inactive for Healthy/Building isolation, then restored Active-in-session.
- Early same-day captures named `01`–`03` were Failed rollup (WC Red) and are omitted from the tree.
