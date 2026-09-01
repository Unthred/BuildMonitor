# BuildMonitor tray icons (#95)

## Visual authority

Approved concept: [`docs/assets/tray-icon-concept.jpg`](../../../docs/assets/tray-icon-concept.jpg)

Builder duck + yellow hard hat. Status badge **bottom-left**. Static icons; no globe overlay in v1.

## Runtime assets (`runtime/`)

| File | State |
|------|--------|
| `tray-neutral.ico` | Neutral / monitoring |
| `tray-healthy.ico` | Healthy |
| `tray-building.ico` | Local or Azure build activity |
| `tray-attention.ico` | Amber attention (warnings, auth, degraded) |
| `tray-failed.ico` | Failed |

Each ICO embeds **16, 20, 24, 32** px PNG frames.

`../AppIcon.ico` — badge-less builder duck for the Windows application icon (16–256 px).

## PNG previews (`png/`)

Lossless previews at each size for visual review and diffing. Not loaded at runtime.

## Regeneration

Assets are produced by the offline generator (not at app runtime):

```powershell
dotnet run --project tools/GenerateTrayIcons/GenerateTrayIcons.csproj -- C:\src\BuildMonitor
```

Source logic: `tools/GenerateTrayIcons/BuilderDuckRenderer.cs` — programmatic draw tuned for 16/20 px badge legibility. **Do not crop the JPEG concept sheet.**

After regenerating, rebuild TrayApp so embedded resources refresh.

## Code

- `TrayIconPresentationMapper` (Core) — state precedence
- `TrayIconFactory` (TrayApp) — loads embedded ICOs
- `TrafficLightIconFactory` — legacy, unused; remove after visual sign-off
