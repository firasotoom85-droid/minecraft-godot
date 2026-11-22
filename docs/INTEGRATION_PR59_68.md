# Integration report — PRs #59–#68

Consolidated integration of ten open feature branches into one tested
implementation, without new gameplay scope and without changing world or save
compatibility.

- **Integration branch:** `integration/pr59-68`
- **Merge base / starting point:** `main` at `b7a4dd9`
  (the merge commit of PR #58, which brought PRs #49–#57 into `main`)
- **Environment:** Godot 4.7.2 .NET (mono), .NET 9 SDK, Vulkan Forward+,
  NVIDIA GeForce MX230
- **Result:** Debug and Release builds 0 warnings / 0 errors;
  `scripts/verify.sh` **198,718 checks, 0 failures across 48 suites**

## 1. Integrated pull requests

Every branch was fetched from `origin`, shared the same merge base `b7a4dd9`,
and was integrated as a real merge commit so the original PR commits remain
traceable from `main` history.

| PR | Branch | Integrated change |
|---|---|---|
| #59 | `art/item-visual-mapping-audit` | Correct item silhouettes and mappings: whole-fish and drumstick food art, isometric slab/stair previews, contact-sheet audit coverage |
| #60 | `feature/material-gallery-before-after-report` | Locked material-gallery capture profile plus matched atlas/greedy before/after report |
| #61 | `fix/cave-exposure-transition` | Smooth cave exposure driven by cached baked skylight |
| #62 | `art/rare-enchanted-item-visuals` | Rare/legendary item presentation, enchantment glints, slot rarity pin, first-person icon-state refresh |
| #63 | `feature/directional-sunlight-atmosphere` | Phase-aware directional sunlight and sky-fill tint through the existing shared materials |
| #64 | `fix/voxel-ao-seam-validation` | Baked AO continuity coverage across partial-block seams |
| #65 | `feature/procedural-texture-validation-report` | Headless texture/atlas validator, Markdown report and synthetic regression suites |
| #66 | `visual/distance-haze` | Camera-depth distance haze for modern worlds with exponential fog for caves, immersion and legacy worlds |
| #67 | `feature/snow-sand-ash-surface-variation-final` | Tileable calm snow/sand art plus appended `BlockId` 60 / `ItemId` 109 Ash |
| #68 | `fix/warm-voxel-emitter-lighting` | Unified torch and campfire baked emission; shared bounded warm response in the lit shaders |

### Integration order

Actual merge order on the branch, with the resulting merge commits:

| Order | PR | Merge commit |
|---:|---|---|
| 1 | #65 | `afad883` |
| 2 | #64 | `74dfe32` |
| 3 | #60 | `abcd21c` |
| 4 | #59 | `dc01911` |
| 5 | #62 | `9183081` |
| 6 | #61 | `1135eb1` |
| 7 | #63 | `35238cb` |
| 8 | #66 | `c4e40c6` |
| 9 | #67 | `b2d4d7b` |
| 10 | #68 | `8cc5312` |

Two deviations from the suggested order were required:

1. **#67 before #68.** #68 patches `tests/SurfaceVariationTests.cs`, a file that
   #67 creates. Merging #68 first would have forced a content conflict against a
   non-existent file.
2. **#61 → #63 → #66 kept consecutive.** All three rewrite the same cave/fog
   region of `DayNight.cs`. Landing them consecutively resolved that one
   overlap once, in a single coherent shape, instead of re-resolving it three
   times.

The rest of the order is dependency-safe: test/tooling PRs first, then item
presentation (#59 → #62, both rewriting `ItemDb` icon entry points), then the
atmosphere group.

## 2. Conflicts resolved

### `scripts/DayNight.cs` — cave exposure × directional sunlight × distance haze

Three branches touched the same block. The merged result keeps:

- `EvaluateCaveDarkness(skyLight)` and the trilinear sampled
  `TrySampleCachedSkyLight` camera lookup, with the smooth terrain-depth
  fallback used only until the light cache is populated (#61).
- `SkyPalette` with `SunDirection`, `SunColor` and `SkyLightColor`, the
  `EvaluateSunDirection` helper and the `ChunkMaterials.SetSkyLighting(...)`
  call (#63).
- Depth-fog mode selection and the bounded density clamp (#66).

One textual auto-merge was **not** semantically valid: #66 referenced an
`underground` boolean that #61 had replaced with the continuous cave weight.
The resolution introduces `bool sheltered = caveDarkness > 0.001f` and uses it
both for `WorldEnvironmentFactory.FogModeFor(...)` and for the haze density
clamp. A cave mouth therefore blends into the exponential immersion profile
continuously instead of switching fog models at a fixed terrain depth.

### `scripts/BlockDb.cs` — directional sunlight × warm emitter response

#63 wrapped the sky term in `directional_sky_light(worldNormal, sky)` for five
shared shaders; #68 replaced the `shadowed_block` warm term with
`warm_block_response` / `warm_block_illumination` / `warm_block_emission` and
injected a shared `WarmBlockLightingShader` snippet. Four conflict hunks
resolved to one combined illumination expression per lit shader:

```glsl
vec3 illumination = vec3(0.035) + directional_sky_light(surface_world_normal, sky)
        + warm_block_illumination(warm_response);
```

`LitAtlasShader`, `LitGlassShader` and `OpaqueArrayShader` each keep their
existing base ambient (`0.035` / `0.05` / `0.035`) and their single warm
helper include. The glass shader keeps both the added
`varying vec3 surface_world_normal` + `void vertex()` and the warm include. The
lava shader keeps its separate emissive path with the directional sky term
only. No `shadowed_block` reference remains anywhere.

### `scripts/ItemDb.cs` — icon mapping audit × rare/enchanted visuals

#59 rewrote `Icon(ItemId)` to route the six slab/stair items through
`BuildVoxelShapeIcon`, and #62 replaced `Icon(ItemStack)` and the worn-icon
cache with the quality/enchantment-aware path. The merged form keeps both:
`IsVoxelShapeIcon`, the shape-preview rasterizer, the `Icon(ItemStack)` overload
that folds in `EnchantmentVisualMask`, `IconVisualState`, and a single
`QualityIconCache` keyed by `(item, wear stage, enchantment mask)`.
`Icon(ItemId, int durability)` still returns the plain path for any
block-backed item, so shape previews and exact atlas regions both survive.

### `scripts/TextureAtlasValidator.cs` — validator contract vs shape previews

After the merge the live report failed with 12 real errors: #65's validator
required every placeable item to expose an `AtlasTexture` region, while #59
intentionally gives slabs and stairs generated isometric previews.

`AtlasItemMapping` now carries an explicit `UsesShapeIcon` flag, sourced from
`ItemDb.IsVoxelShapeIcon`. The validator then:

- reports `item.icon.path.conflict` if a shape item also claims the atlas path;
- still reports `item.icon.not-atlas-backed` for **every other** placeable item
  that loses its atlas region;
- still requires valid, painted face tiles for both paths;
- skips only the region-equality check for the documented shape items.

The Markdown report gained a `Preview` column (`atlas region` / `shape preview`)
so the distinction is visible in the published report.

The contract is pinned by `TextureAtlasValidationTests.TestShapePreviewIconContract`:
a shape-only mapping passes, a cube item that loses its region still reports
`item.icon.not-atlas-backed`, a cube item with a wrong region still reports
`item.icon.region.invalid`, a shape item claiming both paths reports
`item.icon.path.conflict`, and the live registry must expose exactly six shape
mappings that all agree with `ItemDb.IsVoxelShapeIcon`.

### Test registrations

`tests/TestRunner.cs` received `item-visual-quality` (#62) and
`texture-atlas-validation` (#65) at non-overlapping positions; no duplicate
suite registration was introduced. `tests/AtmosphereTests.cs` and
`tests/LightingTests.cs` each needed one duplicated-method conflict resolved by
keeping both suites' coverage (`TestCaveExposure` +
`TestSunlightDirectionAndBakedChannels` + `TestDistanceHazeProfile`; and
`TestSkyLightInterpolation` + `TestWarmEmitterResponse`).

### Documentation

`README.md` and `ROADMAP.md` conflicted in every merge. Three separate PRs
(#59, #63, #66) each added a "## 35." heading to `ROADMAP.md`; all three records
are preserved and renumbered as sections 35–40, with section 41 recording this
integration. Prior integration history is preserved verbatim, and `PLAN.md`
keeps its historical purpose — no historical section was rewritten.

## 3. Files changed

60 files against `origin/main` (`+4535 / −281`): 18 under `scripts/`, 21 under
`tests/`, 17 under `docs/`, 1 shader, 3 at the repository root
(`README.md`, `ROADMAP.md`, `PLAN.md`).

Primary source files:

- Lighting/atmosphere: `DayNight.cs`, `VoxelLighting.cs`, `VoxelWorld.cs`,
  `BlockDb.cs`, `WorldEnvironmentFactory.cs`, `Campfires.cs`, `Chunk.cs`,
  `MenuBackdrop.cs`, `Player.cs`, `shaders/water.gdshader`
- Items/presentation: `ItemDb.cs`, `ItemId.cs`, `DurabilitySlotPanel.cs`,
  `Hud.cs`, `InventoryUi.cs`, `Player.cs`
- Content: `BlockDb.cs`, `BlockId.cs`, `MaterialResponse.cs`, `Sound.cs`
- Tooling: `TextureAtlasValidator.cs`

## 4. Verification

Executed on Godot 4.7.2 .NET with .NET 9:

| Check | Command | Result |
|---|---|---|
| Debug build | `dotnet build -c Debug` | Succeeded — 0 warnings, 0 errors |
| Release build | `dotnet build -c Release` | Succeeded — 0 warnings, 0 errors |
| Headless suite | `scripts/verify.sh` | **198,718 checks, 0 failures, 48 suites** |
| Atlas report | `res://tests/TextureAtlasValidation.tscn` | PASS — 61 blocks, 60/256 referenced tiles, 61 painted, 47 item mappings, 0 errors / 0 warnings |

All 48 declared suites executed; no suite is unregistered or duplicated, and the
run exits with no leaked objects, RIDs or channel errors. The only warnings are
the two intentional save-recovery warnings owned by `SaveTests`.

### Compatibility checks

- **Persisted IDs:** every pre-existing `BlockId` (0–59) and `ItemId` (0–108)
  value is unchanged; `BlockId` 60 / `ItemId` 109 (Ash) are appended and pinned
  by `ContentTests`.
- **Save format:** still V4. Legacy V1–V3 saves load with defaults; Ash
  round-trips through the existing optional content path.
- **World generation:** V1–V11 remain byte-identical. Golden hashes for V1, V3,
  V5, V6, V7, V8 (`worldgen`) and V9, V10, V11 (`integration-regressions`) all
  match. No generator rule changed in this batch, so **no new generator version
  was introduced**, and no existing version is byte-incompatible.
- **Architecture:** every voxel mutation still goes through
  `VoxelWorld.SetBlock` / `ApplyEdit`; the campfire source migration and the
  gallery fixtures both use that path.
- **Rendering contracts:** greedy meshing, chunk streaming, the packed atlas,
  isolated per-tile array mipmaps and the classic nearest-sampled atlas path are
  unchanged.
- **No new content assets:** all new art is original procedural pixel art
  generated in C#.

## 5. Real-scene visual validation (MX230, Forward+)

Matched captures were reproduced on this integration build and compared against
each source PR's own reference frames (mean absolute error over RGB, 0–255):

| Capture | Result | Comparison to PR reference |
|---|---|---|
| Warm light, torch | flame 14 / adjacent 13, 63 chunks | 2.17 MAD |
| Warm light, campfire | flame 14 / adjacent 13, 69 chunks | 4.36 MAD |
| Warm light, torch + campfire overlap | flame 14 / adjacent 13, 64 chunks | 4.25 MAD |
| Cave exposure (seed 1337, V11, 512/-372) | skylight 15.00 outside / 14.00 passage / 5.27 chamber, darkness 0.778, 56 chunks | matches #61's recorded gradient exactly |
| Sun dawn / noon / sunset / night | 49 chunks each; mean luminance 0.366 / 0.518 / 0.335 / 0.260 | 6.41 / 3.15 / 5.03 / 0.55 MAD vs #63 frames |
| Distance haze (seed 777, V11, -424/760, view distance 5) | 121 chunks | 4.71 MAD vs #66 frame |
| Material gallery, greedy + atlas, close + distant | 61 blocks, 9 chunks, 0 pending, 1280×720 | 1.72–2.26 MAD vs #60 "after"/"before" frames |

Interpretation:

- Warm-light source parity and max-propagated overlap are confirmed on hardware:
  torch and generated campfire both contribute level 14 with adjacent level 13
  in all three modes, and overlap does not sum into a washed-out texture.
- The cave gradient is numerically identical to the value recorded in #61,
  so the haze/cave-fog reconciliation did not disturb the exposure blend.
- The four sun phases preserve the phase ordering and warm/cool separation from
  #63. Residual per-frame differences come from clock advance during capture
  settling, not from a change of palette logic.
- The gallery renders all 61 registered definitions (Ash included, visible in
  the legend) in both the classic atlas and greedy texture-array paths under the
  fixed #60 capture profile.

Captures were written to `/tmp` for this review; the committed reference PNGs
under `docs/visual/` and `docs/worldgen/` are unchanged by this integration.

## 6. Performance

No rendered FPS improvement is claimed: no controlled frame-time benchmark was
run for this batch, and the changes are presentation-level.

Measured CPU figures printed by this suite run (single machine, indicative):

- `WORLDGEN_CHUNK_PROFILE`: V8 9.24 ms/chunk vs V7 8.91 ms/chunk (**1.04×**),
  V6 4.88 ms/chunk.
- `WORLDGEN_WEATHERING_PROFILE`: V9 10.74 ms/chunk vs V8 9.96 ms/chunk
  (+7.8%, within run-to-run noise for two sampled chunks).
- `WORLDGEN_PROFILE`: 4096 sampled columns, combined 1.74× faster than separate
  samplers.

Structural cost review of the integrated batch:

- Cave exposure adds **eight cached light-map reads per frame** plus stack
  storage. It never generates chunks, scans the world or issues physics queries.
- Campfires **remove** one `OmniLight3D` and per-frame flicker updates each.
- The unified warm response evaluates one `exp()` per fragment and reuses it for
  diffuse and emission; it replaces the previous multiply-based term.
- Directional sunlight adds a handful of ALU operations per fragment and no new
  pass, light or mesh rebuild.
- Distance haze uses Godot's built-in depth-fog path; no extra fog volume or node.
- Icon work stays in the existing caches and runs only on first use of a new
  item/wear/enchantment combination.

## 7. Remaining limitations

- Visual validation is representative: one seed/coordinate per feature, captured
  on the MX230 only. No RTX 4060-class comparison and no exhaustive biome/weather
  sweep exists yet.
- No rendered frame-time or FPS benchmark was run for this batch, so the
  performance section is a structural review plus CPU generation timings only.
- The standalone shader paths are validated by source assertions and live
  captures, not by a shader-compiler unit test.
- Manual review still recommended: sunrise/sunset and rain across distant ridges,
  cave entrances in every preset, and the first-person held-item presentation for
  legendary enchanted gear.
- `PLAN.md` was intentionally left as a historical log; current state lives in
  `README.md`, `ROADMAP.md` and this report.
