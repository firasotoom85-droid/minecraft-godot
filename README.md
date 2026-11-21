# Minecraft-like — a voxel survival sandbox in Godot 4.7 (C#)

A from-scratch, infinite-world voxel game built with **Godot 4.7.2 (.NET)** and
**C#**. It keeps the Minecraft loop — mine, craft, build, survive the night —
and adds a cozy-leaning survival layer: campfires that heal and repel hostiles,
an adaptive procedural soundscape, a live 3D main menu, photo mode, and an
event-driven “First Night” guide.

The project is a long-running solo build with an extensive design log in
[`PLAN.md`](PLAN.md) and a forward roadmap in [`ROADMAP.md`](ROADMAP.md).

## Highlights

- **Infinite streaming voxel world** — 16×128×16 chunks, 13 terrain biomes,
  climate-shaped continents, smooth river valleys, mountain ranges, multi-form
  caves, aquifers, magma pockets, depth-aware ore veins, biome-specific trees,
  surface ruins and underground chambers. New worlds use generator V8, with
  broader river banks, improved cave entrances and denser forest canopies.
  Generator versions 1–7 retain their frozen terrain rules, golden chunk hashes
  and save compatibility.
- **Greedy meshing** — merged coplanar full-cube quads cut triangle count
  roughly in half; per-face meshing remains available for comparison. Vertex-light
  and AO samples are part of the merge key, preserving lighting boundaries.
- **Architectural voxel building** — bottom/top/double slabs and four-way,
  upside-down stairs use a shared 2×2×2 occupancy definition for geometry,
  collision, ray selection and placement. Partial faces are split only at shape
  boundaries; full cubes retain the greedy path. Shape surfaces use the regular
  atlas, AO and voxel-light pipeline, with procedurally painted original textures.
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
- **Equipment progression** — wood/stone/iron/diamond swords, bows and ballistic
  arrows, directional shields, weapon durability, critical strikes and
  knockback. Defeats award experience levels; a craftable Enchanting Table
  applies Sharpness, Protection and Unbreaking and upgrades tools and armor one
  tier while preserving durability and enchantments. Ember Warden trophies can
  be forged into an end-game Ember Blade. Legacy saves load with zero XP and
  unenchanted stacks.
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
  and byte-parity anchors so old worlds never change under new worldgen. Save
  format v4 adds optional block-shape state, villager and quest records, and
  boat state; v1-v3 worlds load with defaults and generator versions remain
  unchanged.
- **Portable world management** — checksummed ZIP export/import, duplicate-name
  handling, staged imports with path-traversal and archive-expansion protection,
  backup browsing and safe restore, and per-world playtime/size metadata.


## World management and portability

Open **Play** and choose **Manage** on a world to view its seed, generator
version, cumulative playtime, last-played timestamp, disk size, and available
restore points. **Backups / Export** lists the rotating save snapshots and
restores a selected valid snapshot only after confirmation; the current file is
preserved as a safety backup during restoration.

Use **Export ZIP** to create a portable archive containing the complete world
folder, metadata, and available save backups. Use **Import ZIP** from the world
selection screen to validate and import an archive. Imports are extracted to a
staging directory, checked against the archive's SHA-256 file index, and only
then moved into the worlds folder. Existing worlds are not overwritten; a
name suffix is assigned when an imported world has a duplicate name. Archives
with unsafe paths, links, unsupported save/generator versions, corrupt data
without a valid backup, or excessive expanded size are rejected.

Older manifests without a playtime field continue to load with zero playtime;
existing world-save versions and generator versions are not rewritten by import.

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
`res://tests/TestRunner.tscn`; it exits non-zero on failure or on the default
300-second test timeout. The timeout can be changed with
`GODOT_TEST_TIMEOUT=seconds`. The runner prints one line per suite and a final
check/failure count. If `GODOT` is unset, the script looks on `PATH` and in
common install locations.

