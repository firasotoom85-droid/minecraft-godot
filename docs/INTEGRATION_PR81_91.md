# Integration of PRs #81–#91

This branch integrates ten open pull requests onto `main` (`2eb0eae`). It does
not start a second lighting pipeline and it does not re-land work already on
`main`; every commit below is one existing PR branch merged in dependency order.

## Why this order

The order is driven by data and shader dependency, not by PR number.

1. **#81 — negative-coordinate chunk block-light regressions.** Test-only. It
   adds a world-level check across `x = -16`/`z = -16` and a capture room, and
   changes no runtime path, so it lands first and keeps running unchanged while
   the lighting data model moves underneath it.
2. **#91 — direct skylight, cave spill and block emission.** The core change:
   `VoxelLightData` gains a nibble side map for the pre-flood vertical direct
   pass, the mesher carries direct sky in vertex red and the combined-minus-
   direct delta in the fractional material tag. Everything that measures or
   tunes lighting afterwards must be resolved against this model.
3. **#83 — LOW preset shader instruction reduction.** Rewrites the same
   fragment expressions #91 split. Merged after #91 so the LOW-quality
   substitutions are re-applied to the *new* channel expressions rather than
   the old combined-sky one.
4. **#89 — pale-surface directional contrast.** Adds a `pale_surface` argument
   to `directional_sky_light`, which #83 has already wrapped in its LOW
   variant. Merged after #83 so the combined signature is composed once.
5. **#82 — preset-bounded moon shadow range.** Day/night and environment
   budget work that reads the renderer presets; independent of the channel
   model but shares the comparison capture with #84/#88.
6. **#86 — continuous air-to-water fog.** Touches `DayNight` and the shared
   environment, which #82 also edits. Merged after #82 to keep one fog
   pipeline (see the resolve note below).
7. **#87 — storm lightning terrain illumination.** Adds the flash contribution
   to `SkyPalette.SkyLightEnergy`, which every shader multiplied already; it
   reads the same shared value the channel split now feeds.
8. **#85 — renderer quality matrix and environment preset parity.** Aligns
   environment construction with the presets that #82 and #86 both read.
9. **#84 — image-space voxel-light regression gates.** Adds reference-PNG
   auditing to the comparison runner, so it lands after every shader and
   environment change it would otherwise have to re-baseline.
10. **#88 — spatial voxel-light probe drift reporting.** Restructures the same
    capture entry point #82/#84/#89 extend, and adds the probe-grid JSON plus
    CI wiring, so it lands last.

## Conflicting files and how they were resolved

| File | PRs | Resolution |
|---|---|---|
| `scripts/BlockDb.cs` | #91, #83, #89 | One shader body: direct-sky channel split (#91), LOW `directional_response` substitution and `#ifndef MINECRAFT_LOW_QUALITY` tone-jitter guard (#83), `pale_surface` parameter and gain reduction (#89). The family/wet decode moves ahead of the illumination once, as #89 intended, and is not duplicated. |
| `scripts/Chunk.cs` | #83, #89 | The material factories compose both wrappers: `ApplyQualityVariant(ApplyPaleSurfaceContrast(ApplyLighting(...)))`. |
| `shaders/water.gdshader` | #91, #83 | LOW `directional_response` substitution kept, feeding `direct_sky` plus `cave_bounce_illumination`. |
| `scripts/DayNight.cs` | #82, #86, #87 | Auto-merged: the water-fog blend, the shadow budget and the flash envelope occupy different regions. |
| `tests/LightingComparisonProfiles.cs` | #84, #89 | Sixth profile kept. Its reference is `null`, which now means "captured for manual review". |
| `tests/LightingComparisonCapture.cs` | #82, #84, #88, #89 | One pipeline: optional frame grab, audit when a reference exists, probe-grid JSON, drift prints, and both the pale-surface and shadow audit fields in the render line. |
| `tests/LightingComparisonTests.cs` | #84, #88 | Channel assertions plus the probe-grid suites; reference-anchor assertions skip only the unaudited fixture. |
| `README.md`, `ROADMAP.md`, `PLAN.md` | all | Union of every PR's section. Roadmap sections that collided on the same number are renumbered in merge order (`53`–`62`). |

### Semantic decisions worth calling out

- **Greedy merging.** #91 added per-corner cave-spill nibbles to the merge key.
  #83's shader work and #89's contrast term do not touch meshing, so the key
  survives unchanged. A regression now proves the key separates faces whose
  combined and direct sky disagree, and still merges identical faces.
- **Direct vs combined sky.** Every shader's directional term now receives the
  direct channel; warm block emission still uses total sky exposure for its
  headroom. #89's pale weight multiplies the directional gain, so it also acts
  on direct sky only.
- **Reference auditing.** #84 introduced a committed-PNG gate. #89's new
  fixture has no reference and none can be produced without a GPU capture, so
  the reference became explicitly optional: a missing reference is captured and
  reported as `passed=skipped`, and a test pins that to the one new profile id
  so no established scene can quietly lose its baseline.

## Verification performed in this session

Local, Godot 4.7.2 mono (`4.7.2.stable.mono.official.ed1daf0bf`), .NET SDK
9.0.321, on the integrate branch head:

- `dotnet build -c Debug --nologo`: **0 warnings, 0 errors**.
- `dotnet build minecraft.csproj -c Release --nologo`: **0 warnings, 0 errors**.
- `scripts/verify.sh` (headless `res://tests/TestRunner.tscn`):
  **200,141 checks, 0 failures**, including worldgen V1–V11 golden hashes, save
  round-trips, both meshing paths, streaming/memory budgets, lighting,
  atmosphere, graphics, water-adjacent, mob perception and the comparison
  suites.

- Baked-light probe drift, LOW preset, seed 1337, `--probe-only`, against the
  five committed probe references: **27/27 samples compared, coverage 100%,
  `outside_tolerance=0` in every scene** (forest canopy, torch cave, river
  valley, village street, dungeon arena). The channel separation therefore
  leaves the combined sky/block light that gameplay reads unchanged at every
  reference probe. This is a baked-data check, not a rendered-image check.

Individually, before integration:

- PR #91 head `a7e02f7`: 199,565 checks, 0 failures.
- PR #86 head `6eaedec`: 199,437 checks, 0 failures.
- PR #89 head `c9924eb`: green in GitHub Actions.

## What is not verified

- **No GPU or FPS measurement.** No real-renderer capture, screenshot diff or
  frame-time comparison was produced in this session. Every visual claim in the
  merged PRs remains unconfirmed on the MX230; the reproducible capture
  commands are in `README.md` and `scripts/verify-lighting-screenshots.sh`.
- **Vertex-colour quantisation.** The 1/16 cave-spill range lives in the
  fractional part of an 8-bit vertex alpha. Tests exercise it at float
  precision; the ~16-level GPU quantisation is unmeasured.
- **Greedy vertex-count cost.** The merge key carries two more nibbles per
  corner, which can split quads where direct and combined sky disagree. Memory
  is covered by the streaming budget; the quad-count effect is not measured.
- **#88's CI wiring.** Its `ci.yml` change is merged and runs in Actions, but
  the drift job needs committed probe references and a GPU capture to be
  meaningful.
