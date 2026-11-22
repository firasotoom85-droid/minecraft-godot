using Godot;

namespace Minecraft;

/// <summary>
/// Biome foliage keeps distinct, atlas-backed species without changing any
/// legacy block id, and the V10 generator only substitutes leaf materials.
/// </summary>
public static class FoliageTests
{
    private static readonly BlockId[] Variants =
    [
        BlockId.SpruceLeaves, BlockId.JungleLeaves, BlockId.SwampLeaves,
    ];

    public static void Run(Node host)
    {
        BlockDb.EnsureTables();
        TestAssert.Equal(16, BlockDb.AtlasCols, "foliage keeps the shared 16-column atlas");
        TestAssert.Equal(256, BlockDb.TileCount, "foliage stays within the texture-array layer budget");
        CheckSpecies(BlockId.Leaves, 7, "oak");
        CheckSpecies(BlockId.BirchLeaves, 23, "birch");
        CheckSpecies(BlockId.SpruceLeaves, 56, "spruce");
        CheckSpecies(BlockId.JungleLeaves, 57, "jungle");
        CheckSpecies(BlockId.SwampLeaves, 58, "swamp");

        TestAssert.Equal(BlockId.Leaves, (BlockId)6, "oak foliage id 6 is unchanged");
        TestAssert.Equal(BlockId.BirchLeaves, (BlockId)21, "birch foliage id 21 is unchanged");
        TestAssert.Check((int)BlockId.SpruceLeaves > (int)BlockId.MossyBrick,
                "foliage ids append after every weathering id");

        foreach (BlockId variant in Variants)
        {
            ItemId item = ItemDb.BlockItem(variant);
            TestAssert.Equal(variant, ItemDb.BlockFor(item),
                    $"{variant} placeable item round-trips");
            TestAssert.Equal((int)variant + 49, (int)item,
                    $"{variant} item id follows the block append order");
        }

        TestAssert.Check(BlockDb.RenderFace(BlockId.SpruceLeaves, BlockId.JungleLeaves),
                "different foliage species retain cutout canopy overlap");
        string shader = BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty;
        TestAssert.Check(shader.Contains("ALPHA_SCISSOR_THRESHOLD = 0.5"),
                "all foliage species use the existing alpha-cutout material");

        CheckV10ChangesOnlyLeafMaterials(host);
    }

    private static void CheckSpecies(BlockId id, int expectedTile, string species)
    {
        TestAssert.Check(BlockDb.IsDefined(id), $"{species} foliage ID is registered");
        TestAssert.Equal(expectedTile, BlockDb.IconTile(id), $"{species} foliage atlas tile is stable");
        TestAssert.Check(BlockDb.IsFoliage(id), $"{species} uses foliage rendering rules");
        TestAssert.Check(!BlockDb.IsTransparent(id), $"{species} remains on the opaque cutout path");
        TestAssert.Equal((byte)2, BlockDb.LightOpacity[(int)id],
                $"{species} keeps foliage light transmission");
        Image atlas = BlockDb.AtlasTexture().GetImage();
        int ox = (expectedTile % BlockDb.AtlasCols) * BlockDb.TilePx;
        int oy = (expectedTile / BlockDb.AtlasCols) * BlockDb.TilePx;
        int opaque = 0;
        int holes = 0;
        Vector3 sum = Vector3.Zero;
        for (int y = 0; y < BlockDb.TilePx; y++)
        for (int x = 0; x < BlockDb.TilePx; x++)
        {
            Color c = atlas.GetPixel(ox + x, oy + y);
            if (c.A > 0.5f)
            {
                opaque++;
                sum += new Vector3(c.R, c.G, c.B);
            }
            else
            {
                holes++;
            }
        }
        TestAssert.Check(opaque > 700 && holes > 100,
                $"{species} has a dense but readable alpha-cutout canopy");
        TestAssert.Check(sum.LengthSquared() > 1.0f,
                $"{species} atlas tile contains valid colour data");
    }