The integrated 2026-11-21 suite reports **4,238 checks, 0 failures** across 28
suites, with 0 build warnings. It includes the combined save round-trip
(shapes + villagers + quests + boats), quest reward wiring, fluid/shape
boundaries, legacy saves, every pinned block/item ID and V1–V8 golden chunk
hashes. The headless runner and a verbose real-scene soak both exit without
leaked ObjectDB instances or RIDs. The project-specific reliability audit,
including seven confirmed fixes and the remaining manual-coverage gaps, is in
[`docs/RELIABILITY_AUDIT_2026-11-21.md`](docs/RELIABILITY_AUDIT_2026-11-21.md).

### Performance benchmarking

An opt-in runner at `res://tests/PerformanceRunner.tscn` benchmarks the real
main scene with a disposable V8 world and deterministic routes for open
terrain, dense forest, mountain/river, villages, caves, ocean, storms, hostile
encounters and fast exploration. It exports a JSON summary plus per-frame CSV
under `user://benchmarks/`. The report includes frame-time percentiles,
Godot process/physics timings, process/managed memory and GC, VRAM, draw calls,
mesh-build distribution, chunk counts and queue high-water marks. It does not
write to the selected player world.

See [`docs/PERFORMANCE_BENCHMARKS.md`](docs/PERFORMANCE_BENCHMARKS.md) for
arguments, repeatability requirements and interpretation. Headless runs are
for lifecycle smoke tests only, not rendered performance measurements. The
existing `docs/PERF_BASELINE.md` numbers remain historical until controlled
Debug/Release runs are captured on the reference laptop and desktop.

### Reliability audit

The 2026-11-21 audit found and fixed seven defects that ordinary smoke testing
missed: boat-death respawn stranding, duplicate mob death rewards, non-stackable
inventory overflow, campfire food deletion on a full inventory, world-delete
path traversal, stale villager state at quit-time saves, and modal input
stacking. Each fix has a headless regression except the input-ordering guard,
which is covered by code-path review. Details, severities and unresolved
suspicions are in
[`docs/RELIABILITY_AUDIT_2026-11-21.md`](docs/RELIABILITY_AUDIT_2026-11-21.md).

## World-generation visual validation

V8 was inspected in the real 3D game scene using Godot 4.7.2 .NET, Forward+
Vulkan rendering and an NVIDIA GPU. Baseline and V8 captures use matching
coordinates for a steep river bank, river valley, mountain, forest and cave
region. The paired images and findings are in
[`docs/worldgen/V8_VISUAL_VALIDATION.md`](docs/worldgen/V8_VISUAL_VALIDATION.md).

A fresh integrated-build capture at seed 1337 (V8, river valley at 512,-372)
rendered 49 chunks at 1600×900, and a second capture at the deterministic seed
1337 village (-684,1303) showed generated houses, paths and farmland in the
real scene. Both exited without off-tree transform errors after the resident
save path was fixed. These inspection frames were not committed as new assets.

Capture another seed/coordinate from the real scene with:

```bash
godot --path . res://tests/WorldgenVisualCapture.tscn -- 1337 8 512 -372 res://docs/worldgen/screenshots/custom-v8.png
```

Arguments are seed, generator version, world X, world Z and output PNG path.
The capture scene waits for surrounding chunks to stream before saving the
1600×900 viewport image.

### Long-duration survival soak

