using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Versioned, seed-local terrain model for generator version 7.
/// All noise resources are configured once and then read-only. Generation
/// methods have no shared mutable state, so two worker threads may evaluate
/// distant coordinates in any order without changing the result.
/// </summary>
public sealed class WorldGenV7
{
    public const int Version = 7;

    private readonly int _seed;
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
    private readonly FastNoiseLite _islandMask;
    private readonly FastNoiseLite _caveA;
    private readonly FastNoiseLite _caveB;
    private readonly FastNoiseLite _caverns;
    private readonly FastNoiseLite _entrances;
    private readonly FastNoiseLite _aquifer;
    private readonly FastNoiseLite _magma;
    private readonly FastNoiseLite _plainsVariation;

    public WorldGenV7(int seed, string preset)
    {
        _seed = seed;
        _preset = preset;

        _continent = CreateNoise(0x434F4E54u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.00078f, 4);
        _erosion = CreateNoise(0x45524F53u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0031f, 3);
        _hills = CreateNoise(0x48494C4Cu, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0085f, 3);
        _mountainRange = CreateNoise(0x4D4F554Eu, FastNoiseLite.NoiseTypeEnum.Simplex, 0.00115f, 3);
        _ridge = CreateNoise(0x52494447u, FastNoiseLite.NoiseTypeEnum.Perlin, 0.0044f, 4);
        _temperature = CreateNoise(0x54454D50u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.00135f, 4);
        _moisture = CreateNoise(0x4D4F4953u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0018f, 4);
        _warpX = CreateNoise(0x57525058u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.00125f, 3);
        _warpZ = CreateNoise(0x5752505Au, FastNoiseLite.NoiseTypeEnum.Simplex, 0.00125f, 3);
        _riverA = CreateNoise(0x52495641u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0017f, 2);
        _riverB = CreateNoise(0x52495642u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0021f, 2);
        _riverWidth = CreateNoise(0x52495744u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0011f, 2);
        _islandMask = CreateNoise(0x49534C37u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0065f, 3);
        _caveA = CreateNoise(0x43415641u, FastNoiseLite.NoiseTypeEnum.Perlin, 0.031f, 2);
        _caveB = CreateNoise(0x43415642u, FastNoiseLite.NoiseTypeEnum.Perlin, 0.043f, 2);
        _caverns = CreateNoise(0x43415652u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.014f, 3);
        _entrances = CreateNoise(0x454E5452u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.024f, 2);
        _aquifer = CreateNoise(0x41515549u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.021f, 2);
        _magma = CreateNoise(0x4D41474Du, FastNoiseLite.NoiseTypeEnum.Perlin, 0.026f, 2);
        _plainsVariation = CreateNoise(0x504C4149u, FastNoiseLite.NoiseTypeEnum.Simplex, 0.0068f, 2);
    }

    private FastNoiseLite CreateNoise(uint salt, FastNoiseLite.NoiseTypeEnum type,
            float frequency, int octaves)
    {
        FastNoiseLite noise = new()
        {
            Seed = DeriveSeed(_seed, salt),
            NoiseType = type,
            Frequency = frequency,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = octaves,
            FractalLacunarity = 2.05f,
            FractalGain = 0.48f,
        };
        return noise;
    }

    private static int DeriveSeed(int seed, uint salt)
    {
        uint value = unchecked((uint)seed) ^ salt;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return unchecked((int)value);
    }

    public int SurfaceHeight(int x, int z, int seaLevel) =>
            SampleSurface(x, z, seaLevel).Height;

