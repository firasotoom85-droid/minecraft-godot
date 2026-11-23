using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace Minecraft;

/// <summary>
/// Cross-PR regressions: frozen V9-V11 generator output, V11 determinism and
/// shoreline-only delta, append-only content IDs, new-content save round trips
/// and the wet/dry greedy merge-key identity.
/// </summary>
public static class IntegrationRegressionTests
{
    private const int Seed = 1337;

    private static readonly Vector2I[] LegacyChunks =
    [
        new(-8, -31), new(32, -24), new(0, 0),
    ];

    private static readonly string[] V9Hashes =
    [
        "50a81507699629cb6e9754e7c941c70dc93cc8f5b4f89ec0224730647c0e8e48",
        "41a79bead5db6ed65fe5a78f24a44678cf14b30e84fa0a3363c95882f1238bcc",
        "51cbfb62334b178203a6b30b9aa25c07d9f695c6a7665026a370b8fdcda963ac",
    ];

    private static readonly string[] V10Hashes =
    [
        "50a81507699629cb6e9754e7c941c70dc93cc8f5b4f89ec0224730647c0e8e48",
        "41a79bead5db6ed65fe5a78f24a44678cf14b30e84fa0a3363c95882f1238bcc",
        "51cbfb62334b178203a6b30b9aa25c07d9f695c6a7665026a370b8fdcda963ac",
    ];

    private static readonly string[] V11Hashes =
    [
        "50a81507699629cb6e9754e7c941c70dc93cc8f5b4f89ec0224730647c0e8e48",
        "d0c281ef8237224eb1f9384b4b94324e5dae8bc00666635e9efa9d8dc3509f2d",
        "51cbfb62334b178203a6b30b9aa25c07d9f695c6a7665026a370b8fdcda963ac",
    ];

    private static readonly Vector2I[] ScanChunks =
    [
        new(32, -24), new(31, -24), new(33, -24), new(32, -23), new(31, -23),
        new(33, -23), new(26, -24), new(26, -23), new(0, 0), new(-3, 2),
        new(5, -7), new(-1, -1), new(-4, 0), new(4, 1),
    ];

    public static void Run(Node host)
    {
        BlockDb.EnsureTables();
        Mesher.EnsureTables();
        CheckAppendOnlyIds();
        CheckLegacyGoldenHashes(host);
        CheckV11Determinism(host);
        CheckV11ShorelineDelta(host);
        CheckNewContentSaveRoundTrip();
        CheckWetGreedyMergeKey();
        CheckCampfireBakedEmitterLifecycle(host);
        CheckChunkLightingSolveOrderIndependence(host);
    }

    private static void CheckAppendOnlyIds()
    {
        TestAssert.Equal(59, (int)BlockId.RiverReeds,
                "RiverReeds is appended at the end of the V10 block range");
        TestAssert.Equal(61, Enum.GetValues<BlockId>().Length,
                "the block registry includes the appended ash entry after RiverReeds");
        TestAssert.Equal(108, (int)ItemId.RiverReeds,
                "RiverReeds is appended at the end of the V10 item range");
        TestAssert.Equal(110, Enum.GetValues<ItemId>().Length,
                "the item registry includes the appended ash item after RiverReeds");
        TestAssert.Check(BlockDb.IsCross(BlockId.RiverReeds)
                && !BlockDb.IsSolid(BlockId.RiverReeds),
                "the appended block is the intended non-solid cross plant");
    }

    private static void CheckLegacyGoldenHashes(Node host)
    {
        foreach ((int version, string[] expected) in new[]
                 {
                     (WorldGenV9.Version, V9Hashes),
                     (WorldGenV10.Version, V10Hashes),
                     (WorldGenV11.Version, V11Hashes),
                 })
        {
            string dir = NewWorld(version);
            VoxelWorld? world = null;
            try
            {
                GameSession.Instance.SelectWorld(dir);
                world = new VoxelWorld { Name = $"Golden V{version}", BuildCollision = false };
                host.AddChild(world);
                for (int i = 0; i < LegacyChunks.Length; i++)
                {
                    TestAssert.Equal(expected[i], HashChunk(world, LegacyChunks[i]),
                            $"generator v{version} chunk {LegacyChunks[i]} golden hash is unchanged");
                }
            }
            finally
            {
                RemoveWorld(host, world, dir);
            }
        }
    }

