using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Version 8 preserves the V7 coordinate fields and feature algorithms while
/// sampling the terrain once with a wider, smoother river-bank response.
/// V1-V7 generators remain frozen and are never mutated by this class.
/// </summary>
public sealed class WorldGenV8
{
    public const int Version = 8;

    private readonly WorldGenV7 _features;
    private readonly string _preset;
    private readonly FastNoiseLite _continent;
    private readonly FastNoiseLite _erosion;
    private readonly FastNoiseLite _hills;
    private readonly FastNoiseLite _mountainRange;
    private readonly FastNoiseLite _ridge;
    private readonly FastNoiseLite _temperature;
    private readonly FastNoiseLite _moisture;
    private readonly FastNoiseLite _warpX;
    private readonly FastNoiseLite _warpZ;
    private readonly FastNoiseLite _riverA;
    private readonly FastNoiseLite _riverB;
    private readonly FastNoiseLite _riverWidth;
    private readonly FastNoiseLite _plainsVariation;
    private readonly FastNoiseLite _caveEntrances;

    public WorldGenV8(int seed, string preset)
            : this(seed, preset, new WorldGenV7(seed, preset))
    {
    }

    public WorldGenV8(int seed, string preset, WorldGenV7 features)
    {
        _features = features;
        _preset = preset;

        _continent = CreateNoise(seed, 0x434F4E54u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.00078f, 4);
        _erosion = CreateNoise(seed, 0x45524F53u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0031f, 3);
        _hills = CreateNoise(seed, 0x48494C4Cu,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0085f, 3);
        _mountainRange = CreateNoise(seed, 0x4D4F554Eu,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.00115f, 3);
        _ridge = CreateNoise(seed, 0x52494447u,
                FastNoiseLite.NoiseTypeEnum.Perlin, 0.0028f, 3);
        _temperature = CreateNoise(seed, 0x54454D50u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.00135f, 4);
        _moisture = CreateNoise(seed, 0x4D4F4953u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0018f, 4);
        _warpX = CreateNoise(seed, 0x57525058u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.00125f, 3);
        _warpZ = CreateNoise(seed, 0x5752505Au,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.00125f, 3);
        _riverA = CreateNoise(seed, 0x52495641u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0010f, 2);
        _riverB = CreateNoise(seed, 0x52495642u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0012f, 2);
        _riverWidth = CreateNoise(seed, 0x52495744u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0007f, 2);
        _plainsVariation = CreateNoise(seed, 0x504C4149u,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.0068f, 2);
        _caveEntrances = CreateNoise(seed, 0x5638454Eu,
                FastNoiseLite.NoiseTypeEnum.Simplex, 0.024f, 2);
    }

    private static FastNoiseLite CreateNoise(int seed, uint salt,
            FastNoiseLite.NoiseTypeEnum type, float frequency, int octaves)
    {
        uint value = unchecked((uint)seed) ^ salt;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return new FastNoiseLite
        {
            Seed = unchecked((int)value),
            NoiseType = type,
            Frequency = frequency,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = octaves,
            FractalLacunarity = 2.05f,
            FractalGain = 0.48f,
        };
    }

    public WorldGenV7.ClimateSample ClimateAt(int x, int z, int height,
            bool includeRivers = true)
    {
        float warpX = _warpX.GetNoise2D(x, z) * 132.0f;
        float warpZ = _warpZ.GetNoise2D(x, z) * 132.0f;
        float sampleX = x + warpX;
        float sampleZ = z + warpZ;
        float continentalness = _continent.GetNoise2D(
                x + warpX * 0.42f, z + warpZ * 0.42f);
        float temperature = _temperature.GetNoise2D(sampleX, sampleZ);
        float moisture = _moisture.GetNoise2D(
                x - warpZ * 0.75f, z + warpX * 0.75f);
        temperature -= Math.Max(0, height - 40) * 0.0085f;
        float river = includeRivers ? RiverStrength(x, z) : 0.0f;
        return new WorldGenV7.ClimateSample(
                temperature, moisture, continentalness, river);
    }

