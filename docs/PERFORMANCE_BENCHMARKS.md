# Repeatable performance benchmarks

The opt-in benchmark runner uses the real main scene and current V8 world
generator. It creates a disposable world under `user://worlds/`, runs a fixed
seed and scripted camera route, writes reports under `user://benchmarks/`,
then closes the scene and deletes the temporary world. It never opens or saves
the player's selected world.

## Run

Build and launch with the Godot 4.7.2 .NET editor/runtime. Run the benchmark
scene as a separate process for each scenario:

```bash
dotnet build -c Debug
godot --path . res://tests/PerformanceRunner.tscn -- \
  --scenario=open-terrain --seed=1337 --warmup=15 --duration=30 \
  --preset=0 --view-distance=5 --resolution=1280x720 \
  --commit="$(git rev-parse HEAD)"
```

For Release compilation, use the project file directly because the solution
defines `Debug`, `ExportDebug` and `ExportRelease`, but not a solution-level
`Release` configuration:

```bash
dotnet build minecraft.csproj -c Release
```

For a standalone command-line launch, this project's Godot runtime loads
.godot/mono/temp/bin/Debug/minecraft.dll by default. To test a Release build,
direct Release output to the runtime path selected by this project:

    dotnet build minecraft.csproj -c Release -o .godot/mono/temp/bin/Debug --nologo
    godot --path . res://tests/PerformanceRunner.tscn -- --scenario=open-terrain

Alternatively, select Release in the Godot editor's build-configuration
selector before launching. Confirm the report's buildConfiguration field.
The optional --build argument can override the reported value for a separately
verified artifact; never use it to relabel a Debug assembly as Release.

Keep the Godot version, renderer, window size, V-Sync state, graphics preset,
view distance, seed and camera scenario fixed when comparing commits. Close
overlays and other GPU-heavy applications, keep the benchmark window focused,
and run each configuration at least three times. Compare the per-run results
(prefer the median) rather than selecting the best result.

Interactive runs set the requested window size (default 1280×720), disable
V-Sync and remove the engine FPS cap. The player remains in the real
physics/process loop but has input locked; the benchmark route drives the
player/camera and zeroes movement velocity. This retains underwater camera,
weather, ambience and ordinary scene processing without allowing keyboard input
to perturb the route. The ocean route places the camera below the water surface.

The runner records its display server, V-Sync mode and actual window size.
Headless runs explicitly mark `headless=true`, set GPU details and resolution
to not-applicable, and keep the project's headless FPS cap; their 0 draw calls
and frame rate are not rendering benchmarks.

The runner waits eight seconds for the real scene to initialize and stream its
initial chunks, then performs the requested warm-up before sampling. The
default warm-up is 15 seconds and the default measurement is 30 seconds.
Duration is clamped to 5–600 seconds. The quality governor is disabled and
greedy meshing is enabled for every run.

The graphics presets are ordered from LOW to MAX FSR2. New installations
default to LOW (index 0) with a five-chunk view radius. Version-1 settings are
migrated so legacy LOW/HIGH/ULTRA/MAX/MAX FSR2 selections keep their original
meaning; custom view distances are retained. BALANCED occupies index 1, so do
not reuse the old integer mapping in external tools.

## Scenarios

Run one scenario per process:

| Scenario | Workload and route |
|---|---|
| `open-terrain` | Nearest plains/flower-plains/meadow surface with a small fixed circular route |
| `dense-forest` | Dense forest; falls back to a wooded biome only if the search window contains no dense forest, and records that fallback |
| `mountain-river` | Mountain sample with the strongest local V8 river signal in the fixed search window; the report flags when no strong overlap is found |
| `village` | Nearest deterministically generated V8 village in the search window |
| `deep-cave` | A generated cave air pocket with a solid floor; camera route stays inside the cave |
| `ocean` | Ocean biome at sea level, including underwater rendering |
| `heavy-rain` | Open terrain during storm weather |
| `hostile-encounter` | Night-time open terrain with four Shades, two Rots and two Hollows; the runner verifies requested hostiles remain present at the end of the sample |
| `fast-exploration` | Open terrain with a larger/faster circular route (180 m radius / 18 m/s defaults) to exercise chunk streaming; explicit route arguments override these defaults |

All scenario location searches are deterministic for a given seed and
generator version. If a required location cannot be found, the runner exits
with an error rather than recording a mislabeled workload.

## Arguments

