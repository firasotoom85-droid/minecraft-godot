using System;
using System.Diagnostics;
using Godot;

namespace Minecraft;

/// <summary>Shared epoch/readiness predicate for authoritative terrain collision.</summary>
internal static class ChunkCollisionReadiness
{
    public static bool IsReady(long appliedEditEpoch, long worldEditEpoch,
            bool collisionPayloadApplied) =>
            collisionPayloadApplied && appliedEditEpoch == worldEditEpoch;
}

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
    /// <summary>World edit epoch represented by the currently applied mesh/collider payload.</summary>
    public long AppliedEditEpoch { get; private set; }
    /// <summary>
    /// True when the current edit-epoch payload was applied with collision
    /// building enabled. A payload with zero collision vertices is still a
    /// completed collision snapshot; it does not require a CollisionShape3D.
    /// </summary>
    public bool CollisionPayloadApplied { get; private set; }

    private MeshInstance3D _meshOpaque = null!;
    private MeshInstance3D? _meshGlass;
    private MeshInstance3D? _meshWater;
    private MeshInstance3D? _meshLava;
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
    }

    private CollisionShape3D CreateCollisionNode()
    {
        CollisionShape3D collision = new()
        {
            Name = "Collision",
        };
        AddChild(collision);
        return collision;
    }

    private void ApplyOptionalSurface(
            ref MeshInstance3D? mesh, SurfaceData surface, Material material,
            string nodeName, bool sortByBounds)
    {
        if (surface.Vertex.Count == 0)
        {
            if (mesh != null) mesh.Mesh = null;
            return;
        }

        mesh ??= NewSurfaceMesh(nodeName, noShadow: true);
        if (sortByBounds)
            mesh.SortingUseAabbCenter = true;
        ApplySurface(mesh, surface, material);
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
        AppliedEditEpoch = payload.Epoch;
        EstimatedMeshBytes = payload.EstimatedResourceBytes;
        ApplySurface(_meshOpaque, payload.Opaque, ChunkMaterials.Opaque);
        ApplyOptionalSurface(ref _meshGlass, payload.Glass, ChunkMaterials.Glass,
                "GlassMesh", sortByBounds: true);
        ApplyOptionalSurface(ref _meshWater, payload.Water, ChunkMaterials.Water,
                "WaterMesh", sortByBounds: true);
        ApplyOptionalSurface(ref _meshLava, payload.Lava, ChunkMaterials.Lava,
                "LavaMesh", sortByBounds: false);

        // Collision resources are optional too: most render-only chunks and
        // many empty preview chunks never need a physics child at all.
        CollisionPayloadApplied = World?.BuildCollision == true;
        if (CollisionPayloadApplied)
        {
            long collisionStart = Stopwatch.GetTimestamp();
            int collisionVertices = payload.Collision.Count;
            if (collisionVertices == 0)
            {
                if (_collision != null)
                    _collision.Shape = null;
            }
            else
            {
                _collision ??= CreateCollisionNode();
                Vector3[] faces = payload.Collision.ToArray();
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
        Tween tween = CreateTween();
        tween.SetParallel(true);
        FadeMesh(tween, _meshOpaque);
        FadeMesh(tween, _meshGlass);
        FadeMesh(tween, _meshWater);
        FadeMesh(tween, _meshLava);
    }

    private static void FadeMesh(Tween tween, MeshInstance3D? mesh)
    {
        if (mesh == null) return;
        mesh.Transparency = 1.0f;
        tween.TweenProperty(mesh, "transparency", 0.0f, 0.4f);
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
    private static bool _lowQualityShaderVariantEnabled;
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

    private static ShaderMaterial ApplyQualityVariant(ShaderMaterial material)
    {
        BlockDb.SetLowQualityShaderVariant(material, _lowQualityShaderVariantEnabled);
        return material;
    }

    private static ShaderMaterial ApplyPaleSurfaceContrast(ShaderMaterial material)
    {
        MaterialResponse.ApplyPaleSurfaceContrast(material, _paleSurfaceContrastStrength);
        return material;
    }

    // Pale-surface contrast is a uniform-level response, so it composes with
    // the LOW-quality shader variant instead of replacing it.
    private static ShaderMaterial CreateOpaqueMaterial() => ApplyQualityVariant(
            ApplyPaleSurfaceContrast(ApplyLighting(BlockDb.MakeOpaqueMaterial())));
    private static ShaderMaterial CreateArrayOpaqueMaterial() => ApplyQualityVariant(
            ApplyPaleSurfaceContrast(ApplyLighting(BlockDb.MakeArrayOpaqueMaterial())));
    private static ShaderMaterial CreateGlassMaterial() =>
            ApplyQualityVariant(ApplyLighting(BlockDb.MakeGlassMaterial()));
    private static ShaderMaterial CreateLavaMaterial() =>
            ApplyQualityVariant(ApplyLighting(BlockDb.MakeLavaMaterial()));

    private static Material CreateWaterMaterial()
    {
        Material material = BlockDb.MakeWaterMaterial();
        if (material is ShaderMaterial shader)
            ApplyQualityVariant(ApplyLighting(shader));
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
    /// Switches the compile-time shader variant on existing shared materials.
    /// Meshes and chunk resources remain intact when a preset changes.
    /// </summary>
    public static void SetLowQualityShaderVariant(bool enabled)
    {
        if (_lowQualityShaderVariantEnabled == enabled)
            return;

        _lowQualityShaderVariantEnabled = enabled;
        if (OpaqueLazy.IsValueCreated)
            BlockDb.SetLowQualityShaderVariant(OpaqueLazy.Value, enabled);
        if (OpaqueArrayLazy.IsValueCreated)
            BlockDb.SetLowQualityShaderVariant(OpaqueArrayLazy.Value, enabled);
        if (GlassLazy.IsValueCreated)
            BlockDb.SetLowQualityShaderVariant(GlassLazy.Value, enabled);
        if (WaterLazy.IsValueCreated && WaterLazy.Value is ShaderMaterial water)
            BlockDb.SetLowQualityShaderVariant(water, enabled);
        if (LavaLazy.IsValueCreated)
            BlockDb.SetLowQualityShaderVariant(LavaLazy.Value, enabled);
    }

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
