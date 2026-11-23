# ROADMAP — 1.0 “Cozy Survival” (M5–M10)

Generated 2026-10-21 from a code audit of the current project plus the
progress recorded in `PLAN.md`. `PLAN.md` remains the historical progress log
(M0–M4b). This file now combines that original forward plan, historical
implementation snapshots, and the current milestone audit at the top; dates and
“superseded” markers distinguish the records from current priorities.

**Baseline note:** this roadmap began as a source audit before the M5
verification work. Historical measurements remain in docs/PERF_BASELINE.md;
the repeatable procedure is in docs/PERFORMANCE_BENCHMARKS.md. The MX230 now
has a controlled Release baseline and Milestone 2 comparison in
docs/PERFORMANCE_MILESTONE_2_MX230.md. The equivalent RTX 4060 comparison
remains pending.

---

## Current milestone audit — 2026-11-22

This audit is based on the implemented code, not on the original schedule.

| Milestone | Status | Evidence | Remaining |
|---|---|---|---|
| **M5** Foundation & guardrails | Complete | `BlockCount` 128, `TileCount` 256, append-only content validator, procedural atlas audit/report, 48-suite headless runner, `WorldSave` V4 with atomic backups, performance runner with JSON/CSV output | RTX 4060 comparison and a new controlled Release baseline |
| **M6** Base & progression | Complete | Torch/light propagation, chest, furnace and ingots, armor, farming, bed, ladder, swimming, fluid buckets | Broader survival balance and human playtesting |
| **M7** Living world & goals | Complete | Species herds, hostile roster, dungeon and Ember Warden, trader, fishing, achievements, villages/quests | Human combat and quest readability review |
| **M8** UX, platform & 1.0 | Partially complete | Rebinding, gamepad actions, contextual onboarding, world management/portability, photo mode/quality governor, export presets | Arabic/RTL localization and authored music tracks are not implemented; final 1.0 sign-off remains |
| **M9** Scale & online | Not started | No multiplayer, dimensions, or height-256 world support in the codebase | Deliberately post-1.0 |
| **M10** Post-1.0 | Not started | No data-driven content/mod API or additional non-cube families beyond slabs and stairs | Deliberately post-1.0 |

**PRs #49–#57 integration:** the nine branches were merged on
`integration/pr49-57` with their unique changes preserved, and that branch was
merged into `main` through **PR #58**. `BlockId` 59 and `ItemId` 108 are
appended, save format remains V4, and V1–V10 generation stays byte-identical.
V11 is deterministic across positive/negative coordinates and reversed chunk
order, and its only delta from V10 is air cells becoming shoreline reeds. The
full contribution map and validation are in
[`docs/INTEGRATION_PR49_57.md`](docs/INTEGRATION_PR49_57.md).

