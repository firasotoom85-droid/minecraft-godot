using Godot;

namespace Minecraft;

/// <summary>
/// Shared surface-response palette. Values are compact stable vertex tags,
/// not persisted content IDs; changing this table never affects world saves.
/// </summary>
public enum SurfaceFamily : byte
{
    Stone = 0,
    Wood = 1,
    Soil = 2,
    Ore = 3,
    Foliage = 4,
    Glass = 5,
    Metal = 6,
}

/// <summary>One physically-inspired response definition for world and held items.</summary>
public readonly record struct SurfaceResponse(
    float Roughness, float Metallic, float Specular);

public static class MaterialResponse
{
    // Presentation-only response for high-albedo stone/soil pixels. These
    // constants shape directional sky contribution without changing packed
    // voxel light or any vertex colour channel.
    public const float PaleSurfaceLuminanceMin = 0.36f;
    public const float PaleSurfaceLuminanceMax = 0.72f;
    public const float PaleSurfaceDirectionalContrast = 0.18f;

    private static readonly SurfaceResponse[] Responses =
    [
        new(0.88f, 0.02f, 0.22f), // stone: dry, subdued
        new(0.82f, 0.00f, 0.20f), // wood: matte grain
        new(0.96f, 0.00f, 0.16f), // soil: diffuse
        new(0.72f, 0.08f, 0.28f), // ore: slightly denser mineral
        new(0.92f, 0.00f, 0.18f), // foliage: matte
        new(0.16f, 0.04f, 0.42f), // glass: smooth, low-metal
        new(0.34f, 0.72f, 0.48f), // metal: polished but not mirror-like
    ];

    public static SurfaceResponse Get(SurfaceFamily family) => Responses[(int)family];

    // COLOR.a is the compact material-family channel. The unused range 8..14
    // flags damp faces without adding a vertex attribute.
    public static float VertexTag(SurfaceFamily family, bool wet) =>
            (byte)family + (wet ? 8.0f : 0.0f);

    public static Color WetTint(Color color) =>
            new(color.R * 0.80f, color.G * 0.86f, color.B * 0.88f, color.A);

