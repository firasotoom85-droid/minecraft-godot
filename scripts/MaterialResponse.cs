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
        0 or 1 or 2 or 4 or 16 or 17 or 18 or 36 or 37 => SurfaceFamily.Soil,
        5 or 6 or 9 or 21 or 22 or 26 or 27 or 33 or 34 or 42 or 43 or 46 => SurfaceFamily.Wood,
        7 or 23 or 24 or 25 or 28 or 30 or 31 or 32 or 38 or 39 or 40 or 41 => SurfaceFamily.Foliage,
        12 or 13 or 19 or 20 => SurfaceFamily.Ore,
        8 or 14 => SurfaceFamily.Glass,
        3 or 10 or 11 or 15 or 35 or 44 or 45 or 47 or 48 => SurfaceFamily.Stone,
        _ => SurfaceFamily.Stone,
    };

    public static SurfaceFamily ForItem(ItemId id) => id switch
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
