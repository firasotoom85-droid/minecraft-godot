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
        CheckPlayerInteractionPersistenceAndVoidRecovery(host);
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

    private static void CheckPlayerInteractionPersistenceAndVoidRecovery(Node host)
    {
        const int seed = 20261123;
        string dir = $"user://tests/player-lifecycle-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Player lifecycle", seed, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(Vector3.Zero, 1);

        Player player = MakePlayer();
        host.AddChild(player);
        player.World = world;
        world.Player = player;
        player.Mode = GameMode.Survival;
        player.GlobalPosition = new Vector3(10.5f, 60.5f, 10.5f);

        // Seed a generated-but-unloaded solid voxel just beyond the streamed
        // east edge. The interaction DDA must stop at the first unloaded cell.
        int unloadedSolidY = -1;
        for (int y = world.Height - 1; y > 0; y--)
        {
            if (world.GetBlock(32, y, 0) != BlockId.Air)
            {
                unloadedSolidY = y;
                break;
            }
        }
        TestAssert.Check(unloadedSolidY > 0,
                "fixture finds a solid in generated data beyond the loaded chunk set");
        if (unloadedSolidY > 0)
        {
            TestAssert.Check(world.SetBlock(31, unloadedSolidY, 0, BlockId.Air),
                    "loaded boundary voxel can be cleared before the unloaded ray test");
            TestAssert.Check(!world.TryGetLoadedBlock(32, unloadedSolidY, 0, out _),
                    "loaded-only block lookup refuses generated-but-unloaded chunk data");
            TestAssert.Check(!player.TryRaycastVoxels(
                            new Vector3(31.5f, unloadedSolidY + 0.5f, 0.5f),
                            Vector3.Right, 3.0f, out _, out _, out _, out _),
                    "voxel interaction does not select a block beyond the loaded chunk boundary");

            if (world.GetBlock(29, unloadedSolidY, 0) != BlockId.Air)
                TestAssert.Check(world.SetBlock(29, unloadedSolidY, 0, BlockId.Air),
                        "solid loaded ray approach cell can be cleared");
            TestAssert.Equal(BlockId.Air, world.GetBlock(29, unloadedSolidY, 0),
                    "loaded ray approach cell is air before the target");
            TestAssert.Check(world.SetBlock(30, unloadedSolidY, 0, BlockId.Stone),
                    "loaded ray target cell can be prepared");
            TestAssert.Check(player.TryRaycastVoxels(
                            new Vector3(28.5f, unloadedSolidY + 0.5f, 0.5f),
                            Vector3.Right, 4.0f, out Vector3I loadedTarget, out _, out _, out _),
                    "voxel interaction still selects a solid block in a loaded chunk");
            TestAssert.Equal(new Vector3I(30, unloadedSolidY, 0), loadedTarget,
                    "loaded voxel ray returns the expected target cell");

            // Cross plants have no physics collider. The voxel ray must still
            // choose the nearer plant rather than the solid block behind it.
            world.SetBlock(30, unloadedSolidY, -1, BlockId.Air);
            world.SetBlock(30, unloadedSolidY, 0, BlockId.TallGrass);
            world.SetBlock(30, unloadedSolidY, 1, BlockId.Stone);
            TestAssert.Check(player.TryRaycastVoxels(
                            new Vector3(30.5f, unloadedSolidY + 0.5f, -0.5f),
                            Vector3.Back, 4.0f, out Vector3I plantTarget,
                            out BlockId plantId, out _, out _),
                    "voxel targeting detects a non-colliding cross plant");
            TestAssert.Equal(BlockId.TallGrass, plantId,
                    "cross plant is selected before the solid block behind it");
            TestAssert.Equal(new Vector3I(30, unloadedSolidY, 0), plantTarget,
                    "cross-plant target coordinate is preserved");

            // Exercise the real mining path: no inventory room keeps the voxel
            // intact; freeing a slot allows the same block and reports one gather.
            Vector3I minePosition = new(25, unloadedSolidY, 0);
            TestAssert.Check(world.SetBlock(minePosition.X, minePosition.Y,
                            minePosition.Z, BlockId.Stone),
                    "mining regression fixture places a stone voxel through SetBlock");
            for (int slot = 0; slot < Inventory.SlotCount; slot++)
                player.Inventory.Set(slot, ItemDb.New(ItemId.Dirt, 64));
            player.Inventory.Set(0, ItemDb.New(ItemId.WoodPick, 1));
            int gatheredRawDrops = 0;
            player.ItemGathered += (_, count) => gatheredRawDrops += count;
            ToolInfo woodPick = ItemDb.ToolOf(ItemId.WoodPick)!.Value;
            player.FinishBreak(minePosition, BlockId.Stone, BlockDb.DefOf(BlockId.Stone), woodPick);
            TestAssert.Equal(BlockId.Stone,
                    world.GetBlock(minePosition.X, minePosition.Y, minePosition.Z),
                    "full inventory leaves the mined block in the world");
            TestAssert.Equal(0, gatheredRawDrops,
                    "an unaccepted mining reward does not emit a gather event");

            player.Inventory.Set(Inventory.SlotCount - 1, ItemStack.Empty);
            player.FinishBreak(minePosition, BlockId.Stone, BlockDb.DefOf(BlockId.Stone), woodPick);
            TestAssert.Equal(BlockId.Air,
                    world.GetBlock(minePosition.X, minePosition.Y, minePosition.Z),
                    "mining succeeds after inventory capacity is freed");
            TestAssert.Equal(1, player.Inventory.CountOf(ItemId.Cobblestone),
                    "successful stone mining delivers the normal cobblestone drop");
            TestAssert.Equal(1, gatheredRawDrops,
                    "normal mined block reward emits exactly one gather event");
        }

        // SaveIfDirty must notice sub-threshold movement and orientation changes
        // on the focus-loss path, while leaving an unchanged pose unwritten.
        world.SaveNow();
        string playerPath = WorldStore.PlayerSavePath(dir);
        world.SaveIfDirty();
        TestAssert.Check(!FileAccess.FileExists(playerPath + ".bak"),
                "focus-loss save does not rewrite an unchanged player pose");

        player.GlobalPosition += new Vector3(0.25f, 0.0f, 0.0f);
        player.Rotation = new Vector3(0.0f, 1.2f, 0.0f);
        player.Camera.Rotation = new Vector3(-0.45f, 0.0f, 0.0f);
        world.SaveIfDirty();
        PlayerSave? savedPlayer = WorldStore.ReadJson<PlayerSave>(playerPath, WorldStore.Json);
        TestAssert.Check(savedPlayer?.Pos is { Length: 3 }
                        && Mathf.IsEqualApprox(savedPlayer.Pos[0], player.GlobalPosition.X),
                "focus loss persists player movement smaller than the periodic eight-unit threshold");
        TestAssert.Check(Mathf.IsEqualApprox(savedPlayer?.Yaw ?? 0.0f, 1.2f),
                "focus-loss persistence writes body yaw");
        TestAssert.Check(Mathf.IsEqualApprox(savedPlayer?.Pitch ?? 0.0f, -0.45f),
                "focus-loss persistence writes camera pitch");

        Player restored = MakePlayer();
        host.AddChild(restored);
        restored.RestoreOrientation(savedPlayer?.Yaw, savedPlayer?.Pitch);
        TestAssert.Check(Mathf.IsEqualApprox(restored.Rotation.Y, 1.2f)
                        && Mathf.IsEqualApprox(restored.Camera.Rotation.X, -0.45f),
                "player orientation save values restore to the live body and camera");
        host.RemoveChild(restored);
        restored.Free();

        // Void recovery applies one Fall hit before moving the player to spawn.
        player.GlobalPosition = new Vector3(0.5f, -9.0f, 0.5f);
        player.Velocity = new Vector3(2.0f, -10.0f, 3.0f);
        player.Stats.InvulnUntil = 0.0;
        float healthBeforeVoid = player.Stats.Health;
        player.RecoverFromVoidFall();
        TestAssert.Equal(healthBeforeVoid - 4.0f, player.Stats.Health,
                "survival void recovery applies damage before teleporting");
        TestAssert.Check(player.GlobalPosition.Y >= -8.0f,
                "void recovery returns the player above the safety threshold");
        TestAssert.Equal(Vector3.Zero, player.Velocity,
                "void recovery clears falling velocity");
        player.RecoverFromVoidFall();
        TestAssert.Equal(healthBeforeVoid - 4.0f, player.Stats.Health,
                "a completed void recovery cannot apply duplicate damage");

        player.Mode = GameMode.Creative;
        player.GlobalPosition = new Vector3(0.5f, -9.0f, 0.5f);
        player.RecoverFromVoidFall();
        TestAssert.Equal(healthBeforeVoid - 4.0f, player.Stats.Health,
                "creative void recovery does not apply survival damage");

        world.Player = null;
        player.World = null;
        host.RemoveChild(player);
        player.Free();
        host.RemoveChild(world);
        world.Free();
        DirAccess.RemoveAbsolute(playerPath + ".bak2");
        DirAccess.RemoveAbsolute(playerPath + ".bak");
        DirAccess.RemoveAbsolute(playerPath);
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir) + ".bak2");
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir) + ".bak");
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
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