    private static void CheckV11Determinism(Node host)
    {
        Vector2I[] chunks =
        [
            new(-3, -2), new(2, -4), new(-1, -1), new(0, 0), new(5, -7),
            new(-8, 3), new(26, -30), new(26, -19), new(32, -24), new(33, -24),
            new(-7, 4), new(-8, -31),
        ];
        string firstDir = NewWorld(WorldGenV11.Version);
        string secondDir = NewWorld(WorldGenV11.Version);
        VoxelWorld? first = null;
        VoxelWorld? second = null;
        try
        {
            GameSession.Instance.SelectWorld(firstDir);
            first = new VoxelWorld { Name = "V11 forward", BuildCollision = false };
            host.AddChild(first);
            Dictionary<Vector2I, string> forward = [];
            foreach (Vector2I chunk in chunks)
                forward[chunk] = HashChunk(first, chunk);

            GameSession.Instance.SelectWorld(secondDir);
            second = new VoxelWorld { Name = "V11 reverse", BuildCollision = false };
            host.AddChild(second);
            for (int i = chunks.Length - 1; i >= 0; i--)
            {
                Vector2I chunk = chunks[i];
                TestAssert.Equal(forward[chunk], HashChunk(second, chunk),
                        $"V11 chunk {chunk} is identical when generated in reverse order");
            }
        }
        finally
        {
            RemoveWorld(host, first, firstDir);
            RemoveWorld(host, second, secondDir);
        }
    }