    public WorldGenV7.SurfaceSample SampleSurface(int x, int z, int seaLevel)
    {
        int height;
        WorldGenV7.ClimateSample climate;
        bool climateAtSurface;

        if (_preset == "flat")
        {
            height = seaLevel + 4;
            climate = ClimateAt(x, z, height, includeRivers: false);
            climateAtSurface = true;
        }
        else if (_preset == "sky")
        {
            float baseNoise = _hills.GetNoise2D(x, z);
            float peaks = 1.0f - Math.Abs(_ridge.GetNoise2D(x, z));
            height = Mathf.Clamp(Mathf.RoundToInt(78.0f + baseNoise * 7.0f
                    + peaks * 13.0f), 52, 116);
            climate = ClimateAt(x, z, height, includeRivers: false);
            climateAtSurface = true;
        }
        else
        {
            const int climateReferenceHeightOffset = 12;
            int climateReferenceHeight = seaLevel + climateReferenceHeightOffset;
            climate = ClimateAt(x, z, climateReferenceHeight);
            float land = SmoothStep(-0.42f, -0.04f, climate.Continentalness);
            float erosion = _erosion.GetNoise2D(x, z);
            float hills = _hills.GetNoise2D(x, z);
            float range = _mountainRange.GetNoise2D(x, z);
            float ridges = 1.0f - Math.Abs(_ridge.GetNoise2D(x, z));
            float mountainMask = SmoothStep(0.02f, 0.50f, range);
            float mountainPeaks = ridges * ridges * (0.45f + 0.55f * ridges);

            float terrainHeight = seaLevel - 28.0f + land * 41.0f;
            terrainHeight += hills * (2.5f + land * 4.0f);
            terrainHeight += erosion * (1.8f + land * 2.0f);
            float mountainScale = _preset == "amplified" ? 1.62f : 1.0f;
            terrainHeight += mountainMask
                    * (13.0f + mountainPeaks * 18.0f) * mountainScale;

            float swampMask = SmoothStep(0.25f, 0.58f, climate.Moisture)
                    * (1.0f - SmoothStep(0.12f, 0.46f, climate.Continentalness));
            if (climate.Temperature > -0.12f)
            {
                terrainHeight -= swampMask * 3.2f;
            }

            float channelFloor = seaLevel - 1.0f
                    + (1.0f - climate.RiverStrength) * 2.0f;
            float channelHeight = Math.Min(terrainHeight, channelFloor);
            float bankBlend = MathF.Sqrt(climate.RiverStrength) * land;
            terrainHeight = Mathf.Lerp(terrainHeight, channelHeight, bankBlend);

            height = Mathf.Clamp(Mathf.RoundToInt(terrainHeight), 4, 118);
            climateAtSurface = false;
        }

        float temperature = climateAtSurface ? climate.Temperature
                : climate.Temperature - Math.Max(0, height - 40) * 0.0085f
                        + Math.Max(0, seaLevel + 12 - 40) * 0.0085f;
        Biome biome = WorldGenV7.ClassifyBiome(temperature,
                climate.Moisture, height, seaLevel, _plainsVariation.GetNoise2D(x, z));
        return new WorldGenV7.SurfaceSample(height, biome);
    }

    public int SurfaceHeight(int x, int z, int seaLevel) =>
            SampleSurface(x, z, seaLevel).Height;

    public Biome BiomeAt(int x, int z, int height, int seaLevel) =>
            SampleSurface(x, z, seaLevel).Biome;

    private float RiverStrength(int x, int z)
    {
        float width = 0.038f + (_riverWidth.GetNoise2D(x, z) + 1.0f) * 0.010f;
        float distanceA = Math.Abs(_riverA.GetNoise2D(x, z));
        float distanceB = Math.Abs(_riverB.GetNoise2D(x + 311.0f, z - 173.0f));
        float distance = Math.Min(distanceA, distanceB * 1.12f);
        return 1.0f - SmoothStep(width, width + 0.16f, distance);
    }

    public bool IsIsland(int x, int z) => _features.IsIsland(x, z);

    public bool IsCave(int x, int y, int z, int surfaceHeight)
    {
        if (_features.IsCave(x, y, z, surfaceHeight))
        {
            return true;
        }

        int depth = surfaceHeight - y;
        return _preset != "flat" && depth is >= 1 and <= 10
                && y > 3 && _caveEntrances.GetNoise3D(x, y, z) > 0.80f;
    }

    public bool IsMagmaPocket(int x, int y, int z) =>
            _features.IsMagmaPocket(x, y, z);

    public bool IsAquifer(int x, int y, int z) =>
            _features.IsAquifer(x, y, z);

