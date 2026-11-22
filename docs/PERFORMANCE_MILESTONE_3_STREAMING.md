# Performance Milestone 3 — bounded streaming and memory accounting

Date: 2026-11-21
Engine: Godot 4.7.2 .NET
Validation GPU: NVIDIA GeForce MX230
Branch: `perf/milestone-3-voxel-engine`
Parent: `perf/milestone-2-mx230` (PR #38)

## Scope

This increment reduces unbounded chunk-mesh payload buildup and makes
streaming memory/CPU costs observable. It retains the existing chunk queue,
worker-pool, greedy mesher, chunk nodes, collision path and `PayloadPool`.
There are no changes to persisted `BlockId`/`ItemId` values, save schema or
world-generation versions. V1–V8 generation behavior is unchanged.

## Implementation

- Workers reserve one of 18 payload slots before calling `Mesher.Build`. The
  bound covers active builds and payloads waiting for main-thread application.
- A cancellation token wakes workers waiting for capacity when the world exits.
  The world waits for the current WorkerThreadPool group, drains rebuild/fresh
  apply queues and returns unconsumed buffers to `PayloadPool` before disposing
  the semaphore and token source.
- Main-thread payload application keeps edit rebuilds ahead of new chunks and
  targets 2 ms per frame. At least one payload is always applied when available
  so a single expensive payload cannot prevent forward progress.
- `Profiler` tracks pending payload count/estimated bytes and high-water marks,
  voxel-cache byte-array memory and budget, edit/light metadata estimates,
  applied mesh source-array estimates, retained pool-buffer bytes, application
  timings and collision-build timings.
- `GetCacheMemorySnapshot` measures managed byte-array lengths exactly. The
  dictionary/object, applied mesh and pool values are estimates; the metrics do
  not represent exact GPU/driver allocations or a complete process-memory cap.
- The soak uses `WorldStore.CurrentGenVersion`, so it follows the current
  generator without hard-coding V8 or changing existing world generation.

## Verification

| Check | Result |
|---|---:|
| Debug build | Pass — 0 warnings, 0 errors |
| Release build | Pass — 0 warnings, 0 errors |
| `scripts/verify.sh` | 4,283 checks, 0 failures |
| Benchmark matrix | 54/54 runs passed; nine scenarios in Debug and Release |
| 30-minute survival soak | Pass — 1,800 s, exit code 0 |
| `git diff --check` | Pass |

### Thirty-minute soak measurements

The soak ran the actual main scene in Normal Survival, with view distance 2,
three meshing workers, no far-LOD ring and real chunk generation, colliders,
Jolt physics, mobs, weather, lighting and save handling active. It used a
private temporary world and was not run against the player's selected world.

| Metric | Result |
|---|---:|
| Passive / hostile creatures at finish | 10 / 27 |
| Successful feeds | 891 |
| Cave encounters | 24 |
| Baby observations | 16,199 |
| Noise stimuli / investigations | 360 / 244 |
| Non-finite creature positions | 0 |
| Peak process RSS | 304.1 MiB |
| Peak managed heap | 57.1 MiB |
| Peak Godot static memory | 104.4 MiB |
| Peak managed voxel-cache arrays | 4.2 MiB |
| Configured cache-array budget | 18.9 MiB |
| Peak retained mesh-pool buffers | 23.3 MiB |
| Payload high-water | 14 / 18 |
| Peak orphan nodes | 0 |
| Peak fluid cells / sources | 0 / 64 |
| Save/reload | Pass |
| Temporary world cleanup | Pass |
| Pending payloads drained on exit | Pass |

The cache budget applies to managed voxel byte arrays. Process RSS also includes
engine/runtime allocations and is not expected to equal the cache measurement.
The 23.3 MiB pool figure is retained CPU-side managed list capacity, not VRAM.

### Rendered MX230 check

Both captures used Godot 4.7.2 .NET, Vulkan Forward+ and the NVIDIA GeForce
MX230 selected as the active GPU:

- `performance/screenshots/mx230-streamed-cave.png` — the real survival soak
  scene, LOW preset / view distance 2, with streamed cave geometry, crosshair,
  hotbar, health/hunger and minimap visible.
- `performance/screenshots/mx230-surface-worldgen.png` — the production-scene
  `WorldgenVisualCapture` fixture using V8, seed 1337, center (512,-372), terrain
  height 39, 49 loaded chunks and a 1600×900 viewport.

The renderer reports six non-fatal `RGBFloat`-to-`RGBAFloat` conversions from
the existing campfire smoke `CurveTexture` (`scripts/Campfires.cs:230`) during
the rendered survival check and two during the surface capture. Both captures
completed successfully; this is a device format fallback, not a meshing or
shader compilation failure.

A 120-second windowed survival smoke passed its feeding, cave, save/reload,
temporary-world cleanup and payload-drain gates and exited with code 0. The
runner now clears its references to the freed main scene and process wrapper
and collects before `SceneTree.Quit`; the rerun produced no C# script-binding,
unsafe-reference, shader or RID leak diagnostics. The headless 30-minute soak
remains the source of the long-duration PASS/failure gates; the screenshots
demonstrate rendered output, not statistical frame-rate performance.

## Limitations and interpretation

- This soak is a deterministic integration/reliability test at LOW preset and
  view distance 2, not a 30-minute maximum-view-distance stress test or a human
  combat/playability test.
- Headless timing/memory results are not FPS claims. The rendered check confirms
  Vulkan device selection and visible scene rendering, not a statistically
  controlled GPU performance comparison.
- Applied mesh and metadata byte counts are estimates. No claim is made that
  they equal driver VRAM or every native Godot allocation.
