# Release and Test-Infrastructure Baseline

**Repository:** `firasotoom85-droid/minecraft-godot`  
**Baseline date:** 2026-11-23  
**Default branch:** `main`  
**Implementation baseline commit:** `b9fea0acfd8293e2af4abbda1abb4fca0685f849`  
**Baseline CI run:** [GitHub Actions run 45169523225](https://github.com/firasotoom85-droid/minecraft-godot/actions/runs/45169523225)  
**Scope:** documentation and release/test-infrastructure readiness only. No gameplay or engine implementation was changed.

> **Frozen implementation SHA:** Every implementation worker must create its work branch from **`b9fea0acfd8293e2af4abbda1abb4fca0685f849`**. Do not substitute the tip of a documentation branch, a later `main` commit, or another worker's branch. The issue-ledger PR and this baseline PR are documentation-only descendants of this SHA and do not redefine the implementation baseline.

## 1. Repository and commit identity

The GitHub repository metadata identifies `main` as the default branch. The exact `main` ref was read from GitHub immediately before this report was prepared:

| Field | Verified value |
|---|---|
| Default branch | `main` |
| HEAD SHA | `b9fea0acfd8293e2af4abbda1abb4fca0685f849` |
| Commit subject | `Integrate PRs #81–#91: lighting channels, renderer presets and visual gates (#92)` |
| Commit timestamp | 2026-11-23 11:55:34 UTC (GitHub signature verification timestamp) |
| Signature | Verified by GitHub; reason `valid` |
| Parent commits | `2eb0eaecca97a3cc245e20d8491dde14c1c2cbea`, `c7781224abb3cadb20d59c76b21963e502a57eb6` |

The implementation SHA above is the shared base for parallel remediation work. PR #203 (issue-ledger documentation) is an unmerged documentation branch based on the same code SHA; it must not be used as a replacement worker base. This baseline report is also committed from the exact implementation SHA, not from PR #203.

## 2. Local working-tree inspection

**Local workstation working-tree state: NOT VERIFIED.**

The connected workstation was identified as the user's Linux desktop, but Desktop Commander rejected execution because its monthly command quota had been exhausted. It explicitly advised not to retry or reconnect. Consequently, I could not run `git status`, inspect local untracked files, or establish whether the user's local checkout has uncommitted changes.

No local checkout file was edited, cleaned, reset, stashed, or discarded during this task. Do not infer that the user's local tree is clean from the clean GitHub Actions checkout. Before starting local workers, each worker must inspect the checkout they will use and preserve any user work. The safe clean-validation method below uses a separate Git worktree and leaves the existing checkout untouched.

## 3. Engine, SDK and CI environment

| Component | Repository/CI requirement or observed value |
|---|---|
| Godot | **4.7.2 stable .NET/Mono**; CI downloads the `Godot_v4.7.2-stable_mono_linux_x86_64` build |
| Godot project | `config/features = PackedStringArray("4.7", "C#", "Forward Plus")` |
| Godot .NET SDK | `Godot.NET.Sdk/4.7.2` from `minecraft.csproj` |
| Target framework | `net9.0` |
| .NET SDK pin | `global.json`: SDK `9.0.0`, roll-forward `latestFeature` |
| GitHub Actions SDK selector | `actions/setup-dotnet@v4`, `dotnet-version: 9.0.x` |
| CI operating system | `ubuntu-latest` |
| CI Git | `git version 2.56.1` (from the successful baseline job log) |
| Local workstation Godot/.NET versions | Not inspected; remote command execution was unavailable |
| Exact .NET patch selected in CI | Not printed in the captured job log; the repo pin and workflow selector above are verified, but no patch-level value is asserted |

Relevant files: `global.json`, `minecraft.csproj`, `minecraft.sln`, `project.godot`, `.github/workflows/ci.yml`, `CONTRIBUTING.md`, and `scripts/verify.sh`.

## 4. Build and automated-test commands

### Documented contributor commands

`CONTRIBUTING.md` documents:

```bash
dotnet build
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 ./scripts/verify.sh
```

The verification script itself runs a Debug build and the full headless runner. Its default test timeout is 300 seconds; it accepts `GODOT_TEST_TIMEOUT` to override that value and requires the `timeout` utility. It searches for a Godot executable on `PATH` and in common installation locations, or accepts an explicit `GODOT` path.

### Exact CI commands at the frozen implementation SHA

The successful base-commit CI job `45169523225` executed the following build and test gates:

```bash
dotnet build -c Debug --nologo -warnaserror

dotnet build minecraft.csproj -c Release --nologo -warnaserror

timeout 300 godot-mono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64 \
  --headless --path . res://tests/TestRunner.tscn
```

The CI workflow also ran five headless spatial voxel-light probe scenarios, each with a 300-second timeout:

```bash
bin=godot-mono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64

timeout 300 "$bin" --headless --path . res://tests/ForestCanopyLighting.tscn -- 1337 res://artifacts/lighting-comparisons/forest-canopy.png LOW --probe-only
timeout 300 "$bin" --headless --path . res://tests/TorchCaveLighting.tscn -- 1337 res://artifacts/lighting-comparisons/torch-cave.png LOW --probe-only
timeout 300 "$bin" --headless --path . res://tests/RiverValleyLighting.tscn -- 1337 res://artifacts/lighting-comparisons/river-valley.png LOW --probe-only
timeout 300 "$bin" --headless --path . res://tests/VillageStreetLighting.tscn -- 1337 res://artifacts/lighting-comparisons/village-street.png LOW --probe-only
timeout 300 "$bin" --headless --path . res://tests/DungeonArenaLighting.tscn -- 1337 res://artifacts/lighting-comparisons/dungeon-arena.png LOW --probe-only
```

### Results on the exact implementation SHA

The base-branch Actions run was triggered by a push to `main` at the exact SHA above and completed successfully on 2026-11-23. Its single `build-and-test` job reports:

| Gate | Result |
|---|---|
| Debug .NET build with `-warnaserror` | **PASS** — 0 warnings, 0 errors |
| Release .NET build with `-warnaserror` | **PASS** — 0 warnings, 0 errors |
| Godot headless `TestRunner.tscn` | **PASS** |
| Registered test suites | **52 PASS** |
| Test checks/assertions | **200,141 checks, 0 failures** |
| Five spatial voxel-light probe grids | **PASS** — all five CI commands completed successfully |
| Overall CI job / workflow | **SUCCESS** |

The runner printed `minecraft tests: 200141 checks, 0 failures`. The current source has 52 suite registrations in `tests/TestRunner.cs` (the textual occurrence count also includes the two `RunSuite` method declarations). There is one async suite, `mob-perception`, run through `RunSuiteAsync`.

**Execution caveat:** These are verified GitHub-hosted Actions results for the exact baseline commit, not a local execution in this session. A fresh local rerun was not possible because the connected workstation command quota was exhausted. Do not describe this report as a clean local build.

## 5. Existing warnings, historical failures and flakiness

### Warnings visible in the successful baseline CI run

The successful run contains no compiler warnings or errors; both .NET builds explicitly report zero. The following runtime/runner warnings are separate from compiler diagnostics and did not fail the job:

1. **Invalid scene external-resource UIDs:** `scenes/main.tscn` lines 3–6 report four invalid `ext_resource` UIDs for `Main.cs`, `VoxelWorld.cs`, `Hud.cs`, and `Player.cs`. Godot falls back to the text `res://scripts/…` path and continues. The warnings repeat when the scene is loaded by the probe scenarios.
2. **Save-test diagnostics:** the headless test intentionally exercises an atomic write to a missing directory and a backup recovery path. It logs an atomic-write warning and a successful recovery-from-`.bak` warning; the `save` suite passes. These are expected failure-path diagnostics, not a failed save suite.
3. **ObjectDB exit warning:** one probe process reports `6 ObjectDB instances were leaked at exit`. It did not fail the spatial-probe step or overall job. Keep this visible as a baseline cleanup warning; do not assume it is resolved solely because the run passed.
4. **GitHub Actions JavaScript deprecations:** the runner logs Node `punycode` (`DEP0040`) and `url.parse()` (`DEP0169`) deprecations. GitHub also warns that `actions/cache@v4`, `actions/checkout@v4`, `actions/setup-dotnet@v4`, and `actions/upload-artifact@v4` target Node 20 and are being forced onto Node 24. These are CI action/runtime warnings, not C# compiler diagnostics.
5. **Renderer fallback documented by earlier hardware validation:** `ROADMAP.md` / `docs/PERFORMANCE_MILESTONE_3_STREAMING.md` record non-fatal `RGBFloat`-to-`RGBAFloat` fallback warnings for the campfire smoke `CurveTexture` during MX230 rendered checks. They did not prevent those captures. They are historical device-renderer observations, not a warning demonstrated by the current hosted headless probe job.

### Historical red CI before the baseline

Two workflow events (push and pull request) for the same pre-integration commit `8f289b14845090f6227979597588b634f5652a5d` failed only in the **Headless spatial voxel-light probe grids** step. The job logs show the first probe failed with:

```text
Lighting comparison 'forest-canopy' failed:
System.ArgumentException: Unknown pale-surface capture mode '--probe-only';
use baseline or contrast.
```

The Debug and Release builds in those jobs had zero compiler errors. This is a historical command/runner argument incompatibility on `8f289…`, not a failure of baseline SHA `b9fea…`. Subsequent integration commit `c7781224abb3cadb20d59c76b21963e502a57eb6` and the final `main` baseline `b9fea…` have successful CI runs, including the probe step. Do not count the older failed run as a failure of the current baseline, and do not label it a flaky test: its logged cause is a rejected argument.

### Flakiness assessment

No test failure or timeout occurred in the successful current-baseline CI run. The earlier failed probe was reproducible from an unsupported argument and is not evidence of randomness. No repeated-run local flakiness study was possible in this session. Treat flaky status as **not established**, not “none exists.”

## 6. Test discovery and how workers should add regression coverage

The supported headless entry point is `res://tests/TestRunner.tscn`, with registrations in `tests/TestRunner.cs`. It explicitly invokes named static C# test suites sequentially. Suites use the shared `TestAssert` counter and report `PASS <suite>` / `FAIL <suite>`. The runner exits non-zero when any assertion or suite fails.

`CONTRIBUTING.md` says to add a C# suite file and register it in `tests/TestRunner.cs`. The project uses the Godot .NET SDK defaults; the project file does not contain a hand-maintained test-file include list. Existing test files are grouped by subsystem (for example `VoxelStreamingTests.cs`, `SaveTests.cs`, `MobContentTests.cs`, and `FluidWorldTests.cs`). Scene-driven tests can receive the test runner node where needed; pure rule/data tests expose a static `Run()`.

**Parallel-worker coordination rule:**

- Each worker adds its regression tests in a uniquely named file such as `tests/<Subsystem>RegressionTests.cs`, with a unique static class and focused `Run()` method (or an async method only when the test genuinely requires awaiting scene-tree signals).
- Do not have multiple workers independently edit nearby lines in `tests/TestRunner.cs`. It is a shared serial registry and a predictable merge-conflict hotspot.
- Assign one integration/test-infrastructure owner to collect each worker's requested suite name and one-line registration call, then apply all registry additions in a single coordinated change. The worker PR must identify its test file, exact proposed registration line and required scene setup. Do not mark that regression as part of the merged automated gate until its registration is integrated and the headless runner confirms it executed.
- Suite names must be globally unique. Do not reset global `TestAssert.Checks` or `TestAssert.Failures` from an individual suite. Always clean temporary nodes/worlds/files in `finally` paths and allow queued nodes two process frames to be freed where applicable.
- Preserve the existing serial execution model unless a separate test-infrastructure change explicitly proves that parallel suite execution is safe. Tests using shared Godot static resources, `Settings`, `GameSession`, audio buses, worker pools or temporary `user://` data must not be run concurrently without isolation.

## 7. Available performance and soak evidence

These are existing committed measurements, not new measurements made during this baseline task. Their own report revisions and limitations must remain attached; do not silently re-label them as tests run on `b9fea…`.

| Evidence | Recorded result | Interpretation |
|---|---|---|
| Performance Milestone 2, MX230, 2026-11-21 | Three Release process runs per scenario on MX230 / i7-1065G7 / Godot 4.7.2 / Forward+ / 1280×720; median open-terrain FPS 68.9 → 75.3 (+9.4%); heavy-rain FPS 67.5 → 73.7 (+9.1%). p95 improved 9.1% / 7.1%. | Controlled comparison for that revision and LOW preset. Heavy-rain p99 rose from 16.82 ms to 19.88 ms (+18.2%); the report explicitly says to recheck this tail with longer captures. |
| Performance Milestone 3, 2026-11-21 | Benchmark matrix **54/54 passed** across nine workloads in Debug and Release. | Deterministic scenario matrix; not a cross-hardware FPS guarantee. |
| Milestone 3 survival soak | **PASS, 1,800 seconds (30 minutes)**. 10 passive / 27 hostile at finish; 891 feeds; 24 cave encounters; 0 invalid positions; save/reload and temp-world cleanup passed; peak RSS 304.1 MiB; managed heap 57.1 MiB; voxel-cache arrays 4.2 MiB vs 18.9 MiB budget; retained mesh-pool buffers 23.3 MiB; payload high-water 14/18; 0 peak orphan nodes; payload drain passed. | Headless real-main-scene survival integration soak at LOW/view distance 2, not a rendered FPS test or max-view-distance stress test. |
| Delivery validation | **PASS, 300-second soak** and **PASS, 3,600-second (60-minute) soak** on the documented integration revision. The 60-minute run reports zero invalid positions, save/reload and temporary-world cleanup true, no runtime errors/warnings/leaked objects/RIDs, and RSS peaking near 251 MB then returning to about 158 MB after GC. | Historical delivery evidence; its report does not identify the exact current `b9fea…` SHA, so do not attribute it to this baseline commit. |
| Current baseline CI | Percentile-statistic unit tests passed inside `performance-benchmark`; CI did not launch the nine real rendered benchmark scenarios or a new long-duration soak. | Correctness suite only; no current-baseline FPS or new soak claim. |

The supported opt-in tools are:

- `tests/PerformanceRunner.tscn` — real-scene, seeded benchmark scenarios. Full instructions and measurement caveats are in `docs/PERFORMANCE_BENCHMARKS.md`. Run scenarios separately, keep engine/renderer/resolution/preset/view distance/seed fixed, use at least three runs, and compare medians. Headless output is not a rendered FPS baseline.
- `tests/SurvivalSoakRunner.tscn` via `scripts/soak-survival.sh` — real-main-scene headless survival soak, default 3,600 seconds. It creates a private temporary world and deletes it at successful completion; do not repurpose a user world for soak tests.
- Five spatial light probe fixtures — included in CI and run headlessly as shown above.

## 8. Clean local validation without disturbing existing user work

First inspect the checkout you intend to use. Do not run `reset --hard`, `clean -fdx`, or overwrite/stash user files to force a clean tree.

```bash
git status --short --branch
git rev-parse HEAD
git fetch origin
git rev-parse origin/main
```

If the existing checkout is dirty or its current branch is needed, leave it alone and create a separate detached worktree pinned to the frozen implementation SHA:

```bash
git worktree add --detach ../minecraft-godot-baseline b9fea0acfd8293e2af4abbda1abb4fca0685f849
cd ../minecraft-godot-baseline

git status --short --branch
git rev-parse HEAD
dotnet --version
godot --version
```

Confirm that the versions are compatible with `global.json`, `minecraft.csproj`, and the Godot 4.7.2 .NET requirement. Then run:

```bash
dotnet build -c Debug --nologo -warnaserror
dotnet build minecraft.csproj -c Release --nologo -warnaserror

GODOT=/absolute/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 \
GODOT_TEST_TIMEOUT=300 ./scripts/verify.sh
```

The verification script performs another Debug build and runs the full headless `TestRunner.tscn` with a bounded timeout. For an exact standalone CI-style test invocation after building:

```bash
timeout 300 "$GODOT" --headless --path . res://tests/TestRunner.tscn
```

Optional real-scene soak (choose a deliberate duration; the script accepts 60–86,400 seconds):

```bash
GODOT=/absolute/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 \
  ./scripts/soak-survival.sh 1800
```

For performance comparison, follow `docs/PERFORMANCE_BENCHMARKS.md`; use separate Release runs and record the commit SHA with `--commit="$(git rev-parse HEAD)"`. Do not compare headless frame rates to rendered MX230/RTX results.

Before opening a worker PR, record:
- the worker branch parent SHA (must be the frozen implementation SHA unless the coordinator explicitly authorizes a rebased integration base);
- `git status --short` and confirmation no user changes were lost;
- Debug/Release results and the exact test-runner check count;
- the new regression suite name and confirmation it appears as `PASS` in the runner output;
- any new warning/failure separately from the pre-existing warnings in §5.

## 9. Implementation-worker baseline contract

1. Start each independent implementation branch from `b9fea0acfd8293e2af4abbda1abb4fca0685f849`.
2. Treat ledger and baseline documentation commits as planning records only; they do not change the implementation SHA.
3. Do not merge another worker branch into your task branch. The integration owner controls dependency order and conflict resolution.
4. Keep gameplay, generator, save, renderer, mesher and streaming changes scoped to assigned issues. Preserve append-only persisted IDs, generator-version compatibility, the existing mutation architecture, greedy meshing and chunk streaming.
5. Do not claim a fix is verified until its regression suite is registered, executed and the relevant build/test gates pass.

## 10. Source references

- `CONTRIBUTING.md` — requirements, documented build and verification commands, compatibility rules and test registration procedure.
- `.github/workflows/ci.yml` — CI build, headless test and spatial probe commands.
- `.github/workflows/survival-soak.yml` — opt-in soak workflow.
- `global.json`, `minecraft.csproj`, `minecraft.sln`, `project.godot` — SDK/engine/project configuration.
- `scripts/verify.sh`, `scripts/soak-survival.sh` — local correctness and soak entry points.
- `tests/TestRunner.cs`, `tests/TestRunner.tscn` — explicit headless test discovery and execution.
- `docs/PERFORMANCE_BENCHMARKS.md`, `docs/PERFORMANCE_MILESTONE_2_MX230.md`, `docs/PERFORMANCE_MILESTONE_3_STREAMING.md`, `ROADMAP.md`, `PLAN.md` — available measurements and historical validation.

---

**Baseline disposition:** GitHub CI is green on the frozen implementation SHA. A local working-tree check and local build/test rerun are still outstanding because local command execution was unavailable; verify those on a separate worktree before treating a developer checkout as clean.
