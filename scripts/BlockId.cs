namespace Minecraft;

/// <summary>
/// Block type ids. Values match the persisted save format and the byte layout
/// of chunk data arrays, so the enum is the single source of truth for ids.
/// </summary>
public enum BlockId : byte
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Stone = 3,
    Sand = 4,
    Log = 5,
    Leaves = 6,
    Water = 7,
    Planks = 8,
    Cobblestone = 9,
    Brick = 10,
    CoalOre = 11,
    IronOre = 12,
    Glass = 13,
    Bedrock = 14,
    SnowGrass = 15,
    Snow = 16,
    Gravel = 17,
    GoldOre = 18,
    DiamondOre = 19,
    BirchLog = 20,
    BirchLeaves = 21,
    Cactus = 22,
    CraftingTable = 23,
    Wool = 24,

    // M3 world richness.
    Lava = 25,
    TallGrass = 26,
    FlowerRed = 27,
    FlowerYellow = 28,
}
