using Godot;

namespace Minecraft;

/// <summary>Surface families and light blending stay stable across every render path.</summary>
public static class SurfaceVariationTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();
        CheckCalmTileableTerrainTextures();

        TestAssert.Equal(16, BlockDb.AtlasCols, "stone variation preserves atlas columns");
        TestAssert.Equal(256, BlockDb.TileCount, "stone variation preserves atlas layer count");
        TestAssert.Equal(32, BlockDb.TilePx, "stone variation preserves native tile resolution");
        TestAssert.Equal(3, BlockDb.IconTile(BlockId.Stone), "stone icon tile remains stable");
        TestAssert.Equal(10, BlockDb.IconTile(BlockId.Cobblestone), "cobblestone icon tile remains stable");
        TestAssert.Equal(11, BlockDb.IconTile(BlockId.Brick), "brick icon tile remains stable");

        string atlasShader = BlockDb.MakeOpaqueMaterial().Shader?.Code ?? string.Empty;
        string arrayShader = BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty;
        string glassShader = BlockDb.MakeGlassMaterial().Shader?.Code ?? string.Empty;
        string waterShader = ResourceLoader.Load<Shader>("res://shaders/water.gdshader")?.Code ?? string.Empty;
        TestAssert.Check(!string.IsNullOrEmpty(waterShader), "animated water shader is available for lighting regression checks");
        foreach ((string shader, string label) in new[]
        {
            (atlasShader, "atlas"), (arrayShader, "greedy array"),
        })
        {
            TestAssert.Check(shader.Contains("surface_cell_hash"), $"{label} uses deterministic world-cell variation");
            TestAssert.Check(shader.Contains("surface_world_position"), $"{label} anchors variation in world space");
            TestAssert.Check(shader.Contains("tone * 0.035"), $"{label} keeps stone variation low contrast");
            TestAssert.Check(shader.Contains("mod(floor(COLOR.a + 0.001), 8.0) < 0.5"),
                    $"{label} keeps the material-family test independent of fractional cave fill");
        }

        foreach ((string shader, string label) in new[]
        {
            (atlasShader, "atlas"), (arrayShader, "greedy array"),
            (glassShader, "glass"), (waterShader, "water"),
        })
        {
            TestAssert.Check(shader.Contains("float direct_sky = clamp(COLOR.r * sky_light_energy, 0.0, 1.0);"),
                    $"{label} bounds direct sky before combining light channels");
            TestAssert.Check(shader.Contains(
                            "float bounce = clamp(fract(COLOR.a) * 16.0 * sky_light_energy, 0.0, 1.0);")
                    && shader.Contains("cave_bounce_illumination(bounce)"),
                    $"{label} consumes the independent cave-spill channel");
            TestAssert.Check(shader.Contains("warm_block_response(float sky, float block)")
                    && shader.Contains("return (1.0 - exp(-block * 1.25)) * (1.0 - sky);"),
                    $"{label} consumes warm block light only where skylight leaves headroom");
            TestAssert.Check(shader.Contains("warm_block_illumination(warm_response)")
                    && shader.Contains("warm_block_emission(warm_response)")
                    && shader.Split("exp(-block * 1.25)", System.StringSplitOptions.None).Length == 2,
                    $"{label} shares one bounded warm response between diffuse and emission");
            TestAssert.Check(shader.Contains("return warm_block_light_color * response * 0.025;"),
                    $"{label} keeps the residual torch glow restrained");
            TestAssert.Check(!shader.Contains("warm * block * 0.88")
                    && !shader.Contains("warm * block * 0.72")
                    && !shader.Contains("warm * block * 0.5"),
                    $"{label} does not double-count full block light as strong emission");
        }

        TestAssert.Check(waterShader.Contains("float warm_response = warm_block_response(sky, block);")
                        && waterShader.Contains("EMISSION = tex.rgb * warm_block_emission(warm_response);")
                        && !waterShader.Contains("shadowed_block"),
                "water uses the same sky-headroom response and residual emission as opaque voxel materials");

        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(49), "plank top grain keeps the wood response");
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(50), "plank side grain keeps the wood response");
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(51), "plank end grain keeps the wood response");
        for (int tile = 52; tile <= 55; tile++)
        {
            TestAssert.Equal(SurfaceFamily.Stone, MaterialResponse.ForTile(tile),
                    $"weathered stone tile {tile} keeps the stone response");
        }
        for (int tile = 56; tile <= 58; tile++)
        {
            TestAssert.Equal(SurfaceFamily.Foliage, MaterialResponse.ForTile(tile),
                    $"biome foliage tile {tile} avoids stone shading and response");
        }

        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForItem(ItemId.Torch),
                "held torch inherits the wood response of its registered block tile");
        TestAssert.Equal(SurfaceFamily.Foliage, MaterialResponse.ForItem(ItemId.SpruceLeaves),
                "held spruce leaves inherit the foliage response");
        TestAssert.Equal(SurfaceFamily.Foliage, MaterialResponse.ForItem(ItemId.JungleLeaves),
                "held jungle leaves inherit the foliage response");
        TestAssert.Equal(SurfaceFamily.Foliage, MaterialResponse.ForItem(ItemId.SwampLeaves),
                "held swamp leaves inherit the foliage response");

        foreach (ItemId item in ItemDb.Placeable)
        {
            BlockId? block = ItemDb.BlockFor(item);
            TestAssert.Check(block.HasValue, $"placeable item {item} resolves to its block definition");
            if (block is { } blockId)
            {
                TestAssert.Equal(MaterialResponse.ForTile(BlockDb.IconTile(blockId)), MaterialResponse.ForItem(item),
                        $"held {item} material response matches its placed block atlas tile");
            }
        }

        TestAssert.Equal(0.0f, MaterialResponse.ShadowedBlockLight(1.0f, 1.0f),
                "full daylight does not stack torch light into an already full light budget");
        TestAssert.Equal(0.0f, MaterialResponse.ShadowedBlockLight(0.4f, 0.0f),
                "unlit surfaces receive no block-light contribution");
        float rainTorch = MaterialResponse.ShadowedBlockLight(0.38f, 0.93f);
        float nightTorch = MaterialResponse.ShadowedBlockLight(0.10f, 0.93f);
        float caveTorch = MaterialResponse.ShadowedBlockLight(0.0f, 0.93f);
        TestAssert.Check(rainTorch > 0.0f && rainTorch < nightTorch && nightTorch < caveTorch,
                "rain, night and cave torch contribution grows as skylight falls");
        TestAssert.Check(caveTorch <= 1.0f && float.IsFinite(caveTorch),
                "torch contribution remains finite and bounded in a fully dark cave");
    }

    private static void CheckCalmTileableTerrainTextures()
    {
        Image atlas = BlockDb.AtlasTexture().GetImage();
        CheckTileableSurface(atlas, 4, "sand");
        CheckTileableSurface(atlas, 16, "snow");
        CheckTileableSurface(atlas, 17, "snowy grass side",
                checkVertical: false, checkContrastRange: false);
        CheckTileableSurface(atlas, 60, "ash");

        Color sand = AverageTile(atlas, 4);
        TestAssert.Check(sand.R > sand.G && sand.G > sand.B,
                "sand retains its warm golden material identity");
        Color snow = AverageTile(atlas, 16);
        TestAssert.Check(Luminance(snow) > 0.86f && snow.B > snow.R,
                "snow remains bright and cool rather than turning gray");
        Color ash = AverageTile(atlas, 60);
        TestAssert.Check(Luminance(ash) is > 0.22f and < 0.52f
                        && Mathf.Abs(ash.R - ash.B) < 0.06f,
                "ash reads as restrained neutral charcoal rather than a saturated material");

        BlockDb.BlockDef ashDefinition = BlockDb.DefOf(BlockId.Ash);
        TestAssert.Check(ashDefinition.Solid && !ashDefinition.Transparent,
                "ash is a normal opaque solid block on the existing mesher path");
        TestAssert.Equal(ItemId.Ash, ashDefinition.Drop,
                "mined ash returns its append-only placeable item ID");
        TestAssert.Equal(ItemId.Ash, ItemDb.BlockItem(BlockId.Ash),
                "ash block maps to its matching inventory item");
        TestAssert.Equal(BlockId.Ash, ItemDb.BlockFor(ItemId.Ash)!.Value,
                "ash inventory item maps back to its block definition");
        TestAssert.Check(BlockDb.IsWettable(BlockId.Ash),
                "ash uses the existing damp soil response beside water");
        TestAssert.Equal(SurfaceFamily.Soil, MaterialResponse.ForTile(60),
                "ash atlas tile uses the diffuse soil material response");
        TestAssert.Equal(SurfaceFamily.Soil, MaterialResponse.ForItem(ItemId.Ash),
                "held ash inherits the placed block's diffuse material response");

        Texture2D icon = ItemDb.Icon(ItemId.Ash);
        TestAssert.Check(icon is AtlasTexture,
                "first-person ash item reuses the production atlas icon path");
        if (icon is AtlasTexture atlasIcon)
        {
            TestAssert.Check(ReferenceEquals(atlasIcon.Atlas, BlockDb.AtlasTexture()),
                    "first-person ash icon references the shared atlas resource");
            TestAssert.Equal(new Rect2(
                            60 % BlockDb.AtlasCols * BlockDb.TilePx,
                            60 / BlockDb.AtlasCols * BlockDb.TilePx,
                            BlockDb.TilePx, BlockDb.TilePx),
                    atlasIcon.Region,
                    "first-person ash icon uses appended tile 60 without moving existing UVs");
        }

        for (int face = 0; face < 6; face++)
        {
            TestAssert.Equal(60, BlockDb.FaceTile(BlockId.Ash, face),
                    $"ash face {face} uses its unique appended atlas tile");
        }
    }

    private static void CheckTileableSurface(Image atlas, int tile, string name,
            bool checkVertical = true, bool checkContrastRange = true)
    {
        int originX = tile % BlockDb.AtlasCols * BlockDb.TilePx;
        int originY = tile / BlockDb.AtlasCols * BlockDb.TilePx;
        float minLuminance = 1.0f;
        float maxLuminance = 0.0f;
        double horizontalSeam = 0.0;
        double horizontalInterior = 0.0;
        double verticalSeam = 0.0;
        double verticalInterior = 0.0;

        for (int y = 0; y < BlockDb.TilePx; y++)
        for (int x = 0; x < BlockDb.TilePx; x++)
        {
            Color pixel = atlas.GetPixel(originX + x, originY + y);
            float luminance = Luminance(pixel);
            minLuminance = Mathf.Min(minLuminance, luminance);
            maxLuminance = Mathf.Max(maxLuminance, luminance);
            TestAssert.Check(Mathf.IsEqualApprox(pixel.A, 1.0f),
                    $"{name} tile remains fully opaque at {x},{y}");

            if (x == BlockDb.TilePx - 1)
            {
                Color first = atlas.GetPixel(originX, originY + y);
                horizontalSeam += Mathf.Abs(luminance - Luminance(first));
            }
            else
            {
                Color right = atlas.GetPixel(originX + x + 1, originY + y);
                horizontalInterior += Mathf.Abs(luminance - Luminance(right));
            }

            if (y == BlockDb.TilePx - 1)
            {
                Color first = atlas.GetPixel(originX + x, originY);
                verticalSeam += Mathf.Abs(luminance - Luminance(first));
            }
            else
            {
                Color below = atlas.GetPixel(originX + x, originY + y + 1);
                verticalInterior += Mathf.Abs(luminance - Luminance(below));
            }
        }

        int detailPx = BlockDb.TextureDetailPx;
        for (int y = 0; y < BlockDb.TilePx; y += detailPx)
        for (int x = 0; x < BlockDb.TilePx; x += detailPx)
        {
            Color cluster = atlas.GetPixel(originX + x, originY + y);
            for (int dy = 0; dy < detailPx; dy++)
            for (int dx = 0; dx < detailPx; dx++)
            {
                Color neighbour = atlas.GetPixel(originX + x + dx, originY + y + dy);
                TestAssert.Check(cluster == neighbour,
                        $"{name} retains coherent {detailPx}x{detailPx} detail clusters at {x},{y}");
            }
        }

        double seamHorizontalAverage = horizontalSeam / BlockDb.TilePx;
        double seamVerticalAverage = verticalSeam / BlockDb.TilePx;
        double interiorHorizontalAverage = horizontalInterior
                / (BlockDb.TilePx * (BlockDb.TilePx - 1));
        double interiorVerticalAverage = verticalInterior
                / (BlockDb.TilePx * (BlockDb.TilePx - 1));
        if (checkContrastRange)
        {
            TestAssert.Check(maxLuminance - minLuminance is > 0.008f and < 0.11f,
                    $"{name} has visible but low-contrast tonal variation ({minLuminance:F3}..{maxLuminance:F3})");
        }
        TestAssert.Check(seamHorizontalAverage <= interiorHorizontalAverage * 2.3 + 0.001,
                $"{name} horizontal tile seam is no harsher than ordinary interior detail");
        if (checkVertical)
        {
            TestAssert.Check(seamVerticalAverage <= interiorVerticalAverage * 2.3 + 0.001,
                    $"{name} vertical tile seam is no harsher than ordinary interior detail");
        }
    }

    private static Color AverageTile(Image atlas, int tile)
    {
        int originX = tile % BlockDb.AtlasCols * BlockDb.TilePx;
        int originY = tile / BlockDb.AtlasCols * BlockDb.TilePx;
        Vector3 sum = Vector3.Zero;
        for (int y = 0; y < BlockDb.TilePx; y++)
        for (int x = 0; x < BlockDb.TilePx; x++)
        {
            Color pixel = atlas.GetPixel(originX + x, originY + y);
            sum += new Vector3(pixel.R, pixel.G, pixel.B);
        }

        sum /= BlockDb.TilePx * BlockDb.TilePx;
        return new Color(sum.X, sum.Y, sum.Z);
    }

    private static float Luminance(Color color) =>
            color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
}
