# Roadmap

This is the **current forward plan**, not a running implementation diary. Completed work, integration records and historical audits are preserved under docs/history and the relevant subsystem folders.

**Status date:** 2026-11-24  
**Current engine baseline:** Godot 4.7.2 .NET  
**Current framework target:** .NET 10  
**Latest published build:** [v1.1.0-beta.1](https://github.com/firasotoom85-droid/minecraft-godot/releases/tag/v1.1.0-beta.1)

## 1. Repository and release readiness

| Area | State | Next action |
|---|---|---|
| Documentation and repository standards | In progress | Merge this repository-hygiene work; keep active docs short and move completed reports to history |
| Build and test baseline | Established | Keep Debug/Release warnings-as-errors, registered headless suites and deterministic spatial-light probes green on every PR |
| .NET lifecycle | Migration in this change | Verify .NET 10 through CI and the Godot 4.7.2 export path before treating it as released |
| Release packaging | Being automated | Exercise tag-triggered Linux/Windows export packaging on a beta tag |
| Performance baselines | Partial | Benchmark/soak reports now expose persistent managed-memory categories and high-water marks; record a fresh controlled Release baseline, with the comparable RTX 4060 run still outstanding |
| Gameplay/visual sign-off | Partial | Complete human playtesting across mining, building, combat, quests, saves and low/high graphics presets |
| Public project profile | Incomplete | Repository is already public; add a description and relevant topics in GitHub Settings |
| W15 UI, controls, audio and graphics | In progress | Complete Debug/Release and headless verification; capture minimap, photo-mode and intra-chunk glass scenarios before closing the milestone |

**Persistence contract (integrated W01):** autosave captures an immutable save
snapshot on the main thread; one ordered background save worker serializes it
and performs the three atomic file writes, so `_Process()` performs no
serialization or disk I/O. Explicit save and quit flush older work before
writing the latest state synchronously.

**Persistence recovery (integrated W05):** world and player manifests and saves use model-specific validation with ordered backup fallback. A damaged selected world is retained in recovery rather than silently replaced, and ordinary writes are blocked until a valid world/player pair is restored. The optional `hasSavedData` manifest marker distinguishes a never-saved world from lost save history. Archive imports validate metadata and candidates; duplication and deletion do not traverse links or other reparse points.

**Gameplay lifecycle guard (integrated W08):** crop growth validates loaded-only farmland and open space, pausing without generating terrain. Merchant biome progress reconciles unique `explored/<Biome>` flags. Mob and village simulation suspend when their terrain chunk is unavailable; projectile queries reuse a configured terrain ray cast. Water columns retain level 1 at their foot, and unknown neighbours cannot weaken flow. Mob steering respects plants, lava and stateful slab/stair occupancy.

**W15 implementation (in progress; runtime verification pending):** gameplay deadlines now use a pause-excluding monotonic clock, while UI/gesture timing remains real-time. Keyboard and controller overrides are resolved as complete sets; controller-only block interactions are exposed in Controls. Inventory actions participate in focus navigation, onboarding records a successful inventory-open event, and minimap work tracks yaw, nearby block edits and interrupted builds. F3 uses the shared voxel target; photo mode resamples the current FOV and Far LOD range; automatic quality changes no longer persist over the manual ceiling. Long world names and metadata use separate wrapped controls. Mining completion suppresses the periodic sound on its final tick, and appended plant/furnace material sound mappings have regression coverage.

**W15 music decision:** authored music remains deferred. The nonfunctional Music slider and automatically created unused Music bus have been removed; the legacy `MusicVolume` setting is still read and written for settings-file compatibility.

**W15 visual hold:** issue #137 remains an investigation. Chunk-level glass batching and the existing greedy mesher are unchanged. Do not split panes into extra mesh instances without a reproducible capture showing an intra-chunk ordering defect. Minimap, photo-mode and glass captures, plus Debug/Release and the full headless suite, remain required before marking W15 complete.

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

The current implementation still needs human gameplay/visual sign-off. Arabic/RTL localization and authored music are also not implemented in the current roadmap; decide explicitly whether each is required for the first stable release rather than silently implying it is complete.

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
