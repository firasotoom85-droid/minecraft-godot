# Minecraft-like — a voxel survival sandbox in Godot 4.7 (C#)

A from-scratch, infinite-world voxel game built with **Godot 4.7.2 (.NET)** and
**C#**. It keeps the Minecraft loop — mine, craft, build, survive the night —
and adds a cozy-leaning survival layer: campfires that heal and repel hostiles,
an adaptive procedural soundscape, a live 3D main menu, photo mode, and an
event-driven “First Night” guide.

The project is a long-running solo build with an extensive design log in
[`PLAN.md`](PLAN.md) and a forward roadmap in [`ROADMAP.md`](ROADMAP.md).

## Highlights

- **Infinite streaming voxel world** — 16×128×16 chunks, 4 biomes, rivers,
  caves, lava, ores, trees, and deterministic structures (towers, ruins, mines,
  and the Ember Deep dungeon).
- **Greedy meshing** — merged coplanar quads cut triangle count roughly in half;
  per-face meshing remains available for comparison. Vertex-light and AO samples
  are part of the merge key, preserving lighting boundaries.
- **Voxel lighting** — packed 4-bit sky/block light, sunlight through transparent
  voxels, warm multi-source propagation with opaque occlusion, cave darkness and
  live place/break updates across chunk boundaries. Emitter edits incrementally
  add/remove block light in cached 3×3 regions; opacity edits invalidate the
  affected light maps so direct sun and cave spill are recalculated. Lighting is
  baked into mesh vertex channels; no per-torch light-node pool is required.
- **World presets** — normal, flat, amplified, and floating sky islands, with
  share codes and per-world seeds.
- **Survival** — health, hunger, breath, drowning, fall damage, softcore
  respawn, armor, tools with durability, fishing, farming, and cooking.
- **Block interaction** — material-specific hardness and tool speeds, harvest
  tiers and drops, creative instant mining, ten crack stages, block chips, and
  material-matched procedural breaking and placement sounds.
- **A living world** — species-specific sheep, cow, pig and chicken herds use
  distinct group sizes and spacing, react to danger, graze grass into dirt as
  hunger rises, and raise young that mature over time. Procedural articulated
  legs animate their gait; passive animals rest at night and seek overhead
  shelter during rain and storms. Briarbacks defend birch forests.
- **Creature combat** — Shades and Rots coordinate detection, Hollows and
  Crystal Wisps telegraph dodgeable ranged shots, Cave Stalkers pounce from
  caves, and rare Dreadwings flank players at night; the Ember Warden remains
  the arena boss. A wandering trader, weather, lightning and ambient life
  complete the ecosystem.
- **Interface** — shared pixel-art menu, HUD, inventory, chest and furnace styling; viewport-aware panel scaling; slot hover feedback and tooltips; drag-and-drop and Shift-click stack movement.
- **First-person presentation** — selected-item icon viewmodel with swing animation, layered onto the existing arm, swimming strokes, head bob and landing response.
- **Controller support** — left-stick movement, right-stick look, gamepad actions for mining/placing, hotbar cycling, inventory, pause, map and journal, with persistent button rebinding.
- **Quality of life** — minimap with zoom and waypoints, achievements journal,
  keyboard/controller rebinding, accessibility toggles, photo mode, dev console,
  and an automatic quality governor.
- **Deterministic saves** — per-world folders, atomic writes, rotating backups,
  and byte-parity anchors so old worlds never change under new worldgen.

## Requirements

