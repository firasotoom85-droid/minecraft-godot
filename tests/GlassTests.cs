using Godot;

namespace Minecraft;

/// <summary>Regression coverage for glass transparency, sorting and atlas readability.</summary>
public static class GlassTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();
        TestGlassShader();
        TestGlassChunkSorting();
        TestGlassAtlasAlpha();
    }

    private static void TestGlassShader()
    {
        string shader = ChunkMaterials.Glass.Shader?.Code ?? string.Empty;
        TestAssert.Check(shader.Contains("blend_mix"),
                "glass keeps alpha blending for readable transparent panes");
        TestAssert.Check(shader.Contains("cull_back") && !shader.Contains("cull_disabled"),
                "glass renders only front-facing surfaces to avoid double-blended internal faces");
        TestAssert.Check(shader.Contains("depth_draw_opaque"),
                "glass does not write transparent fragments into the opaque depth buffer");
    }

    private static void TestGlassChunkSorting()
    {
        Chunk chunk = new();
        VoxelWorld world = new();
        chunk.Setup(world, Vector2I.Zero);
        MeshInstance3D? glass = chunk.GetNodeOrNull<MeshInstance3D>("GlassMesh");
        TestAssert.Check(glass is not null, "chunk retains its dedicated glass surface");
        if (glass is not null)
            TestAssert.Check(glass.SortingUseAabbCenter,
                    "transparent glass sorting uses mesh bounds instead of the chunk origin");
        chunk.Free();
        world.Free();
    }

    private static void TestGlassAtlasAlpha()
    {
        Image atlas = BlockDb.AtlasTexture().GetImage();
        const int tile = 14;
        int originX = (tile % BlockDb.AtlasCols) * BlockDb.TilePx;
        int originY = (tile / BlockDb.AtlasCols) * BlockDb.TilePx;
        float centerAlpha = atlas.GetPixel(originX + 16, originY + 16).A;
        float edgeAlpha = atlas.GetPixel(originX, originY + 16).A;
        float glintAlpha = atlas.GetPixel(originX + 6, originY + 6).A;
        TestAssert.Check(centerAlpha > 0.0f && centerAlpha < 0.15f,
                "glass center remains clear enough to see the scene behind it");
        TestAssert.Check(edgeAlpha > centerAlpha && edgeAlpha < 0.4f,
                "single-pixel glass edge remains visible without a heavy tile grid");
        TestAssert.Check(glintAlpha > centerAlpha && glintAlpha < 0.4f,
                "subtle glass glint remains visible at close and distant scales");
    }
}
