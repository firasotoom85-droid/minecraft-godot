# ROADMAP — 1.0 “Cozy Survival” (M5–M10)

Generated 2026-10-21 from a code audit of the current project plus the
progress recorded in `PLAN.md`. `PLAN.md` remains the historical progress log
(M0–M4b); this file is the forward plan.

**Baseline note:** this roadmap began as a source audit before the M5
verification work. Historical measurements remain in docs/PERF_BASELINE.md;
the repeatable procedure is in docs/PERFORMANCE_BENCHMARKS.md. The MX230 now
has a controlled Release baseline and Milestone 2 comparison in
docs/PERFORMANCE_MILESTONE_2_MX230.md. The equivalent RTX 4060 comparison
remains pending.

---

## Current follow-up implementation — 2026-11-22 (V10 biome foliage)

Newly created worlds now use generator V10. V10 keeps V9 terrain, structure
placement and deterministic weathering, and maps the spruce, jungle and swamp
species that V8 already selects to their own canopy blocks. V1–V9 generation
and saved worlds remain frozen, save format stays v4, and persisted IDs are
appended at BlockId 56–58 / ItemId 105–107. Atlas tiles 56–58 follow the
weathered materials, so every material keeps a unique atlas coordinate.

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

## 1. Current state

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
2. **Capacity before content.** Raise block/atlas limits first, or M6 will
   silently break.
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
| **M5** | Foundation & guardrails | 1–1.5 wk | — | Tests, content capacity, save v3, perf baseline |
| **M6** | Base & progression | 3–4 wk | M5 | Torch/light, chest, furnace/ingots/armor, farming, bed, swimming |
| **M7** | Living world & goals | 3–4 wk | M6 | Mob roster, dungeon/boss, trader, fishing, achievements |
| **M8** | UX, platform & 1.0 | 2–3 wk | M5–M7 | Rebinding/gamepad, onboarding, world management, EN/AR, music, release |
| **M9** | Scale & online | 4–8 wk | 1.0 | Mid-LOD/occlusion, height 256, dimensions, LAN co-op |
| **M10** | Post-1.0 | ongoing | 1.0 | Data-driven content, deeper NPC economy, more non-cube families |

Estimated 1.0: **9–12 weeks solo + AI**, faster at the pace this project has
moved.

---

## 5. Detailed milestones

### M5 — Foundation & guardrails (1–1.5 weeks)

**5.1 Content capacity**

- `BlockDb.cs`: raise `BlockCount` 32 → 128; `AtlasCols` 8 → 16 (256 tiles);
  update UV/tile-array math.
- Add a startup **content validator**: every `BlockId` has a definition and
  valid tile; every `ItemId` has a name/icon; every recipe ingredient/result
  exists; block↔item pairs round-trip.
- Document the append-only ID policy at the top of `BlockId.cs` / `ItemId.cs`.
- *Why:* only 3 spare block IDs and 31 spare tiles today. M6 needs far more.

**5.2 Headless test harness**

- Add `tests/` with a `TestRunner.tscn` runnable via
  `godot --headless --path .`; exit non-zero on failure. No third-party addon
  required.
- Suites: content registry, inventory add/stack/overflow, recipe
  matching/crafting, player stats damage/hunger/drown, save atomic
  write/backup/corruption, worldgen anchors, mesher greedy/per-face area
  parity.
- Add `scripts/verify.sh` = build + tests + anchor check.

**5.3 Save v3**

- Make `WorldSave.Version` actually checked; add a migration chain
  (v1→v2→v3).
- Add `Containers` (per-block inventories) and `Flags`/`Achievements` fields,
  all optional.
- Add armor slots to `PlayerSave` (optional, old saves default empty).
- Tests: old save loads; corrupt file recovers from `.bak`; round-trip stable.

**5.4 Performance baseline**

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
recorded.

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
  anchors, mesher parity.
- Headless smoke: boot → create world → place/break → save → reload → assert.
- Optional GitHub Actions: `dotnet build` + headless tests (Godot mono download
  is heavy; local `verify.sh` is the minimum).

**Golden anchors**

- v1 chunk `(7,7)` SHA-256
  `c7fe5ea262d362606196e2fffcdc65dd130eb2df1fc58ab051fb54d3623a352a`
  unchanged.
- Greedy vs per-face surface-area parity per normal.
- `player.json` save→load→save stable.
- New gen versions never alter old ones.

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
| Content capacity/atlas overflow | M5 validator + 16×16 atlas before adding blocks |
| Torch lighting looks flat or costs FPS | Ship pooled-light v1; only build voxel propagation with measured justification |
| Chest/furnace save bloat | Save only non-empty containers; cap; versioned migration |
| Localization touches every code-built UI string | Central `Tr()` first, extract incrementally; prototype Arabic/RTL early |
| Music licensing | Original/CC0 only; license file in repo |
| Scope creep (villages, doors, multiplayer) | Explicit 1.0 cut line; defer to M9/M10 |
| No tests means silent save/worldgen breakage | M5 harness is a hard prerequisite, not optional |
| Arabic font/RTL issues | Bundle a known Arabic font; test shaping and layout on one screen before full extraction |

---

## 9. First five days (actionable)

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
- **Hardware validation pending:** no MX230 visual/performance soak was run in
  CI; verify chunk-streaming frame times and torch-heavy caves on that GPU.


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
## 21. Soil, grass and farmland transitions — 2026-11-21

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
