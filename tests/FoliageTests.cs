using Godot;

namespace Minecraft;

/// <summary>Existing oak and birch foliage retain readable clustered cutout art.</summary>
public static class FoliageTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();
        TestAssert.Equal(16, BlockDb.AtlasCols, "foliage keeps the shared atlas width");
        TestAssert.Equal(256, BlockDb.TileCount, "foliage stays within the texture-array layer budget");
        CheckSpecies(BlockId.Leaves, 7, "oak");
        CheckSpecies(BlockId.BirchLeaves, 23, "birch");
        TestAssert.Check(BlockDb.RenderFace(BlockId.Leaves, BlockId.BirchLeaves),
                "oak and birch canopies retain cutout overlap");
        string shader = BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty;
        TestAssert.Check(shader.Contains("ALPHA_SCISSOR_THRESHOLD = 0.5"),
                "foliage uses the existing alpha-cutout shader");
    }

    private static void CheckSpecies(BlockId id, int tile, string species)
    {
        TestAssert.Check(BlockDb.IsFoliage(id), $"{species} uses foliage rendering rules");
        TestAssert.Equal((byte)2, BlockDb.LightOpacity[(int)id], $"{species} transmits foliage light");
        Image atlas = BlockDb.AtlasTexture().GetImage();
        int ox = tile % BlockDb.AtlasCols * BlockDb.TilePx;
        int oy = tile / BlockDb.AtlasCols * BlockDb.TilePx;
        int opaque = 0, holes = 0;
        for (int y = 0; y < BlockDb.TilePx; y++)
        for (int x = 0; x < BlockDb.TilePx; x++)
        {
            if (atlas.GetPixel(ox + x, oy + y).A > 0.5f) opaque++;
            else holes++;
        }
        TestAssert.Check(opaque > 700 && holes > 100,
                $"{species} has dense foliage with irregular cutout gaps");
    }
}
