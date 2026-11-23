using System;
using System.Diagnostics;
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

    /// <summary>Estimated CPU-side mesh/collision array bytes represented by the applied payload.</summary>
    public long EstimatedMeshBytes { get; private set; }

    private MeshInstance3D _meshOpaque = null!;
    private MeshInstance3D _meshGlass = null!;
    private MeshInstance3D _meshWater = null!;
    private MeshInstance3D _meshLava = null!;
    private CollisionShape3D? _collision;

    public void Setup(VoxelWorld world, Vector2I chunkPos)
    {
        World = world;
        ChunkPos = chunkPos;
        Name = $"chunk_{chunkPos.X}_{chunkPos.Y}";
        Position = new Vector3(chunkPos.X * SX, 0, chunkPos.Y * SZ);
        CollisionLayer = world.BuildCollision ? (uint)PhysicsLayer.Terrain : 0;
        CollisionMask = 0;

        _meshOpaque = NewSurfaceMesh("OpaqueMesh", false);
        _meshGlass = NewSurfaceMesh("GlassMesh", true);
        // Alpha-blended chunk meshes are positioned at the chunk origin. Sort
        // glass and water using their actual bounds centers so adjacent chunks
        // do not sort by an arbitrary corner when viewed through either surface.
        _meshGlass.SortingUseAabbCenter = true;
        _meshWater = NewSurfaceMesh("WaterMesh", true);
        _meshWater.SortingUseAabbCenter = true;
        _meshLava = NewSurfaceMesh("LavaMesh", true);

        if (world.BuildCollision)
        {
            _collision = new CollisionShape3D
            {
                Name = "Collision",
            };
            AddChild(_collision);
        }
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
        EstimatedMeshBytes = payload.EstimatedResourceBytes;
        ApplySurface(_meshOpaque, payload.Opaque, ChunkMaterials.Opaque);
        ApplySurface(_meshGlass, payload.Glass, ChunkMaterials.Glass);
        ApplySurface(_meshWater, payload.Water, ChunkMaterials.Water);
        ApplySurface(_meshLava, payload.Lava, ChunkMaterials.Lava);
        // Render-only chunks (the menu preview) do not need collision arrays,
        // concave shapes or physics-server resources.
        if (_collision != null)
        {
            long collisionStart = Stopwatch.GetTimestamp();
            int collisionVertices = payload.Collision.Count;
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

            Profiler.RecordCollision(Stopwatch.GetElapsedTime(collisionStart).TotalMilliseconds,
                    collisionVertices);
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
    private static Vector3 _sunDirection = Vector3.Up;
    private static Color _sunColor = new(1.0f, 0.96f, 0.89f);
    private static Color _skyLightColor = new(0.88f, 0.94f, 1.0f);
    private static float _sunlightEnergy = 1.0f;
    private static Vector3 _moonDirection = Vector3.Down;
    private static Color _moonColor = new(0.62f, 0.74f, 1.0f);
    private static float _moonlightEnergy;
    private static float _paleSurfaceContrastStrength = MaterialResponse.PaleSurfaceDirectionalContrast;

    private static readonly Lazy<ShaderMaterial> OpaqueLazy = new(CreateOpaqueMaterial);
    private static readonly Lazy<ShaderMaterial> OpaqueArrayLazy = new(CreateArrayOpaqueMaterial);
    private static readonly Lazy<ShaderMaterial> GlassLazy = new(CreateGlassMaterial);
    private static readonly Lazy<Material> WaterLazy = new(CreateWaterMaterial);
    private static readonly Lazy<ShaderMaterial> LavaLazy = new(CreateLavaMaterial);

    private static ShaderMaterial ApplyLighting(ShaderMaterial material)
    {
        material.SetShaderParameter("sky_light_energy", _skyEnergy);
        material.SetShaderParameter("sun_direction", _sunDirection);
        material.SetShaderParameter("sun_color",
                new Vector3(_sunColor.R, _sunColor.G, _sunColor.B));
        material.SetShaderParameter("sky_light_color",
                new Vector3(_skyLightColor.R, _skyLightColor.G, _skyLightColor.B));
        material.SetShaderParameter("sunlight_energy", _sunlightEnergy);
        material.SetShaderParameter("moon_direction", _moonDirection);
        material.SetShaderParameter("moon_color",
                new Vector3(_moonColor.R, _moonColor.G, _moonColor.B));
        material.SetShaderParameter("moonlight_energy", _moonlightEnergy);
        return material;
    }

    private static ShaderMaterial CreateOpaqueMaterial() =>
            ApplyPaleSurfaceContrast(ApplyLighting(BlockDb.MakeOpaqueMaterial()));
    private static ShaderMaterial CreateArrayOpaqueMaterial() =>
            ApplyPaleSurfaceContrast(ApplyLighting(BlockDb.MakeArrayOpaqueMaterial()));

    private static ShaderMaterial ApplyPaleSurfaceContrast(ShaderMaterial material)
    {
        MaterialResponse.ApplyPaleSurfaceContrast(material, _paleSurfaceContrastStrength);
        return material;
    }
    private static ShaderMaterial CreateGlassMaterial() => ApplyLighting(BlockDb.MakeGlassMaterial());
    private static ShaderMaterial CreateLavaMaterial() => ApplyLighting(BlockDb.MakeLavaMaterial());

    private static Material CreateWaterMaterial()
    {
        Material material = BlockDb.MakeWaterMaterial();
        if (material is ShaderMaterial shader)
            ApplyLighting(shader);
        return material;
    }

    /// <summary>Opaque terrain material for the active meshing mode.</summary>
    public static Material Opaque => Mesher.Greedy ? OpaqueArrayLazy.Value : OpaqueLazy.Value;

    /// <summary>Current opaque contrast strength, used by deterministic visual captures.</summary>
    public static float PaleSurfaceContrastStrength => _paleSurfaceContrastStrength;

    /// <summary>Changes only opaque presentation; packed light and vertices are untouched.</summary>
    public static void SetPaleSurfaceContrast(float strength)
    {
        _paleSurfaceContrastStrength = Mathf.Clamp(
                strength, 0.0f, MaterialResponse.PaleSurfaceDirectionalContrast);
        if (OpaqueLazy.IsValueCreated) ApplyPaleSurfaceContrast(OpaqueLazy.Value);
        if (OpaqueArrayLazy.IsValueCreated) ApplyPaleSurfaceContrast(OpaqueArrayLazy.Value);
    }

    public static ShaderMaterial Glass => GlassLazy.Value;

    public static Material Water => WaterLazy.Value;

    public static ShaderMaterial Lava => LavaLazy.Value;

    /// <summary>
    /// Updates the shared baked-sky response for every renderer path. Existing
    /// chunk meshes keep their packed light and vertex layout; shared sun/moon
    /// uniforms change as the sky moves through the day/night cycle.
    /// </summary>
    public static void SetSkyLighting(
            float skyEnergy, Vector3 sunDirection, Color sunColor,
            Color skyLightColor, float sunlightEnergy,
            Vector3 moonDirection, Color moonColor, float moonlightEnergy)
    {
        _skyEnergy = Mathf.Clamp(skyEnergy, 0.0f, 1.25f);
        _sunDirection = sunDirection.LengthSquared() > 0.0001f
                ? sunDirection.Normalized() : Vector3.Up;
        _sunColor = sunColor;
        _skyLightColor = skyLightColor;
        _sunlightEnergy = Mathf.Clamp(sunlightEnergy, 0.0f, 1.25f);
        _moonDirection = moonDirection.LengthSquared() > 0.0001f
                ? moonDirection.Normalized() : Vector3.Down;
        _moonColor = moonColor;
        _moonlightEnergy = Mathf.Clamp(moonlightEnergy, 0.0f, 0.35f);

        if (OpaqueLazy.IsValueCreated) ApplyLighting(OpaqueLazy.Value);
        if (OpaqueArrayLazy.IsValueCreated) ApplyLighting(OpaqueArrayLazy.Value);
        if (GlassLazy.IsValueCreated) ApplyLighting(GlassLazy.Value);
        if (WaterLazy.IsValueCreated && WaterLazy.Value is ShaderMaterial water)
            ApplyLighting(water);
        if (LavaLazy.IsValueCreated) ApplyLighting(LavaLazy.Value);
    }

    /// <summary>Updates solar response while retaining the current shared moon response.</summary>
    public static void SetSkyLighting(
            float skyEnergy, Vector3 sunDirection, Color sunColor,
            Color skyLightColor, float sunlightEnergy) =>
            SetSkyLighting(skyEnergy, sunDirection, sunColor, skyLightColor, sunlightEnergy,
                    _moonDirection, _moonColor, _moonlightEnergy);

    /// <summary>Compatibility helper for callers that only need to scale baked skylight.</summary>
    public static void SetSkyEnergy(float energy) =>
            SetSkyLighting(energy, _sunDirection, _sunColor, _skyLightColor, _sunlightEnergy,
                    _moonDirection, _moonColor, _moonlightEnergy);
}
