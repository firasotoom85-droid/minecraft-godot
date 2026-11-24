using System;
using Godot;

namespace Minecraft;

public enum BlockShapeKind : byte
{
    None = 0,
    Slab = 1,
    Stairs = 2,
}

/// <summary>
/// Canonical non-cubic block geometry. Occupancy is a 2x2x2 grid (bit index
/// x + z*2 + y*4), shared by meshing, collision, voxel ray tests and placement.
/// Full cubes deliberately remain on the existing greedy-mesh fast path.
/// </summary>
public static class BlockShapes
{
    public const byte SlabBottom = 0;
    public const byte SlabTop = 1;
    public const byte SlabDouble = 2;
    public const byte StairFacingMask = 0x03;
    public const byte StairUpsideDown = 0x04;

    // Compute the small finite shape/state domain once. The propagation flood
    // queries these masks per neighbour, so a one-time LUT avoids repeating
    // half-cell connectivity work for every light step.
    private static readonly uint[] SlabLightPassageMasks =
            BuildLightPassageMasks(BlockId.PlanksSlab, 3);
    private static readonly uint[] StairLightPassageMasks =
            BuildLightPassageMasks(BlockId.PlanksStairs, 8);

    public static BlockShapeKind Kind(BlockId id) => id switch
    {
        BlockId.PlanksSlab or BlockId.CobblestoneSlab or BlockId.BrickSlab => BlockShapeKind.Slab,
        BlockId.PlanksStairs or BlockId.CobblestoneStairs or BlockId.BrickStairs => BlockShapeKind.Stairs,
        _ => BlockShapeKind.None,
    };

    public static bool IsShape(BlockId id) => Kind(id) != BlockShapeKind.None;

    public static bool IsValidState(BlockId id, byte state) => Kind(id) switch
    {
        BlockShapeKind.Slab => state <= SlabDouble,
        BlockShapeKind.Stairs => state <= 7,
        _ => state == 0,
    };

    public static byte NormalizeState(BlockId id, byte state) => IsValidState(id, state) ? state : (byte)0;

    public static byte OccupancyMask(BlockId id, byte state)
    {
        if (!BlockDb.IsSolid(id)) return 0;
        state = NormalizeState(id, state);
        return Kind(id) switch
        {
            BlockShapeKind.Slab => state switch
            {
                SlabBottom => 0x0F,
                SlabTop => 0xF0,
                _ => 0xFF,
            },
            BlockShapeKind.Stairs => StairMask(state),
            _ => 0xFF,
        };
    }

    private static byte StairMask(byte state)
    {
        int facing = state & StairFacingMask;
        bool upsideDown = (state & StairUpsideDown) != 0;
        // In each layer, bits use x + z*2; the raised tread is at the back.
        byte backHalf = facing switch
        {
            0 => 0b1100, // north-facing: z = 1
            1 => 0b0101, // east-facing: x = 0
            2 => 0b0011, // south-facing: z = 0
            _ => 0b1010, // west-facing: x = 1
        };
        return upsideDown
            ? (byte)(0xF0 | backHalf)
            : (byte)(0x0F | (backHalf << 4));
    }

    public static bool Occupied(BlockId id, byte state, int x, int y, int z)
    {
        if ((uint)x > 1 || (uint)y > 1 || (uint)z > 1) return false;
        return (OccupancyMask(id, state) & (1 << (x + z * 2 + y * 4))) != 0;
    }

    /// <summary>True when one occupied micro-cell contributes a visible external face.</summary>
    public static bool FaceExposed(BlockId id, byte state, int x, int y, int z, int face,
            BlockId neighbor, byte neighborState)
    {
        if (!Occupied(id, state, x, y, z) || (uint)face >= 6) return false;
        int nx = x, ny = y, nz = z;
        switch (face)
        {
            case 0: ny++; break;
            case 1: ny--; break;
            case 2: nx++; break;
            case 3: nx--; break;
            case 4: nz++; break;
            case 5: nz--; break;
        }

        if ((uint)nx < 2 && (uint)ny < 2 && (uint)nz < 2)
            return !Occupied(id, state, nx, ny, nz);

        if (!BlockDb.IsSolid(neighbor) || BlockDb.IsTransparent(neighbor)
                || BlockDb.IsFoliage(neighbor)) return true;
        nx = face == 2 ? 0 : face == 3 ? 1 : x;
        ny = face == 0 ? 0 : face == 1 ? 1 : y;
        nz = face == 4 ? 0 : face == 5 ? 1 : z;
        return !Occupied(neighbor, neighborState, nx, ny, nz);
    }

    /// <summary>Number of micro-quad faces emitted before adjacent-block culling.</summary>
    public static int ExposedFaceCount(BlockId id, byte state)
    {
        int count = 0;
        for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                    for (int face = 0; face < 6; face++)
                        if (FaceExposed(id, state, x, y, z, face, BlockId.Air, 0)) count++;
        return count;
    }

    public static int DropCount(BlockId id, byte state, int baseDropCount) =>
        Kind(id) == BlockShapeKind.Slab && NormalizeState(id, state) == SlabDouble
                ? baseDropCount * 2 : baseDropCount;

