# Issue closure review — 2026-11-25

Review of the 101 issues that were open on GitHub after the W12/W15 integration.
After hosted CI passed, 32 issues with the evidence below were closed with a
link to this record. A second pass verified and closed the remaining 69 on
`68b1ddf`; **0 issues remain open**. #148 retains a documented residual apply
latency, and #137 was an investigation without a deterministic reproduction.

## Verified as implemented on current `main`

| Issue | Evidence |
|---|---|
| #96, #176 | Target-local mesh dependency revisions replace global `EditEpoch` payload rejection; interior-edit and distant-payload cases are covered in `VoxelStreamingTests` (`TestChunkDependencyRevisions`). |
| #100 | Generator V13 rejects campfire cells within the structure exclusion radius; `WorldgenTests` covers conflicting and non-conflicting sites. |
| #108 | `VoxelWorld.SetBlock` rejects undefined `BlockId` values before any mutation; `VoxelStreamingTests` asserts the mutation guard. |
| #110 | Future world-save versions return `WorldReadStatus.Unsupported` without falling back; `SaveTests` covers it. |
| #123 | `VoxelWorld` subscribes to `PlayerStats.Changed` and marks the world dirty, so stationary survival changes enter autosave. |
| #124 | `Player.TrySetRespawnAnchor` calls `World.MarkDirty()` after a validated anchor change. |
| #127 | `ChunkData` registers generated and cached wheat through `RegisterCropSourcesLocked`, so village crops enter `_crops`. |
| #94 | Far-LOD payload application requires a matching generation, revision and streamed-area skip mask (`FarLodLifecycleRules`; `FarLodLifecycleTests`). |
| #95 | `ChunkWorkTracker.TryBegin` rejects a second claim for an in-flight coordinate; `VoxelStreamingTests` asserts the duplicate-claim refusal. |
| #149 | `SetupInitial` builds only the focal chunk synchronously; the surrounding ring enters the bounded worker pipeline. |
| #151 | Edit-top reconstruction reads through `ChunkData`, so the first later stream reuses the generated cache instead of regenerating terrain. |
| #194 | Far-LOD exclusion uses `FarLodLifecycleRules.StreamedChunks(playerChunk, ViewDistance, UnloadMargin)`, matching the chunk retention boundary; `FarLodLifecycleTests` covers it. |
| #97 | Worker exceptions use bounded retry handling and publish no payload (`VoxelStreamingTests`). |
| #98, #99, #195 | `ResolveWorld` rejects unsupported generator versions, refuses data-bearing worlds without a readable manifest, and refuses incomplete noise metadata without deriving replacements. |
| #101 | Concurrent chunk generation is deduplicated through `ChunkGenerationReservations.GetOrCreate`. |
| #103 | Generator V12 rejects coordinates outside its supported horizontal range. |
| #111 | Unknown byte-backed block IDs remain loadable and are preserved as opaque edits (`SaveTests` future-block case). |
| #129 | Autosave runs when playtime reaches the interval even without a gameplay dirty flag, and playtime is persisted in the manifest. |
| #145 | Campfire, structure and village site caches are evicted by `PruneWorldCaches`. |
| #182 | Appended block IDs have explicit break-sound family coverage in `KeybindTests`. |
| #196, #197, #198 | Queue reconciliation drops rebuild requests outside retention, refreshes fresh priorities, and bounds retry history (`VoxelStreamingTests`). |
| #147, #202 | Far-LOD failure/in-flight completion uses `FarLodLifecycleRules`; `FarLodLifecycleTests` covers failure, cancellation and identity-safe release. |
| #189 | `FindSpawn` expands the deterministic search and throws when no candidate is safe; the hard-coded fallback is gone. |
| #199, #201 | Shutdown cancellation is distinct from retryable worker failure and releases reservations; `VoxelStreamingTests` covers the cancellation paths. |
| #254 | Saved keyboard remaps ignore engine `ui_*` actions and restore order-independently (`3d752a2`, `KeybindTests`). |

## Second-pass closure notes

- **#137** — investigation only: no deterministic intra-chunk glass failure was reproduced; per-chunk AABB sorting is retained and pane-per-node splitting would violate the draw-call budget.
- **#141** — player-adjacent edits rebuild the edited chunk collider synchronously before the next physics step; distant edits remain on the bounded asynchronous pipeline by design.
- **#148** — apply steps are capped at 2,046 vertices. MX230 measurement: resource-step p95 12.86→3.98 ms, p99 18.19→10.98 ms, FPS unchanged, draw calls 1,551. The residual above 2 ms is one non-interruptible `ArrayMesh` call.
- **#200** — `regionStates` now uses the shared `ArrayPool` and is cleared/returned on every path.

## Newer remediation issues (#217–#282)

These correspond to the landed W11/W12/W14/W15 work and are covered by the
202,834-check suite. All were individually verified and closed in the second
pass. W15's rendered captures remain a separate manual validation step in
`docs/visual/W15_VISUAL_VALIDATION.md`; closing a code issue does not substitute
for that capture review.

## Hosted verification and closure

`main` at `68b1ddf` passed hosted CI run 45618814135 (Debug/Release builds,
202,834 headless checks and spatial-light probes). All 69 remaining issues were
closed with issue-specific evidence comments. The tracker now has 0 open issues.
