namespace Minecraft;

/// <summary>
/// Deterministic per-cell structure templates (gen v4+): a watchtower, a
/// ruined building and a small mine. Templates emit blocks through a
/// callback so the world clips them into whichever chunk is being generated;
/// the seed stored in the site keeps every world reproducible forever.
///
/// The xorshift helpers are a frozen copy of the world's generator helpers —
/// changing them would move structures in existing v4 worlds.
/// </summary>
public static class Structures
{
    public static void Stamp(VoxelWorld.StructureInfo site, System.Action<int, int, int, BlockId> put)
    {
        uint rng = site.Seed;
        switch (site.Kind)
        {
            case 0:
                Watchtower(site, ref rng, put);
                break;
            case 1:
                Ruin(site, ref rng, put);
                break;
            case 2:
                Mine(site, ref rng, put);
                break;
            case 3:
                EmberDeep(site, ref rng, put);
                break;
            default:
                Mine(site, ref rng, put);
                break;
        }
    }

    /// <summary>Floor height of the Ember Deep arena for a surface site.</summary>
    public static int DungeonArenaY(VoxelWorld.StructureInfo site) => site.Y - 20;

    /// <summary>
    /// A brick entrance shaft down to a 15×13 arena. The arena is deliberately
    /// simple geometry so it clips cleanly into chunk generation and stays
    /// readable in a dark cave.
    /// </summary>
    private static void EmberDeep(VoxelWorld.StructureInfo s, ref uint rng,
            System.Action<int, int, int, BlockId> put)
    {
        int x = s.X;
        int y = s.Y;
        int z = s.Z;
        int ay = DungeonArenaY(s);
        if (ay < 8)
        {
            return;
        }

        // Shaft down from the surface.
        for (int dy = 1; dy <= y - ay; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    put(x + dx, y - dy, z + dz, BlockId.Air);
                }
            }

