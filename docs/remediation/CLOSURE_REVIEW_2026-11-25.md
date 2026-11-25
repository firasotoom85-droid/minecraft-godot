# Issue closure review — 2026-11-25

Read-only review of the 101 issues open on GitHub after the W12/W15 integration.
No issue state was changed. Closure still requires hosted CI and the issue
owner's confirmation; this record only identifies where the current tree already
implements the requested behaviour.

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

## Still open on `main`

- **#137** — intra-chunk glass sorting remains an investigation requiring rendered before/after captures.
- **#141** — only near-player mutations refresh collision synchronously; distant edits still wait for the async rebuild.
- **#148** — the staged transaction still exceeds the 2 ms cooperative step budget on the MX230 (p95 9.18 ms); a smaller batch would trade draw calls for latency.
- **#200** — incremental block-light updates still materialise state volumes per emitter edit.

## Newer remediation issues (#217–#282)

These correspond to the landed W11/W12/W14/W15 work and are covered by the
202,834-check suite, but they were not closed individually. W15's visual issues
still need the captures in `docs/visual/W15_VISUAL_VALIDATION.md`, and #148
remains open because the MX230 p95 exceeds the cooperative budget. Do not close
the group from this review alone.

## Hosted verification and closure

`main` at `bd7c36c` passed hosted CI run 45610165731 (Debug/Release builds,
headless suite, spatial-light probes). The issues marked verified above were
closed with a link to this review; #137, #141, #148 and #200 remain open because
they need rendered captures, design changes or further performance work.
