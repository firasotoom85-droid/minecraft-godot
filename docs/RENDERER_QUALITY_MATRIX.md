# Renderer quality matrix

This document describes the six existing renderer presets and the systems they
control. It is a configuration/verification reference, not a claim that every
GPU has been benchmarked.

## Preset matrix

| Preset | View distance | In-flight chunks | LOD radius | Shadow distance / atlas | SSAO | SSR | Glow | Volumetric fog | Render scale |
|---|---:|---:|---:|---:|---|---|---|---|---:|
| LOW | 5 | 3 | Off | 45 m / 1024 | — | — | — | — | 0.80× + FXAA |
| BALANCED | 7 | 4 | 256 m | 65 m / 1024 | — | — | — | — | 0.90× + FXAA |
| HIGH | 9 | 5 | 384 m | 90 m / 2048 | On | — | On | — | 1.00× + TAA |
| ULTRA | 12 | 6 | 512 m | 120 m / 4096 | On | On | On | — | 1.00× + TAA |
| MAX | 12 | 6 | 640 m | 130 m / 4096 | On | On | On | On | 1.25× + TAA |
| MAX · FSR2 | 12 | 6 | 640 m | 130 m / 4096 | On | On | On | On | 0.70× FSR2 |

The render-scale pixel-load estimates relative to native resolution are 64%,
81%, 100%, 100%, 156% and 49%, respectively. They describe the approximate
number of internal render pixels before temporal reconstruction, not total
GPU cost. Shadows, screen-space effects, geometry, transparency and driver
behaviour are not represented by this percentage.

## Lighting ownership

- Skylight and block light remain in the existing packed four-bit voxel-light
  channels and are consumed by the shared atlas, greedy texture-array, glass,
  water and lava shader paths.
- The existing directional sun and moon provide dynamic shadows. Their filter,
  angular softness and shadow distance are selected by the active preset.
- Torches and lava do not create one Light3D per voxel. Campfires retain the
  existing bounded, nearby, shadowless prop-light exception; this is not a
  per-block emitter architecture.
- Mesh-baked voxel AO remains active at every quality tier. Screen-space SSAO
  is an additional contact pass only at HIGH and above, with its direct-light
  and AO-channel influence constrained to avoid double-darkening.
- The menu backdrop owns separate temporary viewport and environment settings.
  Its lightweight exponential atmosphere is intentionally not the gameplay
  depth-haze profile.

## Environment construction and compatibility

Gameplay now supplies the saved preset when it creates the shared environment.
The same `Gfx.ApplyEnvironmentPreset` method is used when a preset changes,
so the initial frame and later changes agree. A factory call without an
explicit preset preserves its historical high-quality defaults for existing
callers such as galleries and standalone comparison fixtures.

Preset selection does not change world generation, persisted block/item IDs,
save format, packed lighting, voxel mutation ownership, greedy meshing or chunk
streaming. Legacy V1–V7 worlds retain their exponential fog model and density;
V8+ worlds retain the shared camera-depth haze parameters regardless of preset.

## Repeatable benchmark scenarios

Use the existing `tests/PerformanceRunner.tscn` rather than creating a second
benchmark harness. Run each scenario at the same seed, resolution, view
distance and preset, with auto quality disabled. Recommended representative
scenarios are:

- `open-terrain` — baseline terrain and far-LOD cost.
- `dense-forest` — foliage overdraw, shadows and canopy geometry.
- `deep-cave` — low skylight, local block light and dark adaptation.
- `mountain-river` — depth haze, water and terrain complexity.
- `ocean` — large water coverage and transparency.
- `village` — structures, residents and mixed materials.
- `heavy-rain` — weather particles, clouds and wet-surface presentation.
- `hostile-encounter` — mobs, shadows, particles and combat effects.
- `fast-exploration` — streaming pressure while moving across chunk borders.

Example matched run (repeat three times per preset and compare p50/p95/p99):

```bash
godot --path . res://tests/PerformanceRunner.tscn -- \
  --scenario=dense-forest --seed=1337 --warmup=15 --duration=30 \
  --preset=LOW --view-distance=5 --resolution=1280x720
```

Record the commit, Godot version, renderer, GPU/driver, display resolution,
scenario, preset, seed, warm-up, duration, loaded chunks and pending mesh
payloads with each result. Compare frame-time percentiles and streaming
backlog; do not infer GPU performance from headless runs. Godot's portable
performance monitors do not provide a reliable GPU frame-time metric, so use
a platform GPU profiler when GPU timing is required.

## Verification boundaries

The automated graphics suite checks that every preset's four optional
environment effects match its definition and that preset selection does not
alter legacy fog or modern distance haze. Real-renderer screenshot checks remain
separate: they verify visible output, not frame-time performance. Run
`scripts/verify.sh` and a Debug/Release .NET build on the workstation before
merging this branch.
