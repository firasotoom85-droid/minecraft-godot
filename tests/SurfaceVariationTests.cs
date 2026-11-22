using Godot;

namespace Minecraft;

/// <summary>Surface families and light blending stay stable across every render path.</summary>
public static class SurfaceVariationTests
{
    public static void Run()
    {
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
            TestAssert.Check(shader.Contains("COLOR.a < 0.5"), $"{label} limits variation to the stone response family");
        }

        foreach ((string shader, string label) in new[]
        {
            (atlasShader, "atlas"), (arrayShader, "greedy array"), (glassShader, "glass"),
        })
        {
            TestAssert.Check(shader.Contains("clamp(COLOR.r * sky_light_energy, 0.0, 1.0)"),
                    $"{label} bounds sky contribution before blending light channels");
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

        TestAssert.Check(waterShader.Contains("float shadowed_block = block * (1.0 - sky);"),
                "water retains its separate sky-headroom block-light blend");
        TestAssert.Check(waterShader.Contains("shadowed_block * 0.08"),
                "water retains its restrained residual torch glow");
        TestAssert.Check(waterShader.Contains("clamp(COLOR.r * sky_light_energy, 0.0, 1.0)"),
                "water bounds sky contribution before blending light channels");

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
}
