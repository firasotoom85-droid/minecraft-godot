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
        world.SetBlock(30, 49, 1, BlockId.Stone);
        world.SetBlock(31, 49, 1, BlockId.Stone);
        world.SetBlock(30, 50, 1, BlockId.FlowingWater6);
        world.SetBlock(31, 50, 1, BlockId.FlowingWater1);
        fluids.SimulateForTests(world, new Vector3(25, 50, 2));
        TestAssert.Equal(BlockId.FlowingWater1, world.GetBlock(31, 50, 1),
                "unknown chunk-edge support prevents a weaker loaded neighbour from downgrading flow");

        // A supported cell at the foot of a vertical stream must remain level 1,
        // and its horizontal offers must be level 2 rather than level 3.
        for (int y = 54; y <= 57; y++)
        for (int z = 9; z <= 11; z++)
        for (int x = 9; x <= 12; x++)
            world.SetBlock(x, y, z, BlockId.Air);
        world.SetBlock(10, 54, 10, BlockId.Stone);
        world.SetBlock(10, 55, 10, BlockId.FlowingWater1);
        world.SetBlock(10, 56, 10, BlockId.FlowingWater1);
        world.SetBlock(10, 57, 10, BlockId.FlowingWater1);
        fluids.SimulateForTests(world, new Vector3(10, 55, 10), 512);
        TestAssert.Equal(BlockId.FlowingWater1, world.GetBlock(10, 55, 10),
                "water directly above a supported fall foot preserves level 1");
        TestAssert.Equal(BlockId.FlowingWater2, world.GetBlock(11, 55, 10),
                "horizontal spread from a falling column foot uses level 2");

        // The initial discovery occurs before chunk 2 has streamed. A bounded
        // retry finds its water without movement or a nearby block mutation.
        FluidSystem delayedDiscovery = new();
        Vector3 discoveryCenter = new(25, 50, 1);
        delayedDiscovery.DiscoverForTests(world, discoveryCenter, 0.1);
        TestAssert.Check(!world.HasChunk(new Vector2I(2, 0)),
                "target fluid chunk is initially not streamed");
        world.BuildPreview(new Vector3(32, 50, 0), 1);
        world.SetBlock(32, 49, 1, BlockId.Stone);
        world.SetBlock(32, 50, 1, BlockId.Water);
        TestAssert.Equal(0, delayedDiscovery.ActiveSourceCount,
                "water in a just-loaded chunk waits for scheduled rediscovery");
        delayedDiscovery.DiscoverForTests(world, discoveryCenter, 0.5);
        delayedDiscovery.DiscoverForTests(world, discoveryCenter, 0.5);
        delayedDiscovery.DiscoverForTests(world, discoveryCenter, 0.5);
        TestAssert.Equal(0, delayedDiscovery.ActiveSourceCount,
                "rediscovery remains bounded by its retry interval");
        delayedDiscovery.DiscoverForTests(world, discoveryCenter, 0.5);
        TestAssert.Check(delayedDiscovery.ActiveSourceCount > 0,
                "periodic discovery registers nearby water loaded after the first scan");

        world.SetBlock(4, 50, 2, BlockId.Stone);
        world.SetBlock(3, 50, 4, BlockId.Lava);
        fluids.SimulateForTests(world, new Vector3(2, 50, 2));
        TestAssert.Equal(BlockId.Stone, world.GetBlock(4, 50, 2),
                "solid terrain remains impermeable to water");
        TestAssert.Equal(BlockId.Cobblestone, world.GetBlock(3, 50, 4),
                "water contact converts adjacent lava to cobblestone");

        delayedDiscovery.Free();
        fluids.Free();
        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(dir + "/manifest.json");
        DirAccess.RemoveAbsolute(dir);
    }
}