    /// <summary>
    /// Samples a column once for both terrain height and biome selection.
    /// The chunk builder uses this to avoid evaluating the domain warp and
    /// temperature/moisture layers twice for every surface column.
    /// </summary>
    public SurfaceSample SampleSurface(int x, int z, int seaLevel)
    {
        int climateReferenceHeight = seaLevel + 12;
        ClimateSample climate = ClimateAt(x, z, climateReferenceHeight);

        int height;
        if (_preset == "flat")
        {
            height = seaLevel + 4;
        }
        else if (_preset == "sky")
        {
            float baseNoise = _hills.GetNoise2D(x, z);
            float peaks = 1.0f - Math.Abs(_ridge.GetNoise2D(x, z));
            height = Mathf.Clamp(Mathf.RoundToInt(78.0f + baseNoise * 7.0f
                    + peaks * 13.0f), 52, 116);
            climate = ClimateAt(x, z, height, includeRivers: false);
        }
        else
        {
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
            terrainHeight += mountainMask * (13.0f + mountainPeaks * 28.0f) * mountainScale;

            float swampMask = SmoothStep(0.25f, 0.58f, climate.Moisture)
                    * (1.0f - SmoothStep(0.12f, 0.46f, climate.Continentalness));
            if (climate.Temperature > -0.12f)
            {
                terrainHeight -= swampMask * 3.2f;
            }

            if (land > 0.45f && climate.RiverStrength > 0.25f)
            {
                float channelFloor = seaLevel - 1.0f
                        + (1.0f - climate.RiverStrength) * 2.0f;
                terrainHeight = Math.Min(terrainHeight, channelFloor);
            }

            height = Mathf.Clamp(Mathf.RoundToInt(terrainHeight), 4, 118);
        }

        // ClimateAt applied altitude cooling at the reference height. Adjust
        // it to the actual surface without resampling temperature noise.
        float temperature = climate.Temperature
                - Math.Max(0, height - 40) * 0.0085f
                + Math.Max(0, climateReferenceHeight - 40) * 0.0085f;
        float plains = _plainsVariation.GetNoise2D(x, z);
        Biome biome = ClassifyBiome(temperature, climate.Moisture,
                height, seaLevel, plains);
        return new SurfaceSample(height, biome);
    }

    public ClimateSample ClimateAt(int x, int z, int height, bool includeRivers = true)
    {
        float warpX = _warpX.GetNoise2D(x, z) * 132.0f;
        float warpZ = _warpZ.GetNoise2D(x, z) * 132.0f;
        float sampleX = x + warpX;
        float sampleZ = z + warpZ;
        float continentalness = _continent.GetNoise2D(x + warpX * 0.42f, z + warpZ * 0.42f);
        float temperature = _temperature.GetNoise2D(sampleX, sampleZ);
        float moisture = _moisture.GetNoise2D(x - warpZ * 0.75f, z + warpX * 0.75f);
        temperature -= Math.Max(0, height - 40) * 0.0085f;
        return new ClimateSample(temperature, moisture, continentalness,
                includeRivers ? RiverStrength(x, z) : 0.0f);
    }

    public Biome BiomeAt(int x, int z, int height, int seaLevel) =>
            SampleSurface(x, z, seaLevel).Biome;

    /// <summary>Pure classification function, also used by focused biome tests.</summary>
    public static Biome ClassifyBiome(float temperature, float moisture,
            int height, int seaLevel, float plainsVariation)
    {
        if (height <= seaLevel - 4)
        {
            return Biome.Ocean;
        }

        if (height <= seaLevel + 2)
        {
            return Biome.Beach;
        }

        if (temperature < -0.42f || height > seaLevel + 52)
        {
            return Biome.Snow;
        }

        if (height > seaLevel + 30)
        {
            return Biome.Mountain;
        }

        if (temperature > 0.24f && moisture < -0.20f)
        {
            return Biome.Desert;
        }

        if (temperature > 0.20f && moisture > 0.30f)
        {
            return Biome.Jungle;
        }

        if (moisture > 0.40f && temperature > -0.16f && height < seaLevel + 16)
        {
            return Biome.Swamp;
        }

        if (temperature < -0.24f && moisture > -0.20f)
        {
            return Biome.Taiga;
        }

        if (moisture > 0.19f)
        {
            if (temperature > -0.12f && temperature < 0.28f && moisture < 0.38f)
            {
                return Biome.Birch;
            }

            return Biome.DenseForest;
        }

        if (plainsVariation > 0.25f)
        {
            return Biome.FlowerPlains;
        }

        if (plainsVariation < -0.30f && moisture > -0.08f)
        {
            return Biome.Meadow;
        }

        return Biome.Plains;
    }

    private float RiverStrength(int x, int z)
    {
        float width = 0.038f + (_riverWidth.GetNoise2D(x, z) + 1.0f) * 0.010f;
        float distanceA = Math.Abs(_riverA.GetNoise2D(x, z));
        float distanceB = Math.Abs(_riverB.GetNoise2D(x + 311.0f, z - 173.0f));
        float distance = Math.Min(distanceA, distanceB * 1.12f);
        return 1.0f - SmoothStep(width, width + 0.035f, distance);
    }

