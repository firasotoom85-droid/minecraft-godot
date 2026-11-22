# Performance Milestone 2 — MX230 results

**Date:** 2026-11-21
**Project:** Minecraft Godot, Godot 4.7.2 .NET
**Before commit:** 5ee89916a402ce1b6287d247c09d796ffc945ad6
**After commit:** 8d86939d4608af0fe932d5ddb0509b4c270bb7f4

## Test machine and controls

- GPU: NVIDIA GeForce MX230 (4 GB), Vulkan Forward+.
- CPU: Intel Core i7-1065G7, 8 logical CPUs.
- Resolution: 1280×720, focused interactive window, V-Sync off, no FPS cap.
- Build: Release assembly (verified by the report's buildConfiguration=Release
  field); Godot 4.7.2 stable .NET.
- World: generator V8, seed 1337, same resolved test location.
- Graphics: LOW preset, view distance 5, greedy meshing enabled, quality
  governor disabled.
- Each run: 5-second warm-up followed by 20 seconds of captured frames.
- Three independent process runs per scenario and code revision; results below
  compare the median of those three runs.

The before and after revisions were built as Release assemblies and launched
using the runner's detected configuration. The older LOW preset used 0.85
render scale and 60 m directional shadow distance; the M2 LOW preset uses
0.80 render scale and 45 m shadow distance. Both used a 1024 shadow atlas and
the same five-chunk view radius. The rendering configuration change is part of
the measured improvement; this comparison does not attribute the gain to any
one individual optimization.

## Median results

| Scenario | Metric | Before | After | Change |
|---|---:|---:|---:|---:|
| Open terrain | Average FPS | 68.9 | 75.3 | **+9.4%** |
| Open terrain | p50 frame time | 13.89 ms | 12.96 ms | −6.7% |
| Open terrain | p95 frame time | 15.28 ms | 13.89 ms | **−9.1%** |
| Open terrain | p99 frame time | 16.67 ms | 16.62 ms | −0.3% |
| Open terrain | Maximum sampled RSS | 694.3 MiB | 688.5 MiB | −0.8% |
| Heavy rain | Average FPS | 67.5 | 73.7 | **+9.1%** |
| Heavy rain | p50 frame time | 13.91 ms | 12.96 ms | −6.8% |
| Heavy rain | p95 frame time | 15.39 ms | 14.29 ms | **−7.1%** |
| Heavy rain | p99 frame time | 16.82 ms | 19.88 ms | +18.2% |
| Heavy rain | Maximum sampled RSS | 706.0 MiB | 704.3 MiB | −0.2% |

The median average FPS improved by roughly nine percent in both scenarios.
The p95 frame time improved in both. Open-terrain p99 was effectively
unchanged. Heavy-rain p99 moved up by about 3.1 ms; its 19.9 ms median remains
below the proposed 70 ms MX230 p99 ceiling, but the increase is recorded
rather than hidden. A 20-second capture contains only about 1,300–1,500
frames, so p99 is sensitive to a small number of intermittent frames and
should be rechecked during longer validation.

Process RSS was essentially unchanged (within 1% in these samples). Managed
allocation totals were modestly lower, but these runs are too short to claim a
material memory reduction. The results are not a long-session memory soak or
a full-gameplay guarantee.

## Implemented changes measured by this revision

- Menu preview reduced from a 9×9 area (81 chunks) to 5×5 (25 chunks), a
  69% reduction in preview chunk count.
- Menu chunks keep the regular greedy render mesh but skip collision triangle
  generation, collision-shape creation and physics collision layers.
- Menu-only directional shadows and SSAO/SSR/glow/volumetric fog are disabled;
  3D rendering scale is 80%, camera far distance is 240 m, and menu rendering
  is capped at 30 FPS. Previous frame cap and viewport settings are restored
  when the menu scene exits.
- Day/night cloud updates no longer allocate three temporary arrays every
  frame. Player-column biome tint and surface height are cached and reused for
  cave-depth detection.
- Weather ground-splash height is sampled only when rain is active and the
  snapped four-metre anchor changes.
- Added BALANCED and tuned the preset ladder for the reference laptop. New
  settings default to LOW; a versioned migration preserves the old preset
  mapping and custom view distance.
- Automated tests cover preset values, settings migration, render-only chunk
  collider omission and render-only mesh payloads. Gameplay meshing continues
  to build collision triangles.

## Compatibility and validation

No BlockId or ItemId values were added, moved or reused. No save format or
world-generation version changed; V1–V8 golden generation remains unchanged.
Voxel changes still use the existing mutation path. The normal gameplay path
continues to use the existing chunk streaming, greedy meshing and collision
systems.

Validation on this revision:

- scripts/verify.sh: **4,276 checks, 0 failures**, 29 suites.
- Debug build: 0 warnings, 0 errors.
- Release build: 0 warnings, 0 errors.
- Interactive menu scene launched on the MX230; the captured menu retained
  readable terrain, horizon and controls.
- Three rendered Release runs completed for each of open terrain and heavy
  rain on the MX230.

The menu capture is a visual sanity check, not a quantified menu power or FPS
comparison. A 60-minute gameplay soak and the corresponding RTX 4060 hardware
comparison remain outside this milestone. The heavy-rain p99 change should be
reviewed again using longer captures before setting strict performance gates.
