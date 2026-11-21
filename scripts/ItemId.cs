namespace Minecraft;

/// <summary>Item ids: placeable blocks, materials and tools. Persisted in player saves.</summary>
public enum ItemId : byte
{
    None = 0,

    // Placeable block items.
    Grass, Dirt, Stone, Sand, Gravel, Log, BirchLog, Leaves, BirchLeaves,
    SnowGrass, Snow, Glass, Brick, Cactus, Planks, CraftingTable, Cobblestone,
    CoalOre, IronOre, GoldOre, DiamondOre,

    // Materials.
    Coal, RawIron, RawGold, Diamond, Stick,

    // Tools (tier + kind).
    WoodPick, WoodAxe, WoodShovel,
    StonePick, StoneAxe, StoneShovel,
    IronPick, IronAxe, IronShovel,
    DiamondPick, DiamondAxe, DiamondShovel,

    // Food + drops (M2 survival).
    Apple, RawMutton, CookedMutton, Wool,

    // M3 world richness (appended: persisted ids never move).
    Lava, TallGrass, FlowerRed, FlowerYellow,

    // M6 base building (appended).
    Torch,
    Chest,
    Furnace,
    IronIngot,
    GoldIngot,

    // Armor (appended; slot order is helmet/chest/legs/boots).
    IronHelmet, IronChestplate, IronLegs, IronBoots,
    DiamondHelmet, DiamondChestplate, DiamondLegs, DiamondBoots,

    // M6c farming/home (appended).
    Seeds, Wheat, Bread,
    WoodHoe, StoneHoe, IronHoe, DiamondHoe,
    Bed, Ladder,

    // M7 animal drops (appended).
    RawBeef, CookedBeef, RawPork, CookedPork, RawChicken, CookedChicken,
    Leather, Feather,

    // M7 boss trophy (appended).
    EmberCore,

    // M7 fishing/trading (appended).
    FishingRod, RawFish, CookedFish,
    // Appended persisted IDs: prior item values remain unchanged.
    WoodSword, StoneSword, IronSword, DiamondSword, Bow, Arrow, Shield, EmberBlade, LapisLazuli, EnchantingTable,

    // Transportation and fluid containers (appended persisted IDs).
    EmptyBucket, WaterBucket, LavaBucket, WoodenBoat,
}

public enum ToolKind : byte
{
    None = 0,
    Pick,
    Axe,
    Shovel,
    Hoe,
}

/// <summary>One stack of items. Durability only matters for tools (Count == 1).</summary>
public readonly record struct ItemStack(ItemId Id, int Count, int Durability, int Sharpness = 0, int Protection = 0, int Unbreaking = 0)
{
    public static readonly ItemStack Empty = new(ItemId.None, 0, 0);

    public bool IsEmpty => Id == ItemId.None || Count <= 0;
}
