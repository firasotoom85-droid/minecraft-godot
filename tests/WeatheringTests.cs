using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace Minecraft;

/// <summary>Regression coverage for deterministic V9 structure weathering.</summary>
public static class WeatheringTests
{
    public static void Run(Node host)
    {
        CheckMaterialSelectionIsDeterministicAndBounded();
        CheckV9ChangesOnlyStructureMaterials(host);
        CheckV9ChunkOrderIsStable(host);
    }

    private static void CheckMaterialSelectionIsDeterministicAndBounded()
    {
        VoxelWorld.StructureInfo ruin = new(1, 0, 60, 0, 0xBEEF1234u);
        int mossyStone = 0;
        int wornStone = 0;
        int crackedBrick = 0;
        int mossyBrick = 0;
        for (int y = 61; y <= 66; y++)
        for (int z = -32; z < 32; z++)
        for (int x = -32; x < 32; x++)
        {
            BlockId stone = WorldGenV9.WeatheredMaterial(
                    ruin, BlockId.Cobblestone, x, y, z);
            BlockId brick = WorldGenV9.WeatheredMaterial(
                    ruin, BlockId.Brick, x, y, z);
            switch (stone)
            {
                case BlockId.MossyCobblestone: mossyStone++; break;
                case BlockId.WeatheredCobblestone: wornStone++; break;
                default:
                    TestAssert.Equal(BlockId.Cobblestone, stone,
                            "stone weathering keeps the source material or selects a stone variant");
                    break;
            }
            switch (brick)
            {
                case BlockId.CrackedBrick: crackedBrick++; break;
                case BlockId.MossyBrick: mossyBrick++; break;
                default:
                    TestAssert.Equal(BlockId.Brick, brick,
                            "brick weathering keeps the source material or selects a brick variant");
                    break;
            }

            TestAssert.Equal(stone, WorldGenV9.WeatheredMaterial(
                    ruin, BlockId.Cobblestone, x, y, z),
                    "stone weathering is deterministic at a coordinate");
            TestAssert.Equal(brick, WorldGenV9.WeatheredMaterial(
                    ruin, BlockId.Brick, x, y, z),
                    "brick weathering is deterministic at a coordinate");
        }

        TestAssert.Check(mossyStone > 0, "ruins generate mossy cobblestone");
        TestAssert.Check(wornStone > 0, "ruins generate weathered cobblestone");
        TestAssert.Check(crackedBrick > 0, "ruins generate cracked brick");
        TestAssert.Check(mossyBrick > 0, "ruins generate mossy brick");

        TestAssert.Equal(BlockId.Stone, WorldGenV9.WeatheredMaterial(
                ruin, BlockId.Stone, 5, 61, 7),
                "weathering does not alter natural stone or unrelated materials");
        TestAssert.Equal(BlockId.Cobblestone, WorldGenV9.WeatheredMaterial(
                new VoxelWorld.StructureInfo(99, 0, 60, 0, 1u),
                BlockId.Cobblestone, 5, 61, 7),
                "unknown structure kinds do not receive weathering");

        VoxelWorld.StructureInfo shrine = new(5, 0, 60, 0, 0xC0FFEEu);
        TestAssert.Check(WorldGenV9.WeatheredMaterial(
                        shrine, BlockId.Cobblestone, 0, 61, 0) is
                        BlockId.Cobblestone or BlockId.MossyCobblestone or BlockId.WeatheredCobblestone,
                "shrine stone selection stays in the compatible material family");

        for (int kind = 0; kind <= 5; kind++)
        {
            VoxelWorld.StructureInfo structure = new(kind, 0, 64, 0, (uint)(kind + 1));
            TestAssert.Check(WorldGenV9.WeatheredMaterial(
                            structure, BlockId.Cobblestone, 4, 63, 4) != BlockId.MossyCobblestone,
                    $"structure kind {kind} does not grow moss below its surface anchor");
            TestAssert.Check(WorldGenV9.WeatheredMaterial(
                            structure, BlockId.Brick, 4, 63, 4) != BlockId.MossyBrick,
                    $"structure kind {kind} does not grow brick moss below its surface anchor");
            TestAssert.Check(WorldGenV9.WeatheredMaterial(
                            structure, BlockId.Cobblestone, 4, 65, 4) is
                            BlockId.Cobblestone or BlockId.MossyCobblestone or BlockId.WeatheredCobblestone,
                    $"structure kind {kind} uses only supported cobblestone materials");
            TestAssert.Check(WorldGenV9.WeatheredMaterial(
                            structure, BlockId.Brick, 4, 65, 4) is
                            BlockId.Brick or BlockId.CrackedBrick or BlockId.MossyBrick,
                    $"structure kind {kind} uses only supported brick materials");
        }
    }

