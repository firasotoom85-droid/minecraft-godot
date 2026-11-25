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
| #147, #202 | Far-LOD failure/in-flight completion uses `FarLodLifecycleRules`; `FarLodLifecycleTests` covers failure, cancellation and identity-safe release. |
| #189 | `FindSpawn` expands the deterministic search and throws when no candidate is safe; the hard-coded fallback is gone. |
| #199, #201 | Shutdown cancellation is distinct from retryable worker failure and releases reservations; `VoxelStreamingTests` covers the cancellation paths. |
| #254 | Saved keyboard remaps ignore engine `ui_*` actions and restore order-independently (`3d752a2`, `KeybindTests`). |

## Still open on `main`

- **Streaming/performance:** #94, #95, #97, #141, #145, #148, #149, #151, #194, #195, #196, #197, #198, #200. #148's staged transaction still exceeds the 2 ms step budget on the MX230 (p95 9.18 ms); #141 only covers near-player mutations synchronously.
- **World generation:** #98, #99, #101, #103.
- **Persistence/compatibility:** #111, #129.
- **Rendering investigation:** #137 (intra-chunk glass sorting; needs captures).
- **Audio:** #182 (needs issue-specific confirmation against the W15 material-sound mappings).

## Newer remediation issues (#217–#282)

These correspond to the landed W11/W12/W14/W15 work and are covered by the
202,834-check suite, but they were not closed individually. W15's visual issues
still need the captures in `docs/visual/W15_VISUAL_VALIDATION.md`, and #148
remains open because the MX230 p95 exceeds the cooperative budget. Do not close
the group from this review alone.
