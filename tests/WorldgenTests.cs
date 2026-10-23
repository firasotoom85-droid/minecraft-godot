using System;
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
    }

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
