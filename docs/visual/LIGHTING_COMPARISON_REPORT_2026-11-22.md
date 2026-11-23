# Voxel-light comparison capture report — 2026-11-22

## Capture contract

- Game/runtime: Godot 4.7.2 .NET, Forward+ / Vulkan 1.4.312
- GPU: NVIDIA GeForce MX230 (driver 580.178.04)
- World: seed 1337, generator V11, clear weather
- Viewport: 1280×720; fixed 180-frame warm-up
- Quality: LOW, render scale 0.80, 1024 directional shadow atlas
- Disabled post effects: SSAO, SSR, glow and volumetric fog
- Chunk bound: view distance 3; explicit 5×5 preview footprint
- All five runs saved PNG successfully and reported zero OmniLight3D nodes

The output is a controlled visual reference, not an FPS benchmark. Captures use
the existing production world environment, directional sun/moon, baked
VoxelLightData, atlas/greedy-array/glass/water shaders, and chunk mesher. The
only synthetic geometry is the temporary torch chamber/shaft and every such
voxel edit uses VoxelWorld.SetBlock.

## Per-scene observations

| Scenario | Fixed clock | Deterministic location | Probe | Sky / block | Registered nearby sources |
|---|---:|---|---|---:|---:|
| Forest canopy | 0.50 noon | DenseForest canopy at (-512, -768), 112 sampled trees | Log (-518, 56, -765) | 0 / 0 | 336 |
| Torch cave | 0.50 noon | Chamber (-20, 30, 16), shaft at (-18, 43, 16) | Torch (-20, 32, 13) | 10 / 14 | 724 |
| River valley | 0.50 noon | Channel (1416, -1528), river strength 1.00 | Water (1416, 40, -1528) | 13 / 0 | 119 |
| Village street | 0.73 dusk | Plains village centre (-684, 50, 1303) | Torch (-690, 53, 1294) | 11 / 14 | 163 |
| Dungeon arena | 0.02 night | Ember Deep (447, 51, -378), arena Y 31 | Torch (441, 33, -380) | 6 / 14 | 499 |

Light probe values are the packed 4-bit baked sky and block channels at the
named voxel. The forest probe intentionally samples the opaque tree trunk;
its zero values are not an assertion that the whole canopy is unlit. The image
is the visual assessment for canopy shadowing. Nearby-source counts are
existing source-registry entries found in the probe's bounded search region;
they are not scene light-node counts.

## Reference frames

- [Forest canopy](screenshots/lighting-forest-canopy.png)
- [Torch cave](screenshots/lighting-torch-cave.png)
- [River valley](screenshots/lighting-river-valley.png)
- [Village street](screenshots/lighting-village-street.png)
- [Dungeon arena](screenshots/lighting-dungeon-arena.png)

Reproduce with the five commands under **Controlled voxel-light comparison
scenes** in README.md. Keep seed and quality preset constant across a set.
The optional quality argument is LOW, BALANCED, HIGH or ULTRA.

## Scope and performance

No production lighting, world-generation, save schema, persisted IDs, shader,
renderer preset, greedy-meshing or chunk-streaming code was changed. The
world-location scans run once in the standalone capture runner; none runs in
gameplay. The capture bound is intentionally 25 preview chunks. The captures
confirm that emitters remain baked voxel-light sources and do not allocate one
OmniLight node per torch/campfire. This is not a whole-game MX230 frame-time or
VRAM benchmark.
