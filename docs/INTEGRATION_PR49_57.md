# Integration report — PRs #49–#57

Branch: `integration/pr49-57`, based on `main` (`fd4453b`). Each feature branch
was merged with `git merge --no-ff`; no branch was squashed and no unique change
was discarded. The consolidated pull request targets `main` and is not merged.

## Contribution and conflict map

| PR | Branch | Unique contribution | Overlaps resolved | Verification |
|---|---|---|---|---|
| #49 | `gameplay/milestone-5-polish` | 15-step first-hour guide, quest-stage announcements, pause/keybind Escape ownership, first-person action/locomotion poses | `Player.cs` with #51 wear and #56 outline; `README.md`, `PLAN.md`, `ROADMAP.md` with later docs; `TestRunner` registration | Onboarding, first-person-animation, quest/pause suites |
| #50 | `fix/transparent-cutout-depth` | Mesh-bounds-center sorting for glass/water, depth-write and back-face policy, cutout depth/shadow treatment | Shader edits with #53 and #57; `GlassTests` renamed to `transparent-materials` without losing coverage | Transparent-materials suite |
| #51 | `feature/equipment-durability-icons` | Cached scuff stages for tools/weapons/armor, shared condition meter in existing slots, held-item wear refresh | `ItemDb` icon cache with #52; `Player.cs` with #49/#56; slot-panel draw path | Equipment-durability-visuals suite and rendered fixture |
| #52 | `art/generated-item-icon-cohesion` | Centered/normalized standalone icons, graphite contour, shared key-light fill, per-image mip chain | `ItemDb` with #51; block icons stay on the atlas; first-person sampling policy | Item-icons suite |
| #53 | `visual/texture-lighting-contrast-audit` | Foliage response for canopy tiles 56–58, held-item family derived from registered block, bounded sky/block shader blend | Shaders with #50/#57; `MaterialResponse`/`ItemDb` with #52 and #57 | Surface-variation and shader-path tests |
| #54 | `feature/block-edge-shading` | One half-voxel AO/light sampler for cubes, split faces and slab/stair micro-faces; diagonal chunk-state borders | `Mesher` with #57 wet tags/merge key; `BlockShapeTests` with greedy path | Block-shapes and greedy/per-face parity |
| #55 | `feature/material-test-gallery` | Production-pipeline gallery for every registered block; close/distant greedy and atlas capture | `TestRunner` registration with #49/#54; README/ROADMAP docs; BlockId count now 60 | Material-gallery suite and MX230 captures |
| #56 | `feature/block-selection-contrast` | Two-pass dark/bright target outline; shared cube/slab/stair geometry cache | `Player.cs` with #49/#51; no mesher/material change | Selection-outline suite |
| #57 | `art/water-adjacent-readability` | Wet vertex tag and shader response, far-LOD wet tint, generator V11 river reeds, atlas tile 59 / BlockId 59 / ItemId 108 | Shaders with #50/#53; `Mesher` merge key with #54; `BlockDb`/`ItemDb`/`ContentTests`; docs | Water-adjacent-materials, integration-regressions, V11 captures |

## Dependencies and redundancy

- #57 depends on #53’s shared family response and #54’s corner sampler/merge-key
  layout. Its wet bit is `1L << 56`, above the packed AO/light fields.
- #52 depends on #51’s lazily cached icon variants; the worn path clears and
  regenerates mipmaps after painting so the pristine atlas-backed path is not
  altered.
- #49, #51 and #56 all edit `Player.cs` but own separate fields/methods; all
  three were retained.
- #53 and #54 both edit `Mesher.cs`; the combined `EmitLitFace` keeps #54’s
  sampler arguments and #57’s wet flag.
- No PR is fully superseded. Two redundant pieces were removed during
  integration:
  - the extra V11 dispatch branch in `VoxelWorld.StampDecorators` (the existing
    `>= V8` branch already reaches `StampDecoratorsV8`, where the V11 guard
    lives);
  - the duplicate RiverReeds fallback in `MaterialResponse.ForItem` (the
    registered block lookup already returns the reed tile and foliage family).
- The V11 reed stamp now checks the actual stamped surface block before writing,
  so a village path or carved hole cannot receive a floating reed. Its
  placement hash is checked before the four neighbour-height samples; output is
  unchanged and the common non-reed roll avoids that sampling.

## Compatibility evidence

- **Persisted IDs:** `BlockId` 0–59 and `ItemId` 0–108 are contiguous and
  append-only. `RiverReeds` is 59/108; no prior value moved.
- **World generation:** V1–V10 rules are untouched. V9 and V10 anchor hashes for
  chunks `(-8,-31)`, `(32,-24)` and `(0,0)` at seed 1337 match the
  pre-integration `main` checkout exactly. V11 uses a separate version and only
  changes V10 `Air` cells into `RiverReeds`; every changed cell is checked for
  grass/sand ground, the sea-level band, a shallow-water neighbour and the
  deterministic placement roll.
