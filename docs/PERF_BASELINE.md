# Performance baseline

> **Snapshot, not a CI benchmark.** These numbers come from one development
> laptop. They are not reproducible on a clean machine without the same
> hardware and a focused window; use the build/test steps in `README.md` for
> correctness and re-record a baseline when tuning performance.

## Machine

- CPU: Intel Core i7-1065G7 (8 threads)
- GPU: NVIDIA GeForce MX230, 4 GB
- RAM: 7 GB
- OS: CachyOS (Arch)
- Godot: 4.7.2-stable mono
- Build: **Debug** (`dotnet build -c Debug`), 0 warnings

## Integration measurement — 2026-11-21

Taken after merging WorldGen V7, the pixel-art UI/controller pass and the
survival equipment progression into `integration/v1.0-merge`.

### Method

A temporary probe scene instantiated `res://scenes/main.tscn`, waited 15 s for
streaming to settle, then sampled 20 s of frame deltas and printed one summary.
The window was 1280×720, V-Sync off, on an isolated user-data directory.

### Results — HIGH preset, view distance 8

| Metric | Value |
|---|---|
| Frames sampled | 613 |
| Average FPS | **30.6** |
| Average frame time | 32.71 ms |
| Median (p50) frame time | 32.36 ms |
| p99 frame time | 36.99 ms |
| Worst frame | 70.4 ms |
| Streamed chunks | 441 |
| Scene nodes | 2,585 |
| Static memory | 171 MB |
| Video memory | 263 MB |
| Worker mesh build (avg / max) | 383 ms / 538 ms |

### Results — default settings, auto quality on

| Metric | Value |
|---|---|
| Average FPS | 29.6 |
| Average frame time | 33.83 ms |
| p99 frame time | 39.47 ms |
| Streamed chunks | 512 |
| Video memory | 282 MB |
| Worker mesh build (avg / max) | 621 ms / 736 ms |

### Comparison against unmodified `main`

The same probe on `origin/main` (headless, isolated user data) reported a
worker mesh average of **342 ms** and a max of 380 ms. The integration run at
HIGH/VD8 reported **383 ms / 538 ms**. The worlds differ (V7 terrain is denser),
so this is an indication of V7 content cost, not a meshing regression. Frame
times are stable: p99 is within ~4 ms of p50, i.e. no streaming hitches were
observed in the sampled window.

Headless (no rendering) the scene runs at ~145 FPS, which confirms the cost is
render/scene-side rather than simulation-side.

### Caveats

- Debug assemblies; release builds are faster.
- One run per configuration; the camera path and world seed are not fixed.
- No RTX 4060-class hardware was available, so high-end numbers are not
  recorded.
- The quality governor is disabled for the HIGH measurement so the preset does
  not change mid-sample.

## Historical baseline — M5 (2026-10-21)

Recorded while verifying the M5 content capacity and test-harness work.

- Scene: `res://scenes/main.tscn`, normal preset, survival.
- Renderer: Forward Plus, 1152×648 window.
- `Gfx` preset **HIGH** (index 1), view distance **8**, greedy meshing **on**,
  auto-quality **off**.
- Idle FPS: **42–47**, 441 streamed chunks, 3,092 scene nodes.

## Test suite at this baseline

`./scripts/verify.sh` → **1,182 checks, 0 failures** on the integration branch,
including content registry, recipes, inventory stack/drag rules, save
compatibility, legacy worldgen golden hashes, lighting, fluids, mining, mob
content, equipment and keybinds.

## Integrated 1.0 world-generation measurement — 2026-11-21

### Method

`tests/WorldgenTests.ProfileChunkGeneration` warms three generators (V6, V7,
V8), then times the same eight chunks (`-2..1` on both axes) in one headless
process. Reproduce with:

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 \
  "$GODOT" --headless --path . res://tests/TestRunner.tscn 2>&1 \
  | grep WORLDGEN_CHUNK_PROFILE
```

The measurement is informational, not a CI gate. It runs on the same MX230
laptop with a Debug assembly, so absolute values move with background load;
compare V7 and V8 from the same printed line.

### Results — warmed 8-chunk sample

| Generator | ms/chunk | Relative to V7 |
|---|---:|---:|
| V6 | 3.52 | — |
| V7 | 6.46 | 1.00× |
| V8 (terrain + villages) | 6.52 | **1.01×** |

Post-audit reruns on the same machine: 7.69 / 7.87 ms per chunk (V7 / V8,
**1.02×**) in the final suite, and 6.93 / 7.62 ms (**1.10×**) when another
Godot process was competing for CPU. An earlier run under heavier system load
measured 11.46 / 11.49 ms (1.01×). The ratio is therefore sensitive to host
load while the implementation is unchanged; compare V7 and V8 from the same
printed line rather than across runs. Village sites are cached per 384-block
cell under a lock, so repeated chunk requests do not re-sample village
placement noise.

The integrated suite at this measurement is **4,217 checks, 0 failures**,
including V1–V8 golden chunks, the combined shape/villager/quest/boat save
round-trip, live village resident spawning, and bucket/fluid/shape boundaries.
