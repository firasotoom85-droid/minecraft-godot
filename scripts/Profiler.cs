using System;
using System.Threading;

namespace Minecraft;

/// <summary>
/// Cheap always-on perf counters for the F3 panel: mesh build timings
/// (recorded on worker threads, lock-guarded ring buffer) and streaming
/// queue depths (written on the main thread only).
/// </summary>
public static class Profiler
{
    private const int Window = 64;
    private static readonly Lock Lock = new();
    private static readonly double[] Samples = new double[Window];
    private static int _count;
    private static int _next;

    public static int FreshQueued;
    public static int RebuildQueued;
    public static int DataChunks;
    public static bool WorkersActive;

    public static int PendingPayloadCount;
    public static long PendingPayloadBytes;
    public static int PendingPayloadHighWaterCount;
    public static long PendingPayloadHighWaterBytes;
    public static long VoxelCacheArrayBytes;
    public static long VoxelCacheArrayHighWaterBytes;
    public static long GeneratorSeedArrayBytes;
    public static long GeneratorSeedArrayHighWaterBytes;
    public static long VoxelCacheBudgetBytes;
    public static long CacheIndexBytesEstimate;
    public static long CacheIndexBytesHighWaterEstimate;
    public static long CropRegistryBytesEstimate;
    public static long CropRegistryBytesHighWaterEstimate;
    public static long SourceScanRegistryBytesEstimate;
    public static long SourceScanRegistryBytesHighWaterEstimate;
    public static long CropScanRegistryBytesEstimate;
    public static long CropScanRegistryBytesHighWaterEstimate;
    public static long CampfireSiteCacheBytesEstimate;
    public static long CampfireSiteCacheBytesHighWaterEstimate;
    public static long StructureSiteCacheBytesEstimate;
    public static long StructureSiteCacheBytesHighWaterEstimate;
    public static long VillageSiteCacheBytesEstimate;
    public static long VillageSiteCacheBytesHighWaterEstimate;
    public static long EditTopArrayBytes;
    public static long EditTopArrayHighWaterBytes;
    public static long EditTopIndexBytesEstimate;
    public static long EditTopIndexBytesHighWaterEstimate;
    public static long WorldStateRegistryBytesEstimate;
    public static long WorldStateRegistryBytesHighWaterEstimate;
    public static long ChunkIndexBytesEstimate;
    public static long ChunkIndexBytesHighWaterEstimate;
    public static long EditMetadataBytesEstimate;
    public static long EditMetadataBytesHighWaterEstimate;
    public static long LightSourceBytesEstimate;
    public static long LightSourceBytesHighWaterEstimate;
    public static long AppliedMeshBytesEstimate;
    public static long AppliedMeshBytesHighWaterEstimate;
    public static long PooledMeshBufferBytes;
    public static long PooledMeshBufferHighWaterBytes;
    public static long StreamingQueueBytesEstimate;
    public static long StreamingQueueBytesHighWaterEstimate;
    public static long PersistentMetadataBytesEstimate;
    public static long PersistentMetadataBytesHighWaterEstimate;
    public static long TrackedManagedBytesEstimate;
    public static long TrackedManagedBytesHighWaterEstimate;
    public static double ApplyAverageMs;
    public static double ApplyMaximumMs;
    public static double ApplyStepAverageMs;
    public static double ApplyStepMaximumMs;
    public static double CollisionAverageMs;
    public static double CollisionMaximumMs;
    public static long CollisionVerticesApplied;

    private static readonly Lock RuntimeMetricsLock = new();
    private static double _applyTotalMs;
    private static long _applySamples;
    private static double _applyStepTotalMs;
    private static long _applyStepSamples;
    private static double _collisionTotalMs;
    private static long _collisionSamples;

    public static void RecordBuild(double ms)
    {
        lock (Lock)
        {
            Samples[_next] = ms;
            _next = (_next + 1) % Window;
            if (_count < Window)
            {
                _count++;
            }
        }
    }

    /// <summary>Clears the recent mesh-build window before an isolated benchmark measurement.</summary>
    public static void ResetBuildSamples()
    {
        lock (Lock)
        {
            Array.Clear(Samples, 0, Samples.Length);
            _count = 0;
            _next = 0;
        }
    }

    /// <summary>Record one completed payload waiting for main-thread application.</summary>
    public static void RecordPayloadEnqueued(long estimatedBytes)
    {
        int pending = Interlocked.Increment(ref PendingPayloadCount);
        long bytes = Interlocked.Add(ref PendingPayloadBytes, Math.Max(0L, estimatedBytes));
        UpdateMaximum(ref PendingPayloadHighWaterCount, pending);
        UpdateMaximum(ref PendingPayloadHighWaterBytes, bytes);
    }

