using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace Minecraft;

public static class RunnerReliabilityTests
{
    public static async Task Run()
    {
        CheckMemoryAccounting();
        CheckDocumentationAndVerification();
        await CheckCompletionCoordinator();
        await CheckRunnerLifecyclesAsync();
    }

    private static void CheckMemoryAccounting()
    {
        VoxelWorld world = new();
        Chunk diagnosticChunk = new();
        try
        {
            SetField(world, "_noiseSeeds", new[] { 11, 22, 33 });
            VoxelCacheMemorySnapshot before = world.GetCacheMemorySnapshot();
            Vector2I chunk = new(2, -3);

            Field<Dictionary<Vector2I, byte[]>>(world, "_dataCache").Add(chunk, new byte[17]);
            Field<Dictionary<Vector2I, byte[]>>(world, "_blockStateCache").Add(chunk, new byte[11]);
            Field<Dictionary<Vector2I, byte[]>>(world, "_skyLightCache").Add(chunk, new byte[13]);
            Field<Dictionary<Vector2I, byte[]>>(world, "_blockLightCache").Add(chunk, new byte[19]);
            Field<Dictionary<Vector2I, VoxelLightData>>(world, "_voxelLightCache")
                    .Add(chunk, new VoxelLightData(new byte[7], new byte[4]));
            Field<Dictionary<Vector2I, Dictionary<int, BlockId>>>(world, "_edits")
                    .Add(chunk, new Dictionary<int, BlockId> { [1] = BlockId.Stone });
            Field<Dictionary<Vector2I, Dictionary<int, byte>>>(world, "_blockStateEdits")
                    .Add(chunk, new Dictionary<int, byte> { [1] = 1 });

            Field<HashSet<Vector3I>>(world, "_crops").Add(new Vector3I(1, 2, 3));
            Field<HashSet<Vector2I>>(world, "_sourceScannedChunks").Add(chunk);
            Field<HashSet<Vector2I>>(world, "_cropScannedChunks").Add(chunk);
            Field<Dictionary<long, Vector3I?>>(world, "_campfireCache")
                    .Add(1L, new Vector3I(1, 2, 3));
            Field<Dictionary<long, VoxelWorld.StructureInfo?>>(world, "_structureCache")
                    .Add(2L, new VoxelWorld.StructureInfo(1, 10, 20, 30, 42));

            Field<Dictionary<Vector2I, byte[]>>(world, "_editTops").Add(chunk, new byte[256]);
            Field<Dictionary<Vector2I, byte[]>>(world, "_editTopBlocks").Add(chunk, new byte[256]);
            Field<Dictionary<Vector2I, Chunk>>(world, "_chunks")
                    .Add(new Vector2I(9, 9), diagnosticChunk);
            Field<Dictionary<long, VillageInfo?>>(world, "_villageCache")
                    .Add(3L, new VillageInfo(VillageTheme.Plains, 100, 70, 120, 0, 0, 42));
            world.Containers.Add("chest:1", default!);
            world.Furnaces.Add("furnace:1", default!);
            world.Flags.Add("flag:1", true);
            world.Villagers.Add("village:1/resident-0", default!);
            world.Quests.Add("resident-0/quest-0", default!);
            Field<Dictionary<Vector3I, byte>>(world, "_lightSources")
                    .Add(new Vector3I(3, 4, 5), 14);
            world.SavedInventory = [default!];
            world.SavedBoats = [default!];
            Field<PriorityQueue<Vector2I, float>>(world, "_queue")
                    .Enqueue(new Vector2I(1, 2), 1.0f);
            Field<HashSet<Vector2I>>(world, "_queued").Add(new Vector2I(1, 2));
            Field<PriorityQueue<Vector2I, float>>(world, "_rebuildQueue")
                    .Enqueue(new Vector2I(3, 4), 2.0f);
            Field<HashSet<Vector2I>>(world, "_rebuildQueued").Add(new Vector2I(3, 4));

            VoxelCacheMemorySnapshot populated = world.GetCacheMemorySnapshot();
            Profiler.RecordCacheMemory(populated);
            long trackedHighWaterBeforeClear = Profiler.TrackedManagedBytesHighWaterEstimate;
            long metadataHighWaterBeforeClear = Profiler.PersistentMetadataBytesHighWaterEstimate;
            TestAssert.Equal(17L, populated.BlockDataBytes, "block array accounting uses exact lengths");
            TestAssert.Equal(11L, populated.BlockStateBytes, "block-state array accounting uses exact lengths");
            TestAssert.Equal(13L, populated.SkyLightBytes, "sky-light array accounting uses exact lengths");
            TestAssert.Equal(19L, populated.BlockLightBytes, "block-light array accounting uses exact lengths");
            TestAssert.Equal(11L, populated.PackedLightBytes,
                    "packed and direct-sky arrays are both included exactly");
            TestAssert.Equal(12L, populated.GeneratorSeedArrayBytes,
                    "the persistent generator seed array is counted by exact length");
            TestAssert.Equal(512L, populated.EditTopArrayBytes,
                    "both persistent edit-top byte arrays are counted exactly");
            TestAssert.Check(Profiler.GeneratorSeedArrayHighWaterBytes >= 12L,
                    "generator seed-array high-water is retained by the profiler");
            TestAssert.Check(populated.CacheIndexBytesEstimate > before.CacheIndexBytesEstimate,
                    "voxel cache dictionary capacities are estimated");
            TestAssert.Check(populated.CropRegistryBytesEstimate > before.CropRegistryBytesEstimate,
                    "persistent crop registry capacity is estimated");
            TestAssert.Check(populated.SourceScanRegistryBytesEstimate > before.SourceScanRegistryBytesEstimate,
                    "source-scan registry capacity is estimated");
            TestAssert.Check(populated.CropScanRegistryBytesEstimate > before.CropScanRegistryBytesEstimate,
                    "crop-scan registry capacity is estimated separately from the source-scan registry");
            TestAssert.Check(populated.CampfireSiteCacheBytesEstimate > before.CampfireSiteCacheBytesEstimate,
                    "campfire site cache capacity is estimated");
            TestAssert.Check(populated.StructureSiteCacheBytesEstimate > before.StructureSiteCacheBytesEstimate,
                    "structure site cache capacity is estimated");
            TestAssert.Check(populated.VillageSiteCacheBytesEstimate > before.VillageSiteCacheBytesEstimate,
                    "generator-v8 village site cache capacity is estimated");
            TestAssert.Check(populated.EditTopIndexBytesEstimate > before.EditTopIndexBytesEstimate,
                    "edit-top dictionary overhead is estimated separately from its arrays");
            TestAssert.Check(populated.WorldStateRegistryBytesEstimate > before.WorldStateRegistryBytesEstimate,
                    "saved string-keyed world registries and staging lists are represented");
            TestAssert.Check(populated.StreamingQueueBytesEstimate > before.StreamingQueueBytesEstimate,
                    "streaming priority queues and deduplication sets are estimated");
            TestAssert.Check(populated.ChunkIndexBytesEstimate > before.ChunkIndexBytesEstimate,
                    "built-chunk index capacity is represented");
            TestAssert.Check(populated.EditMetadataBytesEstimate > before.EditMetadataBytesEstimate,
                    "block and shape edit dictionary capacities are represented");
            TestAssert.Check(populated.LightSourceBytesEstimate > before.LightSourceBytesEstimate,
                    "light-source dictionary capacity is represented");
            TestAssert.Check(populated.TrackedManagedBytesEstimate > before.TrackedManagedBytesEstimate,
                    "aggregate tracked managed-memory estimate includes persistent caches");

            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            _ = world.GetCacheMemorySnapshot();
            long allocationAfter = GC.GetAllocatedBytesForCurrentThread();
            TestAssert.Equal(0L, allocationAfter - allocationBefore,
                    "a populated memory snapshot does not allocate to measure collection capacity");

            ClearTrackedCollections(world);
            VoxelCacheMemorySnapshot cleared = world.GetCacheMemorySnapshot();
            Profiler.RecordCacheMemory(cleared);
            TestAssert.Check(Profiler.CropScanRegistryBytesHighWaterEstimate
                    >= populated.CropScanRegistryBytesEstimate,
                    "crop-scan high-water is retained after collection trimming");
            TestAssert.Check(Profiler.TrackedManagedBytesHighWaterEstimate
                    >= Math.Max(trackedHighWaterBeforeClear, populated.TrackedManagedBytesEstimate),
                    "tracked managed-memory high-water survives a lower post-trim snapshot");
            TestAssert.Check(Profiler.PersistentMetadataBytesHighWaterEstimate
                    >= Math.Max(metadataHighWaterBeforeClear, populated.PersistentMetadataBytesEstimate),
                    "persistent metadata high-water survives a lower post-trim snapshot");
            TestAssert.Equal(0L, cleared.VoxelArrayBytes, "clearing byte-array caches drops exact array totals");
            TestAssert.Equal(0L, cleared.EditTopArrayBytes, "clearing edit-top arrays drops their exact total");
            TestAssert.Check(cleared.CropRegistryBytesEstimate <= populated.CropRegistryBytesEstimate,
                    "trimming the crop set releases its estimated retained capacity");
            TestAssert.Check(cleared.SourceScanRegistryBytesEstimate <= populated.SourceScanRegistryBytesEstimate,
                    "trimming the source-scan registry releases its estimated retained capacity");
            TestAssert.Check(cleared.CropScanRegistryBytesEstimate <= populated.CropScanRegistryBytesEstimate,
                    "trimming the crop-scan registry releases its estimated retained capacity");
            TestAssert.Check(cleared.CampfireSiteCacheBytesEstimate <= populated.CampfireSiteCacheBytesEstimate,
                    "trimming the campfire cache releases its estimated retained capacity");
            TestAssert.Check(cleared.StructureSiteCacheBytesEstimate <= populated.StructureSiteCacheBytesEstimate,
                    "trimming the structure cache releases its estimated retained capacity");
            TestAssert.Check(cleared.StreamingQueueBytesEstimate < populated.StreamingQueueBytesEstimate,
                    "trimming streaming queues lowers their retained-capacity estimate");
            TestAssert.Check(cleared.TrackedManagedBytesEstimate < populated.TrackedManagedBytesEstimate,
                    "trimming persistent collections lowers the aggregate estimate");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(diagnosticChunk))
                diagnosticChunk.Free();
            world.Free();
        }
    }

    private static T Field<T>(VoxelWorld world, string name) where T : class
    {
        FieldInfo? field = typeof(VoxelWorld).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(world) as T
                ?? throw new InvalidOperationException($"Missing VoxelWorld diagnostic field '{name}'.");
    }

    private static void SetField<T>(VoxelWorld world, string name, T value)
    {
        FieldInfo field = typeof(VoxelWorld).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing VoxelWorld diagnostic field '{name}'.");
        field.SetValue(world, value);
    }

    private static void ClearTrackedCollections(VoxelWorld world)
    {
        Clear(Field<Dictionary<Vector2I, byte[]>>(world, "_dataCache"));
        Clear(Field<Dictionary<Vector2I, byte[]>>(world, "_blockStateCache"));
        Clear(Field<Dictionary<Vector2I, byte[]>>(world, "_skyLightCache"));
        Clear(Field<Dictionary<Vector2I, byte[]>>(world, "_blockLightCache"));
        Clear(Field<Dictionary<Vector2I, VoxelLightData>>(world, "_voxelLightCache"));
        Clear(Field<Dictionary<Vector2I, Dictionary<int, BlockId>>>(world, "_edits"));
        Clear(Field<Dictionary<Vector2I, Dictionary<int, byte>>>(world, "_blockStateEdits"));
        Clear(Field<HashSet<Vector3I>>(world, "_crops"));
        Clear(Field<HashSet<Vector2I>>(world, "_sourceScannedChunks"));
        Clear(Field<HashSet<Vector2I>>(world, "_cropScannedChunks"));
        Clear(Field<Dictionary<long, Vector3I?>>(world, "_campfireCache"));
        Clear(Field<Dictionary<long, VoxelWorld.StructureInfo?>>(world, "_structureCache"));
        Clear(Field<Dictionary<long, VillageInfo?>>(world, "_villageCache"));
        Clear(Field<Dictionary<Vector2I, byte[]>>(world, "_editTops"));
        Clear(Field<Dictionary<Vector2I, byte[]>>(world, "_editTopBlocks"));
        Clear(Field<Dictionary<Vector3I, byte>>(world, "_lightSources"));
        Clear(world.Containers);
        Clear(world.Furnaces);
        Clear(world.Flags);
        Clear(world.Villagers);
        Clear(world.Quests);
        Clear(Field<Dictionary<Vector2I, Chunk>>(world, "_chunks"));
        SetField(world, "_noiseSeeds", Array.Empty<int>());
        Field<PriorityQueue<Vector2I, float>>(world, "_queue").Clear();
        Field<PriorityQueue<Vector2I, float>>(world, "_queue").TrimExcess();
        Clear(Field<HashSet<Vector2I>>(world, "_queued"));
        Field<PriorityQueue<Vector2I, float>>(world, "_rebuildQueue").Clear();
        Field<PriorityQueue<Vector2I, float>>(world, "_rebuildQueue").TrimExcess();
        Clear(Field<HashSet<Vector2I>>(world, "_rebuildQueued"));
        world.SavedInventory?.Clear();
        world.SavedInventory?.TrimExcess();
        world.SavedBoats?.Clear();
        world.SavedBoats?.TrimExcess();
    }

    private static void Clear<TKey, TValue>(Dictionary<TKey, TValue> dictionary) where TKey : notnull
    {
        dictionary.Clear();
        dictionary.TrimExcess();
    }

    private static void Clear<T>(HashSet<T> set)
    {
        set.Clear();
        set.TrimExcess();
    }

    private static void CheckDocumentationAndVerification()
    {
        string root = ProjectSettings.GlobalizePath("res://");
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        string roadmap = File.ReadAllText(Path.Combine(root, "ROADMAP.md"));
        string plan = File.ReadAllText(Path.Combine(root, "PLAN.md"));
        string benchmarkDocs = File.ReadAllText(Path.Combine(root, "docs", "PERFORMANCE_BENCHMARKS.md"));
        string readmeFlow = Regex.Replace(readme, @"\s+", " ");
        string roadmapFlow = Regex.Replace(roadmap, @"\s+", " ");
        string benchmarkDocFlow = Regex.Replace(benchmarkDocs, @"\s+", " ");
        string testRunner = File.ReadAllText(Path.Combine(root, "tests", "TestRunner.cs"));
        string testingDocs = File.ReadAllText(Path.Combine(root, "docs", "development", "TESTING.md"));
        string rendererMatrix = File.ReadAllText(Path.Combine(root, "docs", "RENDERER_QUALITY_MATRIX.md"));
        string verify = File.ReadAllText(Path.Combine(root, "scripts", "verify.sh"));
        string soak = File.ReadAllText(Path.Combine(root, "scripts", "soak-survival.sh"));

        MatchCollection registrations = Regex.Matches(
                testRunner, @"RunSuite(?:Async)?\s*\(""([a-z0-9-]+)""");
        HashSet<string> suiteNames = [];
        foreach (Match registration in registrations)
            suiteNames.Add(registration.Groups[1].Value);
        TestAssert.Check(registrations.Count > 0,
                "documentation test sees the registered suite list");
        TestAssert.Equal(registrations.Count, suiteNames.Count,
                "every registered headless suite has a unique name");
        TestAssert.Check(suiteNames.Contains("runner-reliability"),
                "the lifecycle regression suite is registered with the real TestRunner");
        TestAssert.Check(!Regex.IsMatch(readme, @"\d[\d,]* checks[^\n]*across \d+ suites")
                && !Regex.IsMatch(roadmap, @"\d[\d,]* checks[^\n]*across \d+ suites"),
                "active documentation does not pin historical suite and assertion totals");
        TestAssert.Check(readme.Contains("registered suites", StringComparison.Ordinal)
                && readme.Contains("PERFORMANCE_BENCHMARKS.md", StringComparison.Ordinal)
                && roadmap.Contains("registered headless suites", StringComparison.Ordinal)
                && roadmap.Contains("persistent managed-memory categories", StringComparison.Ordinal),
                "README and ROADMAP describe the live verification and performance-reporting workflow");
        TestAssert.Check(readmeFlow.Contains("immutable save snapshot", StringComparison.Ordinal)
                && readmeFlow.Contains("background save worker", StringComparison.Ordinal)
                && readmeFlow.Contains("`_Process()` does not serialize or perform disk I/O", StringComparison.Ordinal)
                && readmeFlow.Contains("Explicit save and quit flush older work", StringComparison.Ordinal),
                "README records the integrated W01 persistence contract");
        TestAssert.Check(roadmapFlow.Contains("immutable save snapshot", StringComparison.Ordinal)
                && roadmapFlow.Contains("background save worker", StringComparison.Ordinal)
                && roadmapFlow.Contains("`_Process()` performs no serialization or disk I/O", StringComparison.Ordinal)
                && roadmapFlow.Contains("Explicit save and quit flush older work", StringComparison.Ordinal),
                "ROADMAP records the integrated W01 persistence contract");
        TestAssert.Check(plan.Contains("Current world generator: V11", StringComparison.Ordinal)
                && plan.Contains("World save format: V4", StringComparison.Ordinal)
                && plan.Contains(".NET 10 SDK", StringComparison.Ordinal),
                "project decisions record the current generator, save and SDK baseline");
        TestAssert.Check(testingDocs.Contains("current TestRunner.cs are authoritative", StringComparison.Ordinal)
                && testingDocs.Contains("GODOT_TEST_TIMEOUT", StringComparison.Ordinal),
                "testing guidance directs readers to current runner output and timeout configuration");
        string[] presetNames = ["LOW", "BALANCED", "HIGH", "ULTRA", "MAX", "MAX · FSR2"];
        TestAssert.Check(readme.Contains("six graphics-quality presets", StringComparison.Ordinal),
                "README summarizes the six renderer presets");
        foreach (string presetName in presetNames)
            TestAssert.Check(rendererMatrix.Contains(presetName, StringComparison.Ordinal),
                    $"renderer matrix documents the {presetName} preset");
        TestAssert.Check(benchmarkDocFlow.Contains("capacity-based estimates", StringComparison.Ordinal)
                && !Regex.IsMatch(benchmarkDocFlow, @"For non-empty collections it reports capacity-based estimates\.[^\n]*For non-empty collections")
                && benchmarkDocFlow.Contains("source-scan", StringComparison.Ordinal)
                && benchmarkDocFlow.Contains("crop-scan", StringComparison.Ordinal)
                && benchmarkDocFlow.Contains("high-water", StringComparison.Ordinal)
                && benchmarkDocFlow.Contains("no per-frame scan", StringComparison.Ordinal)
                && benchmarkDocFlow.Contains("object graphs", StringComparison.Ordinal)
                && VoxelCacheMemorySnapshot.AccountingNote.Contains("VoxelLighting thread-static", StringComparison.Ordinal)
                && VoxelCacheMemorySnapshot.AccountingNote.Contains("village-site cache", StringComparison.Ordinal)
                && VoxelCacheMemorySnapshot.AccountingNote.Contains("empty collection backing storage", StringComparison.Ordinal)
                && VoxelCacheMemorySnapshot.AccountingNote.Contains("generator seed array", StringComparison.Ordinal)
                && VoxelCacheMemorySnapshot.AccountingNote.Contains("Source-scan and crop-scan", StringComparison.Ordinal),
                "benchmark documentation and runtime reports expose explicit memory-accounting exclusions");
        TestAssert.Check(verify.Contains("GODOT_TEST_TIMEOUT", StringComparison.Ordinal)
                && verify.Contains("timeout --signal=TERM", StringComparison.Ordinal),
                "verification documentation is backed by the bounded headless script");
        TestAssert.Check(soak.Contains("SOAK_TIMEOUT_SECONDS", StringComparison.Ordinal)
                && soak.Contains("timeout --signal=TERM", StringComparison.Ordinal),
                "soak documentation is backed by the bounded soak wrapper");
        HashSet<string> accountedCollections = [
            "_chunks", "_noiseSeeds", "SavedInventory", "SavedBoats", "Containers", "Furnaces", "Flags",
            "Villagers", "Quests", "_dataCache", "_edits", "_blockStateEdits",
            "_blockStateCache", "_lightSources", "_sourceScannedChunks", "_cropScannedChunks",
            "_skyLightCache", "_blockLightCache", "_voxelLightCache", "_crops", "_queue", "_queued",
            "_rebuildQueue", "_rebuildQueued", "_applyFresh", "_applyRebuild",
            "_campfireCache", "_structureCache", "_villageCache", "_editTops", "_editTopBlocks",
            "_colliderReadyChunks",
        ];
        foreach (FieldInfo field in typeof(VoxelWorld).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType != typeof(string)
                    && typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType))
                TestAssert.Check(accountedCollections.Contains(field.Name),
                        $"world-owned collection '{field.Name}' is represented in memory diagnostics");
        }
    }

    private static async Task CheckCompletionCoordinator()
    {
        int exitCode = -1;
        int reportedCode = -1;
        int exitRequests = 0;
        int cleanupCount = 0;
        List<Exception> failures = [];
        int success = await RunnerCompletion.ExecuteAsync(
                async () => await Task.Yield(),
                [
                    () => { cleanupCount++; return Task.CompletedTask; },
                    async () => { await Task.Yield(); cleanupCount++; },
                ],
                failures.Add,
                code => { exitCode = code; exitRequests++; },
                code => reportedCode = code);
        TestAssert.Equal(0, success, "successful asynchronous runner work returns zero");
        TestAssert.Equal(0, exitCode, "successful completion requests a zero process exit");
        TestAssert.Equal(0, reportedCode, "completion reporter receives the successful result");
        TestAssert.Equal(1, exitRequests, "completion requests process exit exactly once");
        TestAssert.Equal(2, cleanupCount, "all successful cleanup steps run in order");
        TestAssert.Equal(0, failures.Count, "successful completion reports no failures");

        InvalidOperationException operationFailure = new("asynchronous report failed");
        bool cleanupAfterFailure = false;
        exitCode = -1;
        int failed = await RunnerCompletion.ExecuteAsync(
                async () =>
                {
                    await Task.Yield();
                    throw operationFailure;
                },
                [
                    () => Task.FromException(new IOException("first cleanup failed")),
                    () => { cleanupAfterFailure = true; return Task.CompletedTask; },
                ],
                failures.Add,
                code => exitCode = code);
        TestAssert.Equal(1, failed, "an exception after an await becomes a failed runner result");
        TestAssert.Equal(1, exitCode, "asynchronous work failure requests a non-zero exit");
        TestAssert.Check(failures.Contains(operationFailure),
                "the original asynchronous report exception is observable");
        TestAssert.Check(cleanupAfterFailure,
                "a cleanup exception does not bypass later cleanup steps");

        bool cleanupAfterBrokenLogger = false;
        exitCode = -1;
        int reporterFailure = await RunnerCompletion.ExecuteAsync(
                () => Task.FromException(new InvalidOperationException("work failed")),
                [
                    () => { cleanupAfterBrokenLogger = true; return Task.CompletedTask; },
                ],
                _ => throw new InvalidOperationException("diagnostic logger failed"),
                code => exitCode = code);
        TestAssert.Equal(1, reporterFailure, "a failing diagnostic callback cannot turn failure into success");
        TestAssert.Equal(1, exitCode, "runner still requests a non-zero exit when reporting throws");
        TestAssert.Check(cleanupAfterBrokenLogger,
                "a failing diagnostic callback cannot bypass cleanup");

        bool laterCleanupRan = false;
        int finalCode = -1;
        int resultCallbackFailure = await RunnerCompletion.ExecuteAsync(
                () => Task.CompletedTask,
                [
                    () => Task.FromException(new IOException("finalizer failed")),
                    () => { laterCleanupRan = true; return Task.CompletedTask; },
                ],
                failures.Add,
                code => finalCode = code,
                _ => throw new InvalidOperationException("result reporter failed"));
        TestAssert.Equal(1, resultCallbackFailure,
                "cleanup and final-result reporting exceptions are observable");
        TestAssert.Equal(1, finalCode,
                "a completion reporter exception is reflected in the process exit code");
        TestAssert.Check(laterCleanupRan,
                "cleanup continues after an earlier finalization exception");
    }

    private static async Task CheckRunnerLifecyclesAsync()
    {
        await CheckBenchmarkRunnerLifecycleAsync();
        await CheckSoakRunnerLifecycleAsync();
    }

    private static async Task CheckBenchmarkRunnerLifecycleAsync()
    {
        // Failure before the first frame await.
        {
            BenchmarkLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(
                    () => Task.FromException(new InvalidOperationException("before first frame")));
            TestAssert.Equal(1, code, "benchmark failure before the first frame await is observed");
            TestAssert.Equal(1, harness.Failures.Count,
                    "the benchmark failure reporter receives the pre-await exception");
            TestAssert.Equal(1, harness.ExitCode, "pre-await benchmark failure requests a non-zero exit");
            TestAssert.Equal(1, harness.ExitRequests, "pre-await failure requests exit exactly once");
            TestAssert.Equal(2, harness.FrameWaits, "benchmark cleanup still awaits both scene frames");
            TestAssert.Equal(1, harness.SettleWaits, "benchmark cleanup still waits for scene settle");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "pre-await benchmark failure still deletes the temporary world");
            TestAssert.Check(RunnerField<Node?>(harness.Runner, "_main") == null,
                    "benchmark failure releases the scene root");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Failure after the first frame await.
        {
            BenchmarkLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(async () =>
            {
                await harness.Runner.WaitForProcessFrameAsync();
                throw new InvalidOperationException("after first frame");
            });
            TestAssert.Equal(1, code, "benchmark failure after the first frame await is observed");
            TestAssert.Equal(1, harness.Failures.Count,
                    "the benchmark failure reporter receives the first-frame exception");
            TestAssert.Equal(1, harness.ExitCode, "post-first-frame failure requests a non-zero exit");
            TestAssert.Equal(3, harness.FrameWaits,
                    "cleanup frames still run after a first-frame report failure");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "post-first-frame benchmark failure still deletes the temporary world");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Failure after subsequent frame awaits.
        {
            BenchmarkLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(async () =>
            {
                for (int i = 0; i < 3; i++)
                    await harness.Runner.WaitForProcessFrameAsync();
                throw new InvalidOperationException("after later frames");
            });
            TestAssert.Equal(1, code, "benchmark failure after later frame awaits is observed");
            TestAssert.Equal(1, harness.Failures.Count,
                    "the benchmark failure reporter receives the later-frame exception");
            TestAssert.Equal(5, harness.FrameWaits,
                    "all cleanup frames run after a later-frame report failure");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "later-frame benchmark failure still deletes the temporary world");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Repeated finish triggers.
        {
            BenchmarkLifecycleHarness harness = new();
            bool duplicateOperationRan = false;
            Task<int> first = harness.Runner.BeginCompletion(() => Task.CompletedTask);
            Task<int> duplicate = harness.Runner.BeginCompletion(() =>
            {
                duplicateOperationRan = true;
                return Task.CompletedTask;
            });
            TestAssert.Check(ReferenceEquals(first, duplicate),
                    "repeated benchmark finish triggers return the same completion task");
            int code = await first;
            TestAssert.Equal(0, code, "successful benchmark completion produces exit code zero");
            TestAssert.Equal(0, harness.Failures.Count, "successful benchmark completion reports no failure");
            TestAssert.Equal(0, harness.ExitCode, "successful completion requests a zero process exit");
            TestAssert.Equal(1, harness.ExitRequests, "successful completion requests exit exactly once");
            TestAssert.Equal(1, harness.DeleteCalls, "successful completion deletes the temporary world once");
            TestAssert.Check(!duplicateOperationRan,
                    "a duplicate finish trigger does not run the report operation twice");
            Task<int> afterCompletion = harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Check(ReferenceEquals(first, afterCompletion),
                    "a late finish trigger reuses the completed lifecycle task");
            TestAssert.Equal(1, harness.ExitRequests,
                    "a late finish trigger cannot request a second exit");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Scene teardown while asynchronous work is pending.
        {
            BenchmarkLifecycleHarness harness = new();
            TaskCompletionSource<bool> pending = new();
            Task<int> completion = harness.Runner.BeginCompletion(async () =>
            {
                await pending.Task;
            });
            TestAssert.Check(!completion.IsCompleted,
                    "benchmark completion stays pending while asynchronous work is outstanding");
            harness.Scene.QueueFree();
            TestAssert.Check(harness.Scene.IsQueuedForDeletion(),
                    "the benchmark scene fixture is queued for teardown during pending work");
            pending.SetResult(true);
            int code = await completion;
            TestAssert.Equal(0, code, "scene teardown during pending work still completes successfully");
            TestAssert.Equal(1, harness.ExitRequests, "teardown during pending work requests exit once");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "teardown during pending work still cleans the temporary world");
            TestAssert.Check(RunnerField<Node?>(harness.Runner, "_main") == null,
                    "teardown during pending work releases the scene root safely");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Cleanup failure remains observable.
        {
            BenchmarkLifecycleHarness harness = new();
            harness.DeleteResult = false;
            int code = await harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Equal(1, code, "failed benchmark world cleanup produces a failed result");
            TestAssert.Check(harness.Failures.Count > 0,
                    "failed benchmark cleanup reaches the failure reporter");
            TestAssert.Equal(1, harness.ExitCode, "failed benchmark cleanup requests a non-zero exit");
            TestAssert.Equal(1, harness.DeleteCalls, "the failed cleanup attempt is recorded");
            TestAssert.Check(RunnerField<Node?>(harness.Runner, "_main") == null,
                    "reference release still runs after a cleanup failure");
            harness.Runner._ExitTree();
            TestAssert.Equal(2, harness.DeleteCalls,
                    "the emergency benchmark fallback retries only the failed cleanup");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Cleanup and reference release remain idempotent after success.
        {
            BenchmarkLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Equal(0, code, "benchmark idempotence scenario starts from success");
            harness.Runner._ExitTree();
            harness.Runner._ExitTree();
            TestAssert.Equal(1, harness.DeleteCalls,
                    "repeated benchmark teardown cannot delete the world twice");
            TestAssert.Equal(1, harness.ExitRequests,
                    "repeated benchmark teardown cannot request another exit");
            TestAssert.Check(RunnerField<Node?>(harness.Runner, "_main") == null,
                    "repeated benchmark reference release stays safe");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }
    }

    private static async Task CheckSoakRunnerLifecycleAsync()
    {
        // Failure before the first frame await.
        {
            SoakLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(
                    () => Task.FromException(new InvalidOperationException("before first frame")));
            TestAssert.Equal(1, code, "soak failure before the first frame await is observed");
            TestAssert.Equal(1, harness.Failures.Count,
                    "the soak failure reporter receives the pre-await exception");
            TestAssert.Equal(1, harness.ExitCode, "pre-await soak failure requests a non-zero exit");
            TestAssert.Equal(2, harness.FrameWaits, "soak cleanup still awaits both scene frames");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "pre-await soak failure still deletes the temporary world");
            TestAssert.Equal(1, harness.ExitRequests, "pre-await soak failure requests exit once");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Failure after the first and after later frame awaits.
        {
            SoakLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(async () =>
            {
                await harness.Runner.WaitForProcessFrameAsync();
                throw new InvalidOperationException("after first frame");
            });
            TestAssert.Equal(1, code, "soak failure after the first frame await is observed");
            TestAssert.Equal(1, harness.Failures.Count,
                    "the soak failure reporter receives the first-frame exception");
            TestAssert.Equal(3, harness.FrameWaits,
                    "soak cleanup frames still run after a first-frame failure");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "post-first-frame soak failure still deletes the temporary world");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }
        {
            SoakLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(async () =>
            {
                for (int i = 0; i < 3; i++)
                    await harness.Runner.WaitForProcessFrameAsync();
                throw new InvalidOperationException("after later frames");
            });
            TestAssert.Equal(1, code, "soak failure after later frame awaits is observed");
            TestAssert.Equal(1, harness.Failures.Count,
                    "the soak failure reporter receives the later-frame exception");
            TestAssert.Equal(5, harness.FrameWaits,
                    "soak cleanup frames still run after a later-frame failure");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "later-frame soak failure still deletes the temporary world");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Repeated finish triggers and successful exit code.
        {
            SoakLifecycleHarness harness = new();
            bool duplicateOperationRan = false;
            Task<int> first = harness.Runner.BeginCompletion(() => Task.CompletedTask);
            Task<int> duplicate = harness.Runner.BeginCompletion(() =>
            {
                duplicateOperationRan = true;
                return Task.CompletedTask;
            });
            TestAssert.Check(ReferenceEquals(first, duplicate),
                    "repeated soak finish triggers return the same completion task");
            int code = await first;
            TestAssert.Equal(0, code, "successful soak completion produces exit code zero");
            TestAssert.Equal(0, harness.Failures.Count, "successful soak completion reports no failure");
            TestAssert.Equal(0, harness.ExitCode, "successful soak completion requests a zero process exit");
            TestAssert.Equal(1, harness.ExitRequests, "successful soak completion requests exit once");
            TestAssert.Equal(1, harness.DeleteCalls, "successful soak completion deletes the temporary world once");
            TestAssert.Check(!duplicateOperationRan,
                    "a duplicate soak finish trigger does not run the save operation twice");
            TestAssert.Check(RunnerField<VoxelWorld?>(harness.Runner, "_world") == null,
                    "successful soak completion releases world references");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Worker drain completes before temporary-world deletion.
        {
            SoakLifecycleHarness harness = new();
            int drainChecks = 0;
            harness.Runner.WorkersDrainedOverride = () =>
            {
                drainChecks++;
                harness.Events.Add("drain");
                return drainChecks >= 3;
            };
            int code = await harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Equal(0, code, "soak completes after workers drain");
            TestAssert.Equal(3, drainChecks, "the soak waits for worker shutdown to report drained");
            TestAssert.Check(harness.Events.IndexOf("delete") > harness.Events.LastIndexOf("drain"),
                    "temporary-world deletion happens only after the worker drain check succeeds");
            TestAssert.Check(harness.TemporaryWorldCleaned,
                    "the soak records temporary-world cleanup after a successful drain");
            TestAssert.Equal(1, harness.DeleteCalls, "successful soak cleanup deletes the world once");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Drain failure is observable and still permits later cleanup.
        {
            SoakLifecycleHarness harness = new();
            harness.Runner.DrainFrameLimit = 3;
            harness.Runner.WorkersDrainedOverride = () =>
            {
                harness.Events.Add("drain");
                return false;
            };
            int code = await harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Equal(1, code, "a soak worker-drain failure produces a failed result");
            TestAssert.Check(harness.Failures.Count > 0,
                    "a soak worker-drain failure reaches the failure reporter");
            TestAssert.Equal(1, harness.ExitCode, "a soak worker-drain failure requests a non-zero exit");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "temporary-world cleanup still runs after a drain failure");
            TestAssert.Check(harness.Events.IndexOf("delete") > harness.Events.LastIndexOf("drain"),
                    "deletion is still attempted only after the bounded drain wait");
            TestAssert.Check(RunnerField<VoxelWorld?>(harness.Runner, "_world") == null,
                    "references are released after a drain failure");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Cleanup failure does not skip remaining cleanup.
        {
            SoakLifecycleHarness harness = new();
            harness.DeleteResult = false;
            int code = await harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Equal(1, code, "failed soak world cleanup produces a failed result");
            TestAssert.Check(harness.Failures.Count > 0,
                    "failed soak cleanup reaches the failure reporter");
            TestAssert.Equal(1, harness.ExitCode, "failed soak cleanup requests a non-zero exit");
            TestAssert.Equal(1, harness.DeleteCalls, "the failed soak cleanup attempt is recorded");
            TestAssert.Check(!harness.TemporaryWorldCleaned,
                    "the soak records that temporary-world cleanup did not complete");
            TestAssert.Check(RunnerField<VoxelWorld?>(harness.Runner, "_world") == null,
                    "reference release still runs after soak cleanup failure");
            TestAssert.Check(RunnerField<Node?>(harness.Runner, "_main") == null,
                    "scene release still runs after soak cleanup failure");
            harness.Runner._ExitTree();
            TestAssert.Equal(2, harness.DeleteCalls,
                    "the emergency soak fallback retries only the failed cleanup");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Scene teardown while asynchronous work is pending.
        {
            SoakLifecycleHarness harness = new();
            TaskCompletionSource<bool> pending = new();
            Task<int> completion = harness.Runner.BeginCompletion(async () =>
            {
                await pending.Task;
            });
            TestAssert.Check(!completion.IsCompleted,
                    "soak completion stays pending while save work is outstanding");
            harness.Scene.QueueFree();
            pending.SetResult(true);
            int code = await completion;
            TestAssert.Equal(0, code, "soak scene teardown during pending work completes");
            TestAssert.Equal(1, harness.DeleteCalls,
                    "soak scene teardown during pending work still deletes the temporary world");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }

        // Cleanup and reference release remain idempotent after success.
        {
            SoakLifecycleHarness harness = new();
            int code = await harness.Runner.BeginCompletion(() => Task.CompletedTask);
            TestAssert.Equal(0, code, "soak idempotence scenario starts from success");
            harness.Runner._ExitTree();
            harness.Runner._ExitTree();
            TestAssert.Equal(1, harness.DeleteCalls,
                    "repeated soak teardown cannot delete the world twice");
            TestAssert.Equal(1, harness.ExitRequests,
                    "repeated soak teardown cannot request another exit");
            TestAssert.Check(RunnerField<VoxelWorld?>(harness.Runner, "_world") == null,
                    "repeated soak reference release stays safe");
            await SettleSceneFixturesAsync();
            harness.FreeFixtures();
        }
    }

    private sealed class BenchmarkLifecycleHarness
    {
        public PerformanceRunner Runner { get; } = new();
        public Node Scene { get; }
        public int FrameWaits;
        public int SettleWaits;
        public int DeleteCalls;
        public int ExitRequests;
        public int ExitCode = -1;
        public bool DeleteResult = true;
        public bool WorkersDrained = true;
        public readonly List<string> Events = [];
        public readonly List<Exception> Failures = [];

        public BenchmarkLifecycleHarness()
        {
            Scene = new Node { Name = "BenchmarkLifecycleScene" };
            Tree.Root.AddChild(Scene);
            SetRunnerField(Runner, "_main", Scene);
            SetRunnerField(Runner, "_worldDir", "user://tests/runner-lifecycle-benchmark");
            Runner.WaitForFrameOverride = () =>
            {
                FrameWaits++;
                Events.Add("frame");
                return Task.CompletedTask;
            };
            Runner.WaitForSettleOverride = () =>
            {
                SettleWaits++;
                Events.Add("settle");
                return Task.CompletedTask;
            };
            Runner.DeleteWorldOverride = _ =>
            {
                DeleteCalls++;
                Events.Add("delete");
                return DeleteResult;
            };
            Runner.WorkersDrainedOverride = () =>
            {
                Events.Add("drain");
                return WorkersDrained;
            };
            Runner.RequestExitOverride = code =>
            {
                ExitRequests++;
                ExitCode = code;
            };
            Runner.ReportFailureOverride = Failures.Add;
        }

        public void FreeFixtures()
        {
            if (GodotObject.IsInstanceValid(Scene)) Scene.Free();
            if (GodotObject.IsInstanceValid(Runner)) Runner.Free();
        }
    }

    private sealed class SoakLifecycleHarness
    {
        public SurvivalSoakRunner Runner { get; } = new();
        public Node Scene { get; }
        public int FrameWaits;
        public int DeleteCalls;
        public int ExitRequests;
        public int ExitCode = -1;
        public bool DeleteResult = true;
        public readonly List<string> Events = [];
        public readonly List<Exception> Failures = [];

        public bool TemporaryWorldCleaned =>
                RunnerField<bool>(Runner, "_temporaryWorldCleaned");

        public SoakLifecycleHarness()
        {
            Scene = new Node { Name = "SoakLifecycleScene" };
            Tree.Root.AddChild(Scene);
            SetRunnerField(Runner, "_main", Scene);
            SetRunnerField(Runner, "_soakWorldDir", "user://tests/runner-lifecycle-soak");
            SetRunnerField(Runner, "_soakPassed", true);
            SetRunnerField(Runner, "_voxelCacheBudgetBytes", long.MaxValue);
            Profiler.PendingPayloadHighWaterCount = 0;
            Profiler.VoxelCacheArrayHighWaterBytes = 0;
            Runner.WaitForFrameOverride = () =>
            {
                FrameWaits++;
                Events.Add("frame");
                return Task.CompletedTask;
            };
            Runner.DeleteWorldOverride = _ =>
            {
                DeleteCalls++;
                Events.Add("delete");
                return DeleteResult;
            };
            Runner.WorkersDrainedOverride = () =>
            {
                Events.Add("drain");
                return true;
            };
            Runner.RequestExitOverride = code =>
            {
                ExitRequests++;
                ExitCode = code;
            };
            Runner.ReportFailureOverride = Failures.Add;
        }

        public void FreeFixtures()
        {
            if (GodotObject.IsInstanceValid(Scene)) Scene.Free();
            if (GodotObject.IsInstanceValid(Runner)) Runner.Free();
        }
    }

    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    private static async Task SettleSceneFixturesAsync()
    {
        await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }

    private static T RunnerField<T>(object runner, string name)
    {
        FieldInfo field = runner.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing runner lifecycle field '{name}'.");
        return (T)field.GetValue(runner)!;
    }

    private static void SetRunnerField(object runner, string name, object? value)
    {
        FieldInfo field = runner.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing runner lifecycle field '{name}'.");
        field.SetValue(runner, value);
    }
}
