# Remediation issue ledger

**Repository:** `firasotoom85-droid/minecraft-godot`  
**Refreshed:** 2026-11-23  
**Default branch audited:** `main`  
**Audited head:** `b9fea0acfd8293e2af4abbda1abb4fca0685f849`  
**Active baseline:** 110 open issues, #93–#202 inclusive

## Scope and verification record

- The active set was fetched from the repository Issues REST endpoint with `state=open`, `per_page=100`, and explicit pagination through pages 1 and 2. The pages returned 100 and 10 issues respectively; the resulting issue numbers are the contiguous range #93–#202.
- Every active issue description was read in full. The GitHub issue metadata reports zero comments on 109 issues; #149 has one comment, which was read in full and incorporated below.
- Repository metadata identifies `main` as the default branch. Source review was performed against `main` at `b9fea0acfd8293e2af4abbda1abb4fca0685f849`. The issue-specific confidence statements below preserve whether a finding is static, suspected, or requires runtime reproduction; static analysis is not represented as runtime verification.
- No application code, issue title/body/labels/assignees/state, or save data was changed for this ledger task. No issue was closed or reopened.
- The recursive `main` tree contains no `docs/remediation/ISSUE_LEDGER.md`; no prior ledger/snapshot was found on the default branch or a `remediation` branch. An exact numeric delta against a prior issue list is therefore unavailable. This refreshed snapshot is the new baseline.
- The supplied task refers to W01–W10 assignment tables in a prompt pack, but those tables were not present in the task text or repository. The owners below are therefore an explicit **provisional subsystem allocation**, not a claim that an unavailable table was followed. Reconcile this map with the prompt-pack table before dispatching workers. Every active issue is assigned exactly once in this ledger; GitHub assignees remain unchanged (none set).

## Baseline validation

| Check | Result |
|---|---:|
| Open issues fetched | 110 |
| Unique issue numbers | 110 |
| Range | #93–#202 |
| Issues with exactly one provisional worker owner | 110 |
| Issues missing or multiply assigned in ledger map | 0 |
| Closed issues included as active fixes | 0 |
| GitHub issue-state/assignee mutations | 0 |

## Provisional worker lanes

| Worker | Primary subsystem | Open issues |
|---|---|---:|
| W01 | Chunk streaming, scheduling, Far LOD and worker lifecycle | 13 |
| W02 | Meshing, voxel rendering, lighting surfaces and collision | 17 |
| W03 | World generation, seeds, terrain caches and spawn | 13 |
| W04 | Inventory, item rewards, crafting and quest acquisition | 9 |
| W05 | Save/load, world manifests, backups and filesystem safety | 23 |
| W06 | Player interaction, placement, combat and damage | 6 |
| W07 | Mobs, fluids, crops, villages and ecology | 10 |
| W08 | UI, input, settings, accessibility and focus | 7 |
| W09 | Audio systems and scene-scoped audio lifecycle | 4 |
| W10 | Cross-cutting performance, diagnostics, automated tests and documentation | 8 |

## Duplicate and overlap disposition

| Issue pair | Classification | Evidence-based disposition |
|---|---|---|
| #105 / #125 | **True duplicate** | Both describe the same ordinary mined-block drop path bypassing `Player.RecordGathered()` / `ItemGathered`, preventing Gather quest progress. One shared implementation and common tests can satisfy both; retain both GitHub numbers and do not close either in this ledger PR. |
| #109 / #128 | **Partial overlap** | Both cover player inventory, container and furnace deserialization validation. #109 explicitly includes stack limits and item-specific durability/enchantment metadata; #128 adds duplicate slot records and explicit `ItemId.None`/undefined-ID handling. A shared validator can underpin both, but preserve the distinct acceptance cases. |
| #104 / #118 | **Partial overlap** | Both cover discarded inventory overflow from mining. #104 also calls out special `GiveDrop()` paths; #118 expands the scope to mob loot and persistent recoverable drops. A common reward-delivery/pickup path can address the shared defect while keeping mob-loot-specific tests. |
| #171 / #173 | **True duplicate** | Both identify the same `Sound.Setup()` registration of scene-owned low-pass filters into global audio buses without removing those exact effects at teardown. Treat as one technical fix with both issue IDs tracked; do not close either here. |

Other tracked relationships are not duplicates: #199/#201 cover non-blocking shutdown orchestration versus cancellation inside active chunk meshing; #94/#147/#202 concern distinct Far LOD stale-payload, exception-cleanup and evicted-tile queue-progress paths; #115/#117 concern recursive delete versus copy symlink safety; #162/#164 concern focus entry versus focus restoration.

## Active issue ledger

> Priority mapping for planning: High = P1, Moderate = P2, Low = P3. Severity is copied from the issue description. `PR/commit`, final remediation verification and closure remain pending until a worker implements and validates a fix.

### #93 — Bug: Greedy meshing mode changes during a build can omit or duplicate opaque geometry

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The build consumes mutable global configuration repeatedly instead of capturing an immutable per-build configuration. The payload carries an edit epoch but no meshing configuration/version, so `ApplyBuild()` cannot reject a payload built under an obsolete mode. The rebuild operation also targets only currently installed chunks. **Issue confidence:** Confirmed by static analysis; not yet reproduced at runtime. The interleaving and divergent branches are present in the source, but an automated/runtime reproduction should be added with the fix.
- **Evidence locations:** - File: `scripts/Mesher.cs`; - Methods: `Build()`, `BuildOpaqueGreedy()`; - Lines: 230–267 and 317–373; - Related setting change: `scripts/DevConsole.cs`, command handler, lines 232–240; - Related payload validation/rebuild: `scripts/VoxelWorld.cs`, `ApplyBuild()` and `RebuildAll()`, approximately lines 2579–2614 and 2847–2854
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #95 and #96 (meshing/streaming build consistency).
- **Required implementation:** Capture `Mesher.Greedy` once at the start of `Build()` and use that immutable local value for the entire build, including all helpers and per-face decisions. Also maintain a meshing-configuration revision that changes when the setting changes; attach it to each payload and reject/requeue payloads whose revision is stale. Ensure in-flight fresh chunks are covered by the new revision/rebuild flow. Keep the existing greedy and per-face meshing implementations rather than replacing either path.
- **Required regression test:** - Add a deterministic worker/build test that pauses after the pre-pass decision, toggles greedy mode, and then resumes the build.
- Verify false-to-true and true-to-false transitions cannot omit or duplicate opaque faces.
- Verify a fresh chunk already being built when the mode changes is eventually applied using the new mode.
- Verify payloads built with an old meshing revision are rejected and rebuilt.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #94 — Bug: Far LOD applies tile payloads built with an obsolete streamed-area skip mask

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The asynchronous task captures a value-type snapshot of `skip`, but the task/payload has no revision token. The in-flight deduplication prevents scheduling a replacement while the old task is active, and completion does not detect that the requested state changed. **Issue confidence:** Confirmed by static analysis; not yet reproduced at runtime. The stale-state path is visible in the update, deduplication, and completion flow.
- **Evidence locations:** - File: `scripts/FarLod.cs`; - Methods: `UpdateAround()`, `Enqueue()`, `Dispatch()`, `BuildTask()`, `ApplyTile()`; - Lines: 85–145, 193–232, and 393–433
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #147 and #202 (Far LOD task lifecycle and stale payload handling).
- **Required implementation:** Add a monotonically increasing revision to each tile (or an equivalent immutable build token). Capture the revision and skip rectangle when dispatching, carry the token through completion, and apply the payload only if the tile still exists with the same revision. If a completion is stale, discard it and enqueue exactly one build for the tile's latest state. A tile removed and recreated at the same coordinate must receive a distinct generation token.
- **Required regression test:** - Use a controllable worker completion to change a tile's skip rectangle while its build is in flight.
- Assert that the old payload is not applied and that a build for the new skip rectangle is dispatched after completion.
- Cover tile removal/recreation at the same coordinate while an older task is running.
- Add a movement regression that advances the streamed rectangle across a far-LOD tile boundary and checks for neither missing nor overlapping coverage.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #95 — Bug: Chunk streaming can schedule duplicate builds for the same in-flight coordinate

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** Queue membership is being used as a proxy for work ownership. Dequeue removes that membership before the asynchronous build finishes, and no in-flight state or dirty-after-completion mechanism replaces it. **Issue confidence:** Confirmed by static analysis; not yet reproduced at runtime. The queue/state design permits this interleaving. Runtime frequency and the amount of duplicate application should be measured with a deterministic test.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `WorkerLoop()`, `RebuildQueue()`, `Enqueue()`, `EnqueueRebuild()`, `ApplyBuild()`; - Lines: 2504–2525, 2783–2807, 2822–2845, and 2579–2614
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #97, #196, #197 and #198 (chunk queue ownership, pruning and priority).
- **Required implementation:** Track active work per coordinate separately from pending queue membership. Preserve whether the active request is fresh or a rebuild, and record a dirty/rebuild-requested flag if another request arrives while it is active. On completion, clear the active state and enqueue at most one follow-up using the latest required state. Ensure failure/cancellation paths also clear active state and preserve pending intent.
- **Required regression test:** - Add a deterministic worker test that pauses after dequeuing C, requests another fresh/rebuild job for C, and then resumes the first worker.
- Assert that only one build for C is active at once.
- Assert that a request arriving during the build causes exactly one follow-up build after completion.
- Verify a fresh chunk is installed once and that multiple payload applications do not occur for the same unchanged snapshot.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #96 — Performance: Global EditEpoch invalidates chunk builds unrelated to the edited area

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** The epoch is global rather than spatially scoped. The mesher already snapshots padded chunk and neighbor data, while `SetBlock()` schedules local affected chunks; however, payload validation does not track versions for those actual dependencies. **Issue confidence:** Confirmed by static analysis; not yet reproduced with a workload benchmark. The global comparison is explicit. The actual cost depends on edit frequency and concurrent build volume.
- **Evidence locations:** - File: `scripts/Mesher.cs`, method `Build()`, lines 230–247; - File: `scripts/VoxelWorld.cs`, method `SetBlock()`, lines 2060–2184 (epoch increment and local neighbor rebuild scheduling); - File: `scripts/VoxelWorld.cs`, method `ApplyBuild()`, lines 2585–2594
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #93, #146 and #176 (meshing invalidation/lock scope and rebuild breadth).
- **Required implementation:** Replace the all-world invalidation check with dependency-scoped version validation. Capture revisions for the target chunk and the neighboring block/light data actually sampled by the mesher, then validate those revisions before applying the payload. Preserve invalidation for every affected chunk, including lighting dependencies; do not simply remove epoch validation.
- **Required regression test:** - Start builds for two distant chunks, edit one chunk, and assert that the unaffected payload remains valid and is applied without a redundant rebuild.
- Edit a block on a chunk boundary and assert that every dependent neighbor payload is rejected/rebuilt.
- Cover edits that change light opacity/emission so light-dependent payloads remain correctly invalidated.
- Add a stress benchmark with repeated local edits while distant chunks stream, recording discarded payload count and total mesh-build time.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #97 — Bug: Chunk worker exceptions discard dequeued requests without retrying them

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The worker's exception path has resource cleanup but no request-state recovery. Queue membership is removed before execution, and there is no retry counter, failure queue, or completion path that restores the failed request. **Issue confidence:** Confirmed by static analysis; not yet reproduced by injecting a worker exception. The missing retry is explicit in the catch/finally path.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `WorkerLoop()`; - Lines: 2504–2571 (dequeue, build, exception handling, and cleanup)
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #95, #147 and #202 (worker completion bookkeeping).
- **Required implementation:** Retain the request intent and add bounded per-coordinate retries with a small backoff. On a transient failure, requeue the same coordinate as fresh or rebuild as appropriate. After the retry limit, expose a clear failed state/diagnostic rather than retrying indefinitely. Ensure cancellation during shutdown is not treated as a normal build failure, and preserve current payload/semaphore cleanup.
- **Required regression test:** - Inject a one-time worker build exception and verify that the same coordinate is retried and eventually applied.
- Inject repeated failures and verify retry limits/backoff prevent a hot retry loop.
- Verify fresh-build and rebuild requests retain their correct intent across retries.
- Verify cancellation during scene shutdown does not schedule new work and that payload-slot reservations are still balanced.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #98 — [Worldgen] Reject unsupported generator versions when loading local worlds

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** Generator-version validation is implemented at the archive boundary rather than at the authoritative world-loading boundary. The generation dispatch uses lower-bound checks such as `_genVersion >= WorldGenV8.Version`, so an unknown future version is interpreted as the latest known implementation. **Issue confidence:** Confirmed by static analysis. The unsupported-version path and its generation dispatch are directly visible in the code; runtime reproduction was not performed.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `ResolveWorld()`; - Lines: `280–300`; - Related validation: `scripts/WorldArchive.cs`, `ValidateManifestMetadata()`, lines `376–381`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #99 and #195 (generator metadata compatibility/fallback).
- **Required implementation:** Add a shared manifest validation step in the normal world-loading path before assigning `_genVersion` or allowing `GenerateChunkData()` to run. Reject versions below 1 and above `WorldStore.CurrentGenVersion`. Reuse the same validation rule as archive import. Do not rewrite the manifest, downgrade the version, or generate replacement chunks.
- **Required regression test:** - Add a local-world loading test with `GenVersion = WorldStore.CurrentGenVersion + 1`.
- Assert loading fails before `ChunkData()` can generate a chunk.
- Assert the manifest bytes and existing save files remain unchanged.
- Test the minimum supported version and the current version still load successfully.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #99 — [Worldgen] Do not silently assign generator V1 when modern-world metadata is missing

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** The manifest fallback uses the absence of readable metadata as proof of legacy-world identity. It does not inspect whether modern save files or other world data already exist before assigning the legacy generator. **Issue confidence:** Confirmed by static analysis. The fallback behavior is explicit. The triggering condition requires all manifest candidates to be unavailable; runtime reproduction was not performed.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `ResolveWorld()`; - Lines: `280–300`; - File: `scripts/Worlds.cs`; - Method: `DefaultFor()`; - Lines: `306–315`; - Related method: `ReadJson<T>()`, lines `472–497`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #98, #112 and #195 (generator selection and manifest recovery).
- **Required implementation:** Before calling `DefaultFor()`, distinguish a genuinely legacy untouched directory from a directory containing existing world or player saves. Preserve the V1 migration path for known legacy data, but do not infer V1 for directories containing modern saves without metadata. Return a recoverable error state and preserve all files when generator identity cannot be established.
- **Required regression test:** - Create a modern world with a known generator version and save files, then remove or corrupt all manifest candidates in a test fixture.
- Assert the world is not loaded as V1 and no replacement manifest is written.
- Assert the existing `world.json`, `player.json`, and backups remain byte-identical.
- Retain a test proving a genuine pre-worlds legacy save still migrates to V1.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #100 — [Worldgen] Prevent campfire stamps from overwriting generated structures

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** Feature placement has an implicit order-based priority rather than a shared occupancy or conflict-resolution policy. Village placement explicitly checks nearby campfire sites, but the reverse exclusion is not applied to campfires and surface structures. **Issue confidence:** Suspected. The overwrite behavior and missing exclusion are confirmed by static analysis, but an affected seed and actual damaged structure were not reproduced during this read-only audit. Verify with a deterministic seed-level test before implementing a behavior change.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `GenerateChunkData()` campfire stamping section; - Lines: `1189–1228`; - Method: `GetCampfireSite()`; - Lines: `739–807`; - Method: `StampStructures()`; - Lines: `1487–1523`; - Related comparison: `scripts/WorldGenV8.cs`, `GetVillageSite()`, lines `547–575`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #188 (generated terrain/structure stamp ordering).
- **Required implementation:** Add deterministic campfire-versus-structure conflict resolution. Prefer rejecting a campfire site that overlaps a structure footprint, or use a shared occupancy predicate so feature precedence is independent of chunk generation order. Preserve existing generator output for historical versions; any change to generated output must be introduced behind a new generator version.
- **Required regression test:** - Add a deterministic seed fixture with a campfire site overlapping a surface structure, once such a seed is identified.
- Verify the structure template's expected non-air blocks remain unchanged after full chunk generation.
- Generate the affected chunks in both orders and compare their voxel arrays.
- Test overlap at positive and negative chunk boundaries.
- Verify non-overlapping campfires retain their existing output.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #101 — [Worldgen] Deduplicate concurrent generation requests for the same uncached chunk

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** The cache lookup and generation are not coordinated by a per-chunk in-flight reservation. The lock protects dictionary access and insertion but deliberately does not cover the expensive generation operation, leaving a cache-miss race. **Issue confidence:** Confirmed by static analysis. The check-generate-insert race is explicit; runtime contention and allocation impact were not measured.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `ChunkData(Vector2I cp)`; - Lines: `1553–1578`; - Related method: `GenerateChunkData(Vector2I cp)`, beginning at line `1052`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #95 (concurrent chunk-work deduplication).
- **Required implementation:** Introduce a per-chunk in-flight generation entry, such as a `Lazy<byte[]>`, task, or dedicated reservation object. The first caller owns generation and all other callers reuse its result. Remove failed in-flight entries safely so exceptions do not permanently poison the cache. Do not hold the global `_mutex` throughout generation, because unrelated chunks must remain parallel.
- **Required regression test:** - Add a controlled concurrent test that requests the same uncached chunk from multiple tasks.
- Instrument or inject a generation counter and assert the generation body runs once for that coordinate.
- Assert all callers receive the same cached array reference and byte-identical contents.
- Request multiple different chunks concurrently and assert they can still generate in parallel.
- Cover generation exceptions and verify a later retry can succeed.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #102 — [Worldgen] Avoid recomputing the full surface sample for known-height biome queries

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** The optimization contract in `VoxelWorld.BiomeAt(x, z, h)` is not propagated into the versioned generator APIs. The generator wrappers discard the known height and recompute the entire surface sample. **Issue confidence:** Confirmed by static analysis for redundant sampling. The amount of real-world performance impact has not been benchmarked.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `BiomeAt(int x, int z, int h)`; - Lines: `444–455`; - File: `scripts/WorldGenV7.cs`; - Method: `BiomeAt(int x, int z, int height, int seaLevel)`; - Lines: `186–187`; - File: `scripts/WorldGenV8.cs`; - Method: `BiomeAt(int x, int z, int height, int seaLevel)`; - Lines: `181–182`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #103 (world-generation coordinate/sampling behavior).
- **Required implementation:** Refactor the V7/V8 sampling API so a single `SurfaceSample` or climate result can provide both height and biome. Preserve exact version-specific output for normal callers. Avoid introducing a second noise sample merely to classify a column whose sample is already available.
- **Required regression test:** - Add tests verifying `BiomeAt(x, z, knownHeight)` classifies using the supplied height where the API contract requires it.
- Add a sampling counter or focused benchmark demonstrating that a known-height query does not call the full `SampleSurface()` path again.
- Compare biome outputs against existing golden samples for V7 and V8 to ensure no unintended generator changes.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #103 — [Worldgen] Define a safe horizontal coordinate range or rebase large worlds

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** Integer world-coordinate calculations are converted into single-precision inputs for noise sampling and global transforms. Single-precision spacing increases with coordinate magnitude, so block-scale distinctions eventually disappear. **Issue confidence:** Suspected as a product-level defect. The floating-point precision limit is mathematically established, but the project does not state whether such extreme coordinates are within its supported gameplay range, and runtime impact was not reproduced.
- **Evidence locations:** - File: `scripts/WorldGenV7.cs`; - Methods: `CreateNoise()`, `ClimateAt()`, `SampleSurface()`; - Lines: `67–81`, `102–169`, `172–184`; - File: `scripts/WorldGenV8.cs`; - Methods: `CreateNoise()`, `ClimateAt()`, `SampleSurface()`; - Lines: `73–110`, `112–180`; - File: `scripts/Chunk.cs`; - Method: `Setup()`; - Lines: `34–40`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** First define and document the supported horizontal world extent. If exploration is intended to extend beyond single-precision block resolution, introduce world-origin rebasing and keep rendering/physics transforms chunk-relative. Ensure deterministic noise sampling remains based on stable global coordinates and preserve existing generator outputs within the current supported range.
- **Required regression test:** - Add precision tests at coordinates around `2^23`, `2^24`, `2^25`, and their negative counterparts.
- Verify adjacent world columns produce distinguishable coordinate inputs where the supported range requires it.
- Verify chunk-local transforms preserve block-scale movement and ray selection after rebasing, if implemented.
- Verify existing generator golden hashes remain unchanged within the currently supported range.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #104 — [Blocks] Preserve mining drops when the inventory is full

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** The mining reward path treats the return value of `Inventory.Add()` as a notification condition rather than an ownership transfer that must be completed. There is no fallback owner for the unaccepted quantity.

`GiveDrop()` has the same omission, so fixing only the ordinary `BlockDef` drop branch would leave special drops exposed. **Issue confidence:** Confirmed by static analysis. Not reproduced at runtime during this audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Method: `FinishBreak()`; - Lines: `1462–1542`; - Related method: `GiveDrop()`; - Lines: `1555–1566`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Partially overlaps #118: both cover mining overflow; #118 additionally covers mob loot.
- **Required implementation:** Route all block and special drops through one reward-delivery helper. The helper should retain the unaccepted amount in a recoverable world pickup/storage system, use the existing world ownership and mutation boundaries, and define save/load and chunk-unload behavior. Avoid creating one physics node per item when stack merging or chunk-local pickup storage can preserve the same behavior.
- **Required regression test:** 1. Mine a block with a completely full inventory and verify the entire drop remains recoverable.
2. Repeat with an inventory that has space for only part of the drop.
3. Exercise a special drop routed through `GiveDrop()`.
4. Save, reload, and stream the pickup's chunk out and back in; verify no item is lost or duplicated.
5. Verify successful in-inventory collection still reports only the quantity actually accepted.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #105 — [Quests] Report ordinary mined block drops through ItemGathered

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** Reward delivery and gather-event reporting are implemented in different paths. `GiveDrop()` computes the accepted quantity and invokes `RecordGathered()`, but `FinishBreak()` duplicates the inventory-add operation instead of using that reporting path.