    /// <summary>Record a payload after the main thread has removed it from the bounded queue.</summary>
    public static void RecordPayloadDequeued(long estimatedBytes)
    {
        int pending = Interlocked.Decrement(ref PendingPayloadCount);
        long bytes = Interlocked.Add(ref PendingPayloadBytes, -Math.Max(0L, estimatedBytes));
        if (pending < 0) Interlocked.Exchange(ref PendingPayloadCount, 0);
        if (bytes < 0) Interlocked.Exchange(ref PendingPayloadBytes, 0);
    }

    public static void RecordApply(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0.0) return;
        lock (RuntimeMetricsLock)
        {
            _applyTotalMs += milliseconds;
            _applySamples++;
            ApplyAverageMs = _applyTotalMs / _applySamples;
            ApplyMaximumMs = Math.Max(ApplyMaximumMs, milliseconds);
        }
    }

    /// <summary>Record one bounded main-thread resource-creation step.</summary>
    public static void RecordApplyStep(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0.0) return;
        lock (RuntimeMetricsLock)
        {
            _applyStepTotalMs += milliseconds;
            _applyStepSamples++;
            ApplyStepAverageMs = _applyStepTotalMs / _applyStepSamples;
            ApplyStepMaximumMs = Math.Max(ApplyStepMaximumMs, milliseconds);
        }
    }

    public static void RecordCollision(double milliseconds, int vertices)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0.0) return;
        lock (RuntimeMetricsLock)
        {
            _collisionTotalMs += milliseconds;
            _collisionSamples++;
            CollisionAverageMs = _collisionTotalMs / _collisionSamples;
            CollisionMaximumMs = Math.Max(CollisionMaximumMs, milliseconds);
            CollisionVerticesApplied += Math.Max(0, vertices);
        }
    }

    public static void RecordCacheMemory(VoxelCacheMemorySnapshot snapshot)
    {
        VoxelCacheArrayBytes = snapshot.VoxelArrayBytes;
        GeneratorSeedArrayBytes = snapshot.GeneratorSeedArrayBytes;
        VoxelCacheBudgetBytes = snapshot.VoxelArrayBudgetBytes;
        CacheIndexBytesEstimate = snapshot.CacheIndexBytesEstimate;
        CropRegistryBytesEstimate = snapshot.CropRegistryBytesEstimate;
        SourceScanRegistryBytesEstimate = snapshot.SourceScanRegistryBytesEstimate;
        CropScanRegistryBytesEstimate = snapshot.CropScanRegistryBytesEstimate;
        CampfireSiteCacheBytesEstimate = snapshot.CampfireSiteCacheBytesEstimate;
        StructureSiteCacheBytesEstimate = snapshot.StructureSiteCacheBytesEstimate;
        VillageSiteCacheBytesEstimate = snapshot.VillageSiteCacheBytesEstimate;
        EditTopArrayBytes = snapshot.EditTopArrayBytes;
        EditTopIndexBytesEstimate = snapshot.EditTopIndexBytesEstimate;
        WorldStateRegistryBytesEstimate = snapshot.WorldStateRegistryBytesEstimate;
        ChunkIndexBytesEstimate = snapshot.ChunkIndexBytesEstimate;
        EditMetadataBytesEstimate = snapshot.EditMetadataBytesEstimate;
        LightSourceBytesEstimate = snapshot.LightSourceBytesEstimate;
        AppliedMeshBytesEstimate = snapshot.AppliedMeshBytesEstimate;
        PooledMeshBufferBytes = snapshot.PooledMeshBufferBytes;
        StreamingQueueBytesEstimate = snapshot.StreamingQueueBytesEstimate;
        PersistentMetadataBytesEstimate = snapshot.PersistentMetadataBytesEstimate;
        TrackedManagedBytesEstimate = snapshot.TrackedManagedBytesEstimate;

        UpdateMaximum(ref VoxelCacheArrayHighWaterBytes, snapshot.VoxelArrayBytes);
        UpdateMaximum(ref GeneratorSeedArrayHighWaterBytes, snapshot.GeneratorSeedArrayBytes);
        UpdateMaximum(ref CacheIndexBytesHighWaterEstimate, snapshot.CacheIndexBytesEstimate);
        UpdateMaximum(ref CropRegistryBytesHighWaterEstimate, snapshot.CropRegistryBytesEstimate);
        UpdateMaximum(ref SourceScanRegistryBytesHighWaterEstimate, snapshot.SourceScanRegistryBytesEstimate);
        UpdateMaximum(ref CropScanRegistryBytesHighWaterEstimate, snapshot.CropScanRegistryBytesEstimate);
        UpdateMaximum(ref CampfireSiteCacheBytesHighWaterEstimate, snapshot.CampfireSiteCacheBytesEstimate);
        UpdateMaximum(ref StructureSiteCacheBytesHighWaterEstimate, snapshot.StructureSiteCacheBytesEstimate);
        UpdateMaximum(ref VillageSiteCacheBytesHighWaterEstimate, snapshot.VillageSiteCacheBytesEstimate);
        UpdateMaximum(ref EditTopArrayHighWaterBytes, snapshot.EditTopArrayBytes);
        UpdateMaximum(ref EditTopIndexBytesHighWaterEstimate, snapshot.EditTopIndexBytesEstimate);
        UpdateMaximum(ref WorldStateRegistryBytesHighWaterEstimate, snapshot.WorldStateRegistryBytesEstimate);
        UpdateMaximum(ref ChunkIndexBytesHighWaterEstimate, snapshot.ChunkIndexBytesEstimate);
        UpdateMaximum(ref EditMetadataBytesHighWaterEstimate, snapshot.EditMetadataBytesEstimate);
        UpdateMaximum(ref LightSourceBytesHighWaterEstimate, snapshot.LightSourceBytesEstimate);
        UpdateMaximum(ref PersistentMetadataBytesHighWaterEstimate, snapshot.PersistentMetadataBytesEstimate);
        UpdateMaximum(ref AppliedMeshBytesHighWaterEstimate, snapshot.AppliedMeshBytesEstimate);
        UpdateMaximum(ref PooledMeshBufferHighWaterBytes, snapshot.PooledMeshBufferBytes);
        UpdateMaximum(ref StreamingQueueBytesHighWaterEstimate, snapshot.StreamingQueueBytesEstimate);
        UpdateMaximum(ref TrackedManagedBytesHighWaterEstimate, snapshot.TrackedManagedBytesEstimate);
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref target);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    /// <summary>Average and max mesh build time over the last 64 builds.</summary>
    public static (double Avg, double Max) BuildStats()
    {
        lock (Lock)
        {
            if (_count == 0)
            {
                return (0.0, 0.0);
            }

            double sum = 0.0;
            double max = 0.0;
            for (int i = 0; i < _count; i++)
            {
                sum += Samples[i];
                if (Samples[i] > max)
                {
                    max = Samples[i];
                }
            }

            return (sum / _count, max);
        }
    }

    /// <summary>
    /// Distribution for an explicit benchmark snapshot. Copying and sorting
    /// happen only when requested; no benchmark sampling or gameplay frame
    /// performs allocations through this method.
    /// </summary>
    public static BuildTimeStats BuildDistribution()
    {
        lock (Lock)
        {
            if (_count == 0)
            {
                return new BuildTimeStats(0, 0.0, 0.0, 0.0, 0.0, 0.0);
            }

            double[] ordered = new double[_count];
            double sum = 0.0;
            for (int i = 0; i < _count; i++)
            {
                ordered[i] = Samples[i];
                sum += Samples[i];
            }

            Array.Sort(ordered);
            return new BuildTimeStats(
                    _count,
                    sum / _count,
                    BenchmarkStatistics.PercentileSorted(ordered, 0.50),
                    BenchmarkStatistics.PercentileSorted(ordered, 0.95),
                    BenchmarkStatistics.PercentileSorted(ordered, 0.99),
                    ordered[^1]);
        }
    }
}

