namespace Minecraft;

public static class VoxelStreamingTests
{
    public static void Run()
    {
        TestAssert.Check(!StreamingApplyBudget.ShouldStop(25.0, 0, 2.0),
                "time budget still permits one payload to make progress");
        TestAssert.Check(!StreamingApplyBudget.ShouldStop(1.99, 1, 2.0),
                "payload application continues while within its frame time budget");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(2.0, 1, 2.0),
                "payload application stops once its frame time budget is exhausted");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(0.0, 1, 0.0),
                "zero time budget stops after the first payload");

        long expectedLow = 11L * 11L * 16 * 128 * 16 * StreamingMemoryBudget.BytesPerVoxelUpperBound;
        TestAssert.Equal(expectedLow,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(2, 2, 16, 128, 16),
                "voxel cache budget includes the direct-sky side map, streamed radius and existing halo");
        TestAssert.Equal(17L * 17L * 16 * 128 * 16 * StreamingMemoryBudget.BytesPerVoxelUpperBound,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(5, 2, 16, 128, 16),
                "voxel cache budget scales with the selected view distance");
        TestAssert.Equal(expectedLow,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(0, 2, 16, 128, 16),
                "voxel cache budget clamps unsupported view distances to the runtime minimum");

        TestMeshingConfigurationRevision();
        TestPayloadPoolCapacityBudget();
    }

    private static void TestMeshingConfigurationRevision()
    {
        bool original = Mesher.Greedy;
        try
        {
            (bool greedy, long revision) = Mesher.CaptureConfiguration();
            TestAssert.Equal(original, greedy,
                    "meshing configuration capture returns the current greedy mode");
            TestAssert.Check(Mesher.IsConfigurationCurrent(revision),
                    "the current meshing configuration revision is accepted");

            Mesher.Greedy = !greedy;
            (bool nextGreedy, long nextRevision) = Mesher.CaptureConfiguration();
            TestAssert.Equal(!greedy, nextGreedy,
                    "changing greedy mode publishes the new immutable build mode");
            TestAssert.Check(nextRevision > revision,
                    "a meshing mode change advances the configuration revision");
            TestAssert.Check(!Mesher.IsConfigurationCurrent(revision),
                    "payloads captured before a mode change are identified as stale");
            TestAssert.Check(Mesher.IsConfigurationCurrent(nextRevision),
                    "payloads captured after a mode change use the active revision");
        }
        finally
        {
            Mesher.Greedy = original;
        }
    }

    private static void TestPayloadPoolCapacityBudget()
    {
        long retainedBefore = PayloadPool.RetainedBufferBytes;
        int surfacesBefore = PayloadPool.RetainedSurfaceCount;
        int collisionsBefore = PayloadPool.RetainedCollisionCount;

        SurfaceData oversized = new();
        oversized.Vertex.Capacity = (int)(PayloadPool.MaxRetainedItemBytes / 12) + 1;
        long oversizedBytes = oversized.EstimatedBufferBytes;
        TestAssert.Check(oversizedBytes > PayloadPool.MaxRetainedItemBytes,
                "oversized surface fixture exceeds the per-buffer retention threshold");

        (bool _, long revision) = Mesher.CaptureConfiguration();
        MeshPayload oversizedPayload = new(oversized, new SurfaceData(),
                new SurfaceData(), new SurfaceData(), [], 0, revision, 0);
        PayloadPool.Release(oversizedPayload);

        TestAssert.Equal(retainedBefore, PayloadPool.RetainedBufferBytes,
                "oversized surface backing arrays are discarded instead of retained");
        TestAssert.Equal(surfacesBefore, PayloadPool.RetainedSurfaceCount,
                "discarding an oversized surface does not increment retained-item accounting");
        TestAssert.Equal(collisionsBefore, PayloadPool.RetainedCollisionCount,
                "empty collision buffers are not counted as retained pool entries");
        TestAssert.Check(PayloadPool.RetainedBufferBytes <= PayloadPool.MaxRetainedBufferBytes,
                "static mesher pool never exceeds its process-wide byte budget");
        TestAssert.Check(PayloadPool.RetainedSurfaceCount >= 0
                        && PayloadPool.RetainedCollisionCount >= 0,
                "mesher pool retained-item counters stay non-negative");

        // Normal-sized buffers should remain reusable under the global cap.
        SurfaceData reusable = new();
        reusable.Vertex.Capacity = 32;
        long reusableBytes = reusable.EstimatedBufferBytes;
        MeshPayload reusablePayload = new(reusable, new SurfaceData(),
                new SurfaceData(), new SurfaceData(), [], 0, revision, 0);
        PayloadPool.Release(reusablePayload);
        TestAssert.Equal(retainedBefore + reusableBytes, PayloadPool.RetainedBufferBytes,
                "normal-size surface capacity is accounted while retained");
        TestAssert.Equal(surfacesBefore + 1, PayloadPool.RetainedSurfaceCount,
                "normal-size surface is added to the reusable pool");
        SurfaceData rented = PayloadPool.RentSurface();
        TestAssert.Check(ReferenceEquals(reusable, rented),
                "the next surface rent reuses the returned backing buffer");
        TestAssert.Equal(retainedBefore, PayloadPool.RetainedBufferBytes,
                "renting a surface subtracts its exact retained capacity");

        System.Threading.Tasks.Task[] workers = new System.Threading.Tasks.Task[8];
        for (int worker = 0; worker < workers.Length; worker++)
        {
            workers[worker] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    SurfaceData a = PayloadPool.RentSurface();
                    SurfaceData b = PayloadPool.RentSurface();
                    SurfaceData c = PayloadPool.RentSurface();
                    SurfaceData d = PayloadPool.RentSurface();
                    System.Collections.Generic.List<Vector3> collision = PayloadPool.RentCollision();
                    MeshPayload payload = new(a, b, c, d, collision, 0, revision, 0);
                    PayloadPool.Release(payload);
                }
            });
        }
        System.Threading.Tasks.Task.WaitAll(workers);
        TestAssert.Check(PayloadPool.RetainedBufferBytes <= PayloadPool.MaxRetainedBufferBytes,
                "concurrent worker rent/release never exceeds the retained byte budget");
        TestAssert.Check(PayloadPool.RetainedSurfaceCount >= 0
                        && PayloadPool.RetainedCollisionCount >= 0,
                "concurrent worker rent/release keeps item counters non-negative");
        TestAssert.Check(PayloadPool.RetainedBufferBytes >= 0,
                "concurrent worker rent/release keeps retained bytes non-negative");
    }
}
