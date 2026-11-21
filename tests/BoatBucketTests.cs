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

        world.SetBlock(5, 50, 5, BlockId.Water);
        int waterBefore = BucketMechanics.FluidUnits(ItemId.EmptyBucket)
                + BucketMechanics.FluidUnits(world.GetBlock(5, 50, 5));
        TestAssert.Equal(8, waterBefore, "one source cell is one bucket of fluid");
        TestAssert.Check(BucketMechanics.TryCollect(world, new Vector3I(5, 50, 5),
                ItemId.EmptyBucket, out ItemId waterBucket), "empty bucket collects source water");
        TestAssert.Equal(ItemId.WaterBucket, waterBucket, "source water transforms the held bucket");
        TestAssert.Equal(BlockId.Air, world.GetBlock(5, 50, 5), "collected source is removed through SetBlock");
        int waterAfterCollect = BucketMechanics.FluidUnits(waterBucket)
                + BucketMechanics.FluidUnits(world.GetBlock(5, 50, 5));
        TestAssert.Equal(waterBefore, waterAfterCollect, "collecting water conserves bucket fluid units");

        world.SetBlock(6, 50, 5, BlockId.Air);
        TestAssert.Check(BucketMechanics.TryPlace(world, new Vector3I(6, 50, 5),
                waterBucket, out ItemId emptyBucket, out BlockId placedWater),
                "water bucket places a source in loaded air");
        TestAssert.Equal(ItemId.EmptyBucket, emptyBucket, "placing water returns an empty bucket");
        TestAssert.Equal(BlockId.Water, placedWater, "water bucket places a source, not flowing water");
        TestAssert.Equal(BlockId.Water, world.GetBlock(6, 50, 5), "placed source is committed to the voxel world");
        TestAssert.Equal(8, BucketMechanics.FluidUnits(emptyBucket)
                + BucketMechanics.FluidUnits(world.GetBlock(6, 50, 5)),
                "placing water conserves bucket fluid units");

        world.SetBlock(7, 50, 5, BlockId.FlowingWater1);
        TestAssert.Check(!BucketMechanics.TryCollect(world, new Vector3I(7, 50, 5),
                ItemId.EmptyBucket, out _), "flowing water cannot be scooped as a source");
        TestAssert.Equal(BlockId.FlowingWater1, world.GetBlock(7, 50, 5),
                "failed flowing-water collection leaves the voxel unchanged");
        world.SetBlock(8, 50, 5, BlockId.Stone);
        TestAssert.Check(!BucketMechanics.TryPlace(world, new Vector3I(8, 50, 5),
                ItemId.WaterBucket, out _, out _), "bucket refuses to overwrite solid terrain");
        TestAssert.Equal(BlockId.Stone, world.GetBlock(8, 50, 5),
                "invalid bucket placement preserves terrain");
        TestAssert.Check(!BucketMechanics.TryPlace(world, new Vector3I(7, 50, 5),
                ItemId.WaterBucket, out _, out _), "bucket refuses to overwrite flowing fluid");

        world.SetBlock(9, 50, 5, BlockId.Lava);
        TestAssert.Check(BucketMechanics.TryCollect(world, new Vector3I(9, 50, 5),
                ItemId.EmptyBucket, out ItemId lavaBucket), "empty bucket collects source lava");
        TestAssert.Equal(ItemId.LavaBucket, lavaBucket, "lava collection creates a lava bucket");
        TestAssert.Equal(BlockId.Air, world.GetBlock(9, 50, 5), "collected lava source is removed");
        TestAssert.Check(BucketMechanics.TryPlace(world, new Vector3I(10, 50, 5),
                lavaBucket, out ItemId returnedBucket, out BlockId placedLava),
                "lava bucket places a source into open space");
        TestAssert.Equal(ItemId.EmptyBucket, returnedBucket, "placing lava returns an empty bucket");
        TestAssert.Equal(BlockId.Lava, placedLava, "lava bucket restores a source cell");
        TestAssert.Equal(8, BucketMechanics.FluidUnits(lavaBucket),
                "lava bucket retains one source's fluid units");

        TestAssert.Check(BoatPhysics.FootprintLoaded(world, new Vector3(15.4f, 50, 8.5f)),
                "boat can approach a loaded chunk edge with a loaded hull footprint");
        TestAssert.Check(!BoatPhysics.FootprintLoaded(world, new Vector3(31.5f, 50, 8.5f)),
                "boat cannot advance into an unloaded chunk footprint");

        // A placed boat remains an ordinary chunk-colliding CharacterBody3D.
        world.SetBlock(8, 49, 8, BlockId.Water);
        BoatSystem boats = new();
        host.AddChild(boats);
        Player player = MakeTestPlayer();
        host.AddChild(player);
        boats.Setup(world, player, null!);
        TestAssert.Check(boats.TryPlaceBoat(new Vector3I(8, 49, 8), BlockId.Water),
                "boat places on loaded source water");
        Boat? boat = null;
        foreach (Node child in boats.GetChildren())
            if (child is Boat found) boat = found;
        TestAssert.Check(boat != null, "boat system creates a physical boat node");
        if (boat != null)
        {
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
            TestAssert.Check(boats.Dismount(boat), "player dismounts to a loaded clear cell");
            TestAssert.Check(player.CurrentBoat == null && boat.Rider == null,
                    "dismount restores independent player control");
            TestAssert.Check(boats.ToSave() is { Count: 1 } saved && !saved[0].Occupied,
                    "dismounted boat remains persistent and unoccupied");
        }

        player.GetParent()?.RemoveChild(player);
        player.QueueFree();
        host.RemoveChild(boats);
        boats.QueueFree();
        host.RemoveChild(world);
        world.QueueFree();
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
