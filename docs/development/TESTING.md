# Testing and validation

Use the narrowest test that proves the behavior, then run the full registered suite before opening a PR.

## Standard verification

    dotnet build -c Debug --nologo -warnaserror
    dotnet build minecraft.csproj -c Release --nologo -warnaserror
    GODOT=/path/to/Godot_v4.7.2-stable_mono ./scripts/verify.sh

scripts/verify.sh performs a Debug build and launches tests/TestRunner.tscn in headless mode. The default test timeout is 300 seconds and can be changed with GODOT_TEST_TIMEOUT.

The test runner prints suite-level PASS/FAIL messages and a final assertion/failure count. Do not copy a historical check count into current documentation; the current CI run and current TestRunner.cs are authoritative. The `runner-reliability` suite drives the real benchmark and survival-soak completion lifecycles, including pre-await and post-await failures, repeated finish triggers, scene teardown, worker drain before temporary-world deletion, and idempotent cleanup.

## Test categories

- Deterministic unit/integration suites: content IDs, recipes, inventory, mining, saves, world generation, shapes, lighting, fluids, mobs, quests, controller bindings and other gameplay systems.
- Real-scene headless probes: production world setup and voxel-light samples for forest, cave, river, village and dungeon contexts.
- Visual capture scenes: production meshes, materials, lighting and environment captured at fixed seeds, positions and renderer profiles.
- Soak tests: the live main scene, world streaming, physics, population, weather and repeated save/read-back over time.
- Performance benchmarks: fixed routes and profiles with JSON/CSV output; use comparable Release runs.

These validation types are not interchangeable. A headless pass does not prove rendered appearance, a screenshot does not prove long-term stability, and a soak does not prove that a human can complete combat or quest objectives.

## Survival soak

    GODOT=/path/to/Godot_v4.7.2-stable_mono bash scripts/soak-survival.sh 3600

The script accepts durations from 60 to 86400 seconds. It uses a temporary test world and must not borrow or delete the player's normal world.

## Visual validation

Use the dedicated scenes documented in README.md and the corresponding report under docs/visual or docs/worldgen. Record:
- Godot version and renderer.
- GPU/driver and operating system.
- Seed and world-generation version.
- Camera/site and weather/time settings.
- Graphics preset and output resolution.
- Before/after capture paths.

Do not call a visual change verified on the MX230 unless the capture was actually run on that GPU. Do not infer FPS from a rendered screenshot.

## CI

The CI workflow runs Debug and Release builds with warnings as errors, the full headless test runner and deterministic headless lighting probe grids. Separate workflows validate changed C# formatting, active documentation links and tagged release exports.