An opt-in integration runner loads the real main scene in Normal Survival and
exercises naturally spawned herds, feeding/breeding, hostile encounters, cave
fauna, weather and day/night cycles. It uses an isolated
`user://tests/survival-soak-*` world, verifies the world/player saves can be
read back, then removes that temporary directory; it never saves over the
player's selected world. Population and cave-encounter counts are logged every
minute. Run a 60-minute soak with:

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 bash scripts/soak-survival.sh 3600
```

Use `300` for a five-minute smoke run. The runner reports PASS only when it
finds a generated cave, records cave encounters, successfully feeds the natural
herd and observes a juvenile, retains a live passive population, reads back
both save files, and removes the temporary world. It reserves the final two
hotbar slots for wheat and seeds because a new-world starter kit occupies all
nine hotbar slots and normal `Inventory.Add` then places food in the backpack,
which cannot be selected for animal feeding.
The runner uses the real main scene, but fixes its test-only graphics load
to LOW, a two-chunk view radius, three meshing workers, and no far-LOD ring.
This avoids turning a gameplay soak into an accidental MAX-preset streaming
stress test on a small CI host. World generation, colliders, Jolt physics,
survival rules, mob AI, feeding/breeding, weather, day/night and save handling
remain enabled. It also caps the uncapped headless render loop at 60 FPS to
avoid saturating the Godot 4.7.2 Jolt job pool; it does not use fixed-FPS
simulation, so survival time remains real-time. The script bounds runtime to
duration + 300 seconds by default; override that with
`SOAK_TIMEOUT_SECONDS`. The soak remains separate from the fast headless
suite and is not a human combat or visual gameplay playtest.

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

Bindings are rebindable from **Esc → Controls**. Keyboard and controller
bindings are stored separately; the controller map is additive and never
erases a keyboard binding.

| Controller | Action |
|---|---|
| Left stick | Move |
| Right stick | Look |
| `A` | Jump / swim up |
| `B` | Toggle creative flight |
| `Y` | Inventory |
| `Start` | Pause |
| Left shoulder | Place / use |
| Right shoulder | Mine / attack |
| Right stick click | Pick block |
| D-pad left / right | Previous / next hotbar slot |
| D-pad up / down | Minimap / achievements journal |

Menus support controller focus, confirm and cancel. Keyboard and controller buttons are configured separately and persist in `user://keybinds.cfg` and `user://gamepad.cfg`.

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

### Buckets and boats

Buckets are unstackable inventory items. An empty bucket collects only a true
source-water or lava cell; flowing water cannot be scooped. Filled buckets
place a new source into a loaded, replaceable voxel and become empty again.
Both operations use `VoxelWorld.SetBlock`, so edits continue through the
existing save, lighting, and meshing path. Craft a bucket from three iron
ingots in a V; craft a wooden boat from five planks in a U.

Place a wooden boat on a loaded water surface. Right-click a nearby boat to
board, use WASD to accelerate and steer, hold Shift to brake, and press Space
to dismount to a nearby clear cell. The boat samples the existing source and
flowing-water heights for buoyancy, collides with streamed terrain, and stops
before any part of its hull can enter an unloaded chunk. Boat position,
heading, velocity, and rider state are optional world-save fields; older
worlds load without a vehicle list.

## Project layout