    public bool IsIsland(int x, int z) => _islandMask.GetNoise2D(x, z) > 0.10f;

    public bool IsCave(int x, int y, int z, int surfaceHeight)
    {
        if (_preset == "flat" || y <= 3 || y >= surfaceHeight - 4)
        {
            return false;
        }

        int depth = surfaceHeight - y;
        if (depth < 5)
        {
            return false;
        }

        float a = Math.Abs(_caveA.GetNoise3D(x, y, z));
        float b = Math.Abs(_caveB.GetNoise3D(x - 91.0f, y + 37.0f, z + 53.0f));
        if (a < 0.040f && b < 0.135f)
        {
            return true;
        }

        if (y <= 76 && depth >= 9 && _caverns.GetNoise3D(x, y, z) > 0.61f)
        {
            return true;
        }

        return depth <= 12 && _entrances.GetNoise3D(x, y, z) > 0.70f;
    }

    public bool IsMagmaPocket(int x, int y, int z) =>
            y <= 10 && _magma.GetNoise3D(x, y, z) > -0.12f;

    public bool IsAquifer(int x, int y, int z) =>
            y <= 42 && _aquifer.GetNoise3D(x, y, z) > 0.48f
            && _moisture.GetNoise2D(x, z) > -0.22f;

    public BlockId OreAt(int x, int y, int z, Biome biome)
    {
        uint value = CoordinateHash(x, y, z, 0x4F524537u);
        if (y <= 20 && value % (biome == Biome.Mountain ? 460u : 760u) == 0)
        {
            return BlockId.DiamondOre;
        }

        if (y <= 44 && value % (biome == Biome.Mountain ? 245u : 390u) == 0)
        {
            return BlockId.GoldOre;
        }

        if (y <= 82 && value % (biome == Biome.Mountain ? 105u : 145u) == 0)
        {
            return BlockId.IronOre;
        }

        if (y <= 112 && value % 84u < 2u)
        {
            return BlockId.CoalOre;
        }

        return BlockId.Stone;
    }

    public bool IsGravelOceanFloor(int x, int z) =>
            (CoordinateHash(x, 0, z, 0x47524156u) & 3u) == 0u;

    public uint CoordinateHash(int x, int y, int z, uint salt)
    {
        uint h = unchecked((uint)_seed) ^ salt;
        h ^= unchecked((uint)x) * 0x9E3779B1u;
        h = (h << 13) | (h >> 19);
        h ^= unchecked((uint)y) * 0x85EBCA77u;
        h = (h << 11) | (h >> 21);
        h ^= unchecked((uint)z) * 0xC2B2AE3Du;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

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

    public readonly record struct ClimateSample(
        float Temperature,
        float Moisture,
        float Continentalness,
        float RiverStrength);

    public readonly record struct SurfaceSample(int Height, Biome Biome);
}

/// <summary>V7-specific block stamping and decoration. Legacy versions never enter these paths.</summary>
public partial class VoxelWorld
{
    private WorldGenV7? _worldGenV7;

    private WorldGenV7 GeneratorV7 => _worldGenV7
            ?? throw new InvalidOperationException("WorldGenV7 was not initialized.");

    private void InitializeWorldGenV7()
    {
        if (_genVersion >= WorldGenV7.Version)
        {
            _worldGenV7 = new WorldGenV7(_manifest.Seed, _preset);
        }
    }

    private BlockId BlockAtV7(int x, int y, int z, int height, Biome biome)
    {
        if (y < 0 || y >= Height)
        {
            return BlockId.Air;
        }

        if (_preset == "sky")
        {
            if (y == 0 || !GeneratorV7.IsIsland(x, z) || y < height - 26)
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
                id = GeneratorV7.IsGravelOceanFloor(x, z) ? BlockId.Gravel : BlockId.Sand;
            }
            else
            {
                id = sandy ? BlockId.Sand : BlockId.Grass;
            }
        }
        else if (y > height - 4)
        {
            id = sandy || biome == Biome.Ocean
                    ? (biome == Biome.Ocean && GeneratorV7.IsGravelOceanFloor(x, z)
                            ? BlockId.Gravel : BlockId.Sand)
                    : BlockId.Dirt;
        }

        if (id == BlockId.Stone)
        {
            id = GeneratorV7.OreAt(x, y, z, biome);
        }

