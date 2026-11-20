# UI redesign

## Visual direction

The redesign uses the existing procedural voxel world as the main visual artwork.
The menu continues to show the live 3D world behind the interface, while item
art remains the game's generated pixel-art icons. No unrelated stock imagery,
unverified Creative Commons downloads, or third-party font files were added.

The interface uses a dark slate / deep teal palette with a restrained green
accent, warm secondary highlights, thin borders, rounded panels, stronger
spacing, readable muted text, and explicit hover / pressed / selected states.

## Updated surfaces

- **Main menu:** left navigation rail, brand block, editorial hero panel, and
  a right content stage for world selection, creation, management, and settings.
- **World selection:** card-based saved-world rows with seed, generation preset,
  generation version, last-played metadata, and direct play/manage actions.
- **World creation:** separated name, seed/share-code, and generation controls.
- **Pause menu:** session summary, resume/save/settings/controls/title actions,
  plus the shared scrollable settings layout.
- **Inventory:** larger item cells, clearer craft/backpack/equipment grouping,
  distinct inset recipe cards, and visible selected tab/slot states.
- **HUD:** a restrained backing plate around survival indicators and the hotbar,
  with a teal selected slot, plus shared panel styling for contextual overlays.
- **Settings, controls, achievements, and trader:** section cards, consistent
  button styling, scrollable long lists, and clearer state / requirement labels.

## Asset and licensing decision

No new raster artwork was needed for this pass: the live procedural 3D backdrop
and runtime-generated block/item icons already match the game's visual language.
Avoiding additional image dependencies also keeps the project self-contained
and avoids uncertain attribution or license compatibility. Any later imported
art should be CC0 / CC BY with its exact source URL, creator, license name, and
license URL recorded alongside the asset.

## Validation

The repository's GitHub Actions workflow performs a .NET 9 build and runs the
Godot 4.7.2 Mono headless test runner. The UI redesign branch should be merged
only after its latest branch-head workflow has passed. Visual layout should
also be reviewed at both 1920×1080 and a smaller 1280×720 viewport in-game.