`QuestSystem.Setup()` subscribes to `ItemGathered`; it does not monitor inventory changes or infer acquisition from inventory contents. **Issue confidence:** Confirmed by static analysis. Not reproduced at runtime during this audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Method: `FinishBreak()`; - Lines: `1528–1542`; - Related event helper: `RecordGathered()`; - File: `scripts/QuestSystem.cs`; - Method: `Setup()`; - Line: `83`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** True duplicate of #125: same ordinary mined-drop ItemGathered omission and Gather-quest consequence; preserve both issue numbers.
- **Required implementation:** Use one shared reward-delivery helper for ordinary block drops and special drops. It must report only the quantity actually accepted by the inventory or otherwise successfully collected; do not count discarded overflow as gathered. Preserve current harvest-tier and drop-count rules.
- **Required regression test:** 1. Add an integration test that accepts the Blacksmith quest, mines Iron Ore through the real player mining path, and verifies the gather stage advances by the accepted Raw Iron count.
2. Verify an insufficient-tier pickaxe does not report Raw Iron.
3. Verify a full inventory does not report items that were not collected.
4. Verify special drops and ordinary block drops report the same event semantics.
5. Verify a single mined drop cannot emit duplicate gather progress.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #106 — [Interaction] Use voxel-aware targeting for Pick Block on non-colliding blocks

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Player interaction, placement, combat and damage.
- **Verified root cause / investigation status:** The game has two targeting models: physics-ray collision and the custom voxel ray used by mining/placement. Pick Block relies exclusively on the former even though the mesher intentionally excludes collision for cross plants.

This is an interaction-targeting mismatch, not a reason to add colliders to every plant. **Issue confidence:** Confirmed by static analysis. Not reproduced at runtime during this audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Method: `PickBlock()`; - Lines: `1946–1980`; - Related method: `TryGetTargetBlock()`; - File: `scripts/Mesher.cs`; - Cross-plant meshing branch: approximately lines `281–294`; - File: `scripts/BlockDb.cs`; - Method: `IsCross()`; - Lines: `533–536`
- **Primary worker owner:** W06 — Player interaction, placement, combat and damage. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #143 (non-colliding voxel targeting vs targeting into unloaded chunks).
- **Required implementation:** Use the existing voxel-aware target acquisition for Pick Block rather than requiring `Ray.IsColliding()`. Preserve the current exclusions for Air, fluids, Bedrock, and blocks without a corresponding item. Do not add collision geometry to cross plants merely to make Pick Block work.
- **Required regression test:** 1. Verify Pick Block selects TallGrass, Torch, flowers, wheat stages, and RiverReeds when each is the nearest voxel target.
2. Verify a solid block behind a cross plant is not selected instead.
3. Verify existing cube and partial-shape targets still select their matching item.
4. Verify Air, Bedrock, water, and unmapped blocks retain their intended behavior.
5. Verify hotbar selection and backpack-to-hotbar swapping continue to raise the inventory change notification.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #107 — [Farming] Stop wheat growth when its farmland support is removed

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** Crop registration tracks wheat positions only. The growth system validates the crop ID and upper clearance but omits the support predicate that is checked during initial planting. A mutation to the supporting voxel does not invalidate or otherwise update the crop above it. **Issue confidence:** Confirmed by static analysis. Not reproduced at runtime during this audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Method: `PlantSeedsAt()`; - Lines: `1236–1258`; - File: `scripts/CropSystem.cs`; - Method: `_Process()`; - Lines: `39–64`; - File: `scripts/VoxelWorld.cs`; - Methods: `UpdateCropSource()`, `CropSnapshot()`; - Lines: `2294–2308`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #127 (crop support/growth vs generated-village crop registration).
- **Required implementation:** Add a support validation to the crop growth lifecycle and define how unsupported crops are handled. If support changes should update crops immediately, use the existing `BlockChanged` notification or a targeted neighbor check rather than scanning the entire world. Apply crop removal through `VoxelWorld.SetBlock()` so persistence, lighting, and rebuild notifications remain coherent.
- **Required regression test:** 1. Plant wheat on Farmland and verify normal growth remains possible.
2. Replace the supporting Farmland with Air and verify the crop does not grow.
3. Replace the supporting Farmland with a non-farmland block and verify the same behavior.
4. Verify the selected unsupported-crop removal/drop policy.
5. Verify crop registration is cleaned up after the crop is removed.
6. Verify normal crop growth and save/load behavior remain unchanged when support is valid.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #108 — [Blocks] Reject undefined BlockId values in VoxelWorld.SetBlock

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** `SetBlock()` normalizes the shape state and checks world bounds and Bedrock, but omits `BlockDb.IsDefined(id)`. The lower-level `Chunk.SetBlockLocal()` also accepts any enum value and writes its byte representation.

