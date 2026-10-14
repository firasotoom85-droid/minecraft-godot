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
}