- **Determinism:** V11 chunks generated forward and in reverse order across
  positive and negative coordinates hash identically.
- **Saves:** format remains V4. Existing V2/V3/V4 tests still pass, a saved V10
  world stays on V10, and a V4 round-trip preserves reed block edits, reed item
  stacks, container data, block-state extension and hunger.
- **Voxel architecture:** every new test fixture edits through
  `VoxelWorld.SetBlock`. No replacement mutation, meshing, lighting, streaming
  or collision system was introduced.
- **Rendering:** opaque atlas, greedy texture array, alpha-scissor foliage,
  blended glass/water, lava, first-person items and selection outlines all keep
  their existing materials. Wet identity is in the greedy key; the gallery uses
  the production chunk/mesher/material path.

## Validation performed

Environment: Godot 4.7.2 .NET, local .NET SDK 9.0.321, Linux.

| Check | Result |
|---|---|
| `dotnet build -c Debug --nologo` | 0 warnings, 0 errors |
| `dotnet build minecraft.csproj -c Release --nologo` | 0 warnings, 0 errors |
| `GODOT=... ./scripts/verify.sh` | exit 0; all suites pass |
| Headless `TestRunner.tscn` | 46 suites, 189,838 checks, 0 failures |
| `git diff --check` | no whitespace errors |

Test counts vary slightly between runs because some worldgen suites stop after
finding the feature under test. Zero failures and a complete suite list are the
gates, not a fixed total. No test is skipped and no suite is registered twice.

New combined coverage added in `tests/IntegrationRegressionTests.cs`:

- pinned V9, V10 and V11 chunk hashes (positive and negative coordinates);
- V11 forward versus reversed chunk-generation order;
- V11 delta limited to `Air → RiverReeds`, with placement-rule validation;
- append-only block/item ranges;
- V4 save/player/manifest round-trip containing the appended reed content;
- wet-versus-dry greedy merge-key equality except bit 56, and non-wettable
  materials remaining unaffected.

## Performance

Matched headless workload, same probe and seed, three interleaved rounds. Timings
are Debug CPU time; they are not rendered FPS claims.

| Workload | `main` | Integrated | Delta |
|---|---|---|---|
| Per-face `Mesher.Build`, chunk `(0,0)` | 2.86 ms | 3.95 ms | +38% |
| Greedy `Mesher.Build`, chunk `(0,0)` | 6.80 ms | 7.90 ms | +16% |
| V10 chunk generation, 48 mixed-sign chunks | 5.95 ms/chunk | ~5.97 ms/chunk | noise |
| V11 chunk generation, same workload | — | ~6.03 ms/chunk | ~1% over V10 |

The mesher increase is the cost of sampling actual half-voxel occupancy and
copying diagonal chunk borders for AO correctness. It happens on mesh worker
threads, adds no per-frame work, draw calls, vertices or materials. V11
generation is effectively at V10 parity after moving the placement hash ahead
of neighbour sampling.

## Checklist

- [x] #49 first-hour guidance and first-person animations
- [x] #50 transparent water/glass sorting and depth policy
- [x] #51 equipment durability icons and condition meter
- [x] #52 procedural standalone icon consistency
- [x] #53 material lighting/contrast corrections
- [x] #54 shared voxel edge AO
- [x] #55 registered-block material gallery
- [x] #56 block targeting outline
- [x] #57 water-adjacent materials and V11 river reeds
- [x] Debug and Release builds: 0 warnings / 0 errors
- [x] Full headless suite: 0 failures
- [x] `scripts/verify.sh` and `git diff --check`
- [x] Append-only IDs, V4 saves, V1–V10 generation preserved
- [x] V11 deterministic and delta-limited
- [x] README, ROADMAP and PLAN reconciled
- [x] Consolidated PR [#58](https://github.com/firasotoom85-droid/minecraft-godot/pull/58) opened; CI `build-and-test` passes
- [x] Original PRs #49–#57 linked to #58 as superseded and closed after the combined verification passed

## Remaining limitations

- The V11 reed captures and gallery/equipment captures are the ones committed by
  the individual PRs; this integration did not perform a new rendered visual
  pass. Other biomes, weather states and presets remain unreviewed.
- First-person poses, controller timing, combat readability and broader village
  usability still need human playtesting.
- No rendered FPS benchmark or RTX 4060 comparison was run. The only new
  performance data is the matched headless CPU table above.
- Mesh CPU is measurably higher for partial-block AO. This was accepted for
  correctness; a future optimization could avoid diagonal chunk snapshots in
  chunks with no partial shapes, but that needs its own correctness and
  performance validation.
