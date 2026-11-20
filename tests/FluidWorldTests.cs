using Godot;

namespace Minecraft;

/// <summary>Integration tests over generated chunks and VoxelWorld.SetBlock.</summary>
public static class FluidWorldTests
{
    public static void Run(Node host)
    {
        string dir = $"user://tests/fluid-world-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Fluid integration", 2468, 1, WorldStore.LegacyNoiseSeeds, 0, 0));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(Vector3.Zero, 1);

        // Isolate a deterministic air pocket in the temporary test world.
        for (int y = 49; y <= 51; y++)
        for (int z = 1; z <= 5; z++)
        for (int x = 1; x <= 6; x++)
            world.SetBlock(x, y, z, BlockId.Air);
        for (int z = 1; z <= 4; z++)
        for (int x = 1; x <= 4; x++)
            world.SetBlock(x, 49, z, BlockId.Stone);

        world.SetBlock(2, 50, 2, BlockId.Water);
        world.SetBlock(3, 50, 3, BlockId.Water);
        world.SetBlock(5, 49, 2, BlockId.Stone);
        world.SetBlock(5, 50, 2, BlockId.Water);
        FluidSystem fluids = new();
        fluids.SimulateForTests(world, new Vector3(2, 50, 2));
        TestAssert.Equal(BlockId.Water, world.GetBlock(3, 50, 2),
                "two horizontal sources regenerate a source in a basin");
        TestAssert.Check(BlockDb.IsWater(world.GetBlock(2, 50, 3)),
                "source water spreads into a loaded horizontal opening");
        TestAssert.Equal(BlockId.FlowingWater1, world.GetBlock(5, 50, 3),
                "a single source produces a flowing, non-source neighbour");

        // x=32 is in chunk 2, which the preview above deliberately does not load.
        world.SetBlock(30, 50, 1, BlockId.FlowingWater6);
        world.SetBlock(31, 50, 1, BlockId.FlowingWater7);
        fluids.SimulateForTests(world, new Vector3(25, 50, 2));
        TestAssert.Equal(BlockId.FlowingWater7, world.GetBlock(31, 50, 1),
                "flow does not recede at an unloaded chunk boundary");

        world.SetBlock(4, 50, 2, BlockId.Stone);
        world.SetBlock(3, 50, 4, BlockId.Lava);
        fluids.SimulateForTests(world, new Vector3(2, 50, 2));
        TestAssert.Equal(BlockId.Stone, world.GetBlock(4, 50, 2),
                "solid terrain remains impermeable to water");
        TestAssert.Equal(BlockId.Cobblestone, world.GetBlock(3, 50, 4),
                "water contact converts adjacent lava to cobblestone");

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(dir + "/manifest.json");
        DirAccess.RemoveAbsolute(dir);
    }
}
