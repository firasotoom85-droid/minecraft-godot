using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// One packed byte per voxel: sky light in the high nibble and block light in
/// the low nibble. Each channel ranges from 0 (dark) to 15 (full intensity).
/// </summary>
public sealed class VoxelLightData(byte[] packed)
{
    public readonly byte[] Packed = packed;

    public static int Index(int x, int y, int z, int height) =>
        x + z * VoxelWorld.ChunkX + y * VoxelWorld.ChunkX * VoxelWorld.ChunkZ;

    public byte SkyAt(int x, int y, int z, int height) =>
        (byte)(Packed[Index(x, y, z, height)] >> 4);

    public byte BlockAt(int x, int y, int z, int height) =>
        (byte)(Packed[Index(x, y, z, height)] & 0x0F);
}

public readonly record struct VoxelLightSource(Vector3I Position, byte Level);

/// <summary>
/// CPU voxel-light solver. Lighting is calculated when a chunk is meshed,
/// cached as compact voxel data, and baked into vertex colours. It does not
/// allocate scene lights or perform propagation work every frame.
/// </summary>
public static class VoxelLighting
{
    public const int MaxLevel = 15;
    public const int BlockLightRadius = 14;
    private const int RegionSize = VoxelWorld.ChunkX + BlockLightRadius * 2;

    [ThreadStatic]
    private static Queue<int>? _queueScratch;

    [ThreadStatic]
    private static Queue<(int Index, byte Level)>? _removalQueueScratch;

    private static Queue<int> RentQueue()
    {
        _queueScratch ??= new Queue<int>(4096);
        _queueScratch.Clear();
        return _queueScratch;
    }

    private static int Opacity(byte id) =>
        id < BlockDb.LightOpacity.Length ? BlockDb.LightOpacity[id] : MaxLevel;

    private static int Index(int x, int y, int z, int width, int depth) =>
        x + z * width + y * width * depth;