The world-save loader already validates saved block IDs, but the live mutation boundary does not enforce the same registry invariant. **Issue confidence:** Confirmed by static analysis. Not reproduced at runtime during this audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `SetBlock(int x, int y, int z, BlockId id, byte state)`; - Lines: `2060–2099`; - File: `scripts/Chunk.cs`; - Method: `SetBlockLocal()`; - Lines: `88–102`; - File: `scripts/BlockDb.cs`; - Methods: `IsDefined()`, `DefOf()`; - Lines: `294–296`; - File: `scripts/Mesher.cs`; - Block lookup path: approximately lines `303–320`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Reject undefined IDs at the start of `VoxelWorld.SetBlock()`, before state normalization or any mutation side effect. Make the lower-level chunk mutation method internal/private to the mutation owner or independently enforce the same invariant. Preserve the existing valid ID values and append-only persisted-ID contract; do not renumber or reuse IDs.
- **Required regression test:** 1. Verify `SetBlock()` rejects the first undefined ID (currently 61).
2. Verify it rejects the largest byte-backed enum value.
3. Verify rejection leaves block data, block states, edit dictionaries, dirty state, edit epoch, lighting, and `BlockChanged` notifications unchanged.
4. Verify every currently defined `BlockId` remains accepted where world bounds permit.
5. Verify valid block edits still rebuild the affected chunk and its neighbors.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #109 — [Inventory Saves] Validate item IDs and stack metadata when loading saved inventories

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** The loaders validate only slot bounds and `Count > 0`. They do not consult item registry membership or `ItemDb.MaxStack()`, and they do not validate item-specific durability/enchantment constraints before constructing the stack. **Issue confidence:** Confirmed by static analysis. Malformed-save runtime behavior was not reproduced during this audit.
- **Evidence locations:** - File: `scripts/Inventory.cs`; - Method: `LoadFrom(List<ItemStackSave>? items)`; - Lines: `301–321`; - File: `scripts/Container.cs`; - Method: `LoadFrom(List<ItemStackSave>? items)`; - Lines: `48–62`; - File: `scripts/FurnaceData.cs`; - Method: `LoadFrom(List<ItemStackSave>? items)`; - Lines: `157–180`; - Related registry: `scripts/ItemDb.cs`, `MaxStack()`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Partially overlaps #128: shared inventory/container/furnace stack validation; #128 adds duplicate-slot and None/undefined-ID cases.
- **Required implementation:** Create one canonical saved-stack validation routine shared by player inventory, containers, and furnace slots. Validate item registration, positive count, maximum stack size, and item-specific durability/enchantment bounds. Define how invalid records are reported and recovered. Preserve valid legacy saves and avoid rewriting or discarding original save data merely because a newer/older content registry encounters an unknown item.
- **Required regression test:** 1. Reject or recover from an undefined item ID in player inventory.
2. Repeat for chest contents and each furnace slot.
3. Verify a stack above `ItemDb.MaxStack(id)` is not admitted.
4. Verify non-stackable tools, weapons, armor, and buckets cannot load with count greater than one.
5. Verify invalid durability/enchantment values are handled safely.
6. Verify valid current and legacy player/container/furnace saves still round-trip without changing item IDs or slot ownership.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #110 — [Save Compatibility] Refuse future world-save versions before loading or saving

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The load path treats a version warning as non-fatal, while the save path unconditionally stamps every serialized model with the current format version. There is no read-only compatibility state or guard preventing an older executable from saving a future-version world. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/VoxelWorld.cs; - Method: LoadSave(); - Lines: 3172–3177; - File: scripts/VoxelWorld.cs; - Method: SaveNow(); - Lines: 2998–3011; - File: scripts/Worlds.cs; - Type/constant: WorldSave / WorldFormatVersion; - Lines: 29–43, 77–79; ; [Load path](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3172-L3177) · [Save path](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L2998-L3011)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #98 (world-save version vs generator-version compatibility).
- **Required implementation:** Reject unsupported future versions during world validation, before loading editable state. Carry an explicit compatibility/read-only result through world selection and prevent all save entry points from replacing unsupported files. Keep the current V1–V4 backward-compatibility behavior unchanged.
- **Required regression test:** - Load a V5 fixture containing an unknown field and verify the world is marked unsupported/read-only.
- Invoke autosave, manual save, focus-loss save, and quit-save paths and verify the primary file and both backups retain their original hashes.
- Verify V1–V4 fixtures continue loading according to the supported compatibility policy.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #111 — [Persistence] Preserve unknown block IDs instead of dropping saved edits

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The loader combines validation with destructive filtering. It has no preservation store for unknown block IDs and no world-level flag preventing a save after unsupported content is encountered. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/VoxelWorld.cs; - Method: LoadSave(); - Lines: 3087–3107; - File: scripts/VoxelWorld.cs; - Method: SaveNow(); - Lines: 2949–2953; ; [Load validation](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3087-L3107) · [Edit serialization](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L2949-L2953)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #113 and #114 (save validation/recovery).
- **Required implementation:** Track unsupported block edits separately using their raw persisted numeric IDs and coordinates, or mark the entire world read-only when any unsupported ID is found. Do not renumber, remap, or reuse persisted IDs. Preserve append-only ID semantics.
- **Required regression test:** - Deserialize a world fixture containing a known block ID and an unknown block ID.
- Verify the unknown edit is preserved through a save, or verify all save entry points refuse to write.
- Verify a world containing only known IDs still loads and saves normally.
- Verify existing persisted-ID golden tests remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #112 — [World Recovery] Do not silently replace a selected world when its manifest is unreadable

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The selection logic conflates an invalid selected-world manifest with the absence of a selected world. The manifest reader returns null for both, and the caller does not retain the selected directory plus its recovery/error state. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/GameSession.cs; - Method: EnsureWorld(); - Lines: 34–56; - File: scripts/Worlds.cs; - Method: ReadManifest(); - Lines: 123–124; - File: scripts/Worlds.cs; - Method: DefaultFor(); - Lines: 306–316; - File: scripts/VoxelWorld.cs; - Method: ResolveWorld(); - Lines: 280–299; ; [EnsureWorld](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/GameSession.cs#L34-L56) · [DefaultFor](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Worlds.cs#L306-L316)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #99 and #195 (manifest fallback and generator seed/version compatibility).
- **Required implementation:** Represent world selection and manifest loading as explicit states (valid, recovered, missing-new-world, corrupt-existing-world, unsupported). Keep the selected directory associated with corruption errors. Recover from valid backups where possible; otherwise show an explicit recovery decision. Only create default metadata for a confirmed new world with no existing save artifacts.
- **Required regression test:** - Create two worlds, corrupt all manifest candidates for the selected world, and verify the other world is not silently selected.
- Verify the damaged world remains visible as requiring recovery.
- Verify an existing directory with world/player save files never receives guessed V1 metadata.
- Verify a genuinely new world still receives the intended defaults.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #113 — [Save Validation] Reject structurally malformed JSON before accepting a save candidate

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The generic read helper treats successful deserialization as equivalent to a valid save. There is no model-specific validation stage between deserialization and returning the candidate. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/Worlds.cs; - Method: ReadJson<T>(); - Lines: 472–504; - File: scripts/VoxelWorld.cs; - Method: LoadSave(); - Lines: 3071–3087 and 3202–3213; - File: scripts/Inventory.cs; - Method: LoadFrom(); - Lines: 301–323; - File: scripts/Container.cs; - Method: LoadFrom(); - Lines: 48–63; ; [ReadJson](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Worlds.cs#L472-L504) · [LoadSave](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3071-L3087)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #111 and #114 (malformed data vs unknown IDs vs recovery candidate selection).
- **Required implementation:** Add model-specific validators for manifest, world, player, container, furnace, villager, quest, and boat data. Validate required collections and nested records, vector lengths, finite numeric values, slot ranges, IDs, and supported versions. Make ReadJson accept a validator so an invalid candidate is skipped and the next backup is tried. Keep validation non-mutating.
- **Required regression test:** - Primary save with a null chunk dictionary and valid .bak: backup is loaded.
- Primary save with a null furnace record and valid .bak2: backup is loaded.
- Null item-stack list elements and null boat entries do not crash loading.
- All-invalid candidates produce an explicit corrupt result rather than an exception.
- Valid legacy V1–V4 fixtures remain accepted.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #114 — [Save Recovery] Distinguish missing save files from unrecoverable existing saves

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** ReadJson<T>() returns null for both missing and unrecoverable data. The caller does not know whether any candidate existed or why each candidate failed, so it cannot choose a safe recovery policy. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/Worlds.cs; - Method: ReadJson<T>(); - Lines: 472–504; - File: scripts/VoxelWorld.cs; - Method: LoadSave(); - Lines: 3069–3073 and 3243–3257; - File: scripts/Inventory.cs; - Method: LoadFrom(); - Lines: 300–310; ; [ReadJson](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Worlds.cs#L472-L504) · [World load](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3069-L3073) · [Inventory defaults](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Inventory.cs#L300-L310)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #111 and #113 (missing/corrupt save recovery policy).
- **Required implementation:** Return a structured read result that distinguishes missing, valid, recovered-from-backup, corrupt, and unsupported. Determine whether the world directory already contains save artifacts before classifying a missing file as a first-run state. Do not initialize starter inventory or permit saves for an existing world whose player save history is unrecoverable without explicit user confirmation.
- **Required regression test:** - New world with no save files receives its intended initial player state.
- Existing world with all world-save candidates corrupt enters a recovery-required state and cannot autosave.
- Existing world with all player-save candidates corrupt does not receive the starter kit silently.
- A deliberate reset creates a new baseline only after explicit confirmation and preserves the original files until then.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #115 — World deletion can follow directory links outside the worlds folder

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The recursive delete operation relies on path-string validation but does not enforce a no-follow-links policy at each filesystem entry before opening and descending into it. The archive enumeration code already demonstrates a reparse-point exclusion pattern, but the deletion path does not apply an equivalent guard. **Issue confidence:** Strongly suspected by static analysis. The missing link/reparse-point guard is visible in the recursive deletion path. Actual traversal depends on platform-specific Godot/.NET filesystem behavior and was not runtime-verified.
- **Evidence locations:** - File: `scripts/Worlds.cs`; - Method: `Delete()`; - Lines: `221–240`; - Method: `DeleteDirectory()`; - Lines: `279–303`; - Related safer pattern: `WorldArchive.EnumerateFilesWithoutLinks()`, lines `308–324`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #117 (recursive delete link safety vs recursive copy link safety).
- **Required implementation:** Apply a no-follow-links policy to every entry before recursion. Detect symbolic links and reparse points using the platform-appropriate Godot/.NET APIs, and never open or recurse through them. Resolve or otherwise verify containment for non-link entries where appropriate. Keep deletion scoped to the selected world directory, and fail safely if an entry cannot be classified. Add tests using temporary external targets and platform-appropriate links.
- **Required regression test:** - Create a temporary world containing a directory symlink to an external directory with nested files; delete the world and assert every external file remains unchanged.
- Add a nested-link case to verify the guard applies at every recursion depth.
- Verify ordinary nested directories and files inside a world are still deleted.
- Verify link handling behaves safely when link creation is unavailable or unsupported on the test platform.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #116 — Crafting-grid result collection duplicates items when inventory accepts only part of the output

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** `TakeResult()` uses `Inventory.Add(result)` as both a capacity check and a transfer. That method is intentionally partial and mutating: it fills available stacks/slots before returning the leftover count. The caller treats a positive leftover as a failed transaction but does not roll back the items already inserted. Unlike `Recipes.Craft()`, this path does not call `CanFit()` before mutation. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit.
- **Evidence locations:** - File: `scripts/InventoryUi.cs`; - Method: `TakeResult()`; - Lines: `930–955`; - Related method: `scripts/Inventory.cs::Add(ItemStack)`, lines `62–95`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Use the existing `Inventory.CanFit(result.Id, result.Count)` preflight before any output mutation, or implement a small transactional transfer that can roll back partial insertion. Consume the matched ingredients only after the full output has been committed to the cursor or inventory. Preserve existing crafting recipes and stack semantics.
- **Required regression test:** - Reproduce with a Log → 4 Planks recipe, a plank stack with capacity for only 1–3 items, and every other inventory slot occupied.
- Assert that a failed collection leaves both inventory and crafting ingredients byte-for-byte unchanged.
- Assert that successful collection transfers the full result and consumes the matched ingredients once.
- Cover empty cursor, matching cursor, incompatible cursor, and full-inventory cases.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #117 — Recursive world duplication can copy files through directory links

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The recursive copy operation assumes every enumerated child directory is an ordinary directory. It does not enforce a no-follow-links policy before recursion or copying file entries, so lexical source/destination paths do not guarantee that copied content originates inside the source world. **Issue confidence:** Strongly suspected by static analysis. The missing link/reparse-point guard is visible in the recursive copy path. Whether the link is followed during enumeration/opening is platform-dependent and was not runtime-verified.
- **Evidence locations:** - File: `scripts/Worlds.cs`; - Method: `Duplicate()`; - Lines: `186–218`; - Method: `CopyDirectory()`; - Lines: `243–276`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #115 (recursive copy link safety vs recursive delete link safety).
- **Required implementation:** Define and enforce an explicit filesystem-link policy for world duplication. Inspect every source entry before opening it; skip or reject symbolic links, junctions, and reparse points, and do not follow links for either directory recursion or file reads. Prefer a shared safe traversal helper where it can be reused without changing existing world format or save behavior.
- **Required regression test:** - Create a source world with a directory symlink to an external directory containing files; duplicate it and assert no external content appears in the destination.
- Add a nested-link case to verify every recursion level is guarded.
- Add a file-link case and assert the linked target is not copied as ordinary file content.
- Verify regular nested files and directories are still copied correctly.
- Verify behavior when link creation is unsupported by the test platform.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #118 — Mining and mob loot overflow is discarded instead of becoming recoverable world drops

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** Reward delivery is implemented as a direct call to `Inventory.Add()`. The caller observes the leftover integer but does not transfer it to a world pickup, queue, or persistent overflow store. Block removal and mob death are already committed before reward capacity is known. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Methods: `FinishBreak()`, `GiveDrop()`; - Lines: `1462–1568`; - File: `scripts/Mobs.cs`; - Method: `OnMobDied()`; - Lines: `952–1010`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Partially overlaps #104: both cover mining overflow; #118 additionally covers mob loot.
- **Required implementation:** Centralize reward delivery so it returns the accepted amount and routes every leftover through one recoverable drop/pickup path. Keep reward ownership, stack merging, chunk-unload behavior, entity/storage caps, and save/load semantics explicit. Do not implement one physics node per item if a bounded stack-based mechanism fits the existing architecture.
- **Required regression test:** - Fill the inventory, mine a block, and assert the complete drop remains recoverable.
- Repeat with mob loot and partial capacity.
- Verify stack merging, pickup of the remaining quantity, and no duplication.
- Verify recoverability after save/load and chunk unload/reload.
- Verify multiple rewards and mixed item IDs do not overwrite one another.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #119 — Failed world creation can start an existing world instead

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The creation API has no explicit success/failure contract for manifest persistence. It discards the boolean result of `WriteManifest()`, while downstream code assumes that a returned path represents a valid, loadable world. The session fallback then masks the creation failure by resolving a different world. **Issue confidence:** Confirmed by static analysis. The unchecked write result and fallback selection are directly visible in the code. Runtime reproduction with a read-only or full filesystem was not performed.
- **Evidence locations:** - File: `scripts/Worlds.cs`; - Method: `Create()`; - Lines: `164–171`; - File: `scripts/MainMenu.cs`; - Method: `CreateWorld()`; - Lines: `359–382`; - Method: `StartWorld()`; - Lines: `396–400`; - File: `scripts/GameSession.cs`; - Method: `EnsureWorld()`; - Lines: `35–55`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Make world creation return an explicit result that distinguishes success from failure and includes a user-displayable error. Treat manifest persistence as a required creation step; do not return a success path unless it completed. Clean up a newly created partial directory on failure where safe. In session initialization, distinguish an absent explicit selection from an invalid explicitly selected world: an invalid selection should produce an error rather than silently choosing a different save.
- **Required regression test:** - Simulate `WriteManifest()` failure during creation and assert the creation result is failure.
- With another valid world present, assert failed creation does not select or open that world.
- Assert the main-menu flow remains on the menu and reports the failure.
- Verify successful creation still opens the newly created world.
- Verify existing worlds and their save files remain unchanged after a failed creation attempt.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #120 — Closing the application with inventory cursor or crafting-grid items open loses those items

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** The inventory UI has a temporary item-ownership state separate from `Inventory`, but the save boundary serializes only `Inventory`. The normal UI close path returns temporary items, while application shutdown/focus-loss paths bypass that settlement lifecycle. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit.
- **Evidence locations:** - File: `scripts/InventoryUi.cs`; - Methods: `Close()`, `ForceClose()`, `ReturnCraftItems()`, `GiveCursorBack()`; - Lines: `649–701`; - File: `scripts/Main.cs`; - Method: `_Notification()`; - Lines: `188–202`; - File: `scripts/VoxelWorld.cs`; - Method: `SaveNow()`; - Lines: `3000–3045`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #116 (shutdown return of cursor/grid items vs partial craft-result transfer).
- **Required implementation:** Introduce one idempotent inventory-UI settlement operation and invoke it before serializing player inventory on every supported save/exit path. Alternatively, move temporary item ownership into a serializable model. If items cannot fit back into the inventory, preserve them in a defined overflow location rather than leaving them in an unpersisted UI field.
- **Required regression test:** - Save with a stack on the cursor, reload, and assert the exact item ID/count is preserved.
- Save with one or more crafting-grid stacks, reload, and assert every item is preserved.
- Exercise window-close, focus-loss, pause-menu save, and quit-to-menu paths.
- Repeat with a full inventory and verify settlement does not discard leftovers.
- Call settlement more than once and verify it does not duplicate items.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #121 — Null SHA-256 metadata throws during world archive import validation

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The deserialized archive metadata is treated as fully populated even though JSON can represent a declared string property as null. Validation checks the hash's length but does not validate the field's presence and value first. **Issue confidence:** Confirmed by static analysis. The null dereference follows directly from the deserialized field being nullable at runtime. Runtime reproduction was not performed.
- **Evidence locations:** - File: `scripts/WorldArchive.cs`; - Method: `Import()`; - Lines: `190–218`; - Validation expression: `record.Sha256.Length`; - File: `scripts/WorldManagementDialog.cs`; - Method: `ImportWorldAsync()`; - Lines: `297–324`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Validate each metadata record and required field before reading it. Reject a null or malformed SHA-256 value with `InvalidDataException` (or the importer's established invalid-archive result), then stop processing before modifying the destination. Keep the validation explicit so future metadata fields receive the same required-field treatment.
- **Required regression test:** - Add an archive fixture with `"sha256": null` and assert import returns the expected invalid-archive failure rather than throwing `NullReferenceException`.
- Assert the import destination and existing worlds remain unchanged after rejection.
- Retain tests for valid hashes, corrupted archives, traversal rejection, and successful round-trip import.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #122 — Loading a save written while the player is dead restores full health at the death location

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Zero health is overloaded as both a valid dead-player value and a legacy/unset-save sentinel. The player save has no persisted death-state field, and the load path silently treats a dead save as a fresh healthy player. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit.
- **Evidence locations:** - File: `scripts/PlayerStats.cs`; - Method: `Load()`; - Lines: `244–253`; - File: `scripts/Player.cs`; - Methods: `Respawn()`, `LoadFromSave()`; - Lines: `775–840`; - File: `scripts/VoxelWorld.cs`; - Method: `SaveNow()`; - Lines: `3015–3040`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Separate legacy missing-health detection from a genuine dead-player save. Add an optional backward-compatible death-state field or define an explicit load migration rule, then route dead saves through the normal death/respawn lifecycle. Do not reinterpret existing legacy saves incorrectly.
- **Required regression test:** - Serialize a dead player's state, reload it, and verify the death/respawn contract.
- Verify a legacy save with an omitted health field still receives its intended default.
- Verify a normal living save restores its exact health.
- Verify respawn anchor, inventory, XP, and spawn protection are preserved according to softcore rules.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #123 — Player stat changes do not mark the world dirty, so stationary Survival changes can be lost

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The player-stat model exposes a change event, but persistence dirty tracking is wired only to inventory changes and selected world events. Player stats are included in `SaveNow()`, yet their mutations do not reliably schedule that save. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit.
- **Evidence locations:** - File: `scripts/Main.cs`; - Method: `_Ready()`; - Lines: `130–150`; - File: `scripts/PlayerStats.cs`; - Methods: `ApplyDamage()`, `AddExperience()`, `AddHunger()`, `Update()`; - Lines: `43–185`; - File: `scripts/VoxelWorld.cs`; - Methods: `SaveIfDirty()`, `MarkDirty()`, autosave logic; - Lines: `3040–3065`, `2760–2780`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Connect the persisted player-stat change event to the existing dirty-tracking path. Keep the current autosave interval and dirty coalescing; do not invoke a disk write for every stat tick.
- **Required regression test:** - Change hunger while stationary and verify the world becomes dirty and focus-loss saving writes the new value.
- Apply damage without changing inventory and verify health is persisted.
- Award XP without a loot item and verify XP is persisted.
- Verify repeated stat events are coalesced by the existing autosave interval.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #124 — Using a campfire at full health and hunger does not mark the new respawn anchor dirty

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The campfire rest operation has a persisted side effect (respawn anchor) but does not notify the persistence owner. It relies implicitly on unrelated mutations to trigger saving. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Method: `UseCampfire()`; - Lines: `1396–1435`; - Comparison: `SleepAt()`, lines `1327–1347`; - File: `scripts/VoxelWorld.cs`; - Method: `SaveIfDirty()`; - Lines: `3056–3065`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Mark the world dirty when the campfire rest action successfully changes the respawn anchor. Prefer a shared respawn-anchor setter that consistently notifies persistence for both campfires and beds.
- **Required regression test:** - Set up full health/full hunger, use a campfire, and assert the world becomes dirty.
- Save/reload and verify the campfire anchor is preserved.
- Verify resting with changed stats still produces one coherent dirty transition.
- Verify bed and campfire anchors follow the same persistence contract.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #125 — Mining ordinary block drops bypass ItemGathered and leave Gather quests incomplete

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** The block-drop implementation bypasses the existing gather-event abstraction. Different reward sources use inconsistent inventory-delivery code: mob loot and `GiveDrop()` record successfully acquired quantities, while ordinary block drops do not. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit. This finding is also recorded in the existing Player-Journey Audit dated 2026-11-21.
- **Evidence locations:** - File: `scripts/Player.cs`; - Methods: `FinishBreak()`, `GiveDrop()`, `RecordGathered()`; - Lines: `1515–1568`, `89–94`; - File: `scripts/QuestSystem.cs`; - Method: `Setup()`; - Lines: `76–86`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** True duplicate of #105: same ordinary mined-drop ItemGathered omission and Gather-quest consequence; preserve both issue numbers.
- **Required implementation:** Route standard block drops through the existing `GiveDrop()`/gather-reporting pattern or a shared reward helper. Report only the count successfully acquired; overflow must not count toward Gather objectives.
- **Required regression test:** - Accept the Blacksmith quest and mine Iron Ore with a valid tool; assert Raw Iron advances the objective.
- Verify a wrong-tier tool does not count a drop that was not awarded.
- Verify partial inventory capacity advances progress only by the accepted amount.
- Verify full inventory does not advance the objective.
- Cover other standard block drops used by Gather objectives.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #126 — Merchant biome-discovery quest starts at zero even when required biomes were already explored

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** Quest acceptance does not reconcile stateful objectives against the existing world state. Biome discovery is stored independently as a world flag, while quest progress is updated only through new discovery events. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit. This finding is also recorded in the existing Player-Journey Audit dated 2026-11-21.
- **Evidence locations:** - File: `scripts/QuestSystem.cs`; - Methods: `_Process()`, `Accept()`, `Report()`; - Lines: `89–104`, `126–145`, `155–175`; - Quest definition: lines `24–30`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** When accepting an `ExploreBiome` objective, count matching existing `explored/*` flags and initialize progress from that count. Ensure subsequent reports use unique flags and cannot double count a biome.
- **Required regression test:** - Discover three biomes before accepting the quest; verify the first stage completes immediately.
- Test one and two pre-existing discoveries.
- Discover additional biomes after acceptance and verify progress increments once per biome.
- Revisit biomes and verify no duplicate progress.
- Save/reload between acceptance and subsequent discoveries.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #127 — Generated village wheat is absent from the crop-growth registry

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** The generator's direct chunk-data stamping path bypasses `VoxelWorld.SetBlock()` and `UpdateCropSource()`. The crop registry currently relies on runtime edits and persisted-edit reconstruction rather than registration of generated structure content. **Issue confidence:** Confirmed by static analysis; runtime reproduction was not performed in this audit. This finding is also recorded in the existing Player-Journey Audit dated 2026-11-21.
- **Evidence locations:** - File: `scripts/VillageStructures.cs`; - Method: `Farm()`; - Lines: `109–139`; - File: `scripts/WorldGenV8.cs`; - Method: `StampVillagesV8()`; - Lines: `629–675`; - File: `scripts/VoxelWorld.cs`; - Methods: `UpdateCropSource()`, `CropSnapshot()`; - Lines: `2294–2308`; - File: `scripts/CropSystem.cs`; - Method: `_Process()`; - Lines: `18–57`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #107 (village-generated crop registry vs support removal/growth).
- **Required implementation:** Register generated crop coordinates when village structure data is integrated into the chunk/world lifecycle. Keep generated blocks as generator output rather than converting them into player edits. Preserve frozen generator output for existing world versions and ensure registry updates do not duplicate entries.
- **Required regression test:** - Generate a village using a supported generator version and assert its farm wheat appears in the crop snapshot.
- Advance daytime growth ticks and verify village crops advance.
- Verify player-planted crops continue growing.
- Verify unloading/reloading a chunk does not lose registration or cause duplicate growth entries.
- Verify existing generator golden hashes and saved-world compatibility remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #128 — Inventory, container, and furnace save loaders accept invalid item IDs and over-limit stacks

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Inventory, item rewards, crafting and quest acquisition.
- **Verified root cause / investigation status:** Persistence loading reconstructs runtime item stacks directly from serialized integer fields instead of routing them through a shared validator that enforces the persisted ID registry, stack limits, slot constraints, and equipment metadata invariants. **Issue confidence:** Confirmed by static analysis for missing validation; runtime consequences of particular malformed IDs were not exercised.
- **Evidence locations:** - File: `scripts/Inventory.cs`; - Method: `LoadFrom()`; - Lines: `300–325`; - File: `scripts/Container.cs`; - Method: `LoadFrom()`; - Lines: `48–63`; - File: `scripts/FurnaceData.cs`; - Method: `LoadFrom()`; - Lines: `157–183`; - File: `scripts/ItemDb.cs`; - Methods: `MaxStack()`, `New()`; - Lines: `304–315`
- **Primary worker owner:** W04 — Inventory, item rewards, crafting and quest acquisition. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Partially overlaps #109: shared loader validation; #128 adds duplicate-slot and None/undefined-ID cases.
- **Required implementation:** Add a shared save-stack validator and use it consistently in all three loaders. Validate defined non-None IDs, slot uniqueness/range, positive count, maximum stack size, and applicable durability/enchantment bounds. Preserve all valid legacy saves and report skipped/corrected records through the existing recovery diagnostics.
- **Required regression test:** - Reject undefined and `ItemId.None` records with positive counts.
- Reject or safely split non-stackable items with count greater than one.
- Reject or safely split stackable items above `MaxStack`.
- Detect duplicate slot records deterministically.
- Apply the same validation to inventory, containers, and furnace slots.
- Verify valid existing and legacy saves still round-trip exactly.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #129 — [Autosave] Persist world clock and playtime without unrelated gameplay mutations

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Autosave scheduling is driven by gameplay dirty state, but time-based persisted state is updated independently and is not represented in that dirty state. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/VoxelWorld.cs; - Method: _Process(); - Lines: 2681–2687 and 2767–2780; - File: scripts/DayNight.cs; - Method: _Process(); - Lines: 431–441; - File: scripts/VoxelWorld.cs; - Method: SaveNow(); - Lines: 3031–3042; ; [Autosave gate](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L2767-L2780) · [Clock update](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/DayNight.cs#L431-L441) · [Playtime persistence](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3031-L3042)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Track time-based persistence explicitly. Mark clock/playtime state dirty at a controlled cadence, or maintain a separate metadata-save deadline that triggers a save without causing per-frame writes. Preserve the existing four-second autosave policy for ordinary gameplay changes.
- **Required regression test:** - Run an idle-world fixture for multiple autosave intervals and verify TimeOfDay is persisted.
- Verify PlaytimeSeconds advances after idle autosave.
- Verify no save is issued every frame.
- Verify focus-loss and shutdown include the latest accumulated values.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #130 — [Player Persistence] Save small player-position changes when application focus is lost

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The movement threshold is evaluated only inside the periodic _Process() autosave block. The focus-loss save path does not perform the same position-change check. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/VoxelWorld.cs; - Method: _Process(); - Lines: 2767–2778; - File: scripts/VoxelWorld.cs; - Method: SaveIfDirty(); - Lines: 3055–3062; - File: scripts/Main.cs; - Method: _Notification(); - Lines: 195–202; ; [Movement threshold](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L2767-L2778) · [Focus-loss save](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Main.cs#L195-L202) · [SaveIfDirty](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3055-L3062)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Before deciding whether to save on focus loss, compare the current player position with _lastSavedPos and mark the player-save state dirty when they differ. Keep the eight-unit threshold for ordinary periodic autosave if it is intentional.
- **Required regression test:** - Save a world, move the player by a distance below eight units, invoke the focus-loss path, and verify player.json contains the new position.
- Verify zero movement does not cause unnecessary writes.
- Verify movement beyond eight units still triggers the normal periodic autosave.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #131 — [Save UX] Propagate SaveNow failures to manual save and quit flows

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** SaveNow() keeps its aggregate write result private and exposes no return value or structured status to its callers. Callers therefore cannot distinguish a successful save from a partial or failed save. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/VoxelWorld.cs; - Method: SaveNow(); - Lines: 2946–3052; - File: scripts/PauseMenu.cs; - Method: Setup() save-button callback; - Lines: 77–81; - File: scripts/PauseMenu.cs; - Method: QuitToMenu(); - Lines: 182–187; ; [Save result handling](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L2946-L3052) · [Save button](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/PauseMenu.cs#L77-L81) · [Quit path](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/PauseMenu.cs#L182-L187)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Return a structured SaveResult from SaveNow(), including which file(s) failed and whether the full snapshot committed. Update manual save UI and quit flow to act on that result. Preserve retry behavior by keeping dirty state until a complete save succeeds.
- **Required regression test:** - Inject a world.json write failure and verify SaveNow reports failure.
- Inject a player.json write failure and verify the pause menu does not display success.
- Verify QuitToMenu does not change scenes on an unacknowledged save failure.
- Verify a successful three-file save still clears dirty state and displays success.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #132 — [Backup Rotation] Preserve the oldest backup until the replacement save commits

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Backup retention is mutated before the commit point. The rotation procedure does not stage the oldest backup or roll the complete rotation back after failure. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/Worlds.cs; - Method: WriteAtomic(); - Lines: 411–448 and 454–466; ; [Backup rotation](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Worlds.cs#L411-L448) · [Final commit](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Worlds.cs#L454-L466)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Stage backup rotation so the prior primary and both backups remain recoverable until the new primary is committed. Only remove the oldest recovery point after successful commit. Keep the current sibling-temp and checked-rename architecture.
- **Required regression test:** - Start with three distinct valid generations and inject a failure when moving the current file to .bak; verify all original generations remain recoverable.
- Inject a failure during the final temp-to-primary commit; verify the previous primary and both backup generations remain.
- Verify a successful write still leaves the newest three generations in the expected order.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #133 — [Player Persistence] Persist player yaw and camera pitch across world reloads

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** The player persistence model and serialization/restoration paths include position but omit body yaw and camera pitch. **Issue confidence:** Confirmed by static analysis; not reproduced at runtime.
- **Evidence locations:** - File: scripts/Worlds.cs; - Type: PlayerSave; - Lines: 45–54; - File: scripts/VoxelWorld.cs; - Method: SaveNow(); - Lines: 3013–3029; - File: scripts/VoxelWorld.cs; - Method: LoadSave(); - Lines: 3243–3261; - File: scripts/Main.cs; - Method: _Ready(); - Lines: 43–49; ; [PlayerSave model](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Worlds.cs#L45-L54) · [Player serialization](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/VoxelWorld.cs#L3013-L3029) · [Player restoration](https://github.com/firasotoom85-droid/minecraft-godot/blob/b9fea0acfd8293e2af4abbda1abb4fca0685f849/scripts/Main.cs#L43-L49)
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Append optional yaw and pitch fields to PlayerSave. Serialize the player's body yaw and camera pitch, validate finite/range-safe values, and restore them after the player and camera nodes are initialized. Default absent fields for old saves without altering existing positional field semantics.
- **Required regression test:** - Round-trip nonzero yaw and positive/negative camera pitch through PlayerSave serialization.
- Verify Main's player restoration applies both values.
- Load a legacy player.json without orientation fields and verify it retains the existing default behavior.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #134 — [Rendering][High] Cave entry switches to exponential fog before cave density is blended

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The mode-selection predicate is effectively binary while the density transition is gradual. The density value is not separately converted for the exponential profile, and the outdoor depth-fog clamp/range update is skipped once the scene is considered sheltered.

Relevant code:
- [DayNight.cs — cave density blend and fog mode](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/DayNight.cs#L553-L595)
- [DayNight.cs — EvaluateCaveDarkness](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/DayNight.cs#L220-L229)
- [WorldEnvironmentFactory.cs — fog mode selection and distance-haze constants](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/WorldEnvironmentFactory.cs#L8-L29) **Issue confidence:** High confidence in the source-level mode/density mismatch. Runtime appearance and severity have not yet been reproduced or measured.
- **Evidence locations:** - File: `scripts/DayNight.cs`; - Method: `_Process(double delta)`; - Relevant code: cave density blend, sheltered predicate, and fog-mode selection around lines 553–595; - Related file: `scripts/WorldEnvironmentFactory.cs`; - Method: `FogModeFor(bool usesDistanceHaze, bool underground)` around lines 24–28
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #136 (cave fog-mode transition vs cutout shader antialiasing).
- **Required implementation:** Use one of these approaches:

1. Keep modern worlds in depth-fog mode and transition the depth range for caves; or
2. Maintain separate depth-fog and exponential-fog density profiles, with an explicit smooth transition and suitable hysteresis when switching modes.

Do not change the legacy V1–V7 fog behavior. Preserve generator-version compatibility.
- **Required regression test:** - Add a deterministic test covering fog mode and density around the `0.001` sheltered threshold.
- Test several skylight values across the full `EvaluateCaveDarkness` transition.
- Add an integration/runtime check that samples fog settings while moving outdoors → cave mouth → cave interior → outdoors.
- Verify legacy generator versions retain their existing fog mode and density behavior.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #135 — [Rendering][Moderate] Graphics presets override configured MSAA with Disabled

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** MSAA is represented in the preset data but is set to zero for every preset. Runtime preset application makes this value authoritative, while the regression test verifies only the startup project configuration.

Relevant code:
- [Gfx.cs — preset definitions](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/Gfx.cs#L28-L41)
- [Gfx.cs — runtime MSAA assignment](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/Gfx.cs#L260-L272)
- [project.godot — configured 3D MSAA](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/project.godot#L208-L213)
- [GlassTests.cs — current cutout configuration test](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/tests/GlassTests.cs#L58-L69) **Issue confidence:** High confidence. The zero values and runtime assignment are directly visible in source. The visual difference should still be checked on the supported GPU/rendering configurations.
- **Evidence locations:** - File: `scripts/Gfx.cs`; - Preset definitions: lines 28–41; - Method: `ApplyPreset()`, around line 270; - File: `project.godot`; - Setting: `rendering/anti_aliasing/quality/msaa_3d`, around line 211; - Related tests: `tests/GlassTests.cs`, `TestCutoutShaders()`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Define intentional MSAA levels per preset rather than hardcoding zero for every tier. Keep lower-end profiles free to disable MSAA if that is a deliberate performance decision, but make the choice explicit and consistent with cutout rendering expectations.

Update the tests to inspect the active viewport after `ApplyPreset()`, not only the project setting.
- **Required regression test:** - Assert the expected `Viewport.Msaa3D` value for every preset after application.
- Verify the saved preset restores the same effective MSAA value on startup.
- Add a cutout rendering test or capture for foliage/alpha-scissor edges at the supported MSAA levels.
- Verify presets that intentionally use no MSAA remain configured as designed.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #136 — [Rendering][Moderate] Cutout voxel shaders omit ALPHA_ANTIALIASING_EDGE

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The cutout shaders configure alpha scissoring and alpha-to-coverage but omit the alpha-antialiasing edge threshold.

Relevant code:
- [BlockDb.cs — atlas cutout shader](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/BlockDb.cs#L710-L720)
- [BlockDb.cs — atlas alpha output](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/BlockDb.cs#L832-L841)
- [BlockDb.cs — greedy array cutout shader](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/BlockDb.cs#L995-L1005)
- [BlockDb.cs — greedy array alpha output](https://github.com/firasotoom85-droid/minecraft-godot/blob/main/scripts/BlockDb.cs#L1120-L1130)
- [Godot 4.7 spatial shader reference](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html) **Issue confidence:** High confidence that the parameter is absent from both shader implementations. The exact visual improvement after adding it requires runtime validation.
- **Evidence locations:** - File: `scripts/BlockDb.cs`; - Shader: `LitAtlasShader`, render mode around line 715 and alpha threshold around line 837; - Shader: `OpaqueArrayShader`, render mode around line 1000 and alpha threshold around line 1126; - Related test: `tests/GlassTests.cs`, `TestCutoutShaders()`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Set `ALPHA_ANTIALIASING_EDGE` to an intentional value below `ALPHA_SCISSOR_THRESHOLD` in both cutout shaders, following the Godot 4.7 shader documentation. Use the same policy for atlas and greedy-array paths unless a measured reason requires different values.

Do not alter texture alpha, atlas coordinates, greedy merge keys, or persisted block identifiers as part of this fix.
- **Required regression test:** - Extend `GlassTests` to assert that both cutout shader sources declare the edge threshold.
- Assert the configured edge threshold is below the alpha-scissor threshold.
- Render matching atlas and greedy-array foliage test scenes with MSAA enabled and compare silhouette behavior.
- Verify shader compilation on the project's supported Forward+ target.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #137 — [Rendering][Investigation] Validate intra-chunk transparent glass sorting

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** Low. The batching and sorting configuration are confirmed by source inspection, but the visual failure remains unverified.
- **Evidence locations:** - File: `scripts/Mesher.cs`; - Method: `Build()`; - Glass surface allocation around line 250 and transparent-surface routing around lines 320–330; - File: `scripts/Chunk.cs`; - Glass mesh creation and sorting configuration around lines 44–50
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Investigation-only relation: #193 and #180 concern other transparency/render-pass paths; no duplicate claim.
- **Required implementation:** First create a minimal deterministic Godot scene and confirm whether same-chunk glass panes actually sort incorrectly on the supported renderer.

If reproduced, evaluate a small number of spatial transparent batches or another renderer-compatible sorting strategy. Preserve chunk-level batching and avoid one node per glass block. Do not modify the water path unless a separate reproduction demonstrates the same problem.

If no visible failure can be reproduced, close this issue as not reproducible and document the tested camera/layout conditions.
- **Required regression test:** - Add a reproducible scene with at least two depth-separated panes in one chunk.
- Capture both camera directions and a camera movement sequence that changes pane order.
- Compare same-chunk panes with equivalent panes placed in separate chunks.
- Measure draw calls, frame time, and memory if a batching change is introduced.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #138 — [Physics] Void-fall recovery bypasses survival damage

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Player interaction, placement, combat and damage.
- **Verified root cause / investigation status:** Void recovery and void damage are implemented in two different callbacks. The physics callback owns the position transition and executes the recovery before the render-frame callback evaluates the same threshold. The two callbacks do not share a single void-fall state transition. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed in this read-only audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Methods: `_PhysicsProcess(double delta)`, `_Process(double delta)`; - Lines: `519–522`, `604–668`
- **Primary worker owner:** W06 — Player interaction, placement, combat and damage. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #142 (void recovery damage path vs armor mitigation/durability for fall damage).
- **Required implementation:** Move void damage and recovery into one authoritative physics-side transition. Apply damage before teleporting, prevent repeated application while recovering, and keep creative-mode behavior consistent with the existing damage rules.
- **Required regression test:** - Add a player/world integration test that places a Survival player below Y = -8 and verifies void damage is applied before recovery.
- Verify the player is recovered only once and velocity is cleared.
- Verify Creative mode does not take damage.
- Verify ordinary falls and sky-island respawn behavior remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #139 — [Physics] Chunk colliders omit boundary faces against unbuilt neighbors

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The same padded voxel neighborhood is used for render-face culling and collision-face generation. Render geometry can safely hide a shared face because the neighboring chunk will eventually render its side, but collision geometry cannot rely on a neighbor that has not been built. The mesher does not distinguish a generated-data neighbor from a physics-active neighbor. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed in this read-only audit.
- **Evidence locations:** - File: `scripts/Mesher.cs`; - Methods: `Build()`, `BuildPadded()`; - Lines: `230–245`, `335–367`, `944–1016`; - File: `scripts/VoxelWorld.cs`; - Methods: `RebuildQueue()`, `ApplyBuild()`; - Lines: `2579–2611`, `2784–2807`; - File: `scripts/Chunk.cs`; - Method: `ApplyPayload()`; - Lines: `110–133`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Separate render visibility from collision boundary ownership. Preserve neighbor-aware render culling, but emit collision boundary faces when the neighboring chunk has no active collider, or maintain a collision-ready neighbor shell. Ensure the same rule is applied when a neighboring chunk loads or unloads so seam ownership remains deterministic.
- **Required regression test:** - Build one collidable chunk beside a generated-but-unbuilt chunk containing solid boundary voxels.
- Assert that the loaded chunk's collision includes the exposed frontier faces.
- Load the neighbor and verify no collision seam or duplicate surface causes movement artifacts.
- Unload the neighbor and verify the remaining chunk retains a closed collision boundary.
- Repeat for all four cardinal edges and negative chunk coordinates.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #140 — [Entities] Mobs keep simulating after their terrain chunks unload

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** Chunk lifetime is managed independently from entity lifetime. The chunk unload limit is based on `ViewDistance + UnloadMargin`, while mob despawn thresholds are fixed distances. With view distance 2, the chunk unload frontier is around four chunk offsets from the player, but mob despawn radii extend to 80 or 120 metres. No loaded-chunk guard or entity suspension state bridges the gap. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed in this read-only audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `SetViewDistance()`, `UnloadFar()`; - Lines: `2422–2433`, `2909–2945`; - File: `scripts/Mobs.cs`; - Methods: `_Process()`, `DespawnFar()`; - Lines: `161–180`, `286–293`; - File: `scripts/Mob.cs`; - Method: `_PhysicsProcess(double delta)`; - Lines: `283–353`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Coordinate mob lifetime with chunk availability. At minimum, suspend physics/AI for mobs whose current chunk is unloaded and safely resume them when it loads; alternatively despawn or serialize them before the supporting collider is removed. Keep the existing population caps and deferred-free validity handling intact.
- **Required regression test:** - With view distance 2, unload a chunk containing a gravity-based mob while the mob is still inside the current despawn radius.
- Verify that the mob does not continue falling or simulating without terrain.
- Move the player back and verify the entity resumes safely or is restored according to the selected lifecycle policy.
- Verify passive and hostile despawn behavior at normal and minimum view distances.
- Verify queued-for-deletion mobs are not processed twice.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #141 — [Physics] Block mutations leave the chunk collider stale until async remeshing completes

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** Visual mesh and physics collision are bundled into one `MeshPayload` and applied together. `SetBlock()` only enqueues asynchronous work; it does not synchronously update or invalidate the affected physics shape. The global edit epoch prevents stale payloads from being applied, but it does not prevent physics from using the previous collision shape while the replacement is pending. **Issue confidence:** Confirmed by static analysis. Runtime timing and the visible duration were not measured in this read-only audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `SetBlock()`, `EnqueueRebuild()`, `WorkerLoop()`, `ApplyBuild()`; - Lines: `2063–2184`, `2504–2577`, `2579–2611`, `2835–2846`; - File: `scripts/Chunk.cs`; - Method: `ApplyPayload()`; - Lines: `110–133`; - File: `scripts/Player.cs`; - Methods: `FinishBreak()`, `PlaceBlock()`; - Lines: `1462–1510`, `1623–1788`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Introduce an explicit collision-synchronization path for committed edits. The smallest safe design may prioritize and apply collision data before the next relevant physics step while allowing visual mesh work to remain asynchronous. Preserve edit-epoch validation and ensure stale payloads cannot overwrite newer collision state.
- **Required regression test:** - Place a solid block and assert its collision is active at the next physics query.
- Remove a supporting block and assert its collision is no longer active at the next physics query.
- Perform rapid consecutive edits to the same block and verify an older payload cannot restore stale collision.
- Repeat edits at chunk boundaries and verify neighboring collision remains correct.
- Measure the mutation-to-collision update time under a busy streaming queue.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #142 — [Gameplay] Fall damage bypasses armor mitigation and durability

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Player interaction, placement, combat and damage.
- **Verified root cause / investigation status:** The player damage pipeline in `Player.ApplyDamage()` applies armor mitigation through `EquipmentProgression.ShouldDamageArmor()` and `ArmorReduction`, then calls `DamageArmor()` after a landed hit. `TrackFall()` bypasses that pipeline and calls the stats model directly. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed in this read-only audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Methods: `TrackFall()`, `ApplyDamage()`; - Lines: `673–722`, `843–890`; - File: `scripts/EquipmentProgression.cs`; - Method: `ShouldDamageArmor()`; - Lines: `95–97`; - File: `scripts/Combat.cs`; - Enum: `DamageKind`; - Lines: `6–15`
- **Primary worker owner:** W06 — Player interaction, placement, combat and damage. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #138 (fall-damage mitigation vs void-fall recovery).
- **Required implementation:** Route landing damage through `Player.ApplyDamage(new DamageInfo(amount, DamageKind.Fall))`. Preserve the existing fall-height calculation and water exclusion, and verify that any fall-specific feedback remains unchanged.
- **Required regression test:** - Verify a fall causes less health loss with armor equipped than without armor.
- Verify armor durability decreases after a qualifying fall.
- Verify drowning and starvation still bypass armor as intended.
- Verify Creative mode and respawn invulnerability still refuse fall damage.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #143 — [Interaction] Voxel targeting can select blocks in unloaded chunks

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Player interaction, placement, combat and damage.
- **Verified root cause / investigation status:** The interaction ray uses the general-purpose `GetBlock()` API, which has generate-on-read behavior. The world already exposes `TryGetLoadedBlock()` for non-generating reads, and the bucket ray uses it to stop when it reaches an unloaded cell, but the normal block-targeting DDA does not enforce the same loaded-data boundary. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed in this read-only audit.
- **Evidence locations:** - File: `scripts/Player.cs`; - Methods: `TryGetTargetBlock()`, `TryRaycastVoxels()`, `TryGetBucketRayHit()`; - Lines: `994–1031`, `1033–1115`, `1877–1945`; - File: `scripts/VoxelWorld.cs`; - Methods: `GetBlock()`, `TryGetLoadedBlock()`, `SetBlock()`; - Lines: `2000–2025`, `2063–2075`
- **Primary worker owner:** W06 — Player interaction, placement, combat and damage. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #106 (unloaded-chunk targeting vs non-colliding voxel Pick Block).
- **Required implementation:** Use a loaded-only voxel read for interaction ray traversal, with an explicit stop at the first unloaded cell. Keep generate-on-read behavior for world-generation and sampling callers that require it. Ensure target feedback is cleared while the relevant chunk is awaiting its collider.
- **Required regression test:** - Aim a player ray into generated-but-unloaded terrain and verify no target block is returned.
- Load the target chunk and verify the same ray selects the expected block.
- Verify mining and placement still work at positive and negative chunk boundaries.
- Verify water/bucket targeting behavior remains unchanged.
- Verify interaction raycasts do not allocate chunk data for unloaded cells.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #144 — [Performance][Moderate] Crop growth rehydrates chunks that are already unloaded

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** The crop registry is not scoped to the streamed chunk lifecycle. The read API used by the crop tick has generation semantics, while the mutation API requires a loaded `Chunk`. The system performs those reads before establishing that the crop's chunk is loaded. **Issue confidence:** Confirmed by static analysis; not reproduced with a runtime traversal test.
- **Evidence locations:** - File: `scripts/CropSystem.cs`; - Method: `_Process()`; - Lines: `20–58`; - File: `scripts/VoxelWorld.cs`; - Methods: `CropSnapshot()`, `GetBlock()`, `ChunkData()`, `SetBlock()`, `UnloadFar()`; - Lines: `2014–2025`, `2063–2080`, `2308`, `2909–2939`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Make crop processing chunk-aware. The smallest safe correction is to skip registry entries whose chunk is not currently loaded before calling `GetBlock()`. If offline crop growth is desired later, implement it using compact crop state and elapsed time rather than reading or rebuilding complete terrain chunks. Keep registry add/remove operations connected to the existing mutation and save-loading paths.
- **Required regression test:** - Register a crop, unload its chunk, and run a growth tick; assert that no chunk generation or cache insertion occurs.
- Assert that an unloaded crop is not passed to `SetBlock()`.
- Reload the chunk and verify normal crop growth resumes.
- Verify crop registration and removal continue to follow the existing `SetBlock()` mutation path.
- Verify generated-village crop registration remains covered by issue #127's regression tests.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #145 — [Performance][Moderate] Procedural site caches grow without unload-time eviction

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** Procedural site caches have no spatial eviction or capacity limit. Their lifetime is the world object's lifetime, but their contents are not coordinated with the streamed-area retention policy. **Issue confidence:** Confirmed by static analysis; retained-entry growth was not measured in a long-duration runtime test.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `CachedCampfireSite()`, `CachedStructureSite()`, `UnloadFar()`; - Lines: `789–810`, `857–927`, `2909–2939`; - File: `scripts/WorldGenV8.cs`; - Methods: `GetVillageSite()`, `CacheVillage()`; - Lines: `513–599`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Add a bounded spatial retention policy for these deterministic caches. Prefer evicting entries outside the same safe region used by chunk streaming, or impose a documented capacity/LRU policy. Keep synchronization around cache access and preserve the deterministic generation functions so evicted entries can be recomputed without changing world-generation output.
- **Required regression test:** - Query site cells across multiple distant regions and assert cache retention remains within the configured bound after eviction.
- Verify cached positive and null results are both eligible for eviction.
- Re-query an evicted cell and assert its result is identical to the original deterministic result.
- Move across chunk boundaries and verify eviction does not remove entries still needed by nearby chunks or minimap queries.
- Verify existing generator-version golden hashes remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #146 — [Performance][Moderate] Meshing holds the global snapshot lock during full light-neighborhood solves

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** Snapshot acquisition and expensive derived-data computation are combined in one critical section. The nested lock in `LightDataNeighborhood()` is reentrant, so it does not release the outer lock around the expensive computation. **Issue confidence:** Confirmed by static analysis; lock wait time and frame impact were not measured in this audit.
- **Evidence locations:** - File: `scripts/Mesher.cs`; - Method: `Build()`; - Lines: `230–247`; - File: `scripts/VoxelWorld.cs`; - Methods: `LightDataNeighborhood()`, `BuildLightPadded()`; - Lines: `1669–1795`, `1090–1105`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Separate immutable snapshot capture from derived lighting computation. Under `SnapshotLock`, capture the required voxel/state inputs and their relevant revisions; release the lock before building the 5×5 region and propagating light. Before publishing maps, validate that their input revisions remain current, otherwise discard and retry. Preserve the current shared-neighborhood solve and cache semantics.
- **Required regression test:** - Instrument lock ownership and assert expensive lighting propagation runs without holding the global snapshot lock.
- Run simultaneous distant chunk builds and verify their snapshot phases are not serialized by a full lighting solve.
- Mutate a light source during an out-of-lock solve and verify stale results are rejected.
- Verify boundary light propagation and all nine neighborhood maps remain identical to current golden cases.
- Add a multi-worker streaming benchmark and compare lock wait/hold time and mesh throughput.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #147 — [Far LOD][Moderate] Worker failures leave tile coordinates permanently in flight

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The worker task has no exception-safe completion/finally path for the in-flight registry. Dispatch capacity is calculated from the same registry, so a bookkeeping leak can stop the subsystem. **Issue confidence:** Confirmed by static analysis; an injected Far-LOD worker exception was not used to reproduce the stall.
- **Evidence locations:** - File: `scripts/FarLod.cs`; - Methods: `Dispatch()`, `BuildTask()`, `ApplyTile()`; - Lines: `175–200`, `393–395`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #94 and #202 (stale Far LOD payload and evicted-tile queue progress).
- **Required implementation:** Make task completion exception-safe. Capture either a payload or failure result, then always schedule a main-thread completion action while the node is alive; that action should remove the in-flight entry and decide whether to retry. During shutdown, ensure the completion path does not touch freed nodes and still releases bookkeeping safely.
- **Required regression test:** - Inject an exception from `BuildArrays()` and assert the tile is removed from `_inFlight`.
- Fill all dispatch slots with failing tasks and assert later queued tiles can still be dispatched.
- Verify null payloads and successful payloads continue to use the normal apply path.
- Verify scene shutdown during a build does not queue work against a freed node or retain in-flight entries.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #148 — [Performance][Moderate] Single chunk payload application can exceed the main-thread time budget

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The time-slice controller is payload-granular, while the cost of an individual payload is unbounded and is not split into smaller application units. The code's "at least one payload" progress guarantee can override the nominal budget. **Issue confidence:** Confirmed by static analysis; the maximum duration of a single application was not benchmarked during this audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `_Process()` (payload application loop); - Lines: `2721–2745`; - File: `scripts/Chunk.cs`; - Methods: `ApplyPayload()`, `ApplySurface()`; - Lines: `109–137`, `155–178`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Profile and report each application sub-step separately. If large-payload spikes exceed the frame target, split safe work into staged application or reduce redundant copies where Godot's API permits. Keep the existing main-thread Godot resource creation requirements, payload ownership/release rules, and stale-payload validation intact.
- **Required regression test:** - Apply representative small, median, and worst-case payloads and record apply/collision time separately.
- Verify the apply queue progresses without starving fresh or rebuild payloads.
- Verify staged application never exposes a partially replaced chunk as complete.
- Verify payload buffers are released exactly once on success, rejection, and cancellation.
- Add a performance test that records p95/p99 apply duration at the project's target view distance.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #149 — [Startup][Moderate] Initial 3×3 chunk builds run synchronously on the main thread

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The synchronous helper used for immediate node creation is applied to the entire initial 3×3 region instead of a strictly minimal spawn-critical subset. The asynchronous worker pipeline is not used for those nine builds. **Issue confidence:** Confirmed by static analysis; startup frame duration was not measured during this audit.
- **Evidence locations:** - File: `scripts/Main.cs`; - Method: `_Ready()`; - Line: `166`; - File: `scripts/VoxelWorld.cs`; - Methods: `SetupInitial()`, `BuildNow()`; - Lines: `2383–2397`, `2479–2495`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #148 and #150 (startup main-thread work vs payload application budget and synchronous autosave).
- **Required implementation:** Keep only the minimum spawn-critical terrain synchronous, and submit the remaining initial neighborhood through the existing worker queue. Ensure spawn collision and safe player placement are ready before enabling movement. Do not replace or bypass the existing streaming/greedy-meshing pipeline.

**Comment-scope addition (#149, comment 7105749575):** Include the menu-backdrop caller. `MainMenu._Ready()` constructs `MenuBackdrop`; its `_Ready()` calls `BuildPreview()` with radius 2, which loops over a 5×5 area and calls synchronous `BuildNow()` when uncached. Move non-critical preview work off the main thread or otherwise bound it using the existing streaming architecture; do not create gameplay collision for the menu preview.
- **Required regression test:** - Verify the player cannot fall or move into an unbuilt spawn-critical region.
- Verify the initial queue contains every required non-critical spawn-neighborhood chunk.
- Verify the first playable state is reached without waiting for all nine full builds to finish.
- Verify fresh worlds and loaded worlds show the same initial terrain and collision.
- Add a cold-start benchmark measuring time to first responsive frame.

**Comment-scope regression (#149):** Measure cold entry to main menu after leaving a world and verify the 25 preview builds do not create a main-thread stall; confirm render-only preview has no gameplay colliders and remains visually complete.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #150 — [Autosave][Moderate] Autosave serializes and writes the complete world synchronously

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** Snapshot preparation, serialization, and disk persistence are combined in the synchronous `SaveNow()` path. The periodic autosave scheduler does not offload the expensive stages. **Issue confidence:** Confirmed by static analysis; save duration was not measured with a large edited world during this audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `_Process()`, `SaveNow()`; - Lines: `2767–2780`, `2946–3053`; - File: `scripts/WorldStore.cs`; - Method: `WriteAtomic()`; - Lines: `376–469`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Capture a consistent save snapshot on the main thread, then move pure serialization and file I/O to a serialized background save worker. Ensure a newer save cannot overtake an older one, keep all live Godot object access on the main thread, and retain the existing atomic writer/backup format and world-save compatibility.
- **Required regression test:** - Save a large edited world and assert the main-thread snapshot phase is bounded.
- Verify serialization and disk I/O execute off the main thread.
- Trigger two saves quickly and verify they commit in order without losing the newest dirty state.
- Inject write/flush/rename failures and verify the existing atomic recovery guarantees remain intact.
- Load worlds written by the revised path using the current and previous supported save formats.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #151 — [Save Load][Moderate] Rebuild edit-top metadata regenerates every edited chunk without reusing data

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Save-load metadata reconstruction invokes the terrain generator directly instead of sharing generated chunk data with the normal cache/streaming path. The temporary arrays are not promoted or reused. **Issue confidence:** Confirmed by static analysis; duplicate generation time was not measured during this audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `LoadSave()`; - Lines: `3069–3170`; - Related methods: `GenerateChunkData()`, `ChunkData()`, `UpdateEditTop()`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Avoid generating a second copy of terrain for an edited chunk. Reuse a generated array through a safe cache/snapshot insertion path, or compute edit-top information with a narrower generator query that preserves the current terrain and edit overlay. Ensure cache accounting and unload behavior remain correct.
- **Required regression test:** - Load a save with edits in N distinct chunks and assert each edited chunk's terrain generator is invoked no more than necessary before it is first streamed.
- Verify edit tops and top-block IDs match the existing implementation.
- Verify edited chunks still render identically after streaming.
- Verify unedited chunks are not eagerly generated by the load path.
- Verify save compatibility and generator-version golden hashes remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #152 — [Performance][Low] VillageSystem allocates a removal list every frame

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** The deferred-removal scratch collection is allocated unconditionally in the hot process path rather than being reused or created lazily. **Issue confidence:** Confirmed by static analysis; frame allocation volume was not profiled.
- **Evidence locations:** - File: `scripts/VillageSystem.cs`; - Method: `_Process()`; - Lines: `20–63`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Keep a reusable scratch list as an instance field and clear it after use, or allocate lazily only after the first removal candidate is found. Preserve safe deferred dictionary removal and avoid modifying `_active` during its enumeration.
- **Required regression test:** - Verify invalid and out-of-range residents are removed correctly.
- Verify the active collection can be scanned without allocating a new removal list when no removal is required.
- Verify multiple removals in one frame are all applied exactly once.
- Add an allocation profiler check for a stable-resident scenario.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #153 — [Performance][Moderate] Every chunk creates mesh and collision nodes for unused surfaces

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** Chunk node construction is eager and independent of the generated surface contents. Empty surfaces clear their mesh resource but do not release or avoid the corresponding node. **Issue confidence:** Confirmed by static analysis; node-count and memory savings were not benchmarked by surface category.
- **Evidence locations:** - File: `scripts/Chunk.cs`; - Methods: `Setup()`, `NewSurfaceMesh()`, `ApplySurface()`; - Lines: `34–75`, `155–178`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Create optional glass/water/lava mesh nodes lazily on first non-empty payload. Keep opaque and required collision behavior consistent with current gameplay assumptions. Do not recreate nodes on every rebuild; retain a lazily created node for the chunk's lifetime once needed.
- **Required regression test:** - Build chunks with no glass/water/lava and assert optional surface nodes are not created.
- Build chunks containing each optional surface and assert the correct mesh/material is applied.
- Rebuild a chunk from non-empty to empty and verify stale geometry is removed.
- Verify chunk unload frees all created children and no collision behavior changes.
- Compare scene-node counts across a fixed high-view-distance terrain fixture.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #154 — [Memory][Moderate] Static mesher pools retain peak buffer capacities without a cap

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The pool is process-wide and capacity-retaining, but release logic always returns buffers regardless of size or total retained memory. **Issue confidence:** Confirmed by static analysis; high-water retention across world transitions was not measured in this audit.
- **Evidence locations:** - File: `scripts/Mesher.cs`; - Class: `PayloadPool`; - Methods: `Release()`, `ReturnSurface()`; - Lines: `1211–1268`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Add an explicit retained-byte/item budget. Discard or trim oversized buffers when the budget would be exceeded, while preserving normal-sized reuse and thread safety. Ensure the accounting remains correct when buffers are rented, released, or discarded, and do not trim buffers while they are in active use.
- **Required regression test:** - Release buffers below and above the configured size limit and assert only eligible buffers remain pooled.
- Verify retained-byte counters equal the estimated capacities actually held by the pool.
- Run concurrent rent/release tests and verify counters never become negative or double-count a buffer.
- Load a large world, release payloads, transition to a small world, and assert retained memory stays under the cap.
- Verify greedy-meshing output is identical before and after pool eviction.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #155 — [Diagnostics][Low] Cache-memory snapshot omits persistent procedural and edit caches

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** The snapshot implementation enumerates a fixed subset of cache fields and does not maintain a centralized accounting contract for all world-owned persistent caches. **Issue confidence:** Confirmed by static analysis; the exact omitted byte total was not measured.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `GetCacheMemorySnapshot()`; - Lines: `2451–2476`; - Related fields: `_crops`, `_campfireCache`, `_structureCache`, `_villageCache`, `_editTops`, `_editTopBlocks`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Extend the snapshot with separate, clearly named estimates for every significant persistent cache and registry, including collection overhead estimates where practical. Preserve the distinction between exact array byte counts, estimated managed object/dictionary overhead, and driver-specific VRAM. If a category cannot be counted accurately, report it as unknown rather than silently excluding it.
- **Required regression test:** - Populate each tracked cache/registry with a deterministic number of entries and verify the appropriate snapshot category increases.
- Verify clearing/eviction decreases the corresponding category.
- Verify array byte counts are exact and estimated collection overhead is clearly identified.
- Add a test that new world-owned persistent caches must be represented in the snapshot or explicitly listed as excluded.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #156 — [Performance][Moderate] Mob projectiles allocate a ray-query object and result every physics tick

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** The projectile does not retain/reuse its ray-query parameters, and the per-frame API result is materialized as a dictionary rather than using a lower-allocation reusable query path. **Issue confidence:** Confirmed by static analysis; allocation rate was not profiled in a projectile-heavy runtime scenario.
- **Evidence locations:** - File: `scripts/MobProjectile.cs`; - Method: `_PhysicsProcess()`; - Lines: `61–88`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Reuse query parameters per projectile where the Godot API permits it, and investigate a reusable lower-allocation hit-result path. Keep the exact collision mask, area behavior, ray start/end, and projectile-stop rules unchanged. Do not introduce shared mutable query state across concurrent projectiles.
- **Required regression test:** - Verify terrain hits and misses match the existing behavior for horizontal, vertical, and diagonal projectile motion.
- Verify the terrain collision mask and `CollideWithAreas = false` behavior remain unchanged.
- Add an allocation-focused test or profiler scenario with many simultaneous projectiles.
- Verify projectile cleanup still occurs on timeout, terrain hit, and peaceful difficulty.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #157 — Photo mode locks player input and blocks its own exit action

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High. The state transition and early return are directly visible in the source. Runtime reproduction was not performed during the read-only audit.
- **Evidence locations:** - File: `scripts/PhotoMode.cs`; - Method: `_UnhandledInput()`; - Lines: `40–52`; - Method: `Toggle()`; - Lines: `63–96`; - File: `scripts/PauseMenu.cs`; - Method: `_UnhandledInput()`; - Lines: `107–123`; - Method: `PauseInputPolicy.CanHandlePause()`; - Lines: `8–14`
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #159 (Photo Mode own exit input lock vs keybinding conflict policy).
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #158 — World directory IDs can collide when the monotonic tick value repeats

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High for the unchecked collision behavior. The collision trigger is deterministic when the same masked tick value is supplied; ordinary user frequency is lower than the impact once a collision occurs. Runtime reproduction was not performed.
- **Evidence locations:** - File: `scripts/Worlds.cs`; - Method: `Create()`; - Lines: `164–171`; - Method: `WriteManifest()`; - Lines: `126–139`; - Method: `Duplicate()` (comparison: it checks target existence); - Lines: `187–218`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #159 — Keyboard rebinding allows collisions with reserved gameplay shortcuts

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High. The incomplete conflict-check scope and independent action handlers are directly visible in the source. Runtime reproduction was not performed.
- **Evidence locations:** - File: `scripts/Keybinds.cs`; - Method: `Conflict()`; - Lines: `116–132`; - File: `scripts/KeybindUi.cs`; - Method: `_UnhandledInput()`; - Lines: `209–229`; - File: `project.godot`; - Input action declarations: `93–110`, `128–172`; - File: `scripts/Gfx.cs`; - Method: `_UnhandledInput()`; - Lines: `198–215`; - File: `scripts/Player.cs`; - Method: `_UnhandledInput()`; - Lines: `319–335`
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #160 — World duplication can publish an incomplete copy after file I/O failures

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High for the missing I/O result checks. Runtime failure injection was not performed.
- **Evidence locations:** - File: `scripts/Worlds.cs`; - Method: `CopyDirectory()`; - Lines: `243–276`; - Method: `Duplicate()`; - Lines: `187–218`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #161 — An inaccessible world backup can abort backup-list refresh

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High. The exception types and uncaught call path are directly visible. Runtime permission testing was not performed.
- **Evidence locations:** - File: `scripts/WorldArchive.cs`; - Method: `WorldBackupStore.List()`; - Lines: `496–522`; - Method: `ValidateBackup()`; - Lines: `552–568`; - File: `scripts/WorldManagementDialog.cs`; - Method: `RefreshBackups()`; - Lines: `234–252`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #162 — World management panel is shown without assigning initial UI focus

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** Moderate. The missing focus assignment is confirmed statically; the exact focus behavior should be verified in a live Godot UI run.
- **Evidence locations:** - File: `scripts/MainMenu.cs`; - Method: `ShowManage()`; - Lines: `304–315`; - Method: `ShowMain()`; - Lines: `406–414` (comparison); - Method: `ShowWorlds()`; - Following panel-switch implementation (comparison)
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #164 (initial focus assignment vs restoring caller focus after overlay close).
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #163 — Settings and binding ConfigFile writes silently ignore persistence errors

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High. The unchecked return values are directly visible. Runtime disk-failure reproduction was not performed.
- **Evidence locations:** - File: `scripts/Settings.cs`; - Method: `Save()`; - Lines: `120–140`; - File: `scripts/Keybinds.cs`; - Methods: `Save()`, `Rebind()`; - Lines: `70–89`; - File: `scripts/GamepadBindings.cs`; - Method: `Save()`; - Lines: `121–136`; - File: `scripts/SettingsPanel.cs`; - Method: `Build()` / local `Apply()`; - Lines: `18–24`
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #164 — Closing world-management overlay does not restore the caller's focus

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** Moderate. The missing focus hand-back is statically confirmed; the resulting navigation behavior needs live keyboard/controller validation.
- **Evidence locations:** - File: `scripts/WorldManagementDialog.cs`; - Method: `ShowFor()`; - Lines: `39–47`; - Method: `_UnhandledInput()`; - Lines: `64–70`; - Method: `BuildInterface()` (Close button callback); - Lines: `142–155`; - Import completion path; - Lines: `297–314`
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #162 (focus hand-back vs initial focus assignment).
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #165 — World listing treats directory-open failures as an empty save library

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High for the error/result conflation. Runtime permission testing was not performed.
- **Evidence locations:** - File: `scripts/Worlds.cs`; - Method: `List()`; - Lines: `142–161`; - File: `scripts/MainMenu.cs`; - Method: `RefreshWorlds()`; - Lines: `266–284`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #166 — World rename reports manifest and filesystem failures as an invalid name

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Save/load, world manifests, backups and filesystem safety.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High. The return paths and UI message mapping are directly visible. Runtime filesystem failure was not performed.
- **Evidence locations:** - File: `scripts/MainMenu.cs`; - Method: `RenameWorld()`; - Lines: `317–327`; - File: `scripts/Worlds.cs`; - Method: `Rename()`; - Lines: `174–185`; - Method: `WriteManifest()`; - Lines: `126–139`
- **Primary worker owner:** W05 — Save/load, world manifests, backups and filesystem safety. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #167 — Refresh current-state documentation to match project configuration and test runner

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** Root cause is not yet stated separately in the report; investigate the summary and cited call path before implementation. **Issue confidence:** High. The cited documentation and source declarations are directly present in the audited revision.
- **Evidence locations:** - File: `PLAN.md`; - Current-state summary: lines `17–33`; - File: `ROADMAP.md`; - Current milestone table: lines `22–29`; - File: `tests/TestRunner.cs`; - Registered suite list: lines `17–68`; - File: `scripts/VoxelWorld.cs`; - World height selection: lines `295–299`; - File: `scripts/Gfx.cs`; - Preset registry: lines `28–42`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the implementation from the issue summary and current call path; keep the change scoped to the reported behavior and preserve existing save, streaming, mutation, and ID contracts.
- **Required regression test:** Add a focused regression test for the reported entry/exit or failure path, verify the expected state transition, and assert that existing neighboring UI/persistence behavior is unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #168 — Bug: Async benchmark and soak completion failures are not observable

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** The runners use `async void` for ordinary private workflow methods rather than reserving it for required Godot callback overrides/event handlers. This removes the task object that should represent the lifetime and result of each asynchronous completion workflow, so callers cannot await it, supervise it, or reliably translate its faults into the test process exit status. **Issue confidence:** Confirmed by static analysis. Runtime fault injection was not performed because the repository could not be cloned into the available execution environment.
- **Evidence locations:** - File: `tests/SurvivalSoakRunner.cs`; - Method: `_Process()`, `Finish()`; - Lines: `181–184`, `442–508`; - File: `tests/PerformanceRunner.cs`; - Method: `_Process()`, `Finish()`, `Fail()`; - Lines: `206–210`, `618–666`, `831–843`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Convert the private completion helpers to `Task`-returning methods and make the initiating runner own and observe the completion task. Await them from an appropriate Godot callback or supervise the stored task explicitly; do not discard the task. Ensure cleanup is in a fault-safe `finally`/supervised recovery path, and make every failure path log the original exception and request `Quit(1)` without allowing a secondary cleanup exception to bypass the exit decision.
- **Required regression test:** - Inject an exception before the first frame wait and verify the runner reports failure and exits non-zero.
- Inject an exception after a frame/timer continuation and verify the fault is observed by the runner rather than escaping as an async-void exception.
- Inject a cleanup failure and verify the runner still records a failed exit status and attempts remaining cleanup.
- Verify normal benchmark and soak completion still wait for scene teardown, remove the disposable world, write the expected report, and exit with the correct status.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #169 — Bug: Survival soak finalization exceptions bypass cleanup and exit

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** A multi-stage asynchronous finalization workflow is implemented as a non-event `async void` method and launched from a synchronous frame callback. The operation therefore has no task-based ownership or completion/error contract. Cleanup and process termination are placed only on the success path instead of an exception-safe completion path. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `tests/SurvivalSoakRunner.cs`; - Method: `Finish()`; - Lines: `442–508`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Change the finalizer to return `Task` and retain/observe that task from the runner's lifecycle coordinator. Add a top-level exception boundary that records the failure and selects a nonzero exit code. Put best-effort scene teardown, temporary-world cleanup, and final process exit in an exception-safe completion path so failures before or after either await cannot strand the runner. Preserve the existing worker-group shutdown ordering before deleting the temporary world.
- **Required regression test:** - Inject an exception during pre-await metrics/save work and assert the soak reports failure, attempts cleanup, and exits nonzero without waiting for the external timeout.
- Inject an exception after the first and after the second process-frame await and assert the same cleanup/exit guarantees.
- Verify the successful soak still drains VoxelWorld/FarLod workers before deleting the temporary world and exits zero.
- Verify repeated frame callbacks cannot start duplicate finalization tasks.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #170 — [Lifecycle][Moderate] Delayed thunder callback accesses audio player after scene exit

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Audio systems and scene-scoped audio lifecycle.
- **Verified root cause / investigation status:** A delayed callback has a longer lifetime than the node and resource it captures. The implementation binds work to a SceneTree-managed timer without tracking the pending callback, cancelling it during teardown, or checking an owner-lifetime state before accessing the child node. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not available in this audit environment.
- **Evidence locations:** - File: `scripts/Sound.cs`; - Methods: `Thunder()`, `_ExitTree()`; - Lines: `271–284`, `884–903`; - File: `scripts/Weather.cs`; - Method: `_Process()`; - Lines: `176–184`; - File: `scripts/Main.cs`; - Method: `_Ready()`; - Lines: `81–82`; - File: `scripts/PauseMenu.cs`; - Method: `QuitToMenu()`; - Lines: `182–186`
- **Primary worker owner:** W09 — Audio systems and scene-scoped audio lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Give delayed thunder work an explicit owner lifetime. At minimum, mark the `Sound` instance as exiting in `_ExitTree()` and have the timeout callback return before touching `_thunderPlayer` when the owner is no longer active; also verify the child reference is valid. Preferably retain the pending timer/callback pair and disconnect it during teardown so the timer does not retain the scene object until timeout. Preserve normal playback while `Sound` remains in the active gameplay scene.
- **Required regression test:** - Schedule a short thunder delay, remove/free the owning gameplay scene before timeout, advance the SceneTree beyond the timeout, and assert that no invalid-object call or runtime error occurs.
- Keep the owning scene active through timeout and verify that thunder playback still occurs.
- Schedule multiple thunder callbacks, exit the scene, and verify that every pending callback is safely cancelled or ignored.
- Exercise the actual storm-to-pause-to-menu path with the delay still pending.
- Verify teardown still stops all active audio players and releases generated audio streams.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #171 — World-scoped underwater filters leak into global audio buses

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Audio systems and scene-scoped audio lifecycle.
- **Verified root cause / investigation status:** The implementation mixes scene-scoped ownership with process-wide resource registration. The low-pass filters are created by a `Sound` node but registered with the singleton `AudioServer`, whose bus state outlives the scene. The teardown releases child playback resources and the stream dictionary but does not unregister the bus effects. In addition, runtime control relies on the positional index 0 rather than retaining and cleaning up ownership of the exact effect entries. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/Sound.cs`; - Method: `Setup()`; - Lines: `153–167`; - Method: `_Process(double delta)`; - Lines: `194–205`; - Method: `_ExitTree()`; - Lines: `884–903`; - File: `scripts/Main.cs`; - Method: `_Ready()` (creates the scene-owned `Sound` node); - Lines: `78–82`; - File: `scripts/PauseMenu.cs`; - Method: `QuitToMenu()`; - Lines: `182–187`
- **Primary worker owner:** W09 — Audio systems and scene-scoped audio lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** True duplicate of #173: both report the same world-scoped low-pass filters leaking/accumulating on global audio buses; preserve both issue numbers.
- **Required implementation:** Make the bus effects explicitly owned and reversible by the `Sound` instance. On teardown, locate and remove only the exact filter resources registered by that instance from their respective buses, or use a single process-wide filter owner with an explicit reset contract. Avoid assuming that the instance's filter remains at index 0 after future bus registrations. Restore the baseline enabled state and ensure repeated scene entry does not increase the bus-effect count.
- **Required regression test:** - Add a headless lifecycle test that records the initial `Sfx` and `Ambient` effect counts, creates and sets up a `Sound` node, then frees it; assert both counts return to their original values.
- Simulate underwater state until both owned filters are enabled, exit the scene, create a fresh dry-world `Sound` node, and assert no old enabled filter remains on either bus.
- Repeat setup/teardown across multiple world transitions and assert bus-effect counts remain constant.
- Verify the new instance can still enable and disable its own filters while entering and leaving water.
- Verify unrelated pre-existing bus effects are preserved and not removed by the cleanup.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #172 — Unvalidated ConfigFile Variants can corrupt settings and controller bindings

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** The configuration boundary treats a Variant returned by `ConfigFile.GetValue()` as if it were already a value of the target C# type. There is no schema/type check or semantic validation before updating global runtime state and InputMap. The `_loaded` and `_configured` flags are set before their respective configuration loops complete, so a failed initialization is also marked as completed. **Issue confidence:** Suspected.
- **Evidence locations:** - File: `scripts/Settings.cs`; - Method: `Load()`; - Lines: `86–117`; - File: `scripts/GamepadBindings.cs`; - Method: `Configure()`; - Lines: `32–56`; - File: `scripts/GamepadBindings.cs`; - Method: `ApplyButton()`; - Lines: `105–111`; - File: `scripts/GameSession.cs`; - Method: `_Ready()`; - Lines: `16–25`; - File: `scripts/Player.cs`; - Method: `_UnhandledInput()`; - Lines: `217–244`; - File: `scripts/SettingsPanel.cs`; - Method: `Build()`; - Lines: `24–43`
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Read each setting and binding into a Variant first, validate its Variant type, supported enum membership, and semantic range, then assign it. Retain the current default for invalid entries and log the affected section/key. Isolate validation per entry so one bad value cannot abort other configuration loading or startup. Set the loaded/configured flags only after initialization has completed safely, or ensure partial failures are retryable.
- **Required regression test:** - Load a settings file where `mouse_sensitivity` is a Dictionary, Array, String, Nil, zero, negative, NaN, or outside the UI range; assert the default or documented fallback is retained.
- Load wrong-typed Boolean and numeric values for invert-Y, FOV, audio, graphics, and gameplay settings; assert invalid entries do not prevent valid entries from loading.
- Load a gamepad file with Dictionary/Array/String/Nil and out-of-range integer values for one or more buttons; assert the default binding is retained and existing InputMap events are not replaced by invalid values.
- Verify malformed values do not interrupt `GameSession` initialization or prevent keybind, audio, content-validation, and legacy-migration setup.
- Verify valid existing settings and controller bindings continue to load unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #173 — Bug: Sound teardown retains low-pass filters on global audio buses

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Audio systems and scene-scoped audio lifecycle.
- **Verified root cause / investigation status:** The implementation mixes scene-owned resource lifetime with process-global ownership. `Sound.Setup()` creates scene-owned effect references but transfers them into the global `AudioServer` bus chain using `AudioServer.AddBusEffect()`. Teardown clears only node-local audio-player stream references and the local stream dictionary; it does not call the corresponding bus-effect removal API or otherwise reset the globally registered effects.

The effect-management code also assumes its filters remain at index 0. A later setup inserts a new effect at index 0, shifting earlier effects while `_Process()` continues toggling only index 0. Thus old filters become unmanaged as soon as another session is created. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/Sound.cs`; - Methods: `Setup()`, `_Process()`, `_ExitTree()`; - Lines: `153–167`, `194–204`, `884–903`; - File: `scripts/Main.cs`; - Method: `_Ready()`; - Lines: `81–82`; - File: `scripts/GameSession.cs`; - Method: `_Ready()`; - Lines: `16–25`; - File: `scripts/PauseMenu.cs`; - Method: `QuitToMenu()`; - Lines: `182–186`; - File: `scripts/MainMenu.cs`; - Method: `StartWorld()`; - Lines: `396–400`; - File: `README.md`; - Section: Underwater audio behavior; - Line: `1560`
- **Primary worker owner:** W09 — Audio systems and scene-scoped audio lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** True duplicate of #171: both report the same world-scoped low-pass filters leaking/accumulating on global audio buses; preserve both issue numbers.
- **Required implementation:** In `Sound._ExitTree()`, locate and remove the exact `_muffle` and `_ambientMuffle` resources from their respective bus chains, using the current effect indices rather than assuming index 0. Remove only the effects owned by this instance so unrelated bus effects remain intact. Ensure the owned filters are no longer enabled and clear their references after removal. Alternatively, move the filters into a single process-persistent audio owner and reuse exactly one instance per bus; do not create another pair for each gameplay scene.
- **Required regression test:** - Record the initial SFX and Ambient effect counts, create and set up one Sound instance, and verify that exactly one effect is added to each bus.
- Exit/free that Sound instance and verify both bus effect counts return to their original values.
- Repeat main-scene to menu to main-scene transitions and verify bus effect counts do not grow between cycles.
- Enable underwater muffling, exit the gameplay scene, enter another world while dry, and verify no filter from the previous Sound instance remains enabled or affects audio.
- Install an unrelated effect on each bus before Sound setup and verify teardown removes only the two Sound-owned filters.
- Verify underwater and emerged transitions still enable and disable the current instance's filters correctly.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #174 — Bug: Block placement rejects replaceable non-solid vegetation

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Player interaction, placement, combat and damage.
- **Verified root cause / investigation status:** The destination-occupancy check hard-codes `Air` and `IsWater` as the only accepted existing contents. It does not consult a replaceability classification. The block database already identifies crossed content such as Tall Grass, flowers, wheat stages, and River Reeds through `IsCross()`, and their definitions are non-solid, but that distinction is not used by the placement validator. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/Player.cs`; - Method: `PlaceBlock()`; - Lines: `1777–1780`; - File: `scripts/BlockDb.cs`; - Method: `IsCross()`; - Lines: `533–535`; - Block definitions: `165–185`, `240`
- **Primary worker owner:** W06 — Player interaction, placement, combat and damage. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Introduce or reuse an explicit replaceability predicate for the supported vegetation/crop block IDs and use it in `PlaceBlock()` alongside the existing Air/water rules. Do not treat every non-solid block as replaceable by default (for example, preserve the existing fluid and special-block rules). Perform the replacement through `VoxelWorld.SetBlock()` so crop registration, lighting, edit persistence, and chunk rebuild notifications remain consistent.
- **Required regression test:** 1. Place a solid block into a destination occupied by Tall Grass and verify the vegetation is replaced and exactly one item is consumed.
2. Repeat for red/yellow flowers, River Reeds, and each wheat growth stage.
3. Verify placement into Air and water retains current behavior.
4. Verify placement into a solid block or Lava remains rejected.
5. Verify replacing wheat updates the crop registry through the existing mutation path.
6. Verify a failed placement consumes no inventory and emits no `BlockPlaced` signal.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #175 — Bug: Placement overlap validation underestimates the player's capsule width

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Player interaction, placement, combat and damage.
- **Verified root cause / investigation status:** `Player.BlockHitsPlayer()` hard-codes an X/Z half-extent of 0.32 m. The player scene configures a `CapsuleShape3D` with radius 0.35 m and height 1.8 m. `BlockShapes.IntersectsAabb()` accurately tests the AABB it receives against occupied block micro-cells, but that input AABB is narrower than the physics collider.

A concrete false-negative is a player center 0.66 m from a block face: the guard's maximum X is 0.98 m and misses a block beginning at 1.0 m, while the 0.35 m capsule reaches 1.01 m and overlaps it. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/Player.cs`; - Methods: `PlaceBlock()`, `BlockHitsPlayer()`; - Lines: `1781–1784`, `1987–1992`; - File: `scenes/main.tscn`; - Player collision shape resource and assignment; - Lines: `8–10`, `34–36`; - File: `scripts/BlockShapes.cs`; - Method: `IntersectsAabb()`; - Lines: `365–382`
- **Primary worker owner:** W06 — Player interaction, placement, combat and damage. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the overlap bounds from the player's actual collision shape rather than a smaller hard-coded width. At minimum, use a conservative horizontal half-extent matching the 0.35 m capsule radius; an exact capsule-versus-occupied-microcell test is preferable if it can be kept consistent with the existing slab/stair occupancy model. Keep the final voxel write on `VoxelWorld.SetBlock()`.
- **Required regression test:** 1. Verify a full block is rejected when its face is 0.34 m from the player's horizontal center and accepted when separated by at least the actual collider radius.
2. Test overlap near the player's front, rear, left, and right sides.
3. Repeat with bottom/top slabs and stairs so empty shape regions remain placeable when they do not intersect the player.
4. Verify a rejected placement does not consume an item or emit `BlockPlaced`.
5. Verify the test uses the same capsule dimensions as `scenes/main.tscn` so a later collider-size change cannot silently invalidate the guard.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #176 — Bug: Interior block edits rebuild unrelated neighboring chunks

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The mutation path correctly compares old and new effective opacity, light-passage masks, source position, and emission before invalidating lighting caches. However, mesh rebuild scheduling is detached from those dependency checks and uses a fixed 3×3 loop for every successful mutation.

The mesher's actual geometry dependency is narrower: `BuildPadded()` and `BuildPaddedStates()` copy one-voxel cardinal borders and individual diagonal corner cells. The light path is separately represented by `LightDataNeighborhood()` and its 14-voxel halo. The unconditional scheduling loop treats all edits as if they were boundary edits or light-affecting changes.

This is distinct from [#95](https://github.com/firasotoom85-droid/minecraft-godot/issues/95), which concerns duplicate requests for a coordinate already in flight, and [#96](https://github.com/firasotoom85-droid/minecraft-godot/issues/96), which concerns world-wide `EditEpoch` rejection of otherwise valid payloads. This issue occurs even with no active build and a perfectly functioning queue. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `SetBlock(int x, int y, int z, BlockId id, byte state)`; - Lines: `2177–2184`; - Related dependency code:;   - File: `scripts/Mesher.cs`, methods `BuildPadded()`, `BuildPaddedStates()`, `BuildLightPadded()`; lines `944–1120`;   - File: `scripts/VoxelWorld.cs`, methods `InvalidateBlockLightingLocked()`, `InvalidateLightingLocked()`, `LightDataNeighborhood()`; lines `1664–1695`, `1970–1993`;   - File: `scripts/Chunk.cs`, method `ApplyPayload()`; lines `110–139`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Keep `SetBlock()` as the mutation boundary. Derive mesh rebuild targets from the edited local coordinates: the owner chunk always; the relevant X/Z cardinal neighbors only when the edit touches their shared edge; and the corresponding diagonal chunk only when the edit touches both edges. Separately include any chunks whose sampled baked-light data can be affected when opacity, light-passage, source position, or emission changes, using the existing light propagation radius and cache invalidation logic.

Do not bypass `SetBlock()` / the established edit architecture, weaken required lighting invalidation, alter generator versions, or replace the existing meshing and streaming systems.
- **Required regression test:** - With a loaded 3×3 neighborhood, replace an interior Stone block with Dirt and assert that only the owner chunk is enqueued.
- For edits at local X=0 and X=15, assert that only the corresponding cardinal X neighbor is added when the edit changes its sampled border.
- Repeat for local Z=0 and Z=15.
- For each of the four X/Z corners, assert that the two corresponding cardinal neighbors and the matching diagonal neighbor are included, with unrelated neighbors excluded.
- Cover transparent-to-opaque and opaque-to-transparent transitions at cardinal edges and corners.
- Cover torch placement/removal and shape-state changes; assert that every chunk whose baked-light samples are affected by the 14-voxel radius is rebuilt and no required light update is skipped.
- Verify updated render faces and collision at all affected seams, including negative chunk coordinates.
- Add a deterministic build-count or queue-target test showing that an interior non-light edit does not create eight extra mesh builds.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #177 — Bug: Flowing water gains an extra level below a falling column

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** The production simulator collapses vertical and horizontal support into one `best + 1` calculation. This bypasses the special `above >= 0 => 1` rule already represented by `FluidRules.ResolveFlowLevel()`. The regression suite tests that helper rule but does not exercise the production `UpdateFlowingCell()` path at the base of a fall. **Issue confidence:** Confirmed by static analysis
- **Evidence locations:** - File: `scripts/FluidSystem.cs`; - Method: `UpdateFlowingCell()`; - Lines: `172–180, 191–215`; - Related rule: `scripts/FluidRules.cs`, `ResolveFlowLevel()`, lines `20–29`; - Existing rule regression: `tests/FluidTests.cs`, lines `32–35`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #178 and #179 (flow-level calculation vs unknown-neighbor boundary behavior vs loaded-water discovery).
- **Required implementation:** Preserve the special vertical-feed rule in `UpdateFlowingCell()`: when the above-neighbour is water, resolve the current cell to level 1 instead of applying the horizontal `best + 1` calculation. Keep unknown-neighbour handling and all edits on the existing `SetBlock` path; do not change persisted IDs, save fields, generator versions, meshing, or streaming.
- **Required regression test:** - Add a deterministic `FluidWorldTests` fixture with a source above a level-1 falling column ending on a solid floor.
- Assert that the bottom cell remains level 1 after production simulation.
- Assert that horizontal water placed from the column base is level 2.
- Repeat with a non-source flowing cell above the base to verify the special case is independent of whether the upper cell is a source.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #178 — Bug: Flowing water weakens beside an unloaded chunk despite an unknown neighbor

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** `unknownNeighbour` is checked only in the `best < 0` and `desired > MaxFlowLevel` branches. The ordinary update branch does not compare `desired` with the current `level` before applying a weakening change. In particular, it fails to preserve the “unknown is not air / do not recede at unloaded boundaries” invariant when a weaker loaded support is also present. **Issue confidence:** Confirmed by static analysis
- **Evidence locations:** - File: `scripts/FluidSystem.cs`; - Method: `UpdateFlowingCell()`; - Lines: `172–210`; - Related loaded-neighbour lookup: `TryGetLoaded()`, lines `310–323`; - Existing boundary regression: `tests/FluidWorldTests.cs`, lines `42–49`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #177 and #179 (unknown-neighbor flow support vs flow-level calculation vs loaded-water discovery).
- **Required implementation:** Before applying a weakening flow-level change, retain the current cell when any support neighbour is unknown. Keep safe upgrades and all actual mutations on `VoxelWorld.SetBlock`; do not generate chunks as part of simulation or change persisted IDs, save format, generator versions, greedy meshing, or chunk streaming.
- **Required regression test:** - Extend `FluidWorldTests` with the loaded/unloaded chunk arrangement above, using the existing preview setup that leaves chunk 2 unloaded.
- Assert that a level-1 boundary cell does not become level 3–7 while an unloaded neighbour could still provide support.
- Add a control case where all support neighbours are loaded and confirm that unsupported flow still weakens or recedes as intended.
- Load the adjacent chunk and verify that normal propagation resumes using the now-known neighbourhood.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #179 — Bug: Fluid discovery skips water in chunks loaded after its scan

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** Fluid discovery is coupled to player-position transitions instead of the chunk streaming lifecycle. `FluidSystem` subscribes to `VoxelWorld.BlockChanged`, which is raised after successful block mutations, but asynchronous chunk completion in `ApplyBuild()` only registers the chunk in `_chunks`; it does not emit a corresponding availability notification. The periodic pulse cannot recover sources that were never discovered. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed because this audit environment has no local repository checkout or Godot/.NET executable.
- **Evidence locations:** - File: `scripts/FluidSystem.cs`; - Methods: `_Process()`, `DiscoverLoadedWater()`; - Lines: 56–115; - File: `scripts/VoxelWorld.cs`; - Method: `ApplyBuild(Vector2I cp, MeshPayload payload)`; - Lines: 2579–2609; - File: `scripts/Main.cs`; - Method: `_Ready()`; - Lines: 166–170; - File: `tests/FluidWorldTests.cs`; - Method: `Run(Node host)`; - Lines: 8–53
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #177 and #178 (newly loaded source discovery vs flow propagation rules).
- **Required implementation:** Connect fluid discovery to the existing chunk lifecycle. Emit a main-thread chunk-ready notification when `VoxelWorld` finishes applying a chunk and registers it in `_chunks`; have `FluidSystem` inspect that newly available chunk (including the necessary boundary cells) and enqueue its water cells through the current bounded queues. Unsubscribe the handler in `_ExitTree()`. Keep loaded-only reads, the existing edit budget, and `SetBlock()` as the only voxel mutation path.
- **Required regression test:** - Run a discovery pass while a deterministic water-containing chunk is not loaded, complete that chunk's load without moving the player, and verify the water is subsequently registered and simulated.
- Repeat after unloading and reloading a chunk containing a source.
- Verify the load notification does not duplicate source/active queue entries or exceed the existing per-tick processing/edit budgets.
- Verify an unloaded neighbor is still treated as unknown and is never generated or interpreted as air.
- Verify fluid mutations still pass through `VoxelWorld.SetBlock()` and continue to update saves, lighting, and meshing.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #180 — Bug: Ladder blocks render as translucent glass instead of opaque cutouts

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The mesher uses the single `Transparent` flag both for block-neighbor visibility semantics and for selecting the blended glass render surface. That mapping is valid for glass and other genuinely blended surfaces, but not for every texture with transparent pixels. Ladder is a non-solid cutout block, not a glass-like alpha-blended material. The project already has a separate opaque alpha-scissor shader path, but ladder is not routed through it. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/BlockDb.cs`; - Method: `BlockDb.Defs`; - Lines: `189–190`; - File: `scripts/BlockDb.cs`; - Methods: `LitGlassShader`, `PaintLadder()`; - Lines: `846–934`, `2623–2651`; - File: `scripts/Mesher.cs`; - Method: `Build()`; - Lines: `318–321`; - File: `scripts/Chunk.cs`; - Method: `ApplyPayload()`; - Lines: `105–115`; - File: `scripts/Player.cs`; - Method: `IsLadderAt()`; - Lines: `572–574`; - File: `tests/GlassTests.cs`; - Methods: `TestCutoutBlockClassification()`, `TestChunkSurfaceRouting()`; - Lines: `102–146`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Give the ladder an explicit cutout render classification and route its faces to the existing opaque alpha-scissor surface while preserving its current non-solid collision behavior and transparent-neighbor visibility semantics. Do not fix this by changing persisted IDs or by blindly setting `Transparent = false`, which would also change neighbor culling and light-attenuation behavior.
- **Required regression test:** - Extend the existing transparent-material routing fixture to place a ladder through `VoxelWorld.SetBlock()`.
- In both greedy and per-face modes, assert ladder triangles are present in the opaque surface and absent from the glass surface.
- Assert the opaque cutout shader retains the alpha-scissor threshold and that ladder background texels remain discarded while opaque rung texels are not attenuated.
- Assert ladder contributes no terrain collision triangles and remains recognized by the player climbing check.
- Verify existing glass and water routing and their blended alpha behavior remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #181 — Bug: Mob navigation treats non-solid plants as blocking walls

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** The movement probe uses an ID inequality as a substitute for collision classification:
`lower != BlockId.Air && !BlockDb.IsWater(lower)` and the equivalent upper-cell test. This duplicates block-occupancy logic instead of consulting the block definition and relevant collision/hazard semantics. The mesher explicitly sends cross plants to cutout geometry without cube faces or collision, while the block table marks these plant IDs non-solid; the steering probe contradicts both contracts. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/Mob.cs`; - Methods: `_PhysicsProcess()`, `SteerAroundObstacles()`, `IsDirectionBlocked()`; - Lines: `300–315`, `433–455`, `530–550`; - File: `scripts/BlockDb.cs`; - Method: `BlockDb.Defs`; - Lines: `166–190`, `231–241`; - File: `scripts/BlockDb.cs`; - Methods: `IsCross()`, `IsShorelineVegetation()`; - Lines: `527–536`; - File: `scripts/Mesher.cs`; - Method: `Build()`; - Lines: `280–297`; - File: `tests/MobContentTests.cs`; - Method: `Run()`; - Lines: `8–55`; - File: `tests/MobPerceptionTests.cs`; - Method: `Run()`; - Lines: `7–100`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #183 (mob wall classification vs partial/slab geometry probe behavior).
- **Required implementation:** Replace the non-Air test for lower/upper obstacles with a collision-aware classification that respects `BlockDb.IsSolid()`, while retaining explicit handling for hazards such as lava and the existing water movement rules. Keep the existing mob steering controller and collision architecture; do not make non-solid plants collidable to compensate for the probe. Add shape-aware occupancy only where the probe is intended to reason about partial solid shapes.
- **Required regression test:** - Add a focused steering test where tall grass, both flowers, torch, all wheat stages, river reeds and ladder occupy the lower/upper probe cells; assert these non-solid blocks do not mark the direction as a wall.
- Assert stone, glass and other collidable full blocks still block steering.
- Assert water keeps its existing swim/avoid behavior and lava remains treated as hazardous/impassable.
- Place a non-solid plant in a one-block-wide passage and assert a gravity mob continues through instead of stopping or selecting an unnecessary side route.
- Run the existing mob content, perception and world-collision suites unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #182 — Bug: Appended block materials fall back to dirt break and placement sounds

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Audio systems and scene-scoped audio lifecycle.
- **Verified root cause / investigation status:** Audio material classification is duplicated in three hard-coded switches in `Sound.cs` and was not extended alongside the append-only block registry. The block database already identifies foliage, cross plants and material tiles, but the audio path has no exhaustive mapping or validation that ensures every defined block resolves to its intended sound family. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/Sound.cs`; - Methods: `FamilyFor()`, `PlaceFamilyFor()`, `PitchFor()`, `OnBroken()`, `OnPlaced()`; - Lines: `223–252`, `314–339`; - File: `scripts/BlockDb.cs`; - Methods: `BlockDb.Defs`, `IsFoliage()`, `IsCross()`, `IsShorelineVegetation()`; - Lines: `214–241`, `521–536`; - File: `scripts/BlockId.cs`; - Lines: `67–85`; - File: `README.md`; - Sections: Procedural block sound behavior / material-matched breaking and placement sounds; - Lines: `695–704`; - File: `ROADMAP.md`; - Section: Survival harvest/audio behavior; - Lines: `1215–1224`
- **Primary worker owner:** W09 — Audio systems and scene-scoped audio lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Centralize the block-to-sound-family mapping or extend the existing switches so appended IDs map to their base material family. Prefer shared category helpers for foliage, wood and stone variants, with an explicit fallback only for blocks that intentionally use dirt sounds. Add an exhaustive validation/test for every defined `BlockId` so future appended IDs cannot silently inherit an unrelated sound family. Do not change persisted IDs or the sound synthesis/player architecture.
- **Required regression test:** - Assert spruce, jungle and swamp leaves plus river reeds resolve to the foliage break and placement families.
- Assert planks slabs/stairs resolve to wood and cobblestone/brick slabs/stairs plus weathered masonry resolve to stone.
- Assert existing dirt, sand, glass, foliage, wood and stone mappings remain unchanged.
- Assert every defined block ID has an intentional sound-family and pitch mapping rather than silently falling through to the generic dirt family.
- Exercise `BlockBroken` and `BlockPlaced` signals for representative appended blocks and verify the selected stream family.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #183 — Bug: Crystal Wisp steering treats empty slab volume as a solid wall

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Mobs, fluids, crops, villages and ecology.
- **Verified root cause / investigation status:** The steering helper collapses the block contract to the coarse `Solid` flag. The project already stores shape state and exposes the actual occupancy through `BlockShapes.OccupancyMask()` / `Occupied()`; the mesher emits shape geometry and collision from those occupied micro-cells. `IsWallBlocked()` does not use that state-aware occupancy path. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/CrystalWisp.cs`; - Methods: constructor, `AddShape()`; - Lines: `16–30`; - File: `scripts/Mob.cs`; - Methods: `_PhysicsProcess()`, `SteerAroundWalls()`, `IsWallBlocked()`; - Lines: `300–315`, `480–527`; - File: `scripts/BlockShapes.cs`; - Methods: `OccupancyMask()`, `Occupied()`, `IntersectsAabb()`; - Lines: `52–89`, `365–377`; - File: `scripts/Mesher.cs`; - Method: `AddShapeGeometry()`; - Lines: `510–545`; - File: `tests/BlockShapeTests.cs`; - Method: `Run()`; - Lines: `12–130`
- **Primary worker owner:** W07 — Mobs, fluids, crops, villages and ecology. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #181 (partial-block probe volume vs non-solid plant navigation).
- **Required implementation:** Make `IsWallBlocked()` use the block state and the Wisp's collision bounds when testing partial solids. Keep the fast solid-block path for full cubes, and use the existing `BlockShapes` occupancy helpers for slabs/stairs rather than changing their `Solid` definitions or collider geometry. Preserve the existing mob steering controller.
- **Required regression test:** - Place a bottom slab through `VoxelWorld.SetBlock()`; position a Crystal Wisp so its sphere is entirely above the slab's top surface while the body probe still falls within the same voxel; assert the path is not considered blocked.
- Place a Wisp so its sphere overlaps the slab's occupied lower half; assert the path is blocked.
- Repeat for top slabs and representative upright/upside-down stair facings.
- Verify stone and other full cubes still block the same probe.
- Verify greedy/per-face meshing and existing slab/stair collider geometry remain unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #184 — Bug: F3 HUD displays '?' for nine valid biome IDs

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** UI, input, settings, accessibility and focus.
- **Verified root cause / investigation status:** `Hud.BiomeNames` is a positional lookup table that was not extended when the biome enum grew from four values to 13. The display code validates the index against the stale array length rather than an exhaustive mapping of the current enum. **Issue confidence:** Confirmed by static analysis. The mismatch is explicit in the enum, lookup array, and display expression. It was not reproduced in a running Godot instance during this audit.
- **Evidence locations:** - File: `scripts/Hud.cs`; - Method: `UpdateInfo()`; - Lines: `15–15 and 929–930`
- **Primary worker owner:** W08 — UI, input, settings, accessibility and focus. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Extend the HUD's biome-name mapping to cover every current `Biome` enum value in its declared order, or use an exhaustive enum-to-name mapping with a safe fallback for genuinely unknown values. Keep the persisted enum values and world-generation rules unchanged.
- **Required regression test:** - Add a focused test that verifies every declared `Biome` value resolves to a non-empty, non-`?` display name.
- Explicitly cover IDs 0–12, including the nine currently unmapped values.
- Verify an out-of-range value still takes the safe fallback path without throwing.
- Verify the F3 information text displays representative late-enum biomes such as `DenseForest`, `Ocean`, `Mountain`, and `Meadow`.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #185 — Bug: Cave entrance predicates never carve the surface voxel

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** The versioned cave predicates exclude the surface coordinate at their boundary. V7's upper-depth guard prevents its entrance field from reaching the surface; V8 adds a second field but uses a minimum depth of one. The chunk builder includes the surface layer in its generated data, and the block classifiers preserve the surface material whenever the cave predicate returns false. The shallow noise threshold therefore produces subsurface cavities rather than surface breaches. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/WorldGenV7.cs`; - Method: `IsCave(int x, int y, int z, int surfaceHeight)`; - Lines: `267–292`; - File: `scripts/WorldGenV8.cs`; - Method: `IsCave(int x, int y, int z, int surfaceHeight)`; - Lines: `195–205`; - File: `scripts/WorldGenV7.cs`; - Method: `BlockAtV7(...)`; - Lines: `388–455`; - File: `scripts/WorldGenV8.cs`; - Method: `BlockAtV8(...)`; - Lines: `318–386`; - File: `scripts/VoxelWorld.cs`; - Methods: `IsCave(...)`, `GenerateChunkData(Vector2I cp)`; - Lines: `390–400`, `1052–1090`; - File: `tests/WorldgenTests.cs`; - Method: `CheckV8TerrainQuality()`; - Lines: `521–585`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Implement the surface-breach behavior in a new generator version (V12), leaving V1–V11 cave predicates and generated output unchanged. In the new version, allow the versioned entrance predicate to select the surface voxel for eligible deterministic locations, while retaining the intended dry-land, preset, bedrock, and vertical-bound checks. Keep the cave/entrance decisions as pure functions of world coordinates and the world seed, and continue applying them through the existing block-generation path. Do not mutate chunk arrays outside the established generation/mutation architecture.
- **Required regression test:** - Choose fixed seeds and coordinates where the new entrance field qualifies; assert that the generated surface voxel is Air (or the explicitly intended entrance material) and that it connects to cave voxels immediately below.
- Assert that nearby non-qualifying columns retain their original surface block and that no disconnected shallow pocket is counted as an entrance.
- Cover normal, amplified, sky, and flat presets as applicable; verify ocean/shoreline exclusions and the y=0..Height-1 bounds.
- Generate both sides of positive and negative chunk boundaries in forward and reverse order; assert identical cave-mouth voxels and chunk hashes.
- Verify aquifer water, magma pockets, and ores retain their intended priority/placement around entrance voxels.
- Assert that pinned V1–V11 golden chunk hashes remain unchanged and that all existing generator compatibility tests pass.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #186 — Bug: Cactus placement ignores the world seed

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** The shared `Hash2(long x, long z)` helper is a deterministic coordinate hash, not a world-seeded hash. `GetCactusAt()` continues using it without a generator-version branch, while the newer tree and surface-decoration paths use `GeneratorV8.CoordinateHash(...)`, which incorporates the generator's master seed and a feature-specific salt. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `GetCactusAt(int tx, int tz)`; - Lines: `698–736`; - File: `scripts/VoxelWorld.cs`; - Method: `GenerateChunkData(Vector2I cp)`; - Lines: `1144–1167`; - File: `scripts/VoxelWorld.cs`; - Method: `Hash2(long x, long z)`; - Lines: `3270–3275`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Add a feature-salted, world-seeded cactus hash behind a new generator version (V12). Use that hash for both the cactus placement roll and height. Keep the existing `Hash2` behavior for V1–V11 so their generated chunks and existing saves remain byte-identical. Do not introduce a shared or unseeded RNG.
- **Required regression test:** - For V12, compare two fixed, different master seeds over a deterministic set of coordinates that are desert columns in both worlds; assert that the cactus placement/height layout differs somewhere in the sample.
- For one fixed V12 seed, assert repeated cactus queries at the same positive and negative coordinates return identical results.
- Generate the same V12 chunk set in forward and reverse order in independent worlds and compare chunk hashes.
- Assert cactus placement remains restricted to valid desert columns and preserves the existing surface/height boundaries.
- Re-run V1–V11 golden chunk hashes and assert all legacy generator outputs remain byte-identical.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #187 — Bug: V7 ore vein size counts revisited positions

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** `TryV7Vein()` stamps the current position and then selects any of the six axis directions without excluding a return to an earlier position. The `step < size` loop advances regardless of whether `StampOre()` actually changed a voxel. `StampOre()` correctly avoids overwriting anything except `Stone`, but that protection also makes a revisited position a no-op; the caller does not account for that result. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/WorldGenV7.cs`; - Method: `TryV7Vein()`; - Lines: `672–699`; - File: `scripts/VoxelWorld.cs`; - Method: `StampOre()`; - Lines: `1322–1336`; - File: `docs/SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md`; - Section: Resource and crafting progression; - Lines: `42–47`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Correct the placement accounting in a new generator version (V12): track positions already visited by each vein and count only distinct successful stone-to-ore placements toward its target size. Keep the attempt budget bounded for paths that encounter caves, existing ores, or other non-stone blocks. Do not change V1–V11 generation output or their golden hashes.
- **Required regression test:** - Add a deterministic walk fixture that backtracks onto an earlier position and assert that the repeated coordinate does not consume a distinct-placement slot.
- In a solid-stone fixture, assert that each generated vein places the configured number of distinct ore voxels within its size range.
- Verify caves, existing ores, and non-stone blocks are not overwritten and cannot cause an unbounded walk.
- Cover positive and negative coordinates, all chunk edges/corners, and reverse chunk-generation order.
- Pin V1–V11 output hashes to prove the fix is isolated to the new generator version.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #188 — [Worldgen] Prevent overlapping canopies from suppressing tree trunks

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** Tree geometry is emitted in one pass with a single priority rule: every non-air tree block is accepted only when the destination is Air. `TreeBlockForV7()` can return foliage for positions offset from its own trunk, while it returns logs for the source tree's trunk. Because `GenerateChunkData()` processes one tree completely before the next, an earlier foliage result can claim a later tree's trunk cells. The outcome depends on the deterministic scan order rather than an explicit trunk-over-foliage precedence rule. **Issue confidence:** Suspected.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `GenerateChunkData(Vector2I cp)`; - Lines: `1100–1138`; - File: `scripts/WorldGenV7.cs`; - Method: `TreeBlockForV7()`; - Lines: `521–600`; - File: `scripts/WorldGenV8.cs`; - Method: `GetTreeAtV8()`; - Lines: `255–310`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Add an explicit tree-block priority rule behind a new generator version. For example, stamp all trunks and authored branch logs for the chunk's padded tree neighborhood before stamping foliage, then let foliage fill only remaining Air cells. Preserve the current V1–V11 output byte-for-byte; do not alter historical generator paths or saved-world behavior. Keep the existing padded-neighborhood clipping so tree geometry remains complete across chunk boundaries.
- **Required regression test:** - Find and pin a deterministic seed/coordinate fixture with two valid trees whose canopy and trunk volumes overlap; assert every expected trunk log remains present.
- Cover overlap along both X and Z axes, including a pair whose crowns intersect across a chunk boundary.
- Generate the involved chunks in forward and reverse order in independent worlds and assert byte-identical output for the new generator version.
- Assert foliage still fills unoccupied canopy cells and does not overwrite any trunk or branch log.
- Re-run V1–V11 golden chunk hashes and assert their output remains unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #189 — [Startup][High] FindSpawn returns an unsafe hard-coded fallback

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** High / P1.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** The search result and fallback are represented by the same non-nullable `Vector3`. `bestD` records whether a candidate was found, but its sentinel value is never checked after the scan. There is no retry or final validation path. The hard-coded Y coordinate is also independent of the current world generator's sea level and preset. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not performed: the repository was inspected through GitHub, and a local Godot 4.7.2 .NET runtime/.NET SDK was not available in the execution environment.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `FindSpawn()`; - Lines: `2617–2639`; - File: `scripts/VoxelWorld.cs`; - Method: `SpawnBlocked()`; - Lines: `2645–2667`; - File: `scripts/WorldGenV8.cs`; - Methods: `SampleSurface()`, `BlockAtV8()`; - Lines: `112–175`, `317–338`; - File: `scripts/Main.cs`; - Method: `_Ready()`; - Lines: `44–56`; - File: `scripts/Worlds.cs`; - Members: `CurrentGenVersion`, `Create()`; - Lines: `77`, `164–171`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Keep the existing deterministic candidate selection and its valid-result behavior, but represent “no safe candidate” explicitly (for example, with a `TryFindSpawn` result). On failure, run a bounded deterministic outward retry that validates the actual generated floor, capsule clearance, fluid/hazard occupancy, island membership, and world bounds. If no safe location exists, propagate a controlled startup failure/state instead of placing the player at a guessed coordinate. Preserve existing generator output, save data, and the existing chunk streaming/greedy-meshing pipeline.
- **Required regression test:** - Add a deterministic normal-world fixture where the current scan has no accepted candidate and the origin surface is at/below sea level + 1; verify the returned spawn is not inside terrain or water.
- Add a sky-world fixture where the origin column is not an island and no sampled column is accepted; verify the player is never placed in air over the void.
- Force all sampled candidates to be rejected by the blocker predicate; verify no unchecked default coordinate is returned.
- For normal, flat, amplified, and sky presets, assert that every successful spawn has a solid support block, sufficient headroom for the player capsule, no intersecting blocks, no lava/water hazard at body/head level, and valid height bounds.
- Verify that the existing successful spawn choices remain stable for known seeds and that initial spawn collision is ready before player movement is enabled.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #190 — Bug: Opaque faces disappear behind cutout foliage

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The project has two distinct concepts encoded separately: `Transparent` controls material routing (cutout leaf blocks intentionally remain false), while `IsFoliage`/`Occl` marks leaves as non-occluding for AO and enclosed-block optimization. The face visibility policy (`RenderFace`) and partial-shape policy (`FaceExposed`) still use only `IsTransparent`/`IsSolid` when deciding whether a neighbor occludes a face. That inconsistent classification causes cutout foliage to be treated as an opaque occluder specifically at adjacency boundaries. The `Occl` lookup is not used in either face-culling test. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/BlockDb.cs`; - Method: `RenderFace()`; - Lines: `499–519`; - File: `scripts/BlockDb.cs`; - Method: `EnsureTables()` (foliage occluder lookup); - Lines: `415–418`; - File: `scripts/Mesher.cs`; - Method: `Build()`; - Lines: `335–354`; - File: `scripts/Mesher.cs`; - Method: `BuildFaceMergeKey()`; - Lines: `813–817`; - File: `scripts/BlockShapes.cs`; - Method: `FaceExposed()`; - Lines: `111–115`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Keep leaf `Transparent=false` so foliage remains on the cutout material/depth path. Centralize or consistently apply a face-occlusion predicate that treats `IsFoliage(neighbor)` as non-occluding, while preserving the intentional foliage/foliage overlap rule. Apply the matching rule in `BlockShapes.FaceExposed()` so slab/stair partial faces are emitted against foliage where appropriate. Ensure only the solid/shape backing face is emitted at an opaque/foliage interface to avoid duplicate coplanar cutout geometry. This is a rendering-only correction: do not change persisted IDs, save data, or generator versions.
- **Required regression test:** - Add a full-cube fixture and assert the correct one-sided output for `Stone ↔ Leaves`, `Cobblestone ↔ BirchLeaves`, and each of `SpruceLeaves`, `JungleLeaves`, and `SwampLeaves` in both per-face and greedy modes.
- Exercise the actual `Mesher.Build()` payload and assert the backing opaque face exists while the foliage face against the opaque block is not duplicated.
- Add slab and stair fixtures adjacent to each applicable foliage type; assert microcell faces behind foliage are exposed according to the partial shape's occupied cells.
- Add a cross-chunk fixture with the leaf at local X/Z chunk edge and the solid block in the neighboring chunk; assert the backing surface is emitted consistently regardless which adjacent chunk is built first.
- Retain tests for foliage/foliage overlap, glass/water interfaces, same-water internal-face suppression, and existing greedy/per-face surface parity.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #191 — Bug: Cross-plant lighting normals are inverted for both winding passes

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** `EmitDoubleSided()` treats the right-handed cross product as the front-face normal without accounting for the project's clockwise front-face convention. The canonical cube-face path already follows the correct convention by pairing clockwise `FaceVerts` with outward `FaceDirs`; the cross-plant helper does not follow that normal-sign rule. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was unavailable in the audit environment.
- **Evidence locations:** - File: `scripts/Mesher.cs`; - Method: `EmitDoubleSided()` (called by `AddCross()`); - Lines: `913–930`; - Reference face convention: `scripts/BlockDb.cs`, `FaceDirs` / `FaceVerts`, lines `246–263`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Derive the shading normal from each pass's final triangle winding using Godot's clockwise-front convention. With the existing vertex orders, the canonical pass should receive `-n` and the reversed pass should receive `n` (or use an equivalent winding-aware normal calculation). Preserve the two-sided geometry, existing vertex order, shared opaque surface, greedy meshing, and block IDs.
- **Required regression test:** - Extend the existing isolated TallGrass fixture to inspect every emitted triangle's vertex normal.
- In both per-face and greedy modes, cover both diagonal planes and both winding passes; assert that the assigned normal opposes `(b - a).Cross(c - a)` for every clockwise front-facing triangle.
- Assert that the two emitted sides of each plane have opposite normals and remain visible with the current backface-culling material.
- Verify the atlas and texture-array opaque shader paths both receive the corrected normals.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #192 — Bug: Gameplay camera far plane clips configured far LOD

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** Render-distance ownership is split between the world and the camera. `Gfx.ApplyPreset()` updates `VoxelWorld.ViewDistance` and `VoxelWorld.LodRadius`, and those values drive chunk streaming and FarLod tile selection. The gameplay camera's `far` value is instead hard-coded in `scenes/main.tscn`. `Main._Ready()` applies the graphics preset and view distance, while `Player._Ready()` configures camera exposure attributes but does not synchronize its far plane. There is no corresponding camera update in the preset application path. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scenes/main.tscn`; - Method: `Camera3D` node configuration; - Lines: 38–41; - File: `scripts/Gfx.cs`; - Method: `Presets`; - Lines: 28–37; - Method: `ApplyPreset()`; - Lines: 219–229; - File: `scripts/Main.cs`; - Method: `_Ready()`; - Lines: 61–65; - File: `scripts/Player.cs`; - Method: `_Ready()`; - Lines: 180–199
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Make the active gameplay camera far plane derive from the same authoritative render-distance configuration as chunk streaming and FarLod. Recalculate it whenever the preset, custom view distance, or LOD radius changes. The calculation should cover the farthest retained tile geometry (including FarLod's tile-selection margin and tile footprint), not merely copy the LOD radius. Keep the value finite and check depth-precision and fog behavior at the maximum supported range. Do not change chunk streaming, the existing LOD mesh representation, or greedy meshing.
- **Required regression test:** - Add an integration test using the gameplay scene that obtains the active Player/Camera3D and verifies its far plane covers the maximum visible geometry extent for every renderer preset.
- Verify LOW with LOD disabled retains its intended near-view behavior.
- Verify HIGH (384 m), ULTRA (512 m), MAX (640 m), and MAX · FSR2 update the camera range when applied at runtime.
- Verify F7/F8 custom view-distance changes also update the camera range when chunk streaming extends beyond the previous far plane.
- Include a rendered capture with terrain between 300 m and the configured LOD extent and verify it is not clipped; check near geometry, depth precision, and distance haze at the maximum range.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #193 — Bug: Opaque lava enters transparent sorting because its shader writes ALPHA

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Meshing, voxel rendering, lighting surfaces and collision.
- **Verified root cause / investigation status:** The shader assigns `ALPHA` only to copy the texture alpha. This is unnecessary for the opaque lava atlas tile: `PaintLava()` fills the tile with `FillNoise(..., 1.0f, ...)` and paints additional pixels using three-component `Color` values, which default to alpha 1.0.

Godot 4.7's spatial shader reference states that if `ALPHA` is read or written, the material goes through the transparent pipeline. It also defines `depth_draw_opaque` as drawing depth only for opaque geometry. The current test checks for `depth_draw_opaque`, checks that `ALPHA = tex.a;` is present, and checks only that the literal `blend_mix` token is absent. It therefore treats the erroneous alpha write as proof of opaque behavior and misses the actual pipeline switch.

The related chunk configuration makes the sorting consequence concrete: glass and water use `SortingUseAabbCenter`, but lava does not. All chunk surface MeshInstances are created at the chunk origin, while their mesh vertices occupy positions throughout the chunk.

Godot reference: https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/BlockDb.cs`; - Shader: `LitLavaShader`; - Lines: `1191–1193`, `1243–1266`; - Method: `PaintLava()`; - Lines: `2374–2399`; - File: `scripts/Mesher.cs`; - Method: `Build()`; - Lines: `320–321`; - File: `scripts/Chunk.cs`; - Methods: `Setup()`, `NewSurfaceMesh()`, `ApplyPayload()`; - Lines: `39–51`, `63–75`, `109–116`; - File: `tests/GlassTests.cs`; - Method: `TestLavaDepthBehavior()`; - Lines: `50–57`; - File: `README.md`; - Section: `Transparency and cutout rendering`; - Lines: `121–132`
- **Primary worker owner:** W02 — Meshing, voxel rendering, lighting surfaces and collision. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Remove the unnecessary `ALPHA = tex.a` output from `LitLavaShader` so the shader retains the default opaque alpha behavior while preserving `depth_draw_opaque`, emissive output, and the existing dedicated lava surface. Do not move lava into the glass surface or add another mesh/node. Keep the existing cast-shadow policy unchanged unless a separate design requirement calls for lava shadows.

Correct `TestLavaDepthBehavior()` so it asserts that the lava shader does not write `ALPHA` (rather than requiring that assignment), and retain checks for the opaque depth mode and emissive output. Add a rendered regression fixture for a lava block in front of and behind glass/water in the same chunk, viewed from both directions. Preserve the existing mesher routing, greedy meshing, chunk streaming, block IDs, and save compatibility.
- **Required regression test:** - Assert the lava shader source does not read or write `ALPHA`, does not opt into a blend mode, and retains `depth_draw_opaque`.
- Assert the procedural lava atlas tile is fully opaque at representative crust, glow, and bubble pixels.
- Render a same-chunk fixture with glass at local `(8, 10, 8)` and lava at `(8, 10, 9)`; capture from positive and negative Z directions and verify the lava blocks glass behind it while glass in front still blends.
- Repeat the fixture with water instead of glass and verify water/lava depth order.
- Verify greedy and per-face meshing both continue routing lava into the dedicated lava surface, without adding it to the opaque terrain payload.
- Verify glass and water retain their current alpha blending, AABB-center sorting, two-sided/back-face policies, and no-shadow settings.
- Re-run the existing transparent-material tests and the full headless suite.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #194 — Bug: Far LOD overlaps chunk meshes retained beyond ViewDistance

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The two systems use different definitions of the full-resolution streamed area. Chunk lifetime is controlled by `ViewDistance + UnloadMargin`, but Far-LOD surface ownership is controlled by `ViewDistance` alone. No shared visual-ownership radius or visibility transition prevents both renderers from owning the same coordinates. **Issue confidence:** Confirmed by static analysis. The two radii and the mesh-generation paths are explicit in the source. Runtime z-fighting was not reproduced because this audit environment has no local Godot or .NET runtime.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `UnloadFar(Vector2I pcp)`; - Lines: `2909–2922`; - Related constant: `UnloadMargin`, lines `34–40`; - File: `scripts/FarLod.cs`; - Methods: `StreamedChunks(Vector2I pcp)`, `TileSkip(Vector2I tc, Rect2I streamed)`, `BuildArrays(Vector2I tc, Rect2I skip)`; - Lines: `149–163`, `206–264`; - File: `scripts/Gfx.cs`; - Method/data: `Presets`; - Lines: `28–40`; - File: `scripts/Chunk.cs`; - Methods: `Setup(VoxelWorld world, Vector2I chunkPos)`, `NewSurfaceMesh(string nodeName, bool noShadow)`; - Lines: `34–72`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #192 (streamed chunk retention/LOD overlap vs camera far-plane clipping).
- **Required implementation:** Give chunk rendering and Far LOD one shared definition of visual ownership. If retained chunks are intended to stay visible during the unload hysteresis window, have Far LOD exclude the full-resolution radius (`ViewDistance + UnloadMargin`). Alternatively, explicitly hide/fade retained chunk meshes outside the view-distance radius before allowing LOD geometry to render there. Keep the unload/cache hysteresis and current Far-LOD tile architecture; do not rebuild terrain generation or change persisted data.
- **Required regression test:** - With LOD enabled and `ViewDistance = 2`, stream an edge chunk, move the player so that the chunk is at offsets 3 and 4, and verify Far-LOD mesh generation excludes its 4 m cells while the chunk node remains active.
- Move until that chunk is actually unloaded and verify the corresponding LOD cells are then generated, with no gap or duplicate coverage.
- Use a flat-height terrain fixture and assert that no full-resolution chunk top face and Far-LOD top quad occupy the same world-space cell.
- Repeat the transition on all four horizontal edges and across negative X/Z chunk coordinates.
- Verify LOD-disabled LOW and LOD-enabled presets, and preserve existing tile revision/stale-payload behavior.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #195 — Do not derive replacement noise seeds for legacy worlds with incomplete manifests

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** World generation, seeds, terrain caches and spawn.
- **Verified root cause / investigation status:** Manifest deserialization is being treated as metadata validation. `ReadJson<T>()` checks JSON parsing and non-null deserialization, but does not validate required `WorldManifest` members. `ResolveWorld()` then uses a generic current seed-derivation fallback without considering the selected generator version or whether the manifest represents a migrated pre-seed world. **Issue confidence:** Confirmed by static analysis. The fallback and its use by legacy noise configuration are directly visible. Runtime reproduction was not performed because this audit environment has no local repository checkout, Godot executable or .NET SDK.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `ResolveWorld()`; - Lines: `290–294`; - File: `scripts/VoxelWorld.cs`; - Method: `_Ready()`; - Lines: `169–193`; - File: `scripts/Worlds.cs`; - Members: `WorldManifest.NoiseSeeds`, `LegacyNoiseSeeds`, `DeriveNoiseSeeds()`; - Lines: `9–21`, `83–117`; - File: `scripts/Worlds.cs`; - Method: `MigrateLegacyIfNeeded()`; - Lines: `352–360`
- **Primary worker owner:** W03 — World generation, seeds, terrain caches and spawn. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Validate the manifest's generator version and noise-seed metadata before constructing the world. Reject missing or malformed seed arrays rather than silently deriving replacements. Preserve the existing V1 migration path, which explicitly supplies `LegacyNoiseSeeds`; do not change any existing generator algorithm, persisted IDs, world format, or valid manifest contents.
- **Required regression test:** - Deserialize a valid-JSON V1 manifest with `genVersion` and `seed` present but `noiseSeeds` absent; assert it is rejected or enters an explicit recovery path before chunk generation.
- Repeat with arrays containing fewer than five and more than five entries.
- Verify the migrated legacy V1 fixture still uses `LegacyNoiseSeeds` and retains the existing V1 anchor hash.
- Verify valid V1, V3, V5 and V6 manifests retain their existing golden hashes.
- Verify a current V7+ world with valid metadata remains unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #196 — [Streaming][Moderate] Prune rebuild requests outside the active streaming area

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The fresh queue is treated as spatially scoped, but the separate rebuild queue is treated only as a deduplicated work list. Streaming-area invalidation is implemented in `RebuildQueue()` and `UnloadFar()` for fresh requests, chunk nodes and caches, but not for pending rebuild requests. The worker prioritizes every rebuild without checking whether its coordinate is still relevant, and the only spatial rejection occurs in `ApplyBuild()`, after the expensive worker-side build. **Issue confidence:** Confirmed by static analysis. The queue and lifecycle paths establish the stale-work ordering and late rejection; runtime reproduction was not available in this audit environment.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `RebuildQueue(Vector2I pcp)`; - Lines: `2783–2807`; - Method: `EnqueueRebuild(Vector2I cp)`; - Lines: `2834–2845`; - Method: `WorkerLoop(int element)`; - Lines: `2504–2571`; - Method: `ApplyBuild(Vector2I cp, MeshPayload payload)`; - Lines: `2579–2614`; - Method: `UnloadFar(Vector2I pcp)`; - Lines: `2909–2938`; - File: `scripts/Mesher.cs`; - Method: `Build(VoxelWorld world, Vector2I cp)`; - Lines: `228–260`; - File: `scripts/VoxelWorld.cs`; - Method: `ChunkData(Vector2I cp)`; - Lines: `1553–1578`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Reconcile pending rebuild requests with the retained streaming radius when the player chunk or view distance changes. Remove obsolete pending coordinates from both the priority queue and membership set, or use an equivalent generation/region validity check before a worker starts `Mesher.Build()`. Preserve rebuilds that remain in range and preserve all voxel edits in the existing mutation/save data. Also ensure that a stale request already dequeued but waiting for a payload slot is revalidated before starting the build. Do not bypass `SetBlock` / `ApplyEdit`, alter persisted IDs or save formats, or change existing greedy-meshing and chunk-streaming architecture.
- **Required regression test:** - Queue rebuilds for several old-region chunks, move the player outside their retained radius before dispatch, and assert those obsolete requests are not meshed.
- Verify fresh chunks in the new region are dispatched without waiting for stale old-region rebuilds.
- Assert obsolete rebuild processing does not repopulate `_dataCache`, block-state or voxel-light caches outside the retained area.
- Repeat the scenario across several distinct regions with queued edits and assert the pending rebuild queue remains spatially bounded.
- Verify in-range rebuild requests still apply after an edit and that saved edits are present when the player returns to the old region.
- Cover view-distance increases/decreases and negative chunk coordinates.
- Verify shutdown still drains published payloads and releases every semaphore reservation.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #197 — Bug: Fresh chunk priorities retain obsolete movement direction

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The priority value is stored inside `.NET PriorityQueue<Vector2I, float>` when `RebuildQueue()` or `Enqueue()` inserts an item. Updating `_velocityDir` does not update existing heap keys. The only normal refresh condition in `_Process()` is `pcp != _lastPlayerChunk`; direction changes within a chunk are therefore invisible to the pending queue. Teleport callers do not reset the world's previous-position sample, and the direction calculation does not distinguish continuous motion from relocation. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `_Process(double delta)`, `RebuildQueue(Vector2I pcp)`, `Priority(Vector2I cp, Vector2I pcp)`; - Lines: `2681–2707`, `2784–2819`; - File: `scripts/DevConsole.cs`; - Methods: `Execute()/tp command branch`, `Execute()/spawn command branch`; - Lines: `168–176`, `244–247`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Refresh pending fresh-request priorities when movement intent changes materially, rather than only at chunk-boundary crossings. Add a small direction/speed hysteresis so normal input jitter does not repeatedly clear and rebuild the queue. Treat known teleports/respawns as relocation events by resetting the previous-position sample and movement bias before queue priorities are computed. Preserve active worker ownership and the existing chunk queue/WorkerThreadPool/mesher path; do not re-enqueue an already active coordinate or change persisted data.
- **Required regression test:** - Queue fresh chunks while moving east, reverse west without leaving the current chunk, and assert the immediately adjacent west chunk is dequeued before a farther east chunk.
- Queue with a nonzero heading, stop inside the same chunk, and assert the old directional bias no longer determines dequeue order.
- Teleport with `GlobalPosition` changed and `Velocity = Vector3.Zero`; assert the destination queue is not biased by the displacement vector.
- Cover positive and negative chunk coordinates and movement along both X and Z axes.
- Apply small alternating direction noise around the refresh threshold and assert hysteresis prevents repeated full queue rebuilds.
- Verify active requests are not duplicated or invalidated by reprioritization and that normal nearest-first ordering remains when stationary.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #198 — Bug: Stale chunk rebuild requests block fresh streaming after rapid travel

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The fresh and rebuild pipelines have separate priority queues and membership sets, but only the fresh queue is reset by `RebuildQueue()`. `UnloadFar()` prunes loaded chunk nodes and data/light caches without reconciling rebuild queue state. `WorkerLoop()` gives `_rebuildQueue` unconditional precedence and performs no current-region validation before `Mesher.Build()`. `ApplyBuild()` has a valid range guard, but it is too late to prevent stale worker work, and the apply stage likewise prioritizes the rebuild payload queue. **Issue confidence:** Confirmed by static analysis.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Methods: `RebuildAll()`, `_Process(double delta)`, `RebuildQueue(Vector2I pcp)`, `WorkerLoop(int element)`, `ApplyBuild(Vector2I cp, MeshPayload payload)`, `UnloadFar(Vector2I pcp)`; - Lines: `2847–2854`, `2701–2707`, `2784–2807`, `2504–2572`, `2579–2614`, `2909–2940`; - File: `scripts/DevConsole.cs`; - Method: `Execute()/greedy command branch`; - Lines: `232–240`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** Reconcile the rebuild queue when the streaming center or view distance changes: atomically remove queued rebuild coordinates that are no longer in the current retention region or no longer have a resident chunk, and update the membership set consistently. Validate a dequeued rebuild's relevance before meshing as a final guard. Discard obsolete completed rebuild payloads without allowing them to consume the fresh-apply budget ahead of current-region chunks. Preserve edits through the existing world mutation/save data; dropping an obsolete mesh rebuild must not drop voxel edits. Coordinate this with active request tracking so already-running work cannot reinsert stale requests or create duplicate builds. Keep the existing worker pool, chunk streaming, and greedy meshing architecture.
- **Required regression test:** - At high view distance, load many chunks, call `RebuildAll()`, then teleport to a distant region before the rebuild queue drains; assert obsolete old-region rebuilds are not meshed and a nearby destination chunk is applied promptly.
- Repeat with block-edit rebuild requests, verifying the edit itself remains persisted and the still-resident edited chunk receives its required remesh.
- Shrink view distance while rebuilds are queued and assert out-of-radius work is pruned; expand it again and assert newly required fresh chunks can be scheduled.
- Verify in-radius rebuild requests still complete and that stale completed rebuild payloads do not block fresh payload application.
- Cover positive and negative chunk coordinates, boundary crossings, and repeated rapid teleports.
- Assert no duplicate active work, no stale payload application, no lost SetBlock/ApplyEdit changes, and no unbounded queue growth.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #199 — [Performance][Moderate] Scene exit blocks on chunk and far-LOD workers

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** Worker completion is coupled to scene-tree destruction through blocking joins on the main thread. Cancellation only wakes workers blocked on the payload-slot semaphore; it is not a cancellation mechanism for a mesher or far-LOD build already in progress. The current lifecycle therefore trades a clean worker shutdown for a potentially long main-thread scene-transition stall. **Issue confidence:** Confirmed by static analysis
- **Evidence locations:** - File: `scripts/PauseMenu.cs`; - Method: `QuitToMenu()`; - Lines: `182–187`; - File: `scripts/VoxelWorld.cs`; - Method: `_ExitTree()`; - Lines: `244–265`; - File: `scripts/FarLod.cs`; - Method: `_ExitTree()`; - Lines: `71–80`
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #201 (asynchronous scene-exit drain vs cancellation checkpoints inside chunk builds).
- **Required implementation:** Introduce a shutdown/drain phase that keeps the world and its worker-owned data alive while outstanding chunk and LOD tasks complete, then frees the scene and disposes synchronization primitives. Drive the drain asynchronously across process frames or through a transition coordinator/loading scene rather than blocking in `_ExitTree()`. Preserve cancellation for workers waiting on payload capacity, drain every queued payload exactly once, and do not dispose world state or payload reservations until all active tasks have stopped.
- **Required regression test:** - Start a world with chunk-worker tasks in progress, request a transition to the menu, and verify that the main thread continues advancing frames while shutdown drains.
- Repeat with far-LOD tasks in flight and with both worker systems active simultaneously.
- Cover shutdown when no tasks are active, when workers are waiting for payload slots, and when a worker is already inside meshing/LOD generation.
- Assert that the scene is freed only after all workers stop, pending payload count and reservations return to zero, and no deferred result applies to a freed world.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #200 — Incremental block-light updates allocate a full state volume per emitter edit

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Low / P3.
- **Subsystem:** Cross-cutting performance, diagnostics, automated tests and documentation.
- **Verified root cause / investigation status:** The incremental region solver pools two of its three full-volume scratch arrays but allocates the optional state volume directly. The allocation is conditional on any neighboring chunk having state data, not on the size or number of non-default state entries. As a result, one stateful block anywhere in the 3×3 neighborhood enables a full-neighborhood allocation for every eligible emitter edit in that region. **Issue confidence:** Confirmed by static analysis. The allocation size and frequency follow directly from the cached-neighborhood branch and array length calculation; a runtime allocation profile was not collected during this audit.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `TryApplyIncrementalBlockLightLocked()`; - Lines: `1877–1910`, `1913–1939`, `1962–1966`; - Related method: `SetBlock()`; - Lines: `2147–2167`; - Related method: `ChunkBlockStates()`; - Lines: `2041–2055`
- **Primary worker owner:** W10 — Cross-cutting performance, diagnostics, automated tests and documentation. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** None identified during this refresh.
- **Required implementation:** When `hasNonDefaultStates` is true, rent `regionStates` from `ArrayPool<byte>.Shared` instead of allocating it directly. Clear the used `volume` range before copying the optional chunk state arrays, then return the rented state buffer in the existing `finally` block. Keep the no-state path as `null` and preserve the existing state-copy layout, incremental solver call, cache ownership, and mutation path.
- **Required regression test:** - In a disposable world, warm all nine chunk-data and voxel-light cache entries, then ensure one neighboring chunk contains a non-default slab or stair state.
- After warm-up, repeatedly toggle a torch through `VoxelWorld.SetBlock()` in the cached region and measure current-thread allocated bytes; verify the per-edit full-volume allocation is gone.
- Compare incremental block-light output with a full block-light solve after both emitter additions and removals.
- Cover a neighborhood with no state edits, one stateful neighbor, multiple stateful neighbors, and stateful chunks at each edge/corner of the 3×3 region.
- Verify rented state scratch is cleared between uses so state bytes from a previous rent cannot affect cells whose current state is zero.
- Verify buffer return on normal completion and exception paths, and confirm stateful block placement/save compatibility remains unchanged.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #201 — Bug: Chunk workers ignore shutdown cancellation after meshing starts

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** The cancellation token source is wired to the capacity wait, not to the actual worker operation. `WorkerLoop()` does not pass a token to `Mesher.Build()`; the mesher's snapshot, optional chunk generation, greedy-face construction, per-block traversal and collision construction have no cancellation checks. The existing post-build `_alive` check protects publication but cannot interrupt work already underway. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not available: the connected test machine reported that its monthly Desktop Commander usage was exhausted, so the Godot test runner could not be launched. No runtime timing or teardown duration is claimed.
- **Evidence locations:** - File: `scripts/VoxelWorld.cs`; - Method: `_ExitTree()`; - Lines: 244–250; - File: `scripts/VoxelWorld.cs`; - Method: `WorkerLoop(int element)`; - Lines: 2528–2551; - File: `scripts/Mesher.cs`; - Method: `Build(VoxelWorld world, Vector2I cp)`; - Lines: 230–269 and 269–425; - File: `scripts/Mesher.cs`; - Method: `BuildOpaqueGreedy(...)`; - Lines: 678–769; - File: `scripts/VoxelWorld.cs`; - Methods: `GenerateChunkData(Vector2I cp)`, `ChunkData(Vector2I cp)`; - Lines: 1052–1141 and 1553–1577; - File: `scripts/MainMenu.cs`; - Method: `StartWorld(string dir)`; - Lines: 396–400; - File: `scripts/PauseMenu.cs`; - Method: `QuitToMenu()`; - Lines: 182–187
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #199 (cancel active meshing vs non-blocking scene-exit orchestration).
- **Required implementation:** Propagate a cancellation token into the active chunk-build operation and check it at bounded safe points in chunk-data generation, snapshot preparation, greedy meshing, per-block meshing and collision construction. Keep the existing worker-group wait and post-build publication guard as final safety checks. Ensure cancellation before `MeshPayload` construction returns every rented surface/collision buffer and scratch resource in a `finally` path, and preserve the existing semaphore reservation release in `WorkerLoop()`. Cancellation must not cache partially generated chunk data or change deterministic generation output. Keep the existing chunk streaming and greedy meshing architecture; no persisted IDs, save format or generator version need to change.
- **Required regression test:** - Start a worker build, deterministically pause it after the payload-slot wait, cancel world shutdown, and assert the build exits at a cancellation checkpoint without completing the full mesher traversal.
- Exercise cancellation during a chunk-data cache miss and verify no partial chunk is inserted into `_dataCache`; a later uncanceled generation must retain its existing deterministic result.
- Exercise cancellation in both greedy and per-face meshing paths and assert every rented surface/collision/scratch buffer is returned.
- Verify a worker blocked on the payload semaphore and a worker already inside meshing both terminate during scene exit.
- Verify no payload is published after shutdown and all completed-payload reservations and pending-payload counters are balanced.
- Run the existing V1–V11 world-generation compatibility and greedy/per-face geometry tests to prove no output changes.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

### #202 — Bug: Far LOD evicted-tile completion can strand queued tile builds

- **Current GitHub status:** Open; no state change made.
- **Severity / priority:** Moderate / P2.
- **Subsystem:** Chunk streaming, scheduling, Far LOD and worker lifecycle.
- **Verified root cause / investigation status:** Queue scheduling is coupled to successful application of a payload to a currently existing tile. The completion path handles the bookkeeping removal first, but the early return for a missing tile bypasses the only subsequent `Dispatch()` call. There is no independent per-frame dispatch pump in `FarLod` to recover the queued work. **Issue confidence:** Confirmed by static analysis. Runtime reproduction was not available because the connected test machine's monthly Desktop Commander usage was exhausted. The in-flight limit, tile removal, completion callback and early return establish the stalled-queue path.
- **Evidence locations:** - File: `scripts/FarLod.cs`; - Method: `UpdateAround(Vector2I pcp)`; - Lines: 137–147; - File: `scripts/FarLod.cs`; - Method: `Dispatch()`; - Lines: 175–190; - File: `scripts/FarLod.cs`; - Method: `BuildTask(Vector2I tc, Rect2I skip)`; - Lines: 193–200; - File: `scripts/FarLod.cs`; - Method: `ApplyTile(Vector2I tc, LodPayload? payload)`; - Lines: 393–400
- **Primary worker owner:** W01 — Chunk streaming, scheduling, Far LOD and worker lifecycle. (Provisional ledger owner; no GitHub assignee applied.)
- **Related / duplicate issue IDs:** Related but distinct: #94 and #147 (evicted tile queue progress vs stale skip-mask payloads and worker exception cleanup).
- **Required implementation:** Move queue dispatch into an unconditional completion/finally path that runs after removing the completed coordinate from `_inFlight`, regardless of whether the tile still exists, the payload is null, or the result is discarded. Apply a payload only when its tile remains valid; do not create a replacement node for an evicted tile. Keep worker completion and main-thread scene-tree application separated. Coordinate with the existing stale-payload and exception-cleanup paths so all outcomes release bookkeeping exactly once.
- **Required regression test:** - Dispatch exactly `BuildLimit` tasks, remove all their tile records before completion, queue replacement tiles, then complete the old tasks and assert replacement tasks are dispatched without another `UpdateAround()` call.
- Repeat with only some in-flight tiles removed and assert valid tiles still apply while queued tiles make progress.
- Cover null payload completion and a tile removed then recreated at the same coordinate.
- Verify shutdown does not dispatch new work after `_alive` becomes false.
- Verify no old payload is applied to a missing/recreated tile and no coordinate remains stuck in `_inFlight`.
- **PR / commit reference:** Pending — not implemented; no remediation PR or commit exists at this baseline.
- **Final verification:** Pending remediation. At minimum, run the Godot 4.7.2 .NET build and `scripts/verify.sh`, execute the focused regression suite, and attach issue-specific runtime/performance evidence where the report requires it. Preserve existing world/save compatibility and existing chunk streaming/greedy meshing unless the issue itself is scoped to those systems.
- **Closure status:** Open — unchanged by this ledger refresh; no issue closure requested or performed.

## Dispatch and closure rules

1. Preserve all GitHub issue numbers, including the true-duplicate pairs. Duplicate classification in this document is an implementation-planning aid, not authorization to close or renumber issues.
2. A worker should update the PR/commit, test evidence, final verification and closure fields only after the implementation is present and verified. Do not mark an issue fixed solely because a related issue or shared helper was implemented.
3. Any owner reassignment must be evidence-based and recorded here. Until the prompt-pack W01–W10 table is supplied and reconciled, treat the current lane assignments as provisional.
4. For world-generation work, use a new generator version where generation output changes; preserve existing generator outputs. Persisted `BlockId`/`ItemId` values remain append-only. Route voxel changes through the existing mutation seam (`SetBlock` / `ApplyEdit`).
5. Do not replace the existing greedy mesher or chunk streaming architecture as a shortcut for individual fixes. Add regression coverage for shutdown, stale asynchronous payloads, save compatibility and performance boundaries when relevant.

## Refresh change report

- **Verified current open baseline:** 110 issues (#93–#202).
- **Changes versus prior issue list:** Not computable from repository history because no prior issue ledger or dated issue-list snapshot was found on `main` or a remediation branch. The two-page live refresh is authoritative for this PR.
- **Ownership changes:** No comparison to the prompt-pack assignment table is possible because that table was not supplied/found. All 110 issues have exactly one provisional W01–W10 lane in this file; no GitHub assignments were changed.
- **Fixed/stale assessment:** The current source spot-check did not establish a fully fixed issue that could be removed from the open set. #167 is partly stale/partly addressed: `PLAN.md` now reports 52 registered suites, while the current `ROADMAP.md` milestone table still says 48; keep #167 open for the remaining documentation reconciliation. Investigation-only #137 remains unverified at runtime and must not be treated as a confirmed renderer defect or as fixed.
- **Issue comments reviewed:** #149 has one comment and its menu-preview workload is included above; all other currently open issues report zero comments.
- **Closed issues:** Excluded from the active remediation ledger. No closed issue was reopened, imported as an active fix, or modified.
