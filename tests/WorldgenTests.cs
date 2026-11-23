using System;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace Minecraft;

public static class WorldgenTests
{
    private const string AnchorHash = "c7fe5ea262d362606196e2fffcdc65dd130eb2df1fc58ab051fb54d3623a352a";

    public static void Run(Node host)
    {
        string v1Dir = $"user://tests/world-v1-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(v1Dir);
        WorldStore.WriteManifest(v1Dir, new WorldManifest(
                "Anchor", 1337, 1, WorldStore.LegacyNoiseSeeds, 0, 0));
        GameSession.Instance.SelectWorld(v1Dir);

        VoxelWorld v1 = new();
        host.AddChild(v1);
        TestAssert.Equal(64, v1.Height, "gen v1 keeps height 64");
        byte[] data = v1.ChunkData(new Vector2I(7, 7));
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        TestAssert.Equal(AnchorHash, hash, "v1 anchor chunk hash is unchanged");
        TestAssert.Equal(16 * 64 * 16, data.Length, "v1 chunk data size");

        CheckMesherParity(v1);

        host.RemoveChild(v1);
        v1.QueueFree();
        DirAccess.RemoveAbsolute(v1Dir + "/manifest.json");
        DirAccess.RemoveAbsolute(v1Dir);

        CheckLightPersistence(host);
        CheckWorldLightingMutations(host);
        CheckContainerPersistence(host);

        string v5Dir = $"user://tests/world-v5-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(v5Dir);
        WorldStore.WriteManifest(v5Dir, new WorldManifest(
                "V5", 777, 5, WorldStore.DeriveNoiseSeeds(777), 0, 0, "survival", null));
        GameSession.Instance.SelectWorld(v5Dir);

        VoxelWorld v5 = new();
        host.AddChild(v5);
        TestAssert.Equal(128, v5.Height, "gen v5 uses height 128");
        TestAssert.Equal(40, v5.SeaLevel, "gen v5 sea level");
        byte[] v5data = v5.ChunkData(Vector2I.Zero);
        TestAssert.Equal(16 * 128 * 16, v5data.Length, "v5 chunk data size");
        for (int x = -48; x <= 48; x += 16)
        {
            for (int z = -48; z <= 48; z += 16)
            {
                int h = v5.SurfaceHeight(x, z);
                TestAssert.Check(h >= 3 && h <= 118, $"v5 height {h} at {x},{z} is in range");
            }
        }

        host.RemoveChild(v5);
        v5.QueueFree();
        DirAccess.RemoveAbsolute(v5Dir + "/manifest.json");
        DirAccess.RemoveAbsolute(v5Dir);

        CheckLegacyGoldenHashes(host);
        CheckV7GoldenHashes(host);
        CheckV8GoldenHashes(host);
        CheckV7BiomeDistribution();
        ProfileV7ColumnSampling();
        CheckV7Determinism(host);
        CheckChunkBoundaries(host, WorldGenV7.Version);
        CheckV8BiomeDistribution();
        CheckV8TerrainQuality();
        CheckV8Determinism(host);
        CheckV8TreeDensity(host);
        CheckV8VillageGeneration(host);
        CheckChunkBoundaries(host, WorldGenV8.Version);
        ProfileChunkGeneration(host);
        CheckV12KnownHeightBiomeQueries();
        CheckV12FeatureRules();
        CheckV12WorldBoundary(host);
        CheckV12SurfaceCaveEntrance(host);
        CheckV12ChunkDeterminism(host);
    }