    /// <summary>
    /// Direct skylight pass followed by a local flood into open caves. Opaque
    /// blocks stop sun; glass transmits it; water and foliage attenuate it.
    /// </summary>
    public static byte[] BuildSkyLight(byte[] blocks, int width, int height, int depth)
    {
        BlockDb.EnsureTables();
        if (blocks.Length != width * height * depth)
            throw new ArgumentException("Block volume dimensions do not match its data.", nameof(blocks));

        byte[] sky = new byte[blocks.Length];
        int plane = width * depth;

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int level = MaxLevel;
                for (int y = height - 1; y >= 0; y--)
                {
                    int i = x + z * width + y * plane;
                    int opacity = Opacity(blocks[i]);
                    if (opacity >= MaxLevel)
                    {
                        level = 0;
                        continue;
                    }

                    level = Math.Max(0, level - opacity);
                    sky[i] = (byte)level;
                }
            }
        }

        Queue<int> queue = RentQueue();
        // Only darker neighbours are enqueued, so open columns do not flood
        // the queue with duplicate level-15 cells.
        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = Index(x, y, z, width, depth);
                    int level = sky[i];
                    if (level <= 1) continue;
                    SeedSky(blocks, sky, queue, x - 1, y, z, level, width, height, depth);
                    SeedSky(blocks, sky, queue, x + 1, y, z, level, width, height, depth);
                    SeedSky(blocks, sky, queue, x, y - 1, z, level, width, height, depth);
                    SeedSky(blocks, sky, queue, x, y + 1, z, level, width, height, depth);
                    SeedSky(blocks, sky, queue, x, y, z - 1, level, width, height, depth);
                    SeedSky(blocks, sky, queue, x, y, z + 1, level, width, height, depth);
                }
            }
        }

        FloodSky(blocks, sky, queue, width, height, depth);
        return sky;
    }

    /// <summary>
    /// Merge sky entering through adjacent chunk borders, then flood it through
    /// the destination chunk. Neighbours provide local-only maps, preventing
    /// recursive chunk cache dependencies.
    /// </summary>
    public static byte[] MergeNeighbourSky(
            byte[] blocks, int height, byte[] localSky,
            byte[]? west, byte[]? east, byte[]? north, byte[]? south)
    {
        int width = VoxelWorld.ChunkX;
        int depth = VoxelWorld.ChunkZ;
        byte[] sky = (byte[])localSky.Clone();
        Queue<int> queue = RentQueue();

        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (west != null)
                    SeedEdge(blocks, sky, queue, Index(0, y, z, width, depth),
                            west[Index(width - 1, y, z, width, depth)]);
                if (east != null)
                    SeedEdge(blocks, sky, queue, Index(width - 1, y, z, width, depth),
                            east[Index(0, y, z, width, depth)]);
            }

            for (int x = 0; x < width; x++)
            {
                if (north != null)
                    SeedEdge(blocks, sky, queue, Index(x, y, 0, width, depth),
                            north[Index(x, y, depth - 1, width, depth)]);
                if (south != null)
                    SeedEdge(blocks, sky, queue, Index(x, y, depth - 1, width, depth),
                            south[Index(x, y, 0, width, depth)]);
            }
        }

        FloodSky(blocks, sky, queue, width, height, depth);
        return sky;
    }

    private static void SeedEdge(byte[] blocks, byte[] sky, Queue<int> queue, int target, byte source)
    {
        int candidate = source - Math.Max(1, Opacity(blocks[target]));
        if (candidate > sky[target])
        {
            sky[target] = (byte)candidate;
            queue.Enqueue(target);
        }
    }

    private static void SeedSky(
            byte[] blocks, byte[] sky, Queue<int> queue,
            int x, int y, int z, int source, int width, int height, int depth)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height || (uint)z >= (uint)depth)
            return;

        int target = Index(x, y, z, width, depth);
        int candidate = source - Math.Max(1, Opacity(blocks[target]));
        if (candidate > sky[target])
        {
            sky[target] = (byte)candidate;
            queue.Enqueue(target);
        }
    }

    private static void FloodSky(
            byte[] blocks, byte[] sky, Queue<int> queue,
            int width, int height, int depth)
    {
        int plane = width * depth;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int level = sky[i];
            if (level <= 1) continue;
            int x = i % width;
            int z = (i / width) % depth;
            int y = i / plane;
            if (x > 0) SpreadSky(blocks, sky, queue, i - 1, level);
            if (x + 1 < width) SpreadSky(blocks, sky, queue, i + 1, level);
            if (y > 0) SpreadSky(blocks, sky, queue, i - plane, level);
            if (y + 1 < height) SpreadSky(blocks, sky, queue, i + plane, level);
            if (z > 0) SpreadSky(blocks, sky, queue, i - width, level);
            if (z + 1 < depth) SpreadSky(blocks, sky, queue, i + width, level);
        }
    }

    private static void SpreadSky(byte[] blocks, byte[] sky, Queue<int> queue, int target, int source)
    {
        int candidate = source - Math.Max(1, Opacity(blocks[target]));
        if (candidate > sky[target])
        {
            sky[target] = (byte)candidate;
            queue.Enqueue(target);
        }
    }

    /// <summary>
    /// Multi-source block-light flood fill. Sources use local coordinates, so
    /// adjacent chunks can be tested as one volume across their shared border.
    /// </summary>
    public static byte[] PropagateBlockLight(
            byte[] blocks, int width, int height, int depth,
            IReadOnlyList<VoxelLightSource> sources)
    {
        BlockDb.EnsureTables();
        if (blocks.Length != width * height * depth)
            throw new ArgumentException("Block volume dimensions do not match its data.", nameof(blocks));

        byte[] light = new byte[blocks.Length];
        Queue<int> queue = RentQueue();
        int plane = width * depth;

        foreach (VoxelLightSource source in sources)
        {
            Vector3I p = source.Position;
            if (source.Level == 0 || (uint)p.X >= (uint)width
                    || (uint)p.Y >= (uint)height || (uint)p.Z >= (uint)depth)
                continue;

            int i = Index(p.X, p.Y, p.Z, width, depth);
            if (source.Level > light[i])
            {
                light[i] = source.Level;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int level = light[i];
            if (level <= 1) continue;
            int x = i % width;
            int z = (i / width) % depth;
            int y = i / plane;
            if (x > 0) SpreadBlock(blocks, light, queue, i - 1, level);
            if (x + 1 < width) SpreadBlock(blocks, light, queue, i + 1, level);
            if (y > 0) SpreadBlock(blocks, light, queue, i - plane, level);
            if (y + 1 < height) SpreadBlock(blocks, light, queue, i + plane, level);
            if (z > 0) SpreadBlock(blocks, light, queue, i - width, level);
            if (z + 1 < depth) SpreadBlock(blocks, light, queue, i + width, level);
        }

        return light;
    }

    /// <summary>
    /// Applies one emitter change to an existing block-light volume. Removal
    /// first clears values whose only support was the removed light, then
    /// re-floods from surviving boundaries and emitters. Addition only visits
    /// cells whose light level can increase.
    /// </summary>
    public static void ApplyIncrementalBlockLight(
            byte[] blocks, int width, int height, int depth,
            byte[] light, int changedIndex, byte newEmission)
    {
        BlockDb.EnsureTables();
        int volume = width * height * depth;
        if (blocks.Length < volume || light.Length < volume)
            throw new ArgumentException("Block and light volumes must have matching dimensions.");
        if ((uint)changedIndex >= (uint)volume)
            throw new ArgumentOutOfRangeException(nameof(changedIndex));

        newEmission = (byte)Math.Min(MaxLevel, (int)newEmission);
        byte oldLevel = light[changedIndex];
        if (newEmission >= oldLevel)
        {
            if (newEmission == oldLevel) return;
            light[changedIndex] = newEmission;
            Queue<int> additions = RentQueue();
            additions.Enqueue(changedIndex);
            FloodBlockLight(blocks, light, additions, width, height, depth);
            return;
        }

        Queue<(int Index, byte Level)> removals = _removalQueueScratch ??= new Queue<(int, byte)>(4096);
        removals.Clear();
        Queue<int> relight = RentQueue();
        light[changedIndex] = 0;
        removals.Enqueue((changedIndex, oldLevel));

        int plane = width * depth;
        while (removals.Count > 0)
        {
            (int index, byte removedLevel) = removals.Dequeue();
            int x = index % width;
            int z = (index / width) % depth;
            int y = index / plane;
            if (x > 0) RemoveBlockNeighbour(blocks, light, removals, relight, index - 1, removedLevel);
            if (x + 1 < width) RemoveBlockNeighbour(blocks, light, removals, relight, index + 1, removedLevel);
            if (y > 0) RemoveBlockNeighbour(blocks, light, removals, relight, index - plane, removedLevel);
            if (y + 1 < height) RemoveBlockNeighbour(blocks, light, removals, relight, index + plane, removedLevel);
            if (z > 0) RemoveBlockNeighbour(blocks, light, removals, relight, index - width, removedLevel);
            if (z + 1 < depth) RemoveBlockNeighbour(blocks, light, removals, relight, index + width, removedLevel);
        }

        byte ownEmission = Emission(blocks[changedIndex]);
        if (ownEmission > 0)
        {
            light[changedIndex] = Math.Max(light[changedIndex], ownEmission);
            relight.Enqueue(changedIndex);
        }

        FloodBlockLight(blocks, light, relight, width, height, depth);
    }

    private static byte Emission(byte blockId) =>
        blockId < BlockDb.LightEmission.Length ? BlockDb.LightEmission[blockId] : (byte)0;

    private static void RemoveBlockNeighbour(
            byte[] blocks, byte[] light,
            Queue<(int Index, byte Level)> removals, Queue<int> relight,
            int target, byte removedLevel)
    {
        byte current = light[target];
        if (current == 0) return;

        byte ownEmission = Emission(blocks[target]);
        if (ownEmission > 0)
        {
            if (ownEmission > current) light[target] = ownEmission;
            relight.Enqueue(target);
            return;
        }

        if (current < removedLevel)
        {
            light[target] = 0;
            removals.Enqueue((target, current));
        }
        else
        {
            relight.Enqueue(target);
        }
    }

    private static void FloodBlockLight(
            byte[] blocks, byte[] light, Queue<int> queue,
            int width, int height, int depth)
    {
        int plane = width * depth;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int level = light[i];
            if (level <= 1) continue;
            int x = i % width;
            int z = (i / width) % depth;
            int y = i / plane;
            if (x > 0) SpreadBlock(blocks, light, queue, i - 1, level);
            if (x + 1 < width) SpreadBlock(blocks, light, queue, i + 1, level);
            if (y > 0) SpreadBlock(blocks, light, queue, i - plane, level);
            if (y + 1 < height) SpreadBlock(blocks, light, queue, i + plane, level);
            if (z > 0) SpreadBlock(blocks, light, queue, i - width, level);
            if (z + 1 < depth) SpreadBlock(blocks, light, queue, i + width, level);
        }
    }

    private static void SpreadBlock(byte[] blocks, byte[] light, Queue<int> queue, int target, int source)
    {
        int candidate = source - Math.Max(1, Opacity(blocks[target]));
        if (candidate > light[target])
        {
            light[target] = (byte)candidate;
            queue.Enqueue(target);
        }
    }

    /// <summary>
    /// Builds a chunk's block-light map from a 14-cell halo. This includes
    /// sources and opaque blockers in unloaded neighbour chunks as well.
    /// </summary>
    public static byte[] BuildBlockLightForChunk(VoxelWorld world, Vector2I cp, int height)
    {
        int width = RegionSize;
        int depth = RegionSize;
        int originX = cp.X * VoxelWorld.ChunkX - BlockLightRadius;
        int originZ = cp.Y * VoxelWorld.ChunkZ - BlockLightRadius;
        int chunkStride = VoxelWorld.ChunkX * VoxelWorld.ChunkZ;

        byte[][] chunks = new byte[9][];
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                chunks[(dz + 1) * 3 + dx + 1] = world.ChunkData(cp + new Vector2I(dx, dz));

        byte[] region = new byte[width * height * depth];
        for (int z = 0; z < depth; z++)
        {
            int zGroup = z < BlockLightRadius ? 0
                    : z < BlockLightRadius + VoxelWorld.ChunkZ ? 1 : 2;
            int localZ = z < BlockLightRadius ? z + VoxelWorld.ChunkZ - BlockLightRadius
                    : z < BlockLightRadius + VoxelWorld.ChunkZ ? z - BlockLightRadius
                    : z - BlockLightRadius - VoxelWorld.ChunkZ;

            for (int y = 0; y < height; y++)
            {
                int dst = (y * depth + z) * width;
                int src = y * chunkStride + localZ * VoxelWorld.ChunkX;
                byte[] west = chunks[zGroup * 3];
                byte[] center = chunks[zGroup * 3 + 1];
                byte[] east = chunks[zGroup * 3 + 2];
                Array.Copy(west, src + VoxelWorld.ChunkX - BlockLightRadius, region, dst, BlockLightRadius);
                Array.Copy(center, src, region, dst + BlockLightRadius, VoxelWorld.ChunkX);
                Array.Copy(east, src, region, dst + BlockLightRadius + VoxelWorld.ChunkX,
                        VoxelWorld.ChunkX - BlockLightRadius);
            }
        }

        List<VoxelLightSource> nearby = world.NearbyLightSources(
                originX, originX + width - 1, originZ, originZ + depth - 1);
        List<VoxelLightSource> localSources = new(nearby.Count);
        foreach (VoxelLightSource source in nearby)
        {
            localSources.Add(new VoxelLightSource(
                    new Vector3I(source.Position.X - originX, source.Position.Y, source.Position.Z - originZ),
                    source.Level));
        }

        byte[] regionLight = PropagateBlockLight(region, width, height, depth, localSources);
        byte[] result = new byte[VoxelWorld.ChunkX * height * VoxelWorld.ChunkZ];
        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < VoxelWorld.ChunkZ; z++)
            {
                int src = (y * depth + z + BlockLightRadius) * width + BlockLightRadius;
                int dst = (y * VoxelWorld.ChunkZ + z) * VoxelWorld.ChunkX;
                Array.Copy(regionLight, src, result, dst, VoxelWorld.ChunkX);
            }
        }

        return result;
    }

    public static byte Pack(byte sky, byte block) =>
        (byte)((Math.Min(MaxLevel, (int)sky) << 4) | Math.Min(MaxLevel, (int)block));

    /// <summary>
    /// Interpolates the eight baked sky-light samples around a point. Samples
    /// are ordered with X changing fastest, then Y, then Z. Keeping this small
    /// helper allocation-free lets camera exposure follow voxel light smoothly
    /// without scanning chunks or introducing runtime light nodes.
    /// </summary>
    public static float InterpolateSkyLight(
            byte c000, byte c100, byte c010, byte c110,
            byte c001, byte c101, byte c011, byte c111,
            float x, float y, float z)
    {
        x = Mathf.Clamp(x, 0.0f, 1.0f);
        y = Mathf.Clamp(y, 0.0f, 1.0f);
        z = Mathf.Clamp(z, 0.0f, 1.0f);

        float x00 = Mathf.Lerp(c000, c100, x);
        float x10 = Mathf.Lerp(c010, c110, x);
        float x01 = Mathf.Lerp(c001, c101, x);
        float x11 = Mathf.Lerp(c011, c111, x);
        float y0 = Mathf.Lerp(x00, x10, y);
        float y1 = Mathf.Lerp(x01, x11, y);
        return Mathf.Clamp(Mathf.Lerp(y0, y1, z), 0.0f, MaxLevel);
    }

    /// <summary>Shader channels: sky, warm block emission, and AO/face shade.</summary>
    public static Color VertexColor(byte packed, float shade) =>
        new((packed >> 4) / 15.0f, (packed & 0x0F) / 15.0f,
                Mathf.Clamp(shade, 0.0f, 1.0f), 1.0f);

    /// <summary>Average light from four packed samples surrounding a face corner.</summary>
    public static byte AverageCorner(byte a, byte b, byte c, byte d)
    {
        int sky = ((a >> 4) + (b >> 4) + (c >> 4) + (d >> 4) + 2) / 4;
        int block = ((a & 15) + (b & 15) + (c & 15) + (d & 15) + 2) / 4;
        return Pack((byte)sky, (byte)block);
    }

    /// <summary>Average light from the four voxel samples around a face corner.</summary>
    public static byte AverageCorner(byte[] lightPad, int i, int du, int dv) =>
        AverageCorner(lightPad[i], lightPad[i + du],
                lightPad[i + dv], lightPad[i + du + dv]);
}