    /// <summary>
    /// V10 may only turn an existing oak leaf cell into a species leaf cell.
    /// Terrain, trunks, structures, ores and every non-leaf block must match V9.
    /// </summary>
    private static void CheckV10ChangesOnlyLeafMaterials(Node host)
    {
        const int seed = 1337;
        string v9Dir = CreateWorldDirectory("foliage-v9", seed, WorldGenV9.Version);
        string v10Dir = CreateWorldDirectory("foliage-v10", seed, WorldGenV10.Version);
        VoxelWorld? v9 = null;
        VoxelWorld? v10 = null;
        try
        {
            GameSession.Instance.SelectWorld(v9Dir);
            v9 = new VoxelWorld();
            host.AddChild(v9);
            GameSession.Instance.SelectWorld(v10Dir);
            v10 = new VoxelWorld();
            host.AddChild(v10);

            TestAssert.Equal(WorldGenV9.Version, v9.WorldGenVersion,
                    "V9 world remains pinned to generator V9");
            TestAssert.Equal(WorldGenV10.Version, v10.WorldGenVersion,
                    "V10 world selects generator V10");
            TestAssert.Equal(WorldGenV10.Version, WorldStore.CurrentGenVersion,
                    "new worlds default to generator V10");

            Vector2I? candidate = null;
            byte[]? candidateData = null;
            int variants = 0;
            for (int cz = -24; cz <= 24 && candidate is null; cz++)
            for (int cx = -24; cx <= 24; cx++)
            {
                Vector2I chunk = new(cx, cz);
                byte[] data = v10.ChunkData(chunk);
                variants = CountVariants(data);
                if (variants > 0)
                {
                    candidate = chunk;
                    candidateData = data;
                    break;
                }
            }

            TestAssert.Check(candidate is not null,
                    "V10 places species foliage somewhere in the surveyed area");
            if (candidate is not { } found || candidateData is null)
                return;

            byte[] oldData = v9.ChunkData(found);
            TestAssert.Equal(oldData.Length, candidateData.Length,
                    "V9 and V10 chunks retain the same storage shape");
            int replaced = 0;
            for (int i = 0; i < oldData.Length; i++)
            {
                BlockId oldId = (BlockId)oldData[i];
                BlockId newId = (BlockId)candidateData[i];
                if (newId is BlockId.SpruceLeaves or BlockId.JungleLeaves or BlockId.SwampLeaves)
                {
                    replaced++;
                    TestAssert.Equal(BlockId.Leaves, oldId,
                            "V10 only replaces an existing oak leaf cell with a species leaf");
                }
                else
                {
                    TestAssert.Equal(oldId, newId,
                            "V10 preserves every non-foliage V9 cell byte-for-byte");
                }
            }
            TestAssert.Equal(variants, replaced,
                    "V10 foliage count matches the replaced oak leaf cells");
        }
        finally
        {
            if (v9 != null)
            {
                host.RemoveChild(v9);
                v9.QueueFree();
            }
            if (v10 != null)
            {
                host.RemoveChild(v10);
                v10.QueueFree();
            }
            RemoveWorldDirectory(v9Dir);
            RemoveWorldDirectory(v10Dir);
        }
    }

    private static int CountVariants(byte[] data)
    {
        int count = 0;
        foreach (byte value in data)
        {
            if ((BlockId)value is BlockId.SpruceLeaves or BlockId.JungleLeaves
                    or BlockId.SwampLeaves)
            {
                count++;
            }
        }
        return count;
    }

    private static string CreateWorldDirectory(string prefix, int seed, int version)
    {
        string dir = $"user://tests/{prefix}-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                prefix, seed, version, WorldStore.DeriveNoiseSeeds(seed),
                0, 0, "survival", "normal"));
        return dir;
    }

    private static void RemoveWorldDirectory(string dir)
    {
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(dir);
    }
}
