using System;
using System.Collections.Generic;
using Godot;
using System.Linq;

namespace Minecraft;

public static class BlockShapeTests
{
    public static void Run(Node host)
    {
        Run();
        CheckChunkGeometryAndPersistence(host);
        CheckStreamingColliderBoundary(host);
        CheckLightingSnapshotLockAndRetry(host);
    }

    public static void Run()
    {
        BlockDb.EnsureTables();

        BlockId slab = BlockId.PlanksSlab;
        BlockId stairs = BlockId.PlanksStairs;
        TestAssert.Equal(BlockShapeKind.Slab, BlockShapes.Kind(slab), "slab kind is registered");
        TestAssert.Equal(BlockShapeKind.Stairs, BlockShapes.Kind(stairs), "stair kind is registered");
        TestAssert.Equal((byte)0x0F, BlockShapes.OccupancyMask(slab, BlockShapes.SlabBottom),
                "bottom slab occupies only the lower half");
        TestAssert.Equal((byte)0xF0, BlockShapes.OccupancyMask(slab, BlockShapes.SlabTop),
                "top slab occupies only the upper half");
        TestAssert.Equal((byte)0xFF, BlockShapes.OccupancyMask(slab, BlockShapes.SlabDouble),
                "double slab is a full cube");
        TestAssert.Check(BlockShapes.IsFullCube(slab, BlockShapes.SlabDouble),
                "double slab reuses full-cube optimization");
        TestAssert.Check(!BlockShapes.IsFullCube(slab, BlockShapes.SlabBottom),
                "single slab stays on shape path");
        TestAssert.Equal(16, BlockShapes.ExposedFaceCount(slab, BlockShapes.SlabBottom),
                "bottom slab has no internal or missing micro-faces");
        TestAssert.Equal(24, BlockShapes.ExposedFaceCount(slab, BlockShapes.SlabDouble),
                "double slab exposes the 24 outer micro-faces of a cube");

        byte[] expectedStairs = [0xCF, 0x5F, 0x3F, 0xAF];
        byte[] expectedUpsideDown = [0xFC, 0xF5, 0xF3, 0xFA];
        for (byte facing = 0; facing < 4; facing++)
        {
            TestAssert.Equal(expectedStairs[facing], BlockShapes.OccupancyMask(stairs, facing),
                    $"stair facing {facing} has the right lower orientation");
            TestAssert.Equal(expectedUpsideDown[facing],
                    BlockShapes.OccupancyMask(stairs, (byte)(facing | BlockShapes.StairUpsideDown)),
                    $"stair facing {facing} has the right inverted orientation");
        }

        TestAssert.Check(BlockShapes.ExposedFaceCount(stairs, 0) is > 16 and < 24,
                "stair geometry exposes its tread and riser without internal faces");
        TestAssert.Check(!BlockShapes.FaceExposed(slab, BlockShapes.SlabBottom, 0, 0, 0,
                    2, BlockId.Air, 0),
                "adjacent occupied half-cells do not emit an internal face");
        TestAssert.Check(BlockShapes.FaceExposed(slab, BlockShapes.SlabBottom, 0, 0, 0,
                    0, BlockId.Air, 0),
                "slab top surface remains visible");
        TestAssert.Check(!BlockShapes.FaceExposed(slab, BlockShapes.SlabBottom, 1, 0, 0,
                    2, BlockId.Stone, 0),
                "opaque neighboring full cube suppresses a shared half-face");
        TestAssert.Check(BlockShapes.FaceExposed(slab, BlockShapes.SlabBottom, 1, 0, 0,
                    2, BlockId.PlanksSlab, BlockShapes.SlabTop),
                "upper neighboring slab leaves the lower boundary visible");

        foreach (BlockId foliage in new[]
        {
            BlockId.Leaves, BlockId.BirchLeaves, BlockId.SpruceLeaves,
            BlockId.JungleLeaves, BlockId.SwampLeaves,
        })
        {
            TestAssert.Check(BlockShapes.FaceExposed(slab, BlockShapes.SlabBottom,
                            1, 0, 0, 2, foliage, 0),
                    $"{foliage} does not occlude an exposed slab micro-face");
            TestAssert.Check(BlockShapes.FaceExposed(stairs, 0,
                            1, 0, 0, 2, foliage, 0),
                    $"{foliage} does not occlude an exposed stair micro-face");
        }

        TestAssert.Equal(BlockShapes.SlabBottom,
                BlockShapes.SelectSlabState(Vector3.Up, new Vector3(0.5f, 1.0f, 0.5f)),
                "top-face placement chooses bottom slab");
        TestAssert.Equal(BlockShapes.SlabTop,
                BlockShapes.SelectSlabState(Vector3.Down, new Vector3(0.5f, 0.0f, 0.5f)),
                "underside placement chooses top slab");
        TestAssert.Equal(BlockShapes.SlabTop,
                BlockShapes.SelectSlabState(Vector3.Right, new Vector3(1.0f, 0.75f, 0.5f)),
                "side placement above midpoint chooses top slab");
        TestAssert.Equal(BlockShapes.SlabBottom,
                BlockShapes.SelectSlabState(Vector3.Right, new Vector3(1.0f, 0.25f, 0.5f)),
                "side placement below midpoint chooses bottom slab");

        TestAssert.Check(BlockShapes.CanCombineSlab(slab, slab, BlockShapes.SlabBottom,
                    Vector3.Up, new Vector3(0.5f, 0.5f, 0.5f)),
                "placing onto the exposed top of a bottom slab makes a double slab");
        TestAssert.Check(BlockShapes.CanCombineSlab(slab, slab, BlockShapes.SlabTop,
                    Vector3.Down, new Vector3(0.5f, 0.0f, 0.5f)),
                "placing beneath the exposed underside of a top slab makes a double slab");
        TestAssert.Check(!BlockShapes.CanCombineSlab(slab, slab, BlockShapes.SlabBottom,
                    Vector3.Right, new Vector3(1.0f, 0.25f, 0.5f)),
                "side placement never merges through a slab side");

        TestAssert.Equal((byte)0, BlockShapes.StairStateFromPlacement(Vector3.Up, Vector3.Zero, 0.0f),
                "north-facing top placement produces upright stairs");
        TestAssert.Equal((byte)3, BlockShapes.StairStateFromPlacement(Vector3.Up, Vector3.Zero, Mathf.Pi * 0.5f),
                "east-west yaw maps to west-facing stair state");
        TestAssert.Equal((byte)6, BlockShapes.StairStateFromPlacement(Vector3.Down, Vector3.Zero, Mathf.Pi),
                "underside placement produces upside-down south-facing stairs");
        TestAssert.Equal(new Vector3I(0, 1, 0), BlockShapes.CellStepFromNormal(Vector3.Up),
                "top-face target places in the cell above");
        TestAssert.Equal(new Vector3I(-1, 0, 0), BlockShapes.CellStepFromNormal(Vector3.Left),
                "left-face target places in the cell to the left");

        TestAssert.Check(!BlockShapes.IntersectsAabb(slab, BlockShapes.SlabBottom,
                    Vector3.Zero, new Vector3(0.1f, 0.6f, 0.1f), new Vector3(0.9f, 0.9f, 0.9f)),
                "player volume in the empty upper half does not collide with bottom slab");
        TestAssert.Check(BlockShapes.IntersectsAabb(slab, BlockShapes.SlabBottom,
                    Vector3.Zero, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.9f, 0.4f, 0.9f)),
                "player volume overlapping the lower slab collides");
        TestAssert.Check(BlockShapes.IntersectsAabb(stairs, 0,
                    Vector3.Zero, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.9f, 0.4f, 0.9f)),
                "stair base is collidable");

        TestAssert.Equal(0.35f, Player.CollisionRadius,
                "player capsule radius matches the main scene collider");
        TestAssert.Equal(1.8f, Player.CollisionHeight,
                "player capsule height matches the main scene collider");
        string mainScene = FileAccess.GetFileAsString("res://scenes/main.tscn");
        TestAssert.Check(mainScene.Contains("radius = 0.35")
                        && mainScene.Contains("height = 1.8"),
                "capsule overlap constants stay aligned with the player scene resource");

        TestAssert.Check(Player.BlockIntersectsPlayerCapsule(BlockId.Stone, 0,
                    new Vector3(1, 0, 0), new Vector3(0.66f, 0, 0.5f),
                    Player.CollisionRadius, Player.CollisionHeight),
                "full-cube placement is rejected at 0.34 m from the capsule center");
        TestAssert.Check(!Player.BlockIntersectsPlayerCapsule(BlockId.Stone, 0,
                    new Vector3(1, 0, 0), new Vector3(0.65f, 0, 0.5f),
                    Player.CollisionRadius, Player.CollisionHeight),
                "capsule contact at exactly its radius is not treated as penetration");
        foreach (Vector3 basePosition in new[]
        {
            new Vector3(-0.34f, 0, 0.5f), new Vector3(1.34f, 0, 0.5f),
            new Vector3(0.5f, 0, -0.34f), new Vector3(0.5f, 0, 1.34f),
        })
            TestAssert.Check(Player.BlockIntersectsPlayerCapsule(BlockId.Stone, 0,
                        Vector3.Zero, basePosition, Player.CollisionRadius, Player.CollisionHeight),
                    "capsule overlap is detected on each horizontal side");

        TestAssert.Check(!Player.BlockIntersectsPlayerCapsule(BlockId.PlanksSlab,
                    BlockShapes.SlabBottom, Vector3.Zero, new Vector3(0.5f, 0.6f, 0.5f),
                    Player.CollisionRadius, Player.CollisionHeight),
                "capsule above a bottom slab does not falsely overlap the empty lower shape");
        TestAssert.Check(Player.BlockIntersectsPlayerCapsule(BlockId.PlanksSlab,
                    BlockShapes.SlabTop, Vector3.Zero, new Vector3(0.5f, 0.6f, 0.5f),
                    Player.CollisionRadius, Player.CollisionHeight),
                "capsule intersects the occupied upper half of a top slab");
        TestAssert.Check(!Player.BlockIntersectsPlayerCapsule(BlockId.PlanksStairs,
                    0, Vector3.Zero, new Vector3(0.9f, 0.6f, 0.1f),
                    Player.CollisionRadius, Player.CollisionHeight),
                "capsule in the unoccupied upper stair corner remains placeable");

        TestAssert.Check(BlockShapes.Raycast(new Vector3(0.25f, 0.75f, 0.25f), Vector3.Down,
                    Vector3I.Zero, slab, BlockShapes.SlabBottom, 0.0f, 2.0f,
                    out float slabHit, out Vector3 slabNormal),
                "ray intersects the occupied lower slab half");
        TestAssert.Equal(0.25f, slabHit, "slab ray hit distance matches the top surface");
        TestAssert.Equal(Vector3.Up, slabNormal, "slab ray reports the correct surface normal");
        TestAssert.Check(!BlockShapes.Raycast(new Vector3(0.25f, 0.25f, 0.25f), Vector3.Down,
                    Vector3I.Zero, slab, BlockShapes.SlabTop, 0.0f, 2.0f, out _, out _),
                "ray through empty lower half misses a top slab");
        TestAssert.Check(BlockShapes.Raycast(new Vector3(0.25f, 0.25f, 0.25f), Vector3.Up,
                    Vector3I.Zero, slab, BlockShapes.SlabTop, 0.0f, 2.0f, out _, out _),
                "ray from below hits the underside of a top slab");
        TestAssert.Check(BlockShapes.Raycast(new Vector3(0.25f, 0.25f, -1.0f), Vector3.Back,
                    Vector3I.Zero, stairs, 0, 0.0f, 3.0f, out _, out _),
                "ray hits the stair's lower front half");

        TestAssert.Equal(46, (int)BlockId.PlanksSlab, "first shape block id is appended");
        TestAssert.Equal(51, (int)BlockId.BrickStairs, "last shape block id is appended");
        TestAssert.Equal((int)ItemId.EnchantingTable + 1, (int)ItemId.PlanksSlab,
                "first shape item ID is appended after existing persisted items");
        TestAssert.Equal((int)ItemId.PlanksSlab + 5, (int)ItemId.BrickStairs,
                "shape item IDs remain contiguous and append-only");
        TestAssert.Equal(2, BlockShapes.DropCount(slab, BlockShapes.SlabDouble, 1),
                "double slabs drop two slab items");
        TestAssert.Equal(1, BlockShapes.DropCount(slab, BlockShapes.SlabBottom, 1),
                "single slabs drop one slab item");
        TestAssert.Equal(ItemId.PlanksSlab, ItemDb.BlockItem(BlockId.PlanksSlab),
                "slab block maps to its inventory item");
        TestAssert.Equal(BlockId.CobblestoneStairs, ItemDb.BlockFor(ItemId.CobblestoneStairs) ?? BlockId.Air,
                "stair inventory item maps to block");
        TestAssert.Check(BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.PlanksSlab), null) > 0.0f,
                "slabs use their registered mining hardness");
        TestAssert.Check(BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.BrickStairs), null) > 0.0f,
                "stairs use their registered mining hardness");
        TestAssert.Check(ItemDb.NameOf(ItemId.PlanksSlab) == "Oak Planks Slab",
                "slab inventory items resolve to their block display names");
        TestAssert.Check(ItemDb.DescriptionOf(ItemId.PlanksStairs).Contains("upside down"),
                "stair inventory tooltips explain orientation");

        foreach (ItemId item in new[]
        {
            ItemId.PlanksSlab, ItemId.CobblestoneSlab, ItemId.BrickSlab,
            ItemId.PlanksStairs, ItemId.CobblestoneStairs, ItemId.BrickStairs,
        })
            TestAssert.Check(Recipes.All.Any(recipe => recipe.Result == item),
                    $"{item} has a crafting recipe");
    }

    private static void CheckLightingSnapshotLockAndRetry(Node host)
    {
        string dir = $"user://tests/light-snapshot-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Light snapshot retry", 271828, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(271828), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new() { BuildCollision = false };
        host.AddChild(world);
        try
        {
            world.BuildPreview(Vector3.Zero, 1);

            using System.Threading.ManualResetEventSlim snapshotCaptured = new(false);
            using System.Threading.ManualResetEventSlim resumeSolve = new(false);
            int capturedSnapshots = 0;
            world.SnapshotReleasedTestHook = () =>
            {
                int attempt = System.Threading.Interlocked.Increment(ref capturedSnapshots);
                if (attempt == 1)
                {
                    snapshotCaptured.Set();
                    resumeSolve.Wait(System.TimeSpan.FromSeconds(15));
                }
            };

            // Preview radius 1 has already populated centers through (1,1);
            // center (3,3) guarantees a cache miss while retaining chunk (1,1)
            // inside the solve's 5x5 snapshot region.
            System.Threading.Tasks.Task<VoxelLightData[]> solve = System.Threading.Tasks.Task.Run(
                    () => world.LightDataNeighborhood(new Vector2I(3, 3)));
            bool reachedSolve = snapshotCaptured.Wait(System.TimeSpan.FromSeconds(10));

            System.Threading.Tasks.Task<bool>? lockProbe = null;
            bool acquiredDuringSolve = false;
            if (reachedSolve)
            {
                // Start the lock probe only after the worker is paused beyond
                // its snapshot section; this proves the solve itself is out of lock.
                lockProbe = System.Threading.Tasks.Task.Run(() =>
                {
                    lock (world.SnapshotLock)
                        return true;
                });
                acquiredDuringSolve = lockProbe.Wait(System.TimeSpan.FromSeconds(2));
            }

            BlockId oldBlock = world.GetBlock(24, 120, 24);
            BlockId replacement = oldBlock == BlockId.Air ? BlockId.Stone : BlockId.Air;
            bool mutationSucceeded = reachedSolve && world.SetBlock(24, 120, 24, replacement);

            resumeSolve.Set();
            bool solveCompleted = solve.Wait(System.TimeSpan.FromSeconds(30));
            if (solveCompleted)
                _ = solve.GetAwaiter().GetResult();
            if (lockProbe != null && !lockProbe.IsCompleted)
                lockProbe.Wait(System.TimeSpan.FromSeconds(10));

            world.SnapshotReleasedTestHook = null;
            TestAssert.Check(reachedSolve,
                    "light neighborhood solve reaches derived work after releasing the snapshot lock");
            TestAssert.Check(acquiredDuringSolve,
                    "another thread can acquire SnapshotLock while neighborhood lighting is solving");
            TestAssert.Check(mutationSucceeded,
                    "a block edit can complete while the prior light snapshot is being solved");
            TestAssert.Check(solveCompleted && capturedSnapshots >= 2,
                    "a light solve captured before a light-affecting edit is discarded and retried against changed dependencies");
        }
        finally
        {
            world.SnapshotReleasedTestHook = null;
            host.RemoveChild(world);
            world.QueueFree();
            DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckStreamingColliderBoundary(Node host)
    {
        string dir = $"user://tests/collider-frontier-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Collider frontier", 314159, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(314159), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new() { BuildCollision = true };
        host.AddChild(world);
        bool previousGreedy = Mesher.Greedy;
        Mesher.Greedy = false;
        try
        {
            world.BuildPreview(Vector3.Zero, 1);

            TestAssert.Check((world.MissingColliderBoundaryMask(new Vector2I(1, 0))
                            & (1 << 2)) != 0,
                    "east frontier recognizes the generated but unbuilt +X neighbor");
            TestAssert.Check((world.MissingColliderBoundaryMask(new Vector2I(-1, 0))
                            & (1 << 3)) != 0,
                    "west frontier recognizes the generated but unbuilt -X neighbor at negative coordinates");
            TestAssert.Check((world.MissingColliderBoundaryMask(new Vector2I(0, 1))
                            & (1 << 4)) != 0,
                    "south frontier recognizes the generated but unbuilt +Z neighbor");
            TestAssert.Check((world.MissingColliderBoundaryMask(new Vector2I(0, -1))
                            & (1 << 5)) != 0,
                    "north frontier recognizes the generated but unbuilt -Z neighbor");

            CheckMissingColliderEdge(world, new Vector2I(1, 0),
                    sourceX: 31, sourceZ: 8, neighborX: 32, neighborZ: 8,
                    face: 2, axis: 0, plane: 16.0f, cellX: 15, cellZ: 8,
                    "positive X edge");
            CheckMissingColliderEdge(world, new Vector2I(-1, 0),
                    sourceX: -16, sourceZ: 8, neighborX: -17, neighborZ: 8,
                    face: 3, axis: 0, plane: 0.0f, cellX: 0, cellZ: 8,
                    "negative X edge");
            CheckMissingColliderEdge(world, new Vector2I(0, 1),
                    sourceX: 8, sourceZ: 31, neighborX: 8, neighborZ: 32,
                    face: 4, axis: 2, plane: 16.0f, cellX: 8, cellZ: 15,
                    "positive Z edge");
            CheckMissingColliderEdge(world, new Vector2I(0, -1),
                    sourceX: 8, sourceZ: -16, neighborX: 8, neighborZ: -17,
                    face: 5, axis: 2, plane: 0.0f, cellX: 8, cellZ: 0,
                    "negative Z edge");

            Vector2I frontier = new(1, 0);
            int surfaceY = world.SurfaceHeight(32, 8);
            int solidY = -1;
            for (int y = surfaceY; y >= 1; y--)
            {
                BlockId candidate = world.GetBlock(32, y, 8);
                if (BlockDb.IsSolid(candidate) && !BlockDb.IsTransparent(candidate))
                {
                    solidY = y;
                    break;
                }
            }

            TestAssert.Check(solidY > 0,
                    "frontier fixture finds a generated solid block in the unloaded neighbor");
            if (solidY > 0)
            {
                bool frontierSourceChanged = world.SetBlock(31, solidY, 8, BlockId.Stone);
                TestAssert.Check(frontierSourceChanged
                                || world.GetBlock(31, solidY, 8) == BlockId.Stone,
                        "frontier fixture source block is stone through SetBlock");

                MeshPayload frontierPayload = Mesher.Build(world, frontier);
                try
                {
                    TestAssert.Equal(2, CountTrianglesOnPlane(
                                    frontierPayload.Collision, 0, 16.0f, solidY, 8),
                            "loaded chunk emits its collision backing face against an unbuilt solid neighbor");
                    TestAssert.Equal(0, CountTrianglesOnPlane(
                                    frontierPayload.Opaque.Vertex, 0, 16.0f, solidY, 8),
                            "collision frontier face does not add duplicate render geometry");
                }
                finally
                {
                    PayloadPool.Release(frontierPayload);
                }

                // Loading the neighbor supplies the opposite active collider.
                // The same boundary face must then disappear from this chunk.
                world.BuildPreview(new Vector3(32.0f, 100.0f, 0.0f), 1);
                TestAssert.Equal(0,
                        world.MissingColliderBoundaryMask(frontier) & (1 << 2),
                        "loading the neighboring chunk clears the +X missing-collider bit");

                MeshPayload connectedPayload = Mesher.Build(world, frontier);
                try
                {
                    TestAssert.Equal(0, CountTrianglesOnPlane(
                                    connectedPayload.Collision, 0, 16.0f, solidY, 8),
                            "connected chunk colliders do not retain a redundant internal boundary wall");
                }
                finally
                {
                    PayloadPool.Release(connectedPayload);
                }

                bool sourceBlockChanged = world.SetBlock(31, solidY, 9, BlockId.Stone);
                TestAssert.Check(sourceBlockChanged
                                || world.GetBlock(31, solidY, 9) == BlockId.Stone,
                        "partial-neighbor frontier source block is stone through SetBlock");
                bool slabChanged = world.SetBlock(32, solidY, 9, BlockId.PlanksSlab,
                        BlockShapes.SlabBottom);
                TestAssert.Check(slabChanged
                                || world.GetBlock(32, solidY, 9) == BlockId.PlanksSlab
                                        && world.GetBlockState(32, solidY, 9) == BlockShapes.SlabBottom,
                        "partial generated neighbor slab is present through SetBlock");

                // Move the streaming center so the tested chunk remains at the
                // unload margin while its +X neighbor falls outside it.
                world.ViewDistance = 0;
                world.UnloadFar(new Vector2I(-1, 0));
                TestAssert.Check(world.HasChunk(frontier)
                                && !world.HasChunk(frontier + Vector2I.Right),
                        "unloading the adjacent chunk keeps the tested frontier chunk active");
                TestAssert.Check((world.MissingColliderBoundaryMask(frontier) & (1 << 2)) != 0,
                        "unloading the neighbor restores the +X missing-collider boundary bit");

                MeshPayload unloadedPayload = Mesher.Build(world, frontier);
                try
                {
                    TestAssert.Equal(2, CountTrianglesOnPlane(
                                    unloadedPayload.Collision, 0, 16.0f, solidY, 8),
                            "remaining chunk restores its collision wall when the neighbor unloads");
                    TestAssert.Equal(0, CountTrianglesOnPlane(
                                    unloadedPayload.Opaque.Vertex, 0, 16.0f, solidY, 8),
                            "unloading a neighbor restores collision without duplicating render geometry");
                    TestAssert.Equal(2, CountTrianglesOnPlane(
                                    unloadedPayload.Collision, 0, 16.0f, solidY, 9),
                            "unbuilt partial-shape neighbor does not leave a half-height collision opening");
                    TestAssert.Check(CountTrianglesOnPlane(
                                    unloadedPayload.Opaque.Vertex, 0, 16.0f, solidY, 9) > 0,
                            "render geometry still clips to the exposed half beside an unbuilt slab");
                }
                finally
                {
                    PayloadPool.Release(unloadedPayload);
                }
            }
        }
        finally
        {
            Mesher.Greedy = previousGreedy;
            host.RemoveChild(world);
            world.QueueFree();
            DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void CheckMissingColliderEdge(
            VoxelWorld world, Vector2I sourceChunk,
            int sourceX, int sourceZ, int neighborX, int neighborZ,
            int face, int axis, float plane, int cellX, int cellZ, string label)
    {
        TestAssert.Check((world.MissingColliderBoundaryMask(sourceChunk) & (1 << face)) != 0,
                $"{label} reports a missing adjacent collider");

        int surfaceY = world.SurfaceHeight(neighborX, neighborZ);
        int solidY = -1;
        for (int y = surfaceY; y >= 1; y--)
        {
            BlockId neighbor = world.GetBlock(neighborX, y, neighborZ);
            if (BlockDb.IsSolid(neighbor) && !BlockDb.IsTransparent(neighbor))
            {
                solidY = y;
                break;
            }
        }
        TestAssert.Check(solidY > 0,
                $"{label} fixture finds a generated solid neighbor block");
        if (solidY <= 0) return;

        bool changed = world.SetBlock(sourceX, solidY, sourceZ, BlockId.Stone);
        TestAssert.Check(changed || world.GetBlock(sourceX, solidY, sourceZ) == BlockId.Stone,
                $"{label} source block is stone through SetBlock");

        MeshPayload payload = Mesher.Build(world, sourceChunk);
        try
        {
            TestAssert.Equal(2, CountTrianglesOnCellPlane(
                            payload.Collision, axis, plane, cellX, solidY, cellZ),
                    $"{label} emits a full collision boundary face against an unbuilt neighbor");
            TestAssert.Equal(0, CountTrianglesOnCellPlane(
                            payload.Opaque.Vertex, axis, plane, cellX, solidY, cellZ),
                    $"{label} frontier face remains collision-only without duplicate render geometry");
        }
        finally
        {
            PayloadPool.Release(payload);
        }
    }

    private static int CountTrianglesOnCellPlane(
            System.Collections.Generic.IReadOnlyList<Vector3> vertices,
            int axis, float coordinate, int x, int y, int z)
    {
        int count = 0;
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            bool onPlane = true;
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 point = vertices[i + vertex];
                onPlane &= Mathf.IsEqualApprox(point[axis], coordinate)
                        && point.X >= x && point.X <= x + 1.0f
                        && point.Y >= y && point.Y <= y + 1.0f
                        && point.Z >= z && point.Z <= z + 1.0f;
            }
            if (onPlane) count++;
        }
        return count;
    }

    private static int CountTrianglesOnPlane(
            System.Collections.Generic.IReadOnlyList<Vector3> vertices,
            int axis, float coordinate, int y, int z)
    {
        int count = 0;
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            bool onPlane = true;
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 point = vertices[i + vertex];
                onPlane &= Mathf.IsEqualApprox(point[axis], coordinate)
                        && point.Y >= y && point.Y <= y + 1.0f
                        && point.Z >= z && point.Z <= z + 1.0f;
            }
            if (onPlane) count++;
        }
        return count;
    }

    private static void CheckChunkGeometryAndPersistence(Node host)
    {
        string dir = $"user://tests/block-shapes-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Block shape integration", 8675309, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(8675309), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(dir);

        VoxelWorld world = new();
        host.AddChild(world);
        bool originalPreviewGreedy = Mesher.Greedy;
        Mesher.Greedy = true;
        try
        {
            world.BuildPreview(Vector3.Zero, 1);
        }
        finally
        {
            Mesher.Greedy = originalPreviewGreedy;
        }
        TestAssert.Equal(0, world.ChunkBlockStates(Vector2I.Zero).Length,
                "chunks without non-default shape data retain an empty state buffer");

        // A clean air volume isolates geometry and its boundary interactions.
        for (int y = 99; y <= 102; y++)
            for (int z = 1; z <= 5; z++)
                for (int x = 1; x <= 14; x++)
                    world.SetBlock(x, y, z, BlockId.Air);

        world.SetBlock(2, 100, 2, BlockId.PlanksSlab, BlockShapes.SlabBottom);
        world.SetBlock(4, 100, 2, BlockId.PlanksSlab, BlockShapes.SlabTop);
        world.SetBlock(6, 100, 2, BlockId.PlanksSlab, BlockShapes.SlabDouble);
        world.SetBlock(8, 100, 2, BlockId.PlanksStairs, 3);
        world.SetBlock(11, 100, 4, BlockId.Stone);
        world.SetBlock(12, 100, 4, BlockId.PlanksSlab, BlockShapes.SlabBottom);
        world.SetBlock(13, 99, 4, BlockId.Dirt);
        world.SetBlock(13, 100, 4, BlockId.Farmland);

        // Clear the negative-Z/east chunk corner too; these are AO-only
        // neighbours for the final diagonal-seam regression.
        for (int y = 99; y <= 102; y++)
            for (int z = -1; z <= 0; z++)
                for (int x = 14; x <= 16; x++)
                    world.SetBlock(x, y, z, BlockId.Air);

        // AO must use the actual occupied half of neighbouring shape blocks.
        world.SetBlock(1, 100, 4, BlockId.Stone);
        world.SetBlock(2, 101, 4, BlockId.PlanksSlab, BlockShapes.SlabBottom);
        world.SetBlock(4, 100, 4, BlockId.Stone);
        world.SetBlock(5, 101, 4, BlockId.PlanksSlab, BlockShapes.SlabTop);

        // Stair risers have an occupied micro-cell on one side of their edge,
        // even though the neighbouring whole voxel is air.
        world.SetBlock(8, 100, 5, BlockId.PlanksStairs, 0);
        world.SetBlock(10, 100, 5, BlockId.PlanksStairs, BlockShapes.StairUpsideDown);

        // The top-slab state lives in the diagonal chunk and must remain visible
        // to the AO sampler at the center chunk's north-east corner.
        TestAssert.Check(world.SetBlock(15, 100, 0, BlockId.Stone),
                "chunk-edge cube edit succeeds in the center chunk");
        TestAssert.Check(world.SetBlock(16, 101, -1, BlockId.PlanksSlab, BlockShapes.SlabTop),
                "diagonal top-slab edit succeeds in the north-east chunk");

        TestAssert.Check(world.TryEditTop(13, 4, out int farmlandTop, out BlockId farmlandSurface),
                "edited surface lookup exposes the topmost edited column");
        TestAssert.Equal(100, farmlandTop, "farmland edit surface height is retained");
        TestAssert.Equal(BlockId.Farmland, farmlandSurface,
                "far LOD can resolve an edited farmland surface without reading chunk data");
        TestAssert.Check(world.TryEditTop(13, 4, out int legacyTop),
                "existing height-only edit lookup remains compatible");
        TestAssert.Equal(100, legacyTop, "legacy edit lookup keeps the same surface height");

        TestAssert.Equal(BlockShapes.SlabTop, world.GetBlockState(4, 100, 2),
                "world edit pipeline stores upper-slab state");
        TestAssert.Equal(VoxelWorld.ChunkX * world.Height * VoxelWorld.ChunkZ,
                world.ChunkBlockStates(Vector2I.Zero).Length,
                "only chunks with non-default states allocate a dense state buffer");
        TestAssert.Equal(BlockShapes.SlabDouble, world.GetBlockState(6, 100, 2),
                "world edit pipeline stores double-slab state");
        TestAssert.Equal((byte)3, world.GetBlockState(8, 100, 2),
                "world edit pipeline stores stair facing state");

        bool originalGreedy = Mesher.Greedy;
        Mesher.Greedy = false;
        MeshPayload perFace = Mesher.Build(world, Vector2I.Zero);
        Dictionary<SharedCornerKey, Color> perFaceCornerColors = CaptureCornerColors(perFace.Opaque);
        CheckSharedCornerContinuity(perFace.Opaque, "per-face");

        // Same geometric corner rule for a cube face and a partial occluder:
        // a lower slab occupies the sampled lower micro-cell; an upper slab
        // does not. This also catches the old id-only Occl lookup.
        CheckVertexShade(perFace.Opaque, new Vector3(2, 101, 4), Vector3.Right,
                0.68f, "bottom-slab occupancy darkens the matching cube corner");
        CheckVertexShade(perFace.Opaque, new Vector3(5, 101, 4), Vector3.Right,
                0.80f, "top-slab occupancy leaves the lower sampled corner open");
        CheckVertexShade(perFace.Opaque, new Vector3(16, 101, 0), Vector3.Right,
                0.80f, "diagonal-chunk top-slab state reaches corner AO without cardinal-edge aliasing");

        // The shape's own occupied half-cells participate in AO at the stair
        // riser, including the mirrored upside-down orientation.
        CheckVertexShade(perFace.Opaque, new Vector3(8, 100.5f, 5.5f), BlockDb.FaceDirs[5],
                0.5525f, "upright stair riser shades its occupied lower edge");
        CheckVertexShade(perFace.Opaque, new Vector3(10, 100.5f, 5.5f), BlockDb.FaceDirs[5],
                0.5525f, "inverted stair riser shades its occupied upper edge");

        TestAssert.Equal(perFace.Opaque.Vertex.Count, perFace.Opaque.Normal.Count,
                "shape surfaces carry a normal for every emitted vertex");
        TestAssert.Equal(perFace.Opaque.Vertex.Count, perFace.Opaque.Color.Count,
                "shape surfaces carry AO and voxel-light color for every emitted vertex");
        TestAssert.Equal(perFace.Opaque.Vertex.Count, perFace.Opaque.Uv.Count,
                "shape surfaces carry atlas UVs for every emitted vertex");
        TestAssert.Check(CheckBlockUvMapping(perFace.Opaque, new Vector3(2, 100, 2),
                    BlockId.PlanksSlab, BlockShapes.SlabBottom, false) > 0,
                "slab micro-faces use block-relative atlas UVs instead of repeating a full tile");
        TestAssert.Check(CheckBlockUvMapping(perFace.Opaque, new Vector3(8, 100, 2),
                    BlockId.PlanksStairs, 3, false) > 0,
                "rotated stair micro-faces retain continuous block-relative wood UVs");
        TestAssert.Check(CheckBlockUvMapping(perFace.Opaque, new Vector3(11, 100, 4),
                    BlockId.Stone, 0, false, faceFilter: 2) > 0,
                "cube faces clipped against a slab keep the original full-cube texel density");
        TestAssert.Check(perFace.Opaque.Color.Exists(c => Mathf.IsEqualApprox(c.A,
                        (float)SurfaceFamily.Wood)),
                "wood faces carry their response family in vertex alpha");
        TestAssert.Equal(0, perFace.Opaque.Uv2.Count,
                "per-face atlas meshes do not allocate a texture-array UV channel");
        Chunk? geometryChunk = null;
        foreach (Node child in world.GetChildren())
            if (child is Chunk candidate && candidate.ChunkPos == Vector2I.Zero)
                geometryChunk = candidate;
        TestAssert.Check(geometryChunk != null, "shape geometry chunk exists in the preview world");
        geometryChunk!.ApplyPayload(perFace);
        TestAssert.Equal(BlockShapes.ExposedFaceCount(BlockId.PlanksSlab, BlockShapes.SlabBottom) * 2,
                TriangleCountInCell(perFace.Opaque, 2, 100, 2),
                "bottom slab emits each exposed micro-face exactly once");
        TestAssert.Equal(BlockShapes.ExposedFaceCount(BlockId.PlanksSlab, BlockShapes.SlabTop) * 2,
                TriangleCountInCell(perFace.Opaque, 4, 100, 2),
                "top slab emits each exposed micro-face exactly once");
        TestAssert.Equal(12, TriangleCountInCell(perFace.Opaque, 6, 100, 2),
                "double slab emits six full-cube quads");
        TestAssert.Equal(BlockShapes.ExposedFaceCount(BlockId.PlanksStairs, 3) * 2,
                TriangleCountInCell(perFace.Opaque, 8, 100, 2),
                "stair tread and riser geometry covers all occupied half-cells");
        TestAssert.Equal(TriangleCountInCell(perFace.Opaque, 2, 100, 2),
                CollisionTriangleCountInCell(perFace.Collision, 2, 100, 2),
                "slab collision triangles match its rendered outer surface");
        TestAssert.Equal(TriangleCountInCell(perFace.Opaque, 8, 100, 2),
                CollisionTriangleCountInCell(perFace.Collision, 8, 100, 2),
                "stair collision triangles match its rendered outer surface");
        TestAssert.Check(Math.Abs(AreaOnPlane(perFace.Opaque, 0, 12.0f, 100.0f, 101.0f, 4.0f, 5.0f) - 0.5) < 0.001,
                "full-cube face is clipped to the uncovered half beside a bottom slab");
        double perFaceArea = SurfaceArea(perFace.Opaque);
        TestAssert.Check(perFace.Collision.Count > 0,
                "gameplay meshing retains collision triangles for partial block geometry");
        PayloadPool.Release(perFace);

        // The live menu preview uses the same render mesher but does not need
        // collision output. Verify that this avoids triangle construction,
        // while restoring the normal gameplay setting before the greedy pass.
        world.BuildCollision = false;
        MeshPayload renderOnly = Mesher.Build(world, Vector2I.Zero);
        TestAssert.Equal(0, renderOnly.Collision.Count,
                "render-only mesh payload omits collision triangles");
        TestAssert.Check(renderOnly.Opaque.Vertex.Count > 0,
                "render-only mesh payload retains visible voxel geometry");
        PayloadPool.Release(renderOnly);
        world.BuildCollision = true;

        Mesher.Greedy = true;
        MeshPayload greedy = Mesher.Build(world, Vector2I.Zero);
        CheckSharedCornerContinuity(greedy.Opaque, "greedy");
        CheckRendererCornerParity(perFaceCornerColors, greedy.Opaque);
        geometryChunk!.ApplyPayload(greedy);
        TestAssert.Equal(greedy.Opaque.Vertex.Count, greedy.Opaque.Uv2.Count,
                "greedy and split opaque faces carry texture-array tile indices");
        TestAssert.Equal(greedy.Opaque.Vertex.Count, greedy.Opaque.Color.Count,
                "greedy and split shape faces preserve AO/voxel-light attributes");
        CheckVertexShade(greedy.Opaque, new Vector3(2, 101, 4), Vector3.Right,
                0.68f, "greedy cube corner uses the same bottom-slab occupancy AO");
        CheckVertexShade(greedy.Opaque, new Vector3(5, 101, 4), Vector3.Right,
                0.80f, "greedy cube corner ignores the unoccupied top-slab half");
        CheckVertexShade(greedy.Opaque, new Vector3(16, 101, 0), Vector3.Right,
                0.80f, "greedy AO reads the diagonal chunk's partial-shape state");
        CheckVertexShade(greedy.Opaque, new Vector3(8, 100.5f, 5.5f), BlockDb.FaceDirs[5],
                0.5525f, "greedy mode keeps upright stair-riser AO aligned");
        CheckVertexShade(greedy.Opaque, new Vector3(10, 100.5f, 5.5f), BlockDb.FaceDirs[5],
                0.5525f, "greedy mode keeps inverted stair-riser AO aligned");
        TestAssert.Check(CheckBlockUvMapping(greedy.Opaque, new Vector3(2, 100, 2),
                    BlockId.PlanksSlab, BlockShapes.SlabBottom, true) > 0,
                "greedy texture-array slab faces preserve full-block UV scale and tile layer");
        TestAssert.Check(CheckBlockUvMapping(greedy.Opaque, new Vector3(8, 100, 2),
                    BlockId.PlanksStairs, 3, true) > 0,
                "greedy texture-array stair faces preserve rotated grain and tile layer");
        TestAssert.Check(CheckBlockUvMapping(greedy.Opaque, new Vector3(11, 100, 4),
                    BlockId.Stone, 0, true, faceFilter: 2) > 0,
                "greedy texture-array clipped cube faces retain continuous tile coordinates");
        TestAssert.Check(greedy.Opaque.Color.Exists(c => Mathf.IsEqualApprox(c.A,
                        (float)SurfaceFamily.Wood)),
                "greedy faces preserve the same material-response family tags");
        double greedyArea = SurfaceArea(greedy.Opaque);
        TestAssert.Check(Math.Abs(perFaceArea - greedyArea) < 0.001,
                "greedy and per-face meshing preserve equal surface area around shapes");
        TestAssert.Check(Math.Abs(AreaOnPlane(greedy.Opaque, 0, 12.0f, 100.0f, 101.0f, 4.0f, 5.0f) - 0.5) < 0.001,
                "greedy path preserves partial boundary faces between a cube and slab");
        PayloadPool.Release(greedy);
        Mesher.Greedy = originalGreedy;

        // A loaded 1-cell tunnel isolates a shape-state edit from alternate
        // paths. Changing the same slab from bottom to double must invalidate
        // the packed 3x3 neighbourhood; restoring the cavity must relight it.
        for (int y = 100; y <= 102; y++)
            for (int z = 5; z <= 7; z++)
                for (int x = 13; x <= 21; x++)
                    world.SetBlock(x, y, z, BlockId.Stone);
        for (int x = 13; x <= 20; x++)
            world.SetBlock(x, 101, 6, BlockId.Air);
        TestAssert.Check(world.SetBlock(14, 101, 6, BlockId.Torch),
                "isolated cross-chunk voxel-light tunnel accepts its torch through SetBlock");
        TestAssert.Check(world.SetBlock(17, 101, 6, BlockId.PlanksSlab, BlockShapes.SlabBottom),
                "neighbouring chunk accepts the light-passage slab through SetBlock");
        Vector2I lightChunk = new(1, 0);
        VoxelLightData bottomSlabLight = world.LightData(lightChunk);
        byte throughOpenCavity = bottomSlabLight.BlockAt(3, 101, 6, world.Height);
        TestAssert.Check(throughOpenCavity > 0,
                "baked block light reaches the far side of an open slab cavity across the chunk boundary");

        TestAssert.Check(world.SetBlock(17, 101, 6, BlockId.PlanksSlab, BlockShapes.SlabDouble),
                "changing a neighbouring-chunk slab state succeeds through the shared SetBlock seam");
        VoxelLightData doubleSlabLight = world.LightData(lightChunk);
        TestAssert.Equal((byte)0, doubleSlabLight.BlockAt(3, 101, 6, world.Height),
                "cross-chunk shape-state mutation invalidates cached light and a double slab blocks the tunnel");

        TestAssert.Check(world.SetBlock(17, 101, 6, BlockId.PlanksSlab, BlockShapes.SlabBottom),
                "restoring the partial slab state succeeds through SetBlock");
        VoxelLightData restoredSlabLight = world.LightData(lightChunk);
        TestAssert.Check(restoredSlabLight.BlockAt(3, 101, 6, world.Height) > 0,
                "restoring the open slab cavity rebuilds continuous baked block light across the chunk seam");

        world.SaveNow();
        host.RemoveChild(world);
        world.QueueFree();

        GameSession.Instance.SelectWorld(dir);
        VoxelWorld loaded = new();
        host.AddChild(loaded);
        loaded.BuildPreview(Vector3.Zero, 1);
        TestAssert.Equal(BlockShapes.SlabTop, loaded.GetBlockState(4, 100, 2),
                "world reload restores top-slab state from the optional save extension");
        TestAssert.Check(loaded.TryEditTop(13, 4, out int restoredFarmlandTop, out BlockId restoredFarmland),
                "world reload reconstructs the edited surface material lookup");
        TestAssert.Equal(100, restoredFarmlandTop, "reloaded farmland keeps its edited surface height");
        TestAssert.Equal(BlockId.Farmland, restoredFarmland,
                "reloaded farmland remains visible to the far LOD material path");
        TestAssert.Check(loaded.SetBlock(13, 100, 4, BlockId.Air),
                "removing the top farmland edit succeeds through SetBlock");
        TestAssert.Check(loaded.TryEditTop(13, 4, out int exposedSoilTop, out BlockId exposedSoil),
                "removing farmland exposes the next edited soil layer");
        TestAssert.Equal(99, exposedSoilTop, "exposed dirt layer becomes the edited surface height");
        TestAssert.Equal(BlockId.Dirt, exposedSoil,
                "exposed dirt material replaces farmland in the far LOD surface cache");
        TestAssert.Equal(BlockShapes.SlabDouble, loaded.GetBlockState(6, 100, 2),
                "world reload restores double-slab state");
        TestAssert.Equal((byte)3, loaded.GetBlockState(8, 100, 2),
                "world reload restores stair orientation");
        TestAssert.Equal((byte)0, loaded.GetBlockState(2, 100, 2),
                "default bottom-slab state loads without an extension entry");

        // A chunk whose last non-default state is removed returns to the
        // zero-allocation default representation.
        TestAssert.Check(loaded.SetBlock(17, 100, 2, BlockId.PlanksSlab, BlockShapes.SlabTop),
                "second chunk accepts a non-default shape state");
        TestAssert.Equal(VoxelWorld.ChunkX * loaded.Height * VoxelWorld.ChunkZ,
                loaded.ChunkBlockStates(new Vector2I(1, 0)).Length,
                "second chunk allocates its state buffer on first non-default state");
        TestAssert.Check(loaded.SetBlock(17, 100, 2, BlockId.Air),
                "removing the last stateful block succeeds through SetBlock");
        TestAssert.Equal(0, loaded.ChunkBlockStates(new Vector2I(1, 0)).Length,
                "state buffer is released when a chunk returns to default states");

        host.RemoveChild(loaded);
        loaded.QueueFree();
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    // The opaque surface is emitted as triangle-list quads (six vertices each).
    // Ignore duplicates from a quad's own two triangles and compare only vertices
    // shared by distinct coplanar quads: those are the places where a dark seam
    // can appear if adjacent AO/light stencils disagree.
    private readonly record struct SharedCornerKey(
            int X2, int Y2, int Z2, int NormalX, int NormalY, int NormalZ);

    private readonly record struct SharedCornerSample(Color Color, int QuadIndex);

    private static SharedCornerKey GetSharedCornerKey(SurfaceData surface, int index)
    {
        Vector3 position = surface.Vertex[index];
        Vector3 normal = surface.Normal[index];
        return new SharedCornerKey(
                Mathf.RoundToInt(position.X * 2.0f),
                Mathf.RoundToInt(position.Y * 2.0f),
                Mathf.RoundToInt(position.Z * 2.0f),
                Mathf.RoundToInt(normal.X),
                Mathf.RoundToInt(normal.Y),
                Mathf.RoundToInt(normal.Z));
    }

    private static bool SameBakedChannels(Color a, Color b) =>
            Mathf.Abs(a.R - b.R) <= 0.0001f
            && Mathf.Abs(a.G - b.G) <= 0.0001f
            && Mathf.Abs(a.B - b.B) <= 0.0001f;

    private static Dictionary<SharedCornerKey, Color> CaptureCornerColors(SurfaceData surface)
    {
        Dictionary<SharedCornerKey, Color> colors = new(surface.Vertex.Count);
        for (int i = 0; i < surface.Vertex.Count; i++)
            colors.TryAdd(GetSharedCornerKey(surface, i), surface.Color[i]);
        return colors;
    }

    private static void CheckSharedCornerContinuity(SurfaceData surface, string renderer)
    {
        Dictionary<SharedCornerKey, SharedCornerSample> firstSamples = new(surface.Vertex.Count);
        int sharedCorners = 0;
        int mismatchedCorners = 0;
        SharedCornerKey firstMismatch = default;

        for (int i = 0; i < surface.Vertex.Count; i++)
        {
            SharedCornerKey key = GetSharedCornerKey(surface, i);
            int quadIndex = i / 6;
            if (!firstSamples.TryGetValue(key, out SharedCornerSample previous))
            {
                firstSamples.Add(key, new SharedCornerSample(surface.Color[i], quadIndex));
                continue;
            }

            if (previous.QuadIndex == quadIndex)
                continue;

            sharedCorners++;
            if (!SameBakedChannels(previous.Color, surface.Color[i]))
            {
                mismatchedCorners++;
                firstMismatch = key;
            }
        }

        TestAssert.Check(sharedCorners > 0,
                $"{renderer} mesh exposes shared vertices across independently emitted quads");
        TestAssert.Equal(0, mismatchedCorners,
                $"{renderer} mesh has continuous sky, block-light and AO channels at shared corners"
                + (mismatchedCorners == 0 ? string.Empty
                        : $" (first mismatch at half-voxel ({firstMismatch.X2}, {firstMismatch.Y2}, {firstMismatch.Z2}))"));
    }

    private static void CheckRendererCornerParity(
            Dictionary<SharedCornerKey, Color> perFaceColors, SurfaceData greedy)
    {
        int comparedCorners = 0;
        int mismatchedCorners = 0;
        SharedCornerKey firstMismatch = default;

        for (int i = 0; i < greedy.Vertex.Count; i++)
        {
            SharedCornerKey key = GetSharedCornerKey(greedy, i);
            if (!perFaceColors.TryGetValue(key, out Color perFaceColor))
                continue;

            comparedCorners++;
            if (!SameBakedChannels(perFaceColor, greedy.Color[i]))
            {
                mismatchedCorners++;
                firstMismatch = key;
            }
        }

        TestAssert.Check(comparedCorners > 0,
                "greedy mesh shares geometric corners with the per-face reference mesh");
        TestAssert.Equal(0, mismatchedCorners,
                "greedy and per-face meshes agree on every common baked-light/AO corner"
                + (mismatchedCorners == 0 ? string.Empty
                        : $" (first mismatch at half-voxel ({firstMismatch.X2}, {firstMismatch.Y2}, {firstMismatch.Z2}))"));
    }

    private static void CheckVertexShade(SurfaceData surface, Vector3 position,
            Vector3 normal, float expected, string message)
    {
        float actual = float.NaN;
        for (int i = 0; i < surface.Vertex.Count; i++)
        {
            if (surface.Vertex[i].DistanceSquaredTo(position) < 0.000001f
                    && surface.Normal[i].DistanceSquaredTo(normal) < 0.000001f)
            {
                actual = surface.Color[i].B;
                break;
            }
        }

        TestAssert.Check(float.IsFinite(actual) && Mathf.Abs(actual - expected) < 0.0001f,
                $"{message} (expected shade {expected:F4}, got {actual:F4})");
    }

    private static int CheckBlockUvMapping(SurfaceData surface, Vector3 blockOrigin,
            BlockId id, byte state, bool greedy, int faceFilter = -1)
    {
        Vector3 blockMax = blockOrigin + Vector3.One;
        int matchedTriangles = 0;
        for (int i = 0; i + 2 < surface.Vertex.Count; i += 3)
        {
            bool inside = true;
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 point = surface.Vertex[i + vertex];
                inside &= point.X >= blockOrigin.X - 0.0001f && point.X <= blockMax.X + 0.0001f
                        && point.Y >= blockOrigin.Y - 0.0001f && point.Y <= blockMax.Y + 0.0001f
                        && point.Z >= blockOrigin.Z - 0.0001f && point.Z <= blockMax.Z + 0.0001f;
            }
            if (!inside) continue;

            int face = -1;
            Vector3 normal = surface.Normal[i];
            for (int candidate = 0; candidate < 6; candidate++)
            {
                if (normal.Dot(BlockDb.FaceDirs[candidate]) > 0.999f)
                {
                    face = candidate;
                    break;
                }
            }
            if (face < 0 || (faceFilter >= 0 && face != faceFilter)) continue;

            int tile = BlockDb.ShapeFaceTile(id, state, face);
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector2 expectedLocal = BlockDb.BlockFaceUvAt(
                        id, state, face, surface.Vertex[i + vertex] - blockOrigin);
                Vector2 expected = greedy
                        ? expectedLocal
                        : BlockDb.MapUv(BlockDb.TileUvRect(tile), expectedLocal);
                TestAssert.Check((surface.Uv[i + vertex] - expected).Length() < 0.0001f,
                        $"{id} face {face} vertex {i + vertex} has continuous block-relative UVs");
                if (greedy)
                    TestAssert.Check(Mathf.Abs(surface.Uv2[i + vertex].X - tile) < 0.0001f,
                            $"{id} face {face} keeps its atlas-array layer");
            }
            matchedTriangles++;
        }

        return matchedTriangles;
    }

    private static int TriangleCountInCell(SurfaceData surface, int x, int y, int z) =>
        TriangleCountInBounds(surface.Vertex, new Vector3(x, y, z), new Vector3(x + 1, y + 1, z + 1));

    private static int CollisionTriangleCountInCell(System.Collections.Generic.List<Vector3> vertices,
            int x, int y, int z) =>
        TriangleCountInBounds(vertices, new Vector3(x, y, z), new Vector3(x + 1, y + 1, z + 1));

    private static int TriangleCountInBounds(System.Collections.Generic.IReadOnlyList<Vector3> vertices,
            Vector3 min, Vector3 max)
    {
        int count = 0;
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            bool inside = true;
            for (int v = 0; v < 3; v++)
            {
                Vector3 p = vertices[i + v];
                inside &= p.X >= min.X - 0.0001f && p.X <= max.X + 0.0001f
                        && p.Y >= min.Y - 0.0001f && p.Y <= max.Y + 0.0001f
                        && p.Z >= min.Z - 0.0001f && p.Z <= max.Z + 0.0001f;
            }
            if (inside) count++;
        }
        return count;
    }

    private static double SurfaceArea(SurfaceData surface)
    {
        double area = 0.0;
        for (int i = 0; i + 2 < surface.Vertex.Count; i += 3)
        {
            Vector3 a = surface.Vertex[i];
            Vector3 b = surface.Vertex[i + 1];
            Vector3 c = surface.Vertex[i + 2];
            area += (b - a).Cross(c - a).Length() * 0.5;
        }
        return area;
    }

    private static double AreaOnPlane(SurfaceData surface, int axis, float plane,
            float minA, float maxA, float minB, float maxB)
    {
        int axisA = (axis + 1) % 3;
        int axisB = (axis + 2) % 3;
        double area = 0.0;
        for (int i = 0; i + 2 < surface.Vertex.Count; i += 3)
        {
            Vector3 a = surface.Vertex[i];
            Vector3 b = surface.Vertex[i + 1];
            Vector3 c = surface.Vertex[i + 2];
            Vector3[] triangle = [a, b, c];
            bool onPlane = true;
            foreach (Vector3 p in triangle)
                onPlane &= Mathf.Abs(p[axis] - plane) < 0.0001f
                        && p[axisA] >= minA - 0.0001f && p[axisA] <= maxA + 0.0001f
                        && p[axisB] >= minB - 0.0001f && p[axisB] <= maxB + 0.0001f;
            if (onPlane) area += (b - a).Cross(c - a).Length() * 0.5;
        }
        return area;
    }
}