    private static void CheckV11ShorelineDelta(Node host)
    {
        string v10Dir = NewWorld(WorldGenV10.Version);
        string v11Dir = NewWorld(WorldGenV11.Version);
        VoxelWorld? v10 = null;
        VoxelWorld? v11 = null;
        try
        {
            GameSession.Instance.SelectWorld(v10Dir);
            v10 = new VoxelWorld { Name = "V10 delta baseline", BuildCollision = false };
            host.AddChild(v10);
            GameSession.Instance.SelectWorld(v11Dir);
            v11 = new VoxelWorld { Name = "V11 delta", BuildCollision = false };
            host.AddChild(v11);

            WorldGenV8 generator = new(Seed, "normal");
            int reeds = 0;
            foreach (Vector2I chunk in ScanChunks)
            {
                byte[] oldData = v10.ChunkData(chunk);
                byte[] newData = v11.ChunkData(chunk);
                TestAssert.Equal(oldData.Length, newData.Length,
                        "V10 and V11 retain the same chunk storage shape");
                for (int index = 0; index < oldData.Length; index++)
                {
                    if (oldData[index] == newData[index]) continue;
                    BlockId oldId = (BlockId)oldData[index];
                    BlockId newId = (BlockId)newData[index];
                    if (oldId != BlockId.Air || newId != BlockId.RiverReeds)
                    {
                        TestAssert.Check(false,
                                $"V11 changed {oldId}->{newId} outside the shoreline-reed delta");
                        continue;
                    }

                    reeds++;
                    int y = index / (Mesher.SX * Mesher.SZ);
                    int localZ = index / Mesher.SX % Mesher.SZ;
                    int localX = index % Mesher.SX;
                    int wx = chunk.X * Mesher.SX + localX;
                    int wz = chunk.Y * Mesher.SZ + localZ;
                    int surfaceHeight = y - 1;
                    BlockId ground = (BlockId)oldData[index - Mesher.SX * Mesher.SZ];
                    WorldGenV7.SurfaceSample sample = generator.SampleSurface(
                            wx, wz, v11.SeaLevel);
                    TestAssert.Equal(surfaceHeight, v11.SurfaceHeight(wx, wz),
                            $"reed at {wx},{wz} sits directly on the generated surface");
                    TestAssert.Check(ground is BlockId.Grass or BlockId.Sand,
                            $"reed at {wx},{wz} sits on grass or sand");
                    TestAssert.Check(WorldGenV11.IsShorelineColumn(
                                    surfaceHeight, v11.SeaLevel, sample.Biome, ground, true),
                            $"reed at {wx},{wz} obeys the shoreline column rule");
                    bool shallowNeighbor = false;
                    foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        shallowNeighbor |= WorldGenV11.IsShallowWaterNeighbor(
                                v11.SurfaceHeight(wx + dx, wz + dz), v11.SeaLevel);
                    }
                    TestAssert.Check(shallowNeighbor,
                            $"reed at {wx},{wz} has a shallow-water neighbour");
                    uint hash = generator.CoordinateHash(wx, surfaceHeight, wz, 0x52564431u);
                    TestAssert.Check(WorldGenV11.ShouldPlaceReed(hash, sample.Biome),
                            $"reed at {wx},{wz} passes the deterministic placement roll");
                }
            }

            TestAssert.Check(reeds > 0,
                    "the sampled positive and negative chunks contain V11 shoreline reeds");
            TestAssert.Equal(WorldGenV10.Version, v10.WorldGenVersion,
                    "a saved V10 world remains pinned to the V10 generator");
            TestAssert.Equal(WorldGenV11.Version, v11.WorldGenVersion,
                    "a new world selects the V11 generator");
        }
        finally
        {
            RemoveWorld(host, v10, v10Dir);
            RemoveWorld(host, v11, v11Dir);
        }
    }

    private static void CheckNewContentSaveRoundTrip()
    {
        Dictionary<string, Dictionary<string, BlockId>> edits = new()
        {
            ["4,-7"] = new Dictionary<string, BlockId>
            {
                ["1042"] = BlockId.RiverReeds,
                ["1043"] = BlockId.SpruceLeaves,
                ["1044"] = BlockId.Gravel,
            },
        };
        Dictionary<string, List<ItemStackSave>> containers = new()
        {
            ["8,42,3"] = [new ItemStackSave(0, (int)ItemId.RiverReeds, 5)],
        };
        Dictionary<string, Dictionary<string, byte>> states = new()
        {
            ["4,-7"] = new Dictionary<string, byte> { ["1042"] = 0 },
        };
        WorldSave save = new(WorldStore.WorldFormatVersion, edits, 91.5,
                Containers: containers, BlockStates: states);
        PlayerSave player = new([24.5f, 48.0f, -7.5f], 3,
                [new ItemStackSave(9, (int)ItemId.RiverReeds, 5)],
                "survival", 20.0f, 18.0f);

        string dir = $"user://tests/integration-save-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        try
        {
            TestAssert.Check(WorldStore.WriteManifest(dir, new WorldManifest(
                            "Reed save", Seed, WorldGenV11.Version,
                            WorldStore.DeriveNoiseSeeds(Seed), 1, 2, "survival", "normal")),
                    "a V11 world manifest writes through the production save path");
            WorldManifest? manifest = WorldStore.ReadManifest(dir);
            TestAssert.Equal(WorldGenV11.Version, manifest?.GenVersion ?? -1,
                    "the V11 generator version survives manifest round-trip");

            string worldPath = dir + "/world.json";
            string playerPath = dir + "/player.json";
            TestAssert.Check(WorldStore.WriteAtomic(worldPath,
                            JsonSerializer.Serialize(save, WorldStore.Json)),
                    "a save containing appended content writes atomically");
            TestAssert.Check(WorldStore.WriteAtomic(playerPath,
                            JsonSerializer.Serialize(player, WorldStore.Json)),
                    "a player save containing the appended reed item writes atomically");

            WorldSave? loaded = WorldStore.ReadJson<WorldSave>(worldPath, WorldStore.Json);
            TestAssert.Equal(WorldStore.WorldFormatVersion, loaded?.Version ?? -1,
                    "the save format remains V4 with the appended content");
            TestAssert.Equal(BlockId.RiverReeds, loaded?.Edits?["4,-7"]["1042"],
                    "RiverReeds block edits survive the V4 save round-trip");
            TestAssert.Equal(BlockId.SpruceLeaves, loaded?.Edits?["4,-7"]["1043"],
                    "V10 leaf edits survive beside the new content");
            TestAssert.Equal(5, loaded?.Containers?["8,42,3"][0].Count ?? -1,
                    "RiverReeds item stacks survive the container round-trip");
            TestAssert.Equal((int)ItemId.RiverReeds, loaded?.Containers?["8,42,3"][0].Id ?? -1,
                    "the appended reed item ID is persisted verbatim");
            TestAssert.Equal(0, loaded?.BlockStates?["4,-7"]["1042"] ?? 255,
                    "the reed block state extension remains readable");

            PlayerSave? loadedPlayer = WorldStore.ReadJson<PlayerSave>(playerPath, WorldStore.Json);
            TestAssert.Equal((int)ItemId.RiverReeds, loadedPlayer?.Items?[0].Id ?? -1,
                    "the appended reed item survives inventory round-trip");
            TestAssert.Equal(18.0f, loadedPlayer?.Hunger ?? -1.0f,
                    "hunger survives the V11 content save round-trip");
        }
        finally
        {
            RemoveWorldDirectory(dir);
        }
    }

    private static void CheckWetGreedyMergeKey()
    {
        const int height = 6;
        byte[] pad = new byte[Mesher.PLANE * height];
        byte[] statePad = new byte[Mesher.PLANE * height];
        byte[] lightPad = new byte[Mesher.PLANE * height];
        Array.Fill(lightPad, VoxelLighting.Pack(15, 0));

        int center = Mesher.PLANE * 2 + Mesher.PZ * 2 + 2;
        pad[center] = (byte)BlockId.Gravel;
        long dryKey = Mesher.FaceKey(pad, statePad, lightPad, center, 0);
        TestAssert.Check((dryKey & (1L << 56)) == 0L,
                "an air-facing gravel top has no wet merge bit");

        pad[center + Mesher.PLANE] = (byte)BlockId.Water;
        long wetKey = Mesher.FaceKey(pad, statePad, lightPad, center, 0);
        TestAssert.Check((wetKey & (1L << 56)) != 0L,
                "a water-facing gravel top carries the wet merge bit");
        TestAssert.Equal(1L << 56, wetKey ^ dryKey,
                "wetness is the only merge-key difference between wet and dry gravel");
        pad[center + Mesher.PLANE] = 0;

        pad[center] = (byte)BlockId.Stone;
        long dryStoneKey = Mesher.FaceKey(pad, statePad, lightPad, center, 0);
        pad[center + Mesher.PLANE] = (byte)BlockId.Water;
        long wetStoneKey = Mesher.FaceKey(pad, statePad, lightPad, center, 0);
        TestAssert.Equal(dryStoneKey, wetStoneKey,
                "water never changes the merge key of a non-wettable material");
    }

    private static void CheckCampfireBakedEmitterLifecycle(Node host)
    {
        string dir = NewWorld(WorldGenV11.Version);
        VoxelWorld? world = null;
        try
        {
            GameSession.Instance.SelectWorld(dir);
            world = new VoxelWorld { Name = "Campfire lighting regression", BuildCollision = false };
            host.AddChild(world);

            Vector3I? site = null;
            for (int cz = -8; cz <= 8 && site == null; cz++)
            for (int cx = -8; cx <= 8 && site == null; cx++)
                site = world.GetCampfireSite(cx, cz);

            TestAssert.Check(site.HasValue,
                    "deterministic V11 test world contains a campfire lighting fixture");
            if (site is not { } fire) return;

            Vector2I chunk = new(
                    Mathf.FloorToInt(fire.X / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(fire.Z / (float)VoxelWorld.ChunkZ));
            world.BuildPreview(new Vector3(fire.X + 0.5f, fire.Y + 1.0f, fire.Z + 0.5f), 1);
            VoxelLightData baked = world.LightData(chunk);
            int localX = fire.X - chunk.X * VoxelWorld.ChunkX;
            int localZ = fire.Z - chunk.Y * VoxelWorld.ChunkZ;
            TestAssert.Equal(VoxelLighting.CampfireEmission,
                    baked.BlockAt(localX, fire.Y + 1, localZ, world.Height),
                    "a live generated campfire contributes its source in the air cell above the opaque log");
            TestAssert.Equal((byte)(VoxelLighting.CampfireEmission - 1),
                    baked.BlockAt(localX + 1, fire.Y + 1, localZ, world.Height),
                    "campfire light escapes the opaque base and illuminates adjacent air");

            Vector3I flame = new(fire.X, fire.Y + 1, fire.Z);
            bool registered = world.NearbyLightSources(flame.X, flame.X, flame.Z, flame.Z)
                    .Exists(source => source.Position == flame
                            && source.Level == VoxelLighting.CampfireEmission);
            TestAssert.Check(registered,
                    "generated campfire emitters participate in the existing world source registry");

            TestAssert.Check(world.SetBlock(fire.X, fire.Y, fire.Z, BlockId.Stone),
                    "replacing a campfire log with another opaque block succeeds through SetBlock");
            TestAssert.Check(!world.NearbyLightSources(flame.X, flame.X, flame.Z, flame.Z)
                            .Exists(source => source.Position == flame),
                    "opaque replacement removes the virtual campfire source from the registry");
            TestAssert.Equal((byte)0,
                    world.LightData(chunk).BlockAt(localX, fire.Y + 1, localZ, world.Height),
                    "incremental replacement clears the cached flame-cell light, not the opaque base voxel");

            TestAssert.Check(world.SetBlock(fire.X, fire.Y, fire.Z, BlockId.Log),
                    "restoring the generated campfire log succeeds through SetBlock");
            TestAssert.Check(world.NearbyLightSources(flame.X, flame.X, flame.Z, flame.Z)
                            .Exists(source => source.Position == flame
                                    && source.Level == VoxelLighting.CampfireEmission),
                    "restoring the site log restores its baked source without persisted ID changes");
            TestAssert.Equal(VoxelLighting.CampfireEmission,
                    world.LightData(chunk).BlockAt(localX, fire.Y + 1, localZ, world.Height),
                    "incremental restoration lights the air cell above the campfire base");

            TestAssert.Check(world.SetBlock(fire.X, fire.Y, fire.Z, BlockId.Torch),
                    "replacing a campfire base with a torch succeeds through SetBlock");
            TestAssert.Check(world.NearbyLightSources(fire.X, fire.X, fire.Z, fire.Z)
                            .Exists(source => source.Position == fire
                                    && source.Level == BlockDb.LightEmission[(int)BlockId.Torch]),
                    "a torch placed at the campfire base emits from its own block cell");
            TestAssert.Check(!world.NearbyLightSources(flame.X, flame.X, flame.Z, flame.Z)
                            .Exists(source => source.Position == flame),
                    "converting a campfire log into a torch removes the shifted flame-cell source");
            TestAssert.Equal(BlockDb.LightEmission[(int)BlockId.Torch],
                    world.LightData(chunk).BlockAt(localX, fire.Y, localZ, world.Height),
                    "equal-strength campfire-to-torch conversion rebuilds light at the new source cell");

            TestAssert.Check(world.SetBlock(fire.X, fire.Y, fire.Z, BlockId.Log),
                    "restoring a campfire log after a torch succeeds through SetBlock");
            TestAssert.Check(world.NearbyLightSources(flame.X, flame.X, flame.Z, flame.Z)
                            .Exists(source => source.Position == flame),
                    "torch-to-campfire conversion restores the raised flame-cell source");

            TestAssert.Check(world.SetBlock(fire.X, fire.Y, fire.Z, BlockId.Air),
                    "removing a campfire log succeeds through SetBlock");
            TestAssert.Check(!world.NearbyLightSources(flame.X, flame.X, flame.Z, flame.Z)
                            .Exists(source => source.Position == flame),
                    "removing a campfire log clears its virtual emitter and prevents stale relighting");
            TestAssert.Equal((byte)0,
                    world.LightData(chunk).BlockAt(localX, fire.Y + 1, localZ, world.Height),
                    "incremental removal leaves no stale campfire light in cached chunk maps");
        }
        finally
        {
            RemoveWorld(host, world, dir);
        }
    }

    private static void CheckChunkLightingSolveOrderIndependence(Node host)
    {
        Vector2I westCenter = new(-1, 0);
        Vector2I eastCenter = Vector2I.Zero;
        string forwardDir = NewWorld(WorldGenV11.Version);
        string reverseDir = NewWorld(WorldGenV11.Version);
        VoxelWorld? forward = null;
        VoxelWorld? reverse = null;
        try
        {
            GameSession.Instance.SelectWorld(forwardDir);
            forward = new VoxelWorld { Name = "Chunk lighting west-first", BuildCollision = false };
            host.AddChild(forward);
            VoxelLightData[] forwardWest = forward.LightDataNeighborhood(westCenter);
            VoxelLightData[] forwardEast = forward.LightDataNeighborhood(eastCenter);

            GameSession.Instance.SelectWorld(reverseDir);
            reverse = new VoxelWorld { Name = "Chunk lighting east-first", BuildCollision = false };
            host.AddChild(reverse);
            VoxelLightData[] reverseEast = reverse.LightDataNeighborhood(eastCenter);
            VoxelLightData[] reverseWest = reverse.LightDataNeighborhood(westCenter);

            // Adjacent 3x3 requests overlap in two columns. Compare all six
            // shared chunks so worker completion order cannot select a
            // different baked result for the same world-space chunk.
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = 0; dx <= 1; dx++)
            {
                int westSlot = (dz + 1) * 3 + dx + 1;
                int eastSlot = (dz + 1) * 3 + dx;
                Vector2I sharedChunk = westCenter + new Vector2I(dx, dz);
                TestAssert.Check(EqualPackedLight(
                                forwardWest[westSlot], reverseEast[eastSlot]),
                        $"chunk {sharedChunk} has identical packed sky/block channels when neighbouring meshes request opposite solve orders");
                TestAssert.Check(EqualPackedLight(
                                forwardEast[eastSlot], reverseWest[westSlot]),
                        $"chunk {sharedChunk} remains identical when its neighbour-first cache is populated first");
            }

            bool beforeBoundary = forward.TrySampleCachedSkyLight(
                    new Vector3(-0.01f, 52.5f, 0.5f), out float beforeLight);
            bool afterBoundary = forward.TrySampleCachedSkyLight(
                    new Vector3(0.01f, 52.5f, 0.5f), out float afterLight);
            TestAssert.Check(beforeBoundary && afterBoundary,
                    "camera sky exposure is available on both sides of a negative-to-zero chunk seam after the neighbourhood solve");
            if (beforeBoundary && afterBoundary)
            {
                TestAssert.Check(System.Math.Abs(afterLight - beforeLight) <= 0.32f,
                        "trilinear camera exposure remains continuous across the loaded chunk seam");
            }

            TestAssert.Equal(9, forwardWest.Length,
                    "west-first solve returns its complete 3x3 packed-light snapshot");
            TestAssert.Equal(9, forwardEast.Length,
                    "east-second solve returns its complete 3x3 packed-light snapshot");
            TestAssert.Equal(9, reverseWest.Length,
                    "west-second solve returns its complete 3x3 packed-light snapshot");
            TestAssert.Equal(9, reverseEast.Length,
                    "east-first solve returns its complete 3x3 packed-light snapshot");
        }
        finally
        {
            RemoveWorld(host, forward, forwardDir);
            RemoveWorld(host, reverse, reverseDir);
        }
    }

    private static bool EqualPackedLight(VoxelLightData first, VoxelLightData second)
    {
        if (first.Packed.Length != second.Packed.Length
                || first.DirectSkyPacked.Length != second.DirectSkyPacked.Length)
            return false;
        for (int i = 0; i < first.Packed.Length; i++)
        {
            if (first.Packed[i] != second.Packed[i]) return false;
        }
        for (int i = 0; i < first.DirectSkyPacked.Length; i++)
        {
            if (first.DirectSkyPacked[i] != second.DirectSkyPacked[i]) return false;
        }
        return true;
    }

    private static string NewWorld(int version)
    {
        string dir = $"user://tests/integration-v{version}-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                $"Integration V{version}", Seed, version,
                WorldStore.DeriveNoiseSeeds(Seed), 0, 0, "survival", "normal"));
        return dir;
    }

    private static string HashChunk(VoxelWorld world, Vector2I chunk) =>
            Convert.ToHexString(SHA256.HashData(world.ChunkData(chunk))).ToLowerInvariant();

    private static void RemoveWorld(Node host, VoxelWorld? world, string dir)
    {
        if (world != null)
        {
            host.RemoveChild(world);
            world.Free();
        }

        RemoveWorldDirectory(dir);
    }

    private static void RemoveWorldDirectory(string dir)
    {
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(dir);
    }
}