    private static void CheckLegacyGoldenHashes(Node host)
    {
        (int Version, int Seed, Vector2I Chunk, string Hash)[] anchors =
        [
            (3, 314159, new Vector2I(3, -2),
                    "55fa46edd0746091208552eb3d33811d7a8006253240e3052f0d471e4bb3981c"),
            (5, 271828, new Vector2I(-4, 1),
                    "283e0cac321253b8d7518e82a35b663d8a428e8ac39872fbb2649d2a6600d30f"),
            (6, 1618033, new Vector2I(5, 6),
                    "808c6e83b40e7f7b476caba9e3fd0a97fb3b0d563db9c0015a9b57b232af95ee"),
        ];

        foreach ((int version, int seed, Vector2I chunk, string expectedHash) in anchors)
        {
            string dir = $"user://tests/world-v{version}-golden-{Time.GetTicksUsec()}";
            DirAccess.MakeDirRecursiveAbsolute(dir);
            WorldStore.WriteManifest(dir, new WorldManifest(
                    $"V{version} golden", seed, version,
                    WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", null));
            GameSession.Instance.SelectWorld(dir);

            VoxelWorld world = new();
            host.AddChild(world);
            byte[] bytes = world.ChunkData(chunk);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            TestAssert.Equal(expectedHash, hash,
                    $"generator v{version} golden chunk hash remains byte-identical");
            TestAssert.Equal(version >= 5 ? 128 : 64, world.Height,
                    $"legacy v{version} keeps its original world height");

            host.RemoveChild(world);
            world.QueueFree();
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckV7GoldenHashes(Node host)
    {
        const int seed = 11235813;
        string dir = $"user://tests/world-v7-golden-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "V7 golden", seed, WorldGenV7.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        (Vector2I Chunk, string Hash)[] anchors =
        [
            (new(-8, -31), "2b817767839b3e6be4d2869a87a5b693ecc9f45ff5999e7564ade6e4bf7dc4e8"),
            (new(32, -24), "824a32b2508b44082388336abc9d870fbaa4c51c3221b8b1c86e2ccc02cac92e"),
            (new(0, 0), "7b57ebc8abfd9bd5b2f4ae1ed43ce04a26ca372e752b83d9c90ec63571d6c73b"),
        ];
        foreach ((Vector2I chunk, string expected) in anchors)
        {
            string actual = Convert.ToHexString(
                    SHA256.HashData(world.ChunkData(chunk))).ToLowerInvariant();
            TestAssert.Equal(expected, actual,
                    $"V7 golden chunk {chunk} remains byte-identical after V8 integration");
        }

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void CheckV8GoldenHashes(Node host)
    {
        const int seed = 11235813;
        string dir = $"user://tests/world-v8-golden-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "V8 golden", seed, WorldGenV8.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        (Vector2I Chunk, string Hash)[] anchors =
        [
            (new Vector2I(-8, -31), "fb838e2920caee58aea90d7ed8f5395cebc971bad3cf482cbfabf4d531e5184d"),
            (new Vector2I(32, -24), "ce826ae17057b2d089f394e5716a716c39005f9da62a5bd9a22e49056dc9da6f"),
            (new Vector2I(0, 0), "b5c21e174eb865f5a20dea280826aacfef7ad83d36bfc2e4eba4a2e0d74f7d4f"),
        ];
        foreach ((Vector2I chunk, string expected) in anchors)
        {
            string actual = Convert.ToHexString(
                    SHA256.HashData(world.ChunkData(chunk))).ToLowerInvariant();
            TestAssert.Equal(expected, actual,
                    $"V8 golden chunk {chunk} remains byte-identical");
        }

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void CheckV7BiomeDistribution()
    {
        int[] seeds = [1337, 777, 24681357, -90817263];
        int[] counts = new int[13];
        const int step = 8;
        foreach (int seed in seeds)
        {
            WorldGenV7 generator = new(seed, "normal");
            for (int z = -512; z <= 512; z += step)
            {
                for (int x = -512; x <= 512; x += step)
                {
                    WorldGenV7.SurfaceSample sample = generator.SampleSurface(x, z, 40);
                    counts[(int)sample.Biome]++;
                }
            }
        }

        string[] names =
        [
            "Plains", "Desert", "Snow", "Birch", "DenseForest", "Taiga",
            "Jungle", "Swamp", "Ocean", "Beach", "Mountain", "FlowerPlains", "Meadow",
        ];
        for (int i = 0; i < counts.Length; i++)
        {
            TestAssert.Check(counts[i] > 0, $"generated V7 worlds contain biome {names[i]}");
        }

        GD.Print($"WORLDGEN_BIOMES seeds={string.Join(",", seeds)} counts={string.Join(",", counts)}");
    }

    private static void CheckV7Determinism(Node host)
    {
        const int seed = 24681357;
        int[] seeds = WorldStore.DeriveNoiseSeeds(seed);
        string firstDir = $"user://tests/world-v7-order-a-{Time.GetTicksUsec()}";
        string secondDir = $"user://tests/world-v7-order-b-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(firstDir);
        DirAccess.MakeDirRecursiveAbsolute(secondDir);
        WorldStore.WriteManifest(firstDir, new WorldManifest(
                "V7 order A", seed, WorldGenV7.Version, seeds, 0, 0, "survival", "normal"));
        WorldStore.WriteManifest(secondDir, new WorldManifest(
                "V7 order B", seed, WorldGenV7.Version, seeds, 0, 0, "survival", "normal"));

        Vector2I[] positions =
        [
            new(-3, 2), new(2, -4), new(-1, -1), new(0, 0), new(1, 0), new(0, 1),
        ];
        string[] firstHashes = new string[positions.Length];
        GameSession.Instance.SelectWorld(firstDir);
        VoxelWorld first = new();
        host.AddChild(first);
        TestAssert.Equal(128, first.Height, "v7 retains 128-block world height");
        TestAssert.Equal(40, first.SeaLevel, "v7 retains sea level 40");
        int sampleHeight = first.SurfaceHeight(-913, 1721);
        TestAssert.Equal(sampleHeight, first.SurfaceHeight(-913, 1721),
                "v7 surface height is stable for repeated coordinate queries");
        for (int i = 0; i < positions.Length; i++)
        {
            firstHashes[i] = Convert.ToHexString(SHA256.HashData(first.ChunkData(positions[i])));
        }

        host.RemoveChild(first);
        first.QueueFree();

        GameSession.Instance.SelectWorld(secondDir);
        VoxelWorld second = new();
        host.AddChild(second);
        for (int i = positions.Length - 1; i >= 0; i--)
        {
            string hash = Convert.ToHexString(SHA256.HashData(second.ChunkData(positions[i])));
            TestAssert.Equal(firstHashes[i], hash,
                    $"v7 chunk {positions[i]} is invariant under reversed generation order");
        }

        host.RemoveChild(second);
        second.QueueFree();
        foreach (string dir in new[] { firstDir, secondDir })
        {
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckChunkBoundaries(Node host, int version)
    {
        const int seed = 1357911;
        string dir = $"user://tests/world-v{version}-boundaries-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                $"V{version} boundaries", seed, version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);

        // Search multiple chunk seams and biome regions for a tree whose
        // canopy reaches both adjacent chunks. Compare actual generated voxels.
        bool foundCrossChunkTree = false;
        for (int chunkZ = -8; chunkZ <= 8 && !foundCrossChunkTree; chunkZ++)
        {
            for (int chunkX = -8; chunkX <= 8 && !foundCrossChunkTree; chunkX++)
            {
                int boundaryX = chunkX * VoxelWorld.ChunkX;
                int originZ = chunkZ * VoxelWorld.ChunkZ;
                for (int z = originZ + 2; z < originZ + VoxelWorld.ChunkZ - 2
                        && !foundCrossChunkTree; z++)
                {
                    for (int x = boundaryX - 2; x <= boundaryX + 1
                            && !foundCrossChunkTree; x++)
                    {
                        if (world.GetTreeAt(x, z) is not { } tree)
                        {
                            continue;
                        }

                        byte[] left = world.ChunkData(new Vector2I(chunkX - 1, chunkZ));
                        byte[] right = world.ChunkData(new Vector2I(chunkX, chunkZ));
                        int localZ = z - originZ;
                        for (int y = tree.Base; y < tree.Base + tree.Trunk + 2; y++)
                        {
                            BlockId leftExpected = world.TreeBlockFor(tree, boundaryX - 1, y, z);
                            BlockId rightExpected = world.TreeBlockFor(tree, boundaryX, y, z);
                            if (leftExpected == BlockId.Air || rightExpected == BlockId.Air)
                            {
                                continue;
                            }

                            int leftIndex = 15 + localZ * 16 + y * 256;
                            int rightIndex = localZ * 16 + y * 256;
                            if ((BlockId)left[leftIndex] == leftExpected
                                    && (BlockId)right[rightIndex] == rightExpected)
                            {
                                foundCrossChunkTree = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        TestAssert.Check(foundCrossChunkTree,
                $"V{version} tree canopies stamp correctly across chunk seams");

        // Search a compact set of neighbouring chunks for a same-material ore
        // continuation crossing the seam. This exercises the neighbour-cell
        // vein stamping, not only pointwise ore noise.
        bool foundOreSeam = false;
        for (int cz = 0; cz < 3 && !foundOreSeam; cz++)
        {
            for (int cx = 0; cx < 3 && !foundOreSeam; cx++)
            {
                byte[] left = world.ChunkData(new Vector2I(cx, cz));
                byte[] right = world.ChunkData(new Vector2I(cx + 1, cz));
                int boundaryX = (cx + 1) * VoxelWorld.ChunkX;
                for (int z = 0; z < VoxelWorld.ChunkZ && !foundOreSeam; z++)
                {
                    for (int y = 2; y < world.Height - 2; y++)
                    {
                        int li = 15 + z * VoxelWorld.ChunkX + y * VoxelWorld.ChunkX * VoxelWorld.ChunkZ;
                        int ri = z * VoxelWorld.ChunkX + y * VoxelWorld.ChunkX * VoxelWorld.ChunkZ;
                        BlockId a = (BlockId)left[li];
                        BlockId b = (BlockId)right[ri];
                        if (a == b && a is BlockId.CoalOre or BlockId.IronOre
                                or BlockId.GoldOre or BlockId.DiamondOre)
                        {
                            TestAssert.Equal(a, (BlockId)world.GetBlock(boundaryX - 1, y, cz * 16 + z),
                                    "ore seam left voxel agrees with chunk data");
                            TestAssert.Equal(b, (BlockId)world.GetBlock(boundaryX, y, cz * 16 + z),
                                    "ore seam right voxel agrees with chunk data");
                            foundOreSeam = true;
                            break;
                        }
                    }
                }
            }
        }

        TestAssert.Check(foundOreSeam,
                $"V{version} ore vein stamping produces a continuous ore seam across neighbouring chunks");

        // Find a surface structure whose template crosses a chunk boundary and
        // compare the actual generated voxels on both sides to the template.
        bool foundStructureSeam = false;
        for (int cz = 0; cz < 4 && !foundStructureSeam; cz++)
        {
            for (int cx = 0; cx < 4 && !foundStructureSeam; cx++)
            {
                if (world.GetStructureSite(cx, cz) is not { } site)
                {
                    continue;
                }

                Dictionary<Vector3I, BlockId> blocks = [];
                Structures.Stamp(site, (x, y, z, id) => blocks[new Vector3I(x, y, z)] = id);
                int minX = int.MaxValue;
                int maxX = int.MinValue;
                foreach (Vector3I position in blocks.Keys)
                {
                    minX = Math.Min(minX, position.X);
                    maxX = Math.Max(maxX, position.X);
                }

                for (int boundary = (Mathf.FloorToInt(minX / 16.0f) + 1) * 16;
                        boundary <= maxX && !foundStructureSeam; boundary += 16)
                {
                    bool leftFound = false;
                    bool rightFound = false;
                    foreach ((Vector3I position, BlockId id) in blocks)
                    {
                        if (id == BlockId.Air || position.Y <= 1 || position.Y >= world.Height
                                || Math.Abs(position.X - boundary) > 7)
                        {
                            continue;
                        }

                        int chunkX = Mathf.FloorToInt(position.X / 16.0f);
                        int chunkZ = Mathf.FloorToInt(position.Z / 16.0f);
                        int localX = position.X - chunkX * 16;
                        int localZ = position.Z - chunkZ * 16;
                        byte[] chunk = world.ChunkData(new Vector2I(chunkX, chunkZ));
                        BlockId actual = (BlockId)chunk[localX + localZ * 16 + position.Y * 256];
                        if (actual != id)
                        {
                            continue;
                        }

                        if (position.X < boundary)
                        {
                            leftFound = true;
                        }
                        else
                        {
                            rightFound = true;
                        }

                        if (leftFound && rightFound)
                        {
                            foundStructureSeam = true;
                            break;
                        }
                    }
                }
            }
        }

        TestAssert.Check(foundStructureSeam,
                $"V{version} surface structure templates stamp blocks on both sides of chunk seams");

        // Resolve an underground room near a chunk boundary directly from its
        // deterministic cell seed, then verify its floor in both generated chunks.
        bool foundUndergroundSeam = false;
        for (int cz = 0; cz < 32 && !foundUndergroundSeam; cz++)
        {
            for (int cx = 0; cx < 32 && !foundUndergroundSeam; cx++)
            {
                if (!world.TryGetUndergroundStructureSiteV7(cx, cz,
                        out Vector3I site, out int kind, out _))
                {
                    continue;
                }

                int radiusX = kind == 1 ? 5 : 4;
                int radiusZ = kind == 2 ? 4 : 3;
                int modX = site.X % 16;
                int modZ = site.Z % 16;
                int boundaryX = modX <= 8 ? site.X - modX : site.X + (16 - modX);
                int boundaryZ = modZ <= 8 ? site.Z - modZ : site.Z + (16 - modZ);
                BlockId floor = kind == 0 ? BlockId.Brick : BlockId.Cobblestone;

                if (Math.Abs(site.X - boundaryX) <= radiusX)
                {
                    int chunkZ = Mathf.FloorToInt(site.Z / 16.0f);
                    int localZ = site.Z - chunkZ * 16;
                    byte[] left = world.ChunkData(new Vector2I(boundaryX / 16 - 1, chunkZ));
                    byte[] right = world.ChunkData(new Vector2I(boundaryX / 16, chunkZ));
                    BlockId a = (BlockId)left[15 + localZ * 16 + site.Y * 256];
                    BlockId b = (BlockId)right[localZ * 16 + site.Y * 256];
                    foundUndergroundSeam = a == floor && b == floor;
                }

                if (!foundUndergroundSeam && Math.Abs(site.Z - boundaryZ) <= radiusZ)
                {
                    int chunkX = Mathf.FloorToInt(site.X / 16.0f);
                    int localX = site.X - chunkX * 16;
                    byte[] back = world.ChunkData(new Vector2I(chunkX, boundaryZ / 16 - 1));
                    byte[] front = world.ChunkData(new Vector2I(chunkX, boundaryZ / 16));
                    BlockId a = (BlockId)back[localX + 15 * 16 + site.Y * 256];
                    BlockId b = (BlockId)front[localX + site.Y * 256];
                    foundUndergroundSeam = a == floor && b == floor;
                }
            }
        }

        TestAssert.Check(foundUndergroundSeam,
                $"V{version} underground chambers stamp their floor across chunk seams");

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void CheckV8BiomeDistribution()
    {
        int[] seeds = [1337, 777, 24681357, -90817263];
        int[] counts = new int[13];
        foreach (int seed in seeds)
        {
            WorldGenV8 generator = new(seed, "normal");
            for (int z = -512; z <= 512; z += 8)
            {
                for (int x = -512; x <= 512; x += 8)
                {
                    WorldGenV7.SurfaceSample sample = generator.SampleSurface(x, z, 40);
                    counts[(int)sample.Biome]++;
                }
            }
        }

        string[] names =
        [
            "Plains", "Desert", "Snow", "Birch", "DenseForest", "Taiga",
            "Jungle", "Swamp", "Ocean", "Beach", "Mountain", "FlowerPlains", "Meadow",
        ];
        for (int i = 0; i < counts.Length; i++)
        {
            TestAssert.Check(counts[i] > 0, $"V8 worlds contain biome {names[i]}");
        }

        GD.Print($"WORLDGEN_V8_BIOMES seeds={string.Join(",", seeds)} counts={string.Join(",", counts)}");
    }

    private static void CheckV8TerrainQuality()
    {
        const int seed = 11235813;
        WorldGenV7 legacy = new(seed, "normal");
        WorldGenV8 improved = new(seed, "normal");

        int oldA = legacy.SurfaceHeight(-128, -488, 40);
        int oldB = legacy.SurfaceHeight(-120, -488, 40);
        int newA = improved.SurfaceHeight(-128, -488, 40);
        int newB = improved.SurfaceHeight(-120, -488, 40);
        int oldSlope = Math.Abs(oldA - oldB);
        int newSlope = Math.Abs(newA - newB);
        GD.Print($"WORLDGEN_V8_RIVER_BANK seed={seed} v7={oldA}/{oldB} v8={newA}/{newB} slope={oldSlope}->{newSlope}");
        TestAssert.Check(newSlope < oldSlope,
                "V8 widens and softens the steep V7 river-bank transition");

        Godot.Environment legacyEnvironment = WorldEnvironmentFactory.Build(WorldGenV7.Version);
        Godot.Environment modernEnvironment = WorldEnvironmentFactory.Build(WorldGenV8.Version);
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential, legacyEnvironment.FogMode,
                "V7 retains its original exponential fog model");
        TestAssert.Equal(WorldEnvironmentFactory.LegacyAirFogDensity, legacyEnvironment.FogDensity,
                "V7 retains its original atmospheric fog density");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Depth, modernEnvironment.FogMode,
                "V8 uses camera-depth haze without changing terrain generation");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeDensity, modernEnvironment.FogDensity,
                "V8 uses the shared distance-haze profile");
        TestAssert.Check(modernEnvironment.VolumetricFogDensity
                        < legacyEnvironment.VolumetricFogDensity,
                "V8 reduces volumetric haze without changing legacy worlds");

        int oldCaveSamples = 0;
        int newCaveSamples = 0;
        foreach (int caveSeed in new[] { 1337, 777, 24681357, -90817263 })
        {
            WorldGenV7 v7 = new(caveSeed, "normal");
            WorldGenV8 v8 = new(caveSeed, "normal");
            for (int z = -256; z <= 256; z += 8)
            {
                for (int x = -256; x <= 256; x += 8)
                {
                    int surface = v7.SurfaceHeight(x, z, 40);
                    if (surface < 50)
                    {
                        continue;
                    }

                    for (int depth = 1; depth <= 10; depth += 2)
                    {
                        int y = surface - depth;
                        if (v7.IsCave(x, y, z, surface))
                        {
                            oldCaveSamples++;
                        }
                        if (v8.IsCave(x, y, z, surface))
                        {
                            newCaveSamples++;
                        }
                    }
                }
            }
        }

        GD.Print($"WORLDGEN_V8_CAVE_ENTRANCES v7_samples={oldCaveSamples} v8_samples={newCaveSamples}");
        TestAssert.Check(newCaveSamples > oldCaveSamples,
                "V8 creates additional deterministic shallow cave entrances");

        int mountainColumns = 0;
        WorldGenV8 mountainSurvey = new(777, "normal");
        for (int z = -512; z <= 512; z += 8)
        {
            for (int x = -512; x <= 512; x += 8)
            {
                if (mountainSurvey.SampleSurface(x, z, 40).Biome == Biome.Mountain)
                {
                    mountainColumns++;
                }
            }
        }
        TestAssert.Check(mountainColumns > 0,
                "V8 preserves elevated mountain regions");
    }

    private static void CheckV8Determinism(Node host)
    {
        const int seed = 24681357;
        int[] seeds = WorldStore.DeriveNoiseSeeds(seed);
        string firstDir = $"user://tests/world-v8-order-a-{Time.GetTicksUsec()}";
        string secondDir = $"user://tests/world-v8-order-b-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(firstDir);
        DirAccess.MakeDirRecursiveAbsolute(secondDir);
        WorldStore.WriteManifest(firstDir, new WorldManifest(
                "V8 order A", seed, WorldGenV8.Version, seeds, 0, 0, "survival", "normal"));
        WorldStore.WriteManifest(secondDir, new WorldManifest(
                "V8 order B", seed, WorldGenV8.Version, seeds, 0, 0, "survival", "normal"));

        Vector2I[] positions =
        [
            new(-3, 2), new(2, -4), new(-1, -1), new(0, 0), new(1, 0), new(0, 1),
        ];
        string[] hashes = new string[positions.Length];
        GameSession.Instance.SelectWorld(firstDir);
        VoxelWorld first = new();
        host.AddChild(first);
        TestAssert.Equal(128, first.Height, "V8 retains 128-block world height");
        TestAssert.Equal(40, first.SeaLevel, "V8 retains sea level 40");
        for (int i = 0; i < positions.Length; i++)
        {
            hashes[i] = Convert.ToHexString(SHA256.HashData(first.ChunkData(positions[i])));
        }
        host.RemoveChild(first);
        first.QueueFree();

        GameSession.Instance.SelectWorld(secondDir);
        VoxelWorld second = new();
        host.AddChild(second);
        for (int i = positions.Length - 1; i >= 0; i--)
        {
            string hash = Convert.ToHexString(SHA256.HashData(second.ChunkData(positions[i])));
            TestAssert.Equal(hashes[i], hash,
                    $"V8 chunk {positions[i]} is invariant under reversed generation order");
        }
        host.RemoveChild(second);
        second.QueueFree();

        foreach (string dir in new[] { firstDir, secondDir })
        {
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckV8TreeDensity(Node host)
    {
        const int seed = 24681357;
        string v7Dir = $"user://tests/world-v7-tree-density-{Time.GetTicksUsec()}";
        string v8Dir = $"user://tests/world-v8-tree-density-{Time.GetTicksUsec()}";
        foreach (string dir in new[] { v7Dir, v8Dir })
        {
            DirAccess.MakeDirRecursiveAbsolute(dir);
        }
        WorldStore.WriteManifest(v7Dir, new WorldManifest(
                "V7 tree density", seed, WorldGenV7.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        WorldStore.WriteManifest(v8Dir, new WorldManifest(
                "V8 tree density", seed, WorldGenV8.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));

        int CountTrees(string dir)
        {
            GameSession.Instance.SelectWorld(dir);
            VoxelWorld world = new();
            host.AddChild(world);
            int count = 0;
            for (int z = 104; z < 152; z++)
            {
                for (int x = 168; x < 216; x++)
                {
                    if (world.GetTreeAt(x, z) is not null)
                    {
                        count++;
                    }
                }
            }
            host.RemoveChild(world);
            world.QueueFree();
            return count;
        }

        int oldCount = CountTrees(v7Dir);
        int newCount = CountTrees(v8Dir);
        GD.Print($"WORLDGEN_V8_FOREST_TREES seed={seed} v7={oldCount} v8={newCount}");
        TestAssert.Check(newCount > oldCount,
                "V8 increases tree density in a fixed dense-forest region");

        foreach (string dir in new[] { v7Dir, v8Dir })
        {
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckV8VillageGeneration(Node host)
    {
        const int seed = 1337;
        int[] seeds = WorldStore.DeriveNoiseSeeds(seed);
        string firstDir = $"user://tests/world-v8-village-a-{Time.GetTicksUsec()}";
        string secondDir = $"user://tests/world-v8-village-b-{Time.GetTicksUsec()}";
        foreach (string dir in new[] { firstDir, secondDir })
        {
            DirAccess.MakeDirRecursiveAbsolute(dir);
            WorldStore.WriteManifest(dir, new WorldManifest(
                    "V8 village", seed, WorldGenV8.Version, seeds, 0, 0, "survival", "normal"));
        }

        GameSession.Instance.SelectWorld(firstDir);
        VoxelWorld first = new();
        host.AddChild(first);

        VillageInfo? site = null;
        for (int cellZ = -4; cellZ <= 4 && site == null; cellZ++)
        {
            for (int cellX = -4; cellX <= 4 && site == null; cellX++)
            {
                site = first.GetVillageSite(cellX, cellZ);
            }
        }

        TestAssert.Check(site is not null, "V8 seed 1337 contains a deterministic village site");
        if (site is not { } village)
        {
            host.RemoveChild(first);
            first.QueueFree();
            foreach (string dir in new[] { firstDir, secondDir })
            {
                DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
                DirAccess.RemoveAbsolute(dir);
            }
            return;
        }

        TestAssert.Check(VillageGenerator.ThemeForBiome(
                        first.BiomeAt(village.X, village.Z, village.Y)) == village.Theme,
                "village site uses the theme of its biome");
        TestAssert.Check(village.Y > first.SeaLevel + 3,
                "village site stays above the shoreline");

        // No surface structure or campfire may sit inside the settlement.
        int structureCell = VoxelWorld.StructureCell;
        int minStructureX = Mathf.FloorToInt((village.X - 52) / (float)structureCell);
        int maxStructureX = Mathf.FloorToInt((village.X + 52) / (float)structureCell);
        int minStructureZ = Mathf.FloorToInt((village.Z - 52) / (float)structureCell);
        int maxStructureZ = Mathf.FloorToInt((village.Z + 52) / (float)structureCell);
        for (int cz = minStructureZ; cz <= maxStructureZ; cz++)
        for (int cx = minStructureX; cx <= maxStructureX; cx++)
        {
            if (first.GetStructureSite(cx, cz) is not { } structure) continue;
            int dx = structure.X - village.X;
            int dz = structure.Z - village.Z;
            TestAssert.Check(dx * dx + dz * dz >= 52 * 52,
                    "village site does not overlap a surface structure");
        }

        // Generate every chunk that the settlement touches in order, then the
        // same chunks in reverse order in an independent world.
        int minChunkX = Mathf.FloorToInt((village.X - VillageStructures.Radius) / (float)VoxelWorld.ChunkX);
        int maxChunkX = Mathf.FloorToInt((village.X + VillageStructures.Radius) / (float)VoxelWorld.ChunkX);
        int minChunkZ = Mathf.FloorToInt((village.Z - VillageStructures.Radius) / (float)VoxelWorld.ChunkZ);
        int maxChunkZ = Mathf.FloorToInt((village.Z + VillageStructures.Radius) / (float)VoxelWorld.ChunkZ);
        Dictionary<Vector2I, string> orderedHashes = [];
        for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
        for (int cx = minChunkX; cx <= maxChunkX; cx++)
        {
            Vector2I chunk = new(cx, cz);
            orderedHashes[chunk] = Convert.ToHexString(SHA256.HashData(first.ChunkData(chunk)));
        }

        int chunksWithVillageBlocks = 0;
        int bedCount = 0, chestCount = 0, furnaceCount = 0, tableCount = 0, farmCount = 0, torchCount = 0;
        for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
        for (int cx = minChunkX; cx <= maxChunkX; cx++)
        {
            int chunkVillageBlocks = 0;
            int ox = cx * VoxelWorld.ChunkX;
            int oz = cz * VoxelWorld.ChunkZ;
            for (int z = oz; z < oz + VoxelWorld.ChunkZ; z++)
            for (int x = ox; x < ox + VoxelWorld.ChunkX; x++)
            for (int y = Math.Max(1, village.Y - 2); y <= Math.Min(first.Height - 1, village.Y + 8); y++)
            {
                BlockId id = first.GetBlock(x, y, z);
                switch (id)
                {
                    case BlockId.Bed: bedCount++; chunkVillageBlocks++; break;
                    case BlockId.Chest: chestCount++; chunkVillageBlocks++; break;
                    case BlockId.Furnace: furnaceCount++; chunkVillageBlocks++; break;
                    case BlockId.CraftingTable: tableCount++; chunkVillageBlocks++; break;
                    case BlockId.Farmland: farmCount++; chunkVillageBlocks++; break;
                    case BlockId.Torch: torchCount++; chunkVillageBlocks++; break;
                }
            }
            if (chunkVillageBlocks > 0) chunksWithVillageBlocks++;
        }

        TestAssert.Check(bedCount >= 4, "generated village contains resident beds");
        TestAssert.Check(chestCount >= 2, "generated village contains storage");
        TestAssert.Check(furnaceCount >= 1, "generated village contains a furnace");
        TestAssert.Check(tableCount >= 1, "generated village contains a crafting table");
        TestAssert.Check(farmCount >= 20, "generated village contains farmland");
        TestAssert.Check(torchCount >= 4, "generated village contains light posts");
        TestAssert.Check(chunksWithVillageBlocks >= 2,
                "village structures stamp across more than one chunk");

        // The well and farm must not place water above the settlement surface.
        TestAssert.Equal(BlockId.Water, first.GetBlock(village.X, village.Y + 1, village.Z),
                "village well is a contained source");
        TestAssert.Equal(BlockId.Water, first.GetBlock(village.X + 17, village.Y, village.Z + 1),
                "village farm channel sits level with the farmland");
        TestAssert.Check(first.GetBlock(village.X + 17, village.Y + 1, village.Z + 1) != BlockId.Water,
                "village farm does not place water above the crops");

        host.RemoveChild(first);
        first.QueueFree();

        GameSession.Instance.SelectWorld(secondDir);
        VoxelWorld second = new();
        host.AddChild(second);
        VillageInfo? repeated = second.GetVillageSite(village.CellX, village.CellZ);
        TestAssert.Check(repeated is { } again && again.X == village.X && again.Y == village.Y
                        && again.Z == village.Z && again.Theme == village.Theme && again.Seed == village.Seed,
                "village site is byte-identical in an independent world");

        List<Vector2I> reversed = [];
        for (int cz = maxChunkZ; cz >= minChunkZ; cz--)
        for (int cx = maxChunkX; cx >= minChunkX; cx--)
            reversed.Add(new Vector2I(cx, cz));
        foreach (Vector2I chunk in reversed)
        {
            string hash = Convert.ToHexString(SHA256.HashData(second.ChunkData(chunk)));
            TestAssert.Equal(orderedHashes[chunk], hash,
                    $"village chunk {chunk} is invariant under reversed generation order");
        }

        host.RemoveChild(second);
        second.QueueFree();
        foreach (string dir in new[] { firstDir, secondDir })
        {
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckV7TerrainProfiles()
    {
        WorldGenV7 generator = new(11235813, "normal");
        int riverChannels = 0;
        int riverBanks = 0;
        int biomeTransitions = 0;
        int mountainColumns = 0;
        int caveSamples = 0;
        int maximumEightBlockSlope = 0;
        int steepX = 0;
        int steepZ = 0;
        int steepHeight = 0;
        int steepEastHeight = 0;

        for (int z = -512; z <= 512; z += 8)
        {
            Biome previous = Biome.Plains;
            bool hasPrevious = false;
            for (int x = -512; x <= 512; x += 8)
            {
                WorldGenV7.SurfaceSample sample = generator.SampleSurface(x, z, 40);
                WorldGenV7.SurfaceSample east = generator.SampleSurface(x + 8, z, 40);
                int slope = Math.Abs(sample.Height - east.Height);
                if (slope > maximumEightBlockSlope)
                {
                    maximumEightBlockSlope = slope;
                    steepX = x;
                    steepZ = z;
                    steepHeight = sample.Height;
                    steepEastHeight = east.Height;
                }
                if (sample.Biome != east.Biome)
                {
                    biomeTransitions++;
                }

                if (hasPrevious && sample.Biome != previous)
                {
                    biomeTransitions++;
                }

                previous = sample.Biome;
                hasPrevious = true;

                if (sample.Height > 70)
                {
                    mountainColumns++;
                }

                WorldGenV7.ClimateSample climate = generator.ClimateAt(
                        x, z, sample.Height, includeRivers: true);
                if (climate.RiverStrength > 0.78f && sample.Height <= 42)
                {
                    riverChannels++;
                    WorldGenV7.SurfaceSample bankEast = generator.SampleSurface(x + 24, z, 40);
                    WorldGenV7.SurfaceSample bankWest = generator.SampleSurface(x - 24, z, 40);
                    WorldGenV7.SurfaceSample bankSouth = generator.SampleSurface(x, z + 24, 40);
                    WorldGenV7.SurfaceSample bankNorth = generator.SampleSurface(x, z - 24, 40);
                    if (Math.Max(Math.Max(bankEast.Height, bankWest.Height),
                            Math.Max(bankSouth.Height, bankNorth.Height)) >= sample.Height + 2)
                    {
                        riverBanks++;
                    }
                }

                for (int y = Math.Max(4, sample.Height - 28); y < sample.Height - 4; y += 4)
                {
                    if (generator.IsCave(x, y, z, sample.Height))
                    {
                        caveSamples++;
                    }
                }
            }
        }

        TestAssert.Check(riverChannels > 0, "V7 terrain contains carved river channels");
        TestAssert.Check(riverBanks > 0, "V7 rivers have raised bank transitions");
        TestAssert.Check(biomeTransitions > 100, "V7 biomes transition across sampled terrain");
        TestAssert.Check(mountainColumns > 0, "V7 terrain contains elevated mountain columns");
        TestAssert.Check(maximumEightBlockSlope < 40,
                "V7 mountain and terrain slopes avoid extreme eight-block cliffs");
        TestAssert.Check(caveSamples > 0, "V7 terrain contains cave passages below the surface");
        WorldGenV7.ClimateSample steepClimate = generator.ClimateAt(steepX, steepZ, steepHeight);
        WorldGenV7.ClimateSample eastClimate = generator.ClimateAt(steepX + 8, steepZ, steepEastHeight);
        GD.Print($"WORLDGEN_TERRAIN river_channels={riverChannels} raised_banks={riverBanks} biome_transitions={biomeTransitions} mountain_columns={mountainColumns} cave_samples={caveSamples} max_slope_8={maximumEightBlockSlope} steep_at=({steepX},{steepZ}) heights={steepHeight}/{steepEastHeight} river={steepClimate.RiverStrength:F2}/{eastClimate.RiverStrength:F2} land={steepClimate.Continentalness:F2}/{eastClimate.Continentalness:F2}");
    }

    private static void ProfileChunkGeneration(Node host)
    {
        const int seed = 7654321;
        Vector2I[] chunks =
        [
            new(-2, -2), new(-1, -2), new(0, -2), new(1, -2),
            new(-2, -1), new(-1, -1), new(0, -1), new(1, -1),
        ];
        double[] elapsed = new double[4];
        int[] versions = [6, WorldGenV7.Version, WorldGenV8.Version, WorldGenV12.Version];
        for (int v = 0; v < versions.Length; v++)
        {
            int version = versions[v];
            string dir = $"user://tests/world-v{version}-profile-{Time.GetTicksUsec()}";
            DirAccess.MakeDirRecursiveAbsolute(dir);
            WorldStore.WriteManifest(dir, new WorldManifest(
                    $"V{version} profile", seed, version,
                    WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
            GameSession.Instance.SelectWorld(dir);
            VoxelWorld world = new();
            host.AddChild(world);
            // Warm the generator, noise wrappers and chunk-data path before
            // timing, so the V8/V7 comparison excludes first-use JIT cost.
            _ = world.ChunkData(new Vector2I(20, 20));
            Stopwatch timer = Stopwatch.StartNew();
            foreach (Vector2I chunk in chunks)
            {
                _ = world.ChunkData(chunk);
            }

            timer.Stop();
            elapsed[v] = timer.Elapsed.TotalMilliseconds / chunks.Length;
            host.RemoveChild(world);
            world.QueueFree();
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }

        GD.Print($"WORLDGEN_CHUNK_PROFILE chunks={chunks.Length} v6_ms_per_chunk={elapsed[0]:F2} v7_ms_per_chunk={elapsed[1]:F2} v8_ms_per_chunk={elapsed[2]:F2} v12_ms_per_chunk={elapsed[3]:F2} v12_vs_v8={(elapsed[3] / Math.Max(0.001, elapsed[2])):F2}x");
        TestAssert.Check(elapsed[0] > 0.0 && elapsed[1] > 0.0
                        && elapsed[2] > 0.0 && elapsed[3] > 0.0,
                "V6, V7, V8 and V12 chunk generation profiling completed");
    }

    private static void ProfileV7ColumnSampling()
    {
        WorldGenV7 generator = new(424242, "normal");
        const int iterations = 4096;
        for (int i = 0; i < 128; i++)
        {
            generator.SampleSurface(i, i * 3, 40);
        }

        Stopwatch separate = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            int x = (i & 63) * 7 - 224;
            int z = (i >> 6) * 7 - 224;
            _ = generator.SurfaceHeight(x, z, 40);
            _ = generator.BiomeAt(x, z, 40, 40);
        }

        separate.Stop();
        Stopwatch combined = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            int x = (i & 63) * 7 - 224;
            int z = (i >> 6) * 7 - 224;
            _ = generator.SampleSurface(x, z, 40);
        }

        combined.Stop();
        GD.Print($"WORLDGEN_PROFILE columns={iterations} separate_ms={separate.Elapsed.TotalMilliseconds:F2} combined_ms={combined.Elapsed.TotalMilliseconds:F2} speedup={(separate.Elapsed.TotalMilliseconds / Math.Max(0.001, combined.Elapsed.TotalMilliseconds)):F2}x");
        TestAssert.Check(combined.ElapsedTicks > 0 && separate.ElapsedTicks > 0,
                "V7 column sampling benchmark completed");
    }

    // Profiling is informational: CI timing is not used as a hard performance gate.
    /// <summary>A saved torch edit must repopulate the light registry on load.</summary>
    private static void CheckLightPersistence(Node host)
    {
        string dir = $"user://tests/world-light-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Light", 1337, 1, WorldStore.LegacyNoiseSeeds, 0, 0));

        // Chunk (0,0), local (1,10,1): index = x + z*16 + y*256.
        int index = 1 + 1 * 16 + 10 * 16 * 16;
        int cropIndex = 2 + 1 * 16 + 10 * 16 * 16;
        WorldSave save = new(WorldStore.WorldFormatVersion, new Dictionary<string, Dictionary<string, BlockId>>
        {
            ["0,0"] = new Dictionary<string, BlockId>
            {
                [index.ToString()] = BlockId.Torch,
                [cropIndex.ToString()] = BlockId.Wheat1,
            },
        }, 0.5);
        WorldStore.WriteAtomic(WorldStore.WorldSavePath(dir), JsonSerializer.Serialize(save, WorldStore.Json));

        GameSession.Instance.SelectWorld(dir);
        VoxelWorld world = new();
        host.AddChild(world);
        List<Vector3I> lights = world.NearestLights(new Vector3(1.5f, 10.5f, 1.5f), 4);
        TestAssert.Equal(1, lights.Count, "saved torch repopulates the light registry");
        Vector3I[] crops = world.CropSnapshot();
        TestAssert.Equal(1, crops.Length, "saved wheat repopulates the crop registry");
        if (crops.Length == 1)
        {
            TestAssert.Equal(2, crops[0].X, "crop x");
            TestAssert.Equal(10, crops[0].Y, "crop y");
            TestAssert.Equal(1, crops[0].Z, "crop z");
        }
        if (lights.Count == 1)
        {
            TestAssert.Equal(1, lights[0].X, "light x");
            TestAssert.Equal(10, lights[0].Y, "light y");
            TestAssert.Equal(1, lights[0].Z, "light z");
        }

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    /// <summary>
    /// Exercises lighting through the real world mutation/cache path rather
    /// than only testing the standalone flood-fill solver.
    /// </summary>
    private static void CheckWorldLightingMutations(Node host)
    {
        string dir = $"user://tests/world-light-mutations-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Light mutations", 4242, 1, WorldStore.LegacyNoiseSeeds, 0, 0));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(new Vector3(8, 32, 8), 1);

        int y = world.Height - 3;
        byte torchLevel = BlockDb.LightEmission[(int)BlockId.Torch];
        Vector2I westChunk = Vector2I.Zero;

        TestAssert.Equal(BlockId.Air, world.GetBlock(8, y, 8),
                "world-light test location is open air");
        TestAssert.Check(world.SetBlock(8, y, 8, BlockId.Torch),
                "world accepts torch placement");
        VoxelLightData placed = world.LightData(westChunk);
        TestAssert.Equal(torchLevel, placed.BlockAt(8, y, 8, world.Height),
                "placed torch retains its emission in world cache");
        TestAssert.Equal((byte)(torchLevel - 1), placed.BlockAt(9, y, 8, world.Height),
                "placed torch illuminates adjacent world voxel");

        TestAssert.Check(world.SetBlock(8, y, 8, BlockId.Air),
                "world accepts torch removal");
        VoxelLightData removed = world.LightData(westChunk);
        TestAssert.Equal((byte)0, removed.BlockAt(9, y, 8, world.Height),
                "torch removal clears stale world block light");

        TestAssert.Check(world.SetBlock(8, y, 8, BlockId.Torch), "first overlapping torch placed");
        TestAssert.Check(world.SetBlock(12, y, 8, BlockId.Torch), "second overlapping torch placed");
        TestAssert.Check(world.SetBlock(8, y, 8, BlockId.Air), "first overlapping torch removed");
        VoxelLightData overlap = world.LightData(westChunk);
        TestAssert.Equal((byte)(torchLevel - 2), overlap.BlockAt(10, y, 8, world.Height),
                "overlapping source keeps light after the nearer source is removed");
        TestAssert.Check(world.SetBlock(12, y, 8, BlockId.Air), "remaining overlapping torch removed");

        TestAssert.Check(world.SetBlock(15, y, 8, BlockId.Torch),
                "boundary torch placed in western chunk");
        VoxelLightData[] seam = world.LightDataNeighborhood(Vector2I.Zero);
        TestAssert.Equal((byte)(torchLevel - 1), seam[5].BlockAt(0, y, 8, world.Height),
                "world block light crosses x=15/16 chunk boundary");
        TestAssert.Check(world.SetBlock(15, y, 8, BlockId.Air), "boundary torch removed");

        int roofY = world.Height - 2;
        TestAssert.Check(world.SetBlock(6, roofY, 6, BlockId.Stone),
                "opaque skylight blocker placed");
        VoxelLightData shaded = world.LightData(westChunk);
        TestAssert.Check(shaded.SkyAt(6, roofY - 1, 6, world.Height) < 15,
                "placing opaque block reduces direct skylight below it");
        TestAssert.Check(world.SetBlock(6, roofY, 6, BlockId.Air),
                "opaque skylight blocker removed");
        VoxelLightData exposed = world.LightData(westChunk);
        TestAssert.Equal((byte)15, exposed.SkyAt(6, roofY - 1, 6, world.Height),
                "removing opaque block restores direct skylight");

        TestAssert.Check(world.SetBlock(8, y, 8, BlockId.Torch),
                "persistent torch placed before unload");
        world.SaveNow();
        MethodInfo? unload = typeof(VoxelWorld).GetMethod(
                "UnloadFar", BindingFlags.Instance | BindingFlags.NonPublic);
        TestAssert.Check(unload is not null, "world exposes its existing unload path to regression test");
        unload?.Invoke(world, [new Vector2I(100, 100)]);
        world.BuildPreview(new Vector3(8, 32, 8), 1);
        VoxelLightData reloaded = world.LightData(westChunk);
        TestAssert.Equal(torchLevel, reloaded.BlockAt(8, y, 8, world.Height),
                "reloaded chunk restores saved torch emission");
        TestAssert.Equal((byte)(torchLevel - 1), reloaded.BlockAt(9, y, 8, world.Height),
                "reloaded chunk restores propagated block light");

        host.RemoveChild(world);
        world.QueueFree();
        foreach (string suffix in new[] { "", ".bak", ".bak2" })
        {
            DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir) + suffix);
            DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir) + suffix);
        }
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    /// <summary>A saved chest must load, accept changes, and survive SaveNow.</summary>
    private static void CheckContainerPersistence(Node host)
    {
        string dir = $"user://tests/world-chest-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Chest", 1337, 1, WorldStore.LegacyNoiseSeeds, 0, 0));

        WorldSave save = new(WorldStore.WorldFormatVersion, null, 0.5,
                new Dictionary<string, List<ItemStackSave>>
                {
                    ["1,2,3"] = [new ItemStackSave(0, (int)ItemId.Stone, 12)],
                },
                null,
                new Dictionary<string, FurnaceSave>
                {
                    ["4,5,6"] = new(
                            [new ItemStackSave(0, (int)ItemId.RawIron, 2), new ItemStackSave(1, (int)ItemId.Coal, 1)],
                            12.0f, 80.0f, 3.0f),
                });
        WorldStore.WriteAtomic(WorldStore.WorldSavePath(dir), JsonSerializer.Serialize(save, WorldStore.Json));

        GameSession.Instance.SelectWorld(dir);
        VoxelWorld world = new();
        host.AddChild(world);
        if (world.TryGetFurnace(new Vector3I(4, 5, 6), out FurnaceData? furnace) && furnace is not null)
        {
            TestAssert.Equal(ItemId.RawIron, furnace.Input.Id, "saved furnace loads its input");
            TestAssert.Equal(12.0f, furnace.BurnTime, "saved furnace loads its burn timer");
        }
        else
        {
            TestAssert.Check(false, "saved furnace is reachable");
        }

        if (world.TryGetContainer(new Vector3I(1, 2, 3), out Container? chest) && chest is not null)
        {
            TestAssert.Equal(12, chest.Slots[0].Count, "saved chest loads its contents");
            chest.Add(ItemId.Torch, 2);
            world.SaveNow();

            WorldSave? reloaded = WorldStore.ReadJson<WorldSave>(
                    WorldStore.WorldSavePath(dir), WorldStore.Json);
            List<ItemStackSave>? items = reloaded?.Containers?["1,2,3"];
            TestAssert.Check(items is not null, "saved chest survives SaveNow");
            if (items is not null)
            {
                TestAssert.Equal(12, items[0].Count, "chest stone survives SaveNow");
                TestAssert.Equal(2, items[1].Count, "chest torch survives SaveNow");
            }

            FurnaceSave? savedFurnace = reloaded?.Furnaces?["4,5,6"];
            TestAssert.Check(savedFurnace is not null, "furnace survives SaveNow");
            if (savedFurnace is not null)
            {
                TestAssert.Equal(ItemId.RawIron, (ItemId)savedFurnace.Slots[0].Id, "furnace input survives SaveNow");
                TestAssert.Equal(12.0f, savedFurnace.BurnTime, "furnace burn timer survives SaveNow");
            }
        }
        else
        {
            TestAssert.Check(false, "saved chest is reachable");
        }

        host.RemoveChild(world);
        world.QueueFree();
        foreach (string suffix in new[] { "", ".bak", ".bak2" })
        {
            DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir) + suffix);
            DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir) + suffix);
        }

        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    /// <summary>
    /// Greedy and per-face meshing must cover exactly the same surface area per
    /// face normal. This catches off-by-one slices and dropped bottom faces.
    /// </summary>
    private static void CheckMesherParity(VoxelWorld world)
    {
        bool oldGreedy = Mesher.Greedy;
        Mesher.Greedy = false;
        MeshPayload perFace = Mesher.Build(world, new Vector2I(7, 7));
        Dictionary<string, double> faceAreas = Areas(perFace);
        PayloadPool.Release(perFace);

        Mesher.Greedy = true;
        MeshPayload greedy = Mesher.Build(world, new Vector2I(7, 7));
        Dictionary<string, double> greedyAreas = Areas(greedy);
        PayloadPool.Release(greedy);
        Mesher.Greedy = oldGreedy;

        foreach ((string key, double area) in faceAreas)
        {
            greedyAreas.TryGetValue(key, out double actual);
            TestAssert.Check(Math.Abs(area - actual) < 0.001,
                    $"mesher area parity normal {key}: per-face {area:F3}, greedy {actual:F3}");
        }

        foreach ((string key, double area) in greedyAreas)
        {
            TestAssert.Check(faceAreas.ContainsKey(key),
                    $"greedy mesher invented normal {key} with area {area:F3}");
        }
    }

    private static void CheckV12KnownHeightBiomeQueries()
    {
        const int x = 127;
        const int z = -311;
        foreach (string preset in new[] { "normal", "flat", "amplified", "sky" })
        {
            WorldGenV7 v7 = new(1337, preset);
            WorldGenV7.SurfaceSample sampleV7 = v7.SampleSurface(x, z, 40);
            TestAssert.Equal(sampleV7.Biome, v7.BiomeAt(x, z, sampleV7.Height, 40),
                    $"V7 {preset} known-height biome matches its sampled surface");
            TestAssert.Equal(Biome.Beach, v7.BiomeAt(x, z, 42, 40),
                    $"V7 {preset} known height selects beach without resampling terrain");
            TestAssert.Equal(Biome.Ocean, v7.BiomeAt(x, z, 36, 40),
                    $"V7 {preset} known height selects ocean without resampling terrain");

            WorldGenV8 v8 = new(1337, preset);
            WorldGenV7.SurfaceSample sampleV8 = v8.SampleSurface(x, z, 40);
            TestAssert.Equal(sampleV8.Biome, v8.BiomeAt(x, z, sampleV8.Height, 40),
                    $"V8 {preset} known-height biome matches its sampled surface");
            TestAssert.Equal(Biome.Beach, v8.BiomeAt(x, z, 42, 40),
                    $"V8 {preset} known height selects beach without resampling terrain");
            TestAssert.Equal(Biome.Ocean, v8.BiomeAt(x, z, 36, 40),
                    $"V8 {preset} known height selects ocean without resampling terrain");
        }
    }

    private static void CheckV12FeatureRules()
    {
        WorldGenV8 seedA = new(1337, "normal");
        WorldGenV8 seedB = new(7331, "normal");
        bool seedChangesCactusHash = false;
        for (int z = -4; z <= 4 && !seedChangesCactusHash; z++)
        {
            for (int x = -4; x <= 4; x++)
            {
                uint first = WorldGenV12.CactusHash(seedA, x, z);
                uint second = WorldGenV12.CactusHash(seedB, x, z);
                if (first != second)
                {
                    seedChangesCactusHash = true;
                    break;
                }
            }
        }

        TestAssert.Check(seedChangesCactusHash,
                "V12 cactus placement hash changes with the world seed");
        TestAssert.Equal(WorldGenV12.CactusHash(seedA, 17, -29),
                WorldGenV12.CactusHash(seedA, 17, -29),
                "V12 cactus placement hash is deterministic for a fixed seed and coordinate");

        TestAssert.Check(WorldGenV12.ShouldCarveSurfaceEntrance(
                        55, 55, 40, "normal", true),
                "V12 opens a dry surface voxel above an existing cave entrance");
        TestAssert.Check(!WorldGenV12.ShouldCarveSurfaceEntrance(
                        54, 55, 40, "normal", true),
                "V12 does not extend the surface entrance rule below the surface");
        TestAssert.Check(!WorldGenV12.ShouldCarveSurfaceEntrance(
                        42, 42, 40, "normal", true),
                "V12 protects ocean and shoreline columns from surface cave openings");
        TestAssert.Check(!WorldGenV12.ShouldCarveSurfaceEntrance(
                        55, 55, 40, "flat", true),
                "V12 keeps flat worlds sealed");
        TestAssert.Check(!WorldGenV12.ShouldCarveSurfaceEntrance(
                        55, 55, 40, "normal", false),
                "V12 does not open a surface without a cave directly below");

        Span<Vector3I> visited = stackalloc Vector3I[2];
        Span<byte> occupied = stackalloc byte[2];
        occupied.Clear();
        Vector3I firstPosition = new(7, 24, -3);
        TestAssert.Check(WorldGenV12.TryRecordUniqueOrePosition(
                        visited, occupied, firstPosition),
                "V12 ore walk records its first candidate");
        TestAssert.Check(!WorldGenV12.TryRecordUniqueOrePosition(
                        visited, occupied, firstPosition),
                "V12 ore walk rejects a revisited candidate");
        TestAssert.Check(WorldGenV12.TryRecordUniqueOrePosition(
                        visited, occupied, new Vector3I(8, 24, -3)),
                "V12 ore walk records a distinct candidate");
        TestAssert.Check(!WorldGenV12.TryRecordUniqueOrePosition(
                        visited, occupied, new Vector3I(9, 24, -3)),
                "V12 ore walk stops recording when its fixed visit table is full");

        TestAssert.Equal(BlockId.Leaves,
                WorldGenV12.MergeTreeBlock(BlockId.Air, BlockId.Leaves),
                "V12 tree canopy still fills air");
        TestAssert.Equal(BlockId.Log,
                WorldGenV12.MergeTreeBlock(BlockId.Leaves, BlockId.Log),
                "V12 trunk replaces a leaf from an earlier canopy");
        TestAssert.Equal(BlockId.Log,
                WorldGenV12.MergeTreeBlock(BlockId.Log, BlockId.Leaves),
                "V12 later canopy leaves cannot replace a trunk");
        TestAssert.Equal(BlockId.Stone,
                WorldGenV12.MergeTreeBlock(BlockId.Stone, BlockId.BirchLog),
                "V12 tree stamping retains the existing air-only rule for non-leaf blocks");
        TestAssert.Equal(BlockId.Log,
                WorldGenV12.MergeTreeBlock(BlockId.Log, BlockId.BirchLog),
                "V12 overlapping trunks do not overwrite an existing log");

        const int limit = WorldGenV12.HorizontalBlockLimit;
        TestAssert.Check(WorldGenV12.ContainsWorldPosition(-limit, 0),
                "V12 includes the negative horizontal world boundary");
        TestAssert.Check(WorldGenV12.ContainsWorldPosition(limit - 1, 0),
                "V12 includes the last positive horizontal block");
        TestAssert.Check(!WorldGenV12.ContainsWorldPosition(limit, 0),
                "V12 excludes the positive horizontal world boundary");
        TestAssert.Check(!WorldGenV12.ContainsWorldPosition(-limit - 1, 0),
                "V12 excludes coordinates below the negative horizontal boundary");
        TestAssert.Check(WorldGenV12.ContainsChunk(new Vector2I(-limit / 16, 0)),
                "V12 includes the first supported negative chunk");
        TestAssert.Check(WorldGenV12.ContainsChunk(new Vector2I(limit / 16 - 1, 0)),
                "V12 includes the last supported positive chunk");
        TestAssert.Check(!WorldGenV12.ContainsChunk(new Vector2I(limit / 16, 0)),
                "V12 rejects the first positive out-of-range chunk");
        TestAssert.Check(!WorldGenV12.ContainsChunk(new Vector2I(-limit / 16 - 1, 0)),
                "V12 rejects the first negative out-of-range chunk");

        float lastBlock = limit - 1;
        float halfStep = lastBlock + 0.5f;
        TestAssert.Check(halfStep > lastBlock && halfStep < limit,
                "V12 horizontal extent preserves half-block float precision at its edge");
    }

    private static void CheckV12WorldBoundary(Node host)
    {
        const int seed = 27182818;
        string dir = $"user://tests/world-v12-boundary-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "V12 boundary", seed, WorldGenV12.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        int limit = WorldGenV12.HorizontalBlockLimit;
        TestAssert.Equal(WorldGenV12.Version, world.WorldGenVersion,
                "V12 boundary test loads the requested generator version");
        TestAssert.Equal(BlockId.Bedrock, world.GetBlock(limit, 64, 0),
                "V12 block reads treat the positive world edge as bedrock");
        TestAssert.Equal(BlockId.Bedrock, world.GetBlock(-limit - 1, 64, 0),
                "V12 block reads treat the negative world edge as bedrock");
        TestAssert.Check(world.TryGetLoadedBlock(limit, 64, 0, out BlockId boundary)
                        && boundary == BlockId.Bedrock,
                "V12 loaded-block reads expose the immutable boundary");
        TestAssert.Check(!world.SetBlock(limit, 64, 0, BlockId.Stone),
                "V12 block mutations cannot cross the positive world edge");

        foreach (Vector2I outside in new[]
                {
                    new Vector2I(limit / VoxelWorld.ChunkX, 0),
                    new Vector2I(-limit / VoxelWorld.ChunkX - 1, 0),
                })
        {
            byte[] blocks = world.ChunkData(outside);
            bool allBedrock = true;
            foreach (byte block in blocks)
            {
                if ((BlockId)block != BlockId.Bedrock)
                {
                    allBedrock = false;
                    break;
                }
            }

            TestAssert.Check(allBedrock,
                    $"V12 out-of-range chunk {outside} is a solid bedrock boundary");
        }

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void CheckV12SurfaceCaveEntrance(Node host)
    {
        const int seed = 1337;
        WorldGenV8 generator = new(seed, "normal");
        int entranceX = 0;
        int entranceZ = 0;
        int entranceHeight = 0;
        bool found = false;
        for (int z = -512; z <= 512 && !found; z += 4)
        {
            for (int x = -512; x <= 512; x += 4)
            {
                WorldGenV7.SurfaceSample surface = generator.SampleSurface(x, z, 40);
                if (surface.Height <= 42
                        || !generator.IsCave(x, surface.Height - 1, z, surface.Height))
                {
                    continue;
                }

                entranceX = x;
                entranceZ = z;
                entranceHeight = surface.Height;
                found = true;
                break;
            }
        }

        TestAssert.Check(found,
                "fixed V8 seed provides a dry column with a shallow cave entrance");
        if (!found)
        {
            return;
        }

        string dir = $"user://tests/world-v12-cave-mouth-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "V12 cave entrance", seed, WorldGenV12.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        TestAssert.Check(world.IsCave(entranceX, entranceHeight - 1,
                        entranceZ, entranceHeight),
                "V12 retains the cave block directly below the surface");
        TestAssert.Check(world.IsCave(entranceX, entranceHeight,
                        entranceZ, entranceHeight),
                "V12 exposes the surface voxel above that cave");
        TestAssert.Equal(BlockId.Air,
                world.GetBlock(entranceX, entranceHeight, entranceZ),
                "V12 block generation leaves the cave mouth open to the surface");

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void CheckV12ChunkDeterminism(Node host)
    {
        const int seed = 24681357;
        string firstDir = $"user://tests/world-v12-order-a-{Time.GetTicksUsec()}";
        string secondDir = $"user://tests/world-v12-order-b-{Time.GetTicksUsec()}";
        foreach (string dir in new[] { firstDir, secondDir })
        {
            DirAccess.MakeDirRecursiveAbsolute(dir);
            WorldStore.WriteManifest(dir, new WorldManifest(
                    "V12 deterministic", seed, WorldGenV12.Version,
                    WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        }

        Vector2I[] positions =
        [
            new(-3, 2), new(2, -4), new(-1, -1), new(0, 0), new(1, 0), new(0, 1),
        ];
        string[] hashes = new string[positions.Length];
        GameSession.Instance.SelectWorld(firstDir);
        VoxelWorld first = new();
        host.AddChild(first);
        for (int i = 0; i < positions.Length; i++)
        {
            hashes[i] = Convert.ToHexString(SHA256.HashData(first.ChunkData(positions[i])));
        }

        host.RemoveChild(first);
        first.QueueFree();

        GameSession.Instance.SelectWorld(secondDir);
        VoxelWorld second = new();
        host.AddChild(second);
        for (int i = positions.Length - 1; i >= 0; i--)
        {
            string hash = Convert.ToHexString(SHA256.HashData(second.ChunkData(positions[i])));
            TestAssert.Equal(hashes[i], hash,
                    $"V12 chunk {positions[i]} is invariant under reversed generation order");
        }

        host.RemoveChild(second);
        second.QueueFree();

        foreach (string dir in new[] { firstDir, secondDir })
        {
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static Dictionary<string, double> Areas(MeshPayload payload)
    {
        Dictionary<string, double> areas = [];
        foreach (SurfaceData surface in new[] { payload.Opaque, payload.Glass, payload.Water, payload.Lava })
        {
            // The mesher emits each quad as six triangle vertices (TriOrder).
            for (int i = 0; i + 5 < surface.Vertex.Count; i += 6)
            {
                for (int t = 0; t < 6; t += 3)
                {
                    Vector3 a = surface.Vertex[i + t];
                    Vector3 b = surface.Vertex[i + t + 1];
                    Vector3 c = surface.Vertex[i + t + 2];
                    double area = (b - a).Cross(c - a).Length() * 0.5;
                    Vector3 n = surface.Normal[i];
                    string key = $"{Mathf.RoundToInt(n.X)},{Mathf.RoundToInt(n.Y)},{Mathf.RoundToInt(n.Z)}";
                    areas[key] = areas.GetValueOrDefault(key) + area;
                }
            }
        }

        return areas;
    }
}
