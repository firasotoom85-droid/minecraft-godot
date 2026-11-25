# Data lifecycle and invariants

## World creation and loading

1. GameSession loads settings, key bindings, controller mappings and audio bus configuration, then runs content validation and legacy-save migration.
2. WorldStore creates or reads a per-world manifest containing the seed, world-generation version and world preset.
3. Main resolves the selected world and restores optional player state, inventory and world records.
4. VoxelWorld resolves the manifest and uses its fixed generator version. Existing worlds do not silently adopt the newest generator.

The current world save format is V4. Optional records allow older saves to load with defaults. Treat scripts/Worlds.cs and the save regression suites as authoritative.

## Voxel mutation path

All gameplay block edits must pass through VoxelWorld.SetBlock or the existing ApplyEdit path. A mutation updates authoritative world/edit state, marks the world dirty and invalidates relevant derived data such as affected mesh and lighting caches. Callers must not mutate a Chunk.Blocks array, the edit dictionaries or light caches directly.

When a new mutation type is introduced, verify:
- Coordinates at positive and negative chunk boundaries.
- Block-shape normalization and persisted state, where applicable.
- Light/opacity/emitter cache invalidation.
- Neighbor chunks and diagonal seams where geometry or lighting depends on them.
- Save/reload behavior and old-world compatibility.

## Chunk generation and mesh jobs

Chunk data is deterministic for the world's seed and generator version. The generated base is combined with persisted edits before it is presented to the chunk mesher.

Mesher.Build captures block and shape snapshots under the world's snapshot lock, then records a revision for the target chunk's sampled dependencies. The world-wide `EditEpoch` remains a diagnostic counter; it is not used to reject unrelated mesh payloads. Geometry edits invalidate the target and only sampled edge/corner neighbors. Light-affecting edits update per-source-chunk light revisions, invalidate target chunks through a 5×5 area (the 3×3 baked-light neighborhood plus each map's 14-voxel propagation halo), and cause light solves to validate their own 5×5 source region.

Array and geometry work runs off the scene tree on WorkerThreadPool jobs. The main thread validates the local dependency revision, meshing configuration, collider-boundary state and retention before staging, before each resource slice and immediately before commit. Mesh surfaces and collision shapes are created in triangle-aligned batches capped at 8,190 vertices per resource step. The current chunk remains visible and collidable until a complete replacement is ready, then one transaction commits it. The 2 ms target is cooperative between steps rather than a hard per-call time guarantee; performance reports retain rolling p50/p95/p99 resource-step and collision-step timings.

Every rented payload/surface/collision buffer has one release owner and must be returned even when a task is cancelled, rejected, throws or the world is exiting. The completed-payload slot remains reserved while an application transaction is staged so worker backpressure continues to reflect all pending main-thread work.

FarLod follows the same worker-build/main-thread-apply split. It is a coarse presentation of terrain, not a source of authoritative block mutations.

## Saving

WorldStore owns serialization, path validation, atomic writes and backup rotation. Do not add independent gameplay file writes. Keep the separation between world state and player state.

A persistence change should cover:
- Current-format round-trip.
- Loading previous supported format(s) with defaults.
- Atomic-write failure and backup recovery.
- Malformed or hostile paths/archives.
- World import/export and duplicate-name handling when the change affects portability.

## Generator and content compatibility

- Append BlockId and ItemId values only.
- Never change the meaning of an existing numeric ID.
- Any change to generation output requires a new generator version.
- Keep old generator versions byte-identical and pin changes with golden chunk hashes and positive/negative-coordinate tests.
- Any added asset must have documented source, creator and license. Do not use proprietary Minecraft textures, sounds or models.

See [ADDING_CONTENT.md](../development/ADDING_CONTENT.md) for the content-specific checklist.
