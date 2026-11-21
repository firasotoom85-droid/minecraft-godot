using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Minecraft;

/// <summary>
/// Cross-system regressions. These intentionally exercise more than one feature
/// at once: persisted ids, the v4 world save, quests, fluids and block shapes.
/// </summary>
public static class IntegrationTests
{
    public static void Run(Node host)
    {
        CheckCombinedWorldSaveRoundTrip(host);
        CheckQuestSystemRewards();
        CheckFluidShapeBoundary(host);
        CheckVillageResidentSpawning(host);
    }

    private static void CheckCombinedWorldSaveRoundTrip(Node host)
    {
        const int seed = 20261121;
        string dir = $"user://tests/integration-save-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Integration save", seed, WorldGenV8.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(Vector3.Zero, 1);

        // Clear an isolated volume so terrain cannot satisfy a shape assertion.
        for (int y = 90; y <= 93; y++)
        for (int z = 1; z <= 4; z++)
        for (int x = 1; x <= 5; x++)
            world.SetBlock(x, y, z, BlockId.Air);

        TestAssert.Check(world.SetBlock(2, 90, 2, BlockId.PlanksSlab, BlockShapes.SlabTop),
                "combined world accepts an upper slab edit");
        TestAssert.Check(world.SetBlock(3, 90, 2, BlockId.BrickStairs, 5),
                "combined world accepts an upside-down stair edit");

        VillagerSave resident = new("village:0:0/resident-0", "Alden Ashford",
                (int)VillagerProfession.Farmer,
                [2.5f, 91.0f, 2.5f], [2.5f, 91.0f, 2.5f], [3.5f, 91.0f, 2.5f],
                18.0f, 4);
        world.Villagers[resident.Id] = resident;
        world.Quests["village/farmers-harvest"] = new QuestProgressSave(1, 0);
        world.SavedBoats =
        [
            new BoatSave([6.5f, SeaLevelForTest(world), 2.5f], 0.5f,
                    [0.0f, 0.0f, 0.0f], false),
        ];
        world.TimeOfDay = 0.42;
        world.SaveNow();

        string json = FileAccess.GetFileAsString(WorldStore.WorldSavePath(dir));
        TestAssert.Check(json.Contains("\"blockStates\""), "combined save writes block states");
        TestAssert.Check(json.Contains("\"villagers\""), "combined save writes villagers");
        TestAssert.Check(json.Contains("\"quests\""), "combined save writes quest progress");
        TestAssert.Check(json.Contains("\"boats\""), "combined save writes boats");

        host.RemoveChild(world);
        world.Free();

        GameSession.Instance.SelectWorld(dir);
        VoxelWorld loaded = new();
        host.AddChild(loaded);
        loaded.BuildPreview(Vector3.Zero, 1);
        TestAssert.Equal(BlockShapes.SlabTop, loaded.GetBlockState(2, 90, 2),
                "combined save reloads slab orientation");
        TestAssert.Equal((byte)5, loaded.GetBlockState(3, 90, 2),
                "combined save reloads stair orientation");
        TestAssert.Equal("Alden Ashford", loaded.Villagers[resident.Id].Name,
                "combined save reloads the resident");
        TestAssert.Equal(1, loaded.Quests["village/farmers-harvest"].Stage,
                "combined save reloads the quest stage");
        TestAssert.Check(loaded.SavedBoats is { Count: 1 }
                        && Mathf.IsEqualApprox(loaded.SavedBoats[0].Yaw, 0.5f),
                "combined save reloads boat pose");
        TestAssert.Check(Mathf.IsEqualApprox(0.42f, (float)loaded.TimeOfDay),
                "combined save reloads the world clock");

        // A stale SavedBoats snapshot must not resurrect a removed boat when a
        // BoatSystem owns the live vehicle list.
        Player boatPlayer = new();
        loaded.SavedBoats = null;
        BoatSystem emptyBoats = new();
        host.AddChild(emptyBoats);
        emptyBoats.Setup(loaded, boatPlayer, null!);
        TestAssert.Check(emptyBoats.ToSave() is null,
                "an empty live boat system has no vehicles to save");
        // Simulate a stale field left by an earlier load. The live system, not
        // this snapshot, must own the boats written by the next save.
        loaded.SavedBoats = [new BoatSave([99.0f, 50.0f, 99.0f], 0.0f,
                [0.0f, 0.0f, 0.0f], false)];
        loaded.BoatSystem = emptyBoats;
        loaded.SaveNow();
        WorldSave? rewritten = WorldStore.ReadJson<WorldSave>(
                WorldStore.WorldSavePath(dir), WorldStore.Json);
        TestAssert.Check(rewritten?.Boats is null,
                "live boat state replaces a stale loaded boat snapshot");

        host.RemoveChild(emptyBoats);
        emptyBoats.Free();
        boatPlayer.Free();
        host.RemoveChild(loaded);
        loaded.Free();
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static float SeaLevelForTest(VoxelWorld world) => world.SeaLevel + 0.5f;

    private static void CheckQuestSystemRewards()
    {
        VoxelWorld world = new();
        Player player = new() { Mode = GameMode.Survival };
        Mobs mobs = new();
        QuestSystem quests = new();
        quests.Setup(world, player, mobs);

        TestAssert.Check(quests.Accept("village/farmers-harvest"),
                "quest system accepts a known quest");
        TestAssert.Check(!quests.Accept("village/farmers-harvest"),
                "quest system refuses a duplicate accept");
        player.RecordGathered(ItemId.Wheat, 8);
        TestAssert.Equal(1, quests.ProgressOf("village/farmers-harvest")?.Stage ?? -1,
                "gathering advances the first quest stage");
        player.RecordCrafted(ItemId.Bread, 2);
        TestAssert.Check(quests.ProgressOf("village/farmers-harvest")?.Completed == true,
                "crafting completes the multi-stage quest");

        int experienceBefore = player.Stats.Experience;
        TestAssert.Check(quests.Claim("village/farmers-harvest"),
                "completed quest claims its reward");
        TestAssert.Equal(2, player.Inventory.CountOf(ItemId.GoldIngot),
                "quest reward enters the shared inventory");
        TestAssert.Equal(20, player.Stats.Experience - experienceBefore,
                "quest reward grants its experience");
        TestAssert.Check(quests.ProgressOf("village/farmers-harvest")?.Claimed == true,
                "quest records the claimed state");
        TestAssert.Check(!quests.Claim("village/farmers-harvest"),
                "quest cannot be claimed twice");

        TestAssert.FreeAll(player, mobs, quests, world);
    }

    private static void CheckVillageResidentSpawning(Node host)
    {
        const int seed = 1337;
        string dir = $"user://tests/integration-village-spawn-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Village spawn", seed, WorldGenV8.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        VillageInfo? site = null;
        for (int cellZ = -4; cellZ <= 4 && site == null; cellZ++)
        for (int cellX = -4; cellX <= 4 && site == null; cellX++)
            site = world.GetVillageSite(cellX, cellZ);
        TestAssert.Check(site is not null, "spawn test finds a deterministic village");
        if (site is not { } village)
        {
            host.RemoveChild(world);
            world.Free();
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
            return;
        }

        Vector3 center = new(village.X + 0.5f, village.Y + 1.0f, village.Z + 0.5f);
        world.BuildPreview(center, 4);
        Player player = MakePlayer();
        host.AddChild(player);
        player.GlobalPosition = center;
        Mobs mobs = new();
        VillageSystem system = new();
        host.AddChild(system);
        system.Setup(world, player, mobs);
        system._Process(0.0);

        TestAssert.Check(system.ActiveResidents >= 5,
                "village system spawns its five residents near a loaded settlement");
        Villager? resident = system.GetChildren().OfType<Villager>().FirstOrDefault();
        TestAssert.Check(resident != null, "active resident is a live villager node");
        if (resident != null)
        {
            bool interacted = false;
            player.VillagerInteracted += _ => interacted = true;
            TestAssert.Check(resident.Interact(player),
                    "resident interaction uses the player interaction path");
            TestAssert.Check(interacted, "resident interaction reaches the player event");
            TestAssert.Check(resident.GlobalPosition.DistanceTo(center) < 90.0f,
                    "resident remains within the active village radius");

            // A save must flush live resident state; waiting for _ExitTree is too
            // late because QuitToMenu saves the world before children exit.
            resident.RecordTrade();
            world.SaveNow();
            WorldSave? liveSave = WorldStore.ReadJson<WorldSave>(
                    WorldStore.WorldSavePath(dir), WorldStore.Json);
            TestAssert.Equal(1, liveSave?.Villagers?[resident.ResidentId].TradesCompleted ?? -1,
                    "saving a live world flushes resident trade state");
        }

        host.RemoveChild(system);
        system.Free();
        TestAssert.Check(world.Villagers.Count >= 5,
                "resident identities and transforms are saved when the village system exits");
        player.GetParent()?.RemoveChild(player);
        player.Free();
        mobs.Free();
        host.RemoveChild(world);
        world.Free();
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static Player MakePlayer()
    {
        Player player = new();
        player.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
        });
        Camera3D camera = new() { Name = "Camera3D" };
        camera.AddChild(new RayCast3D { Name = "RayCast3D" });
        player.AddChild(camera);
        return player;
    }