- [Godot 4.7.2 **.NET / mono**](https://godotengine.org/download) editor.
  The standard (non-.NET) editor cannot run C# projects.
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (`global.json` pins the
  feature band and rolls forward within .NET 9).

## Build and run

```bash
dotnet build
godot --path .            # run the game
godot --editor --path .   # open the editor instead
```

You can also open `project.godot` from the Godot project manager and press
**F5**. The first launch creates `user://worlds/` under the Godot user data
directory (`~/.local/share/godot/app_userdata/minecraft/` on Linux).

## Tests

The test suite runs inside a headless Godot instance:

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 ./scripts/verify.sh
```

`scripts/verify.sh` builds the project and runs
`res://tests/TestRunner.tscn`; it exits non-zero on failure. The runner prints
one line per suite and a final check/failure count. If `GODOT` is unset, the
script looks on `PATH` and in common install locations.

### Long-duration survival soak

An opt-in integration runner loads the real game scene and exercises multiple
herds, breeding, hostile encounters, cave fauna, day/night transitions, and
storm shelter behavior. It logs population/cave encounter counts every minute
and saves the world at completion. Run a 60-minute soak with:

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 bash scripts/soak-survival.sh 3600
```

Use a shorter duration such as `300` for a smoke run. The soak is deliberately
separate from the fast CI unit suite; it must complete with a cave encounter,
observed juvenile, and live passive population to report PASS.

## Controls

| Input | Action |
|---|---|
| `WASD` | Move |
| `Space` | Jump / swim up / climb (double-tap to fly in creative) |
| `Shift` | Sprint / dive |
| Mouse | Look |
| Left mouse | Mine / attack (hold to keep; release or change target to cancel) |
| Right mouse | Place / use / eat / cook / rest / tame |
| Middle mouse | Pick block |
| `1`–`9`, mouse wheel | Select hotbar slot |
| `E` | Inventory / crafting |
| `Esc` | Pause menu |
| `M` | Minimap · `[` / `]` zoom |
| `P` | Photo mode (arrow keys scrub time) |
| `J` | Achievements journal |
| `H` | Help overlay |
| `F3` | Info panel |
| `F6` | Cycle quality preset |
| `F7` / `F8` | View distance down / up |
| `F9` | Mute sounds |
| `/` | Dev console |

Bindings are rebindable from **Esc → Controls**. Keyboard and controller buttons are configured separately and persist in `user://keybinds.cfg` and `user://gamepad.cfg`.

**Controller defaults:** left stick moves, right stick looks, A jumps, B toggles creative flight, left/right shoulder place/mine, right-stick click picks a block, Y opens inventory, Start pauses, D-pad left/right cycles the hotbar, and D-pad up/down opens the map/journal.

Inventory supports click-to-pick/place, right-click half-stack splitting, Shift-click quick transfer, and drag-and-drop between player, chest, crafting and furnace slots.

## Fluid behavior

Source water remains full-height. Water spreads downward and outward through
seven progressively shallower flowing levels. Falling columns form continuous
waterfalls; unsupported flow recedes, solid terrain blocks propagation, and
plants/torches are displaced. Water touching lava converts the lava to the
existing cobblestone reaction block; lava itself remains static, viscous, and
damaging rather than behaving like water.

The fixed-step simulator examines a small window of already-streamed chunks
around the player and commits at most six edits per tick through
`VoxelWorld.SetBlock`. It does not generate chunks, alter terrain generation,
or scan the world. Swimming uses buoyancy and smooth acceleration: Space swims
up, Shift dives, shallow water slows wading, and head submersion controls breath
loss. Breath recovers above water; mining is slower while fully submerged.

## Project layout

| Path | Contents |
|---|---|
| `scripts/` | Gameplay and engine code (C#), plus `verify.sh` |
| `scenes/` | `main.tscn` (game) and `main_menu.tscn` |
| `shaders/` | Water shader |
| `tests/` | Headless test runner and suites |
| `docs/` | Performance baseline notes |
| `addons/godot_ai/` | Editor MCP assistant addon (development tooling) |
| `PLAN.md` | Completed milestone log (M0–M4b) |
| `ROADMAP.md` | Forward plan (M5–M10) and progress log |

## World saves

Each world lives in `user://worlds/<id>/`:

- `manifest.json` — seed, generator version, preset, spawn, play time.
- `world.json` — edits, time, containers, furnaces, flags.
- `player.json` — position, inventory, mode, health, hunger, respawn.

Writes are atomic (temp file + rename) and keep two rotating backups. Old
worlds remain loadable: block/item ids are append-only and worldgen changes are
gated behind generator versions.

## License

[MIT](LICENSE). Third-party assets and the bundled editor addon keep their own
licenses; see [`addons/godot_ai/LICENSE`](addons/godot_ai/LICENSE).


## Fluid simulation update

Water uses a deduplicated active-cell queue with bounded per-tick work rather than rescanning the local volume every tick. Unloaded chunk neighbours are treated as unknown rather than air; source blocks are periodically rescheduled, and successful world block edits wake nearby fluid cells. Two horizontal sources over a solid floor can regenerate a source. All fluid changes still pass through `VoxelWorld.SetBlock`. Swimming buoyancy respects floor/ceiling contacts. Underwater and lava fog use separate blended environments, and underwater low-pass filtering is limited to SFX/ambient buses so master/UI audio remains clear.
