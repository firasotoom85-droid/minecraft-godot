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

        TestPayloadPoolCapacityBudget();
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
    }
}
