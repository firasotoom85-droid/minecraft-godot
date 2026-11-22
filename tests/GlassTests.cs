using Godot;

namespace Minecraft;

/// <summary>Regression coverage for blended, cutout and emissive voxel material contracts.</summary>
public static class GlassTests
{
    public static void Run(Node host)
    {
        BlockDb.EnsureTables();
        TestGlassShader();
        TestWaterShader();
        TestLavaDepthBehavior();
        TestCutoutShaders();
        TestTransparentChunkSorting();
        TestCutoutBlockClassification();
        TestChunkSurfaceRouting(host);
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
        TestAssert.Check(shader.Contains("ALPHA = tex.a * 0.58;")
                        && !shader.Contains("ALPHA_SCISSOR_THRESHOLD"),
                "glass keeps continuous atlas alpha instead of cutout thresholding");
    }

    private static void TestWaterShader()
    {
        string shader = (ChunkMaterials.Water as ShaderMaterial)?.Shader?.Code ?? string.Empty;
        TestAssert.Check(shader.Contains("blend_mix"),
                "water retains alpha blending for shoreline and underwater visibility");
        TestAssert.Check(shader.Contains("depth_draw_opaque"),
                "water keeps translucent fragments out of opaque depth writes");
        TestAssert.Check(shader.Contains("cull_disabled"),
                "water remains visible from above and below the surface");
        TestAssert.Check(shader.Contains("ALPHA = clamp("),
                "water alpha remains bounded after depth and fresnel modulation");
        TestAssert.Check(!shader.Contains("ALPHA_SCISSOR_THRESHOLD"),
                "water remains blended rather than entering the binary cutout path");
    }

    private static void TestLavaDepthBehavior()
    {
        string shader = ChunkMaterials.Lava.Shader?.Code ?? string.Empty;
        TestAssert.Check(shader.Contains("depth_draw_opaque"),
                "emissive lava keeps depth writing for fully opaque texels");
        TestAssert.Check(shader.Contains("ALPHA = tex.a;") && !shader.Contains("blend_mix"),
                "lava remains on the opaque emissive surface rather than alpha sorting");
    }

    private static void TestCutoutShaders()
    {
        CheckCutoutShader(BlockDb.MakeOpaqueMaterial().Shader?.Code ?? string.Empty,
                "packed atlas");
        CheckCutoutShader(BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty,
                "greedy texture array");
        TestAssert.Check(ProjectSettings.GetSetting(
                        "rendering/anti_aliasing/quality/msaa_3d").AsInt32() > 0,
                "Forward+ enables MSAA so alpha-to-coverage can soften foliage silhouettes");
    }

    private static void CheckCutoutShader(string shader, string path)
    {
        TestAssert.Check(shader.Contains("depth_draw_opaque"),
                $"{path} cutouts write depth for surviving opaque texels");
        TestAssert.Check(shader.Contains("alpha_to_coverage"),
                $"{path} cutouts retain multisample alpha edge coverage");
        TestAssert.Check(shader.Contains("ALPHA = tex.a;")
                        && shader.Contains("ALPHA_SCISSOR_THRESHOLD = 0.5;"),
                $"{path} uses the same binary alpha threshold for leaves and cross-plants");
        TestAssert.Check(!shader.Contains("blend_mix"),
                $"{path} cutouts do not enter the sorted alpha-blended pipeline");
    }

    private static void TestTransparentChunkSorting()
    {
        Chunk chunk = new();
        VoxelWorld world = new();
        chunk.Setup(world, Vector2I.Zero);
        MeshInstance3D? glass = chunk.GetNodeOrNull<MeshInstance3D>("GlassMesh");
        MeshInstance3D? water = chunk.GetNodeOrNull<MeshInstance3D>("WaterMesh");
        TestAssert.Check(glass is not null, "chunk retains its dedicated glass surface");
        TestAssert.Check(water is not null, "chunk retains its dedicated water surface");
        if (glass is not null)
            TestAssert.Check(glass.SortingUseAabbCenter,
                    "glass sorts using its mesh bounds center instead of the chunk origin");
        if (water is not null)
            TestAssert.Check(water.SortingUseAabbCenter,
                    "water sorts using its mesh bounds center instead of the chunk origin");
        chunk.Free();
        world.Free();
    }

