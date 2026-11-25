# Minecraft-like

[![CI](https://github.com/firasotoom85-droid/minecraft-godot/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/firasotoom85-droid/minecraft-godot/actions/workflows/ci.yml)
[![Documentation](https://github.com/firasotoom85-droid/minecraft-godot/actions/workflows/docs.yml/badge.svg?branch=main)](https://github.com/firasotoom85-droid/minecraft-godot/actions/workflows/docs.yml)

An original, cozy-leaning voxel survival sandbox built with **Godot 4.7.2 .NET** and **C#**. Explore a streamed procedural world, gather and craft, build, survive the night, discover villages and dungeons, and make a place of your own.

The project uses original procedural materials and audio. It is Minecraft-inspired, independent, and not affiliated with or endorsed by Mojang or Microsoft.

> **Project status:** active development. The latest published build is [v1.1.0-beta.1](https://github.com/firasotoom85-droid/minecraft-godot/releases/tag/v1.1.0-beta.1). The default branch may contain changes newer than that release.

## Screenshots

<p align="center">
  <img src="docs/visual/screenshots/atmosphere-river-valley-noon-clear.png" alt="Procedural river valley at noon" width="32%">
  <img src="docs/visual/screenshots/lighting-torch-cave.png" alt="Torch-lit underground cave" width="32%">
  <img src="docs/visual/screenshots/lighting-village-street.png" alt="Village street at dusk" width="32%">
</p>

## Highlights

- Infinite, seeded chunk streaming with versioned world generation, multiple biomes, rivers, caves, structures and distant terrain LOD.
- Greedy opaque meshing, a per-face comparison path, pooled mesh payloads and bounded streaming memory.
- Survival systems for health, hunger, equipment, crafting, cooking, farming, fishing, fluids, boats and storage.
- Passive herds, hostile creatures, village residents, trading, quests, dungeons and the Ember Warden encounter.
- Baked voxel lighting, day/night, weather, underwater fog blending and six graphics-quality presets.
- Multi-world saves with atomic writes, rotating backups, import/export and compatibility for older worlds.
- Keyboard and controller rebinding, minimap, achievements journal, contextual onboarding, photo mode and a developer console.

Autosaving captures an immutable save snapshot on the main thread; serialization and the three ordered atomic file writes run on the background save worker, so `_Process()` does not serialize or perform disk I/O. Explicit save and quit flush older work before writing the latest state synchronously.

See the [documentation index](docs/README.md) for subsystem details, test procedures, visual references and historical engineering reports.

## Persistence and recovery

World and player writes use sibling temporary files and rotating recovery candidates. Candidates are structurally validated before loading; a malformed primary falls back to the next valid backup, while unsupported future versions are never downgraded to an older save. The optional manifest `hasSavedData` marker records that a complete save pair has existed. If a previously saved world loses every primary and backup, or its metadata is unreadable, it is retained for recovery instead of being silently initialized or redirected to another save; ordinary writes are blocked while recovery is required. Older manifests without playtime or save-history fields load with legacy defaults.

World imports are extracted to a staging directory, checked against the archive's SHA-256 file index, and only then moved into the worlds folder. Existing worlds are not overwritten; duplicate names receive a suffix. Unsafe paths, links, unsupported save/generator versions, structurally invalid records, corrupt data without a valid backup, and excessive expanded size are rejected. World duplication and deletion do not traverse symbolic links, junctions or other filesystem reparse points; failed copies are discarded instead of being published.

## Requirements

- **Godot 4.7.2 .NET / Mono editor**. The standard non-.NET editor cannot build this C# project.
- **.NET 10 SDK**. The repository pins the SDK feature band in global.json.
- A desktop OS supported by Godot. Vulkan is recommended; the game has quality presets for lower-end hardware.

The project has moved from .NET 9, which reached end of support on November 10, 2026, to .NET 10 LTS. See [setup and toolchain notes](docs/development/SETUP.md).

## Build and run

1. Clone the repository and open its folder with the Godot 4.7.2 .NET editor.
2. Import project resources from the editor, or run:

       godot --headless --editor --path . --import

3. Build the C# project:

       dotnet build -c Debug

4. Run the game:

       godot --path .

Use the Godot executable that matches your installation. On systems where it is not on PATH, pass its full path to the scripts below.

## Test

Run the full headless verification suite:

    GODOT=/path/to/Godot_v4.7.2-stable_mono ./scripts/verify.sh

The script builds Debug and runs the registered suites through the real Godot headless test runner. CI additionally builds Debug and Release with warnings treated as errors and captures the deterministic lighting probe scenarios.

The opt-in benchmark and soak reports separate exact retained array lengths from capacity/key-based estimates for persistent caches, registries and streaming queues. These managed-memory diagnostics are not whole-process or VRAM totals; see the [performance benchmark procedure](docs/PERFORMANCE_BENCHMARKS.md) for the accounting boundaries.

For the longer real-scene survival soak:

    GODOT=/path/to/Godot_v4.7.2-stable_mono bash scripts/soak-survival.sh 3600

See [testing and validation](docs/development/TESTING.md) before changing test infrastructure or interpreting a benchmark.

## Default controls

| Input | Action |
|---|---|
| WASD | Move |
| Mouse | Look |
| Left mouse | Mine / attack |
| Right mouse | Place / use |
| Middle mouse | Pick block |
| Space / Shift | Jump / swim up / sprint / dive |
| 1–9 / mouse wheel | Select hotbar slot |
| E / Esc | Inventory / pause |
| M / [ / ] | Minimap / zoom |
| P / J / H | Photo mode / achievements journal / help |
| F3 / F6 | Information panel / quality preset |
| F7 / F8 / F9 | View distance down / up / mute |
| / | Developer console |

Keyboard and controller bindings can be changed in **Esc → Controls**.

## Repository layout

| Path | Purpose |
|---|---|
| scripts/ | C# gameplay, world, streaming, rendering coordination and persistence systems |
| scenes/ | Main menu, gameplay and reusable scene resources |
| shaders/ | Custom voxel surface shaders |
| tests/ | Headless suites and deterministic visual-capture scenes |
| docs/ | Architecture, development guides, benchmarks, audits and visual references |
| addons/ | Editor and development add-ons, each retaining its own license |

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md), the [architecture guide](docs/architecture/ARCHITECTURE.md) and the [current roadmap](ROADMAP.md) before making changes. Compatibility rules for persisted IDs, save data and world generation are mandatory.

## Project documents

- [Documentation index](docs/README.md)
- [Architecture](docs/architecture/ARCHITECTURE.md)
- [Data and lifecycle invariants](docs/architecture/DATA_LIFECYCLE.md)
- [Development setup](docs/development/SETUP.md)
- [Testing](docs/development/TESTING.md)
- [Code style](docs/development/CODE_STYLE.md)
- [Adding content safely](docs/development/ADDING_CONTENT.md)
- [Release process](docs/development/RELEASING.md)
- [Roadmap](ROADMAP.md)
- [Project decisions](PLAN.md)
- [Changelog](CHANGELOG.md)
- [Security policy](SECURITY.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## License

## Gameplay lifecycle and navigation regression fixes — 2026-11-23

Crop growth now validates its farmland support and open space using loaded-only
voxel reads. Wheat without farmland remains planted but does not advance; it
resumes when valid support is restored. An unloaded crop coordinate never
causes its terrain chunk to be generated just to process a growth tick.

The Merchant's three-biome exploration quest initializes from the persistent
unique `explored/<Biome>` world flags, so previously discovered biomes count
when the quest is accepted. Revisiting a biome does not add progress twice.

Mob simulation is suspended when an entity's current terrain chunk is absent
and resumes when that chunk becomes available again. This includes residents
managed by the village system. The village removal pass reuses its scratch list,
and mob projectiles reuse a configured terrain `RayCast3D` instead of building
a ray-query object and result dictionary every physics tick.

Fluid updates preserve level 1 at the foot of a falling column, avoid weakening
flow when a neighbouring chunk is unknown, and periodically rediscover water
in newly streamed nearby chunks. The retry examines loaded cells only. Gravity
mob steering treats non-solid plants as traversable while retaining explicit
lava avoidance; Crystal Wisp steering accounts for the occupied microcells of
slabs and stairs.

All block changes continue through `VoxelWorld.SetBlock`. These fixes do not
change persisted IDs, save format, generator versions, the mesher, or chunk
streaming architecture. Regression cases live in the existing
`MobPerceptionTests`, `VillageTests`, `FluidWorldTests`, and
`MobContentTests` suites.

## Chunk worker cancellation and pooled payload ownership — 2026-11-24

World-shutdown cancellation now propagates through chunk generation, chunk and
lighting snapshots, sky/block-light propagation, greedy and per-face meshing,
and Far-LOD tile generation. The existing worker pool, chunk queues, greedy
mesher, Far-LOD architecture and worker/main-thread publication boundary remain
in place.

Meshing workers retain ownership of rented render and collision buffers until
a complete `MeshPayload` is constructed. Exceptions and cancellation before
that transfer return every buffer already rented; payload release is
idempotent. Shutdown cancellation is distinct from a recoverable Far-LOD build
failure and does not schedule retries during teardown.

Regression coverage is in `VoxelStreamingTests`,
`FarLodLifecycleTests` and `GlassTests`. Verification on 2026-11-25 ran Debug
and Release warnings-as-errors builds with zero warnings, 201,710 Godot
headless checks with zero failures, and five spatial lighting probe grids with
27/27 reference samples each and zero measured drift. This W13.1 slice does not
change global `EditEpoch` validation, save formats, persisted IDs or
world-generation versions.

## Dependency-local revisions and staged chunk application — 2026-11-25

Chunk mesh payloads now carry a revision for the target chunk's actual
sampling dependencies. Geometry-only edits invalidate the owner and only the
sampled edge/corner neighbors. Light-affecting edits advance source revisions
for the light solve and invalidate the 5×5 target area needed by the 3×3 baked
light-map neighborhood and its 14-voxel propagation halo. The world-wide
`EditEpoch` remains diagnostic; it no longer rejects unrelated mesh payloads.

Main-thread chunk application is a transaction. Render surfaces and concave
collision shapes are prepared in triangle-aligned batches of at most 8,190
vertices. The existing chunk remains visible and its old collider remains
active until all replacement resources are ready, then one commit installs
the new surfaces and collider set. The completed-payload semaphore reservation
stays owned by the staged transaction and is released exactly once on commit,
stale rejection, exception or shutdown.

Performance reports now include rolling p50/p95/p99 timings for resource
steps and collision batches. The nominal 2 ms budget is checked between steps;
a single Godot resource-creation call can still exceed that wall-clock target.
Use the benchmark output on the MX230 reference and higher-end hardware before
tuning the batch size or declaring a frame-time improvement.

Regression coverage includes interior, boundary and corner edits; the
two-chunk lighting halo; preservation of the old chunk before transaction
commit; multi-batch render/collision payloads; and timing-window statistics.
This W13.2 work does not change persisted IDs, save formats, generator
versions, greedy meshing or the WorkerThreadPool/chunk-streaming architecture.

Project code and documentation are licensed under the [MIT License](LICENSE). Bundled third-party components and assets retain their own terms; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
