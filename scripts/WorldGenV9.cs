namespace Minecraft;

/// <summary>
/// Version 9 keeps V8 terrain and structure placement, then adds deterministic
/// material aging to the existing structure stamp. Hashing two-block
/// cells creates readable patches without changing template geometry or RNG
/// consumption, and the variants remain ordinary full cubes in the atlas.
/// </summary>
public static class WorldGenV9
{
    public const int Version = 9;

    /// <summary>
    /// Selects a weathered block material for eligible structure stonework.
    /// Coordinates are grouped into 2x2x2 cells so moss and wear form coherent
    /// patches instead of single-voxel noise.
    /// </summary>
    public static BlockId WeatheredMaterial(
            VoxelWorld.StructureInfo site, BlockId material, int x, int y, int z)
    {
        if (site.Kind is < 0 or > 5)
            return material;

        int relativeY = y - site.Y;
        switch (material)
        {
            case BlockId.Cobblestone:
                {
                    int mossLayer = site.Kind switch
                    {
                        3 => 5,
                        4 => 4,
                        5 => 5,
                        _ => 2,
                    };
                    int mossChance = site.Kind switch
                    {
                        0 => 7,
                        1 => 25,
                        2 => 3,
                        3 => 18,
                        4 => 38,
                        5 => 46,
                        _ => 0,
                    };
                    uint moss = CoordinateHash(site.Seed, x, y, z, 0x4D4F5353u);
                    if (relativeY >= 1 && relativeY <= mossLayer && moss % 100u < mossChance)
                        return BlockId.MossyCobblestone;

                    int weatherChance = site.Kind switch
                    {
                        0 => 34,
                        1 => 31,
                        2 => 23,
                        3 => 38,
                        4 => 24,
                        5 => 22,
                        _ => 0,
                    };
                    uint wear = CoordinateHash(site.Seed, x, y, z, 0x57454152u);
                    return wear % 100u < weatherChance
                            ? BlockId.WeatheredCobblestone : material;
                }

            case BlockId.Brick:
                {
                    int mossLayer = site.Kind is 1 or 5 ? 3 : 1;
                    int mossChance = site.Kind switch
                    {
                        1 => 12,
                        3 => 4,
                        4 => 6,
                        5 => 31,
                        _ => 0,
                    };
                    uint moss = CoordinateHash(site.Seed, x, y, z, 0x42524D53u);
                    if (relativeY >= 1 && relativeY <= mossLayer && moss % 100u < mossChance)
                        return BlockId.MossyBrick;

                    int crackChance = site.Kind switch
                    {
                        0 => 12,
                        1 => 32,
                        2 => 18,
                        3 => 36,
                        4 => 20,
                        5 => 23,
                        _ => 0,
                    };
                    uint crack = CoordinateHash(site.Seed, x, y, z, 0x43524143u);
                    return crack % 100u < crackChance ? BlockId.CrackedBrick : material;
                }

            default:
                return material;
        }
    }

    private static uint CoordinateHash(uint seed, int x, int y, int z, uint salt)
    {
        uint hash = seed ^ salt;
        hash ^= unchecked((uint)(x >> 1)) * 73856093u;
        hash = (hash << 13) | (hash >> 19);
        hash ^= unchecked((uint)(y >> 1)) * 19349663u;
        hash = (hash << 11) | (hash >> 21);
        hash ^= unchecked((uint)(z >> 1)) * 83492791u;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        return hash ^ (hash >> 16);
    }
}
