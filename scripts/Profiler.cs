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
