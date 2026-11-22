using Godot;

namespace Minecraft;

public static class TerrainMaterialTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();

        TestAssert.Equal(0, BlockDb.FaceTile(BlockId.Grass, 0), "grass upward face keeps its original atlas tile");
        TestAssert.Equal(2, BlockDb.FaceTile(BlockId.Grass, 1), "grass underside stays dirt");
        TestAssert.Equal(1, BlockDb.FaceTile(BlockId.Grass, 2), "grass vertical faces use the grass edge tile");
        TestAssert.Equal(2, BlockDb.FaceTile(BlockId.Dirt, 0), "dirt top uses the shared dirt tile");
        TestAssert.Equal(36, BlockDb.FaceTile(BlockId.Farmland, 0), "farmland top keeps its original furrow tile");
        TestAssert.Equal(2, BlockDb.FaceTile(BlockId.Farmland, 1), "farmland underside remains dirt");
        TestAssert.Equal(37, BlockDb.FaceTile(BlockId.Farmland, 2), "farmland edges use the tilled-soil cap tile");
        TestAssert.Equal(-1, BlockDb.FaceTile((BlockId)127, 0), "undefined persisted IDs have no texture face");
        TestAssert.Equal(-1, BlockDb.FaceTile(BlockId.Grass, 6), "invalid face indices do not read past the block definition");

        Color grassTop = BlockDb.AverageFaceColor(BlockId.Grass, 0);
        Color grassSide = BlockDb.AverageFaceColor(BlockId.Grass, 2);
        Color dirtTop = BlockDb.AverageFaceColor(BlockId.Dirt, 0);
        Color farmlandTop = BlockDb.AverageFaceColor(BlockId.Farmland, 0);
        TestAssert.Check(grassTop.G > grassTop.R * 1.35f, "grass top average retains a clear green identity for far LOD");
        TestAssert.Check(dirtTop.R > dirtTop.G, "exposed dirt average remains warm brown");
        TestAssert.Check(farmlandTop.R > farmlandTop.G, "tilled soil top remains distinct from grass green");
        TestAssert.Check(grassSide.R > grassTop.R * 0.85f,
                "grass side keeps its brown soil body rather than reusing the green top tint");
        TestAssert.Equal(BlockDb.AverageFaceColor(BlockId.Grass, 4), BlockDb.AverageTileColor(BlockId.Grass),
                "legacy icon/particle color still uses the original +Z side tile");

        Image atlas = BlockDb.AtlasTexture().GetImage();
        Color furrow = Pixel(atlas, 36, 8, 3);
        Color ridge = Pixel(atlas, 36, 8, 5);
        TestAssert.Check(Luminance(ridge) > Luminance(furrow) + 0.20f,
                "farmland furrow shadow and ridge highlight remain readable at tile scale");

        Color farmlandCap = Pixel(atlas, 37, 2, 2);
        Color farmlandBody = Pixel(atlas, 37, 2, 12);
        TestAssert.Check(Luminance(farmlandBody) > Luminance(farmlandCap) + 0.10f,
                "farmland vertical face has a defined dark tilled cap over the soil body");

        Color grassCanopyRow = AverageRow(atlas, 1, 2);
        Color grassSoilRow = AverageRow(atlas, 1, 24);
        TestAssert.Check(grassCanopyRow.G > grassCanopyRow.R,
                "grass edge has a green upper band");
        TestAssert.Check(grassSoilRow.R > grassSoilRow.G,
                "grass edge transitions into brown exposed soil");
    }

    private static Color Pixel(Image atlas, int tile, int x, int y) =>
        atlas.GetPixel((tile % BlockDb.AtlasCols) * BlockDb.TilePx + x,
                (tile / BlockDb.AtlasCols) * BlockDb.TilePx + y);

    private static Color AverageRow(Image atlas, int tile, int y)
    {
        Vector3 sum = Vector3.Zero;
        for (int x = 0; x < BlockDb.TilePx; x++)
        {
            Color c = Pixel(atlas, tile, x, y);
            sum += new Vector3(c.R, c.G, c.B);
        }

        sum /= BlockDb.TilePx;
        return new Color(sum.X, sum.Y, sum.Z);
    }

    private static float Luminance(Color color) =>
        color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
}