    private static void TestCutoutBlockClassification()
    {
        TestAssert.Check(BlockDb.IsFoliage(BlockId.Leaves)
                        && !BlockDb.IsTransparent(BlockId.Leaves),
                "leaf canopies stay on the opaque depth-writing cutout path");
        TestAssert.Check(BlockDb.IsCross(BlockId.TallGrass)
                        && BlockDb.IsTransparent(BlockId.TallGrass),
                "cross-plants retain their data classification and use the explicit cutout mesher path");
        TestAssert.Check(BlockDb.IsTransparent(BlockId.Glass)
                        && BlockDb.IsTransparent(BlockId.Water),
                "glass and water remain separate blended block surfaces");
    }

    private static void TestChunkSurfaceRouting(Node host)
    {
        string dir = $"user://tests/transparent-materials-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Transparent material routing", 8675309, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(8675309), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new() { BuildCollision = false };
        host.AddChild(world);
        bool originalGreedy = Mesher.Greedy;
        try
        {
            Mesher.Greedy = false;
            world.BuildPreview(Vector3.Zero, 1);

            // Isolate each material at altitude so terrain and adjacent cells
            // cannot accidentally satisfy a surface-routing assertion.
            for (int y = 99; y <= 102; y++)
            for (int z = 1; z <= 3; z++)
            for (int x = 1; x <= 9; x++)
                world.SetBlock(x, y, z, BlockId.Air);

            TestAssert.Check(world.SetBlock(2, 100, 2, BlockId.TallGrass),
                    "cross-plant fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(4, 100, 2, BlockId.Leaves),
                    "foliage fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(6, 100, 2, BlockId.Glass),
                    "glass fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(8, 100, 2, BlockId.Water),
                    "water fixture is placed through VoxelWorld.SetBlock");

            foreach (bool greedy in new[] { false, true })
            {
                Mesher.Greedy = greedy;
                MeshPayload payload = Mesher.Build(world, Vector2I.Zero);
                try
                {
                    string mode = greedy ? "greedy" : "per-face";
                    TestAssert.Equal(8, CountTrianglesInCell(payload.Opaque.Vertex, 2, 100, 2),
                            $"{mode} meshing routes double-sided cross-plants to the opaque cutout surface");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Opaque.Vertex, 4, 100, 2),
                            $"{mode} meshing routes leaf cubes to the opaque cutout surface");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Glass.Vertex, 6, 100, 2),
                            $"{mode} meshing routes glass faces to the blended glass surface");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Water.Vertex, 8, 100, 2),
                            $"{mode} meshing routes water faces to the blended water surface");
                    TestAssert.Equal(0, CountTrianglesInCell(payload.Opaque.Vertex, 6, 100, 2),
                            $"{mode} meshing does not duplicate glass into the opaque surface");
                    TestAssert.Equal(0, CountTrianglesInCell(payload.Opaque.Vertex, 8, 100, 2),
                            $"{mode} meshing does not duplicate water into the opaque surface");
                }
                finally
                {
                    PayloadPool.Release(payload);
                }
            }
        }
        finally
        {
            Mesher.Greedy = originalGreedy;
            host.RemoveChild(world);
            world.QueueFree();
            DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static int CountTrianglesInCell(
            System.Collections.Generic.IReadOnlyList<Vector3> vertices, int x, int y, int z)
    {
        Vector3 min = new(x, y, z);
        Vector3 max = min + Vector3.One;
        int count = 0;
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            bool inside = true;
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 point = vertices[i + vertex];
                inside &= point.X >= min.X - 0.0001f && point.X <= max.X + 0.0001f
                        && point.Y >= min.Y - 0.0001f && point.Y <= max.Y + 0.0001f
                        && point.Z >= min.Z - 0.0001f && point.Z <= max.Z + 0.0001f;
            }
            if (inside) count++;
        }
        return count;
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
