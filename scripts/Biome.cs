namespace Minecraft;

/// <summary>Versioned terrain biomes. Existing numeric values remain stable.</summary>
public enum Biome
{
    Plains = 0,
    Desert = 1,
    Snow = 2,
    Birch = 3,

    // WorldGen V7 climate and elevation regions. Append-only.
    DenseForest = 4,
    Taiga = 5,
    Jungle = 6,
    Swamp = 7,
    Ocean = 8,
    Beach = 9,
    Mountain = 10,
    FlowerPlains = 11,
    Meadow = 12,
}
