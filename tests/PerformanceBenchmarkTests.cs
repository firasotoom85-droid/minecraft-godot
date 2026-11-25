namespace Minecraft;

public static class PerformanceBenchmarkTests
{
    public static void Run()
    {
        double[] empty = [];
        TestAssert.Equal(0.0, BenchmarkStatistics.Percentile(empty, 0.50),
                "empty benchmark sample has a zero percentile");

        double[] one = [16.0];
        TestAssert.Equal(16.0, BenchmarkStatistics.Percentile(one, 0.50),
                "single-sample median is that sample");

        double[] ordered = [10.0, 20.0, 30.0, 40.0];
        TestAssert.Equal(20.0, BenchmarkStatistics.Percentile(ordered, 0.50),
                "nearest-rank p50 uses the lower middle rank for four samples");
        TestAssert.Equal(40.0, BenchmarkStatistics.Percentile(ordered, 0.95),
                "nearest-rank p95 selects the upper sample");
        TestAssert.Equal(40.0, BenchmarkStatistics.Percentile(ordered, 0.99),
                "nearest-rank p99 selects the upper sample");

        double[] unordered = [40.0, 10.0, 30.0, 20.0];
        TestAssert.Equal(20.0, BenchmarkStatistics.Percentile(unordered, 0.50),
                "percentile calculation sorts a defensive copy");
        TestAssert.Equal(40.0, unordered[0],
                "percentile calculation does not mutate the caller's sample buffer");

        TestAssert.Equal(10.0, BenchmarkStatistics.Percentile(ordered, -1.0),
                "percentiles below zero clamp to the minimum sample");
        TestAssert.Equal(40.0, BenchmarkStatistics.Percentile(ordered, 2.0),
                "percentiles above one clamp to the maximum sample");

        Profiler.ResetBuildSamples();
        Profiler.RecordBuild(12.0);
        Profiler.RecordBuild(4.0);
        Profiler.RecordBuild(8.0);
        BuildTimeStats buildStats = Profiler.BuildDistribution();
        TestAssert.Equal(3, buildStats.Count, "mesh distribution counts recorded builds");
        TestAssert.Equal(8.0, buildStats.AverageMs, "mesh distribution averages recorded builds");
        TestAssert.Equal(8.0, buildStats.P50Ms, "mesh distribution reports median build time");
        TestAssert.Equal(12.0, buildStats.P95Ms, "mesh distribution reports tail build time");
        TestAssert.Equal(12.0, buildStats.MaxMs, "mesh distribution reports worst build time");
        (double meshAverage, double meshMaximum) = Profiler.BuildStats();
        TestAssert.Equal(8.0, meshAverage, "existing HUD profiler average remains compatible");
        TestAssert.Equal(12.0, meshMaximum, "existing HUD profiler maximum remains compatible");
        Profiler.ResetBuildSamples();
        TestAssert.Equal(0, Profiler.BuildDistribution().Count,
                "benchmark reset clears warm-up mesh samples");

        for (int i = 0; i < 70; i++)
        {
            Profiler.RecordBuild(i);
        }

        BuildTimeStats wrappedStats = Profiler.BuildDistribution();
        TestAssert.Equal(64, wrappedStats.Count, "mesh profiler retains only its bounded 64-sample window");
        TestAssert.Equal(37.5, wrappedStats.AverageMs, "mesh profiler averages the newest wrapped samples");
        TestAssert.Equal(37.0, wrappedStats.P50Ms, "mesh profiler percentiles remain ordered after ring wrap");
        TestAssert.Equal(69.0, wrappedStats.MaxMs, "mesh profiler retains the newest worst sample");
        Profiler.ResetBuildSamples();

        Profiler.ResetApplyStepSamples();
        Profiler.RecordApplyStep(12.0);
        Profiler.RecordApplyStep(4.0);
        Profiler.RecordApplyStep(8.0);
        BuildTimeStats applyStepStats = Profiler.ApplyStepDistribution();
        TestAssert.Equal(3, applyStepStats.Count, "resource-step distribution counts staged operations");
        TestAssert.Equal(8.0, applyStepStats.AverageMs, "resource-step distribution averages samples");
        TestAssert.Equal(8.0, applyStepStats.P50Ms, "resource-step distribution reports median step time");
        TestAssert.Equal(12.0, applyStepStats.P95Ms, "resource-step distribution reports the tail step time");

        Profiler.RecordCollision(7.0, 8190);
        Profiler.RecordCollision(3.0, 3);
        BuildTimeStats collisionStats = Profiler.CollisionDistribution();
        TestAssert.Equal(2, collisionStats.Count, "collision distribution counts staged shape builds");
        TestAssert.Equal(7.0, collisionStats.P95Ms, "collision distribution reports its high-percentile cost");
        TestAssert.Equal(8193L, Profiler.CollisionVerticesApplied,
                "collision metrics record vertices covered by every staged collision batch");

        for (int i = 0; i < 70; i++)
            Profiler.RecordApplyStep(i);
        BuildTimeStats wrappedApplyStats = Profiler.ApplyStepDistribution();
        TestAssert.Equal(64, wrappedApplyStats.Count,
                "resource-step timing retains a bounded 64-sample window");
        TestAssert.Equal(69.0, wrappedApplyStats.MaxMs,
                "resource-step timing preserves the newest maximum after ring wrap");
        Profiler.ResetApplyStepSamples();
        TestAssert.Equal(0, Profiler.ApplyStepDistribution().Count,
                "benchmark reset clears staged resource-step samples");
        TestAssert.Equal(0, Profiler.CollisionDistribution().Count,
                "benchmark reset clears collision-step samples");
    }
}
