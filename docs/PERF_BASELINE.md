# Performance baseline — M5

> **Historical snapshot.** These numbers come from one development laptop, not
> a CI benchmark. They are not reproducible on a clean machine without the same
> hardware and a focused window; use the build/test steps in `README.md` for
> correctness and re-record a baseline when tuning performance.

Recorded 2026-10-21 on the development machine while verifying the M5 content
capacity and test-harness work.

## Machine

- CPU: Intel Core i7-1065G7 (8 threads)
- GPU: NVIDIA GeForce MX230, 4 GB, driver 580.178.04
- RAM: 7 GB
- OS: CachyOS (Arch)
- Godot: 4.7.2-stable mono
- Build: **Debug** (`dotnet build -c Debug`), 0 warnings

## Scene / settings

- Scene: `res://scenes/main.tscn`, most recent world, normal preset, survival.
- Renderer: Forward Plus, 1152×648 window.
- `Gfx` preset **HIGH** (index 1), view distance **8**, greedy meshing **on**,
  auto-quality **off**.
- Window focused for every sample (unfocused readings are throttled).

## Results

| Metric | Value |
|---|---|
| Idle FPS (sample range) | **42–47** |
| Streamed chunks | 441 |
| Scene nodes | 3,092 |
| Chunks after view-distance change | 625 → 441 (settled) |

## Method

1. Launch `main.tscn` with the current world.
2. Focus the game window with `xdotool windowactivate`.
3. Apply `Gfx.ApplyPreset(1, false)` and `World.SetViewDistance(8)`.
4. Wait 8 s for streaming to settle.
5. Sample `Engine.get_frames_per_second()` several times.

## Caveats

This is a **baseline snapshot**, not a benchmark harness: the camera path and
world are not fixed, the build is Debug, and only one run was sampled. The
release build is expected to be faster. Future milestones should compare
against this number using the same window size, preset, view distance and
focused-window rule; M5.4's fixed-seed benchmark scene remains a useful
follow-up when tuning begins.

## Test suite at this baseline

`./scripts/verify.sh` → 373 checks, 0 failures at the M5 checkpoint; the suite
grew to **668 checks** after M8.4 (minimap), including:

- Content registry and recipe/item/block validation.
- Inventory stack/overflow/save round-trip.
- Recipe matching and one-click crafting.
- Damage/death/respawn/drowning.
- Save v2 compatibility, v3 fields, backup rotation and corruption recovery.
- v1 anchor chunk hash unchanged.
- Greedy vs per-face mesher surface-area parity.
