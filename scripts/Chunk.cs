using System;
using Godot;

namespace Minecraft;

/// <summary>
/// A built chunk node: block data reference, three render surfaces
/// (opaque terrain / alpha-blended glass / animated water) and one trimesh
/// collider. All meshing lives in Mesher; this applies the payload on
/// the main thread.
/// </summary>
public partial class Chunk : StaticBody3D
{
    public const int SX = Mesher.SX;
    public const int SZ = Mesher.SZ;

    public VoxelWorld? World { get; private set; }
    public Vector2I ChunkPos { get; private set; } = Vector2I.Zero;
    public byte[] Blocks { get; private set; } = [];

    private MeshInstance3D _meshOpaque = null!;
    private MeshInstance3D _meshGlass = null!;
    private MeshInstance3D _meshWater = null!;
    private MeshInstance3D _meshLava = null!;
    private CollisionShape3D _collision = null!;

    public void Setup(VoxelWorld world, Vector2I chunkPos)
    {
        World = world;
        ChunkPos = chunkPos;
        Name = $"chunk_{chunkPos.X}_{chunkPos.Y}";
        Position = new Vector3(chunkPos.X * SX, 0, chunkPos.Y * SZ);
        CollisionLayer = (uint)PhysicsLayer.Terrain;
        CollisionMask = 0;

        _meshOpaque = NewSurfaceMesh("OpaqueMesh", false);
        _meshGlass = NewSurfaceMesh("GlassMesh", true);
        _meshWater = NewSurfaceMesh("WaterMesh", true);
        _meshLava = NewSurfaceMesh("LavaMesh", true);

        _collision = new CollisionShape3D();
        _collision.Name = "Collision";
        AddChild(_collision);
    }

    private MeshInstance3D NewSurfaceMesh(string nodeName, bool noShadow)
    {
        MeshInstance3D mi = new()
        {
            Name = nodeName,
        };
        if (noShadow)
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        AddChild(mi);
        return mi;
    }

    public static int Index(int x, int y, int z) => x + z * SX + y * SX * SZ;

    public void Generate()
    {
        Blocks = World!.ChunkData(ChunkPos);
    }

    public bool SetBlockLocal(int x, int y, int z, BlockId id)
    {
        if (x < 0 || x >= SX || y < 0 || y >= World!.Height || z < 0 || z >= SZ)
        {
            return false;
        }

        int i = Index(x, y, z);
        if ((BlockId)Blocks[i] == id)
        {
            return false;
        }

        Blocks[i] = (byte)id;
        return true;
    }

    /// <summary>Apply a Mesher.Build() payload: meshes, materials, collider.</summary>
    public void ApplyPayload(MeshPayload payload)
    {
        ApplySurface(_meshOpaque, payload.Opaque, ChunkMaterials.Opaque);
        ApplySurface(_meshGlass, payload.Glass, ChunkMaterials.Glass);
        ApplySurface(_meshWater, payload.Water, ChunkMaterials.Water);
        ApplySurface(_meshLava, payload.Lava, ChunkMaterials.Lava);
        Vector3[] faces = payload.Collision.ToArray();
        if (faces.Length == 0)
        {
            _collision.Shape = null;
        }
        else
        {
            ConcavePolygonShape3D shape = new();
            shape.SetFaces(faces);
            _collision.Shape = shape;
        }
    }

    /// <summary>Fade a freshly streamed chunk in so it does not pop into view.</summary>
    public void FadeIn()
    {
        _meshOpaque.Transparency = 1.0f;
        _meshGlass.Transparency = 1.0f;
        _meshWater.Transparency = 1.0f;
        _meshLava.Transparency = 1.0f;
        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_meshOpaque, "transparency", 0.0f, 0.4f);
        tween.TweenProperty(_meshGlass, "transparency", 0.0f, 0.4f);
        tween.TweenProperty(_meshWater, "transparency", 0.0f, 0.4f);
        tween.TweenProperty(_meshLava, "transparency", 0.0f, 0.4f);
    }

    private static void ApplySurface(MeshInstance3D mi, SurfaceData surf, Material mat)
    {
        if (surf.Vertex.Count == 0)
        {
            mi.Mesh = null;
            return;
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = surf.Vertex.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = surf.Normal.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = surf.Color.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = surf.Uv.ToArray();
        if (surf.Uv2.Count > 0)
        {
            arrays[(int)Mesh.ArrayType.TexUV2] = surf.Uv2.ToArray();
        }

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mi.Mesh = mesh;
        mi.MaterialOverride = mat;
    }
}

/// <summary>
/// Shared chunk materials. Lazy&lt;T&gt; guarantees the atlas and shader are
/// built exactly once on first use, from any thread, without a hand-written
/// null check — the runtime handles the memoization.
/// </summary>
public static class ChunkMaterials
{
    private static readonly Lazy<StandardMaterial3D> OpaqueLazy = new(BlockDb.MakeOpaqueMaterial);
    private static readonly Lazy<ShaderMaterial> OpaqueArrayLazy = new(BlockDb.MakeArrayOpaqueMaterial);
    private static readonly Lazy<StandardMaterial3D> GlassLazy = new(BlockDb.MakeGlassMaterial);
    private static readonly Lazy<Material> WaterLazy = new(BlockDb.MakeWaterMaterial);
    private static readonly Lazy<StandardMaterial3D> LavaLazy = new(BlockDb.MakeLavaMaterial);

    /// <summary>Opaque terrain material for the active meshing mode: the atlas
    /// standard material for per-face meshes, the texture-array shader for
    /// greedy (merged, UV-tiled) meshes.</summary>
    public static Material Opaque => Mesher.Greedy ? OpaqueArrayLazy.Value : OpaqueLazy.Value;

    public static StandardMaterial3D Glass => GlassLazy.Value;

    public static Material Water => WaterLazy.Value;

    public static StandardMaterial3D Lava => LavaLazy.Value;
}