        if (GeneratorV7.IsCave(x, y, z, height))
        {
            if (GeneratorV7.IsMagmaPocket(x, y, z))
            {
                return BlockId.Lava;
            }

            if (GeneratorV7.IsAquifer(x, y, z))
            {
                return BlockId.Water;
            }

            return BlockId.Air;
        }

        return id;
    }

    private TreeInfo? GetTreeAtV7(int tx, int tz)
    {
        if (IsSky && !GeneratorV7.IsIsland(tx, tz))
        {
            return null;
        }

        int height = SurfaceHeight(tx, tz);
        if (height <= SeaLevel + 1 || height >= Height - 12)
        {
            return null;
        }

        Biome biome = BiomeAt(tx, tz, height);
        int chance = biome switch
        {
            Biome.DenseForest => 38,
            Biome.Jungle => 34,
            Biome.Taiga => 58,
            Biome.Swamp => 108,
            Biome.Birch => 78,
            Biome.Meadow => 190,
            Biome.FlowerPlains => 245,
            Biome.Plains => 220,
            Biome.Mountain => 210,
            _ => 0,
        };
        if (chance == 0)
        {
            return null;
        }

        uint hash = GeneratorV7.CoordinateHash(tx, 0, tz, 0x54524537u);
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
        if (kind == 2 || kind == 3)
        {
            trunk += 2;
        }
        else if (kind == 4)
        {
            trunk = 4 + (int)((hash >> 12) % 2u);
        }

        return new TreeInfo(tx, tz, height + 1, trunk, kind);
    }

    private BlockId TreeBlockForV7(TreeInfo tree, int wx, int wy, int wz)
    {
        BlockId log = tree.Kind == 1 ? BlockId.BirchLog : BlockId.Log;
        BlockId leaves = tree.Kind == 1 ? BlockId.BirchLeaves : BlockId.Leaves;
        int dx = wx - tree.X;
        int dz = wz - tree.Z;

        if (dx == 0 && dz == 0 && wy >= tree.Base && wy < tree.Base + tree.Trunk)
        {
            return log;
        }

        if (tree.Kind == 4 && wy == tree.Base + tree.Trunk - 2
                && ((dx == 1 && dz == 0) || (dx == 0 && dz == 1)))
        {
            return log;
        }

        if (tree.Kind == 2)
        {
            int layer = wy - (tree.Base + tree.Trunk - 6);
            if (layer < 0 || layer > 5)
            {
                return BlockId.Air;
            }

            int radius = layer switch
            {
                0 or 1 => 2,
                2 or 3 => 1,
                _ => 0,
            };
            if (Mathf.Abs(dx) > radius || Mathf.Abs(dz) > radius
                    || (radius == 2 && Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 2))
            {
                return BlockId.Air;
            }

            return leaves;
        }

        int crownLayer = wy - (tree.Base + tree.Trunk - 2);
        if (crownLayer < 0 || crownLayer > 3)
        {
            return BlockId.Air;
        }

        int crownRadius = 2;
        if (crownLayer >= 2)
        {
            crownRadius = 1;
        }

        if (Mathf.Abs(dx) > crownRadius || Mathf.Abs(dz) > crownRadius)
        {
            return BlockId.Air;
        }

        if (crownRadius == 2 && crownLayer == 0
                && Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 2)
        {
            return BlockId.Air;
        }

        if (dx == 0 && dz == 0 && crownLayer < 2)
        {
            return BlockId.Air;
        }

        return leaves;
    }

