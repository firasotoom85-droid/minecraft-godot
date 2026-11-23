using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Version 12 compatibility gates and pure placement rules. V1-V11 generation
/// remains frozen; new worlds use these rules through the VoxelWorld dispatch.
/// </summary>
public static class WorldGenV12
{
    public const int Version = 12;

    // Keep half-block movement and chunk transforms exactly representable by
    // single-precision world coordinates throughout the supported play area.
    // The upper edge is exclusive: valid block coordinates are [-limit, limit).
    public const int HorizontalBlockLimit = 1 << 22;

    // The largest V7/V8 ore vein has at most eleven distinct candidate blocks.
    // A bounded retry budget allows a random walk to skip revisited positions.
    public const int MaxOreWalkAttempts = 88;

    public static bool ContainsWorldPosition(int x, int z) =>
            x >= -HorizontalBlockLimit && x < HorizontalBlockLimit
            && z >= -HorizontalBlockLimit && z < HorizontalBlockLimit;

    public static bool ContainsChunk(Vector2I chunkPosition)
    {
        long originX = (long)chunkPosition.X * VoxelWorld.ChunkX;
        long originZ = (long)chunkPosition.Y * VoxelWorld.ChunkZ;
        return originX >= -HorizontalBlockLimit
                && originZ >= -HorizontalBlockLimit
                && originX + VoxelWorld.ChunkX <= HorizontalBlockLimit
                && originZ + VoxelWorld.ChunkZ <= HorizontalBlockLimit;
    }

    public static uint CactusHash(WorldGenV8 generator, int x, int z) =>
            generator.CoordinateHash(x, 0, z, 0x43414354u);

    public static bool ShouldCarveSurfaceEntrance(int y, int surfaceHeight,
            int seaLevel, string preset, bool caveBelow) =>
            y == surfaceHeight && preset != "flat"
            && surfaceHeight > seaLevel + 2 && caveBelow;

    /// <summary>
    /// Preserves existing tree blocks except that authored logs outrank leaves.
    /// Canopies may merge, but can never erase a trunk or branch log.
    /// </summary>
    public static BlockId MergeTreeBlock(BlockId current, BlockId candidate)
    {
        if (candidate == BlockId.Air)
        {
            return current;
        }

        if (current == BlockId.Air
                || (IsTreeLog(candidate) && IsTreeLeaf(current)))
        {
            return candidate;
        }

        return current;
    }

    public static bool TryRecordUniqueOrePosition(Span<Vector3I> visited,
            ref int visitedCount, Vector3I position)
    {
        if (visitedCount < 0 || visitedCount > visited.Length)
        {
            return false;
        }

        for (int i = 0; i < visitedCount; i++)
        {
            if (visited[i] == position)
            {
                return false;
            }
        }

        if (visitedCount == visited.Length)
        {
            return false;
        }

        visited[visitedCount++] = position;
        return true;
    }

    private static bool IsTreeLog(BlockId id) =>
            id is BlockId.Log or BlockId.BirchLog;

    private static bool IsTreeLeaf(BlockId id) =>
            id is BlockId.Leaves or BlockId.BirchLeaves or BlockId.SpruceLeaves
                    or BlockId.JungleLeaves or BlockId.SwampLeaves;
}
