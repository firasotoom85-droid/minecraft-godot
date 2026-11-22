# Visual quality pass — environment and water

**Implementation branch:** `visual/milestone-4-environment-polish`
**Engine:** Godot 4.7.2 .NET / C#
**Baseline:** `main` at `55d79087af6a0caade435f18ae7a6a60d66bfdca`

This increment addresses the environment-rendering portion of Milestone 4. It
extends the existing day/night, weather, and water systems; it does not add a
second environment controller, a new water renderer, or replacement scene
systems.

## Changes

### Day/night atmosphere

- Added a deterministic sky palette evaluator used by the existing
  `DayNight` node. Dawn and dusk now receive their own warm horizon envelope,
  while the upper sky, ground horizon, daylight, night, cloud response, ambient
  energy, sun/moon energy and air-fog multiplier stay in one testable calculation.
- Overcast blends retain a readable horizon instead of flattening the entire
  sky to one grey. Lightning uses the same palette and temporarily lifts the
  sky and ambient response.
- The existing biome fog tint, underground darkness, underwater fog, stars,
  cloud meshes, caustic frames and voxel-light energy application remain in
  their current systems.

### Precipitation and biome transitions

- The existing weather node now refreshes the rain/snow biome choice when the
  player enters a different integer world column. It reuses the cached biome
  result while the player remains in that column, rather than re-querying the
  same terrain cell every frame.
- Weather state probabilities, duration ranges, particle emitters, lightning,
  sound hooks and ground-wetness presentation remain in the existing system.

### Water presentation

- Water now calculates camera-space depth from its transformed world-space
  vertex after the existing top-surface wave displacement. This avoids using
  chunk-local vertex coordinates with the global view matrix, which made
  shoreline depth fade and distance-based ripple damping dependent on chunk
  placement.
- Added low-amplitude procedural shoreline foam using the existing water
  shader and material. No new scene nodes, textures, particle emitters or
  rendering paths were introduced.

## Visual capture

The new `AtmosphereVisualCapture.tscn` runner loads the real game scene,
hides the existing HUD for clean review frames, creates a disposable world with a fixed seed and generator version, applies a
fixed time/weather state, waits for streaming, and saves a GPU-rendered PNG.
The temporary world is removed after capture.

Example (run with the project's Godot 4.7.2 .NET editor binary):

```bash
godot --path . res://tests/AtmosphereVisualCapture.tscn -- \
  1337 8 512 -372 0.50 Clear \
  res://docs/visual/screenshots/atmosphere-river-valley-noon-clear.png
```

Arguments are seed, generator version (1–current supported version), world X, world Z, time of day
(0–1), weather (`Clear`, `Cloudy`, or `Rain`), and output PNG path. Storm is
intentionally excluded from this deterministic capture runner because its
lightning timing is random. Use matching coordinates and vary time/weather to
build a comparison set. For representative V8 locations, see
[`docs/worldgen/V8_VISUAL_VALIDATION.md`](worldgen/V8_VISUAL_VALIDATION.md).

### Captured samples

**Frozen V8 baseline**

![V8 river-valley noon atmosphere](visual/screenshots/atmosphere-river-valley-noon-clear.png)

- Seed 1337, generator V8, centre (512, -372), midday, clear weather.
- Godot 4.7.2 .NET, Vulkan Forward+, NVIDIA GeForce MX230.
- 1600 × 900; 49 streamed chunks; capture completed successfully.

**Current V9 generator**

![V9 river-valley noon atmosphere](visual/screenshots/atmosphere-v9-river-valley-noon-clear.png)

- Same seed, centre, time and weather, using the current generator V9.
- 1600 × 900; 40 streamed chunks; capture completed successfully.
- The capture runner supports all persisted generator versions V1–V9. These
  screenshots compare two generator versions under the same viewing conditions;
  this PR does not modify either generator's output.

These are rendered reference frames, not a sign-off for every biome, weather
state, camera angle, or graphics preset. The remaining manual matrix should
cover dawn/noon/sunset/night in plains and forest, snow precipitation in a
snow biome, desert colour response, mountain horizons, cave entrances and
deep caves, village interiors/exteriors, clear/rain/storm weather, and
above-water/underwater water views on LOW and HIGH presets. The existing
V8 world-generation capture set remains the baseline for terrain shape.

## Compatibility and scope boundaries

- No `BlockId` or `ItemId` values were added, changed, reordered or reused.
- No world-save fields or save versions changed.
- No generator source or output was changed by this rendering-only
  increment. The capture runner accepts existing generator versions V1–V9; the
  committed reference frame intentionally uses V8 for comparison with the
  existing V8 visual baseline.
- Voxel mutation, packed voxel-light propagation, greedy meshing, chunk
  streaming and far-LOD generation were not replaced.
- No third-party textures, models, sounds, or proprietary game assets were
  added.
- Material/texture consistency work already in separate open PRs remains
  separate; this branch does not duplicate their BlockDb, meshing, far-LOD or
  atlas edits.

## Validation

Record results for the exact commit under review:

- Debug build: Godot 4.7.2 .NET / .NET 9; 0 warnings, 0 errors.
- Release build: `dotnet build minecraft.csproj -c Release`; 0 warnings,
  0 errors.
- Headless regression suite on the rebased current main: **153,482 checks,
  0 failures**, including the merged benchmark, streaming, graphics, material,
  glass, V9 weathering and existing gameplay suites.
- Real-scene Forward+ captures: PASS on MX230 for V8 (49 chunks) and V9
  (40 chunks), both at 1600 × 900; water shader compiled and rendered.

The screenshot runner emitted the project's existing missing-script-UID
fallback warnings in this fresh worktree; Godot resolved the tracked scene
script paths and completed the capture. The harness now accepts all supported
generator versions on current main (V1–V9); the committed reference remains
V8 so it can be compared against the project's existing V8 visual baseline. The headless save suite also logs its
expected failed-write and backup-recovery warnings while testing those error
paths. Neither is reported as a clean, warning-free runtime launch.
