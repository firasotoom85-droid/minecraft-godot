using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Minecraft;

/// <summary>Bucket conservation, vehicle input, boarding, collision and stream-boundary regressions.</summary>
public static class BoatBucketTests
{
    public static void Run(Node host)
    {
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.EmptyBucket), "empty buckets do not stack");
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.WaterBucket), "water buckets do not stack");
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.LavaBucket), "lava buckets do not stack");
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.WoodenBoat), "boats do not stack");

        Vector3 accelerated = BoatPhysics.Step(Vector3.Zero, 0.0f, 1.0f, 0.0f, false, 0.1f).Velocity;
        TestAssert.Check(accelerated.Z < 0.0f, "boat accelerates forward along its heading");
        TestAssert.Check(accelerated.Length() <= BoatPhysics.Acceleration * 0.1f + 0.001f,
                "boat acceleration is bounded");
        (Vector3 steered, float yaw) = BoatPhysics.Step(accelerated, 0.0f, 1.0f, 1.0f, false, 0.1f);
        TestAssert.Check(yaw < 0.0f && steered.X > 0.0f,
                "right steering changes heading and redirects forward velocity");
        Vector3 braked = BoatPhysics.Step(new Vector3(0, 0, -5), 0, 0, 0, true, 0.1f).Velocity;
        TestAssert.Check(braked.Length() < 5.0f, "handbrake reduces boat speed");
        Vector3 wallSlide = BoatPhysics.SlideCollision(new Vector3(5, 0, 2), Vector3.Left);
        TestAssert.Check(wallSlide.X < 0.001f && wallSlide.Z > 0.0f,
                "terrain collision removes velocity into a wall and retains slide");
        Vector3 buoyancy = BoatPhysics.ApplyBuoyancy(new Vector3(0, 2, 0), Vector3.Zero,
                true, 3.0f, 0.1f);
        TestAssert.Check(buoyancy.Y > 0.0f, "water surface pulls the hull toward a stable float height");

        string dir = $"user://tests/boat-bucket-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Boat and bucket integration", 9128, 1, WorldStore.LegacyNoiseSeeds, 0, 0));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(Vector3.Zero, 1);

        // Keep an isolated, deterministic fluid test pocket in loaded chunk zero.
        for (int y = 48; y <= 52; y++)
        for (int z = 4; z <= 12; z++)
        for (int x = 4; x <= 12; x++)
            world.SetBlock(x, y, z, BlockId.Air);

        Inventory waterBuckets = new();
        waterBuckets.Add(ItemId.EmptyBucket, 1);
        world.SetBlock(5, 50, 5, BlockId.Water);
        int waterBefore = BucketMechanics.FluidUnits(waterBuckets.Get(0).Id)
                + BucketMechanics.FluidUnits(world.GetBlock(5, 50, 5));
        TestAssert.Equal(8, waterBefore, "one source cell is one bucket of fluid");
        TestAssert.Check(BucketMechanics.TryCollect(world, new Vector3I(5, 50, 5),
                waterBuckets, 0, out BlockId collectedWater),
                "empty bucket collects source water into the selected inventory slot");
        TestAssert.Equal(BlockId.Water, collectedWater, "water collection returns its fluid type");
        TestAssert.Equal(ItemId.WaterBucket, waterBuckets.Get(0).Id,
                "source water transforms the held inventory bucket");
        TestAssert.Equal(BlockId.Air, world.GetBlock(5, 50, 5),
                "collected source is removed through VoxelWorld.SetBlock");
        int waterAfterCollect = BucketMechanics.FluidUnits(waterBuckets.Get(0).Id)
                + BucketMechanics.FluidUnits(world.GetBlock(5, 50, 5));
        TestAssert.Equal(waterBefore, waterAfterCollect, "collecting water conserves bucket fluid units");

        world.SetBlock(6, 50, 5, BlockId.Air);
        TestAssert.Check(BucketMechanics.TryPlace(world, new Vector3I(6, 50, 5),
                waterBuckets, 0, out BlockId placedWater),
                "water bucket places a source in loaded air");
        TestAssert.Equal(ItemId.EmptyBucket, waterBuckets.Get(0).Id,
                "placing water returns an empty bucket in the same slot");
        TestAssert.Equal(BlockId.Water, placedWater, "water bucket places a source, not flowing water");
        TestAssert.Equal(BlockId.Water, world.GetBlock(6, 50, 5),
                "placed source is committed to the voxel world");
        TestAssert.Equal(8, BucketMechanics.FluidUnits(waterBuckets.Get(0).Id)
                + BucketMechanics.FluidUnits(world.GetBlock(6, 50, 5)),
                "placing water conserves bucket fluid units");

        world.SetBlock(7, 50, 5, BlockId.FlowingWater1);
        TestAssert.Check(!BucketMechanics.TryCollect(world, new Vector3I(7, 50, 5),
                waterBuckets, 0, out _), "flowing water cannot be scooped as a source");
        TestAssert.Equal(ItemId.EmptyBucket, waterBuckets.Get(0).Id,
                "failed flowing-water collection does not fill the bucket");
        TestAssert.Equal(BlockId.FlowingWater1, world.GetBlock(7, 50, 5),
                "failed flowing-water collection leaves the voxel unchanged");
        waterBuckets.Set(0, ItemDb.New(ItemId.WaterBucket, 1));
        world.SetBlock(8, 50, 5, BlockId.Stone);
        TestAssert.Check(!BucketMechanics.TryPlace(world, new Vector3I(8, 50, 5),
                waterBuckets, 0, out _), "bucket refuses to overwrite solid terrain");
        TestAssert.Equal(ItemId.WaterBucket, waterBuckets.Get(0).Id,
                "failed solid placement preserves the filled bucket");
        TestAssert.Equal(BlockId.Stone, world.GetBlock(8, 50, 5),
                "invalid bucket placement preserves terrain");
        TestAssert.Check(!BucketMechanics.TryPlace(world, new Vector3I(7, 50, 5),
                waterBuckets, 0, out _), "bucket refuses to overwrite flowing fluid");
        TestAssert.Equal(ItemId.WaterBucket, waterBuckets.Get(0).Id,
                "failed flowing-fluid placement preserves the filled bucket");
        TestAssert.Check(!BucketMechanics.TryPlace(world, new Vector3I(40, 50, 5),
                waterBuckets, 0, out _), "bucket refuses to place fluid into an unloaded chunk");
        TestAssert.Equal(ItemId.WaterBucket, waterBuckets.Get(0).Id,
                "unloaded placement leaves the filled bucket unchanged");
        TestAssert.Check(!BucketMechanics.TryCollect(world, new Vector3I(40, 50, 5),
                waterBuckets, 0, out _), "bucket cannot collect from an unloaded chunk");

        Inventory lavaBuckets = new();
        lavaBuckets.Add(ItemId.EmptyBucket, 1);
        world.SetBlock(9, 50, 5, BlockId.Lava);
        TestAssert.Check(BucketMechanics.TryCollect(world, new Vector3I(9, 50, 5),
                lavaBuckets, 0, out BlockId collectedLava), "empty bucket collects source lava");
        TestAssert.Equal(BlockId.Lava, collectedLava, "lava collection returns the lava fluid type");
        TestAssert.Equal(ItemId.LavaBucket, lavaBuckets.Get(0).Id,
                "lava collection creates a lava bucket in the same inventory slot");
        TestAssert.Equal(BlockId.Air, world.GetBlock(9, 50, 5), "collected lava source is removed");
        TestAssert.Check(BucketMechanics.TryPlace(world, new Vector3I(10, 50, 5),
                lavaBuckets, 0, out BlockId placedLava),
                "lava bucket places a source into open space");
        TestAssert.Equal(ItemId.EmptyBucket, lavaBuckets.Get(0).Id,
                "placing lava returns an empty bucket in the same slot");
        TestAssert.Equal(BlockId.Lava, placedLava, "lava bucket restores a source cell");
        TestAssert.Equal(8, BucketMechanics.FluidUnits(lavaBuckets.Get(0).Id)
                + BucketMechanics.FluidUnits(world.GetBlock(10, 50, 5)),
                "placing lava conserves bucket fluid units");

        TestAssert.Check(BoatPhysics.FootprintLoaded(world, new Vector3(15.4f, 50, 8.5f)),
                "boat can approach a loaded chunk edge with a loaded hull footprint");
        TestAssert.Check(!BoatPhysics.FootprintLoaded(world, new Vector3(31.5f, 50, 8.5f)),
                "boat cannot advance into an unloaded chunk footprint");

        // A placed boat remains an ordinary chunk-colliding CharacterBody3D.
        world.SetBlock(8, 49, 8, BlockId.Water);
        Player player = MakeTestPlayer();
        host.AddChild(player);
        world.SavedBoats = [new BoatSave([40.5f, 50.0f, 40.5f], 0.25f, [0.0f, 0.0f, 0.0f])];
        BoatSystem deferredBoats = new();
        host.AddChild(deferredBoats);
        deferredBoats.Setup(world, player, null!);
        TestAssert.Check(deferredBoats.ToSave() is { Count: 1 } pending
                        && pending[0].Position[0] == 40.5f,
                "boats waiting for an unloaded chunk remain in the next save");
        host.RemoveChild(deferredBoats);
        deferredBoats.Free();
        world.SavedBoats = null;

        BoatSystem boats = new();
        host.AddChild(boats);
        boats.Setup(world, player, null!);
        TestAssert.Check(boats.TryPlaceBoat(new Vector3I(8, 49, 8), BlockId.Water),
                "boat places on loaded source water");
        Boat? boat = null;
        foreach (Node child in boats.GetChildren())
            if (child is Boat found) boat = found;
        TestAssert.Check(boat != null, "boat system creates a physical boat node");
        if (boat != null)
        {
            TestAssert.Equal((uint)PhysicsLayer.Terrain, boat.CollisionMask,
                    "boat hull collides with the existing streamed terrain layer");
            TestAssert.Check(boat.GetNodeOrNull<CollisionShape3D>("HullCollision") != null,
                    "boat has a physical hull collider");
            player.GlobalPosition = boat.GlobalPosition + new Vector3(0, 0.2f, 0);
            TestAssert.Check(boats.TryBoard(boat, player), "player boards an unoccupied boat in reach");
            TestAssert.Check(player.CurrentBoat == boat && boat.Rider == player,
                    "boarding attaches the player and marks the boat occupied");
            List<BoatSave>? boatSave = boats.ToSave();
            TestAssert.Check(boatSave is { Count: 1 } && boatSave[0].Occupied,
                    "boat snapshot persists its occupied state");
            string json = JsonSerializer.Serialize(
                    new WorldSave(WorldStore.WorldFormatVersion, [], 0.5,
                            Boats: boatSave), WorldStore.Json);
            WorldSave? loadedSave = JsonSerializer.Deserialize<WorldSave>(json, WorldStore.Json);
            TestAssert.Check(loadedSave?.Boats is { Count: 1 } && loadedSave.Boats[0].Occupied,
                    "boat position, velocity and rider state survive world-save serialization");

            // Death while seated used to leave the player parented under the boat
            // with collision disabled; respawning must force a clean dismount.
            TestAssert.Check(player.Stats.ApplyDamage(100.0f, GameMode.Survival) && player.Dead,
                    "lethal damage kills a seated player");
            player.RespawnPoint = boat.GlobalPosition + new Vector3(0.0f, 1.0f, 0.0f);
            TestAssert.Check(player.CurrentBoat == boat,
                    "respawn fixture remains attached to its occupied boat");
            TestAssert.Check(world.IsSafePlayerPosition(player.RespawnPoint.Value,
                            allowWater: true, allowVehicleSupport: true),
                    "boat respawn anchor passes capsule and external-support validation");
            TestAssert.Check(world.TryEnsureCollisionReady(player.RespawnPoint.Value,
                            allowWater: true, allowVehicleSupport: true),
                    "boat respawn anchor has an applied collision snapshot");
            player.Respawn();
            TestAssert.Check(player.CurrentBoat == null && boat.Rider == null,
                    "respawning after death in a boat detaches the player and frees the seat");
            TestAssert.Check(player.GetParent() == host,
                    "leaving a boat restores the player's original parent, not the boat system");
            TestAssert.Check(player.GlobalPosition.DistanceTo(player.RespawnPoint.Value) < 0.01f,
                    "respawning after death in a boat uses the respawn point");
            TestAssert.Check(boats.TryBoard(boat, player),
                    "a respawned player can board the boat again");
            Vector3 dismountCandidate = boat.GlobalPosition
                    + boat.GlobalBasis.X * 1.35f + Vector3.Up * 0.15f;
            TestAssert.Check(world.IsSafePlayerPosition(dismountCandidate,
                            allowWater: true, allowVehicleSupport: true),
                    "boat dismount candidate passes capsule and external-support validation");
            TestAssert.Check(world.TryEnsureCollisionReady(dismountCandidate,
                            allowWater: true, allowVehicleSupport: true),
                    "boat dismount candidate has an applied collision snapshot");

            TestAssert.Check(boats.Dismount(boat), "player dismounts to a loaded clear cell");
            TestAssert.Check(player.CurrentBoat == null && boat.Rider == null,
                    "dismount restores independent player control");
            TestAssert.Check(boats.ToSave() is { Count: 1 } saved && !saved[0].Occupied,
                    "dismounted boat remains persistent and unoccupied");
            player.Mode = GameMode.Survival;
            boat.Break(player);
            TestAssert.Equal(1, player.Inventory.CountOf(ItemId.WoodenBoat),
                    "breaking a boat returns the boat item in survival");
            TestAssert.Check(boats.ToSave() is null, "removed boat no longer appears in saved state");
        }

        world.SetBlock(11, 49, 8, BlockId.Water);
        TestAssert.Check(boats.TryPlaceBoat(new Vector3I(11, 49, 8), BlockId.Water),
                "a second boat can be placed on another water cell");
        Boat? secondBoat = null;
        foreach (Node child in boats.GetChildren())
            if (child is Boat found) secondBoat = found;
        if (secondBoat != null)
        {
            for (int i = 0; i < Inventory.SlotCount; i++)
                player.Inventory.Set(i, ItemDb.New(ItemId.Dirt, 64));
            secondBoat.Break(player);
            TestAssert.Check(GodotObject.IsInstanceValid(secondBoat) && boats.ToSave() is { Count: 1 },
                    "a survival boat cannot be removed when its item will not fit");
            player.Inventory.Set(Inventory.SlotCount - 1, ItemStack.Empty);
            secondBoat.Break(player);
            TestAssert.Equal(1, player.Inventory.CountOf(ItemId.WoodenBoat),
                    "boat removal succeeds once inventory space is available");
        }

        player.GetParent()?.RemoveChild(player);
        player.Free();
        host.RemoveChild(boats);
        boats.Free();
        host.RemoveChild(world);
        world.Free();
        DirAccess.RemoveAbsolute(dir + "/manifest.json");
        DirAccess.RemoveAbsolute(dir);
    }

    private static Player MakeTestPlayer()
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
}
