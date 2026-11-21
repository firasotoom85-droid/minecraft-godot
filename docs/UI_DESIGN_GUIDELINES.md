# UI Design Guidelines

These guidelines preserve the visual and asset decisions from the earlier UI redesign work while describing the direction that is maintained in the current game.

## Visual direction

Use the existing procedural voxel world as the primary menu artwork. The main menu may show the live 3D world behind the interface, while block and item artwork should remain consistent with the game's procedural pixel-art icons.

The established visual language is dark slate and deep teal surfaces, a restrained green accent, warm secondary highlights, thin borders, rounded panels, deliberate spacing, readable muted text, and explicit hover, pressed, focus, and selected states.

Prefer a cohesive interface over isolated redesigns of individual panels. Reuse shared styling and layout behavior across the main menu, world selection and creation, pause/settings, inventory, HUD, controls, achievements, trader, and developer tools.

## Interface surfaces

- **Main menu:** clear navigation with a focused content area for world selection, creation, management, and settings.
- **World selection:** readable saved-world entries with seed, generation preset/version, last-played metadata, and direct play/manage actions.
- **World creation:** keep world name, seed/share code, and generation controls distinct.
- **Pause menu:** prioritize resume and session actions; keep settings and controls consistent with the main menu.
- **Inventory:** distinguish crafting, backpack, equipment, and hotbar areas; make selected slots and tabs unambiguous.
- **HUD:** keep survival indicators and hotbar readable without obscuring the world; use the same panel language for contextual overlays.
- **Settings and secondary panels:** use consistent section cards, scroll behavior, button states, and clear requirement/status labels.

## Artwork and licensing

The live procedural 3D backdrop and runtime-generated block/item icons are preferred because they match the game's visual language and keep the project self-contained.

Do not add unverified stock imagery, proprietary game assets, or third-party fonts with unclear licensing. Any later imported art must be compatible with the project and have its exact source URL, creator, license name, and license URL recorded alongside the asset. Prefer original assets or properly licensed CC0 / CC-BY assets.

## Validation

Review interface changes in the real Godot 4.7.2 .NET game scene at both 1920×1080 and 1280×720. Verify keyboard/controller focus, hover/pressed/selected states, scrolling, mouse capture transitions, and modal ordering. Check the menu backdrop's GPU and frame-time cost separately from gameplay rendering.

These guidelines are design constraints, not a substitute for visual playtesting on target hardware.