    private static void CheckFluidShapeBoundary(Node host)
    {
        string dir = $"user://tests/integration-fluid-shape-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Fluid shape", 4242, WorldGenV8.Version,
                WorldStore.DeriveNoiseSeeds(4242), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(Vector3.Zero, 1);
        for (int y = 60; y <= 63; y++)
        for (int z = 1; z <= 4; z++)
        for (int x = 1; x <= 5; x++)
            world.SetBlock(x, y, z, BlockId.Air);
        world.SetBlock(2, 60, 2, BlockId.Stone);
        world.SetBlock(3, 60, 2, BlockId.PlanksSlab, BlockShapes.SlabBottom);
        world.SetBlock(2, 61, 2, BlockId.Water);

        Inventory buckets = new();
        buckets.Add(ItemId.WaterBucket, 1);
        TestAssert.Check(!BucketMechanics.TryPlace(world, new Vector3I(3, 60, 2),
                        buckets, 0, out _),
                "a water bucket refuses a partial slab cell");
        TestAssert.Equal(BlockId.PlanksSlab, world.GetBlock(3, 60, 2),
                "refused bucket placement leaves the slab intact");

        FluidSystem fluids = new();
        fluids.SimulateForTests(world, new Vector3(2, 61, 2));
        TestAssert.Equal(BlockId.Water, world.GetBlock(2, 61, 2),
                "fluid simulation preserves the neighbouring source");
        TestAssert.Equal(BlockId.PlanksSlab, world.GetBlock(3, 60, 2),
                "flowing water never overwrites a partial building block");

        fluids.Free();
        host.RemoveChild(world);
        world.Free();
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }
}
