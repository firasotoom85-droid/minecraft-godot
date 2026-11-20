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
  per-face meshing remains available for comparison.
- **World presets** — normal, flat, amplified, and floating sky islands, with
  share codes and per-world seeds.
- **Survival** — health, hunger, breath, drowning, fall damage, softcore
  respawn, armor, tools with durability, fishing, farming, and cooking.
- **Block interaction** — material-specific hardness and tool speeds, harvest
  tiers and drops, creative instant mining, ten crack stages, block chips, and
  material-matched procedural breaking and placement sounds.
- **A living world** — herding sheep, cows, pigs and chickens react to nearby
  danger, gather in small groups, accept species-appropriate breeding food, and
  raise young that mature over time. Briarbacks defend birch forests.
- **Equipment progression** — wood/stone/iron/diamond swords, bows and arrows, directional shields, weapon durability, critical strikes and knockback. Ember Warden trophies can be forged into an end-game Ember Blade.
- **Creature combat** — Shades and Rots coordinate detection, Hollows and
  Crystal Wisps telegraph dodgeable ranged shots, Cave Stalkers pounce from
  caves, and rare Dreadwings flank players at night; the Ember Warden remains
  the arena boss. A wandering trader, weather, lightning and ambient life
  complete the ecosystem.
- **Quality of life** — minimap with zoom and waypoints, achievements journal,
  key rebinding, accessibility toggles, photo mode, dev console, and an
  automatic quality governor.
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

Bindings are rebindable from **Esc → Controls**.

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

## Combat equipment and enchantments

Survival combat includes tiered swords, a bow with gravity-driven arrow projectiles,
front-facing shield blocks against melee and ranged attacks, and armor mitigation.
Mob defeats award experience; Crystal Wisps can drop lapis lazuli. Craft an **Enchanting Table** from four diamonds, four iron ingots, and an Ember Core;
right-click it to open the dedicated station. Its **Enchant / Upgrade** panel applies Sharpness, Protection, and Unbreaking (I–III)
using experience levels and lapis, and upgrades supported gear while preserving
its remaining durability and enchantments. Experience and enchantment levels are
stored in the existing player/inventory save files; old saves default these fields
to zero.

## World saves

Each world lives in `user://worlds/<id>/`:

- `manifest.json` — seed, generator version, preset, spawn, play time.
- `world.json` — edits, time, containers, furnaces, flags.
- `player.json` — position, inventory, mode, health, hunger, respawn, experience, and optional per-item enchantment levels.

Writes are atomic (temp file + rename) and keep two rotating backups. Old
worlds remain loadable: block/item ids are append-only and worldgen changes are
gated behind generator versions.

## License

[MIT](LICENSE). Third-party assets and the bundled editor addon keep their own
licenses; see [`addons/godot_ai/LICENSE`](addons/godot_ai/LICENSE).