    /// <summary>
    /// Returns a 24-bit mask of the four open micro-cells on each face that
    /// belong to the shape's connected empty cavity. Face order matches the
    /// mesher: +Y, -Y, +X, -X, +Z, -Z. The four bits on opposite faces use
    /// identical world-axis tangent coordinates so adjacent openings align.
    /// </summary>
    public static uint LightPassageMask(BlockId id, byte state) => Kind(id) switch
    {
        BlockShapeKind.Slab => SlabLightPassageMasks[NormalizeState(id, state)],
        BlockShapeKind.Stairs => StairLightPassageMasks[NormalizeState(id, state)],
        _ => 0x00FF_FFFFu,
    };

    private static uint[] BuildLightPassageMasks(BlockId id, int stateCount)
    {
        uint[] masks = new uint[stateCount];
        for (byte state = 0; state < stateCount; state++)
            masks[state] = CalculateLightPassageMask(id, state);
        return masks;
    }

    private static uint CalculateLightPassageMask(BlockId id, byte state)
    {
        byte empty = (byte)~OccupancyMask(id, state);
        if (empty == 0) return 0;

        Span<byte> pending = stackalloc byte[8];
        byte visited = 0;
        uint faces = 0;
        int componentCount = 0;

        for (int start = 0; start < 8; start++)
        {
            byte startBit = (byte)(1 << start);
            if ((empty & startBit) == 0 || (visited & startBit) != 0) continue;

            // A single packed block-light value cannot represent disconnected
            // cavities independently. Keep such a future shape opaque until it
            // has a dedicated multi-component light representation.
            if (++componentCount > 1) return 0;

            int head = 0;
            int tail = 0;
            pending[tail++] = (byte)start;
            visited |= startBit;

            while (head < tail)
            {
                int cell = pending[head++];
                int x = cell & 1;
                int z = (cell >> 1) & 1;
                int y = (cell >> 2) & 1;

                if (y == 1) AddFaceAperture(ref faces, 0, x, y, z);
                if (y == 0) AddFaceAperture(ref faces, 1, x, y, z);
                if (x == 1) AddFaceAperture(ref faces, 2, x, y, z);
                if (x == 0) AddFaceAperture(ref faces, 3, x, y, z);
                if (z == 1) AddFaceAperture(ref faces, 4, x, y, z);
                if (z == 0) AddFaceAperture(ref faces, 5, x, y, z);

                if (x > 0) PushEmpty(cell - 1, empty, ref visited, pending, ref tail);
                if (x < 1) PushEmpty(cell + 1, empty, ref visited, pending, ref tail);
                if (z > 0) PushEmpty(cell - 2, empty, ref visited, pending, ref tail);
                if (z < 1) PushEmpty(cell + 2, empty, ref visited, pending, ref tail);
                if (y > 0) PushEmpty(cell - 4, empty, ref visited, pending, ref tail);
                if (y < 1) PushEmpty(cell + 4, empty, ref visited, pending, ref tail);
            }
        }

        return faces;
    }

    /// <summary>Four aligned open micro-cells on one face (bits 0..3).</summary>
    public static byte LightPassageFaceMask(BlockId id, byte state, int face)
    {
        if ((uint)face >= 6) return 0;
        return (byte)((LightPassageMask(id, state) >> (face * 4)) & 0x0F);
    }

    private static void AddFaceAperture(ref uint faces, int face, int x, int y, int z)
    {
        int tangentIndex = face switch
        {
            0 or 1 => x + z * 2,
            2 or 3 => z + y * 2,
            _ => x + y * 2,
        };
        faces |= 1u << (face * 4 + tangentIndex);
    }

    private static void PushEmpty(int cell, byte empty, ref byte visited,
            Span<byte> pending, ref int tail)
    {
        byte bit = (byte)(1 << cell);
        if ((empty & bit) == 0 || (visited & bit) != 0) return;
        visited |= bit;
        pending[tail++] = (byte)cell;
    }

    public static bool IsFullCube(BlockId id, byte state) => OccupancyMask(id, state) == 0xFF;

    public static bool IsPartialSolid(BlockId id, byte state) =>
        BlockDb.IsSolid(id) && !BlockDb.IsTransparent(id) && !BlockDb.IsFoliage(id) && !IsFullCube(id, state);

    public static byte SelectSlabState(Vector3 hitNormal, Vector3 localHitPoint)
    {
        if (hitNormal.Y > 0.5f) return SlabBottom;
        if (hitNormal.Y < -0.5f) return SlabTop;
        return localHitPoint.Y >= 0.5f ? SlabTop : SlabBottom;
    }

    /// <summary>
    /// A matching slab becomes double only when the clicked horizontal face
    /// selects the already occupied half. Side clicks place an adjacent slab.
    /// </summary>
    public static bool CanCombineSlab(BlockId placedId, BlockId targetId, byte targetState,
            Vector3 hitNormal, Vector3 localHitPoint) =>
        Kind(placedId) == BlockShapeKind.Slab && placedId == targetId
        && targetState != SlabDouble && Mathf.Abs(hitNormal.Y) > 0.5f
        && NormalizeState(targetId, targetState) == SelectSlabState(hitNormal, localHitPoint);

