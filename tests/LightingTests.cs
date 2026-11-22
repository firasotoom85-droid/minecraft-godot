using System.Collections.Generic;

namespace Minecraft;

/// <summary>Regression coverage for sky/block propagation and chunk seams.</summary>
public static class LightingTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();
        TestCornerAverageChannels();
        TestAssert.Check(BlockDb.LightEmission[(int)BlockId.Torch] >= 13, "torch has a strong nibble emission");
        TestAssert.Check(BlockDb.LightEmission[(int)BlockId.Lava] > 0
                && BlockDb.LightEmission[(int)BlockId.Lava] < BlockDb.LightEmission[(int)BlockId.Torch],
                "lava has a distinct lower emission value");
        TestAssert.Equal((byte)15, BlockDb.LightOpacity[(int)BlockId.Stone], "stone fully blocks voxel light");
        TestAssert.Equal((byte)0, BlockDb.LightOpacity[(int)BlockId.Glass], "glass transmits voxel light");
        TestAssert.Check(BlockDb.LightOpacity[(int)BlockId.Water] > 0
                && BlockDb.LightOpacity[(int)BlockId.Water] < 15, "water attenuates voxel light");

        TestBlockAttenuation();
        TestOpaqueBlocking();
        TestTransparentTransmission();
        TestChunkBoundary();
        TestRemoval();
        TestIncrementalBlockUpdates();
        TestSunlightAndCaves();
    }

    private static void TestCornerAverageChannels()
    {
        byte a = VoxelLighting.Pack(15, 0);
        byte b = VoxelLighting.Pack(7, 4);
        byte c = VoxelLighting.Pack(3, 8);
        byte d = VoxelLighting.Pack(1, 12);
        byte expected = VoxelLighting.Pack(7, 6);
        TestAssert.Equal(expected, VoxelLighting.AverageCorner(a, b, c, d),
                "explicit corner samples average sky and block nibbles independently");

        byte[] samples = [a, b, c, d];
        TestAssert.Equal(expected, VoxelLighting.AverageCorner(samples, 0, 1, 2),
                "array-offset and explicit-sample corner averaging stay equivalent");
    }

    private static void TestBlockAttenuation()
    {
        const int width = 8;
        const int height = 5;
        const int depth = 5;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] sources =
        [
            new(new Godot.Vector3I(1, 2, 2), BlockDb.LightEmission[(int)BlockId.Torch]),
        ];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Equal((byte)14, At(light, width, height, depth, 1, 2, 2), "torch source cell retains emission");
        TestAssert.Equal((byte)13, At(light, width, height, depth, 2, 2, 2), "block light attenuates one level per air voxel");
        TestAssert.Equal((byte)12, At(light, width, height, depth, 3, 2, 2), "block light continues attenuating with distance");
    }

    private static void TestOpaqueBlocking()
    {
        const int width = 9;
        const int height = 7;
        const int depth = 7;
        byte[] blocks = new byte[width * height * depth];
        for (int z = 0; z < depth; z++)
            for (int y = 0; y < height; y++)
                blocks[Idx(width, height, depth, 4, y, z)] = (byte)BlockId.Stone;

        VoxelLightSource[] sources = [new(new Godot.Vector3I(2, 3, 3), 14)];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Equal((byte)0, At(light, width, height, depth, 5, 3, 3),
                "full opaque wall blocks block light without leaking around its edges");
    }

    private static void TestTransparentTransmission()
    {
        const int width = 8;
        const int height = 5;
        const int depth = 5;
        byte[] blocks = new byte[width * height * depth];
        blocks[Idx(width, height, depth, 3, 2, 2)] = (byte)BlockId.Glass;
        VoxelLightSource[] sources = [new(new Godot.Vector3I(1, 2, 2), 14)];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Check(At(light, width, height, depth, 4, 2, 2) > 0,
                "block light passes through glass");
        blocks[Idx(width, height, depth, 3, 2, 2)] = (byte)BlockId.Water;
        byte[] waterLight = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Check(At(waterLight, width, height, depth, 3, 2, 2)
                < At(light, width, height, depth, 3, 2, 2),
                "water attenuates more light than glass");
    }

    private static void TestChunkBoundary()
    {
        // Model two adjacent 16-wide chunks as one volume.
        const int width = 32;
        const int height = 8;
        const int depth = 8;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] sources = [new(new Godot.Vector3I(15, 4, 4), 14)];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Equal((byte)13, At(light, width, height, depth, 16, 4, 4),
                "light crosses the chunk boundary into the adjacent voxel");
        TestAssert.Equal((byte)12, At(light, width, height, depth, 17, 4, 4),
                "light continues attenuating inside the neighbouring chunk");
    }

    private static void TestRemoval()
    {
        const int width = 12;
        const int height = 8;
        const int depth = 8;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] present = [new(new Godot.Vector3I(5, 4, 4), 14)];
        byte[] before = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, present);
        byte[] afterRemoval = VoxelLighting.PropagateBlockLight(blocks, width, height, depth,
                System.Array.Empty<VoxelLightSource>());
        TestAssert.Check(At(before, width, height, depth, 6, 4, 4) > 0,
                "source initially illuminates adjacent cells");
        TestAssert.Equal((byte)0, At(afterRemoval, width, height, depth, 6, 4, 4),
                "re-solving after source removal clears stale block light");
    }

    private static void TestIncrementalBlockUpdates()
    {
        const int width = 32;
        const int height = 8;
        const int depth = 8;
        byte[] blocks = new byte[width * height * depth];
        byte[] incremental = new byte[blocks.Length];
        int first = Idx(width, height, depth, 15, 4, 4);
        int second = Idx(width, height, depth, 22, 4, 4);
        byte torch = BlockDb.LightEmission[(int)BlockId.Torch];

        blocks[first] = (byte)BlockId.Torch;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, first, torch);
        byte[] full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth,
                [new VoxelLightSource(new Godot.Vector3I(15, 4, 4), torch)]);
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental emitter addition matches a full block-light solve");

        blocks[second] = (byte)BlockId.Torch;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, second, torch);
        full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth,
                [
                    new VoxelLightSource(new Godot.Vector3I(15, 4, 4), torch),
                    new VoxelLightSource(new Godot.Vector3I(22, 4, 4), torch),
                ]);
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental second source addition matches full overlapping-source solve");

        blocks[first] = (byte)BlockId.Air;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, first, 0);
        full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth,
                [new VoxelLightSource(new Godot.Vector3I(22, 4, 4), torch)]);
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental source removal preserves the overlapping emitter");

        blocks[second] = (byte)BlockId.Air;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, second, 0);
        full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth, System.Array.Empty<VoxelLightSource>());
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental final source removal clears the complete light volume");
    }

    private static bool EqualBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void TestSunlightAndCaves()
    {
        const int width = 32;
        const int height = 12;
        const int depth = 32;
        byte[] blocks = new byte[width * height * depth];

        // A solid roof produces a genuinely enclosed dark volume.
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
                blocks[Idx(width, height, depth, x, 8, z)] = (byte)BlockId.Stone;

        byte[] sky = VoxelLighting.BuildSkyLight(blocks, width, height, depth);
        TestAssert.Equal((byte)15, At(sky, width, height, depth, 16, 9, 16),
                "open sky receives full direct sunlight");
        TestAssert.Equal((byte)0, At(sky, width, height, depth, 16, 7, 16),
                "opaque roof leaves the enclosed cave dark");

        // Open a vertical skylight shaft and verify light spills sideways below the roof.
        for (int y = 0; y <= 8; y++)
            blocks[Idx(width, height, depth, 0, y, 0)] = (byte)BlockId.Air;
        byte[] shaftSky = VoxelLighting.BuildSkyLight(blocks, width, height, depth);
        byte opening = At(shaftSky, width, height, depth, 0, 7, 0);
        byte caveEdge = At(shaftSky, width, height, depth, 4, 7, 0);
        TestAssert.Equal((byte)15, opening, "open skylight shaft keeps direct sun");
        TestAssert.Check(caveEdge > 0 && caveEdge < opening,
                "skylight diffuses laterally into cave openings");
    }

    private static int Idx(int width, int height, int depth, int x, int y, int z) =>
        x + z * width + y * width * depth;

    private static byte At(byte[] values, int width, int height, int depth, int x, int y, int z) =>
        values[Idx(width, height, depth, x, y, z)];
}
