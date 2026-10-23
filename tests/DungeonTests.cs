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
    }
}
