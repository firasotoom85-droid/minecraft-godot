# Project decisions and engineering constraints

This file records the decisions that should remain stable across implementation work. It is not a progress log. The active delivery plan is [ROADMAP.md](ROADMAP.md); historical plans and completed implementation notes are archived under docs/history.

## Product direction

- **Identity:** a cozy-leaning, single-player voxel survival sandbox with danger that can be reduced or opted out through the existing game modes.
- **First stable release:** prioritize a complete, understandable single-player experience, save safety, accessible controls, stable exports and measured performance.
- **Multiplayer:** not a first-release requirement. Keep boundaries that make future replication possible, but do not build a second networking architecture prematurely.
- **Assets:** prefer original procedural content and original audio. Imported assets must have documented provenance and compatible licenses.

## Non-negotiable engineering constraints

1. **Persisted IDs are permanent.** BlockId and ItemId values are save data. Append new IDs; never reorder, reuse or silently reinterpret existing values.
2. **World generation is versioned.** Any generation-rule change gets a new generator version. Old versions must reproduce their original output.
3. **One voxel mutation path.** Use VoxelWorld.SetBlock / ApplyEdit. Do not write chunk arrays or edit caches from gameplay callers.
4. **Save compatibility is deliberate.** Add optional fields with defaults where possible; test old and new save round-trips and backups.
5. **Streaming owns world access.** Avoid new World.Player reads in streaming code; use the established player/observer boundary.
6. **Workers do not own the scene tree.** Build from safe snapshots, reject stale payloads, apply Godot mesh/physics resources on the main thread and release pooled payloads on every exit path.
7. **Input is action-based.** Gameplay reads InputMap actions, not hard-coded physical keys, so rebinding and controller support remain valid.
8. **Keep evidence honest.** Separate headless assertions, rendered captures, human playtests and FPS measurements. Never describe one as another.

## Current technical baseline

- Godot 4.7.2 .NET / C#.
- .NET 10 SDK and net10.0 target.
- Current world generator: V11; older versions remain frozen.
- World save format: V4 with optional fields for newer systems.
- Greedy opaque meshing, per-face comparison, far LOD and bounded chunk streaming remain supported.

For ownership and runtime data flow, read [ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md) and [DATA_LIFECYCLE.md](docs/architecture/DATA_LIFECYCLE.md).