| Argument | Default | Meaning |
|---|---:|---|
| `--scenario` | `open-terrain` | One scenario from the table above |
| `--seed` | `1337` | World seed |
| `--warmup` | `15` | Warm-up seconds, 0–300 |
| `--duration` | `30` | Measurement seconds, 5–600 |
| `--preset` | `0` | Graphics preset index: 0 LOW, 1 BALANCED, 2 HIGH, 3 ULTRA, 4 MAX, 5 MAX FSR2 |
| `--view-distance` | `5` | Chunk view distance, 2–24 |
| `--resolution` | `1280x720` | Interactive window size in pixels; ignored in headless mode |
| `--build` | detected | Build label stored in the report; pass only when verified against the assembly |
| `--route-radius` | scenario-specific | Circular route radius in metres, 4–512 |
| `--route-speed` | scenario-specific | Route speed in metres/second, 0.1–32 |
| `--commit` | `unknown` | Git SHA to store in the report |

## Reference-hardware comparison

The three-repeat MX230 Release comparison for the immediate optimization pass
is documented in
[PERFORMANCE_MILESTONE_2_MX230.md](PERFORMANCE_MILESTONE_2_MX230.md). Use it as
a controlled baseline for that exact Godot version, renderer, resolution,
preset, view distance, seed and scenario. It is not an RTX 4060 baseline and
should not be generalized to other presets or GPUs.

## Reports

Each successful run writes two files:

- `user://benchmarks/perf-<scenario>-<seed>-<timestamp>.json`: hardware,
  engine/build, renderer, resolution, graphics, generator, scenario and final
  state, chosen center and route radius/speed, warm-up, frame-time statistics,
  streaming/mesh counters, memory and GC summary. Memory/object/VRAM high-water
  fields are sampled at one-second intervals.
- `user://benchmarks/perf-<scenario>-<seed>-<timestamp>-frames.csv`: one row
  per measured frame with elapsed time, frame/process/physics time, rendered
  and cached chunk counts, and fresh/rebuild queue depths.

The summary includes average frame time and FPS, nearest-rank p50/p95/p99 and
worst frame time; final scenario state (weather, immersion and requested hostile
spawns); Godot process/physics timings; process RSS, managed heap,
managed allocations and GC collections; Godot static memory in Debug; video
memory; object/node counts; draw calls/primitives; mesh-build distribution
from the existing profiler ring buffer; creature counts and streaming queue
high-water marks.

Godot's portable built-in monitors do not provide GPU frame time, so that
field is explicitly null. The engine process/physics monitor values can lag
their corresponding frames; use the runner's captured frame deltas as the
authoritative frame-time distribution. Godot's static-memory monitor is
Debug-only; use process RSS and managed heap for cross-configuration memory
comparisons.

World-cache accounting reports exact byte-array lengths for voxel block,
shape-state, sky-light, block-light and packed-light arrays, plus the edit-top
and generator seed arrays. For non-empty collections it reports capacity-based
estimates for dictionary indexes, the crop registry, the separate source-scan
and crop-scan chunk registries, campfire/structure/village site caches,
edit-top indexes, saved string-keyed registries and the built-chunk index.
Nested object graphs inside containers, furnaces, villagers and quest records
are not recursively sized; collection/key estimates are deliberately
identified as estimates, not exact heap measurements. The generator-v8
village-site dictionary is accounted for as a separate capacity-based
estimate. Applied mesh source arrays, retained mesh-pool buffers, pending
worker payloads, process RSS, managed heap and video memory remain separate
fields to avoid double-counting unlike resources.

Every category also has a sampled high-water mark. Clearing or trimming a
collection lowers its current estimate; retained high-water values do not
decrease, so a later snapshot cannot hide an earlier peak. Sampling uses the
existing one-second benchmark/soak interval, so these diagnostics add no
per-frame scan or steady-state allocation.

Streaming priority queues and their uniqueness sets are measured from retained
collection capacities. The two concurrent apply queues report an estimated
node/reference overhead from their current item counts; pending payload byte
contents remain in the existing pending-payload counters and are not counted
again here. The saved inventory/boat staging lists are measured from list
capacity, not from deep object graphs.

Empty dictionary/HashSet/priority-queue backing storage is not exposed through
a read-only public capacity API, so it is omitted when a collection has zero
entries rather than allocating backing storage during measurement.

These remain sampled snapshots/high-water marks, not a complete CLR/native
allocator or per-object ownership trace. Scene-tree/native allocations,
VoxelLighting's thread-static scratch queues/buffers and renderer-driver memory
are not included in the managed-cache estimate; use process RSS and the
Godot/video monitors alongside the detailed cache fields when diagnosing total
process retention.

The runner is also safe to smoke-test with `--headless`, but headless timing
does not include real rendering and must never be used as a GPU/FPS baseline.
Headless CI remains a correctness gate; controlled reference hardware remains
the performance gate. Do not add strict timing thresholds to shared CI until
the reference machines and measurement variance are established.
