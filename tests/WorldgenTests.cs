using System;
using System.Diagnostics;
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
        CheckV7BiomeDistribution();
        ProfileV7ColumnSampling();
        CheckV7Determinism(host);
        CheckV7ChunkBoundaries(host);
        ProfileChunkGeneration(host);
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

    private static void CheckV7ChunkBoundaries(Node host)
    {
        const int seed = 1357911;
        string dir = $"user://tests/world-v7-boundaries-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "V7 boundaries", seed, WorldGenV7.Version,
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
                "V7 tree canopies stamp correctly across chunk seams");

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
                "V7 ore vein stamping produces a continuous ore seam across neighbouring chunks");

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
                "V7 structure template blocks are clipped consistently across chunk seams");

        host.RemoveChild(world);
        world.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void ProfileChunkGeneration(Node host)
    {
        const int seed = 7654321;
        Vector2I[] chunks =
        [
            new(-2, -2), new(-1, -2), new(0, -2), new(1, -2),
            new(-2, -1), new(-1, -1), new(0, -1), new(1, -1),
        ];
        double[] elapsed = new double[2];
        int[] versions = [6, WorldGenV7.Version];
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

        GD.Print($"WORLDGEN_CHUNK_PROFILE chunks={chunks.Length} v6_ms_per_chunk={elapsed[0]:F2} v7_ms_per_chunk={elapsed[1]:F2} ratio={(elapsed[1] / Math.Max(0.001, elapsed[0])):F2}x");
        TestAssert.Check(elapsed[0] > 0.0 && elapsed[1] > 0.0,
                "legacy and V7 chunk generation profiling completed");
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
