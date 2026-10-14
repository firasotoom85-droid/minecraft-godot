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
}

public enum ToolKind : byte
{
    None = 0,
    Pick,
    Axe,
    Shovel,
}

/// <summary>One stack of items. Durability only matters for tools (Count == 1).</summary>
public readonly record struct ItemStack(ItemId Id, int Count, int Durability)
{
    public static readonly ItemStack Empty = new(ItemId.None, 0, 0);

    public bool IsEmpty => Id == ItemId.None || Count <= 0;
}
