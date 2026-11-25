# Roadmap

This is the **current forward plan**, not a running implementation diary. Completed work, integration records and historical audits are preserved under docs/history and the relevant subsystem folders.

**Status date:** 2026-11-25  
**Current engine baseline:** Godot 4.7.2 .NET  
**Current framework target:** .NET 10  
**Latest published build:** [v1.1.0-beta.1](https://github.com/firasotoom85-droid/minecraft-godot/releases/tag/v1.1.0-beta.1)

## 1. Repository and release readiness

| Area | State | Next action |
|---|---|---|
| Documentation and repository standards | In progress | Merge this repository-hygiene work; keep active docs short and move completed reports to history |
| W11 persistence and recovery hardening | Integrated | Verified locally on 2026-11-25 (201,755 headless checks, 0 failures) including generation pairing, backup rotation/promotion, archive validation, deletion quarantine and dirty-state autosave. Hosted CI and a human save/recovery playtest remain |
| Gameplay, inventory and progression integrity (W14) | Integrated | Verified locally on 2026-11-25 (202,687 headless checks, 0 failures) for hostile limits, furnace fuel, inventory overflow, recipe transactions and generator V13. Hosted CI and a human playtest remain |
| Build and test baseline | Established | Keep Debug/Release warnings-as-errors, registered headless suites and deterministic spatial-light probes green on every PR |
| Worker cancellation and pooled payload ownership | Integrated (W13.1) | Keep bounded shutdown checkpoints and single-owner pooled-buffer release covered by CI |
| Dependency-local revisions and staged chunk application | Integrated (W13.2) | MX230 measured 2026-11-25: frame p50/p95 unchanged, max draw calls 317→560, resource-step p95 9.18 ms; higher-end GPU comparison remains |
| .NET lifecycle | Migration in this change | Verify .NET 10 through CI and the Godot 4.7.2 export path before treating it as released |
| Release packaging | Being automated | Exercise tag-triggered Linux/Windows export packaging on a beta tag |
| Performance baselines | Partial | Benchmark/soak reports now expose persistent managed-memory categories and high-water marks; record a fresh controlled Release baseline, with the comparable RTX 4060 run still outstanding |
| Terrain coverage and startup streaming | In implementation | Validate per-chunk Far-LOD handoff, the 3x3 startup-ready gate, repeated-UV mip stability and view-distance traversal on LOW and HIGH presets |
| Gameplay/visual sign-off | Partial | Complete human playtesting across mining, building, combat, quests, saves and low/high graphics presets |
| Player lifecycle and safe recovery (W12) | Integrated | Verified locally on 2026-11-25 (202,772 headless checks, 0 failures); keep the destination resolver authoritative; capture controlled Release performance baselines on the MX230 reference and a higher-end GPU before release sign-off |
| UI, controls, audio and graphics (W15) | Integrated (code); visual validation pending | `202,834` checks, 0 failures on main; complete the minimap/photo/glass captures and UI/audio playtest in `docs/visual/W15_VISUAL_VALIDATION.md` |
| Public project profile | Incomplete | Repository is already public; add a description and relevant topics in GitHub Settings |
| W15 UI, controls, audio and graphics | Integrated (code); visual validation pending | Capture minimap, photo-mode and intra-chunk glass scenarios before closing the milestone |

**Persistence contract (integrated W01):** autosave captures an immutable save
snapshot on the main thread; one ordered background save worker serializes it
and performs the three atomic file writes, so `_Process()` performs no
serialization or disk I/O. Explicit save and quit flush older work before
writing the latest state synchronously.

**W11 persistence integrity (implementation in progress):** world and player
save records share a monotonically increasing generation, and the manifest is
the commit marker written after both files. Readers select only a matching
pair across the primary and three rotating backup tiers; a mismatched or
incomplete pair is never silently combined. Recovered pairs are promoted to
the primary files before normal play continues. Backup restores and archive
imports validate complete generation-matched snapshots. User-requested world
deletion atomically moves the whole directory into `user://worlds/.trash` and
retains it as a recovery copy; permanent cleanup remains available for
disposable internal/test worlds.

**Persistence recovery (integrated W05):** world and player manifests and saves use model-specific validation with ordered backup fallback. A damaged selected world is retained in recovery rather than silently replaced, and ordinary writes are blocked until a valid world/player pair is restored. The optional `hasSavedData` manifest marker distinguishes a never-saved world from lost save history. Archive imports validate metadata and candidates; duplication and deletion do not traverse links or other reparse points.

**Gameplay lifecycle guard (integrated W08):** crop growth validates loaded-only farmland and open space, pausing without generating terrain. Merchant biome progress reconciles unique `explored/<Biome>` flags. Mob and village simulation suspend when their terrain chunk is unavailable. Water columns retain level 1 at their foot, and unknown neighbours cannot weaken flow. Mob steering respects plants, lava and stateful slab/stair occupancy.

**Player destination contract (W12, implemented and CI-verified):** startup, saved-position recovery, respawn, void recovery, console teleports and boat dismounts resolve through a shared player destination transaction. It validates support and capsule/hazard clearance, prepares the destination chunk's collision payload at the current world edit epoch, and only then commits the transform. Nearby occupied-geometry edits synchronously refresh the affected chunk collider; distant edits retain the bounded worker rebuild path. The local Godot 4.7.2 .NET verification passes Debug and Release with warnings as errors, all 202,772 W12-era headless assertions, and the five spatial-light probe scenarios. This verifies correctness and regression coverage, not a hardware performance benchmark: controlled Release measurements on the MX230 reference and a higher-end GPU remain part of release sign-off.

**Chunk worker cancellation and payload ownership (integrated W13.1):** world-shutdown cancellation is propagated through chunk generation, snapshot preparation, voxel lighting, greedy/per-face meshing and Far-LOD tile builds. `Mesher.BuildCore()` owns each rented render/collision buffer until a completed `MeshPayload` takes ownership; failure/cancellation returns partial rents, and payload release is idempotent. Far-LOD shutdown cancellation is not treated as a retryable tile failure. Local verification on 2026-11-25 passed Debug/Release warnings-as-errors builds, 201,710 headless checks and five 27/27 spatial lighting probe grids with zero measured drift. W13.1 leaves dependency validation and main-thread payload application to the separate W13.2 slice.

**Terrain coverage and startup streaming (current implementation slice):** Far LOD is suppressed only for chunks whose detailed render payload has actually committed, using an 8×8 coverage mask per 128 m tile. Readiness changes are coalesced once per world frame; the existing worker pool, staged application budget, chunk queues and generator versions remain authoritative. Startup displays progress for the nearby 3×3 chunk area and releases control when it is ready (or after a 12-second non-blocking fallback); the rest of the configured view distance continues streaming. Greedy texture-array sampling uses continuous pre-fract UV gradients to avoid incorrect mip selection at repeated quad boundaries. Run the full Godot 4.7.2 Debug/Release and headless verification workflows, then inspect captures and traversal on LOW and HIGH before marking this slice complete.

**Dependency-local revisions and staged application (integrated W13.2):** each mesh payload captures a target-local revision for the block and lighting data it samples; geometry edits retain edge/corner granularity, while light-affecting edits invalidate the 5×5 target region required by neighboring 3×3 light maps and their 14-voxel propagation halo. Light solves validate only the 5×5 source revisions. The monotonic world `EditEpoch` remains for diagnostics and is no longer the unrelated-payload rejection gate. Main-thread resource creation advances in triangle-aligned slices capped at 8,190 vertices; an existing chunk is not replaced until all mesh and collision resources are prepared. The 2 ms budget is checked between steps, not guaranteed as a hard cap on an individual Godot call. Performance reports include rolling apply-step and collision p50/p95/p99 metrics; controlled MX230 and higher-end runs remain the tuning gate. Dependency revision storage is capped at 16,384 entries; crossing the cap advances a revision floor, so in-flight payloads and light solves are rebuilt instead of applied after revisions are pruned.

**Gameplay state integrity (W14):** hostile population limits are enforced at the common spawn boundary; every herd spawn checks loaded terrain and capsule clearance; damage, breeding, furnace and inventory operations validate inputs before consuming state. Recipe-book outputs are preflighted against post-consumption capacity, achievement unlocks update an open journal, trader sessions expire with their residents, and particle bursts use bounded emitter pools. Generated village wheat is registered through the existing chunk-scan path. Campfire/structure exclusion is generator V13-only so V1–V12 remain frozen. Verified locally on 2026-11-25 by the registered headless suite (202,687 checks, zero failures); existing worlds keep their manifest generator version.

**W15 implementation (integrated; in-game visual verification pending):** gameplay deadlines now use a pause-excluding monotonic clock, while UI/gesture timing remains real-time. Keyboard and controller overrides are resolved as complete sets; controller-only block interactions are exposed in Controls. Inventory actions participate in focus navigation, onboarding records a successful inventory-open event, and minimap work tracks yaw, nearby block edits and interrupted builds. F3 uses the shared voxel target; photo mode resamples the current FOV and Far LOD range; automatic quality changes no longer persist over the manual ceiling. Long world names and metadata use separate wrapped controls. Mining completion suppresses the periodic sound on its final tick, and appended plant/furnace material sound mappings have regression coverage.

**W15 music decision:** authored music remains deferred. The nonfunctional Music slider and automatically created unused Music bus have been removed; the legacy `MusicVolume` setting is still read and written for settings-file compatibility.

**W15 visual hold:** issue #137 remains an investigation. Chunk-level glass batching and the existing greedy mesher are unchanged. Do not split panes into extra mesh instances without a reproducible capture showing an intra-chunk ordering defect. Debug/Release and the full headless suite now pass on main; the minimap, photo-mode and glass captures remain required before marking W15 complete.

## 2. Stable-release quality gates

A release candidate is ready only when all of the following are true:

- Debug and Release builds complete with zero warnings and zero errors.
- The complete registered headless test suite passes.
- Existing world saves load, save and reload without loss; historical generator versions remain byte-identical.
- Linux and Windows exports are produced from the same tagged commit and have published SHA-256 checksums.
- Linux startup receives a smoke test; Windows receives a native launch test before the Windows build is called fully verified.
- A human playtest covers a new world from first launch through progression, construction, combat, quests, death/respawn and world export/import.
- Performance results are measured on the MX230 reference and a higher-end GPU; estimated render cost is not substituted for frame-time measurements.
- No known critical or high-severity defect blocks ordinary play, saves, world loading or release installation.
- The changelog, release notes, third-party notices and known limitations match the shipped build.

The current implementation still needs human gameplay/visual sign-off. Arabic/RTL localization remains unimplemented and must be decided explicitly for the first stable release. Authored music is deferred as recorded in W15; the Settings UI does not expose a nonfunctional music control.

## 3. Post-1.0 direction

These are intentionally not prerequisites for the first stable single-player release:

1. **Scale and online:** optional LAN co-op, dedicated-server experiments, reconnect behavior and multiplayer-safe world ownership.
2. **World expansion:** additional dimensions and a larger world-height format after save/worldgen migration design.
3. **Extensibility:** a data-driven content/mod API and additional non-cube families beyond the existing slabs and stairs.

## 4. Compatibility rules

These rules apply to every milestone:

- Never reorder or reuse persisted BlockId or ItemId values; append new values only.
- World-generation changes require a new generator version. Existing generator versions must remain byte-identical.
- Route voxel changes through the existing VoxelWorld.SetBlock / ApplyEdit mutation path.
- Preserve save compatibility and test both current and legacy save forms.
- Preserve greedy meshing, chunk streaming, worker cancellation and payload-release ownership.
- Do not touch Godot scene-tree or physics resources from worker threads.
- Keep performance claims tied to repeatable measurements and record the hardware, renderer preset, seed, view distance and capture route.

See [PLAN.md](PLAN.md) for the locked product/architecture decisions and [the architecture guide](docs/architecture/ARCHITECTURE.md) for subsystem ownership.
