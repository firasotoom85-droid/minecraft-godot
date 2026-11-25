# Branch audit and integration record — 2026-11-25

This record covers the unmerged remediation branches after W13.1/W13.2 landed
on `main`. Source branches were compared against `main` at `f384926` and newer;
the repository contents, the registered headless suite and runtime benchmark
reports were the evidence, not branch commit messages.

## W11 — persistence and data integrity

Two independent implementations existed:

| Branch | Design | Verdict |
|---|---|---|
| `work/w11-save-integrity` | One monotonically increasing generation shared by manifest/world/player; manifest is the commit marker; three rotating `.bak` tiers; pair promotion on recovery | **Selected** |
| `w11-persistence-integrity` | Manifest/world/player `SnapshotId` plus a per-world `.snapshots/<id>/` tree; pruning of committed snapshots | Rejected for now |

Selection reasons: no new on-disk directory tree, smaller save-path change
surface, explicit generation-zero legacy single-sided compatibility, and the
existing backup tiers already provide the recovery history. The selected branch
was rebased onto `f384926`, built with zero warnings in Debug and Release, and
passed the full suite (201,755 checks). Archive validation still rejects
traversal and reparse-point paths; imports now also require a generation-matched
pair. Merged as `73c1af8`.

Regression fixed during integration: the benchmark runner had been switched to
`WorldStore.DeleteTemporaryWorld`, which only accepts `user://tests/`, while
benchmark worlds live under `user://worlds/`. Every benchmark leaked its world
and exited non-zero. `tests/PerformanceRunner.cs` now uses
`WorldStore.Delete(directory, retainRecoveryCopy: false)`, and `SaveTests`
asserts both the refusal and the permanent library deletion.

## W14 — gameplay, inventory and progression

Rebased 5 commits onto W11 (`main` at the time), conflict only in the roadmap.
Verified 202,687 checks, zero failures, Debug/Release clean. Adds generator V13
for campfire/structure exclusion; V1–V12 remain frozen and existing worlds keep
their manifest generator version. Merged as `e7d2b75`.

## W13 — target-hardware evidence

Measured on the MX230 reference (i7-1065G7, LOW, 1280×720, view distance 5,
`open-terrain`, seed 1337, Release assembly, 30 s measurement): frame p50/p95
were unchanged against pre-W13 (`11.67`/`16.67` ms), p99 was 34.02 ms versus
37.84 ms, maximum draw calls rose from 317 to 560 with primitives unchanged,
and resource-step p95/p99 were 9.18/17.08 ms. Collision batches stayed below
1 ms. Full data and caveats are in `docs/PERFORMANCE_BENCHMARKS.md`. A
higher-end GPU run remains outstanding.

## W15 — UI, controls, audio, graphics

`w15-ui-controls-graphics-followups` (63 commits, 730 insertions) is the
superset: it contains the shared gameplay-clock work and more tests/docs than
`w15-ui-controls-audio-graphics` (48 commits, 560 insertions), which re-implements
the same keybind, minimap, photo, onboarding and audio fixes. Decision: use the
followups branch as the single base and treat the other branch as superseded.

A combined branch (`w15-combined`, merge `5ac8f3f`) merged current `main` and
resolved the only semantic conflict by keeping W14's selected-slot consumption
together with W15's pause-aware eat cooldown. It builds with zero warnings. The
first combined suite exposed two keybind failures on the branch itself:

- `saved keyboard remaps can take a key another saved action vacated (expected E, got W)`
- `keyboard remap sets restore independently of declaration order (expected Z, got E)`

Both were a product bug: `IsReservedOutsideBindable` scanned every InputMap
action, including Godot's built-in `ui_*` actions and platform text-editing
variants, so nearly every key looked reserved and saved remaps silently fell
back to defaults. Commit `3d752a2` skips `ui_*` plumbing while still protecting
non-bindable game actions; the combined branch then passed **202,756 checks,
0 failures**, including keybinds. W15 still needs its minimap/photo/glass
captures and a human UI/audio playtest before merging.

## W12 — player lifecycle

`agent/w12-player-lifecycle` is 81 commits over 22 files (+1,188/−129) with 80
added assertions. It overlaps `VoxelWorld.cs`, `Player.cs`, `Chunk.cs` and
`Main.cs`, all changed by W13/W11/W14, so it cannot be merged as one unreviewed
slice. Proposed review slices, in dependency order:

| Slice | Content | Primary commits | Files | Acceptance |
|---|---|---|---|---|
| 1. Collision-ready epoch | Track applied collision payload readiness; publish readiness only after a successful payload; empty-payload readiness | `a1247ff`, `9eb6a9b`, `ffac8bd`, `9117185`, `f1278df`, `627186d` | `Chunk.cs`, `VoxelWorld.cs`, `VoxelStreamingTests.cs` | Rebase first; chunk collider availability matches applied payload; stale epoch rejected; suite green |
| 2. Safe destination transaction | One teleport/respawn/void/console path validating support, capsule clearance and collision readiness before commit | `dc87e36`, `9725162`, `b7b5e7e`, `0f6f5b6`, `d984593`, `eb7f986`, `53e8e14`, `37570cb` | `VoxelWorld.cs`, `Player.cs`, `Main.cs`, `DevConsole.cs`, `SpawnRecoveryTests.cs` | No transform commit before destination collision is ready; failure path leaves the player at the source |
| 3. Boat and vehicle restore | Occupied boat seat restoration, validated dismount destinations, hull-support exception scoped to saved occupied boats | `74f4257` … `4a30bee` | `BoatSystem.cs`, `VoxelWorld.cs`, `BoatBucketTests.cs` | Saved occupied boat restores only with support; aquatic dismount only when swimmable/safe |
| 4. Projectile and fluid safety | Radius-aware sweeps, deferred fluid reactions around the player, hit-face normals | `babe7a2`, `de807bd`, `c36043b`, `ef23f73`, `2cfc2a4` | `ArrowProjectile.cs`, `MobProjectile.cs`, `FluidSystem.cs`, mob/fluid tests | Sweep uses configured radius; no player-enclosing fluid reaction; suite green |
| 5. Input and runner reliability | Failure-dialog input lock, soak player transitions, test-runner registration, docs | `b7b5e7e`, `d59b8e4`, `bb44c8c`, `c2ea8c0` | `Player.cs`, `SurvivalSoakRunner.cs`, `TestRunner.cs`, docs | Failure dialog cannot leak input; soak transitions use slice 2; registered suite green |

Slices 1–3 are sequential. Slice 4 is independent after slice 1. Slice 5 comes
last. Each slice must be rebased onto current `main`, build Debug and Release
with warnings as errors, and pass the full suite before the next slice is
opened.

**Integration outcome:** attempting slice 1 on current `main` showed that the
collision-epoch commits depend on the destination-transaction and readiness
infrastructure, so they cannot compile as an isolated slice. The review boundary
was therefore moved to the whole branch: `main` was merged into it and the only
conflicts were `Chunk.cs` (staged apply transaction vs. single collider),
`VoxelWorld.cs` (dependency rebuild radius), `README.md` and `ROADMAP.md`. The
resolved tree retained the bounded transaction and multiple collision shapes,
published collision readiness only after commit, kept the 5×5 lighting rebuild
radius, and passed Debug/Release plus 202,772 checks. It landed on `main` as
`3cf241f` (integration commit `22a70c7`).