            put(x - 1, y - dy, z, BlockId.Ladder);
        }

        // Entrance rim.
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -3; dz <= 3; dz++)
            {
                if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) >= 2)
                {
                    put(x + dx, y + 1, z + dz,
                            Range(ref rng, 0, 2) == 0 ? BlockId.Cobblestone : BlockId.Brick);
                }
            }
        }

        // Carve the arena volume first, then add floor, walls and ceiling.
        for (int dx = -7; dx <= 7; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                for (int dy = 0; dy <= 6; dy++)
                {
                    put(x + dx, ay + dy, z + dz, BlockId.Air);
                }

                put(x + dx, ay - 1, z + dz,
                        Range(ref rng, 0, 3) == 0 ? BlockId.Cobblestone : BlockId.Brick);
            }
        }

        for (int dx = -7; dx <= 7; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                bool wall = System.Math.Abs(dx) == 7 || System.Math.Abs(dz) == 6;
                if (!wall)
                {
                    continue;
                }

                for (int dy = 0; dy <= 5; dy++)
                {
                    put(x + dx, ay + dy, z + dz,
                            Range(ref rng, 0, 2) == 0 ? BlockId.Cobblestone : BlockId.Brick);
                }
            }
        }

        for (int dx = -7; dx <= 7; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                if (System.Math.Abs(dx) <= 1 && System.Math.Abs(dz) <= 1)
                {
                    continue;   // shaft opening
                }

                put(x + dx, ay + 6, z + dz, BlockId.Brick);
            }
        }

        // Corner pillars and lava pockets.
        foreach (int sx in new[] { -4, 4 })
        {
            foreach (int sz in new[] { -3, 3 })
            {
                for (int dy = 0; dy <= 4; dy++)
                {
                    put(x + sx, ay + dy, z + sz, BlockId.Cobblestone);
                }
            }
        }

        foreach ((int lx, int lz) in new[] { (-6, -5), (6, -5), (-6, 5), (6, 5) })
        {
            put(x + lx, ay, z + lz, BlockId.Lava);
        }

        // Wall torches (the mesher still draws the flame even without a tracked
        // light; the arena's lava pools provide the ambient glow).
        foreach ((int tx, int tz) in new[] { (-6, -2), (-6, 2), (6, -2), (6, 2) })
        {
            put(x + tx, ay + 2, z + tz, BlockId.Torch);
        }
    }

    /// <summary>9x9 cobble base, log corner posts, plank walls with weathered gaps and a roof.</summary>
    private static void Watchtower(VoxelWorld.StructureInfo s, ref uint rng,
            System.Action<int, int, int, BlockId> put)
    {
        int x = s.X;
        int y = s.Y;
        int z = s.Z;
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -3; dz <= 3; dz++)
            {
                put(x + dx, y, z + dz, BlockId.Cobblestone);
                put(x + dx, y + 1, z + dz, BlockId.Planks);
            }
        }

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                for (int dy = 2; dy <= 7; dy++)
                {
                    put(x + sx * 3, y + dy, z + sz * 3, BlockId.Log);
                }
            }
        }

        for (int d = -3; d <= 3; d++)
        {
            for (int dy = 2; dy <= 3; dy++)
            {
                if (Range(ref rng, 0, 5) == 0)
                {
                    continue;   // weathered gaps
                }

                put(x + d, y + dy, z - 3, BlockId.Planks);
                put(x + d, y + dy, z + 3, BlockId.Planks);
                put(x - 3, y + dy, z + d, BlockId.Planks);
                put(x + 3, y + dy, z + d, BlockId.Planks);
            }
        }

        // Doorway in the south wall.
        put(x, y + 2, z + 3, BlockId.Air);
        put(x, y + 3, z + 3, BlockId.Air);

        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -3; dz <= 3; dz++)
            {
                put(x + dx, y + 8, z + dz, BlockId.Planks);
            }
        }

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                put(x + sx * 3, y + 9, z + sz * 3, BlockId.Cobblestone);
            }
        }
    }

    /// <summary>7x7 crumbled brick/cobble floor and broken walls, plus a lone pillar.</summary>
    private static void Ruin(VoxelWorld.StructureInfo s, ref uint rng,
            System.Action<int, int, int, BlockId> put)
    {
        int x = s.X;
        int y = s.Y;
        int z = s.Z;
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -3; dz <= 3; dz++)
            {
                bool edge = System.Math.Abs(dx) == 3 || System.Math.Abs(dz) == 3;
                if (!edge && Range(ref rng, 0, 2) > 0)
                {
                    continue;
                }

                put(x + dx, y + 1, z + dz,
                        Range(ref rng, 0, 1) == 0 ? BlockId.Cobblestone : BlockId.Brick);
                if (!edge)
                {
                    continue;
                }

                int height = Range(ref rng, 1, 3);
                for (int dy = 2; dy < 2 + height; dy++)
                {
                    if (Range(ref rng, 0, 3) == 0)
                    {
                        continue;   // crumbled away
                    }

                    put(x + dx, y + dy, z + dz,
                            Range(ref rng, 0, 2) == 0 ? BlockId.Brick : BlockId.Cobblestone);
                }
            }
        }

        for (int p = 0; p < 2; p++)
        {
            int px = x + Range(ref rng, -2, 2);
            int pz = z + Range(ref rng, -2, 2);
            int ph = Range(ref rng, 2, 4);
            for (int dy = 1; dy <= ph; dy++)
            {
                put(px, y + dy, pz, BlockId.Cobblestone);
            }
        }
    }

    /// <summary>3x3 timbered shaft down 14 blocks into an ore-flecked chamber.</summary>
    private static void Mine(VoxelWorld.StructureInfo s, ref uint rng,
            System.Action<int, int, int, BlockId> put)
    {
        int x = s.X;
        int y = s.Y;
        int z = s.Z;
        for (int dy = 0; dy <= 14; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    put(x + dx, y - dy, z + dz, BlockId.Air);
                }
            }
        }

        for (int dy = 2; dy <= 14; dy += 4)
        {
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    put(x + sx, y - dy, z + sz, BlockId.Log);
                }
            }
        }

        // Bottom chamber with a cobbled floor and ore in the walls.
        int by = y - 14;
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dy = 0; dy <= 3; dy++)
                {
                    put(x + dx, by - dy, z + dz, BlockId.Air);
                }
            }
        }

        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                put(x + dx, by - 4, z + dz, BlockId.Cobblestone);
            }
        }

        for (int k = 0; k < 10; k++)
        {
            BlockId ore = Range(ref rng, 0, 2) == 0 ? BlockId.IronOre : BlockId.CoalOre;
            int wy = by - Range(ref rng, 1, 3);
            switch (Range(ref rng, 0, 3))
            {
                case 0:
                    put(x + Range(ref rng, -3, 3), wy, z - 3, ore);
                    break;
                case 1:
                    put(x + Range(ref rng, -3, 3), wy, z + 3, ore);
                    break;
                case 2:
                    put(x - 4, wy, z + Range(ref rng, -2, 2), ore);
                    break;
                default:
                    put(x + 4, wy, z + Range(ref rng, -2, 2), ore);
                    break;
            }
        }

        // Surface rim around the shaft mouth.
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) == 2)
                {
                    put(x + dx, y + 1, z + dz, BlockId.Cobblestone);
                }
            }
        }
    }

    // Frozen xorshift helpers (copied from VoxelWorld; never change).
    private static uint Next(ref uint s)
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }

    private static int Range(ref uint s, int minInclusive, int maxInclusive) =>
        minInclusive + (int)(Next(ref s) % (uint)(maxInclusive - minInclusive + 1));
}
