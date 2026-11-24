using System;
using System.Reflection;
using Godot;

namespace Minecraft;

/// <summary>Safe destination, collision-readiness and player recovery regressions.</summary>
public static class SpawnRecoveryTests
{
    public static void Run(Node host)
    {
        string dir = $"user://tests/spawn-recovery-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Spawn recovery", 20261124, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(20261124), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        world.BuildPreview(Vector3.Zero, 1);

        Player player = MakePlayer();
        host.AddChild(player);
        player.World = world;
        world.Player = player;

        CheckWorldSpawnAndFallback(world);
        CheckPartialBlockSupport(world);
        CheckFluidReactionGuard(host, world, player);
        CheckCurrentChunkCollisionRefresh(world, player);
        CheckTeleportAndRecoveryTransactions(world, player);
        CheckVoxelTraversalAndCrossNormals(world, player);
        CheckProjectileSweepShapes(host, player);

        host.RemoveChild(player);
        player.Free();
        host.RemoveChild(world);
        world.Free();
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static void CheckWorldSpawnAndFallback(VoxelWorld world)
    {
        Vector3 spawn = world.FindSpawn();
        TestAssert.Check(world.IsSafePlayerPosition(spawn),
                "generated world spawn satisfies the generic player capsule/support contract");
        TestAssert.Check(world.TryResolvePlayerDestination(null, out Vector3 resolved)
                        && resolved == spawn,
                "empty requested destination resolves through deterministic world spawn");
        TestAssert.Check(world.TryEnsureCollisionReady(resolved),
                "spawn destination has a current terrain collider before use");

        int waterX = Mathf.FloorToInt(spawn.X);
        int waterZ = Mathf.FloorToInt(spawn.Z);
        int waterY = Mathf.FloorToInt(spawn.Y);
        while (waterY < Mathf.FloorToInt(spawn.Y + 1.5f)
                && world.GetBlock(waterX, waterY, waterZ) != BlockId.Air)
            waterY++;
        TestAssert.Check(world.SetBlock(waterX, waterY, waterZ, BlockId.Water),
                "aquatic destination fixture places water through SetBlock");
        TestAssert.Check(!world.IsSafePlayerPosition(spawn)
                        && world.IsSafePlayerPosition(spawn, allowWater: true),
                "water remains a spawn hazard but can be an explicit safe boat-dismount medium");
        TestAssert.Check(world.SetBlock(waterX, waterY, waterZ, BlockId.Air),
                "aquatic destination fixture is removed through SetBlock");

        Vector3 invalidSavedPosition = new(0.5f, -20.0f, 0.5f);
        TestAssert.Check(!world.IsSafePlayerPosition(invalidSavedPosition),
                "out-of-bounds saved player position is rejected");
        TestAssert.Check(world.TryResolvePlayerDestination(invalidSavedPosition,
                        out Vector3 fallback)
                        && world.IsSafePlayerPosition(fallback),
                "invalid saved position falls back to a validated world spawn");

        world.SpawnCandidateValidatorForTests = _ => false;
        TestAssert.Check(world.TryResolvePlayerDestination(invalidSavedPosition,
                        out Vector3 alternate, alternatePosition: spawn)
                        && alternate == spawn,
                "a valid saved respawn anchor is preferred over a failed world search");
        TestAssert.Check(!world.TryResolvePlayerDestination(null, out Vector3 failed)
                        && failed == Vector3.Zero,
                "rejecting every candidate without an alternate reports failure, not a default spawn");
        world.SpawnCandidateValidatorForTests = null;

        Vector3 boatSeat = new(spawn.X, world.Height - 4.5f, spawn.Z);
        Vector3 savedBoatPosition = boatSeat - new Vector3(0.0f, 0.48f, 0.0f);
        world.SavedBoats =
        [
            new BoatSave([savedBoatPosition.X, savedBoatPosition.Y, savedBoatPosition.Z],
                    0.0f, [0.0f, 0.0f, 0.0f], true),
        ];
        TestAssert.Check(world.TryGetSavedOccupiedBoatSeat(boatSeat, out Vector3 restoredSeat)
                        && restoredSeat == boatSeat,
                "saved occupied boat restores the player's validated seat offset");
        TestAssert.Check(!world.IsSafePlayerPosition(restoredSeat, allowWater: true)
                        && world.IsSafePlayerPosition(restoredSeat, allowWater: true,
                                allowVehicleSupport: true),
                "saved boat seat may rely on its restored hull without weakening aquatic dismount support");
        TestAssert.Check(!world.TryGetSavedOccupiedBoatSeat(spawn + Vector3.Right * 4.0f, out _),
                "an unrelated occupied boat cannot override the saved player destination");
        world.SavedBoats = null;
    }

    private static void CheckPartialBlockSupport(VoxelWorld world)
    {
        Vector3 spawn = world.FindSpawn();
        int chunkX = Mathf.FloorToInt(spawn.X / VoxelWorld.ChunkX);
        int chunkZ = Mathf.FloorToInt(spawn.Z / VoxelWorld.ChunkZ);
        int x = chunkX * VoxelWorld.ChunkX + 6;
        int z = chunkZ * VoxelWorld.ChunkZ + 6;
        int y = world.Height - 8;

        if (world.GetBlock(x, y, z) != BlockId.Air)
            TestAssert.Check(world.SetBlock(x, y, z, BlockId.Air),
                    "partial-support fixture clears its support cell through SetBlock");
        TestAssert.Check(world.SetBlock(x, y, z, BlockId.PlanksSlab, BlockShapes.SlabBottom),
                "partial-support fixture places a bottom slab through SetBlock");
        Vector3 bottomSlabPosition = new(x + 0.5f, y + 0.5f, z + 0.5f);
        TestAssert.Check(world.IsSafePlayerPosition(bottomSlabPosition),
                "safe saved-position validation recognizes a grounded player on a bottom slab");
        TestAssert.Check(world.IsSafePlayerPosition(bottomSlabPosition + Vector3.Up * 0.5f),
                "safe saved-position validation accepts the established initial-spawn offset above a bottom slab");

        TestAssert.Check(world.SetBlock(x, y, z, BlockId.PlanksSlab, BlockShapes.SlabTop),
                "partial-support fixture changes to a top slab through SetBlock");
        Vector3 topSlabPosition = new(x + 0.5f, y + 1.0f, z + 0.5f);
        TestAssert.Check(world.IsSafePlayerPosition(topSlabPosition),
                "safe saved-position validation recognizes a grounded player on a top slab");
        TestAssert.Check(world.IsSafePlayerPosition(topSlabPosition + Vector3.Up * 0.5f),
                "safe saved-position validation accepts the established initial-spawn offset above a top slab");

        TestAssert.Check(world.SetBlock(x, y, z, BlockId.Air),
                "partial-support fixture removes its slab through SetBlock");
    }

    private static void CheckFluidReactionGuard(Node host, VoxelWorld world, Player player)
    {
        Vector3 spawn = world.FindSpawn();
        player.GlobalPosition = spawn;
        Vector3I lavaCell = new(
                Mathf.FloorToInt(spawn.X),
                Mathf.FloorToInt(spawn.Y),
                Mathf.FloorToInt(spawn.Z));
        TestAssert.Check(world.SetBlock(lavaCell.X, lavaCell.Y, lavaCell.Z, BlockId.Lava),
                "fluid reaction fixture places lava inside the player capsule through SetBlock");

        FluidSystem fluids = new();
        host.AddChild(fluids);
        fluids.Setup(world, player);
        typeof(FluidSystem).GetField("_editsRemaining",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fluids, 1);
        MethodInfo reactAt = typeof(FluidSystem).GetMethod("ReactAt",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        int edits = (int)reactAt.Invoke(fluids, [lavaCell])!;
        TestAssert.Equal(0, edits,
                "fluid reaction defers a result that would intersect the player capsule");
        TestAssert.Equal(BlockId.Lava,
                world.GetBlock(lavaCell.X, lavaCell.Y, lavaCell.Z),
                "deferred fluid reaction leaves the player's occupied lava voxel unchanged");

        player.GlobalPosition = spawn + Vector3.Right * 3.0f;
        edits = (int)reactAt.Invoke(fluids, [lavaCell])!;
        TestAssert.Equal(1, edits,
                "deferred fluid reaction proceeds once the player clears the occupied voxel");
        TestAssert.Equal(BlockId.Cobblestone,
                world.GetBlock(lavaCell.X, lavaCell.Y, lavaCell.Z),
                "fluid reaction creates its expected solid result after the capsule is clear");

        TestAssert.Check(world.SetBlock(lavaCell.X, lavaCell.Y, lavaCell.Z, BlockId.Air),
                "fluid reaction test removes its cobblestone fixture through SetBlock");
        player.GlobalPosition = spawn;
        host.RemoveChild(fluids);
        fluids.Free();
    }

    private static void CheckCurrentChunkCollisionRefresh(VoxelWorld world, Player player)
    {
        Vector3 spawn = world.FindSpawn();
        player.GlobalPosition = spawn;
        int centerX = Mathf.FloorToInt(spawn.X);
        int centerZ = Mathf.FloorToInt(spawn.Z);
        int spawnChunkX = Mathf.FloorToInt(spawn.X / VoxelWorld.ChunkX);
        int spawnChunkZ = Mathf.FloorToInt(spawn.Z / VoxelWorld.ChunkZ);
        int chunkOriginX = spawnChunkX * VoxelWorld.ChunkX;
        int chunkOriginZ = spawnChunkZ * VoxelWorld.ChunkZ;
        Vector2I chunkPosition = new(spawnChunkX, spawnChunkZ);
        Chunk? chunk = world.GetNodeOrNull<Chunk>(
                $"chunk_{chunkPosition.X}_{chunkPosition.Y}");
        TestAssert.Check(chunk != null, "spawn chunk is built before local collision mutation");
        if (chunk == null) return;

        // Choose an empty adjacent cell in the same built chunk. The cell is
        // outside the capsule but still inside the synchronous edit horizon.
        int x = int.MinValue;
        int y = Mathf.FloorToInt(spawn.Y);
        int z = int.MinValue;
        Vector2I[] offsets =
        [
            Vector2I.Right, Vector2I.Left, Vector2I.Down, Vector2I.Up,
        ];
        for (int dy = 0; dy <= 1 && x == int.MinValue; dy++)
        {
            foreach (Vector2I offset in offsets)
            {
                int candidateX = centerX + offset.X;
                int candidateZ = centerZ + offset.Y;
                if (candidateX < chunkOriginX
                        || candidateX >= chunkOriginX + VoxelWorld.ChunkX
                        || candidateZ < chunkOriginZ
                        || candidateZ >= chunkOriginZ + VoxelWorld.ChunkZ
                        || world.GetBlock(candidateX, y + dy, candidateZ) != BlockId.Air)
                    continue;

                x = candidateX;
                y += dy;
                z = candidateZ;
                break;
            }
        }
        TestAssert.Check(x != int.MinValue,
                "collision fixture finds an empty adjacent cell in the spawn chunk");
        if (x == int.MinValue) return;

        long appliedBefore = chunk.AppliedEditEpoch;
        TestAssert.Check(world.SetBlock(x, y, z, BlockId.Stone),
                "near-player geometry mutation is accepted through SetBlock");
        TestAssert.Check(chunk.AppliedEditEpoch > appliedBefore
                        && chunk.AppliedEditEpoch == world.EditEpoch,
                "near-player collision payload is refreshed to the committed edit epoch after placement");

        long placedEpoch = chunk.AppliedEditEpoch;
        TestAssert.Check(world.SetBlock(x, y, z, BlockId.Air),
                "near-player solid removal is accepted through SetBlock");
        TestAssert.Check(chunk.AppliedEditEpoch > placedEpoch
                        && chunk.AppliedEditEpoch == world.EditEpoch,
                "near-player collision payload is refreshed to the committed edit epoch after removal");

        Vector3 obstructed = new(
                Mathf.FloorToInt(spawn.X) + 0.5f,
                Mathf.FloorToInt(spawn.Y) + 0.5f,
                Mathf.FloorToInt(spawn.Z) + 0.5f);
        TestAssert.Check(world.SetBlock((int)obstructed.X, (int)obstructed.Y,
                        (int)obstructed.Z, BlockId.Stone),
                "capsule obstruction fixture uses the normal voxel mutation path");
        TestAssert.Check(!world.IsSafePlayerPosition(spawn),
                "a newly solid voxel intersecting the player capsule invalidates the saved destination");
        TestAssert.Check(world.TryResolvePlayerDestination(spawn, out Vector3 safeFallback)
                        && safeFallback != spawn && world.IsSafePlayerPosition(safeFallback),
                "an obstructed saved destination resolves to a different validated spawn");
    }

    private static void CheckTeleportAndRecoveryTransactions(VoxelWorld world, Player player)
    {
        world.SpawnCandidateValidatorForTests = _ => false;
        player.LoadFromSave("survival", 0.0f, 20.0f, null, 0, dead: true);
        Vector3 deathPosition = new(12.5f, 70.0f, 12.5f);
        player.GlobalPosition = deathPosition;
        player.Respawn();
        TestAssert.Check(player.Stats.Dead && player.Stats.Health == 0.0f,
                "failed manual respawn preserves death and health state");
        TestAssert.Equal(deathPosition, player.GlobalPosition,
                "failed manual respawn never commits a partial transform");

        player.LoadFromSave("survival", 20.0f, 20.0f, null, 0, dead: false);
        player.GlobalPosition = new Vector3(0.5f, -9.0f, 0.5f);
        player.Velocity = new Vector3(2.0f, -10.0f, 3.0f);
        player.Stats.InvulnUntil = 0.0;
        float healthBeforeVoid = player.Stats.Health;
        player.RecoverFromVoidFall();
        TestAssert.Equal(healthBeforeVoid - 4.0f, player.Stats.Health,
                "failed void recovery applies its hit once before reporting failure");
        player.RecoverFromVoidFall();
        TestAssert.Equal(healthBeforeVoid - 4.0f, player.Stats.Health,
                "failed void recovery does not repeat damage on later physics ticks");
        TestAssert.Equal(Vector3.Zero, player.Velocity,
                "failed void recovery stops repeated high-speed falling");

        world.SpawnCandidateValidatorForTests = null;
        Vector3 destination = world.FindSpawn();
        player.LoadFromSave("survival", 0.0f, 20.0f, destination, 0, dead: true);
        player.GlobalPosition = new Vector3(20.5f, 70.0f, 20.5f);
        player.Respawn();
        TestAssert.Check(!player.Stats.Dead,
                "successful respawn resets death state only after destination readiness");
        TestAssert.Equal(destination, player.GlobalPosition,
                "successful respawn commits the validated anchor position");

        Vector2I destinationChunk = new(
                Mathf.FloorToInt(destination.X / VoxelWorld.ChunkX),
                Mathf.FloorToInt(destination.Z / VoxelWorld.ChunkZ));
        world.UnloadFar(new Vector2I(1000, 1000));
        TestAssert.Check(world.GetNodeOrNull<Chunk>(
                        $"chunk_{destinationChunk.X}_{destinationChunk.Y}") == null,
                "destination chunk can be unloaded before the next relocation");
        TestAssert.Check(world.TryEnsureCollisionReady(destination)
                        && world.GetNodeOrNull<Chunk>(
                                $"chunk_{destinationChunk.X}_{destinationChunk.Y}") != null,
                "teleport readiness rebuilds an unloaded destination chunk synchronously");
    }

    private static void CheckVoxelTraversalAndCrossNormals(VoxelWorld world, Player player)
    {
        player.World = world;
        Vector3 spawn = world.FindSpawn();
        int chunkX = Mathf.FloorToInt(spawn.X / VoxelWorld.ChunkX);
        int chunkZ = Mathf.FloorToInt(spawn.Z / VoxelWorld.ChunkZ);
        int baseX = chunkX * VoxelWorld.ChunkX + 4;
        int baseZ = chunkZ * VoxelWorld.ChunkZ + 4;
        int y = world.Height - 3;
        world.SetBlock(baseX + 1, y, baseZ, BlockId.Air);
        TestAssert.Check(world.SetBlock(baseX + 1, y, baseZ, BlockId.Stone),
                "near-tied DDA side-cell fixture is placed through SetBlock");
        Vector3 origin = new(baseX + 0.5f, y + 0.5f, baseZ + 0.5f);
        Vector3 direction = new Vector3(0.707108f, 0.707105f, 0.0f).Normalized();
        TestAssert.Check(player.TryRaycastVoxels(origin, direction, 2.0f,
                        out Vector3I hit, out BlockId id, out _, out Vector3 normal),
                "near-tied DDA visits the short positive-length intermediate voxel");
        TestAssert.Equal(new Vector3I(baseX + 1, y, baseZ), hit,
                "near-tied DDA reports the intermediate side voxel instead of skipping it");
        TestAssert.Equal(BlockId.Stone, id, "near-tied DDA returns the actual side-cell block");
        TestAssert.Equal(Vector3.Left, normal,
                "near-tied DDA preserves the correct X entry face");

        Vector3I plant = new(baseX + 2, y, baseZ + 2);
        Vector3 plantOrigin = new(plant.X, plant.Y + 0.5f, plant.Z + 0.4f);
        Vector3 plantDirection = new Vector3(0.9f, -0.3f, 0.1f).Normalized();
        TestAssert.Check(BlockDb.RayIntersectsCross(plantOrigin, plantDirection, plant,
                        0.0f, 2.0f, out _, out Vector3 plantNormal),
                "cross-plant ray reports the intersected diagonal plane");
        TestAssert.Check(Mathf.IsZeroApprox(plantNormal.Y)
                        && Mathf.IsEqualApprox(plantNormal.Length(), 1.0f)
                        && plantDirection.Dot(plantNormal) <= 0.0f,
                "cross-plant normal is horizontal, normalized and faces against the ray");
        TestAssert.Check(BlockShapes.CellStepFromNormal(plantNormal).Y == 0,
                "cross-plant placement offsets into a horizontal neighboring cell at any pitch");
    }

    private static void CheckProjectileSweepShapes(Node host, Player player)
    {
        ArrowProjectile arrow = new();
        host.AddChild(arrow);
        arrow.Launch(player, new Vector3(0.5f, 70.0f, 0.5f), Vector3.Right);
        TestAssert.Check(arrow.SweepForTests?.Shape is SphereShape3D arrowShape
                        && Mathf.IsEqualApprox(arrowShape.Radius, 0.10f),
                "player arrow sweeps its configured 0.10 m sphere radius");

        Mobs manager = new();
        host.AddChild(manager);
        MobProjectile projectile = new();
        host.AddChild(projectile);
        projectile.Setup(manager, new Vector3(0.5f, 70.0f, 0.5f),
                new Vector3(2.0f, 70.0f, 0.5f), 4.0f, Colors.White, 8.5f, "mobcrystal");
        TestAssert.Check(projectile.SweepForTests?.Shape is SphereShape3D mobShape
                        && Mathf.IsEqualApprox(mobShape.Radius, 0.20f),
                "mob projectile sweeps its configured 0.20 m sphere radius");

        host.RemoveChild(arrow);
        arrow.Free();
        host.RemoveChild(projectile);
        projectile.Free();
        host.RemoveChild(manager);
        manager.Free();
    }

    private static Player MakePlayer()
    {
        Player player = new();
        player.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = Player.CollisionRadius, Height = Player.CollisionHeight },
        });
        Camera3D camera = new() { Name = "Camera3D" };
        camera.AddChild(new RayCast3D { Name = "RayCast3D" });
        player.AddChild(camera);
        return player;
    }
}
