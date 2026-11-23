# Integration report — PRs #70–#79

**Baseline:** `main` at `c5093cc` (merge of PR #69).
**Integration branch:** `integration/pr70-79`, created from that commit.
**Date:** 2026-11-23.
**Scope:** nine merged PRs; #73 closed as superseded by #74.

`main` was not modified. Every merged branch landed through a real `--no-ff`
merge commit, preserving its original commits.

## Dispositions

| PR | Branch head | Integration | Disposition |
|---|---|---|---|
| #77 | `dcdbeb7` | `4294082` | Integrated first; controlled lighting fixtures. |
| #78 | `0d2c682` | `bef6dfb` | Integrated; chunk-stream continuity tests and capture. |
| #79 | `1b13c45` | `3cc277f` | Integrated; shape-aware baked-light propagation. |
| #70 | `f933e55` | `5559c30` | Integrated; packed-channel emissive presentation. |
| #71 | `eb635a8` | `8b9fe94` | Integrated; dark-night hostile silhouettes. |
| #72 | `b0aa4a8` | `a09197c` | Integrated after #71; directional moon and cloud-aware stars. |
| #73 | `91558de` | — | **Closed as superseded by #74.** Not merged. |
| #74 | `62b7da9` | `1556eb3` | Integrated; bounded cave adaptation plus water/biome exposure. |
| #75 | `a1eeac0` | `17eb70b` | Integrated; preset shadow softness and contact shading. |
| #76 | `dcf112b` | `afcf40a` | Integrated last; unified weather sky and clouds. |

### Dependency and base findings

- All branches except #72 and #74 were based on `c5093cc`; no rebase was
  required.
- **#72 contains #71.** `eb635a8` is an ancestor of `b0aa4a8`. Merging #71
  before #72 therefore applied only #72's moonlight/starfield/documentation
  commits, not a second copy of #71.
- **#73 and #74 are parallel implementations, not a stack.** `91558de` is not
  an ancestor of `62b7da9`; #74 was recreated from `c5093cc` with the cave
  adaptation plus water, snow and forest exposure contexts. Merging both would
  have duplicated the camera-adaptation logic. #73 was closed with that
  explanation.
- #76 consumes the #72 `SkyPalette` extension and the #75 preset profiles, so
  it landed last. #77's fixtures were merged first to provide the shared
  comparison scenes used by later validation.

Recommended and executed order:

`#77 → #78 → #79 → #70 → #71 → #72 → #74 → #75 → #76`

## Semantic conflict resolutions

1. **Water shader (#70 × #72).** The merged shader keeps #70's
   `warm_block_response` and adds the moon term inside the existing
   `directional_sky_light` helper. There is still one water material and one
   shader pass.
2. **Lava (#70 × #72).** Lava keeps its packed block-light emission
   (`lava_source_level`) and receives the same restrained moon contribution as
   the other lit materials.
3. **Sky palette (#72 × #76).** `DayNight.SkyPalette` is one record carrying
   the existing sky/sun fields plus `MoonDirection`, `MoonColor`, `CloudColor`
   and `CloudOpacity`. Cloud tint/opacity and moon lighting are evaluated once
   per palette update, not in separate presentation paths.
4. **Capture harness (#71, #72, #74, #76, #77).** The merged
   `AtmosphereVisualCapture` parser accepts the legacy and preset-first forms,
   cave modes, exposure modes, hostiles and weather. Duplicate capture runners
   were not kept.
5. **Camera exposure ownership (#74).** #74 originally assigned the player's
   adaptive `CameraAttributes` to every atmosphere capture. That made the #71
   silhouette and #76 weather frames depend on adaptation time and raised the
   measured night luminance from the committed 25.96 to about 75. The final
   harness shares adaptive exposure only for cave and exposure scenarios;
   silhouette, weather and general atmosphere frames use the fixed default
   attributes used by their references.
6. **Night fill budget (#71 × #72).** The first integrated moonlight pass
   illuminated outdoor night surfaces about three times more strongly than the
   baked night floor and washed out #71's silhouettes. The directional moon is
   retained, but its shader contribution is scaled by `0.12` in all five lit
   paths. The corrected silhouette capture measures 25.51 mean luminance
   against the 25.96 reference (MAE 2.94), instead of the 75+ mean of the
   uncorrected integration.
7. **Capture determinism.** Captures now default to LOW instead of the user's
   persisted preset. The assertions and references that require a higher tier
   pass it explicitly. This makes a bare capture independent of local settings.
8. **Hostile fixture settle.** LOW's smaller mesher pipeline could still have
   the disposable stage pending after the old 12-frame wait. The fixture now
   waits for the production mesh/rebuild queues to idle and for eye adaptation
   to settle. The wait is limited to the hostile teleport; it is not applied to
   MAX water transitions, whose fluid fixture intentionally keeps rebuilding.
9. **Cave torch diagnostics.** `sealed-torch` is the torch mode; the generic
   `includeTorch` flag is not used for cave modes. The cave log now records the
   torch cell, registry entry, packed source/adjacent levels and room skylight
   so this cannot be mistaken for a lighting failure.
10. **Documentation.** Each PR's roadmap record is preserved. The previously
    unnumbered moonlight entry is now section 47; this integration is section
    52. `PLAN.md` was not rewritten.

## Compatibility

- `BlockId` and `ItemId` were not reordered or renumbered, and this batch adds
  no persisted IDs.
- All world mutations continue through `VoxelWorld.SetBlock`/`ApplyEdit`.
- Save format V4 is unchanged.
- Generator V1–V11 files and `Worlds.cs` are unchanged; the full suite's V1–V11
  golden hashes and reversed-order checks pass.
- Greedy meshing, chunk streaming, packed sky/block light nibbles, the vertex
  layout, shared material owners and the LOW/BALANCED presets are preserved.
- Runtime additions are limited to palette uniforms and existing presentation
  paths. No per-emitter light nodes, terrain scans, extra scene nodes or new
  render passes were introduced. The capture harness changes are test-only.

## Verification

- `dotnet build -c Debug`: 0 warnings, 0 errors.
- `dotnet build -c Release`: 0 warnings, 0 errors.
- `scripts/verify.sh`: **199,417 checks, 0 failures across 48 suites**.
- Texture atlas validator: PASS, 61 blocks, 60 referenced / 61 painted tiles,
  47 placeable mappings, 0 errors and 0 warnings.
- `git diff --check`: clean.
- The suite includes lighting, moonlight, atmosphere, graphics, block-shape,
  integration-regression, save and V1–V11 worldgen coverage. No assertion was
  weakened.

## Real-scene visual validation

Captures ran serially on the NVIDIA GeForce MX230, Vulkan Forward+, Godot
4.7.2 .NET. Image comparisons are full-frame mean absolute error (0–255)
against the committed reference where one exists.

| Scenario | Result |
|---|---|
| Warm torch / campfire / overlap | packed source 14, adjacent 13 in all three; lava source 12, adjacent 11. |
| Night hostiles, LOW | 25.51 mean luminance vs 25.96 reference; MAE 2.94; stage 22.15 vs 20.12. |
| Sealed cave / entrance / sealed torch | MAE 0.49 / 3.14 / 1.39; room skylight 0.00, darkness 1.000, ISO max 40, ambient 0.079, torch 14/13 registered. |
| Water exit at MAX | underwater MAE 0.54, emerged 2.11; ISO 40–140. |
| Snow / taiga exposure at MAX | MAE 0.82 / 0.92; taiga ISO 39.6–153.4. |
| Shadow softness | legacy 1.47/3.21, LOW 0.49/0.56, HIGH 0.36/0.34 (close/far). LOW reports 45 m, 1024 atlas, SoftLow; HIGH 90 m, 2048, SoftMedium, SSAO on. |
| Five controlled lighting scenes | forest canopy 1.24, torch cave 0.02, river valley 0.68, village street 0.69, dungeon arena 0.81. |
| Weather, LOW noon | mean luminance Clear 130.11 → Cloudy 117.92 → Rain 105.14 → Storm 101.40; cloud opacity 0.18 → 0.625 → 0.915 → 0.98. |
| Partial-shape continuity | block light 6/5/6/6 through glass/water/bottom slab/stairs; foliage 13 below vs 15 at the open gap; MAE 1.22. |
| Chunk seam, atlas | before/fade/settled MAE 1.78/1.61/1.53; boundary block light 13, camera skylight 15. |
| Chunk seam, greedy | before/fade/settled MAE 2.13/1.99/1.66; same boundary probes. |
| Atlas vs greedy | MAE 1.51–1.95 across the three phases. |

The weather camera looks down at terrain, so it validates sky/horizon colour,
weather darkening and cloud opacity numerically but does not show the cloud
planes in frame. The sealed cave retains the reference's bright blue fog; that
is the #73/#74 reference behaviour, not an integration regression, and was not
redesigned in this batch.

## Performance

Release assembly, LOW, 1280×720, view distance 5, seed 1337, 10 s warm-up and
30 s measurement. Three runs per configuration, same route and geometry.

| Configuration | p50 | p95 | p99 | Mean FPS |
|---|---:|---:|---:|---:|
| `main` `c5093cc` — run 1 | 12.96 ms | 15.00 ms | 22.22 ms | 74.6 |
| `main` `c5093cc` — run 2 | 12.96 ms | 16.67 ms | 50.00 ms | 70.4 |
| `main` `c5093cc` — run 3 | 12.96 ms | 18.72 ms | 49.87 ms | 70.0 |
| **Baseline median** | **12.96 ms** | **16.67 ms** | **49.87 ms** | **70.4** |
| Integration — run 1 | 9.72 ms | 11.11 ms | 18.15 ms | 99.2 |
| Integration — run 2 | 9.72 ms | 11.11 ms | 21.71 ms | 99.4 |
| Integration — run 3 | 9.72 ms | 12.79 ms | 39.25 ms | 92.8 |
| **Integration median** | **9.72 ms** | **11.11 ms** | **21.71 ms** | **99.2** |

The integrated branch measured 25% lower median frame time and 33% lower p95
than the baseline under this harness. This is a whole-branch comparison, not a
bisection, so the improvement should not be attributed to one PR without a
follow-up A/B. It does establish that the batch introduced no measurable
regression on this configuration.

Heavy rain measured 10.00 ms p50, 17.59 ms p95, 50.00 ms p99 and 86.6 mean FPS.
The report's `gpuFrameTimeMs` is null because Godot exposes no portable GPU
counter; frame deltas are the authoritative measurement.

## Remaining work

- The water and biome exposure references were captured at MAX. The current
  harness defaults to LOW, so reproduce those references with an explicit
  preset index 4; the report and README record that requirement.
- The sealed cave still renders a bright blue fog in its committed reference.
  This predates the integration and was preserved for traceability.
- Weather captures are terrain-facing and do not visually frame the cloud
  planes; cloud tint/opacity were verified numerically and by the palette
  tests.
- No interactive playtest, controller review or long survival soak was run in
  this integration. Rendered captures and the headless suite are the evidence
  here; interactive feel remains manual.
- GPU frame time is unavailable through Godot's portable monitors. CPU frame
  deltas, draw calls and primitive counts are recorded in the benchmark JSON.