    private StructureInfo? GetStructureSiteV7(int cx, int cz)
    {
        uint rng = Mix((uint)_noiseSeeds[2], unchecked((uint)cx),
                unchecked((uint)cz), 0x56375354u);
        int x = cx * StructureCell + Range(ref rng, 12, StructureCell - 12);
        int z = cz * StructureCell + Range(ref rng, 12, StructureCell - 12);
        if (IsSky && !GeneratorV7.IsIsland(x, z))
        {
            return null;
        }

        int height = SurfaceHeight(x, z);
        if (height <= SeaLevel + 2 || height >= Height - 14)
        {
            return null;
        }

        Biome biome = BiomeAt(x, z, height);
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

    private void StampOreVeinsV7(Vector2I cp, byte[] data, int strideY, int ox, int oz)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cp.X + dx;
                int nz = cp.Y + dz;
                uint rng = Mix((uint)_noiseSeeds[0], unchecked((uint)nx),
                        unchecked((uint)nz), 0x56374F52u);
                int cellOx = nx * ChunkX;
                int cellOz = nz * ChunkZ;
                Biome biome = BiomeAt(cellOx + ChunkX / 2, cellOz + ChunkZ / 2);
                TryV7Vein(ref rng, BlockId.CoalOre, cellOx, cellOz,
                        18, 112, 9, 4, 11, data, strideY, ox, oz);
                TryV7Vein(ref rng, BlockId.IronOre, cellOx, cellOz,
                        6, 88, 6, 3, 8, data, strideY, ox, oz);
                TryV7Vein(ref rng, BlockId.GoldOre, cellOx, cellOz,
                        5, 48, biome == Biome.Mountain ? 4 : 2, 2, 6, data, strideY, ox, oz);
                TryV7Vein(ref rng, BlockId.DiamondOre, cellOx, cellOz,
                        4, 24, 1, 2, 4, data, strideY, ox, oz);
            }
        }
    }

    private void TryV7Vein(ref uint rng, BlockId ore, int cellOx, int cellOz,
            int minY, int maxY, int count, int minSize, int maxSize,
            byte[] data, int strideY, int ox, int oz)
    {
        int veins = count + Range(ref rng, 0, 2);
        for (int vein = 0; vein < veins; vein++)
        {
            int x = cellOx + Range(ref rng, 0, ChunkX - 1);
            int z = cellOz + Range(ref rng, 0, ChunkZ - 1);
            int y = Range(ref rng, Math.Max(2, minY), Math.Min(maxY, Height - 2));
            int size = Range(ref rng, minSize, maxSize);
            for (int step = 0; step < size; step++)
            {
                StampOre(x, y, z, ore, data, strideY, ox, oz);
                switch (Range(ref rng, 0, 5))
                {
                    case 0: x++; break;
                    case 1: x--; break;
                    case 2: y++; break;
                    case 3: y--; break;
                    case 4: z++; break;
                    default: z--; break;
                }

                y = Mathf.Clamp(y, 2, Height - 2);
            }
        }
    }

    private void StampDecoratorsV7(byte[] data, int strideY, int ox, int oz)
    {
        for (int wz = oz - 2; wz < oz + ChunkZ + 2; wz++)
        {
            for (int wx = ox - 2; wx < ox + ChunkX + 2; wx++)
            {
                int height = SurfaceHeight(wx, wz);
                if (height <= SeaLevel + 1 || height >= Height - 4)
                {
                    continue;
                }

                Biome biome = BiomeAt(wx, wz, height);
                if (biome is Biome.Desert or Biome.Snow or Biome.Ocean or Biome.Beach
                        || BlockAtV7(wx, height, wz, height, biome) != BlockId.Grass)
                {
                    continue;
                }

                uint random = GeneratorV7.CoordinateHash(wx, height, wz, 0x44454337u);
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

    private const int UndergroundStructureCell = 128;

    private void StampUndergroundStructuresV7(byte[] data, int strideY, int ox, int oz)
    {
        int minCellX = Mathf.FloorToInt((ox - 14) / (float)UndergroundStructureCell);
        int maxCellX = Mathf.FloorToInt((ox + ChunkX + 14) / (float)UndergroundStructureCell);
        int minCellZ = Mathf.FloorToInt((oz - 14) / (float)UndergroundStructureCell);
        int maxCellZ = Mathf.FloorToInt((oz + ChunkZ + 14) / (float)UndergroundStructureCell);

        for (int cz = minCellZ; cz <= maxCellZ; cz++)
        {
            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                uint rng = Mix((uint)_noiseSeeds[2], unchecked((uint)cx),
                        unchecked((uint)cz), 0x554E4437u);
                if (Range(ref rng, 0, 10) != 0)
                {
                    continue;
                }

                int x = cx * UndergroundStructureCell + Range(ref rng, 18, 109);
                int z = cz * UndergroundStructureCell + Range(ref rng, 18, 109);
                // Sky worlds contain 26-block-thick floating islands. Keep
                // chambers inside that island slab and never create detached
                // rooms in the empty space beneath an island.
                if (IsSky && !GeneratorV7.IsIsland(x, z))
                {
                    continue;
                }

                int surface = SurfaceHeight(x, z);
                int minY = IsSky ? Math.Max(4, surface - 24) : 10;
                int maxY = IsSky
                        ? Math.Min(Height - 6, surface - 18)
                        : Math.Min(43, surface - 18);
                if (maxY < minY || (!IsSky && maxY < 12))
                {
                    continue;
                }

                int y = Range(ref rng, minY, maxY);
                int kind = Range(ref rng, 0, 2);
                StampUndergroundSiteV7(x, y, z, kind, ref rng, data, strideY, ox, oz);
            }
        }
    }

    private void StampUndergroundSiteV7(int x, int y, int z, int kind, ref uint rng,
            byte[] data, int strideY, int ox, int oz)
    {
        int radiusX = kind == 1 ? 5 : 4;
        int radiusZ = kind == 2 ? 4 : 3;
        BlockId masonry = kind == 0 ? BlockId.Brick : BlockId.Cobblestone;

        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            for (int dz = -radiusZ; dz <= radiusZ; dz++)
            {
                bool wall = Math.Abs(dx) == radiusX || Math.Abs(dz) == radiusZ;
                PutUndergroundV7(data, strideY, ox, oz, x + dx, y, z + dz, masonry);
                for (int dy = 1; dy <= 4; dy++)
                {
                    PutUndergroundV7(data, strideY, ox, oz, x + dx, y + dy, z + dz,
                            wall ? (dy == 1 || Range(ref rng, 0, 4) != 0 ? masonry : BlockId.Air)
                                 : BlockId.Air);
                }
            }
        }

        foreach (int sx in new[] { -radiusX + 1, radiusX - 1 })
        {
            foreach (int sz in new[] { -radiusZ + 1, radiusZ - 1 })
            {
                for (int dy = 1; dy <= 3; dy++)
                {
                    PutUndergroundV7(data, strideY, ox, oz, x + sx, y + dy, z + sz,
                            BlockId.Cobblestone);
                }
            }
        }

        PutUndergroundV7(data, strideY, ox, oz, x, y + 1, z, BlockId.Cobblestone);
        PutUndergroundV7(data, strideY, ox, oz, x, y + 2, z, BlockId.GoldOre);
        PutUndergroundV7(data, strideY, ox, oz, x, y + 3, z, BlockId.Torch);
        PutUndergroundV7(data, strideY, ox, oz, x + 1, y + 1, z, BlockId.IronOre);

        for (int i = 0; i < 4; i++)
        {
            int side = Range(ref rng, 0, 3);
            int px = x;
            int pz = z;
            switch (side)
            {
                case 0: px += radiusX; pz += Range(ref rng, -radiusZ + 1, radiusZ - 1); break;
                case 1: px -= radiusX; pz += Range(ref rng, -radiusZ + 1, radiusZ - 1); break;
                case 2: pz += radiusZ; px += Range(ref rng, -radiusX + 1, radiusX - 1); break;
                default: pz -= radiusZ; px += Range(ref rng, -radiusX + 1, radiusX - 1); break;
            }

            BlockId ore = Range(ref rng, 0, 5) == 0 ? BlockId.DiamondOre
                    : Range(ref rng, 0, 2) == 0 ? BlockId.GoldOre : BlockId.IronOre;
            PutUndergroundV7(data, strideY, ox, oz, px, y + Range(ref rng, 1, 3), pz, ore);
        }

        // A short 2-wide crawl connects the chamber to natural cave space often
        // enough to make these sites discoverable without exposing them above ground.
        int direction = Range(ref rng, 0, 1) == 0 ? -1 : 1;
        for (int step = 1; step <= 7; step++)
        {
            for (int dy = 1; dy <= 2; dy++)
            {
                PutUndergroundV7(data, strideY, ox, oz,
                        x + direction * (radiusX + step), y + dy, z, BlockId.Air);
            }
        }
    }

    private void PutUndergroundV7(byte[] data, int strideY, int ox, int oz,
            int x, int y, int z, BlockId id)
    {
        int lx = x - ox;
        int lz = z - oz;
        if (lx < 0 || lx >= ChunkX || lz < 0 || lz >= ChunkZ || y <= 1 || y >= Height)
        {
            return;
        }

        data[lx + lz * ChunkX + y * strideY] = (byte)id;
    }
}
