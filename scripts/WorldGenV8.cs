using System;
using Godot;

namespace Minecraft;

public partial class VoxelWorld
{
    public const int VillageCell = 384;
    private readonly Dictionary<long, VillageInfo?> _villageCache = new();

    public VillageInfo? GetVillageSite(int cellX, int cellZ)
    {
        if (_genVersion < 8 || _preset == "sky") return null;
        long key = ((long)cellX << 32) ^ (uint)cellZ;
        lock (_mutex)
        {
            if (_villageCache.TryGetValue(key, out VillageInfo? cached)) return cached;
        }

        uint seed = GeneratorV7.CoordinateHash(cellX, 8173, cellZ, 0x56494C38u);
        uint random = seed;
        if (NextVillage(ref random) % 100u >= 58u) return CacheVillage(key, null);
        int x = cellX * VillageCell + 64 + (int)(NextVillage(ref random) % 257u);
        int z = cellZ * VillageCell + 64 + (int)(NextVillage(ref random) % 257u);
        WorldGenV7.SurfaceSample center = GeneratorV7.SampleSurface(x, z, SeaLevel);
        VillageTheme? theme = VillageGenerator.ThemeForBiome(center.Biome);
        int y = center.Height;
        if (theme == null || y <= SeaLevel + 3 || y >= Height - 22)
            return CacheVillage(key, null);

        // A 3×3 sample grid rejects shorelines, rivers, cliffs and biome edges.
        for (int dz = -18; dz <= 18; dz += 18)
        for (int dx = -18; dx <= 18; dx += 18)
        {
            WorldGenV7.SurfaceSample sample = GeneratorV7.SampleSurface(x + dx, z + dz, SeaLevel);
            if (VillageGenerator.ThemeForBiome(sample.Biome) != theme
                    || sample.Height <= SeaLevel + 2 || Math.Abs(sample.Height - y) > 2
                    || GeneratorV7.IsCave(x + dx, y - 7, z + dz, sample.Height))
                return CacheVillage(key, null);
        }

        int minStructureX = Mathf.FloorToInt((x - 52) / (float)StructureCell);
        int maxStructureX = Mathf.FloorToInt((x + 52) / (float)StructureCell);
        int minStructureZ = Mathf.FloorToInt((z - 52) / (float)StructureCell);
        int maxStructureZ = Mathf.FloorToInt((z + 52) / (float)StructureCell);
        for (int cz = minStructureZ; cz <= maxStructureZ; cz++)
        for (int cx = minStructureX; cx <= maxStructureX; cx++)
        {
            if (GetStructureSite(cx, cz) is { } structure)
            {
                int dx = structure.X - x;
                int dz = structure.Z - z;
                if (dx * dx + dz * dz < 52 * 52) return CacheVillage(key, null);
            }
        }

        int minFireX = Mathf.FloorToInt((x - 28) / 64.0f);
        int maxFireX = Mathf.FloorToInt((x + 28) / 64.0f);
        int minFireZ = Mathf.FloorToInt((z - 28) / 64.0f);
        int maxFireZ = Mathf.FloorToInt((z + 28) / 64.0f);
        for (int cz = minFireZ; cz <= maxFireZ; cz++)
        for (int cx = minFireX; cx <= maxFireX; cx++)
        {
            if (GetCampfireSite(cx, cz) is { } fire)
            {
                int dx = fire.X - x;
                int dz = fire.Z - z;
                if (dx * dx + dz * dz < 24 * 24) return CacheVillage(key, null);
            }
        }

        int minCaveX = Mathf.FloorToInt((x - 30) / 128.0f);
        int maxCaveX = Mathf.FloorToInt((x + 30) / 128.0f);
        int minCaveZ = Mathf.FloorToInt((z - 30) / 128.0f);
        int maxCaveZ = Mathf.FloorToInt((z + 30) / 128.0f);
        for (int cz = minCaveZ; cz <= maxCaveZ; cz++)
        for (int cx = minCaveX; cx <= maxCaveX; cx++)
        {
            if (TryGetUndergroundStructureSiteV7(cx, cz, out Vector3I chamber, out _, out _))
            {
                int dx = chamber.X - x;
                int dz = chamber.Z - z;
                if (chamber.Y >= y - 42 && dx * dx + dz * dz < 30 * 30)
                    return CacheVillage(key, null);
            }
        }

        return CacheVillage(key, new VillageInfo(theme.Value, x, y, z, cellX, cellZ, seed));
    }

