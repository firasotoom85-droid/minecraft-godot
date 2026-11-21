using System;
using Godot;
using System.Linq;

namespace Minecraft;

public static class BlockShapeTests
{
    public static void Run(Node host)
    {
        Run();
        CheckChunkGeometryAndPersistence(host);
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
        TestAssert.Equal(perFace.Opaque.Vertex.Count, perFace.Opaque.Normal.Count,
                "shape surfaces carry a normal for every emitted vertex");
        TestAssert.Equal(perFace.Opaque.Vertex.Count, perFace.Opaque.Color.Count,
                "shape surfaces carry AO and voxel-light color for every emitted vertex");
        TestAssert.Equal(perFace.Opaque.Vertex.Count, perFace.Opaque.Uv.Count,
                "shape surfaces carry atlas UVs for every emitted vertex");
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
        PayloadPool.Release(perFace);

        Mesher.Greedy = true;
        MeshPayload greedy = Mesher.Build(world, Vector2I.Zero);
        geometryChunk!.ApplyPayload(greedy);
        TestAssert.Equal(greedy.Opaque.Vertex.Count, greedy.Opaque.Uv2.Count,
                "greedy and split opaque faces carry texture-array tile indices");
        TestAssert.Equal(greedy.Opaque.Vertex.Count, greedy.Opaque.Color.Count,
                "greedy and split shape faces preserve AO/voxel-light attributes");
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
