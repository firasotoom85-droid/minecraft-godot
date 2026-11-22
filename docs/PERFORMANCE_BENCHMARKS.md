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
  --preset=1 --view-distance=8 --resolution=1280x720 \
  --commit="$(git rev-parse HEAD)"
```

For Release compilation, use the project file directly because the solution
defines `Debug`, `ExportDebug` and `ExportRelease`, but not a solution-level
`Release` configuration:

```bash
dotnet build minecraft.csproj -c Release
```

Only label a run as Release when Godot is actually loading the Release assembly;
confirm the report's `buildConfiguration` field. Do not merely pass a label
for a Debug assembly. The optional `--build` argument can override the reported
configuration when launching a separately built artifact. Keep the Godot
version, renderer, window size, V-Sync state, graphics preset, view distance,
seed and camera scenario fixed when comparing commits. Close overlays and other GPU-heavy applications; keep
the benchmark window focused. Run each configuration at least three times and
compare the per-run results (prefer the median run) rather than selecting the
best result.

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
| `--preset` | `1` | Graphics preset index: 0 LOW, 1 HIGH, 2 ULTRA, 3 MAX, 4 MAX FSR2 |
| `--view-distance` | `8` | Chunk view distance, 2–24 |
| `--resolution` | `1280x720` | Interactive window size in pixels; ignored in headless mode |
| `--build` | detected | Build label stored in the report; pass only when verified against the assembly |
| `--route-radius` | scenario-specific | Circular route radius in metres, 4–512 |
| `--route-speed` | scenario-specific | Route speed in metres/second, 0.1–32 |
| `--commit` | `unknown` | Git SHA to store in the report |

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
comparisons. These measurements are snapshots/high-water marks, not exact
per-chunk memory ownership; a detailed cache/payload byte budget remains a
separate task.

The runner is also safe to smoke-test with `--headless`, but headless timing
does not include real rendering and must never be used as a GPU/FPS baseline.
Headless CI remains a correctness gate; controlled reference hardware remains
the performance gate. Do not add strict timing thresholds to shared CI until
the reference machines and measurement variance are established.