    private VillageInfo? CacheVillage(long key, VillageInfo? value)
    {
        lock (_mutex) _villageCache[key] = value;
        return value;
    }

    public List<VillageInfo> NearbyVillages(Vector3 position, float radius)
    {
        List<VillageInfo> result = [];
        int minX = Mathf.FloorToInt((position.X - radius) / VillageCell);
        int maxX = Mathf.FloorToInt((position.X + radius) / VillageCell);
        int minZ = Mathf.FloorToInt((position.Z - radius) / VillageCell);
        int maxZ = Mathf.FloorToInt((position.Z + radius) / VillageCell);
        float radiusSquared = radius * radius;
        for (int cz = minZ; cz <= maxZ; cz++)
        for (int cx = minX; cx <= maxX; cx++)
        {
            if (GetVillageSite(cx, cz) is not { } village) continue;
            float dx = village.X + 0.5f - position.X;
            float dz = village.Z + 0.5f - position.Z;
            if (dx * dx + dz * dz <= radiusSquared) result.Add(village);
        }
        return result;
    }

    private static uint NextVillage(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state == 0 ? 0x9E3779B9u : state;
    }

    private void StampVillagesV8(byte[] data, int strideY, int ox, int oz)
    {
        int minX = Mathf.FloorToInt((ox - VillageStructures.Radius) / (float)VillageCell);
        int maxX = Mathf.FloorToInt((ox + ChunkX + VillageStructures.Radius) / (float)VillageCell);
        int minZ = Mathf.FloorToInt((oz - VillageStructures.Radius) / (float)VillageCell);
        int maxZ = Mathf.FloorToInt((oz + ChunkZ + VillageStructures.Radius) / (float)VillageCell);
        for (int cellZ = minZ; cellZ <= maxZ; cellZ++)
        for (int cellX = minX; cellX <= maxX; cellX++)
        {
            if (GetVillageSite(cellX, cellZ) is not { } village) continue;
            Action<int, int, int, BlockId> put = (wx, wy, wz, id) =>
            {
                int lx = wx - ox;
                int lz = wz - oz;
                if (lx < 0 || lx >= ChunkX || lz < 0 || lz >= ChunkZ
                        || wy <= 0 || wy >= Height) return;
                int index = lx + lz * ChunkX + wy * strideY;
                if ((BlockId)data[index] != BlockId.Bedrock) data[index] = (byte)id;
            };

            BlockId ground = village.Theme switch
            {
                VillageTheme.Desert => BlockId.Sand,
                VillageTheme.Snow => BlockId.SnowGrass,
                _ => BlockId.Grass,
            };
            BlockId fill = village.Theme == VillageTheme.Desert ? BlockId.Sand : BlockId.Dirt;
            for (int dz = -VillageStructures.Radius; dz <= VillageStructures.Radius; dz++)
            for (int dx = -VillageStructures.Radius; dx <= VillageStructures.Radius; dx++)
            {
                if (dx * dx + dz * dz > VillageStructures.Radius * VillageStructures.Radius) continue;
                int wx = village.X + dx;
                int wz = village.Z + dz;
                int surface = GeneratorV7.SurfaceHeight(wx, wz, SeaLevel);
                if (surface > village.Y)
                {
                    for (int fy = village.Y + 1; fy <= surface; fy++) put(wx, fy, wz, BlockId.Air);
                }
                else if (surface < village.Y)
                {
                    for (int fy = surface + 1; fy < village.Y; fy++) put(wx, fy, wz, fill);
                }
                put(wx, village.Y, wz, ground);
            }
            VillageStructures.Stamp(village, put);
        }
    }
}
