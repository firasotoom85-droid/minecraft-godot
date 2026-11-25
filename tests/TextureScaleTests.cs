using System;
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

        // Greedy terrain samples the center tile of a 3x3 wrapped image in one
        // texture-array layer. The repeated copies make every generated mip level
        // see matching pixels across the source tile's opposite edges.
        Texture2DArray tileArray = BlockDb.TileArrayTexture();
        TestAssert.Equal(BlockDb.TileCount, tileArray.GetLayers(),
                "mipmapped tile array preserves the full atlas layer layout");
        TestAssert.Equal(BlockDb.TileArrayPixels, tileArray.GetWidth(),
                "each array layer includes three horizontal wrap copies");
        TestAssert.Equal(BlockDb.TileArrayPixels, tileArray.GetHeight(),
                "each array layer includes three vertical wrap copies");
        TestAssert.Check(tileArray.HasMipmaps(),
                "texture-array layers include generated repeat-aware mip chains");

        Vector2 tileOrigin = BlockDb.TileArrayUv(Vector2.Zero);
        Vector2 tileEnd = BlockDb.TileArrayUv(Vector2.One);
        TestAssert.Check(Mathf.IsEqualApprox(tileOrigin.X, 1.0f / 3.0f)
                        && Mathf.IsEqualApprox(tileOrigin.Y, 1.0f / 3.0f),
                "repeated UV zero maps to the center source-tile copy");
        TestAssert.Check(tileOrigin.DistanceSquaredTo(tileEnd) < 0.000001f,
                "integer UV boundaries wrap to the same center-copy coordinate");
        Vector2 wrapped = BlockDb.TileArrayUv(new Vector2(1.25f, -0.25f));
        TestAssert.Check(Mathf.IsEqualApprox(wrapped.X, 1.0f / 3.0f + 0.25f / 3.0f)
                        && Mathf.IsEqualApprox(wrapped.Y, 1.0f / 3.0f + 0.75f / 3.0f),
                "negative and positive repeated UVs wrap consistently into the center copy");

        Image atlasForWrap = BlockDb.AtlasTexture().GetImage();
        int wrapTileIndex = BlockDb.DefOf(BlockId.Snow).Tiles[0];
        Image wrappedTile = BlockDb.BuildWrappedTileImage(atlasForWrap, wrapTileIndex);
        try
        {
            TestAssert.Equal(BlockDb.TileArrayPixels, wrappedTile.GetWidth(),
                    "wrapped source image width matches its texture-array layer");
            TestAssert.Equal(BlockDb.TileArrayPixels, wrappedTile.GetHeight(),
                    "wrapped source image height matches its texture-array layer");
            for (int repeatY = 0; repeatY < BlockDb.TileArrayRepeatCount; repeatY++)
            {
                for (int repeatX = 0; repeatX < BlockDb.TileArrayRepeatCount; repeatX++)
                {
                    for (int y = 0; y < BlockDb.TilePx; y++)
                    {
                        for (int x = 0; x < BlockDb.TilePx; x++)
                        {
                            Color expected = wrappedTile.GetPixel(BlockDb.TilePx + x, BlockDb.TilePx + y);
                            Color actual = wrappedTile.GetPixel(
                                    repeatX * BlockDb.TilePx + x, repeatY * BlockDb.TilePx + y);
                            TestAssert.Check(expected == actual,
                                    $"tile wrap copy ({repeatX},{repeatY}) preserves source pixel ({x},{y})");
                        }
                    }
                }
            }

            TestAssert.Equal(Error.Ok, wrappedTile.GenerateMipmaps(),
                    "repeat copies produce a valid mip chain before texture-array upload");
            TestAssert.Check(wrappedTile.HasMipmaps(),
                    "the wrapped source tile retains its generated mip chain");
        }
        finally
        {
            wrappedTile.Dispose();
        }

        string greedyShader = BlockDb.WarmLightingShaderSources[2];
        TestAssert.Check(greedyShader.Contains("textureGrad(", StringComparison.Ordinal)
                        && greedyShader.Contains("dFdx(tile_uv) / 3.0", StringComparison.Ordinal)
                        && greedyShader.Contains("dFdy(tile_uv) / 3.0", StringComparison.Ordinal)
                        && greedyShader.Contains("(vec2(1.0) + fract(tile_uv)) / 3.0", StringComparison.Ordinal),
                "greedy sampling uses the center wrap copy and mip gradients scaled to its one-third UV region");

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
