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

    // M6 base building. Append-only: these ids persist in saves.
    Torch = 29,
    Chest = 30,
    Furnace = 31,
    Farmland = 32,
    Wheat0 = 33,
    Wheat1 = 34,
    Wheat2 = 35,
    Bed = 36,
    Ladder = 37,

    // Flowing water levels. Persisted IDs are append-only.
    FlowingWater1 = 38,
    FlowingWater2 = 39,
    FlowingWater3 = 40,
    FlowingWater4 = 41,
    FlowingWater5 = 42,
    FlowingWater6 = 43,
    FlowingWater7 = 44,

    // M7 equipment progression (appended after the flowing-water range).
    EnchantingTable = 45,

    // Advanced voxel building (append-only persisted IDs).
    PlanksSlab = 46,
    CobblestoneSlab = 47,
    BrickSlab = 48,
    PlanksStairs = 49,
    CobblestoneStairs = 50,
    BrickStairs = 51,

    // Generated structure weathering (append-only persisted IDs).
    MossyCobblestone = 52,
    WeatheredCobblestone = 53,
    CrackedBrick = 54,
    MossyBrick = 55,

    // Biome-specific canopy foliage, generated from V10 onward (append-only).
    SpruceLeaves = 56,
    JungleLeaves = 57,
    SwampLeaves = 58,

    // V11 shoreline vegetation (append-only persisted ID).
    RiverReeds = 59,

    // Original procedural volcanic ash surface (append-only persisted ID).
    Ash = 60,
}
