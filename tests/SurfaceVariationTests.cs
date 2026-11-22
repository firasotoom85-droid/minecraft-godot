using Godot;

namespace Minecraft;

/// <summary>Stone variation remains subtle, deterministic and world-space stable.</summary>
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
        foreach ((string shader, string label) in new[] { (atlasShader, "atlas"), (arrayShader, "greedy array") })
        {
            TestAssert.Check(shader.Contains("surface_cell_hash"), $"{label} uses deterministic world-cell variation");
            TestAssert.Check(shader.Contains("surface_world_position"), $"{label} anchors variation in world space");
            TestAssert.Check(shader.Contains("tone * 0.035"), $"{label} keeps stone variation low contrast");
            TestAssert.Check(shader.Contains("COLOR.a < 0.5"), $"{label} limits variation to the stone response family");
        }
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(49), "plank top grain keeps the wood response");
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(50), "plank side grain keeps the wood response");
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(51), "plank end grain keeps the wood response");
    }
}