public static class StreamingApplyBudget
{
    public const int MaxPendingPayloads = 18;
    // 8190 is triangle-aligned; a single resource-creation step cannot consume an entire chunk payload.
    public const int MaxVerticesPerStep = 8190;

    /// <summary>Return one triangle-aligned resource-creation slice.</summary>
    public static int BatchLength(int remainingVertices)
    {
        if (remainingVertices <= 0) return 0;
        if (remainingVertices % 3 != 0)
            throw new ArgumentException("A mesh batch must contain complete triangles.", nameof(remainingVertices));

        return Math.Min(remainingVertices, MaxVerticesPerStep);
    }

    /// <summary>Always allow one bounded work unit to make progress.</summary>
    public static bool ShouldStop(double elapsedMilliseconds, int completedSteps, double budgetMilliseconds) =>
            completedSteps > 0 && elapsedMilliseconds >= Math.Max(0.0, budgetMilliseconds);
}

public static class StreamingMemoryBudget
{
    public const int BytesPerVoxelUpperBound = 6; // blocks, states, sky, block, packed light and direct-sky nibble

    /// <summary>
    /// Bound byte-array caches to the streamed area plus the existing one-chunk
    /// lighting halo and two-chunk light-solver snapshot margin.
    /// </summary>
    public static long EstimateVoxelArrayBudgetBytes(
            int viewDistance, int unloadMargin, int chunkWidth, int chunkHeight, int chunkDepth)
    {
        int radius = Math.Clamp(viewDistance, 2, 24) + Math.Max(0, unloadMargin) + 1;
        long side = radius * 2L + 1L;
        return side * side * Math.Max(1, chunkWidth) * Math.Max(1, chunkHeight)
                * Math.Max(1, chunkDepth) * BytesPerVoxelUpperBound;
    }
}

