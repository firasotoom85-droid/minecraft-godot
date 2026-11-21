using System;
using Godot;

namespace Minecraft;

public static class VillageStructures
{
    public const int Radius = 23;

    public static void Stamp(VillageInfo village, Action<int, int, int, BlockId> put)
    {
        BlockId log = village.Theme == VillageTheme.Forest ? BlockId.BirchLog : BlockId.Log;
        BlockId wall = village.Theme == VillageTheme.Desert ? BlockId.Sand : BlockId.Planks;
        BlockId roof = village.Theme == VillageTheme.Forest ? BlockId.BirchLeaves : BlockId.Brick;
        BlockId road = village.Theme switch
        {
            VillageTheme.Desert => BlockId.Sand,
            VillageTheme.Snow => BlockId.Gravel,
            VillageTheme.Forest => BlockId.Dirt,
            _ => BlockId.Cobblestone,
        };
        BlockId floor = village.Theme == VillageTheme.Desert ? BlockId.Sand : BlockId.Planks;
        int x = village.X;
        int y = village.Y;
        int z = village.Z;

        for (int d = -18; d <= 18; d++)
        for (int w = -1; w <= 1; w++)
        {
            put(x + w, y, z + d, road);
            put(x + d, y, z + w, road);
        }

        for (int dx = -5; dx <= 5; dx++)
        for (int dz = -5; dz <= 5; dz++) put(x + dx, y, z + dz, road);

        foreach ((int dx, int dz) in new[] { (-5, -5), (5, -5), (-5, 5), (5, 5) })
        {
            for (int dy = 1; dy <= 3; dy++) put(x + dx, y + dy, z + dz, log);
            put(x + dx, y + 4, z + dz, BlockId.Torch);
        }

        // Match workstations to the deterministic resident home/work anchors.
        House(put, x - 9, y, z - 9, log, wall, roof, floor, BlockId.Chest);          // merchant
        House(put, x + 9, y, z - 9, log, wall, roof, floor, BlockId.Chest);          // farmer
        House(put, x - 9, y, z + 9, log, wall, roof, floor, BlockId.Furnace);        // blacksmith
        House(put, x + 9, y, z + 9, log, wall, roof, floor, BlockId.CraftingTable); // librarian
        GuardPost(put, x, y, z - 16, log);
        Farm(put, x + 17, y, z + 1, village.Theme);

        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
            if (Math.Abs(dx) == 2 || Math.Abs(dz) == 2)
                put(x + dx, y + 1, z + dz, BlockId.Cobblestone);
        put(x, y + 1, z, BlockId.Water);
    }

    private static void GuardPost(Action<int, int, int, BlockId> put,
            int x, int y, int z, BlockId log)
    {
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
            put(x + dx, y, z + dz, BlockId.Cobblestone);

        foreach (int sx in new[] { -2, 2 })
        foreach (int sz in new[] { -2, 2 })
        {
            for (int dy = 1; dy <= 4; dy++)
                put(x + sx, y + dy, z + sz, log);
            put(x + sx, y + 5, z + sz, BlockId.Torch);
        }

        for (int dx = -2; dx <= 2; dx++)
            put(x + dx, y + 4, z, BlockId.Planks);
    }

    private static void House(Action<int, int, int, BlockId> put,
            int x, int y, int z, BlockId log, BlockId wall, BlockId roof,
            BlockId floor, BlockId workstation)
    {
        for (int dx = -4; dx <= 4; dx++)
        for (int dz = -4; dz <= 4; dz++)
        {
            put(x + dx, y, z + dz, floor);
            for (int dy = 1; dy <= 3; dy++)
            {
                bool edge = Math.Abs(dx) == 4 || Math.Abs(dz) == 4;
                bool corner = Math.Abs(dx) == 4 && Math.Abs(dz) == 4;
                put(x + dx, y + dy, z + dz, edge ? (corner ? log : wall) : BlockId.Air);
            }
        }

        put(x, y + 1, z + 4, BlockId.Air);
        put(x, y + 2, z + 4, BlockId.Air);
        put(x - 2, y + 2, z + 4, BlockId.Glass);
        put(x + 2, y + 2, z + 4, BlockId.Glass);
        put(x - 4, y + 2, z, BlockId.Glass);
        put(x + 4, y + 2, z, BlockId.Glass);
        for (int dx = -5; dx <= 5; dx++)
        for (int dz = -5; dz <= 5; dz++) put(x + dx, y + 4, z + dz, roof);
        for (int dx = -2; dx <= 2; dx++) put(x + dx, y + 5, z, roof);

        put(x, y + 1, z - 2, BlockId.Bed);
        put(x + 2, y + 1, z - 2, workstation);
        put(x - 3, y + 3, z, BlockId.Torch);
        put(x + 3, y + 3, z, BlockId.Torch);
    }

    private static void Farm(Action<int, int, int, BlockId> put,
            int x, int y, int z, VillageTheme theme)
    {
        for (int dx = -4; dx <= 4; dx++)
        for (int dz = -4; dz <= 4; dz++)
        {
            if (Math.Abs(dx) == 4 || Math.Abs(dz) == 4)
            {
                put(x + dx, y, z + dz, BlockId.Dirt);
                continue;
            }

            if (dx == 0 || dz == 0)
            {
                // Source water sits level with the farmland, one block below
                // the crops, and the outer dirt rim keeps it from spilling.
                put(x + dx, y, z + dz, BlockId.Water);
                continue;
            }

            put(x + dx, y, z + dz, BlockId.Farmland);
            uint hash = unchecked((uint)x * 73856093u ^ (uint)z * 19349663u
                    ^ (uint)dx * 83492791u ^ (uint)dz * 2654435761u);
            put(x + dx, y + 1, z + dz, hash % 4u == 0 ? BlockId.Wheat1 : BlockId.Wheat0);
        }

        put(x, y + 2, z - 5, BlockId.Torch);
        put(x, y + 2, z + 5, BlockId.Torch);
    }
}
