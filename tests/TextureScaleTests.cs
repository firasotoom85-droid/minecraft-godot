using Godot;

namespace Minecraft;

/// <summary>
/// Regression coverage for the consolidated terrain texture work: the greedy
/// renderer samples a mipmapped texture array rather than the packed atlas,
/// and procedural noise keeps a coherent authored detail unit.
/// </summary>
public static class TextureScaleTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();

        // Greedy terrain samples a texture array, not the packed atlas. The
        // array must preserve every tile and carry its isolated mip chains.
        Texture2DArray tileArray = BlockDb.TileArrayTexture();
        TestAssert.Equal(BlockDb.TileCount, tileArray.GetLayers(),
                "mipmapped tile array preserves the full atlas layer layout");
        TestAssert.Check(tileArray.HasMipmaps(),
                "texture-array layers include generated mipmaps");

        TestAssert.Equal(2, BlockDb.TextureDetailPx,
                "procedural material noise shares a two-texel minimum detail unit");
        Image atlas = BlockDb.AtlasTexture().GetImage();
        int snowTile = BlockDb.DefOf(BlockId.Snow).Tiles[0];
        int snowX = snowTile % BlockDb.AtlasCols * BlockDb.TilePx;
        int snowY = snowTile / BlockDb.AtlasCols * BlockDb.TilePx;
        for (int y = 0; y < BlockDb.TilePx; y += BlockDb.TextureDetailPx)
            for (int x = 0; x < BlockDb.TilePx; x += BlockDb.TextureDetailPx)
            {
                Color sample = atlas.GetPixel(snowX + x, snowY + y);
                for (int dy = 0; dy < BlockDb.TextureDetailPx; dy++)
                    for (int dx = 0; dx < BlockDb.TextureDetailPx; dx++)
                    {
                        Color neighbour = atlas.GetPixel(snowX + x + dx, snowY + y + dy);
                        TestAssert.Check(Mathf.IsEqualApprox(sample.R, neighbour.R)
                                && Mathf.IsEqualApprox(sample.G, neighbour.G)
                                && Mathf.IsEqualApprox(sample.B, neighbour.B)
                                && Mathf.IsEqualApprox(sample.A, neighbour.A),
                                $"snow noise keeps a coherent {BlockDb.TextureDetailPx}x{BlockDb.TextureDetailPx} texel cluster at {x},{y}");
                    }
            }
    }
}