public readonly record struct VoxelCacheMemorySnapshot(
        long BlockDataBytes,
        long BlockStateBytes,
        long SkyLightBytes,
        long BlockLightBytes,
        long PackedLightBytes,
        long GeneratorSeedArrayBytes,
        long CacheIndexBytesEstimate,
        long CropRegistryBytesEstimate,
        long SourceScanRegistryBytesEstimate,
        long CropScanRegistryBytesEstimate,
        long CampfireSiteCacheBytesEstimate,
        long StructureSiteCacheBytesEstimate,
        long VillageSiteCacheBytesEstimate,
        long EditTopArrayBytes,
        long EditTopIndexBytesEstimate,
        long WorldStateRegistryBytesEstimate,
        long ChunkIndexBytesEstimate,
        long EditMetadataBytesEstimate,
        long LightSourceBytesEstimate,
        long AppliedMeshBytesEstimate,
        long PooledMeshBufferBytes,
        long StreamingQueueBytesEstimate,
        long VoxelArrayBudgetBytes)
{
    public const string AccountingNote =
            "Voxel block/state/light arrays, edit-top arrays and the generator seed array use exact byte lengths. " +
            "Non-empty Dictionary, HashSet and priority-queue capacities plus List capacities are estimated; " +
            "empty collection backing storage is not exposed read-only and is omitted to avoid measurement allocations. " +
            "Concurrent apply-queue nodes use count-based estimates, with payload contents reported separately. " +
            "Saved inventory/boat values and saved-world object graphs are not deeply sized. " +
            "VoxelLighting thread-static scratch, scene/native allocations, renderer-driver memory and VRAM are excluded. " +
            "Source-scan and crop-scan chunk registries are tracked as separate estimates. " +
            "The generator-v8 village-site cache dictionary is included as its own estimate.";

    public long VoxelArrayBytes => BlockDataBytes + BlockStateBytes + SkyLightBytes
            + BlockLightBytes + PackedLightBytes;

    public long PersistentMetadataBytesEstimate =>
            CacheIndexBytesEstimate + CropRegistryBytesEstimate + SourceScanRegistryBytesEstimate
            + CropScanRegistryBytesEstimate + CampfireSiteCacheBytesEstimate + StructureSiteCacheBytesEstimate + VillageSiteCacheBytesEstimate
            + EditTopIndexBytesEstimate + WorldStateRegistryBytesEstimate + ChunkIndexBytesEstimate
            + EditMetadataBytesEstimate + LightSourceBytesEstimate;

    public long TrackedManagedBytesEstimate => VoxelArrayBytes + GeneratorSeedArrayBytes + EditTopArrayBytes
            + PersistentMetadataBytesEstimate + AppliedMeshBytesEstimate + PooledMeshBufferBytes
            + StreamingQueueBytesEstimate;
}

public readonly record struct BuildTimeStats(
        int Count, double AverageMs, double P50Ms, double P95Ms,
        double P99Ms, double MaxMs);

/// <summary>Small, allocation-bounded statistics helpers shared by benchmarks and tests.</summary>
public static class BenchmarkStatistics
{
    /// <summary>
    /// Nearest-rank percentile (0..1). A defensive copy keeps callers' sample
    /// buffers intact; benchmark reports call this only after sampling ends.
    /// </summary>
    public static double Percentile(System.Collections.Generic.IReadOnlyList<double> values,
            double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        double[] ordered = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            ordered[i] = values[i];
        }

        Array.Sort(ordered);
        return PercentileSorted(ordered, percentile);
    }

    internal static double PercentileSorted(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
        {
            return 0.0;
        }

        double clamped = double.IsFinite(percentile)
                ? Math.Clamp(percentile, 0.0, 1.0) : 0.5;
        int rank = Math.Max(1, (int)Math.Ceiling(clamped * ordered.Length));
        return ordered[Math.Min(rank - 1, ordered.Length - 1)];
    }
}