    private static void CheckV9ChangesOnlyStructureMaterials(Node host)
    {
        const int seed = 1337;
        string v8Dir = CreateWorldDirectory("weathering-v8", seed, WorldGenV8.Version);
        string v9Dir = CreateWorldDirectory("weathering-v9", seed, WorldGenV9.Version);
        VoxelWorld? v8 = null;
        VoxelWorld? v9 = null;
        try
        {
            GameSession.Instance.SelectWorld(v8Dir);
            v8 = new VoxelWorld();
            host.AddChild(v8);
            VoxelWorld.StructureInfo? selected = FindStructure(v8, 1, 12);
            TestAssert.Check(selected is not null, "fixed seed contains a surface ruin");
            if (selected is not { } ruin)
                return;
            GD.Print($"STRUCTURE_WEATHERING_TEST_SITE seed={seed} kind={ruin.Kind} x={ruin.X} y={ruin.Y} z={ruin.Z}");

            GameSession.Instance.SelectWorld(v9Dir);
            v9 = new VoxelWorld();
            host.AddChild(v9);
            int siteCellX = Mathf.FloorToInt(ruin.X / (float)VoxelWorld.StructureCell);
            int siteCellZ = Mathf.FloorToInt(ruin.Z / (float)VoxelWorld.StructureCell);
            VoxelWorld.StructureInfo? upgraded = v9.GetStructureSite(siteCellX, siteCellZ);
            TestAssert.Check(upgraded is { } same && same == ruin,
                    "V9 retains the exact V8 structure site and seed");
            if (upgraded == null)
                return;

            int minChunkX = Mathf.FloorToInt((ruin.X - 4) / (float)VoxelWorld.ChunkX);
            int maxChunkX = Mathf.FloorToInt((ruin.X + 4) / (float)VoxelWorld.ChunkX);
            int minChunkZ = Mathf.FloorToInt((ruin.Z - 4) / (float)VoxelWorld.ChunkZ);
            int maxChunkZ = Mathf.FloorToInt((ruin.Z + 4) / (float)VoxelWorld.ChunkZ);
            List<Vector2I> chunks = [];
            for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
                chunks.Add(new Vector2I(cx, cz));

            byte[][] oldChunks = new byte[chunks.Count][];
            byte[][] newChunks = new byte[chunks.Count][];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < chunks.Count; i++)
                oldChunks[i] = v8.ChunkData(chunks[i]);
            timer.Stop();
            double v8GenerationMs = timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            for (int i = 0; i < chunks.Count; i++)
                newChunks[i] = v9.ChunkData(chunks[i]);
            timer.Stop();
            double v9GenerationMs = timer.Elapsed.TotalMilliseconds;
            GD.Print($"WORLDGEN_WEATHERING_PROFILE chunks={chunks.Count} "
                    + $"v8_ms_per_chunk={v8GenerationMs / chunks.Count:F2} "
                    + $"v9_ms_per_chunk={v9GenerationMs / chunks.Count:F2} "
                    + $"delta_percent={(v9GenerationMs / Math.Max(v8GenerationMs, 0.001) - 1.0) * 100.0:F1}");

            int weathered = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                byte[] oldData = oldChunks[chunkIndex];
                byte[] newData = newChunks[chunkIndex];
                TestAssert.Equal(oldData.Length, newData.Length,
                        "V8 and V9 chunks retain the same storage shape");
                for (int i = 0; i < oldData.Length; i++)
                {
                    BlockId oldId = (BlockId)oldData[i];
                    BlockId newId = (BlockId)newData[i];
                    if (newId is BlockId.MossyCobblestone or BlockId.WeatheredCobblestone
                            or BlockId.CrackedBrick or BlockId.MossyBrick)
                    {
                        weathered++;
                        bool validReplacement =
                                oldId == BlockId.Cobblestone
                                && newId is BlockId.MossyCobblestone or BlockId.WeatheredCobblestone
                                || oldId == BlockId.Brick
                                && newId is BlockId.CrackedBrick or BlockId.MossyBrick;
                        TestAssert.Check(validReplacement,
                                "V9 weathering only substitutes the corresponding V8 stone or brick cell");
                    }
                    else
                    {
                        TestAssert.Equal(oldId, newId,
                                "V9 preserves every non-weathered V8 structure and terrain cell");
                    }
                }
            }

