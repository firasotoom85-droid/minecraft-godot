using Godot;

namespace Minecraft;

/// <summary>Biome foliage keeps distinct, atlas-backed species without changing legacy IDs.</summary>
public static class FoliageTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();
        TestAssert.Equal(16, BlockDb.AtlasCols, "foliage keeps the shared 16-column atlas");
        TestAssert.Equal(256, BlockDb.TileCount, "foliage stays within the existing texture-array layer budget");
        CheckSpecies(BlockId.Leaves, 7, "oak");
        CheckSpecies(BlockId.BirchLeaves, 23, "birch");
        CheckSpecies(BlockId.SpruceLeaves, 49, "spruce");
        CheckSpecies(BlockId.JungleLeaves, 50, "jungle");
        CheckSpecies(BlockId.SwampLeaves, 51, "swamp");

        TestAssert.Check(BlockDb.IsFoliage(BlockId.SpruceLeaves), "spruce is treated as foliage");
        TestAssert.Check(BlockDb.IsFoliage(BlockId.JungleLeaves), "jungle is treated as foliage");
        TestAssert.Check(BlockDb.IsFoliage(BlockId.SwampLeaves), "swamp is treated as foliage");
        TestAssert.Check(BlockDb.RenderFace(BlockId.SpruceLeaves, BlockId.JungleLeaves),
                "different foliage species retain cutout canopy overlap");
        TestAssert.Equal((byte)2, BlockDb.LightOpacity[(int)BlockId.SpruceLeaves],
                "spruce keeps foliage light transmission");
        TestAssert.Equal((byte)2, BlockDb.LightOpacity[(int)BlockId.JungleLeaves],
                "jungle keeps foliage light transmission");
        TestAssert.Equal((byte)2, BlockDb.LightOpacity[(int)BlockId.SwampLeaves],
                "swamp keeps foliage light transmission");

        string shader = BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty;
        TestAssert.Check(shader.Contains("ALPHA_SCISSOR_THRESHOLD = 0.5"),
                "all foliage species use the existing alpha-cutout material");
    }

    private static void CheckSpecies(BlockId id, int expectedTile, string species)
    {
        TestAssert.Check(BlockDb.IsDefined(id), $"{species} foliage ID is registered");
        TestAssert.Equal(expectedTile, BlockDb.IconTile(id), $"{species} foliage atlas tile is stable");
        TestAssert.Check(BlockDb.IsFoliage(id), $"{species} uses foliage rendering rules");
        TestAssert.Check(!BlockDb.IsTransparent(id), $"{species} remains on the opaque cutout path");
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
            else holes++;
        }
        TestAssert.Check(opaque > 700 && holes > 100,
                $"{species} has a dense but readable alpha-cutout canopy");
        TestAssert.Check(sum.LengthSquared() > 1.0f,
                $"{species} atlas tile contains valid colour data");
    }
}
