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
        fluids.Free();

        // Isolate a vertical stream away from the earlier basin fixture. The
        // source at the top keeps the falling column alive while the supported
        // foot spreads horizontally at the next level.
        const int fallX = 20;
        const int fallZ = 20;
        for (int y = 54; y <= 57; y++)
        for (int z = 18; z <= 22; z++)
        for (int x = 18; x <= 22; x++)
            world.SetBlock(x, y, z, BlockId.Air);
        world.SetBlock(fallX, 54, fallZ, BlockId.Stone);
        world.SetBlock(fallX + 1, 54, fallZ, BlockId.Stone);
        world.SetBlock(fallX, 55, fallZ, BlockId.FlowingWater1);
        world.SetBlock(fallX, 56, fallZ, BlockId.FlowingWater1);
        world.SetBlock(fallX, 57, fallZ, BlockId.Water);
        // Keep the upper source and falling segment from creating a parallel
        // side column above the foot, leaving only the foot's east offer open.
        world.SetBlock(fallX - 1, 56, fallZ, BlockId.Stone);
        world.SetBlock(fallX + 1, 56, fallZ, BlockId.Stone);
        world.SetBlock(fallX, 56, fallZ - 1, BlockId.Stone);
        world.SetBlock(fallX, 56, fallZ + 1, BlockId.Stone);
        world.SetBlock(fallX - 1, 57, fallZ, BlockId.Stone);
        world.SetBlock(fallX + 1, 57, fallZ, BlockId.Stone);
        world.SetBlock(fallX, 57, fallZ - 1, BlockId.Stone);
        world.SetBlock(fallX, 57, fallZ + 1, BlockId.Stone);
        FluidSystem columnFluids = new();
        columnFluids.SimulateForTests(world, new Vector3(fallX, 55, fallZ), 512);
        TestAssert.Equal(BlockId.FlowingWater1, world.GetBlock(fallX, 55, fallZ),
                "water directly above a supported fall foot preserves level 1");
        TestAssert.Equal(BlockId.FlowingWater2, world.GetBlock(fallX + 1, 55, fallZ),
                "horizontal spread from a falling column foot uses level 2");
        columnFluids.Free();

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
        const int lavaX = 24;
        const int lavaZ = 3;
        world.SetBlock(lavaX, 50, lavaZ, BlockId.Water);
        world.SetBlock(lavaX + 1, 50, lavaZ, BlockId.Lava);
        FluidSystem lavaFluids = new();
        lavaFluids.SimulateForTests(world, new Vector3(lavaX, 50, lavaZ), 512);
        TestAssert.Equal(BlockId.Stone, world.GetBlock(4, 50, 2),
                "solid terrain remains impermeable to water");
        TestAssert.Equal(BlockId.Cobblestone, world.GetBlock(lavaX + 1, 50, lavaZ),
                "water contact converts adjacent lava to cobblestone");

        lavaFluids.Free();
        delayedDiscovery.Free();
        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(dir + "/manifest.json");
        DirAccess.RemoveAbsolute(dir);
    }
}
