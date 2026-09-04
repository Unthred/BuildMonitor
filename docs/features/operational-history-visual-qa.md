# Visual QA — Operational history timeline (#116)

Artefacts (illustrative mockups, not pixel screenshots of the live tray):

- Status panel Recent activity (light): see chat asset `qa-status-recent-activity-light.png`
- Diagnostics Operational history: see chat asset `qa-diagnostics-operational-history.png`

## Status panel checklist

| Check | Notes |
|-------|-------|
| Current-state prominence | BUILDS / DETAIL / overall footer remain above Recent activity |
| Scanability | Time · Source glyph+label · Primary summary |
| Row density | ~10 rows max; expander collapses for multi-project |
| Source distinction | Label + glyph (L/Az/Ag/U/S), not colour alone |
| Failed visibility | Error emphasis on Failed rows only |
| Timestamp | Local `HH:mm` same-day |
| Card height | Expander default collapsed when 2+ projects; expanded history capped (~140px) with scroll; card body ScrollViewer when work-area clamp applies |
| Empty / unavailable | Italic quiet messages |
| Themes | Uses existing `ThemePalette` brushes |

## Diagnostics checklist

| Check | Notes |
|-------|-------|
| Fuller set | Up to 50 rows with scroll |
| Multi-project | Per-tab filtering via `GetRecentForProject` |
| Detail | Secondary + optional detail under row; tooltip has full stamp |
| No charts / analytics | List only |
| Triggers grid | Still primary for trigger training below history |

## Trade-offs

- Nested detail expanders omitted on status cards (tooltip + secondary text) to keep density low.
- OperationId grouping deferred.
- Source filter ComboBox deferred (V1 per-project lists only).
