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

        Vector3 invalidSavedPosition = new(0.5f, -20.0f, 0.5f);
        TestAssert.Check(!world.IsSafePlayerPosition(invalidSavedPosition),
                "out-of-bounds saved player position is rejected");
        TestAssert.Check(world.TryResolvePlayerDestination(invalidSavedPosition,
                        out Vector3 fallback)
                        && world.IsSafePlayerPosition(fallback),
                "invalid saved position falls back to a validated world spawn");

        world.SpawnCandidateValidatorForTests = _ => false;
        TestAssert.Check(!world.TryResolvePlayerDestination(null, out Vector3 failed)
                        && failed == Vector3.Zero,
                "rejecting every candidate reports failure without an unchecked coordinate");
        world.SpawnCandidateValidatorForTests = null;
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
        fluids.Setup(world, player);
        typeof(FluidSystem).GetField("_editsRemaining",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fluids, 1);
        int edits = (int)typeof(FluidSystem).GetMethod("ReactAt",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fluids, [lavaCell])!;
        TestAssert.Equal(0, edits,
                "fluid reaction defers a result that would intersect the player capsule");
        TestAssert.Equal(BlockId.Lava,
                world.GetBlock(lavaCell.X, lavaCell.Y, lavaCell.Z),
                "deferred fluid reaction leaves the player's occupied lava voxel unchanged");

        TestAssert.Check(world.SetBlock(lavaCell.X, lavaCell.Y, lavaCell.Z, BlockId.Air),
                "fluid reaction test removes its lava fixture through SetBlock");
        fluids.Free();
    }

    private static void CheckCurrentChunkCollisionRefresh(VoxelWorld world, Player player)
    {
        Vector3 spawn = world.FindSpawn();
        player.GlobalPosition = spawn;
        int x = Mathf.FloorToInt(spawn.X) + 1;
        int y = Mathf.FloorToInt(spawn.Y);
        int z = Mathf.FloorToInt(spawn.Z);
        Vector2I chunkPosition = new(
                Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
        Chunk? chunk = world.GetNodeOrNull<Chunk>(
                $"chunk_{chunkPosition.X}_{chunkPosition.Y}");
        TestAssert.Check(chunk != null, "spawn chunk is built before local collision mutation");
        if (chunk == null) return;

        // The adjacent cell is outside the player's 0.35 m capsule but inside
        // the close movement horizon that requires immediate collision refresh.
        BlockId existing = world.GetBlock(x, y, z);
        if (existing != BlockId.Air)
        {
            y++;
            existing = world.GetBlock(x, y, z);
        }
        TestAssert.Equal(BlockId.Air, existing, "collision fixture selects an empty adjacent voxel");

        long appliedBefore = chunk.AppliedEditEpoch;
        TestAssert.Check(world.SetBlock(x, y, z, BlockId.Stone),
                "near-player geometry mutation is accepted through SetBlock");
        TestAssert.Check(chunk.AppliedEditEpoch > appliedBefore
                        && chunk.AppliedEditEpoch == world.EditEpoch,
                "near-player collision payload is refreshed to the committed edit epoch");

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
        const int y = 118;
        TestAssert.Check(world.SetBlock(1, y, 0, BlockId.Stone),
                "near-tied DDA side-cell fixture is placed through SetBlock");
        Vector3 origin = new(0.5f, y + 0.5f, 0.5f);
        Vector3 direction = new Vector3(0.707108f, 0.707105f, 0.0f).Normalized();
        TestAssert.Check(player.TryRaycastVoxels(origin, direction, 2.0f,
                        out Vector3I hit, out BlockId id, out _, out Vector3 normal),
                "near-tied DDA visits the short positive-length intermediate voxel");
        TestAssert.Equal(new Vector3I(1, y, 0), hit,
                "near-tied DDA reports the intermediate side voxel instead of skipping it");
        TestAssert.Equal(BlockId.Stone, id, "near-tied DDA returns the actual side-cell block");
        TestAssert.Equal(Vector3.Left, normal,
                "near-tied DDA preserves the correct X entry face");

        Vector3I plant = new(2, 60, 2);
        Vector3 plantOrigin = new(2.0f, 60.5f, 2.4f);
        Vector3 plantDirection = new Vector3(0.9f, -0.3f, 0.1f).Normalized();
        TestAssert.Check(BlockDb.RayIntersectsCross(plantOrigin, plantDirection, plant,
                        0.0f, 2.0f, out _, out Vector3 plantNormal),
                "cross-plant ray reports the intersected diagonal plane");
        TestAssert.Check(Mathf.IsZeroApprox(plantNormal.Y)
                        && Mathf.IsEqualApprox(plantNormal.Length(), 1.0f)
                        && plantDirection.Dot(plantNormal) <= 0.0f,
                "cross-plant normal is horizontal, normalized and faces against the ray");
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