    /// <summary>Facing follows the player's horizontal look direction; side clicks above halfway invert stairs.</summary>
    public static byte StairStateFromPlacement(Vector3 hitNormal, Vector3 localHitPoint, float playerYaw)
    {
        Vector3 forward = new(-Mathf.Sin(playerYaw), 0.0f, -Mathf.Cos(playerYaw));
        int facing = Mathf.Abs(forward.X) > Mathf.Abs(forward.Z)
            ? (forward.X >= 0.0f ? 1 : 3)
            : (forward.Z >= 0.0f ? 2 : 0);
        bool upsideDown = hitNormal.Y < -0.5f
            || (Mathf.Abs(hitNormal.Y) < 0.5f && localHitPoint.Y >= 0.5f);
        return (byte)(facing | (upsideDown ? StairUpsideDown : 0));
    }

    public static Vector3I CellStepFromNormal(Vector3 normal)
    {
        int axis = Mathf.Abs(normal.X) >= Mathf.Abs(normal.Y) && Mathf.Abs(normal.X) >= Mathf.Abs(normal.Z)
            ? 0 : Mathf.Abs(normal.Y) >= Mathf.Abs(normal.Z) ? 1 : 2;
        int sign = normal[axis] >= 0.0f ? 1 : -1;
        return axis switch
        {
            0 => new Vector3I(sign, 0, 0),
            1 => new Vector3I(0, sign, 0),
            _ => new Vector3I(0, 0, sign),
        };
    }

    /// <summary>Exact ray-vs-shape intersection, clipped to the DDA cell interval.</summary>
    public static bool Raycast(
        Vector3 origin, Vector3 direction, Vector3I block, BlockId id, byte state,
        float minDistance, float maxDistance, out float hitDistance, out Vector3 hitNormal)
    {
        hitDistance = float.PositiveInfinity;
        hitNormal = Vector3.Zero;
        if (!IsShape(id) || IsFullCube(id, state)) return false;

        const float half = 0.5f;
        for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    if (!Occupied(id, state, x, y, z)) continue;
                    Vector3 min = new(block.X + x * half, block.Y + y * half, block.Z + z * half);
                    Vector3 max = min + new Vector3(half, half, half);
                    if (!RayAabb(origin, direction, min, max, out float distance, out Vector3 normal)) continue;
                    if (distance < minDistance - 0.0001f || distance > maxDistance + 0.0001f) continue;
                    if (distance < hitDistance)
                    {
                        hitDistance = Mathf.Max(distance, 0.0f);
                        hitNormal = normal;
                    }
                }

        return float.IsFinite(hitDistance);
    }

    private static bool RayAabb(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max,
        out float distance, out Vector3 normal)
    {
        float near = float.NegativeInfinity;
        float far = float.PositiveInfinity;
        Vector3 nearNormal = Vector3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis];
            float d = direction[axis];
            if (Mathf.Abs(d) < 0.000001f)
            {
                if (o < min[axis] || o > max[axis])
                {
                    distance = 0.0f;
                    normal = Vector3.Zero;
                    return false;
                }
                continue;
            }

            float t1 = (min[axis] - o) / d;
            float t2 = (max[axis] - o) / d;
            float sign = -1.0f;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                sign = 1.0f;
            }
            if (t1 > near)
            {
                near = t1;
                nearNormal = Vector3.Zero;
                nearNormal[axis] = sign;
            }
            far = Mathf.Min(far, t2);
            if (near > far)
            {
                distance = 0.0f;
                normal = Vector3.Zero;
                return false;
            }
        }

        if (far < 0.0f)
        {
            distance = 0.0f;
            normal = Vector3.Zero;
            return false;
        }
        distance = Mathf.Max(near, 0.0f);
        normal = near >= 0.0f ? nearNormal : -direction.Normalized();
        return true;
    }

    /// <summary>Exact player AABB occupancy test; touching boundaries is not an overlap.</summary>
    public static bool IntersectsAabb(BlockId id, byte state, Vector3 blockOrigin, Vector3 aabbMin, Vector3 aabbMax)
    {
        byte mask = OccupancyMask(id, state);
        if (mask == 0) return false;
        const float epsilon = 0.0001f;
        for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    if ((mask & (1 << (x + z * 2 + y * 4))) == 0) continue;
                    Vector3 min = blockOrigin + new Vector3(x * 0.5f, y * 0.5f, z * 0.5f);
                    Vector3 max = min + new Vector3(0.5f, 0.5f, 0.5f);
                    if (aabbMax.X > min.X + epsilon && aabbMin.X < max.X - epsilon
                        && aabbMax.Y > min.Y + epsilon && aabbMin.Y < max.Y - epsilon
                        && aabbMax.Z > min.Z + epsilon && aabbMin.Z < max.Z - epsilon)
                        return true;
                }
        return false;
    }
}