            TestAssert.Check(weathered > 0, "V9 generated ruin contains weathered blocks");
            TestAssert.Equal(WorldGenV9.Version, WorldStore.CurrentGenVersion,
                    "new worlds default to generator V9");
            TestAssert.Equal(WorldGenV8.Version, v8.WorldGenVersion,
                    "legacy V8 world remains pinned to generator V8");
            TestAssert.Equal(WorldGenV9.Version, v9.WorldGenVersion,
                    "new world selects generator V9");
        }
        finally
        {
            if (v8 != null)
            {
                host.RemoveChild(v8);
                v8.QueueFree();
            }
            if (v9 != null)
            {
                host.RemoveChild(v9);
                v9.QueueFree();
            }
            RemoveWorldDirectory(v8Dir);
            RemoveWorldDirectory(v9Dir);
        }
    }

    private static void CheckV9ChunkOrderIsStable(Node host)
    {
        const int seed = 1337;
        string forwardDir = CreateWorldDirectory("weathering-forward", seed, WorldGenV9.Version);
        string reverseDir = CreateWorldDirectory("weathering-reverse", seed, WorldGenV9.Version);
        VoxelWorld? forward = null;
        VoxelWorld? reverse = null;
        try
        {
            GameSession.Instance.SelectWorld(forwardDir);
            forward = new VoxelWorld();
            host.AddChild(forward);
            VoxelWorld.StructureInfo? site = FindStructure(forward, 1, 12);
            TestAssert.Check(site is not null, "fixed seed has a ruin for chunk-order regression");
            if (site is not { } ruin)
                return;

            int minChunkX = Mathf.FloorToInt((ruin.X - 4) / (float)VoxelWorld.ChunkX);
            int maxChunkX = Mathf.FloorToInt((ruin.X + 4) / (float)VoxelWorld.ChunkX);
            int minChunkZ = Mathf.FloorToInt((ruin.Z - 4) / (float)VoxelWorld.ChunkZ);
            int maxChunkZ = Mathf.FloorToInt((ruin.Z + 4) / (float)VoxelWorld.ChunkZ);
            Dictionary<Vector2I, byte[]> forwardChunks = [];
            List<Vector2I> order = [];
            for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                Vector2I chunk = new(cx, cz);
                order.Add(chunk);
                forwardChunks[chunk] = forward.ChunkData(chunk);
            }

            host.RemoveChild(forward);
            forward.QueueFree();
            forward = null;

            GameSession.Instance.SelectWorld(reverseDir);
            reverse = new VoxelWorld();
            host.AddChild(reverse);
            order.Reverse();
            foreach (Vector2I chunk in order)
            {
                byte[] reverseData = reverse.ChunkData(chunk);
                TestAssert.Check(forwardChunks[chunk].AsSpan().SequenceEqual(reverseData),
                        $"V9 weathered chunk {chunk} is independent of generation order");
            }
        }
        finally
        {
            if (forward != null)
            {
                host.RemoveChild(forward);
                forward.QueueFree();
            }
            if (reverse != null)
            {
                host.RemoveChild(reverse);
                reverse.QueueFree();
            }
            RemoveWorldDirectory(forwardDir);
            RemoveWorldDirectory(reverseDir);
        }
    }

    private static VoxelWorld.StructureInfo? FindStructure(VoxelWorld world, int kind, int radius)
    {
        VoxelWorld.StructureInfo? fallback = null;
        for (int cz = -radius; cz <= radius; cz++)
        for (int cx = -radius; cx <= radius; cx++)
        {
            if (world.GetStructureSite(cx, cz) is not { } site || site.Kind != kind)
                continue;

            fallback ??= site;
            Biome biome = world.BiomeAt(site.X, site.Z, site.Y);
            if (biome is Biome.Plains or Biome.FlowerPlains or Biome.Desert or Biome.Snow)
                return site;
        }
        return fallback;
    }

    private static string CreateWorldDirectory(string prefix, int seed, int version)
    {
        string dir = $"user://tests/{prefix}-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                prefix, seed, version, WorldStore.DeriveNoiseSeeds(seed),
                0, 0, "survival", "normal"));
        return dir;
    }

    private static void RemoveWorldDirectory(string dir)
    {
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(dir);
    }
}