    public BlockId OreAt(int x, int y, int z, Biome biome) =>
            _features.OreAt(x, y, z, biome);

    public bool IsGravelOceanFloor(int x, int z) =>
            _features.IsGravelOceanFloor(x, z);

    public uint CoordinateHash(int x, int y, int z, uint salt) =>
            _features.CoordinateHash(x, y, z, salt);

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (value <= edge0)
        {
            return 0.0f;
        }

        if (value >= edge1)
        {
            return 1.0f;
        }

        float t = (value - edge0) / (edge1 - edge0);
        return t * t * (3.0f - 2.0f * t);
    }
}


/// <summary>V8-specific world dispatch and biome-aware vegetation distribution.</summary>
public partial class VoxelWorld
{
    private WorldGenV8? _worldGenV8;

    private WorldGenV8 GeneratorV8 => _worldGenV8
            ?? throw new InvalidOperationException("WorldGenV8 was not initialized.");

    private void InitializeWorldGenV8()
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            _worldGenV8 = new WorldGenV8(_manifest.Seed, _preset, GeneratorV7);
        }
    }

    private TreeInfo? GetTreeAtV8(int tx, int tz)
    {
        if (IsSky && !GeneratorV8.IsIsland(tx, tz))
        {
            return null;
        }

        WorldGenV7.SurfaceSample surface = GeneratorV8.SampleSurface(tx, tz, SeaLevel);
        int height = surface.Height;
        if (height <= SeaLevel + 1 || height >= Height - 12)
        {
            return null;
        }

        Biome biome = surface.Biome;
        int chance = biome switch
        {
            Biome.DenseForest => 27,
            Biome.Jungle => 25,
            Biome.Taiga => 42,
            Biome.Swamp => 78,
            Biome.Birch => 60,
            Biome.Meadow => 165,
            Biome.FlowerPlains => 220,
            Biome.Plains => 205,
            Biome.Mountain => 195,
            _ => 0,
        };
        if (chance == 0)
        {
            return null;
        }

        uint hash = GeneratorV8.CoordinateHash(tx, 0, tz, 0x54524537u);
        if (hash % (uint)chance != 0)
        {
            return null;
        }

        int kind = biome switch
        {
            Biome.Taiga => 2,
            Biome.Jungle => 3,
            Biome.Swamp => 4,
            Biome.Birch => 1,
            Biome.DenseForest => 5,
            _ => 0,
        };
        int trunk = 4 + (int)((hash >> 8) % 3u);
        if (kind is 2 or 3)
        {
            trunk += 2;
        }
        else if (kind == 4)
        {
            trunk = 4 + (int)((hash >> 12) % 2u);
        }

        return new TreeInfo(tx, tz, height + 1, trunk, kind);
    }

    private BlockId BlockAtV8(int x, int y, int z, int height, Biome biome)
    {
        if (y < 0 || y >= Height)
        {
            return BlockId.Air;
        }

        if (_preset == "sky")
        {
            if (y == 0 || !GeneratorV8.IsIsland(x, z) || y < height - 26)
            {
                return BlockId.Air;
            }
        }
        else if (y == 0)
        {
            return BlockId.Bedrock;
        }

        if (y > height)
        {
            return _preset != "sky" && y <= SeaLevel ? BlockId.Water : BlockId.Air;
        }

        BlockId id = BlockId.Stone;
        bool sandy = biome is Biome.Beach or Biome.Desert;
        if (y == height)
        {
            if (biome == Biome.Snow || (biome == Biome.Mountain && height >= SeaLevel + 42))
            {
                id = BlockId.SnowGrass;
            }
            else if (biome == Biome.Ocean)
            {
                id = GeneratorV8.IsGravelOceanFloor(x, z) ? BlockId.Gravel : BlockId.Sand;
            }
            else
            {
                id = sandy ? BlockId.Sand : BlockId.Grass;
            }
        }
        else if (y > height - 4)
        {
            id = sandy || biome == Biome.Ocean
                    ? (biome == Biome.Ocean && GeneratorV8.IsGravelOceanFloor(x, z)
                            ? BlockId.Gravel : BlockId.Sand)
                    : BlockId.Dirt;
        }

        if (id == BlockId.Stone)
        {
            id = GeneratorV8.OreAt(x, y, z, biome);
        }

        if (GeneratorV8.IsCave(x, y, z, height))
        {
            if (GeneratorV8.IsMagmaPocket(x, y, z))
            {
                return BlockId.Lava;
            }

            if (GeneratorV8.IsAquifer(x, y, z))
            {
                return BlockId.Water;
            }

            return BlockId.Air;
        }

        return id;
    }

    private StructureInfo? GetStructureSiteV8(int cx, int cz)
    {
        uint rng = Mix((uint)_noiseSeeds[2], unchecked((uint)cx),
                unchecked((uint)cz), 0x56375354u);
        int x = cx * StructureCell + Range(ref rng, 12, StructureCell - 12);
        int z = cz * StructureCell + Range(ref rng, 12, StructureCell - 12);
        if (IsSky && !GeneratorV8.IsIsland(x, z))
        {
            return null;
        }

        WorldGenV7.SurfaceSample surface = GeneratorV8.SampleSurface(x, z, SeaLevel);
        int height = surface.Height;
        if (height <= SeaLevel + 2 || height >= Height - 14)
        {
            return null;
        }

        Biome biome = surface.Biome;
        if (biome is Biome.Ocean or Biome.Beach)
        {
            return null;
        }

        int roll = Range(ref rng, 0, 99);
        int kind = roll < 6 ? 3
                : roll < 16 ? 4
                : roll < 23 ? 5
                : Range(ref rng, 0, 2);
        if (biome == Biome.Taiga && roll < 34)
        {
            kind = 4;
        }
        else if ((biome is Biome.Jungle or Biome.Swamp) && roll < 42)
        {
            kind = 5;
        }
        else if (biome == Biome.Mountain && roll < 48)
        {
            kind = 2;
        }

        return new StructureInfo(kind, x, height, z, rng);
    }

    private void StampDecoratorsV8(byte[] data, int strideY, int ox, int oz)
    {
        for (int wz = oz - 2; wz < oz + ChunkZ + 2; wz++)
        {
            for (int wx = ox - 2; wx < ox + ChunkX + 2; wx++)
            {
                WorldGenV7.SurfaceSample surface = GeneratorV8.SampleSurface(wx, wz, SeaLevel);
                int height = surface.Height;
                if (height <= SeaLevel + 1 || height >= Height - 4)
                {
                    continue;
                }

                Biome biome = surface.Biome;
                if (biome is Biome.Desert or Biome.Snow or Biome.Ocean or Biome.Beach
                        || BlockAtV8(wx, height, wz, height, biome) != BlockId.Grass)
                {
                    continue;
                }

                uint random = GeneratorV8.CoordinateHash(wx, height, wz, 0x44454337u);
                if (random % (biome == Biome.Mountain ? 520u : 1024u) == 0)
                {
                    StampBoulder(wx, height, wz, data, strideY, ox, oz);
                    continue;
                }

                int lx = wx - ox;
                int lz = wz - oz;
                if (lx < 0 || lx >= ChunkX || lz < 0 || lz >= ChunkZ)
                {
                    continue;
                }

                int above = lx + lz * ChunkX + (height + 1) * strideY;
                if ((BlockId)data[above] != BlockId.Air)
                {
                    continue;
                }

                BlockId plant = biome switch
                {
                    Biome.Jungle => random % 5u == 0 ? BlockId.FlowerRed : BlockId.TallGrass,
                    Biome.Swamp => random % 5u == 0 ? BlockId.FlowerYellow : BlockId.TallGrass,
                    Biome.Taiga => random % 7u == 0 ? BlockId.Snow : BlockId.TallGrass,
                    Biome.FlowerPlains => random % 3u == 0 ? BlockId.FlowerRed
                            : random % 3u == 1 ? BlockId.FlowerYellow : BlockId.TallGrass,
                    Biome.Meadow => random % 4u == 0 ? BlockId.FlowerYellow : BlockId.TallGrass,
                    Biome.DenseForest => random % 5u == 0 ? BlockId.FlowerRed : BlockId.TallGrass,
                    _ => random % 11u == 0 ? BlockId.FlowerYellow : BlockId.TallGrass,
                };
                uint frequency = biome switch
                {
                    Biome.Jungle => 3u,
                    Biome.Swamp => 4u,
                    Biome.FlowerPlains => 4u,
                    Biome.Meadow => 6u,
                    Biome.DenseForest => 5u,
                    _ => 9u,
                };
                if (random % frequency == 0)
                {
                    data[above] = (byte)plant;
                }
            }
        }
    }
}
