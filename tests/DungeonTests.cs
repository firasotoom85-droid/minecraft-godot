using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class DungeonTests
{
    public static void Run(Node host)
    {
        VoxelWorld.StructureInfo site = new(3, 100, 60, 100, 12345u);
        Dictionary<Vector3I, BlockId> blocks = [];
        Structures.Stamp(site, (x, y, z, id) => blocks[new Vector3I(x, y, z)] = id);

        int arenaY = Structures.DungeonArenaY(site);
        TestAssert.Equal(40, arenaY, "dungeon arena sits 20 blocks below the surface");
        TestAssert.Check(blocks.ContainsKey(new Vector3I(100, arenaY - 1, 100)), "dungeon has an arena floor");
        TestAssert.Check(blocks.ContainsKey(new Vector3I(99, 59, 100)), "dungeon shaft has a ladder");
        TestAssert.Check(blocks.ContainsKey(new Vector3I(106, arenaY, 105)), "dungeon has a lava pocket");
        TestAssert.Check(blocks.ContainsKey(new Vector3I(107, arenaY + 2, 100)), "dungeon has a wall");
        TestAssert.Check(blocks.ContainsKey(new Vector3I(100, arenaY + 2, 100)), "dungeon arena is carved out");
        TestAssert.Equal(BlockId.Air, blocks[new Vector3I(100, arenaY + 2, 100)], "arena center is air");

        string dir = $"user://tests/world-dungeon-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Dungeon", 777, 6, WorldStore.DeriveNoiseSeeds(777), 0, 0, "survival", null));
        GameSession.Instance.SelectWorld(dir);
        VoxelWorld world = new();
        host.AddChild(world);

        bool found = false;
        for (int cz = -15; cz <= 15 && !found; cz++)
        {
            for (int cx = -15; cx <= 15 && !found; cx++)
            {
                if (world.GetStructureSite(cx, cz) is { Kind: 3 })
                {
                    found = true;
                }
            }
        }

        TestAssert.Check(found, "gen v6 places at least one Ember Deep near the origin");
        TestAssert.Equal(128, world.Height, "gen v6 keeps height 128");

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);

        // V7 has its own deterministic structure selection stream. Verify the
        // Ember Deep survived the generator upgrade, not only legacy V6 worlds.
        string v7Dir = $"user://tests/world-dungeon-v7-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(v7Dir);
        WorldStore.WriteManifest(v7Dir, new WorldManifest(
                "Dungeon V7", 777, WorldGenV7.Version, WorldStore.DeriveNoiseSeeds(777),
                0, 0, "survival", null));
        GameSession.Instance.SelectWorld(v7Dir);
        VoxelWorld v7World = new();
        host.AddChild(v7World);

        bool foundV7 = false;
        VoxelWorld.StructureInfo v7Site = default;
        for (int cz = -15; cz <= 15 && !foundV7; cz++)
        {
            for (int cx = -15; cx <= 15 && !foundV7; cx++)
            {
                if (v7World.GetStructureSite(cx, cz) is { Kind: 3 } foundSite)
                {
                    foundV7 = true;
                    v7Site = foundSite;
                }
            }
        }

        TestAssert.Check(foundV7, "gen v7 places at least one Ember Deep near the origin");
        if (foundV7)
        {
            TestAssert.Check(Structures.DungeonArenaY(v7Site) > 0
                    && Structures.DungeonArenaY(v7Site) < v7World.Height,
                    "gen v7 Ember Deep arena is inside world bounds");
        }

        host.RemoveChild(v7World);
        v7World.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(v7Dir));
        DirAccess.RemoveAbsolute(v7Dir);
    }
}
