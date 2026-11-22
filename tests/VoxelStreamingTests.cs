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

        long expectedLow = 11L * 11L * 16 * 128 * 16 * 5;
        TestAssert.Equal(expectedLow,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(2, 2, 16, 128, 16),
                "voxel cache budget includes the streamed radius and existing halo");
        TestAssert.Equal(17L * 17L * 16 * 128 * 16 * 5,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(5, 2, 16, 128, 16),
                "voxel cache budget scales with the selected view distance");
        TestAssert.Equal(expectedLow,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(0, 2, 16, 128, 16),
                "voxel cache budget clamps unsupported view distances to the runtime minimum");
    }
}