**PRs #59–#68 integration:** the ten branches in this batch are merged on
`integration/pr59-68` from `main` at `b7a4dd9`. Item icon mapping (#59), the
material-gallery quality report (#60), smooth cave exposure (#61), rare and
enchanted item presentation (#62), phase-aware directional sunlight (#63), AO
seam coverage (#64), the procedural atlas report (#65), camera-depth distance
haze (#66), snow/sand/ash surface variation (#67) and unified warm emitter
lighting (#68) now coexist in one implementation. `BlockId` 60 / `ItemId` 109
(Ash) are appended after every existing value; save format stays V4; V1–V11
generation is byte-identical. The contribution map, conflict resolutions and
measured validation are in
[`docs/INTEGRATION_PR59_68.md`](docs/INTEGRATION_PR59_68.md).

**PRs #70–#79 integration:** nine branches are merged on
`integration/pr70-79` from `main` at `c5093cc`; #73 was closed as superseded by
#74. The batch adds controlled lighting fixtures (#77), chunk-stream light
continuity (#78), shape-aware baked light (#79), packed emissive presentation
(#70), dark-night silhouettes (#71), directional moonlight and stars (#72),
bounded cave and exposure adaptation (#74), preset shadow softness (#75) and
unified weather/cloud presentation (#76). Save format V4 and generator V1–V11
remain unchanged. Dispositions, conflict resolutions, real-scene comparisons
and MX230 measurements are in
[`docs/INTEGRATION_PR70_79.md`](docs/INTEGRATION_PR70_79.md). Debug and
Release builds report 0 warnings and 0 errors, and `scripts/verify.sh` passes
**199,417 checks, 0 failures across 48 suites** with both meshing paths and
the V1–V11/save compatibility checks included.

**Validation (#59–#68):** Debug and Release builds report 0 warnings and 0 errors; the
headless suite reported **198,718 checks, 0 failures across 48 suites**. V9, V10
and V11 golden hashes match the pre-integration baseline, and the V1–V8 legacy
anchors are unchanged. Matched headless measurements from the #49–#57 batch put
greedy meshing at about 7.9 ms/chunk versus 6.9 ms on main (roughly 15% more CPU
for shared partial-block AO), and V11 chunk generation remains at parity with
V10: this batch measured 9.24 ms/chunk for V8 against 8.91 ms/chunk for V7
(1.04×) and V9 weathering within ±1% of V8. These are mesh/generation CPU
timings, not rendered FPS claims. The #59–#68 batch adds no per-frame world
scan or light node: cave exposure reads eight cached light samples per frame,
and generated campfires no longer own an `OmniLight3D` each. No rendered FPS
delta is claimed because no controlled frame-time benchmark was run for this
batch.

## Current follow-up implementation — 2026-11-22 (rare and enchanted item language)

The existing 32px generated-icon pipeline now expresses item quality without
adding persisted content. ItemDb.RarityOf derives Common / Uncommon / Rare /
Legendary from existing equipment tiers and material identity: iron and
gold/lapis sit in the middle band, diamond equipment is rare, and Ember Core /
Ember Blade are legendary. Sharpness, Protection and Unbreaking add distinct
icy-blue, violet and warm-gold pixel glints. Rare / legendary items get a
cohesive cyan / ember facet; the marks are painted over existing silhouette
pixels, preserving the graphite contour and transparent frame.

ItemDb.Icon(ItemStack) caches the combined item / wear-band / enchantment-
family variant and regenerates mipmaps only on first use. Inventory, hotbar,
cursor and first-person held-item presentation share this path. The first-person
viewmodel now notices an enchantment-state change when the held item ID stays
the same, without recreating its material. Existing slot panels draw a small
rarity pin through their current _Draw pass, so atlas-backed rare materials
remain on their original AtlasTexture region. No atlas tile, block/item ID,
save field, generator version, world mutation, meshing or chunk-streaming
behavior changes. The expanded Forward+ equipment capture covers rarity,
enchantment combinations and the existing four wear bands.

## Current follow-up implementation — 2026-11-22 (V11 shoreline readability)

Newly created worlds now use generator V11. V11 retains V10 terrain,
structures, deterministic weathering and biome-specific canopies, and adds
shallow-bank reeds on sandy and grassy shorelines without moving any existing
terrain or structure stamp. V1–V10 generation and saved worlds remain frozen;
save format stays v4. `BlockId.RiverReeds` 59 and `ItemId.RiverReeds` 108 are
appended; their original procedural cutout art uses atlas tile 59.

Wetness is a non-persisted vertex tag in the previously unused material-family
range 8–14. Water-contact faces on grass, dirt, sand, gravel and farmland get a
shared atlas/greedy-array shader response; shoreline cross plants use it when
adjacent to water. The far LOD uses its four existing corner heights to tint
mixed shoreline cells, so it adds no noise sampling. Greedy merging, UV2 tile
layers, chunk streaming and the classic atlas mip policy are preserved.

## Previous follow-up — V10 biome foliage

Newly created worlds previously used generator V10. V10 keeps V9 terrain,
structure placement and deterministic weathering, and maps the spruce, jungle
and swamp species that V8 already selects to their own canopy blocks. V1–V9
generation and saved worlds remain frozen, save format stays v4, and persisted
IDs were appended at BlockId 56–58 / ItemId 105–107. Atlas tiles 56–58 follow
the weathered materials, so every material keeps a unique atlas coordinate.

This is the reconstructed form of the draft advanced-voxel branch: slabs,
stairs, boats, buckets, glass sorting and shared surface responses were already
merged through #31, #33 and #45, so only the genuinely new foliage content was
rebuilt on top of current `main`. Its surface-variation variant is superseded
by the unified stone tone variation that shipped with #45.

## Previous follow-up — V9 structure weathering

V9 advances the integrated V1.0 checkpoint below from generator V8 to V9 for
newly created worlds. V9 only changes deterministic stone/brick materials in
existing surface structures; V1–V8 generation and saved worlds remain frozen,
save format stays v4, and persisted IDs are appended at BlockId 52–55 /
ItemId 101–104. The detailed implementation, verification and MX230 captures
are recorded in the V9 snapshot further down this file.

Atlas tiles for the weathered materials are allocated at 52–55, after the
unified wood-grain tiles at 49–51, so no two materials share an atlas
coordinate. Earlier cave-ore readability work landed through the unified voxel
material pass (#45); ore tiles remain opaque, non-emissive and regression-tested.

## Integrated 1.0 snapshot — 2026-11-21

Six open feature branches were merged into `integration/v1.0-full` from
`main` at `ce20c91`, in dependency order:

| Stage | Feature | Merge commit |
|---|---|---|
| A | Creature intelligence and cave stealth (PR #30) | `be9d640` |
| B | Survival progression and combat balance (PR #35) | `055e00b` |
| C | Advanced voxel slabs and stairs (PR #31) | `298b7e6` |
| D | World generation V8 (PR #34), then villages/quests (PR #32) | `9208b9a`, `152df8d` |
| E | Water buckets and boats (PR #33) | `e763840` |

**Generator decision.** V8 had never shipped or created persisted worlds
outside these branches, so the combined V8 terrain and village implementation
remains generator version **8**. No V9 was introduced. V1–V7 generation code is
untouched, and golden hashes now pin V1, V3, V5, V6, V7 and V8 chunks. World
format remains **v4** with optional shape, villager, quest and boat fields.

**Persisted IDs.** `BlockId` 0–45 and `ItemId` 0–90 are unchanged. Building
shapes occupy `BlockId` 46–51 and `ItemId` 91–96. Transportation occupies
`ItemId` 97–100 (`EmptyBucket`, `WaterBucket`, `LavaBucket`, `WoodenBoat`).
`ContentTests` pins every numeric value and checks both ranges stay disjoint.

**Integration fixes.**

- Rebuilt the combined village/V8 generator as one compilation unit; village
  sites sample the V8 surface, and farm source water sits at farmland height
  inside a dirt rim.
- Added a deterministic V8 village test covering site theme/height, structure
  separation, generated furniture, farm/well water, negative coordinates and
  reversed chunk-order seams.
- Added combined save round-trip coverage for shapes, villagers, quests and
  boats, plus quest reward wiring and a fluid/partial-block boundary test.
- Kept live `BoatSystem` state authoritative over a stale `SavedBoats` snapshot;
  boats prune freed nodes and stop at unloaded chunk footprints.
- Fixed off-tree `Transform3D` errors in `Player.ApplyDamage` by guarding tree
  membership.
- Fixed two real shutdown leaks: nine craft-slot panels were created but never
  parented/reused, and audio playbacks/stream cache survived scene teardown.
  The headless suite and a verbose real-scene soak now exit with no leaked
  objects or RIDs.
- Reliability audit fixes: respawning after dying in a boat now detaches and
  restores the player; mob death is idempotent across burn/dawn/direct paths;
  non-stackable inventory overflow returns its true leftover; campfire cooking
  rolls raw food back when the result cannot fit; world deletion rejects empty,
  `.` and `..` path segments; quit-time saves flush live villager state before
  serialization; and console/inventory/pause panels no longer stack. See
  [`docs/RELIABILITY_AUDIT_2026-11-21.md`](docs/RELIABILITY_AUDIT_2026-11-21.md).

**Verification.** Godot 4.7.2 .NET Debug and ExportRelease builds complete with
0 warnings and 0 errors. The headless suite reports **4,276 checks, 0
failures**, including legacy save loading, all pinned IDs, V1–V8 golden chunks,
block-shape persistence, deterministic village generation, live resident
spawning/interaction and cross-system save/quest/fluid interaction. The
warmed 8-chunk worldgen profile measured V7 at 6.46 ms/chunk and V8 at 6.52
ms/chunk (**1.01×**, within run noise); village site lookup is cached per cell
under a lock. Real-scene validation is recorded below. Visual verification
remains limited to the existing V8 GPU capture set plus fresh integrated river
and village captures; the integrated village, boat and shape presentation has
not had a complete human visual playthrough.

### Performance benchmark runner increment — 2026-11-21

- Added an opt-in real-scene benchmark with nine seeded workloads: open
  terrain, wooded biome, mountain/river signal, village, cave, ocean, storm,
  hostile encounter and fast exploration. Each workload runs in its own
  disposable current-generator world and follows a precomputed route.
- JSON and per-frame CSV reports include frame percentiles, process/physics
  time, memory/GC, video memory, scene/render counters, mesh-build distribution
  and streaming queue high-water marks. The runner closes the main scene before
  deleting the test world.
- Added percentile regression tests. Debug build: 0 warnings / 0 errors.
  Headless suite at the benchmark-runner increment: 4,238 checks / 0 failures.

### Performance Milestone 2 — MX230 immediate optimizations — 2026-11-21

- Main-menu preview now covers a 5×5 area (25 chunks instead of 81), uses
  render-only chunk meshes (no collision triangles or physics shapes), disables
  menu-only shadows and screen-space/volumetric effects, uses 80% render scale,
  and is capped at 30 FPS. Previous frame cap and viewport settings are restored
  when the menu leaves the scene tree; gameplay reapplies its selected preset.
  A live MX230 capture confirmed the scene remains visually readable.
- Removed three temporary arrays from the per-frame cloud update. Day/night
  caches the player's biome tint and surface height by voxel column and shares
  that sample for cave-depth checks. Weather samples ground-splash height only
  while raining and when its snapped four-metre anchor changes.
- Added a BALANCED preset and set LOW (5-chunk view, 80% render scale, 45 m
  shadows, 1K atlas, expensive screen-space effects disabled) as the new-install
  default for the MX230. Versioned the user graphics-setting mapping so old
  preset selections and custom view distances migrate without changing meaning.
- Added graphics migration, render-only chunk and mesh-payload regression tests.
  V1–V8 generation, persisted block/item IDs, world saves, gameplay collision,
  greedy meshing and chunk streaming remain intact.
- **Controlled hardware comparison:** Godot 4.7.2 .NET Release, MX230 /
  i7-1065G7, Vulkan Forward+, 1280×720, V8 seed 1337, LOW preset, view 5,
  5 s warm-up and 20 s measurement, three runs per workload. Median FPS rose
  9.4% in open terrain (68.9 → 75.3) and 9.1% in heavy rain (67.5 → 73.7).
  p95 frame time improved by 9.1% and 7.1%, respectively. Heavy-rain p99
  shifted from 16.8 ms to 19.9 ms; it remains below the 70 ms target, but the
  tail should be monitored in longer captures. Exact results and limitations
  are in docs/PERFORMANCE_MILESTONE_2_MX230.md.
- **Verification:** Debug and Release builds each report 0 warnings / 0 errors;
  headless suite: 4,276 checks / 0 failures across 29 suites. Three matched
  rendered Release runs completed for each benchmark workload.

### Performance Milestone 3 — bounded chunk streaming and memory accounting — 2026-11-21

- Replaced worker-side queue polling and `Thread.Sleep(1)` backpressure with a
  cancellable semaphore. Workers reserve one of 18 in-flight payload slots
  before meshing; the reservation remains held until main-thread apply or
  shutdown cleanup. Rebuild payloads retain priority over fresh chunks.
- Changed main-thread payload application from a fixed four-payload count to a
  2 ms time target, with a one-payload minimum so a slow collider cannot stall
  streaming indefinitely. Worker cancellation, group completion, pending queue
  drain and `PayloadPool` release are handled during world teardown.
- Added bounded-queue high-water and estimated-byte accounting; managed voxel
  cache array sizes and a view-distance-aware budget; estimates for edit/light
  metadata and applied mesh arrays; retained pool-buffer accounting; and apply
  and collision timing counters. Exact array lengths are measured directly;
  dictionary/object and renderer resource sizes remain estimates.
- Added `voxel-streaming` regression tests for the frame-apply budget and cache
  budget formula. `scripts/verify.sh`: **4,283 checks, 0 failures**. Debug and
  Release builds each completed with **0 warnings / 0 errors**. The benchmark
  matrix passed **54/54** runs across nine scenarios in Debug and Release.
- **Thirty-minute survival soak:** PASS at 1,800 seconds. 10 passive / 27
  hostile creatures, 891 feeds, 24 cave encounters, 16,199 baby observations,
  360 noise stimuli / 244 investigations, 0 invalid positions, save/reload and
  temporary-world cleanup true. Peak RSS 304.1 MiB; managed heap 57.1 MiB;
  Godot static memory 104.4 MiB; voxel-cache arrays peaked at 4.2 MiB against
  an 18.9 MiB budget; pooled mesh buffers peaked at 23.3 MiB; payload high-water
  was 14/18; orphan nodes peaked at 0; shutdown drained all payloads.
- **Rendered MX230 check:** Godot 4.7.2 Forward+ selected the NVIDIA GeForce
  MX230. The real survival scene streamed a cave with visible geometry, HUD and
  minimap. A separate production-scene V8 surface capture at seed 1337 and
  center (512,-372) rendered 49 chunks at 1600×900. Both review frames are
  committed in `docs/performance/screenshots/`. The 120-second rendered
  survival smoke also passed and exited with code 0 after the soak runner drops
  references to the freed main scene before shutdown. No C# script-binding,
  unsafe-reference, shader or RID leak diagnostics remained. The renderer still
  emits non-fatal `RGBFloat`-to-`RGBAFloat` fallback warnings from the existing
  campfire smoke `CurveTexture` in `scripts/Campfires.cs:230`; these do not stop
  capture.

Detailed measurement semantics and limitations are in
[`docs/PERFORMANCE_MILESTONE_3_STREAMING.md`](docs/PERFORMANCE_MILESTONE_3_STREAMING.md).

### Delivery validation

- **Five-minute soak:** PASS at 300 s with 10 passive and 11 hostile creatures,
  137 successful feeds, 8 cave encounters, 22,621 juvenile observations, 60
  injected noise stimuli, 218 investigations, 0 invalid positions, successful
  save/reload and temporary-world cleanup. No leaked objects or RIDs were
  reported at shutdown.
- **60-minute soak:** PASS at 3,600 s on the integration revision with 10
  passive / 51 hostile creatures, 1,758 feeds, 48 cave encounters, 43,181
  juvenile frame observations, 720 noise stimuli, 572 investigations, 0 invalid
  positions, save/reload true and temporary-world cleanup true. No runtime
  errors, warnings, leaked objects or leaked RIDs appeared; RSS peaked near
  251 MB and returned to ~158 MB under GC.
- **GPU capture:** seed 1337 V8 river at (512,-372) rendered 49 chunks at
  1600×900; the deterministic seed 1337 village at (-684,1303) rendered its
  houses, paths and farmland. The village capture initially reproduced the
  off-tree transform error, which was fixed in `Villager.ToSave()` and covered
  by a regression test; the recapture produced zero off-tree errors.

---

## Implementation snapshot — 2026-11-20

The historical baseline and milestone notes below are retained for context. The
current mob implementation has since advanced:

- Passive herds share voxel-aware steering, obstacle avoidance, threat-source
  flight, flock cohesion/separation, feeding/breeding, juvenile growth, and
  movement/idle body animation.
- Follow-up audit fixes: surface spawns now use the already-correct terrain-top
  position (no extra one-block lift); Dreadwing windups cancel when terrain
  blocks sight; hovering/ground steering remains separated; projectiles use a
  terrain ray check each physics step to prevent fast bolts tunnelling through
  walls. Dreadwing repositioning now checks chunk availability before querying
  terrain and its AI safely exits if the world is unavailable.
- Sheep, cows and pigs accept wheat; chickens accept seeds. Feeding starts a
  timed breeding state, nearby compatible adults produce a juvenile, and young
  creatures mature after a growth period.
- Surface spawns are biome-aware: normal herds avoid deserts, snow biomes favour
  sheep, and Briarbacks are restricted to birch forests. Hostiles use night,
  storm, biome and cave-depth conditions; distant populations remain capped and
  are removed outside their active radius.
- New creatures: the fast Cave Stalker, defensive Briarback, ranged Crystal Wisp,
  and rare nocturnal Dreadwing. Hollow and Crystal Wisp attacks use visible
  projectiles; hostile perception is sampled at a bounded AI tick rather than
  raycasting every rendered physics frame.
- Procedural creature-specific sound cues and mob-content regression tests were
  added. Existing item IDs, world-generation versions, block mutation paths,
  greedy meshing and chunk streaming were not changed.

**Historical verification (2026-11-21, superseded by the integrated snapshot
above):** that checkpoint built with 0 warnings/0 errors and passed **1,182
checks, 0 failures**, including mob content and ecology rules. A 300 s
real-scene soak ran without script errors. The final integrated test count,
worldgen profile and soak results are in the snapshot and delivery log above.


### World portability increment — 2026-11-21

- Added complete ZIP export/import on top of the existing per-world directory
  and manifest format. Archives contain all non-temporary world files, including
  rotating backups, plus a versioned index with per-file SHA-256 checksums.
- Imports validate archive/save/generator versions, file lengths and hashes,
  duplicate entry names, path containment, symlink/special-file entries, and
  expanded-size/file-count limits. Extraction occurs in a private staging
  directory; the world is published with a same-volume directory rename only
  after validation succeeds. Existing worlds are never overwritten.
- Added world-detail/backup UI using the shared pixel-art menu skin: seed,
  generator version, cumulative playtime, last-played time, world size, and
  valid/corrupt backup metadata. Restore confirmation creates a safety backup
  through the existing atomic save rotation.
- Added optional cumulative playtime to the world manifest and track it through
  the normal autosave path. Older manifests default to zero; world-save and
  generator formats are unchanged.
- Added archive/backup regression coverage for large files, corrupt ZIPs,
  path traversal, duplicate imports, metadata preservation, legacy v2 save
  import, backup restoration, and pre-restore safety snapshots.

**Verification status:** source changes are committed to `main`. A local
Godot/.NET build and headless execution have not yet been run for this
increment; CI verification is required before treating the feature as fully
validated.

### Advanced voxel building increment — 2026-11-21

- Added append-only block IDs 46–51 and item IDs for oak-planks, cobblestone
  and brick slabs/stairs; existing persisted IDs are unchanged.
- Added a reusable 2×2×2 block-shape definition for bottom/top/double slabs and
  four stair facings with upside-down placement. The same occupancy model
  drives ray intersections, player overlap checks and exposed-face meshing.
- Added non-greedy partial-shape geometry and matching collision triangles.
  Full cubes and double slabs retain greedy meshing; cube faces adjacent to a
  partial shape are split only along the shared boundary. Shape faces use the
  existing atlas UVs, face normals, AO and packed voxel lighting.
- Placement uses the targeted face and hit position; stairs follow player
  facing, slabs select their half, and a matching placed slab completes a
  double slab. Double slabs drop two slab items when mined.
- Added recipes, procedural original block textures, inventory mappings,
  descriptions/tooltips, shape-aware selection outlines and slab mining cracks.
- World-save format v4 stores non-default shape states in an optional sparse
  extension. Old saves default missing states to zero; world-generation
  versions and existing block/item IDs are not changed.
- Added regression tests for shape occupancy, face culling, ray intersections,
  player overlap, orientation, recipes, hardness metadata and save compatibility.

**Verification (2026-11-21):** Godot 4.7.2 .NET Debug and Release builds
succeed with 0 warnings and 0 errors. The headless suite passes **1,351 checks,
0 failures**, including real chunk mesh creation, greedy/per-face surface-area
parity, rendered/collision triangle coverage, non-default shape save/reload,
and state-buffer release after the last stateful edit. A manual visual playtest
remains recommended before merging.

## Implementation snapshot — 2026-11-21 (World generation V9: structure weathering)

- Added V9 as an isolated post-stamp material pass for the existing V8 surface and underground
  structure templates. V1–V8 terrain, structure sites, templates, chunk hashes
  and saved worlds remain frozen; the world format stays at v4.
- Added four append-only block IDs (52–55) and matching placeable/drop item IDs
  (101–104): mossy cobblestone, weathered cobblestone, cracked brick and mossy
  brick. The definitions use the normal full-cube atlas/material/mesher path.
- Added original procedural 32px atlas tiles: broad stone joints/chips, muted
  aged brick courses, hand-shaped crack marks and irregular moss patches. The
  new painters run after legacy atlas paints and reuse the existing atlas size.
- Material selection is deterministic from the structure seed and coarse
  coordinate hash. Moss coverage and wear vary by structure kind and height;
  unrelated terrain, furniture, fluids and non-stonework cells are unchanged.
- Added structure-weathering tests for material determinism, atlas content,
  append-only ID pins, V8/V9 cell diffs, legacy generator selection and reverse
  chunk-order equivalence. V9 visual captures use the real scene and MX230.
- Verification: Godot 4.7.2 .NET Debug build has 0 warnings/errors. The full
  headless suite reports 150,325 checks and 0 failures. Close/distant MX230
  Forward+ captures are in docs/worldgen/screenshots/; see
  docs/worldgen/V9_VISUAL_VALIDATION.md for scene and performance notes.

## Implementation snapshot — 2026-11-21 (World generation V8)

- Added an isolated V8 terrain path for newly created worlds. V1–V7 generator
  source and legacy chunk hashes remain frozen; world format and persisted block
  and item IDs are unchanged.
- Widened the river-bank blend while retaining the V7 deterministic river
  fields and evaluating the surface in one pass. The fixed steep-bank sample at
  seed 11235813 changed from a 38-block to an 18-block height step across 8 blocks.
- Increased deterministic tree density in dense forest, jungle, taiga, swamp
  and birch biomes; opened additional shallow cave mouths; reused the existing
  V7 ore, aquifer, magma and underground-structure rules.
- Reduced fog and height haze only in V8 world environments. Existing V1–V7
  worlds retain their prior environment values.
- Added V8 biome, river, cave, tree-density, generation-order and cross-chunk
  regression coverage, plus a real-scene GPU capture harness.
- Matched before/after Forward+ captures cover a steep river bank, river valley,
  mountain, forest and cave across four fixed seeds. See
  `docs/worldgen/V8_VISUAL_VALIDATION.md`.
- Verification: Godot 4.7.2 .NET build succeeds with 0 warnings/0 errors;
  the headless suite passes **1,213 checks, 0 failures**, including three pinned
  V7 golden chunk hashes. Latest warmed 8-chunk profile measured V7 at
  7.03 ms/chunk and V8 at 7.33 ms/chunk on the validation machine (about 4%
  overhead). Further manual exploration across all biomes and presets remains
  planned.

### Village and quest progression — 2026-11-21

- **Generator V8 villages:** deterministic Plains, Forest, Snow and Desert
  settlement sites reuse the frozen V7 terrain/biome sampler. Placement rejects
  shorelines, steep ground, cave openings, existing structures, campfire
  clearings and intersecting underground chambers. Chunk-clipped architecture
  adds houses, roads, a gathering square, light posts, storage, workstations and
  irrigated farms.
- **Residents:** five deterministic named villagers per settlement use the
  existing Mob movement, gravity and collision controller. Profession-specific
  schedules, dialogue and gold-based trade offers connect to the existing
  trader UI. Villagers flee nearby hostile creatures and persist identity,
  profession, position, health, home/work anchors and completed trades.
- **Quest architecture:** reusable objective/stage definitions cover biome
  discovery, gathering, crafting, trading, combat and dungeon clears.
  Multi-stage progress and reward claims are optional V4 world-save fields;
  older worlds remain readable.
- **Verification:** automated coverage exercises deterministic village stamping
  and chunk seams, biome eligibility, trades, schedules, quest progression and
  save round-trips. GPU worldgen captures and the population soak were added
  later; see the integrated snapshot and V8 visual-validation document.

## Glass rendering polish — 2026-11-21

- Kept the existing dedicated glass chunk surface, atlas tile, alpha material,
  voxel-light channels and face-culling rules; no new block/item IDs, save
  fields, generator versions, or mesh/streaming paths were introduced.
- Glass chunk instances sort transparent geometry around their mesh AABB center
  rather than the chunk origin, reducing incorrect ordering between neighboring
  chunk surfaces viewed through windows.
- Switched the glass shader to back-face culling so a closed glass block does
  not blend both sides of the same surface. Transparent fragments remain out of
  opaque depth writes.
- Reduced the glass tile to a clear body, a one-pixel restrained edge and a
  subtle diagonal glint; reduced shader alpha to limit grid-like visual buildup
  across large windows while retaining a readable edge at distance.

## Transparent and cutout material audit — 2026-11-22

- Confirmed that foliage and crossed plants already use the opaque atlas and
  texture-array cutout path: matching 0.5 alpha-scissor thresholds, opaque depth
  writes and alpha-to-coverage with project MSAA enabled. They stay out of the
  blended sorting pass. Lava is an opaque, depth-writing emissive surface, not a
  blended material.
- Fixed the verified gap: water uses `blend_mix` and `depth_draw_opaque` but its
  chunk mesh was sorted from the chunk origin. Water now uses the same mesh
  AABB-center sorting as glass, improving ordering between streamed water chunks
  without changing the surface mesh or streaming architecture.
- Glass and water continue to keep translucent fragments out of opaque depth
  writes. Glass remains back-face culled; water remains two-sided. The
  first-person held-item `Sprite3D` retains texture alpha and nearest filtering.
- Added regression coverage for glass/water alpha and depth contracts, opaque
  cutout parity across packed-atlas and texture-array shaders, MSAA coverage,
  opaque lava behavior, glass/water chunk sorting, and real mesher surface
  routing in both per-face and greedy modes, alongside the glass atlas alpha
  checks.
- Compatibility: no block/item IDs, save fields, generator versions, atlas
  tile indices, or world-generation rules changed. Existing greedy meshing,
  chunk streaming, lighting and collision ownership remain unchanged.
- Verification: `dotnet build -c Debug` and `scripts/verify.sh` pass on Godot
  4.7.2 .NET (186,365 checks, 0 failures). A production visual capture rendered
  close and distant 1280×720 views with Vulkan Forward+ on the NVIDIA MX230.
- Remaining limitation: Forward+ sorts blended instances at object level;
  AABB-center sorting improves chunk ordering but cannot perfectly order
  arbitrarily intersecting transparent geometry inside one chunk.

## 1. Historical state audit — 2026-10-21 (superseded by the current audit above)

### Survival equipment increment — 2026-11-20

- Appended persisted IDs for tiered swords, bow, arrows, shield and Ember Blade.
- Added weapon damage, cadence, durability and knockback metadata, procedural pixel-art icons and item descriptions.
- Added crafting recipes for swords, bow, arrows, shield and the Ember Blade using the rare Ember Core.
- Melee uses weapon stats and airborne critical hits; bow attacks consume arrows; held shields block frontal melee attacks and wear on a block.
- Existing inventory durability serialization applies to weapons without changing old save formats.

### Already built

`PLAN.md` records M0–M4b as complete, and the corresponding code exists:

- Menus, world select/create, pause, settings, atomic saves with backups,
  per-world folders, dev console.
- Items, tools, 9+27 inventory, 2×2/3×3 crafting, recipe book, durability.
- Survival stats, damage pipeline, softcore death, campfires, sheep, one
  hostile (`Shade`), weather and lightning.
- Worldgen v5: normal/flat/amplified/sky presets, rivers, caves, lava,
  structures, height 128.
- Streaming, far LOD, greedy meshing, async remesh, dynamic quality governor,
  profiler.
- Minimap, photo mode, procedural textures/audio, adaptive soundscape.
- Linux and Windows export presets.

### Historical gap audit (pre-M5 baseline; superseded by the integrated snapshot)

These figures describe the 2026-10-21 baseline, not the current code. The
current registry has 60 consecutive block IDs (0–59), `BlockDb.BlockCount` 128,
`BlockDb.TileCount` 256, a 46-suite test runner, and `WorldSave` V4. The table is
retained as the historical starting point for the M5 plan below.

| Area | State at this checkpoint |
|---|---|
| Content capacity | 29 block IDs (`BlockId.cs`), `BlockDb.BlockCount = 32` → **3 free IDs**; 33 of 64 atlas tiles used |
| Content | No torch/light, chest, furnace, farming, armor, ladder, bed, boats, buckets |
| Mobs | Only `Sheep` and `Shade` |
| Water | Drowning and muffling exist, but **no swimming** — water is effectively a walkable floor |
| Progression | Iron/gold ore → raw ore → tools directly; no smelting/ingots/armor |
| Goals | No achievements, journal, boss, or directed objective |
| UX/platform | No key rebinding, gamepad, onboarding, localization, or music |
| World management | Create/list/rename/delete/duplicate plus ZIP portability, backup browser and safe restore |
| Quality | Automated gameplay, save, worldgen and regression suites; README and roadmap maintained |
| Save format | `WorldSave` v3: edits, time, containers, furnaces and world flags; v2 remains readable |

### Strengths to protect

Worldgen determinism, save architecture, greedy meshing, procedural art/audio,
photo mode, and the “cozy but optional danger” identity.

---

## 2. Product direction

**Ship a focused 1.0 before scale or multiplayer.**

Four pillars:

1. **A home that matters** — light, storage, smelting, farming, bed, campfire.
2. **A world that lives** — more animals, a ranged threat, one dungeon boss, a
   trader, fishing, achievements.
3. **Approachable by default** — onboarding, rebinding, gamepad, Arabic/English.
4. **Tactile and cozy** — music, ambience, photo mode, accessible settings.

Why this order: the game already looks and streams well. What it lacks is
**purpose** (why mine?) and **comfort** (how do I learn/control it?).
Multiplayer and dimensions multiply risk without fixing either. Keep the
existing “door open” rules, but do not start them before 1.0.

---

## 3. Non-negotiable constraints

1. **Old worlds always load.** Append IDs only; never renumber. All worldgen
   changes stay gated on `GenVersion`. Re-verify the v1 anchor chunk hash.
2. **Capacity before content (satisfied).** `BlockCount` is 128 and the atlas
   has 256 tiles, so new content can be appended without renumbering. Keep the
   append-only rule for every future ID.
3. **One mutation seam.** All edits through `SetBlock`/`ApplyEdit`; no new
   direct `World.Player` reads.
4. **Every milestone gate:** 0-warning build, tests green, save round-trip,
   perf baseline, 30-minute soak.
5. **Scope guard:** no multiplayer, dimensions, or mod API before 1.0.
   Partial-height building is limited to the implemented slabs and stairs;
   walls, doors, fences and panes remain future work.

---

## 4. Roadmap at a glance

| Milestone | Theme | Size | Depends on | Exit criteria |
|---|---|---|---|---|
| **M5** | Foundation & guardrails | 1–1.5 wk | — | Met: tests, 128 blocks/256 tiles, save V4, perf runner (RTX baseline pending) |
| **M6** | Base & progression | 3–4 wk | M5 | Torch/light, chest, furnace/ingots/armor, farming, bed, swimming |
| **M7** | Living world & goals | 3–4 wk | M6 | Mob roster, dungeon/boss, trader, fishing, achievements |
| **M8** | UX, platform & 1.0 | 2–3 wk | M5–M7 | Rebinding/gamepad, onboarding, world management, EN/AR, music, release |
| **M9** | Scale & online | 4–8 wk | 1.0 | Mid-LOD/occlusion, height 256, dimensions, LAN co-op |
| **M10** | Post-1.0 | ongoing | 1.0 | Data-driven content, deeper NPC economy, more non-cube families |

Estimated 1.0: **9–12 weeks solo + AI**, faster at the pace this project has
moved.

---

## 5. Detailed milestones

### M5 — Foundation & guardrails (completed; historical plan)

> This plan is retained for history. Its capacity, test-harness and save items
> are implemented; only the RTX 4060 performance comparison remains open.

**5.1 Content capacity**

- [x] `BlockDb.cs`: `BlockCount` 128 and `TileCount` 256 (16×16 atlas); UV and
  texture-array math updated. The registry currently uses 60 consecutive
  block IDs and appends new content only.
- Add a startup **content validator**: every `BlockId` has a definition and
  valid tile; every `ItemId` has a name/icon; every recipe ingredient/result
  exists; block↔item pairs round-trip.
- Document the append-only ID policy at the top of `BlockId.cs` / `ItemId.cs`.
- *Why:* only 3 spare block IDs and 31 spare tiles today. M6 needs far more.

**5.2 Headless test harness**

- [x] `tests/TestRunner.tscn` runs under `godot --headless --path .`, exits
  non-zero on failure, and currently registers 46 suites with no third-party
  addon required.
- [x] Suites cover content registry, inventory/stack/overflow, recipes, player
  stats, save/backup/corruption, worldgen anchors and mesher parity, plus the
  later combat, village, fluid, rendering and integration work.
- [x] `scripts/verify.sh` builds the project, runs the suite and exits with the
  test status.

**5.3 Save format (shipped as V4)**

- [x] `WorldSave.Version` is checked; legacy v1–v3 files load with optional
  fields defaulted and are written back as V4. The migration chain preserves
  old edits, containers, furnaces and flags.
- [x] `Containers`, `Flags`, `Furnaces`, block-shape states, villagers, quests
  and boats are optional persisted extensions.
- [x] Armor and equipment slots live in `PlayerSave`; old saves default empty.
- [x] Tests cover old saves, `.bak` recovery, stable round-trips and the
  integration additions. The historical “Save v3” label is superseded by the
  shipped V4 format.

**5.4 Performance baseline**

> Status: the repeatable runner and Debug/Release procedure are implemented;
> the RTX 4060 comparison and a fresh controlled Release baseline are still
> open.

- [x] Add `tests/PerformanceRunner.tscn` with deterministic V8 worlds and
  fixed routes for nine workloads; write JSON summary and per-frame CSV.
- [x] Capture frame p50/p95/p99/worst, process/physics time, RSS/managed heap,
  managed allocations/GC, Debug static memory, VRAM, draw calls/primitives,
  mesh-build distribution, chunk counts and queue high-water marks.
- [ ] Record repeated Debug and Release runs at fixed resolution on the MX230
  and RTX 4060 reference systems; establish variance before adding a regression
  gate. Headless timing is not an FPS baseline.

**5.5 Stabilization**

- 30-minute soak: fly across biomes, place/break, weather, mobs, save/load.
  Fix crashes/leaks; clean logs.

**Done when:** old worlds load, v1 anchor unchanged, tests green, baseline
recorded. **Status:** all are satisfied except the RTX 4060 comparison and fresh
Release baseline; see the current audit above.

---

### M6 — Base & progression (3–4 weeks)

#### M6a — Light & water (week 1)

- **Torch**: block + item + recipe (coal + stick → 4). Cross-shaped mesh with an
  emissive surface; extend the existing `IsCross`/`AddCross` path in
  `Mesher.cs`.
- **Lighting v1**: pooled `OmniLight3D` nodes (max ~16 nearest, 8 m, flicker,
  no shadows), registered from edits/payloads and freed on unload. No voxel
  light propagation yet.
- *Stretch (only if v1 looks bad):* voxel block-light nibble baked into vertex
  colors. This is an L-sized change; do not start it unless review demands.
- **Swimming and voxel fluids (implemented)**: buoyancy, Space = up, Shift =
  dive, immersion-aware movement, underwater fog/audio/particles, slower
  submerged mining, head-based breath/drowning, and distinct lava drag/damage.
  Seven deterministic flow levels spread through loaded caves and terrain with
  a fixed-step update budget; all mutations go through `SetBlock` and existing
  worlds retain their original generator version.
- **Ladder**: climbable block, no orientation needed.
- **Done when:** a cave can be lit and navigated; the player can swim up from
  10 m; FPS stays within 3% of baseline.

#### M6b — Storage, heat & armor (weeks 2–3)

- **Chest**: 27 slots, shared storage UI, save in `WorldSave.Containers`, break
  drops contents, shift-click quick-move. Refactor `InventoryUi`/`InvSlot` into
  a reusable item-grid widget (also needed by the furnace).
- **Furnace**: input/fuel/output, fuel values (coal, planks, logs, sticks),
  smelt times, lit state + light + particles, progress persists.
- **Smelting recipes**: raw iron→iron ingot, raw gold→gold ingot, sand→glass,
  cobblestone→stone, raw mutton→cooked mutton.
- **Ingots**: iron/gold tool recipes move to ingots; gold becomes the trader
  currency.
- **Armor**: helmet/chest/legs/boots slots, leather/iron/diamond sets,
  durability, damage-reduction formula, HUD armor bar. Arm visuals optional.
- **Done when:** chest/furnace contents survive save/load; armor reduces damage
  predictably; old saves still load.

#### M6c — Farm, food & home (week 4)

- **Farming**: hoe tools, farmland block, seeds from tall grass, wheat in 3
  growth stages using appended block IDs, random-tick growth, harvest → wheat
  + seeds, bread recipe.
- **Bed**: one block, sleep at night → skip to dawn, set respawn, cooldown;
  shades despawn/stop spawning nearby.
- **Food balance**: bread, cooked meats, hunger tuning.
- **Creative quality-of-life**: full item palette, not just blocks.
- **Done when:** plant→grow→harvest→bread works; bed skips night and sets
  spawn; no worldgen changes.

---

### M7 — Living world & goals (3–4 weeks)

**7.1 Mob roster + AI v2**

- Passive: cow (beef/leather), chicken (chicken/feather), pig (porkchop);
  breeding with wheat/seeds; babies.
- Hostile: **Hollow** (ranged, keeps distance, slow projectile) and **Rot**
  (melee pack of 2–3). Keep `Shade` as the night baseline.
- AI: raycast “whisker” steering + step-up, line-of-sight, group alert, attack
  telegraph, sound cues. No navmesh.
- Companion v2: wolf/dog tame + follow/stay/guard command wheel; extend
  existing sheep taming.

**7.2 Dungeon + boss**

- One new structure per 320×320 cell: **Ember Deep** — entrance, 2–3 rooms,
  loot chests, spawner, boss arena.
- Boss **Ember Warden**: ~120 HP, three attacks (slam shockwave, ember volley,
  summon shades), phase changes at 66%/33%, arena bounds.
- Unique drop **Ember Core** → craftable lantern (portable light) so the reward
  ties into M6a.
- One-time clear flag in world save; `/struct ember` dev command.

**7.3 Trader & campfire life**

- Wandering trader visits campfires every few in-game days; barter UI; pays
  gold for surplus, sells seeds/glass/wool/maps; despawns at dawn.
- This is the 1.0 stand-in for villages.

**7.4 Fishing**

- Rod recipe, cast to water, 2–8 s wait, reel-timing minigame;
  cod/salmon/treasure; cook fish.

**7.5 Achievements / journal**

- Per-world flags for first block, first night, first iron, first dungeon,
  boss, homestead (bed+chest+furnace+farm), trader.
- Toast on unlock; `J` opens a journal panel; persisted in save v3.

**7.6 Ambient life**

- Butterflies/fireflies/birds as cheap particles; cave drips; distant howls;
  weather-aware scheduling.

**Done when:** the boss is completable, achievements persist, and perf
regression is under 10%.

---

### M8 — UX, platform & 1.0 release (2–3 weeks)

- **Key rebinding**: persist InputMap actions, conflict detection, reset
  defaults; mouse buttons included.
- **Gamepad**: joypad look, deadzone/sensitivity, UI focus navigation, button
  glyphs; verify with one Xbox and one PlayStation controller.
- **Onboarding “First Night”**: six contextual objectives — wood → table →
  tools → shelter → torch → survive night. Skippable; no modal tutorial.
- **World management**: rename, duplicate, delete (confirm + backup),
  export/import world zip, backup restore, world details
  (seed/gen/playtime/size), “open folder”.
- **Localization EN/AR**: central `Tr()` + CSV, extract player-facing strings
  from code-built UI, Arabic font (e.g. Noto Naskh), RTL layout; language
  setting. Dev console can stay English.
- **Accessibility**: head-bob and shake toggles, UI scale, captions for key
  sounds, colorblind-safe colors, hold/toggle sprint and mining.
- **Music**: at least three loops (menu/day/night) on the existing Music bus,
  crossfade/ducking, license file. Use original or CC0; do not ship unclear
  licenses.
- **Photo mode v2**: filters, FOV, hide hand, gallery + open folder, panorama.
- **Release engineering**: version string, README/controls, changelog, Linux +
  Windows packages, Steam Deck smoke test, 60-minute soak, crash-log
  instructions.

**1.0 definition of done**

1. A new player can go menu → tutorial → survive first night → mine → smelt →
   armor → dungeon → boss without a wiki.
2. Every pre-1.0 save loads; no data loss in migration tests.
3. Keyboard/mouse + gamepad, rebinding, English/Arabic.
4. 0-warning build, tests green, 60-minute soak clean.
5. ≥50 FPS release at HIGH/VD8 on the MX230; no >33 ms build spikes.
6. No known crash, save-loss, or progression-blocking bug.

---

### M9 — Scale & online (post-1.0, optional)

Only after 1.0, and only with a measured reason:

- Mid-LOD + section/occlusion culling (the old M4c item).
- Chunk store if memory budget demands it.
- Height 256/512.
- Dimension framework + portals (Nether-like parameterized worlds,
  per-dimension saves).
- LAN co-op: host-authoritative, seed + edit log, player/mob/container sync,
  chat, join codes.
- Dedicated server: headless export, reconnect, interest management, admin
  commands.

Keep the current door-open rules; do not start multiplayer before the 1.0
content is stable.

---

### M10 — Post-1.0 differentiators

- Data-driven content packs: JSON blocks/items/recipes/loot/worldgen
  parameters; texture packs; world templates.
- Deeper villages/NPC economy and quests (base villages and quests shipped
  in the 2026-11-21 integration).
- Non-cube blocks (slabs/stairs/doors) with a block-metadata layer +
  mesher/collision work.
- Seasons and redstone-like logic only if playtests ask for them. Boats and
  buckets shipped in the 2026-11-21 integration.

---

## 6. 1.0 scope cut

| Priority | Items |
|---|---|
| **Must** | M5; M6a/b/c; M7 mobs + dungeon/boss + achievements; M8 rebinding/gamepad, onboarding, world management, EN/AR, accessibility, music, release |
| **Should** | Trader, fishing, companion commands, photo mode v2, richer ambience |
| **Not now** | Multiplayer, dimensions, mod API, web export (verify Godot .NET web support before promising it); additional non-cube families such as doors, fences and panes |

---

## 7. Verification & quality gates

**Automated**

- Content registry, inventory, recipes, stats, save/backup/migration, worldgen
  anchors, mesher parity, rendering/material routing, equipment durability,
  first-person animation, selection outline and the cross-PR integration
  regressions.
- Headless smoke: boot → create world → place/break → save → reload → assert.
- GitHub Actions runs `dotnet build` and the headless suite; local
  `scripts/verify.sh` remains the minimum gate.

**Golden anchors**

- v1 chunk `(7,7)` SHA-256
  `c7fe5ea262d362606196e2fffcdc65dd130eb2df1fc58ab051fb54d3623a352a`
  unchanged, with additional pinned anchors through V11.
- Greedy vs per-face surface-area parity per normal.
- `player.json` save→load→save stable.
- New gen versions never alter old ones; V11 is pinned and its delta is limited
  to shoreline reeds.

**Manual matrix**

- Fresh normal/flat/sky worlds; old v1/v3/v5 worlds; peaceful/normal/hard;
  keyboard + gamepad; 16:9/ultrawide; English/Arabic; 30/60-minute sessions.

**Perf gates**

- Baseline at M5; no >10% regression per milestone.
- Target ≥50 FPS release at HIGH/VD8; p99 frame <33 ms while building.

---

## 8. Main risks

| Risk | Mitigation |
|---|---|
| Content capacity/atlas overflow | Satisfied: 128 block slots, 256 atlas tiles, append-only validator and 61 registered blocks |
| Torch lighting looks flat or costs FPS | Ship pooled-light v1; only build voxel propagation with measured justification |
| Chest/furnace save bloat | Save only non-empty containers; cap; versioned migration |
| Localization touches every code-built UI string | Central `Tr()` first, extract incrementally; prototype Arabic/RTL early |
| Music licensing | Original/CC0 only; license file in repo |
| Scope creep (villages, doors, multiplayer) | Explicit 1.0 cut line; defer to M9/M10 |
| No tests means silent save/worldgen breakage | M5 harness is a hard prerequisite, not optional |
| Arabic font/RTL issues | Bundle a known Arabic font; test shaping and layout on one screen before full extraction |

---

## 9. First five days (historical M5 plan; completed except RTX comparison)

1. **Day 1** — Run build + baseline; record FPS/memory; add the test-runner
   skeleton and content validator.
2. **Day 2** — Raise `BlockCount`/atlas capacity; verify anchor, greedy parity,
   tile array.
3. **Day 3** — Add save v3 skeleton (version check, containers, flags) +
   migration tests.
4. **Day 4** — Prototype torch: block/item/recipe, cross mesh, pooled lights;
   measure FPS.
5. **Day 5** — Prototype swimming; review both prototypes against the baseline;
   adjust M6 scope.

---

## 10. Decisions pending

- [ ] Confirm “cozy survival 1.0” focus (recommended) vs combat/adventure-first
	  vs technical sandbox.
- [ ] Arabic in 1.0 (recommended if it is the target audience) or post-1.0?
- [ ] Keep multiplayer deferred to post-1.0 (recommended) or make LAN co-op a
	  1.0 goal?
- [ ] Music: original/CC0 tracks (recommended) or extend the procedural synth?
- [x] Slabs and stairs shipped in the 2026-11-21 integration. Doors,
      trapdoors, fences, gates, panes, plates, buttons and signs remain future work.

When a decision is made, record it in the project’s decision log (`PLAN.md`
§1 follows the existing `D#` format) so later work does not re-litigate it.

---

## 11. Progress log

- **2026-10-21 — M5 complete (foundation & guardrails).**
  - **Content capacity:** `BlockCount` 32 → 128; atlas 8×8 → 16×16 (256
	tiles); removed the last hardcoded 64-tile UV table. Verified visually in
	the running game: all existing block icons and world textures still map
	correctly.
  - **Content validator:** new `ContentValidation` checks every `BlockId`,
	tile index, item name/stack, block↔item mapping and recipe; boot logs any
	problem. Wired into `GameSession`.
  - **Tests:** headless `TestRunner.tscn` + `scripts/verify.sh`; suites for
	content, inventory, recipes, stats, saves, worldgen and mesher parity.
	**373 checks, 0 failures** at the M5 checkpoint.
  - **Save v3:** `WorldSave` now versions containers and flags; loader warns on
	newer formats, v2 saves still parse, and container/flag data round-trips.
	Backup rotation and corrupt-file recovery are covered by tests.
  - **Golden anchors:** v1 chunk `(7,7)` hash unchanged; greedy vs per-face
	surface area parity verified.
  - **Baseline:** `docs/PERF_BASELINE.md` — Debug build, HIGH/VD8, focused
	window, **42–47 FPS** at 441 chunks / 3,092 nodes on the MX230.
  - **Not verified:** the 30-minute soak and a fixed-camera benchmark are still
	manual follow-ups.

- **2026-10-21 — M6a complete (torch/light + swimming).**
  - **Torch:** appended `BlockId.Torch` / `ItemId.Torch`, procedural cross
	texture, coal + stick → 4 recipe, placeable block item.
  - **Lighting v1:** `BlockLights` pools the 16 nearest placed emitters
	(10 m warm flicker, no shadows); `VoxelWorld` rebuilds the emitter registry
	from saved edits on load and updates it on every `SetBlock`. This is the
	bounded v1 — voxel light propagation is still the later option.
  - **Swimming:** buoyancy with Space to rise and Sprint to dive; movement is
	slower, footstep bob is suppressed, and the existing underwater fog,
	muffling and drowning rules still apply.
  - **Verified live:** night screenshot shows the warm torch pool lighting the
	ground and foliage; one light node at the torch position; a submerged
	player rose from y 34.5 to the surface at y 40.8 while holding Space.
  - **Tests:** **389 checks, 0 failures**, 0-warning build. Test torches and
	the teleported test position were removed and the world clock restored
	before stopping.
  - **Not verified:** long dive/drowning after the swim change and the full
	30-minute soak. The ladder was not part of this slice and remains optional
	for M6b/M6c.

**Next:** M6b — chest, furnace/ingots, armor, and the shared item-grid UI.

- **2026-10-21 — M6b.1 complete (chest storage).**
  - Appended `BlockId.Chest` / `ItemId.Chest`, procedural chest tile, and an
	eight-plank ring recipe.
  - `Container` (27 slots) + save-v3 conversion: chest contents load, accept
	changes, and survive `SaveNow`; empty chests stay out of the save.
  - The inventory screen now shows a chest grid above the player grid when a
	chest is opened (right-click); the existing cursor-held stack logic moves
	items between chest/backpack/hotbar. Breaking a non-empty chest is refused
	with "Empty the chest first".
  - **Verified live:** chest placed and opened; the chest screen rendered 27
	chest slots plus the player inventory and recipe/palette panels.
  - **Tests:** **416 checks, 0 failures**, 0-warning build; chest save/load
	covered in the worldgen suite.
  - **Not verified live:** filling the chest by mouse and reopening it after a
	full game restart (the container save/load path is covered by tests).
  - **Next:** furnace/ingots/armor, then farming/bed.

- **2026-10-21 — M6b.2 complete (furnace, ingots, armor).**
  - **Furnace:** block/item/eight-cobblestone recipe, three slots (input, fuel,
	output), burn and smelt timers, fuel values (coal 80 s, logs 60 s, planks
	15 s, sticks 5 s), and recipes for raw iron/gold, sand, cobblestone and
	mutton. State ticks even while the chunk is unloaded and persists through
	`WorldSave.Furnaces`.
  - **Ingots:** raw iron/gold now smelt into iron/gold ingots; iron tools use
	ingots instead of raw ore.
  - **Armor:** eight pieces (iron + diamond sets), four equipment slots saved
	as inventory slots 36–39, durability wear on every landed hit, 4% damage
	reduction per defense point (capped at 80%), inventory armor row + HUD
	armor pips. Armor does not protect against drowning or starvation.
  - **UI:** furnace panel (input → fuel → output with progress bar), recipe
	book entries, and the armor row on the inventory screen.
  - **Verified:** build 0 warnings, **461 checks / 0 failures** (furnace
	smelting, fuel rules, save round-trip, armor values/save, recipes), live
	furnace panel and armor row rendered; `/armor iron` showed the HUD armor
	pips. Test armor and the test furnace were removed afterward.
  - **Not verified live:** a full 10-second smelt and damage reduction in
	combat (both covered by unit tests); chest/furnace full-restart reload.
  - **Next:** farming/bed (M6c), then move to M7.

- **2026-10-22 — M6c complete (farming, bed, ladder, food).**
  - **Farming:** farmland + three wheat growth stages, hoe tools at all four
	tiers, seeds from tall grass and harvests, bread recipe, and a daytime
	random growth tick. Wheat is a cross-mesh crop; crop positions are tracked
	for growth and rebuilt from saved edits on load.
  - **Bed:** one-block bed, night-only sleep that advances the clock to dawn,
	sets the respawn point, and clears nearby hostiles. Sleeping during the day
	gives the "only at night" notice.
  - **Ladder:** craftable ladder block and climb movement (jump/forward up,
	back down) with fall protection; footsteps and head bob are suppressed
	while climbing.
  - **Food:** bread restores five hunger plus a small heal.
  - **Verified:** build 0 warnings, **528 checks / 0 failures**; live bed,
	farmland/wheat, and ladder placed; wheat planting returned true; ladder
	climb raised the player from y 55.0 to 58.08; bed sleep changed time
	0.90 → 0.26. Test blocks were removed afterward.
  - **Not verified live:** the probabilistic full wheat growth cycle and
	harvesting ripe wheat (content/recipe tests cover the data path).
  - **Next:** M7 — living world & goals (mob roster, dungeon/boss, trader,
	fishing, achievements, ambience).

- **2026-10-22 — M7.1 + M7.5 complete (mob roster + achievements).**
  - **Animals:** cow, pig and chicken added as passive wanderers with beef,
	pork, chicken, leather and feather drops; raw meats cook at campfires and
	smelt in furnaces.
  - **Hostiles:** Rot (tough melee pack) and Hollow (line-of-sight ranged
	attacker) join the Shade; the population manager now spawns weighted
	night/storm waves and supports `/mob rot|hollow|cow|pig|chicken`.
  - **Achievements:** six persisted goals (first block, first torch, first
	iron, homestead, first night, deep), HUD toasts, and a J-key journal.
  - **Verified:** build 0 warnings, **568 checks / 0 failures**; live `/mob`
	spawns returned 14 passive / 3 hostile agents; the journal showed
	“Iron Age” checked after receiving an iron ingot.
  - **Not live-verified:** new hostile chase/ranged combat over a full night,
	animal loot drops by killing, and journal after a full restart (flags save
	through `WorldSave.Flags` and are covered by save tests).
  - **Next:** M7.2 — Ember Deep dungeon and Ember Warden boss.

- **2026-10-22 — M7.2 complete (Ember Deep + Ember Warden).**
  - **Ember Deep:** new gen v6 structure (rare per 160×160 cell) with a
	surface rim, 20-block ladder shaft, 15×13 brick arena, corner pillars,
	lava pockets and wall torches. Gen v4/v5 structure layouts and worlds are
	untouched.
  - **Boss:** Ember Warden (120 HP) with slam, ember volley, and two
	add-summon phases; drops the unique Ember Core. Boss bar in the HUD.
  - **Clear state:** the arena wakes the boss on entry and writes the
	`dungeon/ember` world flag on death, so cleared dungeons stay cleared.
  - **Dev tools:** `/struct ember` teleports to the nearest arena;
	`/mob warden` spawns the boss for testing.
  - **Verified:** build 0 warnings, **577 checks / 0 failures**; live warden
	spawn showed the “EMBER WARDEN” bar at 120 HP and the boss killed the
	player, proving its attack loop is live.
  - **Not live-verified:** a complete dungeon descent and clear flag; the
	template geometry is covered by `DungeonTests`.
  - **Next:** trader, fishing and ambient-life slices to close M7.

- **2026-10-22 — M7.3 + M7.4 complete (trader + fishing).**
  - **Trader:** wandering trader NPC visits campfires at night, sells/buys
	against gold (wheat/cobblestone → gold; gold → seeds/glass/wool), and has a
	barter UI. `/trader` spawns one immediately.
  - **Fishing:** fishing rod recipe, water cast, random bite window, short
	right-click reel timing, raw/cooked fish, campfire/furnace cooking, and
	inventory integration.
  - **Verified:** build 0 warnings, **586 checks / 0 failures**; live trader
	panel rendered with all five offers.
  - **Not live-verified:** right-click trader opening, a completed fishing
	catch, and trade inventory mutation (logic and recipes are covered by
	tests).
  - **Next:** ambient-life polish (butterflies/fireflies/birds, cave drips,
	weather-aware cues) to close M7.

- **2026-10-22 — M7.6 complete (ambient life) / M7 closed.**
  - Added daytime butterfly and bird particle rigs alongside the existing
	motes, fireflies and underwater bubbles; all are gated by time, weather,
	submersion and underground state.
  - **Verified:** build 0 warnings, **586 checks / 0 failures**.
  - **Not live-verified:** visual density/tuning of butterflies and birds.
  - **M7 complete:** mob roster, achievements/journal, Ember Deep + boss,
	trader, fishing and ambient life.
  - **Next:** M8 — UX, platform and the 1.0 release slice (rebinding/gamepad,
	onboarding, world management, EN/AR, accessibility, music, release).

- **2026-10-22 — M8.1 complete (world management).**
  - Main-menu world rows now have **Manage**: rename, duplicate and delete.
  - Delete uses a confirmation dialog and refuses paths outside the worlds
	folder; duplicate copies the full world folder and assigns a new identity.
  - `WorldStore` helpers plus save-suite coverage for rename/copy/delete.
  - **Verified:** build 0 warnings, **597 checks / 0 failures**; live menu
	showed the Manage buttons and the manage panel.
  - **Arabic localization is intentionally skipped per request;** the next
	localization work, if any, is English-only polish.
  - **Not live-verified:** an actual rename/copy/delete from the menu (the
	store operations are covered by tests); delete dialog copy is live.
  - **Next:** onboarding “First Night” and key rebinding / accessibility.

- **2026-10-22 — M8.2 complete (onboarding, keybinds, accessibility).**
  - **Onboarding:** contextual “First Night” chain — wood → crafting table →
	wooden pick → stone → torch → survive night — shown as a persistent HUD
	hint, with per-world progress flags. No modal tutorial.
  - **Keybinds:** pause-menu **Controls** panel for 15 actions, click-to-bind,
	Escape cancels, reset-to-defaults, persisted to `user://keybinds.cfg` and
	applied at boot.
  - **Accessibility:** head-bob toggle and hints toggle in Settings.
  - **Verified:** build 0 warnings, **660 checks / 0 failures**; live HUD showed
	“Craft a crafting table (E)”, and the Controls panel rendered with all
	bindings; the list was made scrollable after the first live check exposed
	an overflow.
  - **Not live-verified:** rebinding a key and the keybind config surviving a
	restart (the InputMap actions and labels are covered by tests).
  - **Arabic remains skipped per request.**
  - **Next:** music + release engineering (Linux/Windows packages, README,
	version string, soak test).

- **2026-10-22 — M8.3 complete (live main-menu backdrop).**
  - Main menu now renders the selected world live in 3D: 9×9 preview chunks
	around the saved player position, sky/lighting/fog from the shared
	environment factory, golden-hour sun, soft vignette and a slowly orbiting,
	gently breathing camera.
  - Menu UI overlays the world with a translucent tint; the world keeps
	rotating while panels are open.
  - Screenshot saved to
	`user://menu_backdrop.png` (`~/.local/share/godot/app_userdata/minecraft/`).
  - **Verified:** build 0 warnings, **660 checks / 0 failures**; live menu
	render inspected and screenshot saved.
  - **Next:** music + release engineering.

- **2026-10-23 — M8.4 complete (minimap overhaul).**
  - Map resolution raised to **128×128** with hill shading, biome colours
	(plains/birch/desert/snow), water depth shading, and player-edit tops.
  - Added compass labels, a black-outlined heading arrow, spawn/respawn/campfire
	waypoints, and a live scale label.
  - Added four zoom levels (128 m → 1024 m across) with `[`/`]` actions and
	pause-menu rebinding support.
  - Rebuilds are sliced over frames (16 rows per frame) instead of one 16k-column
	spike, and still pause while the map is hidden.
  - **Verified:** build 0 warnings, **668 checks / 0 failures**; live zoom
	changed the scale to 512 m and the larger map rendered.
  - **Not measured:** frame-time hitch before/after the sliced rebuild.
  - **Next:** music + release engineering.


- **2026-11-20 — Mob AI, combat and ecology expanded (implementation committed; verification pending).**
  - Shared mob AI now caches steering between short perception ticks, checks
    nearby voxel obstacles without navmesh baking, and uses lower update
    frequency at distance. Passive animals flee from nearby threats, form
    loose same-species herds, and use gentle idle/movement body motion.
  - Added food-driven breeding for sheep/cows/pigs (wheat) and chickens (seeds),
    short-lived love state, nearby pair matching, juvenile scale/speed, and
    timed maturation. Existing sheep tame/stay/follow behavior remains.
  - Spawn director now distinguishes surface night/storm encounters from
    underground cave fauna, filters by biome, limits group and total
    populations, retreats surface hostiles at dawn, and retains the existing
    boss/trader hooks.
  - Added Briarback (defensive birch-forest grazer), Cave Stalker (wind-up
    pouncer), Crystal Wisp (ranged cave sentry), and Dreadwing (rare night
    flanker). Shades/Rots/Hollows gained alert propagation, pack-aware damage
    and telegraphed projectile attacks; existing mob loot was completed using
    already-persisted items.
  - Added synthesized creature sound cues, developer-console spawn names, and
    tests for species roles, breeding food consumption, juvenile state, health
    and drops.
  - **Compatibility:** no `BlockId`/`ItemId` values added or reordered; no save
    schema, world-generation version, terrain mutation, meshing or streaming
    changes.
  - **Not yet verified:** `dotnet build -c Debug`, `scripts/verify.sh`, and a
    live multi-night/cave/animal breeding/performance soak must pass on the
    Godot 4.7.2 .NET runtime.


---

## 12. Block interaction and mining upgrade — 2026-11-20

- Centralized mining duration, harvest-tier checks, and ten crack-stage
  selection in `BlockDb`; every defined block now has explicit resistance,
  preferred tool class, and drop behavior.
- Survival harvest rules distinguish stone/cobblestone and ore tiers. Glass,
  leaves, plants, and bedrock no longer produce incorrect block-item drops;
  existing crop and rare leaf-drop paths are retained.
- The existing `Player` mining loop validates the ray target and reach each
  physics frame, resets on target changes and mouse release, and uses the
  existing `VoxelWorld.SetBlock` mutation path and interaction signals.
- Creative mode breaks instantly and skips survival drops and tool wear.
  Survival durability changes are owned by `Inventory.DamageTool`.
- The existing crack overlay switches through ten deterministic textures.
  Mining ticks emit small material-profiled chips, and the procedural audio
  system selects distinct dirt, stone, wood, sand, glass, and foliage
  breaking/placement sounds.
- Added `MiningTests` for tool speeds, harvest tiers, drops, crack progression,
  hardness validity, and tool durability. Persisted block/item IDs,
  world-generation versions, chunk streaming, greedy meshing, and save formats
  are unchanged.


## 13. Voxel lighting upgrade — 2026-11-20

- Replaced the nearby-`OmniLight3D` pool with packed one-byte-per-voxel light
  data: high nibble for skylight, low nibble for block emission.
- Added direct sunlight per column, transparent-block transmission, water/leaf
  attenuation, cave spill and neighbour-border merging for skylight.
- Added multi-source block-light flood fill over a 14-voxel halo. Opaque voxels
  stop propagation; torch, lava and future emitters derive their levels from
  `BlockDef.Light` without changing persisted block IDs.
- `SetBlock` invalidates affected light caches and schedules loaded chunks in
  the 3×3 neighbourhood for remeshing. Chunk data, emitter registration and
  light caches are rebuilt lazily on load and pruned on unload.
- Greedy meshing includes four-corner sky/block values in its face key, so
  light boundaries cannot be merged away. Per-face and cross meshes use the
  same light channels; the existing AO calculation is retained.
- Shared spatial shaders separate sky illumination from warm block emission.
  Day/night updates one shared sky-energy uniform rather than rebuilding meshes.
- Added propagation, opaque blocking, attenuation, transparent transmission,
  removal, chunk-seam, source-level, skylight-shaft and cave-darkness tests.
- **CI verification:** commit `734f758` passed GitHub Actions with a clean
  .NET build (0 warnings, 0 errors) and the Godot headless suite (828 checks,
  0 failures).
- **MX230 validation:** the fixed-seed cave visual capture now runs on the
  GeForce MX230 in Forward+; a sustained frame-time soak is still outstanding.

### Cave exposure transition — 2026-11-22

- Reused the packed skylight channel already baked by the voxel solver to drive
  cave ambient/fog response from the player's camera position. Eight cached
  voxel-centre samples are trilinearly interpolated, so exposure rolls off
  across cave mouths and corners instead of changing at a fixed three-block
  terrain-depth threshold.
- The cache lookup never generates chunks; until nearby light data is ready,
  a smooth terrain-depth fallback avoids an initial brightness pop. The update
  uses stack storage and a bounded set of dictionary reads, with no added nodes,
  emitters, per-frame world scans or physics queries.
- **MX230 visual check:** Godot 4.7.2 .NET Forward+, seed 1337 / V11 at
  `(512, -372)`, clear noon, 1600×900. The cached samples read skylight 15.00
  above the shaft, 14.00 in the passage and 5.27 at the chamber camera
  (cave-darkness blend 0.778); the 56-chunk capture completed successfully.
- Added interpolation, cache-miss, monotonic exposure, range and transition
  regression tests. Save format, persisted IDs, V1–V11 generation, greedy
  meshing, chunk streaming and all graphics-preset costs remain unchanged.

### Warm emitter consistency — 2026-11-22

- Generated campfires now register
  a virtual source in the air cell directly above the live centre log, using
  the same 14-level channel as torches. Keeping the source outside the opaque
  log lets light escape into surrounding air. Registration respects saved edits;
  replacing/restoring/removing the base log updates the source through the
  existing VoxelWorld.SetBlock mutation seam. Equal-strength conversion between
  a campfire log and a torch moves the source and invalidates the block-light
  cache instead of retaining stale light at the old cell. Fire/smoke remain GPU
  particles, but the per-site OmniLight3D and per-frame flicker updates are gone.
- The atlas, greedy texture-array and glass shaders now share one non-linear,
  bounded amber response (and lower emissive contribution), while sky and AO
  channels remain independent. The response is evaluated once per fragment
  and reused for diffuse/emissive output. Overlapping sources use max-propagated
  nibble levels, not additive light nodes. The world environment keeps neutral
  sky ambient and Filmic tonemapping with a higher glow threshold; LOW/BALANCED
  continue to disable glow, SSAO, SSR and volumetric fog.
- Added regression coverage for shader hue/response parity, overlap
  non-summation, campfire air-cell propagation, base-log and source-position
  migration, incremental cache clearing/restoration, and save-compatible
  persisted edits. Source solving consumes the existing emitter registry
  instead of scanning every voxel in the halo volume.
- **MX230 Forward+ comparison — 2026-11-22:** the repeatable Godot 4.7.2
  main-scene captures (seed 1337, generator V11, fixed site/camera) reported
  baked flame/adjacent levels of 14/13 in torch-only, campfire-only and overlap
  modes. This confirms source parity and max-propagated overlap on the MX230.
  A longer cave/texture-readability and frame-time soak remains pending.


## Mining interaction follow-up audit — 2026-11-20

- Corrected mining timing for blocks without a preferred tool class (including
  glass and flowers); these now use the normal hardness multiplier rather than
  the wrong-tool penalty. Cactus no longer has an arbitrary axe requirement.
- Added voxel DDA targeting for non-colliding crossed plant meshes (tall grass,
  flowers, torches and wheat). Cross-plane checks match the mesher geometry;
  terrain collision meshes remain unchanged.
- Cached per-block particle atlas textures and color ramps so frequent
  mining-chip feedback does not allocate fresh texture and gradient resources
  every quarter-second.
- Oak leaves retain the bonus apple drop; birch leaves do not. Apple inventory
  overflow now uses the same full-inventory notification as other bonus drops.
- Added regression checks for no-tool mining timing and cross-plant ray hits.

- **2026-11-20 — Mob ecology high-priority audit fixes.**
  - Newborn placement now searches a small nearby footprint and requires a solid
    floor plus three clear voxel cells; blocked/occupied locations are rejected
    before either parent enters breeding cooldown.
  - Added shared attack/telegraph eligibility gates and regression tests for
    cooldown expiry, line-of-sight blocking, pounce range and telegraph readiness.
  - Cave Stalkers and Hollows now retain a short-lived last-seen player position
    and investigate it after losing direct sight; ranged wind-up is cancelled
    when line of sight breaks.
  - **Verification pending:** latest GitHub Actions build/test run is still in
    progress. Herd-size tuning, hunger/grazing, limb animation, light-aware cave
    detection, projectile-specific integration tests, and rest/weather behavior
    remain unimplemented; a sustained survival soak is still outstanding.


## 14. Incremental voxel-light mutation follow-up — 2026-11-20

- Added a decrease-and-relight block-light update path: removing an emitter
  clears dependent values, preserves other emitters and surviving boundary
  light, then propagates the remaining sources. Adding an emitter only visits
  cells whose light can increase.
- Integrated the update into the existing `VoxelWorld.SetBlock` mutation path.
  When all nine cached chunk maps are present, their packed block-light
  channels are updated as one 3×3 volume, preserving continuity across chunk
  seams. If the cache is incomplete, the existing lazy full solve remains the
  safe fallback.
- `SetBlock` now distinguishes opacity changes from emission changes.
  Opacity edits invalidate sky and block maps so direct sunlight and cave spill
  are rebuilt from the updated voxel data; emitter-only edits preserve valid
  skylight and update only block light.
- Added solver parity tests comparing incremental add, overlapping-source
  removal and final-source removal byte-for-byte with a full multi-source solve.
  Added world-level tests for placement/removal, overlap, chunk boundaries,
  opacity edits, and saved emitter restoration after chunk unload/reload.
- No persisted IDs, save formats, world-generation versions, meshing rules or
  chunk-streaming policies were changed.
- **Verification:** pending the final CI run for this follow-up; run the
  Godot 4.7.2 .NET build and headless suite before considering the change
  complete.


## 14b. Fluid simulation follow-up — 2026-11-20

- Replaced the recurring full-window flow pass with a deduplicated active-cell queue, bounded per-tick work, and periodic source pulses. Loaded fluid discovery runs when the player changes chunk or moves into a new vertical band.
- Unloaded neighbours are treated as unknown; water cannot flow into them or recede merely because a chunk is not loaded. World-height limits remain known solid boundaries.
- Added source-water regeneration when two horizontal sources border a water cell above a solid floor. Flowing water remains non-source; adjacent lava reacts through `SetBlock`.
- Swimming vertical input respects floor/ceiling contacts. Underwater visibility blends with immersion, lava has a separate warm fog environment, and low-pass filtering no longer affects Master/UI audio.
- Added generated-world integration coverage for source regeneration, lateral spread, impermeable terrain, lava reaction and an unloaded chunk edge, plus collision/source-rule unit checks.
- **Verification pending:** run `dotnet build -c Debug` and `scripts/verify.sh` with Godot 4.7.2 .NET; no runtime/performance soak has been performed in this environment.

---

## 14c. World generation V7 audit — 2026-11-20

- New worlds use the isolated V7 generator; generator versions 1–6 retain their
  existing code paths. Persisted block/item IDs and save schemas are unchanged.
- Added 13 climate/height biomes, continental terrain, smoothed river valleys,
  mountain ridges, biome-specific tree canopies, aquifers, magma pockets,
  depth-aware ore veins, surface landmarks and underground chambers.
- V7 column generation now shares a single surface/climate sample between
  height and biome classification. Tree, cactus, campfire, structure and
  minimap paths reuse the same sample instead of recalculating climate noise.
- Added real generated-world biome distribution checks across four seeds,
  deterministic request-order tests, chunk-seam checks for trees, ore veins,
  surface structures and underground rooms, and golden SHA-256 chunk anchors
  for generator versions 3, 5 and 6 alongside the v1 anchor.
- Added terrain-profile checks for river channels/banks, biome transitions,
  mountain elevations/slopes and cave passages. Added informational CI timings
  for separate vs shared column sampling and full chunk generation.
- **Visual inspection:** CI is headless. Paired GPU captures were later added
  for river banks, biome blending, mountains, forests and caves in
  `docs/worldgen/V8_VISUAL_VALIDATION.md`; integrated village, boat and
  building-shape presentation still needs a complete human visual review.
- **Hardware profiling:** CI timings are correctness diagnostics, not a
  substitute for fixed-seed profiling. One MX230 integration measurement is
  recorded in `docs/PERF_BASELINE.md`; a fixed-seed harness and RTX-class
  numbers remain outstanding.

## 15. M8 UX interface and controller continuation — 2026-11-20

- Unified the menu and gameplay visual language with square pixel-art panels, muted green borders, warm highlights, crisp focus/hover states and high contrast hotbar/crosshair treatment.
- Added responsive scaling to main menu, pause/settings, controls and inventory; chest and furnace panels now use the shared framed surface.
- Inventory slots show item name/count/durability, support hover feedback, Shift-click transfer, and native drag-and-drop across player inventory, crafting, chest and furnace slots. Armor destinations validate equipment type, and furnace output can be dragged into inventory.
- Added additive controller bindings for movement, camera look, jump, sprint, mining, placement, block pick, hotbar cycling, creative flight, inventory, pause, minimap and journal. Controller button overrides persist separately from keyboard settings and can be rebound in the Controls panel.
- Added focus initialization and controller confirm/cancel support across the main menu, pause, controls, inventory and achievement journal.
- First-person arm now displays the selected item icon and animates it with the existing swing, swimming, movement bob and landing presentation.
- Added keybind regression checks for controller defaults, remapping, conflict detection, reset behavior and preservation of controller events during keyboard rebinding.
- Compatibility: no persisted IDs, save schema, world generation, voxel mutation, meshing or chunk streaming changes.
- **Verification (2026-11-21):** merged and built with 0 warnings; the headless suite (including keybind and inventory stack-transfer tests) passes. Visual review at 1920×1080 / 1280×720 and a physical-controller smoke test remain outstanding.
## 16. Combat equipment progression — 2026-11-20

- Added experience points and levels; hostile/passive mob defeats award XP and
  Crystal Wisps provide a renewable but uncommon lapis lazuli source.
- Added Sharpness, Protection, and Unbreaking levels I–III with escalating
  experience-level/lapis costs. Enchantments affect melee damage, armor
  mitigation, and durability wear respectively.
- Added a craftable Enchanting Table block (two diamonds, two iron ingots,
  and one lapis lazuli); right-click opens the dedicated Enchant / Upgrade panel.
  Supported swords, iron armor,
  and iron tools can be upgraded one tier while preserving durability ratio
  and enchantments. The selected hotbar item is the current target.
- Player bow use now spawns a ballistic arrow with gravity and swept collision;
  arrows damage mobs through the shared damage interface and report impact
  feedback. Hostile projectiles now use a distinct projectile damage type.
- Shields block frontal hostile projectiles as well as melee attacks and lose
  durability on a successful block. Unbreaking also reduces tool, weapon,
  shield, and armor wear.
- Save extensions are optional JSON fields: legacy player files load with zero
  experience and legacy inventory stacks load with no enchantments. No old IDs
  were reordered or reused.
- Added regression coverage for enchant costs/eligibility, upgrade preservation,
  save round-trips, unbreaking odds, and shield facing.
- **Verification (2026-11-21):** merged and built with 0 warnings; the headless
  suite passes the equipment, save and combat regressions. Live enchanting and
  ballistic-arrow playtesting was not performed.

## 16b. Directional wood surfaces and shape texel consistency — 2026-11-21

- Reworked procedural oak and birch log end grain with offset/warped growth
  rings, a defined pith, subtle radial checks and species-matched sapwood. Oak
  bark now has longer vertical fissures and restrained knots; birch bark retains
  its characteristic dark marks with fine vertical streaks.
- Appended atlas tiles 49–51 for plank top, long-grain side and cut-end views.
  Existing atlas dimensions, earlier tile indices and the legacy crafting-table
  texture remain unchanged. Oak planks, slabs and stairs now select those
  directional faces instead of stretching one generic building tile over every
  side.
- Partial-shape and cube-to-shape boundary faces derive UVs from the full
  block-local coordinate frame. A half-height/half-width face therefore samples
  only its corresponding half of the texture rather than repeating a complete
  tile at double texel density. East/west stairs rotate their tread grain and
  swap side/end faces to follow their facing.
- The existing ItemDb.Icon → first-person HeldItem sprite path naturally picks
  the plank side-grain tile; no extra scene node or per-frame work was introduced.
- No persisted block/item IDs, save fields, generator rules, world mutations,
  greedy merge keys or chunk-streaming behavior changed. Original procedural
  assets remain self-authored and are built into the existing atlas.
- Added painter/layout tests plus real mesher-payload UV checks in both greedy
  texture-array and per-face atlas modes, including clipped cube faces adjacent
  to partial shapes.

- Added an optional live-scene GPU capture fixture with close and distant
  views of a disposable wood exhibit. Both views were rendered through the
  production Forward+ pipeline on the NVIDIA MX230 and inspected successfully.

## 17. Integration of V7, UI and equipment — 2026-11-21

- Merged `worldgen/v7-release-candidate` (PR #29),
  `ui/pixel-art-redesign-ready` (PR #24) and
  `feature/survival-equipment-progression` (PR #22) into
  `integration/v1.0-merge` with merge commits; branch history is preserved.
- Resolved the only persisted-id collision: flowing water keeps block ids
  38–44 and `EnchantingTable` is appended as 45. Item ids were already
  append-only and needed no renumbering.
- Combined main's DDA mining / target-cancel path with the equipment branch's
  bow firing; right-click and held left-click fire a bow, melee and mining keep
  the existing flow.
- Combined the pixel-art slot styling with the enchantment tooltip and removed
  the branch's duplicate local style helper.
- Kept the `InputLocked` mining guard without the mouse-capture requirement so
  controller mining works while every modal panel still blocks it.
- Armor mitigation is base points (up to 80 %) followed by multiplicative
  Protection, so both systems apply without double-counting.
- Save extensions (`Experience`, per-stack `Sharpness`/`Protection`/`Unbreaking`)
  are optional JSON fields; old player, world and manifest files load unchanged.
- Verification: 0 warnings / 0 errors, **1,182 checks / 0 failures**, legacy
  worldgen golden hashes intact, 300 s real-scene soak with no script errors,
  and a 30.6 FPS MX230 measurement at HIGH/VD8 (see `docs/PERF_BASELINE.md`).
- Superseded branch iterations and the draft `redesign/ui-overhaul` were not
  merged; see `docs/INTEGRATION_2026-11-21.md` for the full decision log.
- **Remaining gaps:** no partial-height block shapes (slabs/stairs) exist in any
  branch; V7 terrain was not visually reviewed on a GPU; no RTX-class
  measurement; the default 3,600-second survival soak remains to be run. The
  five-minute smoke soak and normal juvenile-breeding observation now pass.
- Pull request cleanup (2026-11-21): PRs #29, #24 and #22 are marked merged by
  GitHub; #27, #26 and #19 were closed as superseded with the reasons recorded
  on each PR. All branches remain on the remote; nothing was deleted or
  force-pushed.

## 18. Creature perception and cave stealth — 2026-11-21

- Extended the existing packed voxel-light perception path with short-lived
  per-creature cell caching. Cave Stalkers, Crystal Wisps and Hollows already
  used light thresholds; Dreadwings now respect nearby voxel light as well.
  Dark-adapted Cave Stalkers receive a small low-light pounce/damage bonus.
- Added short-lived cached terrain visibility, invalidated when either actor
  moves or the cache expires. Hostile pursuit now uses remembered positions and
  sound investigation rather than steering directly through blocked terrain.
- Wired existing mining, block-break, block-place and footstep signals into a
  fixed 24-entry acoustic ring. Events expire after five seconds; distance,
  intensity and age determine hearing. No audio engine or per-event scene
  nodes were added.
- Newborns retain both parents as guardians and follow the closest living
  parent while separated. Herd movement now includes bounded heading
  alignment; chicken, sheep, pig and cow flight distances/speeds differ.
- Existing grazing, hunger, shelter/rest, breeding, animation, combat, persisted
  IDs, save formats, generation rules, voxel mutation, greedy meshing and chunk
  streaming are retained.
- Added MobPerceptionTests for torch detection/removal across a negative chunk
  seam, player-signal sound detection, sound investigation, juvenile guardian
  following, real terrain-layer ray occlusion and visibility-cache refresh.
  The survival soak now produces mining/placement noise and reports acoustic
  investigations plus non-finite creature positions.
- Verification on Godot 4.7.2 .NET / .NET 9: build succeeds with 0 warnings
  and 0 errors; headless suites pass with 1,238 checks and 0 failures. GitHub
  Actions CI also passed on the tested code commit (the later changes are
  documentation-only).
- Runtime validation: the five-minute main-scene survival soak completed with
  SURVIVAL SOAK PASS (300 simulated seconds, 10 passive, 11 hostile, 8 cave
  encounters, 254 sound investigations, 0 invalid creature positions).
  A 60-second breeding smoke test also passed using normal food interactions.
- The default 3,600-second soak has not yet been run. Godot reports renderer,
  worker-pool and ObjectDB resource warnings at soak shutdown; the unchanged
  baseline test runner also reports ObjectDB/Jolt cleanup leaks, so these are
  recorded as shutdown diagnostics rather than hidden.


## 19. Survival progression and combat balance audit — 2026-11-21

- Audited wood/stone/iron/diamond harvest gates, V7 ore density/vein attempts,
  mining times, recipes, furnace fuel/smelt values, food/hunger pacing,
  tool/weapon/armor durability, XP/enchantment costs, arrows/shields, hostile
  archetypes, difficulty and Warden reward.
- Fixed melee tool durability: a landed hit with a pickaxe, axe, shovel or hoe
  now uses the same existing equipment wear path as swords and Unbreaking.
- Prevented damage rejected by creative mode, death or respawn invulnerability
  from consuming shield durability. Drowning/starvation still damage health but
  no longer wear armor that does not mitigate those damage types.
- Centralized peaceful/normal/hard damage and 0/7/10 hostile population caps
  for every hostile archetype. Peaceful rejects manual hostile/Warden spawns,
  cancels pending melee wind-ups, hides the dungeon boss and removes active
  hostile projectiles.
- Fixed stale mob wrappers in death, dawn retreat, peaceful cleanup and
  `ClearHostilesNear`: population lists remove actors synchronously before
  deferred `QueueFree`.
- Added a transactional two-gold-to-one-lapis trader offer as a predictable
  alternative to the Crystal Wisp drop; output-capacity checks happen before
  the currency is consumed.
- Fixed furnace elapsed-time loss across fuel boundaries without changing the
  existing saved timer fields or furnace serialization.
- Added a cross-system progression suite covering the resource, crafting,
  smelting, equipment, enchantment, upgrade, combat, boss phase thresholds,
  death, overflow and save/load path. The suite drives the Warden through its full
  health pool and verifies the single defeat event, XP reward and Ember Core.
  Weapon damage/cadence and armor points/durability are asserted against tier data.
- Compatibility: no `BlockId`, `ItemId`, worldgen-version, player-save or
  furnace-save fields were added, removed, reordered or repurposed.
- **Verified code revision (`46c2857`):** GitHub Actions Debug and Release
  builds both succeeded with 0 warnings / 0 errors; the full headless suite
  passed **1,397 checks / 0 failures**. The headless test process reported the
  baseline shutdown allocations (18 ObjectDB / 17 Jolt).
- **Real-scene smoke:** the controlled 300-second run passed with 10 passive
  mobs, 144 successful feeds, 8 cave encounters, 5,519 juvenile observations,
  save read-back and temporary-world cleanup
  ([run 44900680833](https://github.com/firasotoom85-droid/minecraft-godot/actions/runs/44900680833)).
  The smoke still reports 9 CanvasItem RIDs and 28 ObjectDB instances at engine
  exit; recorded as shutdown warnings. The 3,600-second normal-survival soak is
  running against the verified code revision
  ([run 44901217578](https://github.com/firasotoom85-droid/minecraft-godot/actions/runs/44901217578)); no full-hour PASS is claimed yet.
- See `docs/SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md` for detailed values,
  findings, reproducible steps and test evidence.

## 20. Water transportation: buckets and boats — 2026-11-21

- Added unstackable empty, water and lava buckets with generated inventory
  icons, names and descriptions. Three iron ingots craft an empty bucket;
  five planks craft a wooden boat.
- Bucket collection is source-only: flowing water is never converted to a
  bucket. Collection removes the source through `VoxelWorld.SetBlock` and
  changes the selected inventory slot only after the world edit succeeds.
  Filled buckets place a source into a loaded replaceable cell and transform
  back to empty. Solid blocks, existing fluids and unloaded cells are refused.
- Added a custom wooden hull, seat and animated oars. Boats use the existing
  streamed terrain collision, sample source/flow water heights for buoyancy,
  and provide acceleration, steering, reverse and braking.
- Player boarding disables the player capsule and attaches the player to the
  boat; Space dismounts to a loaded clear cell. Left-click removes the boat and
  returns its item in survival. Vehicle state is an optional world-save field.
- Boat movement checks the complete hull footprint against loaded chunks and
  pauses at unloaded boundaries instead of crossing into unbuilt terrain.
- Added tests for source-only collection, fluid conservation, inventory
  transformations, invalid placement, recipes, acceleration/steering/braking,
  collision response, chunk-edge protection, boarding/dismounting, boat removal
  and save compatibility.
- **Verification:** Godot 4.7.2 .NET build completed with 0 warnings and
  0 errors. The final full headless suite passed with 1,247 checks and
  0 failures. Live interactive playtesting was not performed.


## 21. Terrain texture scale and distance consistency — 2026-11-21

- Audited the existing 32×32 procedural atlas, atlas UV path, greedy texture
  array, shape-face UV mapping and selected-item first-person icon path. The
  atlas and close-range UV scale were already consistent; the missing piece was
  distance filtering in the default greedy renderer.
- Standardize procedural base-noise and speckle accents on coherent 2×2
  texel clusters across terrain/material families, preserving the prior
  approximate speckle coverage. Structural grain, seams and silhouettes retain
  their intentional finer pixel accents.
- Generate mipmaps independently for each texture-array tile before creating
  the array. The greedy shader now uses nearest-mipmap filtering: native texels
  remain crisp nearby, while smaller prefiltered levels suppress distant
  high-frequency shimmer and detail popping.
- Do not generate mipmaps on the packed atlas: that would blend adjacent atlas
  tiles at lower levels. The existing atlas coordinates, texture-array layer
  indices, 32×32 source art, UV tiling, block definitions and first-person icon
  presentation remain unchanged.
- Added regression assertions for all 256 array layers, array mipmap
  availability and coherent 2×2 procedural noise clusters. No persisted IDs,
  save fields, world generation, voxel edits, greedy merge keys or chunk
  streaming behavior changed.
- **Verification:** Godot 4.7.2/.NET 9 build passed with 0 warnings and
  0 errors; `scripts/verify.sh` passed all 6,043 checks with 0 failures.
  A Forward+ GPU capture also rendered both close and distant production-scene
  views at 1280×720 on the GeForce MX230, with no shader compilation errors.
  The capture confirms the mipmapped array path renders on the target GPU; a
  controlled before/after FPS comparison was not performed. The complete
  mip-chain payload is estimated at about 1.33 MiB versus 1.00 MiB without
  mipmaps for 256 RGBA8 32×32 layers (approximately 0.33 MiB additional
  texture data).
## 21b. Soil, grass and farmland transitions — 2026-11-21

- Refined the existing procedural 32px atlas tiles rather than replacing the
  art pipeline: grass sides now use a deeper, irregular turf band with a broken
  dark-soil seam; farmland furrows have stronger shadow/ridge contrast and the
  vertical face has a distinct dark tilled cap over its soil body.
- Added face-aware atlas tile/color access. The existing icon/particle color
  API retains its prior side-tile behavior; far LOD now samples upward-facing
  tiles for terrain tops and side-facing tiles for coarse edges.
- Split far-LOD slope skirts into a one-block surface-material cap and an
  underlying substrate segment, so grassy/farmed edges transition into dirt
  instead of stretching one surface tint down the entire slope.
- Extended the derived per-column edit-top cache to retain the visible top
  BlockId as well as its height. Far LOD now shows edited farmland, exposed
  dirt and other edited top blocks after chunk unload. The cache rebuilds from
  the existing sparse block edits on load; the original height-only lookup
  remains available.
- Compatibility: no persisted BlockId or ItemId values changed; no save fields
  or generator rules were added; V1–V8 remain frozen. All voxel changes still
  flow through the existing VoxelWorld.SetBlock path. Greedy meshing and
  chunk streaming were not altered.
- Added TerrainMaterialTests for face-tile assignments, face-color separation,
  grass/soil row contrast, farmland furrow/cap contrast, and icon-color
  compatibility. Added edit-cache round-trip/removal regressions to the shape
  persistence integration test.
- Performance characteristics: atlas averages are computed and cached once on
  the main thread before LOD workers run; LOD worker builds use fixed color
  tables and stack-allocated quad points. The edit material cache adds one
  byte per column in each edited chunk; extra skirt quads are emitted only
  where a coarse edge drops by more than one block. No per-frame terrain scans
  or physics work were added.
- **Verification:** Godot 4.7.2 .NET / .NET 9 Debug build and headless suite
  pass; the completed run reports 4,247 checks and 0 failures. No generator,
  save-format or persisted-ID migrations are required.


## Unified voxel material response (implemented)

- Added a shared response matrix for stone, wood, soil, ores, foliage, glass,
  and metal. Block faces classify their existing procedural atlas tiles and
  pass the response family through vertex colour alpha; greedy texture-array
  tile selection remains in UV2.x.
- Matched atlas and texture-array shader response values, including controlled
  metallic/specular highlights instead of a single roughness-1 response.
- Generated mipmaps from isolated tile images for the greedy texture array,
  preventing adjacent atlas tiles from bleeding into distant surfaces.
- First-person held-item sprites now use the same material response palette.
  Material instances are created only when the selected item changes.
- No persisted IDs, world saves, generator versions, atlas dimensions, or
  texture coordinates were changed. The non-greedy packed-atlas path retains
  nearest sampling without mipmaps to avoid atlas cross-tile contamination.
- Performance: response tags reuse the existing vertex-colour alpha channel,
  so per-face meshes gain no extra vertex attribute or UV allocation. The
  isolated mip chain adds approximately 0.33 MiB to the 256-layer RGBA8 tile
  array; generation occurs once during lazy texture-array creation. Held-item
  materials are rebuilt only on hotbar item changes.
- **Verification:** Godot 4.7.2 .NET / .NET 9 Debug build has 0 warnings and
  0 errors; `scripts/verify.sh` passes 4,268 checks with 0 failures.


## 22. Voxel material art consistency — 2026-11-21

- Improved ore deposits with connected stepped silhouettes, mineral shadow edges and restrained highlights; ore remains opaque and non-emissive.
- Refined the existing oak and birch foliage tiles with clustered cutout gaps and more coherent leaf-tone groups, preserving their original IDs, atlas coordinates, cutout mesh and light-transmission rules.
- Added deterministic low-contrast world-space stone tone variation to both atlas and greedy texture-array shaders. The variation is bounded to the stone material family and does not add per-frame CPU work or mesh vertices.
- Unified material response tags across atlas and greedy mesh paths, with shared roughness/metallic/specular values for stone, wood, soil, ore, foliage, glass and metal.
- Refined glass alpha, front-face culling and bounds-center transparent sorting while retaining the existing chunk glass surface.
- Added regression coverage for ore pixel readability/opacity, foliage alpha coverage and light transmission, stone shader consistency, material-family routing, glass sorting and texture mapping. Extended the production-scene GPU capture fixture to show these material families together at close and distant views.
- No existing BlockId/ItemId values were reordered or reused. No generator, save format, voxel mutation, greedy meshing or chunk-streaming rules changed.

## 23. Player journey repair audit — 2026-11-21

- Added a source-level first-time Survival player-journey audit, covering world creation, starter inventory, crafting, First Night onboarding, mining, exploration, villages, quests, and reward retention. The report separates confirmed code-path defects from design decisions and playtest candidates.
- Repair candidates are prioritized in `docs/PLAYER_JOURNEY_REPAIR_REPORT_2026-11-21.md`, with affected code paths, proposed repair directions, and regression-test expectations.
- This entry records audit findings only; no gameplay fixes or verification claims are implied. Resolve the design questions before implementation, preserve existing saves and generator behavior, and add integration tests for confirmed fixes.

## 24. Visual quality pass — environment and water — 2026-11-22

- Extended the existing `DayNight` runtime with a deterministic atmosphere
  palette for dawn, day, dusk, night, overcast and lightning. Sky, ground
  horizon, sun/moon energy, ambient light and fog response share one evaluator;
  existing cave, underwater, biome tint, clouds, stars and caustics remain.
- Updated the existing weather system to switch between rain and snow when the
  player crosses into or out of a snow biome. Biome sampling is cached per
  integer player column and is not repeated every frame in a stationary cell.
- Corrected the water shader's camera-depth calculation to use the transformed
  world position after top-face wave displacement. Added restrained procedural
  shoreline foam without a new material, texture or node system.
- Added deterministic atmosphere and water presentation regression tests, plus
  a real-scene GPU capture runner for fixed seed, generator version, position,
  time and weather. A 1600×900 V8 river-valley noon capture rendered 49 chunks
  on the MX230 with Forward+ / Vulkan.
- Validation after rebasing onto current main: Godot 4.7.2 .NET Debug and
  Release builds passed with 0 warnings and 0 errors; the integrated suite
  passed **153,482 checks / 0 failures**, including performance, streaming,
  graphics, material, glass, V9 weathering and gameplay regressions. A
  real-scene Forward+ / Vulkan captures rendered 49 chunks on V8 and 40 chunks
  on V9 on the MX230, both at 1600×900 with the same seed and camera target.
- Compatibility: no persisted IDs, save fields, generator outputs, voxel edit
  paths, packed light propagation, greedy meshing or chunk streaming changed.
- The current main branch already contains the material-response matrix,
  isolated texture-array mipmaps, ore/foliage/glass presentation, terrain
  transitions, and generated-ruin weathering. This increment leaves those
  systems and assets unchanged.
- Remaining visual sign-off: repeat the capture matrix across biome/time/
  weather combinations and inspect cave, village, underwater and LOW/HIGH
  preset views manually. One GPU reference frame is not a complete art review.
- See `docs/VISUAL_MILESTONE_4_ENVIRONMENT.md` for scope, exact capture command,
  rendered sample, and validation detail.

## 25. Gameplay polish: first-hour flow and interaction guidance — 2026-11-22

- Expanded the existing six-step First Night hint chain into a contextual
  15-step first-hour guide: movement, wood gathering, inventory, crafting,
  stone mining, torch placement, food gathering/eating, shelter construction,
  furnace use, hostile combat, cave exploration, village interaction and
  surviving the night.
- Reused the existing Player signals/events and Mobs defeat event. The guide
  records successful actions rather than checking starter inventory; passive
  creature defeats do not satisfy the hostile-combat step, and farmland tilling
  does not count as shelter construction.
- Persisted individual facts in the existing world flag dictionary and retained
  the previous First Night step keys and completion flag. No world-save schema,
  generator version, BlockId, ItemId or voxel mutation changes were made.
- Added compact step progress to the existing HUD hint panel. Existing
  Settings.ShowHints and Creative-mode behavior remain in effect.
- Added quest objective stage-completion feedback through the existing quest
  progression event; the HUD announces intermediate stage advancement and
  final objective completion without changing reward-claim behavior.
- Fixed nested pause/keybind Escape ownership with a small input policy:
  while controls are open above pause, the keybind modal owns back/Escape and
  the pause menu stays paused underneath.
- Added OnboardingTests, quest-stage event regression assertions and pause /
  keybind input-policy tests.
- **Verification:** Godot 4.7.2 .NET Debug build: 0 warnings / 0 errors;
  Release build: 0 warnings / 0 errors; scripts/verify.sh: **186,422
  checks / 0 failures**.
- **Remaining Milestone 5 work:** manual controller navigation/rebinding on
  physical hardware, rendered first-person pose review, combat balance and
  enemy readability playtests, boat handling and edge-case playtests, and
  broader village/trading/quest usability review. This increment does not
  claim the entire gameplay-polish milestone is complete.
- See docs/GAMEPLAY_MILESTONE_5.md for implementation and validation details.


## 26. First-person action animation polish — 2026-11-22

- Replaced the single generic punch curve on the existing arm/item viewmodel
  with small deterministic profiles for mining, melee, block placement, item
  use, eating, bow release, shield block, landing and hotbar selection.
- Added independent held-state blends for bow draw and shield guard. The bow
  still fires on the existing input/cooldown path; the shield still uses the
  existing directional damage-block rule. The animation does not change damage,
  durability, arrow consumption or inventory timing.
- Layered locomotion bob, subtle idle breathing, climbing motion and swim
  strokes onto the same viewmodel pose. The existing HeadBob accessibility
  option also suppresses viewmodel locomotion bob.
- Kept the existing camera-child arm, held-item Sprite3D, icon/material cache,
  shadow-disabled arm meshes and camera attachment. No scene nodes, imported
  assets, per-frame collections or new persisted settings were added.
- Added FirstPersonAnimationTests for the action clock, distinct action poses,
  safe positional/rotational envelopes, held-state layering and movement pose.
- Compatibility: no gameplay input actions, persisted IDs, saves, generator
  output, block mutation, combat outcomes or chunk systems changed.
- **Verification:** Godot 4.7.2 .NET Debug and Release builds: 0 warnings /
  0 errors; scripts/verify.sh: **186,422 checks / 0 failures**.
- **Manual sign-off still required:** visually inspect all action poses while
  holding common tools, blocks, food, bow and shield at 720p/1080p on the MX230
  and RTX 4060; verify the hands do not obscure the crosshair or important
  targets; verify controller-trigger hold/release timing. The available run was
  headless and does not claim rendered visual approval.
- See docs/FIRST_PERSON_ANIMATION.md for profile intent and test coverage.
## 27. Equipment durability icon readability — 2026-11-22

- Audited the existing procedural `ItemDb` equipment art, atlas-backed block
  icons, inventory/hotbar slot refresh paths, and first-person held-item sprite.
  Durability was already saved and displayed as text for some tools/weapons,
  but armor had no visual condition indicator and hotbar weapon handling could
  treat a worn item as an ordinary count.
- Added three cached scuff stages for tools, weapons, bows, shields and armor by
  painting restrained pixel marks into the existing original 32×32 icon image.
  The pristine icon remains unchanged; block items retain the exact atlas path,
  region and sampler.
- Added a shared green/amber/red three-pixel condition meter to the existing
  inventory and hotbar slot panel draw pass. Durability values now cover every
  durable equipment type in hotbar labels and slot tooltips; worn values move
  away from the meter. Armor, chest and furnace slot panels use the same path.
- First-person held-item presentation switches to the cached icon when the wear
  stage changes and keeps the existing shared material response. It creates no
  per-frame textures or materials. Slot panels draw the meter directly, so the
  slot hierarchy gains no additional controls.
- Added `EquipmentDurabilityVisualTests` for tools/weapons/armor, empty and
  non-durable items, threshold boundaries, clamping, meter colors, icon pixel
  changes and cache reuse, atlas compatibility, and unchanged slot child count.
- Compatibility: no persisted block/item IDs, save fields, atlas tile indices,
  generator versions or world-generation output changed. No voxel edit, greedy
  meshing, lighting or chunk-streaming code was modified.
- Performance design: at most three lazy 32×32 variants per durable item, shared
  per-process cache; stage checks are constant-time, and the held sprite updates
  only on a band transition. Durability meters reuse the existing slot controls
  and are redrawn only when the displayed condition changes.
- Verification: Godot 4.7.2 .NET Debug build completed with 0 warnings and 0
  errors; `scripts/verify.sh` passed 186,379 checks with 0 failures across 39
  suites. A rendered production
  `InvSlot` fixture ran through Forward+ / Vulkan on the MX230 (960×760); a
  half-size review retained the colored meter and recognizable scuff states.
  The fixture is repeatable via the README command and writes its PNG under
  `user://`.
## 28. Generated standalone item icon cohesion — 2026-11-22

- Audited the full persisted item-icon set using a generated contact sheet.
  Ordinary full-block items continue to return their existing 32px
  AtlasTexture regions; the six slab/stair items now use cached 32px isometric
  previews sampled from their registered atlas face tiles. The packed atlas,
  tile coordinates, atlas mip policy and material response matrix are unchanged.
- Unified standalone procedural icons at the existing 32px source size:
  silhouettes are centered and scaled to a 22px maximum content frame, receive
  a one-pixel graphite outline and a consistent upper-left key-light gradient
  with lower-right edge shading, and retain four transparent pixels outside
  the contour.
- Generated a complete per-image mip chain down to 1×1. The existing first-
  person held-item Sprite3D and its slot-change-cached material now use
  nearest-with-mipmaps filtering; the packed atlas and unrelated crack overlay
  retain their original nearest-only sampling.
- Added ItemIconTests coverage for every persisted item, the empty None
  sentinel, atlas-region compatibility, cached standalone texture identity,
  silhouette bounds, binary alpha, transparent padding, contour color/width,
  shared lighting direction, and mip-chain completeness. Set
  MINECRAFT_ICON_AUDIT_PATH when running scripts/verify.sh to export a
  contact sheet for manual review.
- The finishing pass runs once when each generated standalone icon is first
  requested and the existing texture cache retains it. No per-frame scans,
  allocations, scene nodes, voxel edits, meshing, chunk streaming, persisted
  IDs, save formats, or generator versions were changed.
- Compatibility: no new assets or third-party textures were introduced; all
  icons remain procedurally authored, and existing world/save data is
  unaffected.

## 29. Consistent block-edge shading — 2026-11-22

- Standardized corner lighting for full cubes, faces split around partial
  neighbours, slab/stair micro-faces and greedy opaque merge keys. The shared
  half-voxel sampler uses the existing 2×2×2 occupancy mask and saved shape
  state, so only occupied half-cells contribute AO at a shared edge.
- Baked sky and block light are averaged from the same four half-voxel corner
  samples. Actual diagonal block and sparse state data is now copied into the
  padded chunk corners, preventing shading differences where four chunks meet.
- Kept the current four-sample AO response, vertex colour channels, face tint,
  greedy merge topology, atlas UVs, texture-array layers/mipmaps, material tags,
  first-person held-item palette and chunk streaming. No new textures, licensed
  assets, scene nodes, vertex attributes or per-frame operations were added.
- Added regressions for upper/lower slab occupancy, both stair vertical
  orientations, diagonal chunk state, packed-light averaging, and matching
  corner values in greedy and per-face mesh modes. Existing save and generator
  behavior is unaffected: no IDs, save fields, or worldgen rules changed;
  V1–V8 remain frozen and mutations remain owned by SetBlock/ApplyEdit.
- Hardened seam validation by grouping emitted vertices by half-voxel
  position and face normal. The tests now require identical baked sky,
  block-light and AO channels wherever distinct coplanar quads meet, in both
  render paths, and compare all common corners between per-face and greedy
  output. Vertex alpha remains excluded because it carries the independent
  material-family tag.
- Re-ran the existing deterministic production shape capture (seed 1337) on
  Godot 4.7.2 Forward+ / GeForce MX230 at 1280×720; both close and distant
  frames completed successfully. No visible AO seam was observed in the
  slab/stair comparison row. This is validation only, not a new lighting pass.
- **Performance:** occupancy checks and diagonal border reads are mesh-build
  work only; no extra vertices, draw calls or per-frame CPU/GPU work are added.
  The production capture was run on Godot 4.7.2 Forward+ / GeForce MX230 and
  inspected at close and distant framing. A controlled before/after frame-time
  benchmark was not run, so no FPS delta is claimed.

## 30. Water-adjacent material readability — 2026-11-22

- Added a non-persisted wet material tag in the unused vertex-colour alpha range
  (`family + 8`). Atlas and greedy texture-array shaders decode the same tag,
  add restrained cool darkening and a slightly smoother surface response, and
  retain the original dry response for all untagged geometry.
- The mesher marks only soil/sand/gravel/farmland faces whose adjacent voxel is
  water. Greedy face keys include the wet bit at bit 56 (above packed AO/light),
  preventing a wet face from merging with a dry face. Existing UV2 tile layers,
  geometry, collision output and payload ownership remain unchanged.
- Added original procedural tile 59 and append-only `BlockId.RiverReeds` 59 /
  `ItemId.RiverReeds` 108. V11 stamps them deterministically only into air above
  sand or grass shoreline columns at the waterline and beside shallow water;
  swamp banks receive a higher frequency. V1–V10 remain frozen.
- Far LOD shoreline cells use the four corner heights already collected for
  each 4m cell, applying the same damp tint to the surface cap. No additional
  terrain sample, node, physics query or per-frame scan is introduced.
- Focused regressions cover append-only IDs, the 512px/256-layer atlas layout,
  cutout art, wet-tag decoding in both opaque shaders, shoreline selection,
  far-LOD boundaries, and both greedy/per-face mesh tags.
- Compatibility: save format remains v4; no existing IDs or atlas coordinates
  move, no V1–V10 generator behavior changes, and all edits in the mesh fixture
  go through `VoxelWorld.SetBlock`.
- Performance expectations: four neighbour-height queries are performed only
  for eligible low shoreline columns; far LOD adds no sampling. Wet shader tags
  reuse `COLOR.a`, and the greedy merge key uses one previously unused bit.
  Reeds reuse the existing cross-plant mesh path and add no collision.
- **Rendered visual verification:** Godot 4.7.2 .NET / Vulkan Forward+ on the
  GeForce MX230. Seed 1337 at `(558, -478)`, close camera scale 0.20 and distant
  camera scale 1.00 with 256 m far LOD. Both 1600×900 captures succeeded with
  49 streamed chunks; see `docs/visual/screenshots/water-adjacent-v11-*.png`.
  The real-scene loader prints existing invalid resource-UID fallback warnings
  but resolves tracked script paths; neither capture reported shader/render
  failures. Other biome/weather/preset combinations remain to be reviewed.

## 32. Texture/light contrast audit — 2026-11-22

The integrated material pipeline was audited across clear daylight, overcast
rain, night and cave/torch lighting. The audit found two atlas-family gaps:
new canopy tiles 56–58 fell through to the default stone family (including its
stone-only world-space tone variation), and held placeable items maintained a
separate classification list from their placed block textures. Tile 56–58 now
use the foliage response; all placeable held sprites resolve their family from
the registered block's icon tile. The torch, stairs, weathered blocks and all
other block items therefore stay aligned with their world surfaces.

The opaque atlas, greedy texture-array, glass and animated-water shaders now
clamp the sky channel and blend warm block light only into the remaining
skylight headroom.
A restrained shadow-gated emission term preserves torch readability without
double-counting the full block-light channel on already sunlit surfaces. The
same light response is used by both meshing paths; isolated per-tile mipmaps,
atlas coordinates, AO/light vertex channels, greedy merge keys and chunk
streaming are unchanged.

No texture assets, persisted block/item IDs, save fields or generator rules
were added or modified. This is a shader/classification correction only; V1–V10
world generation remains frozen and no voxel mutation path changed. Focused
regressions cover tile-family ownership, every placeable item's held/world
response parity, all four shader-path light blends and the
daylight/rain/night/cave light contribution ordering.

**Validation:** Debug and Release builds and `scripts/verify.sh` complete with
0 compiler warnings, 0 compiler errors and 186,449 checks / 0 failures. Real
scenes were rendered on the MX230 with Godot 4.7.2 Forward+ at 1600×900 for
clear noon, rain, midnight and a torch-lit cave chamber at midnight. The
optional `[torch]` capture selects a flat dry column, carves a temporary
chamber and places the torch through `VoxelWorld.SetBlock` in its disposable
visual-test world. No standalone GPU benchmark was added for the shader-only
change; the existing benchmark matrix and baselines are unchanged.

## 33. Block selection contrast — 2026-11-22

The existing first-person target outline now uses two complementary unshaded
passes: a dark outer silhouette and fine near-white inset tracks along exposed
faces. The outer geometry stays legible on bright snow and water; the inner
tracks remain visible on dark cave surfaces and at distance. Cubes, bottom/top/
double slabs and all stair orientations share the same shape-aware builder;
partial-shape mesh pairs are cached by block ID and state, so geometry is
rebuilt only when the selected target shape changes.

This is a presentation-only change. It does not touch atlas tiles, world
materials, baked voxel lighting, ray targeting, world mutation, persisted IDs,
save formats, generator rules, greedy meshing or chunk streaming. Both outline
passes disable shadow casting and allocate no per-frame geometry or materials.
The bright inset is one additional small draw pass while a block is targeted;
the static cube meshes are built once and partial-shape pairs are shared from
the existing per-shape cache.

## 34. Registered-block material test gallery — 2026-11-22

- Added `tests/MaterialGallery.tscn`, a standalone 8×8 gallery generated from
  the complete `BlockId` registry (60 registered blocks). A neutral stage,
  fixed key / ambient lighting, per-sample ID markers and a full ID/name legend
  make the set comparable in close and distant views.
- The capture fixture reuses the current `VoxelWorld`, chunk streaming, greedy
  mesher, classic atlas shader, greedy texture-array shader and shared material
  response palette. Collision is disabled, mesher concurrency is capped at two,
  and the in-tree streaming anchor has processing / physics disabled.
- All actual voxel edits use `VoxelWorld.SetBlock` in a disposable test world.
  Air is represented by an outline because its registered definition has no
  surface; Bedrock is represented by an atlas/material preview cube because the
  world API correctly rejects Bedrock placement. Neither preview mutates world
  data or changes production placement rules.
- Added automated coverage for ID coverage and stable ordering, unique grid
  positions, face tile and UV validity, material response bounds, isolated tile
  mipmaps, block/item mapping, and first-person atlas-icon / response routing.
- Compatibility: no persisted IDs, save fields, generator versions, terrain
  generation, mutation architecture, greedy merge keys or chunk-streaming rules
  changed. The scene restores the previous selected world and greedy setting
  and removes its temporary world at shutdown.
- GPU review on Godot 4.7.2 .NET / Vulkan Forward+ / GeForce MX230: both greedy
  texture-array and classic atlas modes rendered all 60 blocks at 1280×720;
  greedy close and distant captures are committed in
  `docs/visual/screenshots/material-gallery-{close,distant}.png`. Both runs
  reached 9 loaded chunks with zero pending payloads and exited cleanly.
- **Final verification:** `dotnet build` and `scripts/verify.sh` on Godot
  4.7.2 .NET passed; **188,144 checks / 0 failures across 39 suites**, with
  0 build warnings and 0 errors.
- No production per-frame work, collider generation, or persistent resource
  allocations were added. The additional plane, labels and preview geometry are
  confined to this opt-in test scene.

## 35. Procedural atlas integrity report — 2026-11-22

Added an opt-in, headless TextureAtlasValidator and Markdown report runner.
The report reads the existing generated atlas, the registered block/item
mappings and the existing greedy texture array. It checks atlas dimensions,
missing/transparent referenced tiles, invalid face indices, repeated pixel
content across distinct tile slots, accidental duplicate six-face block
signatures, directional face pair regressions, placeable item icon regions and
texture-array layer/mipmap compatibility.

Expected duplicate block signatures are explicitly constrained to the common
still/flowing-water texture, directional plank full-cube/slab/stair family, and
cobblestone/brick slab-stair pairs. The legacy painted plank-building tile 46
is documented as intentionally unreferenced; it is not shifted, removed or
reused. Any other painted but unreferenced tile remains a warning.

The suite adds synthetic invalid-reference, missing-pixel, duplicate-image,
duplicate-definition, alias and directional-log regressions. Validation reads
existing resources once per run and is never attached to gameplay processing.
It changes no persisted IDs, save data, generator versions, atlas coordinates,
UVs, shaders, material response, first-person icon mapping, greedy meshing or
chunk streaming. The classic packed atlas keeps nearest sampling without
mipmaps; the greedy path retains isolated per-tile mipmaps for distant views.
- **Verification:** Godot 4.7.2 .NET Debug and Release builds complete with
  0 warnings / 0 errors; scripts/verify.sh reports 189,859 checks and 0
  failures across 47 suites. The standalone runner produces a report with
  0 errors / 0 warnings (60 registered blocks, 59 referenced tiles, 60
  painted tiles, 46 placeable item mappings and 256 mipmapped array layers).
- **MX230 rendered check:** the existing production MaterialGallery rendered
  all 60 block definitions in both greedy texture-array and classic atlas
  modes at 1280×720, close and distant. Each run loaded nine chunks with zero
  pending payloads and exited cleanly. No rendering or shader failures were
  reported. No FPS delta is claimed because the change adds no production
  rendering or per-frame work.

## 36. Matched material gallery visual-quality report — 2026-11-22

- Reused the integrated atlas/material pipeline and the existing material
  gallery; no production material, texture, meshing, lighting or first-person
  asset-routing implementation needed to be rebuilt.
- Locked the opt-in capture profile in `MaterialGallery`: 1280×720,
  55° FOV, fixed close/distant camera offsets, 20 m stage, 112 px legend,
  fixed key/ambient lighting and an explicit viewport-size assertion. The
  runner prints the complete profile and exact world-space target.
- Reduced the old 120×120 m gallery stage to 20×20 m and the legend panel from
  198 px to 112 px. All 60 registered blocks and the full six-row legend remain
  represented, with the display occupying more of each frame.
- Added a matched renderer comparison: classic packed atlas (`atlas`) is the
  before path; greedy texture array (`greedy`) is the after path. Both use seed
  1337, identical sample placement, camera positions and lighting. The report
  distinguishes renderer/sampling comparison from source texture-art changes.
- Committed four real-scene MX230 / Godot 4.7.2 Forward+ captures:
  `material-gallery-before-atlas-{close,distant}.png` and
  `material-gallery-after-greedy-{close,distant}.png`. The paired runs
  each settled at 9 loaded chunks with zero pending payloads.
- Added `MaterialGalleryTests` assertions for capture resolution, FOV,
  near/far camera offsets, far clip, stage/legend dimensions and the fixed
  key/ambient lighting values.
- The visual report at
  `docs/visual/MATERIAL_GALLERY_VISUAL_QUALITY_REPORT_2026-11-22.md`
  records the capture profile, visual observations, comparison limits,
  compatibility notes and all image references.
- Compatibility: no IDs, saves, generator versions, atlas coordinates,
  material responses or voxel mutation paths changed. V1–V8 remain frozen;
  `SetBlock`, greedy meshing and chunk streaming retain their existing
  ownership and behavior. No game-runtime per-frame work or new production
  allocations were introduced.
- **Verification:** Godot 4.7.2 .NET `dotnet build` and
  `scripts/verify.sh` passed; **189,856 checks / 0 failures**, 0 build
  warnings and 0 errors. Both interactive capture runs exited successfully on
  the MX230. No dedicated FPS/GPU-time benchmark was added because the change
  is confined to the opt-in test gallery and documentation.

## 37. Block/item icon semantic mapping audit — 2026-11-22

- Audited all 60 registered block definitions, the full append-only item-name
  registry (108 concrete items plus the None sentinel), every placeable
  block/item mapping, and the generated standalone icon contact sheet.
- Corrected misleading food art: raw/cooked fish now share an unmistakable
  whole-fish silhouette, while raw/cooked chicken use a drumstick silhouette.
  Cooking changes the palette without changing the food identity.
- Slabs and stairs no longer reuse the same flat side-tile icon as their full
  block. All six shape items use lazily generated isometric previews sampled
  from the existing top/side atlas tiles, with the same 32px canvas, graphite
  contour, directional shading and isolated mip chain as other standalone
  icons. Full cubes keep their exact shared AtlasTexture regions.
- Tests now pin shape-vs-material silhouette distinctions, raw/cooked identity,
  food-family differences, atlas routing for ordinary blocks, cache reuse,
  mip-chain completeness and contact-sheet isolation. The contact-sheet
  exporter duplicates a cached image before scaling and verifies that export
  leaves runtime icon dimensions and mipmaps unchanged.
- Compatibility: no BlockId or ItemId values, block definitions, atlas
  tile coordinates/dimensions, save fields, generator versions or worldgen
  output changed. No voxel edits, meshing, lighting, chunk streaming or
  first-person scene hierarchy changed. All added art is original procedural
  pixel art sampled from the game's own original atlas; no third-party or
  proprietary Minecraft assets were introduced.
- Performance: six small shape textures are generated only when first
  requested and kept in the existing item icon cache. A shared atlas image is
  materialized lazily for shape rasterization. No per-frame scans or
  allocations, extra scene nodes or physics work were added.
- Verification: Godot 4.7.2 .NET Debug build completed with 0 warnings and 0
  errors; scripts/verify.sh passed 190,043 checks with 0 failures. The generated
  item contact sheet was manually reviewed; the registered-block close/distant
  material-gallery captures were also checked. Tests confirm all 108 item icons
  remain cached at 32×32 with complete mipmaps after contact-sheet capture.

## 38. Directional sunlight and baked skylight response — 2026-11-22

- Kept the existing 600-second day/night clock, rotating directional sun,
  moon, PSSM shadow configuration and atmosphere evaluator. Added a normalized
  world-space sun direction that mirrors the existing -35-degree azimuth and
  the same time-of-day elevation, plus smooth phase-based sun and sky-fill
  colours. Dawn and sunset warm the existing directional light; night retains
  a cool skylight tint.
- Routed the direction and colour palette through ChunkMaterials, updating
  only the existing shared atlas, greedy-array, glass, lava and water
  materials. Their shaders use world-space face normals to bias the existing
  baked sky channel toward the sun-facing side. The directional light
  continues to provide the actual dynamic shadows.
- Design boundary: packed sky/block nibbles remain mesh-time lighting data;
  this is a presentation response, not a new per-frame voxel-light solve.
  No per-emitter light nodes, mesh rebuilds, vertex channels, texture assets,
  scene nodes or screen-space effects were added. Existing LOW/BALANCED
  renderer settings remain unchanged.
- Automated regressions cover dawn/noon/sunset/night directions, wraparound,
  normalization and continuity, warm low-angle colour, cool night fill, all
  five shader variants and propagation of palette uniforms into shared
  materials. The real-scene capture runner compares four clock phases at one
  fixed V8 seed/coordinate, clear-weather state, camera and viewport on the
  MX230 using Godot 4.7.2 .NET / Vulkan Forward+. All four captures completed
  at 1600×900 with 49 streamed chunks; per-frame mean luminance was 0.389
  (dawn), 0.524 (noon), 0.353 (sunset) and 0.260 (night). The captured PNGs
  are in `docs/visual/screenshots/sunlight-*-clear.png`.
- Compatibility: no save fields, persisted IDs, generator rules or versions,
  atlas tiles, packed-light propagation, SetBlock / ApplyEdit mutation paths,
  greedy meshing or chunk streaming changed.

## 39. Shared camera-depth distance haze — 2026-11-22

- V8+ outdoor environments now use Godot's built-in depth-fog mode. The
  shared environment starts haze at 96 m and eases it toward the sky through
  330 m with a 2.2 power curve and 0.42 density; the runtime cloud multiplier
  is clamped to 0.68. The ramp preserves nearby block contrast and avoids
  turning the visible horizon into an opaque fog wall.
- The effect is scene-level camera-depth fog, so opaque terrain, glass,
  water-compatible surfaces, and far-LOD geometry use the same distance
  calculation. It does not encode fog in voxel-light vertex channels or
  chunk-local shaders; a chunk boundary cannot restart the haze ramp. No
  additional fog volume, scene node, emitter light, mesh attribute, or per-frame
  terrain scan was introduced.
- The existing `DayNight` owner selects depth fog only for outdoor V8+ views.
  Cave darkness, underwater and lava immersion, the menu backdrop, and V1–V7
  worlds keep exponential fog. The existing LOW/BALANCED presets retain the
  inexpensive depth fog while continuing to disable volumetric fog.
- Added deterministic atmosphere regression checks for the depth interval,
  exponent, monotonic bounded falloff, maximum cloud-density clamp, legacy
  compatibility, cave/immersion mode transitions, and LOW/BALANCED settings.
  The real-scene capture runner accepts an optional view distance (3–5) and
  waits for the full requested chunk square before capturing.
- Repeatable Forward+ comparison: Godot 4.7.2 .NET, GeForce MX230, 1600×900,
  seed 777, V11, centre `(-424, 760)`, noon, clear weather, view distance 5
  (121 streamed chunks). Before/after images are
  `docs/visual/screenshots/distance-haze-v11-mountain-{before,after}.png`.
  The sky remains effectively unchanged; most pixel change is confined to
  terrain and the distance/horizon region, consistent with a restrained
  depth haze rather than a full-screen colour grade.
- Compatibility: rendering-only. No block/item IDs, save fields, world
  generation, voxel edit paths, baked-light propagation, greedy meshing,
  chunk streaming, or far-LOD generation rules changed. V1–V8 remain frozen.
- Verification on this branch: Debug build 0 warnings / 0 errors;
  `scripts/verify.sh` passed **189,988 checks / 0 failures** across all 46
  suites. The real-scene mountain comparison completed on MX230 at 121 chunks;
  the before/after captures settled in 471 / 470 frames respectively (capture
  settling is not a frame-time benchmark). The save suite's two intentional
  write/recovery warnings remain expected.
- Performance: no per-frame terrain sampling, voxel-light solve, chunk
  mutation, mesh rebuild, or additional draw node is added. Depth haze uses
  Godot's existing environment fog path; no stand-alone FPS delta was claimed
  because a controlled before/after frame-time benchmark was not run.
- Remaining manual review: compare sunrise, sunset, overcast and rain across
  plains, snow, forest and distant mountain ridges; recheck cave entrances,
  villages, and underwater transitions on LOW and HIGH presets.

## 40. Snow, sand and ash surface variation — 2026-11-22

The texture audit found that snow and sand were authored with independent
random speckles, which did not guarantee a seamless tile edge, and the block
registry had no ash material. The existing surface-variation suite now inspects
the generated atlas pixels directly, in addition to its shader-family checks.

- Replaced the final snow and sand paint with deterministic multi-scale
  periodic value noise. Broad, drift, grain and fine octaves use smooth
  interpolation and wrap in both axes. All samples share the existing 2×2
  texel detail footprint, keeping the texture quiet under distant mipmapping
  without adding a shader sample or runtime noise lookup.
- Applied the same calm periodic field to the exposed soil body of the
  snowy-grass side tile. Its snow cap follows a low-frequency periodic edge
  quantized to the same two-pixel detail unit, so the white/soil boundary meets
  cleanly across horizontal tile repeats without splitting a detail cluster.
- Added original procedural ash art at atlas tile 60, with append-only
  `BlockId.Ash` 60 and `ItemId.Ash` 109. Ash is a solid opaque shovel block
  that drops itself, appears in the normal item palette and material gallery,
  and uses the diffuse soil/wet-contact response plus the existing sand-like
  dig/place sounds. The held icon uses the shared atlas region.
- Design decision: retain the existing 32px atlas, isolated texture-array
  mipmaps, classic atlas sampling policy, material-family tag, greedy meshing
  and chunk streaming. The pre-existing random paint calls still consume their
  original seeded RNG draws before the new periodic pass, so later unrelated
  procedural tiles keep their deterministic appearance.
- Compatibility: existing `BlockId` 0–59, `ItemId` 0–108, atlas tiles 0–59,
  save format V4, and generator V1–V11 are unchanged. Ash does not alter terrain
  stamps or world generation. Placement/mining continue through the standard
  `VoxelWorld.SetBlock` / `ApplyEdit` architecture.
- Automated checks cover opaque pixels, tonal range, horizontal/vertical seam
  energy against interior detail for uniform surfaces, snowy-grass side
  continuity, append-only IDs, block/item round-trips, all six atlas faces,
  first-person atlas mapping and soil material classification. Existing
  mipmap and texture-detail regression tests remain active.
- Performance: the periodic field is painted once during atlas initialization;
  no per-frame scans, additional scene nodes, shader samples, physics work or
  persistent runtime allocations are introduced. The additional art occupies
  one 32×32 tile within the existing 256-tile atlas.
- **Real-scene verification:** Godot 4.7.2 .NET / Vulkan Forward+ on the
  GeForce MX230 rendered the 61-block production gallery in both classic atlas
  and greedy texture-array modes at 1280×720. Each mode reached 9 streamed
  chunks with zero pending payloads; close and distant PNG captures all
  reported successful writes. The four frames are committed under
  `docs/visual/screenshots/surface-variation-{atlas,greedy}-{close,distant}.png`.
- **Automated verification:** `dotnet build -c Release` and
  `scripts/verify.sh` passed on Godot 4.7.2 .NET with 0 compiler
  warnings/errors and **198,108 checks / 0 failures**. The atlas seam, 2×2
  cluster, opacity and tonal checks cover sand, snow and ash; the snowy-grass
  side also passes its horizontal seam and 2×2-cluster checks. The integration
  suite retains the existing V9–V11 generation anchors.
- **Performance review:** the additional periodic painting runs once during
  atlas construction. No per-frame cost, shader instruction, geometry, mesh
  rebuild, collider, streaming or physics changes were introduced. No
  controlled FPS delta is claimed; the manual captures confirm the MX230 path
  remains functional at the gallery's existing 1280×720 profile.

## 41. Consolidated integration of PRs #59–#68 — 2026-11-22

The ten open branches in this batch were merged into `integration/pr59-68` from
`main` at `b7a4dd9`, in dependency order: #65 → #64 → #60 → #59 → #62 → #61 →
#63 → #66 → #67 → #68. Every branch shared that merge base, so no branch needed
rebasing, and all ten landed as real merge commits that keep their original
commit ancestry traceable.

- **Dependency correction.** The suggested order placed #66 before #67, but #68
  patches `tests/SurfaceVariationTests.cs`, which #67 creates. #67 therefore
  lands before #68. The three atmosphere PRs (#61 → #63 → #66) also land
  consecutively so their overlapping `DayNight` fog path resolves once, in one
  coherent shape, rather than three times.
- **Lighting/atmosphere reconciliation.** Smooth cave exposure (#61),
  phase-aware directional sunlight (#63), camera-depth distance haze (#66) and
  unified torch/campfire emission (#68) now share one presentation path.
  `DayNight` derives a continuous cave weight from trilinear cached skylight and
  reuses it as the shelter predicate that selects exponential fog, so a cave
  mouth no longer flips fog mode at a fixed terrain depth. The three lit shaders
  (`LitAtlasShader`, `LitGlassShader`, `OpaqueArrayShader`) each combine
  `directional_sky_light(...)` with the single `warm_block_response` /
  `warm_block_illumination` / `warm_block_emission` helper; the lava shader keeps
  its own emissive path. No per-torch light node, per-frame scan or extra shader
  pass was added — campfires in fact lose one `OmniLight3D` each.
- **Item presentation reconciliation.** #59 and #62 both rewrote the `ItemDb`
  icon entry points. The merged form keeps shape-specific isometric previews for
  the six slab/stair items and the exact `AtlasTexture` region for every other
  block item, layered with the cached rarity facets, enchantment glints and wear
  bands. `Player` keeps the single icon-state refresh that reacts to wear,
  enchantment and item-ID changes without recreating its material.
- **Atlas validator contract.** #65's validator originally required every
  placeable item to expose an `AtlasTexture` region, which #59's shape previews
  intentionally do not. `TextureAtlasValidator` now records an explicit
  `UsesShapeIcon` contract: shape items must not also claim the atlas path, all
  other placeable items must still match their exact region, and both paths
  still require valid, painted face tiles. This is a documented contract update,
  not a relaxed assertion, and it is pinned by
  `TextureAtlasValidationTests.TestShapePreviewIconContract`.
- **Content.** #67 appends `BlockId` 60 (Ash) and `ItemId` 109 (Ash) after every
  existing value, on atlas tile 60, with soil material response, shovel mining,
  block-item drop, sand-family dig/place audio and wet-surface participation.
  No existing ID or atlas coordinate moved.
- **Documentation.** `README.md` and `ROADMAP.md` collided on three separate
  "section 35" headings; each PR record is preserved and renumbered 35–40, with
  this section added. `PLAN.md` keeps its historical role and was not rewritten.
- **Verification (this integration):** Debug and Release builds both completed
  with 0 warnings and 0 errors; `scripts/verify.sh` passed **198,718 checks, 0
  failures across 48 suites**. The standalone atlas report passes with 61
  registered blocks, 60 referenced and 61 painted tiles, 47 placeable mappings,
  256 mipmapped array layers and 0 errors / 0 warnings. Real-scene MX230
  Forward+ captures reproduced matched references for warm lighting
  (flame 14 / adjacent 13 in torch, campfire and overlap), the cave gradient
  (15.00 / 14.00 / 5.27, darkness 0.778), four sun phases (mean luminance 0.366
  / 0.518 / 0.335 / 0.260), distance haze at 121 chunks, and both gallery
  renderer paths with 61 blocks.
- **Compatibility:** save format V4, every persisted `BlockId`/`ItemId` value,
  generator V1–V11 output and golden hashes, the greedy mesher, chunk streaming,
  the packed atlas and texture-array contracts, and the `SetBlock`/`ApplyEdit`
  mutation seam are unchanged. No world generation version was introduced by
  this batch because no generator rule changed.


## 42. Controlled voxel-light comparison scenes — 2026-11-22

- **Audit result.** Baked sky/block channels, incremental emitter mutation,
  warm torch/campfire propagation, transparent-shader response, camera-driven
  cave exposure, directional atmosphere, the Filmic WorldEnvironment and the
  LOW/BALANCED renderer options were already integrated in the main branch.
  The missing piece was a repeatable real-world comparison for forest canopy,
  torch cave, river valley, village street and dungeon arena; production
  lighting did not need another implementation.
- **Fixtures.** Added five standalone scenes backed by one
  `LightingComparisonCapture` runner and five immutable camera/time profiles.
  Each runner loads `scenes/main.tscn`, uses the current V11 generator, a fixed
  seed, clear weather, 1280×720 output and one explicitly selected Gfx preset.
  The forest/river/village/dungeon positions are selected deterministically
  from generated terrain and structures. The cave builds a small stone chamber
  and open skylight shaft in a disposable world.
- **Ownership and design.** Every cave block edit uses `VoxelWorld.SetBlock`,
  so existing edit epochs, source registration and cache invalidation remain
  authoritative. All emitters continue to use the existing packed voxel-light
  caches and shared shaders; the runner asserts that no `OmniLight3D` nodes
  appear. The 5×5 render-only preview bounds capture cost, while LOW stays the
  default at its existing 0.80 scale with expensive post effects disabled.
- **Validation.** LightingComparisonTests pins all five scene/profile
  bindings, common output dimensions and view/chunk bounds, low-end preset
  settings, shared Filmic/ambient/fog environment properties, and the baked
  sky/block/AO vertex-channel layout. Godot 4.7.2 Debug build: 0 warnings /
  0 errors. scripts/verify.sh: **198,773 checks, 0 failures**. Five real
  1280×720 Forward+ captures completed on the MX230 at LOW; all reported
  OmniLight3D=0. The torch cave probe read sky/block 10/14, the village torch
  11/14, the dungeon torch 6/14 and river water 13/0. The forest image covers
  canopy shading; its probe is on opaque trunk wood (0/0). Full locations and
  capture files are indexed in
  docs/visual/LIGHTING_COMPARISON_REPORT_2026-11-22.md.
- **Compatibility and performance.** No production lighting, renderer
  settings, shader code, greedy meshing, chunk streaming, saved fields,
  BlockId/ItemId, atlas IDs or generator V1–V11 rules were changed. The
  location scans run once at capture startup and are not part of gameplay.
  No gameplay frame, physics or per-emitter node cost is added.

## 43. Chunk-stream lighting continuity validation — 2026-11-22

- Audited the existing stream/lighting path before changing it. Every mesh
  neighbourhood uses the snapshot-locked `VoxelWorld.LightDataNeighborhood`
  solve: one 5×5 block snapshot, a 14-voxel attenuation halo, nine packed
  sky/block result maps cached for adjacent chunk workers. `ApplyBuild` calls
  the existing `Chunk.FadeIn` for fresh streamed chunks; opaque, glass, water
  and lava surfaces transition over 0.4 seconds after their baked vertex light
  is already present. Camera cave exposure reads the existing cached sky
  channel with trilinear interpolation. No production lighting, shader,
  environment, graphics-preset, mesher or streaming implementation needed a
  rebuild for this task.
- Added an integration regression that requests neighbouring 3×3 lighting
  snapshots in west-first and east-first order, then compares packed sky and
  block channels for all six shared chunks. It also samples camera skylight
  immediately on both sides of a negative-to-zero chunk seam. This pins cache
  order independence and seam interpolation without adding runtime work.
- Added `ChunkLightingStreamingCapture.tscn`, a repeatable production-main-scene
  capture. A source torch is placed at the boundary of an already loaded chunk
  through `SetBlock`; the adjacent target starts unloaded, then streams in when
  a hidden player moves. The fixture writes before/fade/settled images and
  verifies the first voxel of the arriving chunk has propagated baked light.
  It runs in both classic atlas and greedy meshing modes with the same seed,
  generator, LOW preset, camera and view distance.
- **MX230 visual validation:** Godot 4.7.2 .NET / Vulkan Forward+, seed 1337,
  V11 normal world, 1600×900, view distance 2. The source torch is at
  `(15,41,24)` and target chunk `(1,1)` was absent in both before-captures.
  Atlas mode streamed it in 36 frames; greedy mode took 33 frames. Both report
  block light 13 at the first target voxel from the 14-level source, camera
  skylight 15.00, and 35 loaded chunks at the settled capture. Matching PNGs:
  `docs/visual/screenshots/chunk-light-{atlas,greedy}.{before,fade,settled}.png`.
- Performance/compatibility: production runtime cost is unchanged; the capture
  performs one fixture-only seam lookup and the test compares cached arrays
  only when invoked. No per-emitter lights, per-frame scans, new renderer
  effects, physics work, save fields, IDs, voxel-generation rules, generator
  versions, edits outside `SetBlock`, greedy merge keys, or chunk-streaming
  policies were introduced. V1–V11 and save V4 remain frozen.
- **Verification:** `dotnet build` and `dotnet build -c Release` both pass with
  0 warnings / 0 errors. `scripts/verify.sh` passes **198,736 checks / 0
  failures across 48 suites** with Godot 4.7.2 .NET. This includes the 18 new
  lighting-neighbourhood order and camera seam checks. The intentional save
  failure/recovery warnings remain expected test output.

## 44. Shape-aware baked light continuity — 2026-11-22

- Closed a mismatch between the existing 2×2×2 slab/stair geometry and the
  baked voxel-light solver: partial solid shapes no longer inherit the
  full-cube opacity rule when their connected empty half-cells form a passage.
- Added cached 24-bit face-aperture masks for the three slab states and eight
  stair orientations. Each of the six faces carries four half-face bits in
  shared tangent coordinates. A flood crosses a voxel boundary only where
  the source and destination apertures overlap, preventing an upper stair
  opening from leaking into a lower opening that does not meet it. Direct
  sunlight uses matching top/bottom aperture bits, so an open slab cavity can
  receive sun without passing it through an occupied underside.
- Existing cube opacity and attenuation remain authoritative: glass keeps its
  zero-loss path, water and foliage keep their current attenuation, and
  full-block/double-slab occlusion is unchanged. The solver deliberately
  treats disconnected empty components as opaque because one packed nibble
  cannot represent independent cavity light levels.
- LightDataNeighborhood, the per-chunk block-light halo, and incremental
  emitter updates now carry optional sparse shape states. The contiguous
  state snapshot is allocated only when the sampled neighbourhood contains a
  non-default shape state. Shape orientation/half changes are detected at
  VoxelWorld.SetBlock and invalidate the existing affected 3×3 packed-light
  cache; no voxel array is edited outside the normal mutation seam.
- Automated regressions cover all four stair facings and inverted states,
  exact four-bit aperture alignment, mismatched adjacent stair openings,
  horizontal light through slab/stair cavities, direct sunlight through open
  and closed shape faces, foliage gaps, and a SetBlock slab-state change
  crossing a chunk boundary. Existing greedy/per-face geometry, saved shape
  states, streaming and V1–V11 world-generation checks remain in place.
- Performance boundary: aperture connectivity is computed once for the 11
  registered shape states and accessed through a static lookup table. Ordinary
  blocks retain the existing opacity fast path. Shape-state buffers are
  conditional, light work remains baked and event/cache driven, and the change
  adds no emitter nodes, render passes, per-frame scans, physics work or mesh
  topology changes.
- Compatibility: no BlockId/ItemId values, save fields, atlas indices or
  generator rules changed. Save V4 and generator V1–V11 remain byte-compatible;
  V1–V8 stay frozen. Greedy meshing, chunk streaming, packed vertex channels,
  shaders and SetBlock/ApplyEdit ownership remain intact.
- Verification: Godot 4.7.2 .NET Debug and Release builds completed with
  0 warnings and 0 errors. scripts/verify.sh passed **198,771 checks / 0
  failures**. The only save output was the suite's two intentional
  atomic-write-failure / backup-recovery warnings.
- Real-scene validation: the new repeatable capture ran on GeForce MX230,
  Vulkan Forward+, 1600×900, seed 1337 / V11, centre (8, 8), with 34 chunks.
  Baked samples were glass 6, water 5, bottom slab 6 and upright stairs 6;
  skylight below foliage was 13 and below the open canopy gap was 15. The
  reference frame is docs/visual/screenshots/voxel-light-continuity.png.
  The production main scene reported pre-existing stale external-resource UID
  warnings and loaded each affected script using its text path; the capture
  completed successfully. No controlled FPS delta was measured.

## 45. Emissive surface / packed-channel audit — 2026-11-22

- Closed a renderer contract gap left by the unified torch/campfire pass:
  animated water had retained its earlier amber blend, instead of using the
  same sky-headroom response, hue and bounded residual emission as the atlas,
  greedy texture-array and glass surfaces. Water now calls the shared
  `warm_block_response`, `warm_block_illumination` and `warm_block_emission`
  functions once per fragment; wave motion, shore fade, transparency and
  underwater caustics are unchanged.
- Lava remains a dedicated opaque, depth-writing emissive surface so its
  recognizable orange molten core does not inherit the torch/campfire hue.
  Its emissive intensity now follows the baked low nibble in `COLOR.g`, scaled
  against the registered lava source level (`LightEmission[Lava] / 15`, 12/15).
  The material reads this source level from the existing block table; it no
  longer adds a constant glow when packed block light is absent.
- Extended the warm-light comparison fixture with a lava-only mode and added
  regressions for the nibble-to-vertex-channel mapping, the lava material's
  source-level uniform, zero-channel emissive suppression contract, and exact
  water/shared-shader response parity.
- Compatibility: no BlockId/ItemId values, save fields, generator V1–V11
  outputs, block-light propagation, voxel mutation routes, mesh vertex layout,
  greedy merge keys, chunk streaming, world environment, or graphics presets
  changed. No per-emitter light nodes or scene nodes were added.
- Performance: the water fragment path adds the same single exponential
  response evaluation already used by terrain/glass; lava replaces its
  unconditional glow with a bounded packed-channel normalization. No CPU
  per-frame work, memory allocation, physics, mesh rebuild or stream work was
  added. LOW/BALANCED continue to disable glow, SSAO, SSR and volumetric fog.
- **Repeatable visual fixture:** the existing real-main-scene capture now
  supports fixed seed 1337 / generator V11 / campfire site `(0,0)` and the
  same camera for `torch`, `campfire`, `overlap` and `lava` modes. Each pass
  logs its packed source and adjacent nibble levels and writes a PNG.
- **Current manual-check status:** the fixture launched under Godot 4.7.2
  Vulkan Forward+ on the MX230, but this attempt reached its 90-second timeout
  before writing a screenshot/result while other Godot render/test processes
  were active. No new pixel-level visual comparison or FPS delta is claimed.
  Re-run on an idle desktop for screenshot verification and a sustained
  frame-time soak.

## 46. Nighttime hostile silhouette contrast — 2026-11-22

**Goal:** preserve genuinely dark nights while making dark hostile actors distinguishable from terrain without adding per-actor lights or increasing global illumination.

**Implementation:**
- Lowered clear-night environment ambient and baked sky response from 0.10 to 0.065, while retaining the existing directional moon as the outdoor key and reducing its energy from 0.22 to 0.18. Midday settings remain unchanged; clouds and lightning continue through their existing palette paths.
- Added a restrained light-dependent rim response to the procedural hostile body-material factory. Passive creatures remain unchanged; hostile bodies are not emissive, and the existing Shade eye emission is untouched.
- Extended the atmosphere visual capture with a deterministic Dreadwing/Shade/Ember Warden lineup. Its temporary sky platform uses `VoxelWorld.SetBlock` and exists only in the disposable capture world.
- No voxel IDs, save fields, generator rules, baked-light packing, propagation, greedy meshing, chunk streaming, scene light-node count, or LOW/BALANCED renderer budgets changed.

**Validation:** Godot 4.7.2 .NET / .NET 9 Debug build (0 warnings, 0 errors); `scripts/verify.sh` exits 0; focused lighting and atmosphere suites pass; the full runner reports **198,728 checks, 0 failures**. The matched clear-midnight capture uses seed 1337 / V11 / `(512, -372)` on the NVIDIA MX230. Its mean frame luminance changes 26.29 → 25.96, sky 20.37 → 19.96, and unlit stage 10.01 → 6.77. Capture artifacts are stored under `docs/visual/screenshots/night-hostiles-*.png`.


## 47. Moonlit voxel surfaces and cloud-aware stars — 2026-11-22

- Reused the existing DayNight moon DirectionalLight3D; its normalized
  direction remains opposite the sun and its blue-white tint/energy are sent
  through the existing shared material owner. Atlas, greedy texture-array,
  glass, water and lava shaders now receive moon direction, colour and energy.
- Moon fill is gated by packed sky exposure separately from the low global
  sky-energy scalar. Exposed surfaces receive a visible directional
  contribution while cave faces with zero sky exposure receive none. The
  existing 0.065 night ambient / baked-skylight floor and 0.18 moon key remain
  unchanged; cloud cover continues to attenuate moon energy.
- The procedural starfield retains its one sphere and one texture. Its
  deterministic image gains restrained colour/size variation and low-energy
  emission; visibility fades through twilight and monotonically decreases as
  cloud cover increases. No per-star objects or texture updates occur in the
  frame loop.
- Added a focused moonlight suite for sun/moon opposition, normalized
  directions, low night-fill invariants, cloud attenuation, bounded star
  visibility, and the shared atlas/greedy/glass/water/lava shader uniforms.
- Save format, persisted IDs, V1–V11 world generation, block-light propagation,
  voxel mutation ownership, mesh channels, greedy meshing, chunk streaming and
  LOW/BALANCED renderer budgets are unchanged. No light nodes, shadow passes,
  mesh rebuilds, per-frame scans, physics work or new GPU effects are added.
- Verification: Godot 4.7.2 .NET Debug build completed with 0 warnings and
  0 errors. scripts/verify.sh passed 199,065 checks with 0 failures across
  the complete registered suite. Automated shader/material regressions pass.
  A matched real-scene MX230 visual capture was attempted but did not complete
  in this environment; pixel-level review and a sustained FPS soak remain
  outstanding before marking this change fully visually verified.

## 48. Bounded cave eye adaptation — 2026-11-22

Cave shelter already used the camera's trilinearly sampled baked skylight to
blend ambient fill and fog. The remaining mismatch was the independent global
camera auto-exposure: it could continue raising a sealed room, and a bright
torch in that room could influence the camera histogram even though the
voxel-light shader correctly confined the warm source to nearby surfaces.

- **Shared sky signal.** DayNight now uses the existing caveDarkness weight
  to update CameraAttributesPractical.AutoExposureMaxSensitivity. The range
  eases from 140 outdoors to 40 in a zero-skylight cave. The cave cap equals
  Player's 40 minimum sensitivity, so global exposure has no upward headroom
  in a fully sealed cave. It is based on sky light only; block light does not
  participate.
- **Ambient fill.** The environment ambient scale eases from 1.0 outdoors to
  0.08 at full cave darkness. The existing shader base fill remains unchanged,
  retaining the deliberately small material floor without lifting the whole
  cave. The camera attribute resource is cached at setup and only written when
  its target changes by at least 0.5 sensitivity units.
- **Torch behaviour.** No baked propagation, source strengths, warm shader
  coefficients, light nodes, or rendering passes changed. Torches and
  campfires still use their 14-level baked source channel, with the existing
  shared 0.34 diffuse and 0.025 emissive warm response. A torch in a zero-sky
  room therefore adds local block illumination without restoring global
  exposure headroom.
- **Validation.** Atmosphere tests pin the open-sky, entrance and sealed-cave
  sensitivity/ambient bounds, monotonicity and interpolation. The production
  AtmosphereVisualCapture now supports sealed, entrance and sealed-torch
  fixtures, all edited through VoxelWorld.SetBlock and rendered with the
  production camera attributes. Repeatable captures use seed 1337, generator
  V11, location (512, -372), clear noon, 1600×900 and view radius 3; results are
  stored under docs/visual/screenshots/cave-adaptation-*.png.
- **Compatibility and performance.** No persisted IDs, save fields, generator
  versions or generation rules changed. V1–V11 output remains frozen; greedy
  meshing, chunk streaming, cached light propagation and mutation ownership are
  untouched. The runtime adds one cached camera-resource reference, constant
  time interpolation and throttled property writes; it adds no scene nodes,
  per-emitter lights, per-frame scans, shader work or memory allocations.
  LOW/BALANCED settings remain unchanged.


## 49. Context-aware exposure transitions — 2026-11-22

The cave-only exposure cap is now part of one context-aware camera profile
that also covers water emergence and the snow-to-forest brightness range. The
existing `Player` immersion blend and `DayNight` cached biome column remain the
owners of their respective signals; no new global lighting system was added.

- **Caves:** retain the baked skylight-only shelter weight. It has final
  priority over every other profile and holds min/max sensitivity at ISO 40
  when skylight reaches zero. Block light remains excluded, preserving local
  torch/campfire readability without globally lifting the cave.
- **Water:** the profile follows `Player.WaterViewBlend`, not only the binary
  `Underwater` flag. The underwater exposure cap therefore remains active while
  the existing fog environment fades out after the camera breaks the surface.
  No new water probes or physics queries were added.
- **Snow and forest:** the existing integer-column biome sample selects bright
  snow and shaded-canopy weights. Snow eases the sensitivity range down
  (ISO 32–124); dense forest raises only its upper bound (to ISO 156), with
  intermediate taiga/birch/jungle/swamp weights. Fog tint, exposure weights and
  limits ease toward the new column over the same frame-rate-independent
  exponential blend (2.8 s⁻¹), so boundaries do not snap.
- **Adaptation:** `AutoExposureSpeed` is set to 0.70 while the context gets
  darker and 1.40 when it brightens, with a 0.90 steady value. The directional
  speed is held for one second after the transition signal, then returns to the
  steady value; this gives Godot's luminance adaptation time to respond instead
  of changing speed for only one frame. Sensitivity and speed properties are
  only written when their target changes enough to be visible.
- **Tests and captures:** added bounded-profile tests for cave/water/snow/forest
  priority, biome-profile selection, directional speed and 30/60 FPS blend
  parity. `AtmosphereVisualCapture` now shares the gameplay camera attributes
  for every capture and reports biome, immersion blend, ISO min/max and speed.
  The MX230 / Godot 4.7.2 Forward+ water-exit capture (seed 1337, V11, clear
  noon, 1600×900, 49 chunks) measured underwater blend 0.720 / ISO 37.4–117.0,
  then surfaced blend 0.000 / ISO 39.6–140.0. The paired biome capture streamed
  70 chunks and measured Snow at `(512, -388)` / ISO 32.3–124.3 and Taiga at
  `(544, -380)` / ISO 39.6–153.4. A sealed-torch cave capture streamed 56 chunks
  and measured 15.00 skylight outside / 0.00 in the room, darkness 1.000, max
  ISO 40.0, ambient scale 0.079 and baked torch/adjacent levels 14/13. PNG
  references are committed as
  `docs/visual/screenshots/exposure-{cave-sealed-torch,water-exit-underwater,water-exit-emerged,biome-pair-snow,biome-pair-forest}.png`.
- **Compatibility/performance:** only camera attributes and the current biome
  fog tint are adjusted. Packed sky/block channels, emitter strengths, shaders,
  renderer presets, saves, persisted IDs, V1–V11 generation, greedy meshing,
  chunk streaming and SetBlock/ApplyEdit ownership are unchanged. Work remains
  bounded to the existing cached light sample, one cached biome sample when
  the player enters a new column, and a few scalar/color interpolations.
  Auto-exposure itself remains the pre-existing camera feature; no new
  renderer pass or cost was added to LOW/BALANCED post-processing settings.
  This was not a controlled FPS benchmark, so no frame-time delta is claimed;
  structural review and the real MX230 captures confirm the path functions,
  while a sustained low-preset frame-time soak remains a separate validation.

## 50. Shadow softness and contact shading — 2026-11-22

- Audited the live main-scene Sun, DayNight.Setup, WorldEnvironmentFactory,
  Gfx preset application, the existing baked vertex-AO mesher and the material
  gallery. The sun already used four blended directional cascades; the gap was
  that DayNight forced the renderer-global SoftHigh filter and 0.5-degree
  directional PCSS for every preset, including LOW. Existing LOW/BALANCED
  presets disabled SSAO but did not own the global shadow-filter setting.
- Moved filter quality, angular source size and shadow blur into the existing
  Gfx.Preset. LOW/BALANCED now use SoftLow, keep the existing 1024 px atlas,
  preserve their 45 m / 65 m shadow distances, use a restrained 0.35 blur and
  set sun angular distance to zero to avoid directional PCSS. HIGH uses
  SoftMedium with a restrained 0.18-degree angular size; ULTRA/MAX progressively
  increase filter tier and angular softness. The same profile is applied to
  the existing sun when presets are changed; no light node is added.
- Design boundary: Godot's directional filter-quality API is renderer-global,
  not per-light or per-viewport. Gfx therefore owns it and applies it on
  every preset change. DayNight retains four blended cascades and no longer
  overwrites the selected sun profile; the low-energy moon uses filtered
  shadows with zero angular PCSS. The baked four-corner voxel AO remains the
  low-cost contact baseline. HIGH+ enables a tighter SSAO radius/intensity,
  with direct-light and AO-texture channel contribution disabled. LOW/BALANCED
  retain no SSAO/SSR/glow/volumetric-fog pass.
- Extended the production material gallery with an optional profile argument:
  -1 recreates the previous LOW shadow setup, 0 applies new LOW and 2 applies
  HIGH. The runner fixes seed, camera, direction, material layout and greedy
  meshing, and logs the applied filter, atlas, distance, angular size, blur
  and SSAO profile for each image pair. The MX230 / Godot 4.7.2 Forward+ captures
  use seed 1337, 1280×720, 61 blocks and nine ready chunks; committed close/far
  pairs are in docs/visual/shadow-softness for legacy LOW, new LOW and HIGH.
- Automated graphics regressions pin the low-end filter/PCSS choices, the
  HIGH/ULTRA/MAX progression, per-profile light application, contact-AO
  parameters, legacy graphics-preset migration and the factory defaults.
  The existing lighting and block-shape suites continue to cover packed sky /
  block light, greedy AO continuity and partial-shape contacts.
- Performance boundary: LOW/BALANCED reduce directional filtering from
  SoftHigh to SoftLow and remove the old PCSS angular-size cost. Their
  screen-space effects remain disabled and the baked voxel/AO mesh path is
  unchanged. HIGH+ opts into more expensive filtering/PCSS and local SSAO.
  No controlled FPS delta is claimed; no new per-frame work, emitter nodes,
  chunk rebuilds, physics work or persistent allocations are introduced.
- Compatibility: no save fields, persisted block/item IDs, generation rules
  or versions, baked-light channels, voxel mutation paths, greedy meshing or
  chunk streaming changed. V1–V11 and save V4 remain untouched.

## 51. Weather sky, horizon and cloud quality audit — 2026-11-22

The atmosphere audit found two concrete gaps despite the existing day/night
and distance-haze work: cloud tint was computed separately from the sky
palette and barely distinguished the four weather targets, and the visual
capture parser explicitly rejected `Storm`, silently falling back to clear
weather. The capture runner also had no way to select each live graphics
preset, so the requested weather/preset matrix could not be reproduced from
one fixture.

- Added cloud tint and weather-driven opacity to the existing deterministic
  `DayNight.SkyPalette`. Daylight/night, overcast cover, twilight and lightning
  now feed both sky/horizon and the two existing cloud materials. Clear weather
  retains faint cloud wisps; cloudy, rain and storm increase coverage and
  progressively lower cloud brightness. Lightning lifts the storm cloud tint
  together with the horizon.
- Reused the two existing cloud planes and standard materials. No extra nodes,
  emitters, per-frame world scans, shader passes, textures, voxel edits or
  persisted data were added. The sky/cloud palette is quality-independent;
  LOW/BALANCED still disable SSAO, SSR, glow and volumetric fog while retaining
  inexpensive base fog and the existing two cloud layers.
- Extended `AtmosphereVisualCapture` to select presets 0–5 through
  `Gfx.ApplyPreset(..., persist: false)`, report the active environment profile
  and capture all four weather states. Its legacy include-torch/view-distance
  argument order remains supported. The capture-only weather setup freezes
  state transitions and random lightning so matrix images stay comparable;
  lightning illumination itself remains part of the deterministic palette
  regression coverage.
- Added automated checks for ordered cloud coverage and brightness across
  clear/cloudy/rain/storm, upper-sky and horizon dimming, night cloud colour,
  lightning lift, non-finite inputs, weather parser validation, all six
  quality-preset weather combinations and the LOW/BALANCED post-effect limits.
- Compatibility: no save or world format, persisted IDs, generator V1–V11,
  baked light channels, voxel mutation paths, greedy meshing, chunk streaming,
  renderer material ownership or world-generation code changed.
- **Verification:** Godot 4.7.2 .NET Debug and Release builds both succeeded
  with 0 warnings / 0 errors. `scripts/verify.sh` passed **198,807 checks,
  0 failures** across the registered suites. The two save warnings are the
  existing negative-path checks for a deliberate failed atomic write and
  backup recovery.
- **Real-scene visual matrix:** all 24 unique captures (four weather states
  × six presets) passed on NVIDIA GeForce MX230 / Vulkan Forward+ using seed
  1337, generator V11, centre (512, -372), noon, 1600×900 and 49 streamed
  chunks. The first FSR2/cloudy frame was discarded because the temporal
  output had not settled; a 16-second FSR2 warm-up produced the stable
  replacement. The standard presets retain the 8-second warm-up.
- The matched central-sky / horizon 8-bit mean-luminance samples consistently
  fall from Clear → Cloudy → Rain → Storm. Sky/horizon values are:
  LOW 136.7/140.9 → 127.0/108.5 → 119.1/71.2 → 117.0/59.8;
  BALANCED 136.5/140.3 → 126.9/108.2 → 119.1/71.0 → 116.9/59.7;
  HIGH 136.5/138.8 → 127.5/108.4 → 119.1/70.5 → 117.0/59.0;
  ULTRA 136.7/139.2 → 127.5/107.9 → 119.2/70.2 → 117.0/58.9;
  MAX 138.6/142.0 → 128.2/110.0 → 119.6/72.8 → 117.0/61.2;
  MAX · FSR2 138.5/140.2 → 128.1/109.7 → 120.5/75.7 → 117.9/63.5.
  These are repeatable screenshot-region comparisons, not absolute
  photometric calibration.
- LOW/BALANCED captures reported SSAO, SSR, glow and volumetric fog disabled;
  HIGH/ULTRA enable SSAO and glow, ULTRA also enables SSR; MAX / MAX · FSR2
  enable volumetric fog. The shared weather palette remains unchanged across
  the quality settings.
- **Performance:** only the existing per-frame DayNight palette calculation
  and two existing cloud-material colour/alpha updates changed. No added
  scene nodes, materials, shader samples/passes, textures, emitters,
  per-frame world scans, voxel work or physics work. No FPS delta is claimed;
  the capture matrix is a visual compatibility check, not a controlled
  frame-time benchmark.

## 52. Consolidated integration of PRs #70–#79 — 2026-11-23

The nine live branches in this batch were merged into `integration/pr70-79`
from `main` at `c5093cc`, in dependency order: #77 → #78 → #79 → #70 → #71 →
#72 → #74 → #75 → #76. Every branch landed as a real merge commit. #73 was
closed as superseded by #74 because the two implemented the same cave
adaptation in parallel; #74 also adds water, snow and forest exposure contexts,
and merging both would have duplicated camera logic. #72 contains #71's head,
so merging #71 first applied only its moonlight/starfield contribution.

- **Semantic merges.** The water shader keeps #70's warm response and the #72
  moon term in one pass. Lava keeps packed-channel emission plus the same moon
  term. `SkyPalette` is one record containing sky, sun, moon and cloud fields,
  consumed by #76's weather presentation. The atmosphere capture harness now
  has one parser for legacy and preset-first arguments, cave modes, exposure
  modes, hostiles and weather.
- **Validation fixes.** #74's shared adaptive camera attributes were scoped to
  cave and exposure captures; using them for every capture made #71's night and
  #76's weather references depend on adaptation time. Captures default to LOW
  rather than the saved preset. The hostile fixture waits for the mesh queue
  and eye adaptation before sampling. The moon shader contribution is scaled by
  0.12 so directional moonlight remains visible without undoing #71's dark
  night; the silhouette capture then measures 25.51 mean luminance against the
  25.96 reference (MAE 2.94).
- **Real-scene validation.** On the MX230 / Vulkan Forward+, warm captures
  reported torch/campfire/overlap 14→13 and lava 12→11; sealed, entrance and
  sealed-torch caves matched at MAE 0.49 / 3.14 / 1.39 with room skylight 0,
  ISO max 40 and torch 14/13; water and snow/taiga at MAX matched at MAE
  0.54–2.11; shadow profiles matched at 0.34–3.21; five controlled lighting
  scenes at 0.02–1.24; partial-shape light 6/5/6/6 and foliage 13 vs 15; chunk
  seam atlas/greedy at 1.53–2.13 and cross-path at 1.51–1.95. Weather luminance
  fell monotonically from 130.11 (Clear) to 101.40 (Storm).
- **Performance.** Release / LOW / 1280×720 / view distance 5 / seed 1337,
  three matched 30-second runs after a 10-second warm-up: median 9.72 ms p50,
  11.11 ms p95 and 21.71 ms p99 (99.2 mean FPS), versus 12.96 / 16.67 / 49.87
  ms on `main` `c5093cc` under the same harness. Heavy rain measured 10.00 ms
  p50 and 17.59 ms p95. The difference is a whole-branch measurement and was
  not bisected; it establishes no regression rather than a single-cause win.
- **Compatibility.** `BlockId`/`ItemId`, save V4, V1–V11 generation, golden
  hashes, greedy meshing, chunk streaming, packed light channels and the
  `SetBlock`/`ApplyEdit` seam are unchanged. Debug and Release builds report
  0 warnings / 0 errors; `scripts/verify.sh` passes **199,417 checks, 0
  failures across 48 suites**; the atlas validator reports 0 errors and
  `git diff --check` is clean.
- **Remaining limitations.** The committed exposure references use MAX, so
  they are reproduced with preset index 4 while a bare capture defaults to
  LOW. The sealed cave's bright blue fog is pre-existing reference behaviour.
  Weather captures are terrain-facing, so cloud planes are verified by palette
  values and tests rather than framed pixels. Interactive playtesting and GPU
  frame-time capture remain manual.
