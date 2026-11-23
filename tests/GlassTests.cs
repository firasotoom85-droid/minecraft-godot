using System.Collections.Generic;
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
        TestAssert.Check(!shader.Contains("ALPHA") && !shader.Contains("blend_mix"),
                "lava does not write ALPHA or enter the transparent sorting pipeline");
        TestAssert.Check(shader.Contains("EMISSION = tex.rgb * warm * lava_emission"),
                "lava retains its block-light-driven emissive output");
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
        TestAssert.Check(shader.Contains("ALPHA_ANTIALIASING_EDGE = 0.4;")
                        && 0.4f < 0.5f,
                $"{path} defines an alpha antialiasing edge below its scissor threshold");
        TestAssert.Check(!shader.Contains("blend_mix"),
                $"{path} cutouts do not enter the sorted alpha-blended pipeline");
    }

    private static void TestTransparentChunkSorting()
    {
        Chunk chunk = new();
        VoxelWorld world = new();
        chunk.Setup(world, Vector2I.Zero);

        TestAssert.Check(chunk.GetNodeOrNull<MeshInstance3D>("OpaqueMesh") is not null,
                "chunk creates the required opaque surface immediately");
        TestAssert.Check(chunk.GetNodeOrNull<MeshInstance3D>("GlassMesh") is null
                        && chunk.GetNodeOrNull<MeshInstance3D>("WaterMesh") is null
                        && chunk.GetNodeOrNull<MeshInstance3D>("LavaMesh") is null,
                "empty glass, water and lava surfaces do not allocate mesh nodes");
        TestAssert.Check(chunk.GetNodeOrNull<CollisionShape3D>("Collision") is null,
                "collision node is deferred until a payload contains collision geometry");

        SurfaceData opaque = CreateTriangleSurface();
        SurfaceData glassSurface = CreateTriangleSurface();
        SurfaceData waterSurface = CreateTriangleSurface();
        SurfaceData lavaSurface = CreateTriangleSurface();
        List<Vector3> collision = [Vector3.Zero, Vector3.Right, Vector3.Up];
        (bool greedy, long revision) = Mesher.CaptureConfiguration();
        MeshPayload populated = new(opaque, glassSurface, waterSurface, lavaSurface,
                collision, 0, revision, world.MissingColliderBoundaryMask(Vector2I.Zero));
        chunk.ApplyPayload(populated);
        TestAssert.Check(chunk.GetNodeOrNull<MeshInstance3D>("GlassMesh") is { SortingUseAabbCenter: true },
                "first non-empty glass payload creates a bounds-sorted node");
        TestAssert.Check(chunk.GetNodeOrNull<MeshInstance3D>("WaterMesh") is { SortingUseAabbCenter: true },
                "first non-empty water payload creates a bounds-sorted node");
        TestAssert.Check(chunk.GetNodeOrNull<MeshInstance3D>("LavaMesh") is { SortingUseAabbCenter: false },
                "opaque lava does not enter transparent chunk sorting");
        TestAssert.Check(chunk.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is ConcavePolygonShape3D,
                "non-empty collision payload creates the collider node lazily");

        PayloadPool.Release(populated);
        SurfaceData emptyOpaque = new();
        SurfaceData emptyGlass = new();
        SurfaceData emptyWater = new();
        SurfaceData emptyLava = new();
        MeshPayload emptied = new(emptyOpaque, emptyGlass, emptyWater, emptyLava,
                [], 0, revision, world.MissingColliderBoundaryMask(Vector2I.Zero));
        chunk.ApplyPayload(emptied);
        TestAssert.Check(chunk.GetNodeOrNull<MeshInstance3D>("GlassMesh") is { Mesh: null }
                        && chunk.GetNodeOrNull<MeshInstance3D>("WaterMesh") is { Mesh: null }
                        && chunk.GetNodeOrNull<MeshInstance3D>("LavaMesh") is { Mesh: null },
                "rebuilds clear optional surfaces without deleting or recreating their nodes");
        TestAssert.Check(chunk.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is null,
                "empty collision rebuild clears the existing shape");
        PayloadPool.Release(emptied);

        TestAssert.Check(Mesher.CaptureConfiguration().Greedy == greedy,
                "chunk surface setup does not mutate the active meshing configuration");
        chunk.Free();
        world.Free();
    }

    private static SurfaceData CreateTriangleSurface()
    {
        SurfaceData surface = new();
        surface.Vertex.AddRange([Vector3.Zero, Vector3.Right, Vector3.Up]);
        surface.Normal.AddRange([Vector3.Back, Vector3.Back, Vector3.Back]);
        surface.Color.AddRange([Colors.White, Colors.White, Colors.White]);
        surface.Uv.AddRange([Vector2.Zero, Vector2.Right, Vector2.Up]);
        return surface;
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
            for (int x = 1; x <= 12; x++)
                world.SetBlock(x, y, z, BlockId.Air);

            TestAssert.Check(world.SetBlock(2, 100, 2, BlockId.TallGrass),
                    "cross-plant fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(4, 100, 2, BlockId.Leaves),
                    "foliage fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(5, 100, 2, BlockId.Stone),
                    "opaque backing block is placed directly behind cutout foliage through SetBlock");
            TestAssert.Check(world.SetBlock(7, 100, 2, BlockId.Glass),
                    "glass fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(9, 100, 2, BlockId.Water),
                    "water fixture is placed through VoxelWorld.SetBlock");
            TestAssert.Check(world.SetBlock(11, 100, 2, BlockId.Ladder),
                    "ladder fixture is placed through VoxelWorld.SetBlock");

            foreach (bool greedy in new[] { false, true })
            {
                Mesher.Greedy = greedy;
                MeshPayload payload = Mesher.Build(world, Vector2I.Zero);
                try
                {
                    string mode = greedy ? "greedy" : "per-face";
                    TestAssert.Equal(8, CountTrianglesInCell(payload.Opaque.Vertex, 2, 100, 2),
                            $"{mode} meshing routes double-sided cross-plants to the opaque cutout surface");
                    TestAssert.Equal(10, CountTrianglesInCell(payload.Opaque.Vertex, 4, 100, 2),
                            $"{mode} meshing keeps the leaf cutout surface but removes its coincident face against stone");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Opaque.Vertex, 5, 100, 2),
                            $"{mode} meshing retains the full opaque backing block behind foliage holes");
                    TestAssert.Equal(2, CountTrianglesOnPlane(payload.Opaque.Vertex,
                                    axis: 0, coordinate: 5.0f, y: 100, z: 2),
                            $"{mode} emits the opaque backing face without a coincident foliage back face");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Glass.Vertex, 7, 100, 2),
                            $"{mode} meshing routes glass faces to the blended glass surface");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Water.Vertex, 9, 100, 2),
                            $"{mode} meshing routes water faces to the blended water surface");
                    TestAssert.Equal(12, CountTrianglesInCell(payload.Opaque.Vertex, 11, 100, 2),
                            $"{mode} meshing routes ladder texels to the opaque cutout surface");
                    TestAssert.Equal(0, CountTrianglesInCell(payload.Glass.Vertex, 11, 100, 2),
                            $"{mode} meshing does not route ladder pixels through blended glass");
                    TestAssert.Equal(0, CountTrianglesInCell(payload.Opaque.Vertex, 7, 100, 2),
                            $"{mode} meshing does not duplicate glass into the opaque surface");
                    TestAssert.Equal(0, CountTrianglesInCell(payload.Opaque.Vertex, 9, 100, 2),
                            $"{mode} meshing does not duplicate water into the opaque surface");

                    int crossTriangles = 0;
                    int invertedCrossNormals = 0;
                    for (int i = 0; i + 2 < payload.Opaque.Vertex.Count; i += 3)
                    {
                        Vector3 a = payload.Opaque.Vertex[i];
                        Vector3 b = payload.Opaque.Vertex[i + 1];
                        Vector3 c = payload.Opaque.Vertex[i + 2];
                        if (a.X < 2.0f || a.X > 3.0f || a.Y < 100.0f || a.Y > 101.0f
                                || a.Z < 2.0f || a.Z > 3.0f)
                            continue;
                        Vector3 geometric = (b - a).Cross(c - a);
                        if (geometric.LengthSquared() <= 0.000001f) continue;
                        crossTriangles++;
                        if (geometric.Dot(payload.Opaque.Normal[i]) >= 0.0f)
                            invertedCrossNormals++;
                    }
                    TestAssert.Equal(8, crossTriangles,
                            $"{mode} cross-plants keep both diagonal planes and both windings");
                    TestAssert.Equal(0, invertedCrossNormals,
                            $"{mode} cross-plant shading normals oppose each triangle's clockwise front winding");

                    // Rebuild after deliberately flipping the global setting
                    // between the greedy pre-pass and the per-face stage. The
                    // result must remain identical to the captured mode, while
                    // its old revision must be rejected at payload application.
                    MeshPayload interleaved = Mesher.BuildWithAfterGreedyPrepass(
                            world, Vector2I.Zero, () => Mesher.Greedy = !greedy);
                    try
                    {
                        TestAssert.Check(!Mesher.IsConfigurationCurrent(interleaved.MeshingRevision),
                                $"{mode} payload captured before a mid-build setting change is stale");
                        TestAssert.Check(SamePayloadGeometry(payload, interleaved),
                                $"{mode} build uses one immutable greedy configuration through payload creation");
                    }
                    finally
                    {
                        PayloadPool.Release(interleaved);
                        Mesher.Greedy = greedy;
                    }
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

    private static bool SamePayloadGeometry(MeshPayload left, MeshPayload right) =>
            SameItems(left.Opaque.Vertex, right.Opaque.Vertex)
            && SameItems(left.Opaque.Normal, right.Opaque.Normal)
            && SameItems(left.Opaque.Color, right.Opaque.Color)
            && SameItems(left.Opaque.Uv, right.Opaque.Uv)
            && SameItems(left.Opaque.Uv2, right.Opaque.Uv2)
            && SameItems(left.Glass.Vertex, right.Glass.Vertex)
            && SameItems(left.Glass.Normal, right.Glass.Normal)
            && SameItems(left.Glass.Uv, right.Glass.Uv)
            && SameItems(left.Water.Vertex, right.Water.Vertex)
            && SameItems(left.Lava.Vertex, right.Lava.Vertex)
            && SameItems(left.Collision, right.Collision);

    private static bool SameItems<T>(
            System.Collections.Generic.IReadOnlyList<T> left,
            System.Collections.Generic.IReadOnlyList<T> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
        {
            if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }
        return true;
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

    private static int CountTrianglesOnPlane(
            System.Collections.Generic.IReadOnlyList<Vector3> vertices,
            int axis, float coordinate, int y, int z)
    {
        int count = 0;
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            bool onPlane = true;
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 point = vertices[i + vertex];
                float axisValue = point[axis];
                onPlane &= Mathf.IsEqualApprox(axisValue, coordinate)
                        && point.Y >= y && point.Y <= y + 1.0f
                        && point.Z >= z && point.Z <= z + 1.0f;
            }
            if (onPlane) count++;
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