    /// <summary>Soft albedo mask for bright Stone and Soil pixels.</summary>
    public static float PaleSurfaceWeight(Color albedo, SurfaceFamily family)
    {
        if (family is not (SurfaceFamily.Stone or SurfaceFamily.Soil))
            return 0.0f;
        float luminance = albedo.R * 0.2126f + albedo.G * 0.7152f + albedo.B * 0.0722f;
        float range = PaleSurfaceLuminanceMax - PaleSurfaceLuminanceMin;
        float t = Mathf.Clamp((luminance - PaleSurfaceLuminanceMin) / range, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    /// <summary>Reference directional gain shared by the opaque shader tests.</summary>
    public static float DirectionalSkyGain(float directSun, float sunlightEnergy,
            float paleSurfaceWeight, float contrastStrength = PaleSurfaceDirectionalContrast)
    {
        float direct = Mathf.Clamp(directSun, 0.0f, 1.0f);
        float sun = Mathf.Clamp(sunlightEnergy, 0.0f, 1.0f);
        float pale = Mathf.Clamp(paleSurfaceWeight, 0.0f, 1.0f);
        float strength = Mathf.Clamp(contrastStrength, 0.0f, PaleSurfaceDirectionalContrast);
        return 0.88f + direct * 0.12f - pale * strength * sun * (1.0f - direct);
    }

    /// <summary>Configure the pale-surface uniforms on an opaque material.</summary>
    public static void ApplyPaleSurfaceContrast(ShaderMaterial material, float strength)
    {
        material.SetShaderParameter("pale_surface_luminance_range",
                new Vector2(PaleSurfaceLuminanceMin, PaleSurfaceLuminanceMax));
        material.SetShaderParameter("pale_surface_contrast_strength",
                Mathf.Clamp(strength, 0.0f, PaleSurfaceDirectionalContrast));
    }

    private static Vector3 ToVector3(SurfaceResponse response) =>
        new(response.Roughness, response.Metallic, response.Specular);

    /// <summary>Push the canonical C# response table into a world shader.</summary>
    public static void ApplyTo(ShaderMaterial material)
    {
        material.SetShaderParameter("response_stone", ToVector3(Get(SurfaceFamily.Stone)));
        material.SetShaderParameter("response_wood", ToVector3(Get(SurfaceFamily.Wood)));
        material.SetShaderParameter("response_soil", ToVector3(Get(SurfaceFamily.Soil)));
        material.SetShaderParameter("response_ore", ToVector3(Get(SurfaceFamily.Ore)));
        material.SetShaderParameter("response_foliage", ToVector3(Get(SurfaceFamily.Foliage)));
        material.SetShaderParameter("response_glass", ToVector3(Get(SurfaceFamily.Glass)));
        material.SetShaderParameter("response_metal", ToVector3(Get(SurfaceFamily.Metal)));
    }

    /// <summary>Classify procedural atlas tiles once during mesh construction.</summary>
    public static SurfaceFamily ForTile(int tile) => tile switch
    {
        0 or 1 or 2 or 4 or 16 or 17 or 18 or 36 or 37 or 60 => SurfaceFamily.Soil,
        5 or 6 or 9 or 21 or 22 or 26 or 27 or 33 or 34 or 42 or 43 or 46 or 49 or 50 or 51 => SurfaceFamily.Wood,
        7 or 23 or 24 or 25 or 28 or 30 or 31 or 32 or 38 or 39 or 40 or 41 or 59
            => SurfaceFamily.Foliage,
        12 or 13 or 19 or 20 => SurfaceFamily.Ore,
        8 or 14 => SurfaceFamily.Glass,
        3 or 10 or 11 or 15 or 35 or 44 or 45 or 47 or 48 or 52 or 53 or 54 or 55
            => SurfaceFamily.Stone,
        56 or 57 or 58 => SurfaceFamily.Foliage,
        _ => SurfaceFamily.Stone,
    };

    /// <summary>
    /// Placeable items inherit the response of their registered block texture.
    /// This keeps held sprites and world meshes in the same material family as
    /// content is appended, without maintaining a second block-item catalogue.
    /// </summary>
    public static SurfaceFamily ForItem(ItemId id)
    {
        if (ItemDb.BlockFor(id) is { } block)
            return ForTile(BlockDb.IconTile(block));

        return id switch
        {
            ItemId.Log or ItemId.BirchLog or ItemId.Planks or ItemId.Stick or
            ItemId.WoodPick or ItemId.WoodAxe or ItemId.WoodShovel or ItemId.WoodHoe or
            ItemId.WoodSword or ItemId.Chest or ItemId.Bed or ItemId.WoodenBoat or
            ItemId.Bow or ItemId.FishingRod or ItemId.Arrow => SurfaceFamily.Wood,
            ItemId.CoalOre or ItemId.IronOre or ItemId.GoldOre or ItemId.DiamondOre or
            ItemId.Coal or ItemId.RawIron or ItemId.RawGold or ItemId.Diamond or
            ItemId.LapisLazuli or ItemId.EmberCore => SurfaceFamily.Ore,
            ItemId.IronIngot or ItemId.GoldIngot or ItemId.IronHelmet or ItemId.IronChestplate or
            ItemId.IronLegs or ItemId.IronBoots or ItemId.DiamondHelmet or ItemId.DiamondChestplate or
            ItemId.DiamondLegs or ItemId.DiamondBoots or ItemId.EmptyBucket or ItemId.WaterBucket or
            ItemId.LavaBucket or ItemId.IronPick or ItemId.IronAxe or ItemId.IronShovel or
            ItemId.IronHoe or ItemId.IronSword or ItemId.DiamondPick or ItemId.DiamondAxe or
            ItemId.DiamondShovel or ItemId.DiamondHoe or ItemId.DiamondSword or
            ItemId.Shield or ItemId.EmberBlade => SurfaceFamily.Metal,
            ItemId.Leaves or ItemId.BirchLeaves or ItemId.TallGrass or ItemId.FlowerRed or
            ItemId.FlowerYellow or ItemId.Seeds or ItemId.Wheat or ItemId.Wool or
            ItemId.Cactus or ItemId.Apple or ItemId.Bread or ItemId.Leather or ItemId.Feather or
            ItemId.RawMutton or ItemId.CookedMutton or ItemId.RawBeef or ItemId.CookedBeef or
            ItemId.RawPork or ItemId.CookedPork or ItemId.RawChicken or ItemId.CookedChicken or
            ItemId.RawFish or ItemId.CookedFish => SurfaceFamily.Foliage,
            ItemId.Dirt or ItemId.Grass or ItemId.Sand or ItemId.Gravel or ItemId.Snow or
            ItemId.SnowGrass => SurfaceFamily.Soil,
            ItemId.Glass => SurfaceFamily.Glass,
            _ => SurfaceFamily.Stone,
        };
    }

    /// <summary>
    /// Block light fills only the portion not already supplied by skylight.
    /// This avoids double-counting independent sky and torch channels in sunlit
    /// areas while preserving full warm contribution in genuinely dark caves.
    /// </summary>
    public static float ShadowedBlockLight(float sky, float block) =>
        Mathf.Clamp(block, 0.0f, 1.0f) * (1.0f - Mathf.Clamp(sky, 0.0f, 1.0f));
}
