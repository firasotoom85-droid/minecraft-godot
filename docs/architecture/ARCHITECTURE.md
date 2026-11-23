# Architecture overview

This document describes current code ownership boundaries. It is an orientation map, not a replacement for source-level review.

## Runtime composition

The project starts from the main menu scene. GameSession is an autoload shared by the menu and gameplay scenes. It loads settings and bindings, configures audio buses, validates registered content and migrates a legacy save before a world is selected.

When gameplay starts, Main composes the active scene:

- It resolves the selected world, connects VoxelWorld and Player, restores saved player state and creates the gameplay environment.
- It creates and wires the existing services: day/night, graphics, weather, sound, ambient audio, mobs, dungeons, villages, quests, trader, campfires, fluids, boats, inventory, achievements, onboarding, pause/settings and photo mode.
- HUD receives references to the systems whose state it presents. Gameplay events are connected through signals rather than duplicated in the HUD.

The main composition point is visible in scripts/Main.cs. When adding a system, extend the existing owner and lifecycle instead of creating a parallel manager.

## Subsystem ownership

| Concern | Primary files | Responsibility |
|---|---|---|
| Application/session | project.godot, scripts/GameSession.cs, scripts/MainMenu.cs, scripts/Main.cs | Boot, settings, world selection and runtime composition |
| World and streaming | scripts/VoxelWorld.cs, scripts/Chunk.cs, scripts/FarLod.cs | World state, chunk lifecycle, scheduling, caches and near/far terrain |
| Generation | scripts/VoxelWorld.cs, scripts/WorldGenV7.cs through scripts/WorldGenV11.cs, scripts/Biome.cs, scripts/Structures.cs | Deterministic terrain, biomes, decorations and versioned additions |
| Mesh generation | scripts/Mesher.cs, scripts/Chunk.cs, scripts/BlockShapes.cs | Snapshot-based opaque, transparent and collision payload generation; payload application |
| Blocks and items | scripts/BlockId.cs, scripts/ItemId.cs, scripts/BlockDb.cs, scripts/ItemDb.cs, scripts/Recipes.cs | Persisted identity, definitions, item metadata and recipes |
| Player and survival | scripts/Player.cs, scripts/PlayerStats.cs, scripts/Combat.cs, scripts/Inventory.cs, scripts/EquipmentProgression.cs | Input, movement, interaction, stats, inventory and equipment |
| Living world | scripts/Mobs.cs, scripts/Mob.cs, creature scripts, scripts/VillageSystem.cs, scripts/Villager.cs | Population, AI, residents, herds and combat actors |
| Progression | scripts/QuestSystem.cs, scripts/TraderSystem.cs, scripts/DungeonSystem.cs, scripts/AchievementSystem.cs | Quests, trade, dungeons, achievements and rewards |
| World simulation | scripts/FluidSystem.cs, scripts/CropSystem.cs, scripts/FurnaceSystem.cs, scripts/BoatSystem.cs, scripts/Campfires.cs | Fluids, crops, smelting, boats and campfire interactions |
| Rendering and presentation | scripts/Gfx.cs, scripts/WorldEnvironmentFactory.cs, scripts/DayNight.cs, scripts/VoxelLighting.cs, scripts/MaterialResponse.cs, scripts/Chunk.cs, shaders/ | Quality presets, atmosphere, baked lighting and materials |
| Persistence | scripts/Worlds.cs, scripts/WorldArchive.cs, scripts/WorldManagementDialog.cs | Per-world manifests, save records, backups and import/export |
| UI and input | scripts/Hud.cs, scripts/InventoryUi.cs, scripts/PauseMenu.cs, scripts/SettingsPanel.cs, scripts/Keybinds.cs, scripts/GamepadBindings.cs | HUD, panels, settings and rebinding |
| Validation | tests/, scripts/verify.sh, .github/workflows/ | Deterministic tests, soak/capture scenes and CI |

The table names primary owners, not exclusive call sites. Check callers and signal connections before moving responsibility.

## World and mesh pipeline

The world owns authoritative voxel state and the streamed chunk set. Chunk data is generated lazily from the world's seed and generator version, then combined with persisted edits and optional block-shape state.

Mesher.Build takes a consistent snapshot while holding the world's snapshot lock. It captures the edit epoch and builds a MeshPayload using the existing greedy opaque path plus the remaining face/surface and collision data. Worker jobs do not access the scene tree. The world validates the payload epoch and streaming range before applying the result on the main thread. A stale payload is rejected and the affected chunk is queued for a fresh build.

Chunk.ApplyPayload is the renderer/scene boundary: it installs the opaque, glass, water and lava surfaces and creates collision resources when that chunk was configured for collision. Pooled mesh data must be released on all completion, cancellation and shutdown paths.

FarLod uses the same world data and edit-aware boundary. It builds coarse terrain tiles on workers and applies mesh nodes on the main thread. It is not a second authoritative world store.

## Gameplay and rendering boundaries

Player owns user-driven movement and interaction. It calls the world mutation seam for block changes and reports gameplay events to services such as quests, achievements and onboarding. Mobs owns population coordination while each creature implementation remains responsible for its actor behavior. QuestSystem, TraderSystem, DungeonSystem and AchievementSystem build on those existing event paths.

The rendering stack uses the shared block/material registries and voxel-light data. Gfx owns quality-preset policy; WorldEnvironmentFactory builds the shared environment; DayNight and Weather update existing environment state; ChunkMaterials and shaders consume the registered material and baked-light response. Do not create a per-block light-node system or a competing shader palette.

## Persistence boundaries

WorldStore is the serialization and filesystem boundary. The selected world's manifest defines its seed, generator version and preset. World data and player data are stored separately. WorldArchive provides portable import/export and validates archive contents before they are moved into the worlds directory.

Gameplay systems should use existing world/save ownership rather than writing files independently. See DATA_LIFECYCLE.md for the mutation, mesh and save sequence.

## Testing boundaries

tests/TestRunner.tscn is the headless entry point. TestRunner.cs registers deterministic suites. Visual-capture scenes exercise the real main scene and production resources; soak scenes exercise the live scene and simulation over time. These are different validation classes and must be reported separately.