| Path | Contents |
|---|---|
| `scripts/` | Gameplay and engine code (C#), plus `verify.sh` |
| `scenes/` | `main.tscn` (game) and `main_menu.tscn` |
| `shaders/` | Water shader |
| `tests/` | Headless test runner and suites |
| `docs/` | Performance baselines, benchmark procedure and audits |
| `addons/godot_ai/` | Editor MCP assistant addon (development tooling) |
| `PLAN.md` | Completed milestone log (M0–M4b) |
| `ROADMAP.md` | Forward plan (M5–M10) and progress log |

## Combat equipment and enchantments

Survival combat includes tiered swords, a bow with gravity-driven arrow projectiles,
front-facing shield blocks against melee and ranged attacks, and armor mitigation.
Mob defeats award experience; Crystal Wisps remain a renewable lapis source, and
the wandering trader now sells one lapis for two gold ingots as a predictable
fallback. Craft an **Enchanting Table** from two diamonds, two iron ingots, and
one lapis lazuli; right-click it to open the dedicated station. Its **Enchant /
Upgrade** panel applies Sharpness, Protection, and Unbreaking (I–III) using
experience levels and lapis, and upgrades supported gear while preserving its
remaining durability and enchantments. Failed trades do not consume currency
when the output cannot fit in the inventory.

The progression is wood → stone → iron → diamond, with mining harvest gates
preserved: stone picks unlock iron ore, while iron picks unlock gold and diamond
ore. Ore is depth-banded with seeded V7 veins; mountains have denser gold and
diamond hash sampling. One coal burns for 80 seconds and smelts eight items at
ten seconds each; logs burn for 60 seconds, planks for 15, and sticks for five.
Furnace updates carry elapsed time across fuel boundaries, including larger
frame steps. Raw meat is low-value food; cooked meat restores 5–7 hunger and
heals one health, and bread offers a farmable five-hunger/one-health option.

Armor points reduce physical damage by four percent per point, capped at 80%
base mitigation; Protection then reduces the remaining damage by four percent
per enchantment level on each piece. Unbreaking I/II/III prevents 50%/66.7%/75%
of equipment wear. Full iron armor has 15 points (60% base
reduction) and full diamond armor has 20 (80%). Starvation and drowning bypass
armor mitigation and do not wear armor. Other landed physical/environmental hits
wear equipped armor; successful shield blocks wear the shield. Tools used as
emergency melee weapons also consume their durability on a landed hit.

Peaceful mode suppresses hostile spawns, cancels pending hostile attacks,
removes hostile projectiles and keeps the Ember Warden dormant.
Normal and hard share the same enemy health pools and encounter structure; hard
raises hostile damage and increases the surface/cave hostile population cap from
seven to ten. Natural passive herds target eight animals, reserving two slots
for breeding offspring under the ten-animal population limit. The Warden has
120 health, summons two adds below 66% health
and three below 33%, deals four (normal) or five (hard) damage with its slam,
and guarantees an Ember Core plus
25 experience on defeat. Experience and enchantment levels are stored in the
existing player/inventory save files; old saves default these fields to zero.
See [`docs/SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md`](docs/SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md)
for the balance table, deterministic scenarios and verification record.

## World saves

Each world lives in `user://worlds/<id>/`:

- `manifest.json` — seed, generator version, preset, spawn, play time.
- `world.json` — edits, time, containers, furnaces, flags, block-shape states,
  discovered villagers, multi-stage quest progress, and optional boat state.
- `player.json` — position, inventory, mode, health, hunger, respawn, experience, and optional per-item enchantment levels.

Writes are atomic (temp file + rename) and keep two rotating backups. Old
worlds remain loadable: block/item ids are append-only and worldgen changes are
gated behind generator versions. The v4 fields are optional, so v1-v3 files
load without shape, villager, quest or boat data.

## Persisted block and item IDs

`BlockId` and `ItemId` are save data, not presentation order. Existing values
are never renumbered or reused; new content is appended and pinned by tests.

| Range | Contents |
|---|---|
| `BlockId` 0–45 | Original terrain, plants, fluids and M6/M7 blocks, unchanged |
| `BlockId` 46–51 | Oak-planks, cobblestone and brick slabs/stairs |
| `ItemId` 0–90 | Original items, armor, tools, food, boss trophy and enchanting table, unchanged |
| `ItemId` 91–96 | Slab and stair items, matching the block order above |
| `ItemId` 97–100 | Empty bucket, water bucket, lava bucket, wooden boat |

`tests/ContentTests.cs` pins every block and item value and asserts that the
building and transportation ranges remain disjoint. `tests/WorldgenTests.cs`
pins golden chunk hashes for V1, V3, V5, V6, V7 and V8.

## Villages, residents and quests

Generator V8 adds deterministic Plains, Forest, Snow and Desert settlements
without changing the V7 terrain sampler. Villages are placed on dry, low-slope
clearings and avoid existing surface structures, campfire clearings, cave
openings and nearby underground chambers. Chunk-clipped houses, roads, a
central square, light posts, chests, workstations and irrigated farms are
stamped from the same site seed, including at negative chunk coordinates.
Worlds generated with V1–V7 keep their original terrain and structure placement.

Five named residents populate each discovered village: farmers, merchants,
blacksmiths, librarians and guards. They follow home/work/gathering schedules,
use the shared mob movement and obstacle-steering code, flee nearby hostiles,
and offer profession-specific trades using the existing Gold Ingot economy.
Resident identity, profession, home/work anchors, health, position and trade
history are saved in optional V4 world fields.

Talking to residents opens their trading and quest panel. The reusable quest
service supports exploration, gathering, crafting, trading, combat and dungeon
objectives, including multi-stage rewards. Quest acceptance, stage progress,
completion and reward claims persist in `world.json`.

## Known limitations

- **Building shapes are limited to slabs and stairs.** The occupancy-mask
  architecture is ready for walls, fences, gates, doors, trapdoors, glass panes,
  pressure plates, buttons and signs, but none of those categories is
  implemented yet. There are no vertical slabs.
- **Visual validation is representative, not exhaustive.** V8 has matched
  GPU-rendered before/after captures for river, steep-bank, mountain, forest and
  cave locations across four seeds. The integrated village, boat and block-shape
  presentation has automated coverage but no complete human visual playthrough
  across every biome, preset and UI state.
- **Soak coverage is headless.** The 5- and 60-minute survival soaks exercise
  the real main scene, physics, mobs, weather, saves and cleanup, but they do not
  replace a human combat, building or visual playtest. The five-minute run is
  recorded below; the one-hour run uses the same pass criteria.
- **High-end hardware not measured.** Performance was recorded on the MX230
  laptop only; no RTX 4060-class machine was available for comparison.
- **Controlled performance retest pending.** A repeatable runner now exists
  with fixed seeds/routes and JSON/CSV output, but no new comparable Release
  baseline has been recorded yet. The historical 30.6 FPS result is a single
  unseeded Debug measurement. See `docs/PERFORMANCE_BENCHMARKS.md` and
  `docs/PERF_BASELINE.md`.

## License

[MIT](LICENSE). Third-party assets and the bundled editor addon keep their own
licenses; see [`addons/godot_ai/LICENSE`](addons/godot_ai/LICENSE).


## Fluid simulation internals

Water uses a deduplicated active-cell queue with bounded per-tick work rather than rescanning the local volume every tick. Unloaded chunk neighbours are treated as unknown rather than air; source blocks are periodically rescheduled, and successful world block edits wake nearby fluid cells. Two horizontal sources over a solid floor can regenerate a source. All fluid changes still pass through `VoxelWorld.SetBlock`. Swimming buoyancy respects floor/ceiling contacts. Underwater and lava fog use separate blended environments, and underwater low-pass filtering is limited to SFX/ambient buses so master/UI audio remains clear.

## Creature perception and cave stealth

Cave Stalkers, Crystal Wisps, Hollows and nocturnal Dreadwings use the existing
packed sky/block voxel-light maps to decide when to acquire a target. Nearby
torches can suppress dark-adapted detection; the Cave Stalker gains a modest
pounce and bite bonus in very low light. Creature light samples and terrain
line-of-sight results are cached briefly and invalidated when the sampled cell
or observer/target positions change.

Mining ticks, completed block breaks, placements and footsteps feed a bounded
24-event acoustic ring owned by the existing mob director. Hostiles investigate
recent audible events when they have no current visual target, retain a short
last-seen position when sight is lost, and stop pursuing directly through
terrain. Passive herds add heading alignment and species-specific flight
profiles; newborns retain both parents as guardians and follow the nearest
living parent when separated. Existing grazing, hunger, rest, weather shelter,
breeding, combat, population caps and chunk streaming remain in their original
systems.

The mob-perception integration suite covers a torch-lit cave straddling a
negative-coordinate chunk boundary, emitter removal, actual player mining and
placement signals, acoustic investigation, parent following, freed-reference
pruning, and a terrain physics blocker. The opt-in survival soak also injects
sound stimuli and checks creature positions and observed acoustic investigations.
A five-minute main-scene soak passed with capped populations, eight cave
encounters, 218 sound investigations, 22,621 juvenile observations, successful
save/reload, temporary-world cleanup and no non-finite creature positions. The
runner defaults to one hour; that full 3600-second run is recorded in the
integration report and is separate from a human combat or visual playtest.
