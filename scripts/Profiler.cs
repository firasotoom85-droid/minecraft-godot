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
    public static long VoxelCacheBudgetBytes;
    public static long EditMetadataBytesEstimate;
    public static long LightSourceBytesEstimate;
    public static long AppliedMeshBytesEstimate;
    public static long PooledMeshBufferBytes;
    public static double ApplyAverageMs;
    public static double ApplyMaximumMs;
    public static double CollisionAverageMs;
    public static double CollisionMaximumMs;
    public static long CollisionVerticesApplied;

    private static readonly Lock RuntimeMetricsLock = new();
    private static double _applyTotalMs;
    private static long _applySamples;
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
        VoxelCacheBudgetBytes = snapshot.VoxelArrayBudgetBytes;
        EditMetadataBytesEstimate = snapshot.EditMetadataBytesEstimate;
        LightSourceBytesEstimate = snapshot.LightSourceBytesEstimate;
        AppliedMeshBytesEstimate = snapshot.AppliedMeshBytesEstimate;
        PooledMeshBufferBytes = snapshot.PooledMeshBufferBytes;
        UpdateMaximum(ref VoxelCacheArrayHighWaterBytes, snapshot.VoxelArrayBytes);
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
    /// <summary>Always allow one payload to make progress, even when it exceeds the frame budget.</summary>
    public static bool ShouldStop(double elapsedMilliseconds, int appliedCount, double budgetMilliseconds) =>
            appliedCount > 0 && elapsedMilliseconds >= Math.Max(0.0, budgetMilliseconds);
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
        long EditMetadataBytesEstimate,
        long LightSourceBytesEstimate,
        long AppliedMeshBytesEstimate,
        long PooledMeshBufferBytes,
        long VoxelArrayBudgetBytes)
{
    public long VoxelArrayBytes => BlockDataBytes + BlockStateBytes + SkyLightBytes
            + BlockLightBytes + PackedLightBytes;
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
