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
    // Most chunks contain only default shape states; keep this empty until a
    // non-default orientation/half is actually stored in the chunk.
    public byte[] BlockStates { get; private set; } = System.Array.Empty<byte>();

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
        // Transparent chunk meshes are positioned at the chunk origin. Sort
        // glass using its actual bounds center so adjacent chunks do not sort
        // by an arbitrary corner when viewed through windows.
        _meshGlass.SortingUseAabbCenter = true;
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
        BlockStates = World.ChunkBlockStates(ChunkPos);
    }

    public bool SetBlockLocal(int x, int y, int z, BlockId id) => SetBlockLocal(x, y, z, id, 0);

    public bool SetBlockLocal(int x, int y, int z, BlockId id, byte state)
    {
        if (x < 0 || x >= SX || y < 0 || y >= World!.Height || z < 0 || z >= SZ)
            return false;

        int i = Index(x, y, z);
        state = BlockShapes.NormalizeState(id, state);
        byte oldState = BlockStates.Length == 0 ? (byte)0 : BlockStates[i];
        if ((BlockId)Blocks[i] == id && oldState == state)
            return false;

        Blocks[i] = (byte)id;
        if (state != 0 && BlockStates.Length == 0)
            BlockStates = new byte[Blocks.Length];
        if (BlockStates.Length > 0)
            BlockStates[i] = state;
        return true;
    }

    public void ReleaseBlockStates() => BlockStates = System.Array.Empty<byte>();

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
    private static float _skyEnergy = 1.0f;

    private static readonly Lazy<ShaderMaterial> OpaqueLazy = new(CreateOpaqueMaterial);
    private static readonly Lazy<ShaderMaterial> OpaqueArrayLazy = new(CreateArrayOpaqueMaterial);
    private static readonly Lazy<ShaderMaterial> GlassLazy = new(CreateGlassMaterial);
    private static readonly Lazy<Material> WaterLazy = new(CreateWaterMaterial);
    private static readonly Lazy<ShaderMaterial> LavaLazy = new(CreateLavaMaterial);

    private static ShaderMaterial ApplySky(ShaderMaterial material)
    {
        material.SetShaderParameter("sky_light_energy", _skyEnergy);
        return material;
    }

    private static ShaderMaterial CreateOpaqueMaterial() => ApplySky(BlockDb.MakeOpaqueMaterial());
    private static ShaderMaterial CreateArrayOpaqueMaterial() => ApplySky(BlockDb.MakeArrayOpaqueMaterial());
    private static ShaderMaterial CreateGlassMaterial() => ApplySky(BlockDb.MakeGlassMaterial());
    private static ShaderMaterial CreateLavaMaterial() => ApplySky(BlockDb.MakeLavaMaterial());

    private static Material CreateWaterMaterial()
    {
        Material material = BlockDb.MakeWaterMaterial();
        if (material is ShaderMaterial shader)
            shader.SetShaderParameter("sky_light_energy", _skyEnergy);
        return material;
    }

    /// <summary>Opaque terrain material for the active meshing mode.</summary>
    public static Material Opaque => Mesher.Greedy ? OpaqueArrayLazy.Value : OpaqueLazy.Value;

    public static ShaderMaterial Glass => GlassLazy.Value;

    public static Material Water => WaterLazy.Value;

    public static ShaderMaterial Lava => LavaLazy.Value;

    /// <summary>Adjusts baked skylight response without rebuilding chunk meshes.</summary>
    public static void SetSkyEnergy(float energy)
    {
        _skyEnergy = Mathf.Clamp(energy, 0.0f, 1.25f);
        if (OpaqueLazy.IsValueCreated) OpaqueLazy.Value.SetShaderParameter("sky_light_energy", _skyEnergy);
        if (OpaqueArrayLazy.IsValueCreated) OpaqueArrayLazy.Value.SetShaderParameter("sky_light_energy", _skyEnergy);
        if (GlassLazy.IsValueCreated) GlassLazy.Value.SetShaderParameter("sky_light_energy", _skyEnergy);
        if (WaterLazy.IsValueCreated && WaterLazy.Value is ShaderMaterial water)
            water.SetShaderParameter("sky_light_energy", _skyEnergy);
        if (LavaLazy.IsValueCreated) LavaLazy.Value.SetShaderParameter("sky_light_energy", _skyEnergy);
    }
}
