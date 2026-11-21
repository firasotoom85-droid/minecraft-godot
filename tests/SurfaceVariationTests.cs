using Godot;

namespace Minecraft;

/// <summary>Regression coverage for subtle stone albedo variation and atlas reuse.</summary>
public static class SurfaceVariationTests
{
    public static void Run()
    {
        TestAssert.Equal(16, BlockDb.AtlasCols, "surface detail preserves atlas column count");
        TestAssert.Equal(256, BlockDb.TileCount, "surface detail preserves atlas layer count");
        TestAssert.Equal(32, BlockDb.TilePx, "surface detail preserves pixel-art tile resolution");

        TestAssert.Equal(3, BlockDb.IconTile(BlockId.Stone), "stone atlas tile index remains stable");
        TestAssert.Equal(10, BlockDb.IconTile(BlockId.Cobblestone), "cobblestone atlas tile index remains stable");
        TestAssert.Equal(11, BlockDb.IconTile(BlockId.Brick), "brick atlas tile index remains stable");
        TestAssert.Equal(18, BlockDb.IconTile(BlockId.Gravel), "gravel atlas tile index remains stable");

        CheckVariation(BlockId.Stone, 0.035f, "stone");
        CheckVariation(BlockId.Cobblestone, 0.045f, "cobblestone");
        CheckVariation(BlockId.Gravel, 0.032f, "gravel");
        CheckVariation(BlockId.Brick, 0.022f, "brick");

        TestAssert.Equal(0.0f, BlockDb.SurfaceVariationOf(BlockId.Dirt),
                "dirt keeps its original uniform block tone");
        TestAssert.Equal(0.0f, BlockDb.SurfaceVariationOf(BlockId.Sand),
                "sand keeps its original material tone");
        TestAssert.Equal(0.0f, BlockDb.SurfaceVariationOf(BlockId.Planks),
                "wood is not assigned stone variation");
        TestAssert.Equal(0.0f, BlockDb.SurfaceVariationOf(BlockId.Air),
                "air has no surface variation");

        Color source = new(0.2f, 0.4f, 0.6f, 0.9f);
        Color styled = BlockDb.WithSurfaceVariation(source, BlockId.Stone);
        TestAssert.Equal(source.R, styled.R, "variation metadata preserves red light channel");
        TestAssert.Equal(source.G, styled.G, "variation metadata preserves green light channel");
        TestAssert.Equal(source.B, styled.B, "variation metadata preserves blue AO channel");
        TestAssert.Equal(0.035f, styled.A, "variation strength occupies the unused alpha channel");
        TestAssert.Equal(0.0f, BlockDb.WithSurfaceVariation(source, BlockId.Dirt).A,
                "unconfigured materials disable shader variation");

        Image atlas = BlockDb.AtlasTexture().GetImage();
        Vector2I atlasSize = atlas.GetSize();
        TestAssert.Equal(new Vector2I(BlockDb.AtlasCols * BlockDb.TilePx,
                BlockDb.AtlasCols * BlockDb.TilePx), atlasSize,
                "original atlas dimensions are preserved");
        CheckTileHasReadableDetail(atlas, BlockDb.IconTile(BlockId.Stone), "stone");
        CheckTileHasReadableDetail(atlas, BlockDb.IconTile(BlockId.Cobblestone), "cobblestone");
        CheckTileHasReadableDetail(atlas, BlockDb.IconTile(BlockId.Brick), "brick");
        CheckTileHasReadableDetail(atlas, BlockDb.IconTile(BlockId.Gravel), "gravel");

        // The existing first-person held-item Sprite3D resolves block icons
        // through ItemDb.Icon; keep that route attached to the same atlas.
        if (ItemDb.Icon(ItemId.Stone) is AtlasTexture stoneIcon)
        {
            TestAssert.Check(stoneIcon.Atlas == BlockDb.AtlasTexture(),
                    "first-person stone icon uses the shared block atlas");
            Rect2 expected = BlockDb.TileUvRect(BlockDb.IconTile(BlockId.Stone));
            Rect2 expectedPixels = new(expected.Position * BlockDb.AtlasCols * BlockDb.TilePx,
                    expected.Size * BlockDb.AtlasCols * BlockDb.TilePx);
            TestAssert.Equal(expectedPixels, stoneIcon.Region,
                    "first-person stone icon keeps its original tile region");
        }
        else
        {
            TestAssert.Check(false, "first-person stone icon remains atlas-backed");
        }

        CheckOpaqueShader(BlockDb.MakeOpaqueMaterial(), "atlas opaque shader");
        CheckOpaqueShader(BlockDb.MakeArrayOpaqueMaterial(), "greedy texture-array shader");
    }

    private static void CheckVariation(BlockId id, float expected, string label)
    {
        float variation = BlockDb.SurfaceVariationOf(id);
        TestAssert.Equal(expected, variation, $"{label} variation strength is explicitly configured");
        TestAssert.Check(variation > 0.0f && variation <= 0.05f,
                $"{label} variation remains low contrast");
    }

    private static void CheckTileHasReadableDetail(Image atlas, int tile, string label)
    {
        int ox = (tile % BlockDb.AtlasCols) * BlockDb.TilePx;
        int oy = (tile / BlockDb.AtlasCols) * BlockDb.TilePx;
        float min = 1.0f;
        float max = 0.0f;
        for (int y = 0; y < BlockDb.TilePx; y++)
        for (int x = 0; x < BlockDb.TilePx; x++)
        {
            Color pixel = atlas.GetPixel(ox + x, oy + y);
            TestAssert.Check(pixel.A > 0.99f, $"{label} atlas pixels stay fully opaque");
            float luminance = (pixel.R + pixel.G + pixel.B) / 3.0f;
            min = Mathf.Min(min, luminance);
            max = Mathf.Max(max, luminance);
        }

        TestAssert.Check(max - min >= 0.025f,
                $"{label} tile contains controlled pixel-scale tone detail");
    }

    private static void CheckOpaqueShader(ShaderMaterial material, string label)
    {
        string code = material.Shader?.Code ?? "";
        TestAssert.Check(code.Contains("surface_cell_hash"),
                $"{label} applies deterministic voxel-coordinate variation");
        TestAssert.Check(code.Contains("surface_world_position"),
                $"{label} derives variation from world-space position");
        TestAssert.Check(code.Contains("surface_variation = COLOR.a"),
                $"{label} reads the per-block variation strength");
        TestAssert.Check(code.Contains("texture("),
                $"{label} retains its original atlas texture sampling");
    }
}
