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
    }

    private static void CheckAppendOnlyIds()
    {
        TestAssert.Equal(59, (int)BlockId.RiverReeds,
                "RiverReeds is appended at the end of the V10 block range");
        TestAssert.Equal(60, Enum.GetValues<BlockId>().Length,
                "the block registry grew by exactly the appended reed entry");
        TestAssert.Equal(108, (int)ItemId.RiverReeds,
                "RiverReeds is appended at the end of the V10 item range");
        TestAssert.Equal(109, Enum.GetValues<ItemId>().Length,
                "the item registry grew by exactly the appended reed entry");
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
