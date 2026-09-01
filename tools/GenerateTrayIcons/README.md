# GenerateTrayIcons — REJECTED for production (#95)

This offline tool produced programmatic builder-duck tray ICOs via `BuilderDuckRenderer.cs`.

**Manual tray QA failed.** Generated output does not preserve approved mascot identity at 16–20 px and must not ship.

## Do not use for production

- Do not run this tool to refresh deployed tray icons.
- Do not treat "close to concept" programmatic draws as sufficient.
- Do not tweak radii/colours in `BuilderDuckRenderer` expecting tray approval.

## Future asset pipeline

Externally supplied transparent PNG masters (five states × 16/20/24/32 px) will be committed under `src/TrayApp/Assets/tray/` and converted to multi-resolution ICOs. Cursor wires those into TrayApp; **do not redraw the mascot in code.**

Retained for reference only until removed or repurposed as a PNG→ICO packager for supplied artwork.
