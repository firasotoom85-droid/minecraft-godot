using System;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace Minecraft;

/// <summary>
/// Chunk mesher: turns block data into raw triangle arrays (+ collision
/// triangles) with per-vertex ambient occlusion baked into vertex colors.
///
/// Pure and thread-safe: reads chunk data through <c>world.ChunkData()</c> and
/// never touches the scene tree, so it can run on WorkerThreadPool workers.
/// The caller applies the returned payload on the main thread.
/// </summary>
public static class Mesher
{
    public const int SX = 16;
    public const int SZ = 16;
    public const int PX = SX + 2;
    public const int PZ = SZ + 2;
    public const int PLANE = PX * PZ;

    /// <summary>Greedy meshing flag (set from Settings; read by workers).</summary>
    private static bool _greedy;
    private static long _configurationRevision;
    private static readonly Lock ConfigurationLock = new();

    public static bool Greedy
    {
        get => Volatile.Read(ref _greedy);
        set
        {
            lock (ConfigurationLock)
            {
                if (_greedy == value) return;
                Volatile.Write(ref _greedy, value);
                Interlocked.Increment(ref _configurationRevision);
            }
        }
    }

    internal static (bool Greedy, long Revision) CaptureConfiguration()
    {
        lock (ConfigurationLock) return (_greedy, _configurationRevision);
    }

    internal static bool IsConfigurationCurrent(long revision) =>
            Interlocked.Read(ref _configurationRevision) == revision;

    // Two triangles per quad, preserving the clockwise winding of the face verts.
    public static readonly int[] TriOrder = [0, 1, 2, 0, 2, 3];

    // Padded-array index offsets of the 6 face neighbours (+Y, -Y, +X, -X, +Z, -Z).
    private static readonly int[] FaceOff = [PLANE, -PLANE, 1, -1, PX, -PX];

    // Classic voxel corner AO: both sides occluded -> 0, else 3 - (s1 + s2 + corner).
    private static readonly float[] AoFactors = [0.5f, 0.7f, 0.85f, 1.0f];

    // Cached face-axis lookup tables. Corner occlusion is sampled on a
    // half-voxel occupancy grid so slabs/stairs and full cubes share the same
    // geometric corner rules without changing greedy mesh topology.
    private static readonly Lock TableLock = new();

    // Greedy-meshing sweep tables, derived per face: the two tangent axes, and
    // which world tangent axis each UV component scales with (1 tile per block).
    private static int[] _normalAxis = [];
    private static int[] _uAxis = [];
    private static int[] _vAxis = [];
    private static int[] _uvAxisOf = [];

    public static void EnsureTables()
    {
        lock (TableLock)
        {
            if (_normalAxis.Length != 0)
            {
                return;
            }

            BlockDb.EnsureTables();
            _normalAxis = new int[6];
            _uAxis = new int[6];
            _vAxis = new int[6];
            _uvAxisOf = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1];
            for (int f = 0; f < 6; f++)
            {
                List<int> axes = [];
                for (int axis = 0; axis < 3; axis++)
                {
                    bool varies = false;
                    for (int i = 1; i < 4; i++)
                    {
                        if (Mathf.Abs(BlockDb.FaceVerts[f * 4 + i][axis] - BlockDb.FaceVerts[f * 4][axis]) > 0.5f)
                        {
                            varies = true;
                            break;
                        }
                    }

                    if (varies)
                    {
                        axes.Add(axis);
                    }
                    else
                    {
                        _normalAxis[f] = axis;
                    }
                }

                int uAxis = axes[0];
                int vAxis = axes[1];
                _uAxis[f] = uAxis;
                _vAxis[f] = vAxis;
                // Which tangent axis does each UV component scale with? Found by
                // comparing two corners that differ along exactly one tangent axis.
                for (int i = 0; i < 4; i++)
                {
                    for (int j = i + 1; j < 4; j++)
                    {
                        Vector3 d = BlockDb.FaceVerts[f * 4 + j] - BlockDb.FaceVerts[f * 4 + i];
                        int changed = -1;
                        int changes = 0;
                        for (int a = 0; a < 3; a++)
                        {
                            if (Mathf.Abs(d[a]) > 0.5f)
                            {
                                changed = a;
                                changes++;
                            }
                        }

                        if (changes != 1 || changed == _normalAxis[f])
                        {
                            continue;
                        }

                        Vector2 duv = BlockDb.FaceUvs[f * 4 + j] - BlockDb.FaceUvs[f * 4 + i];
                        if (Mathf.Abs(duv.X) > 0.5f)
                        {
                            _uvAxisOf[f * 2] = changed;
                        }
                        else if (Mathf.Abs(duv.Y) > 0.5f)
                        {
                            _uvAxisOf[f * 2 + 1] = changed;
                        }
                    }
                }
            }
        }
    }

    private static int MicroBlockOffset(int coordinate) =>
        coordinate < 0 ? -1 : coordinate > 1 ? 1 : 0;

    private static int MicroLocalCoordinate(int coordinate) =>
        coordinate < 0 ? 1 : coordinate > 1 ? 0 : coordinate;

    /// <summary>
    /// Map a half-voxel cell coordinate around one block into the padded voxel
    /// array. Coordinates -1 and 2 cross into the adjacent voxel; all other
    /// values address one of the block's two occupancy cells on that axis.
    /// </summary>
    private static int MicroSampleIndex(int sourceIndex, int x, int y, int z) =>
        sourceIndex + MicroBlockOffset(x) + MicroBlockOffset(y) * PLANE
                + MicroBlockOffset(z) * PX;

    private static bool IsMicroOccluder(
            byte[] pad, byte[] statePad, int sourceIndex, int x, int y, int z)
    {
        int sampleIndex = MicroSampleIndex(sourceIndex, x, y, z);
        BlockId id = (BlockId)pad[sampleIndex];
        if (BlockDb.Occl[(int)id] == 0) return false;
        if (!BlockShapes.IsShape(id)) return true;

        return BlockShapes.Occupied(id, statePad[sampleIndex],
                MicroLocalCoordinate(x), MicroLocalCoordinate(y), MicroLocalCoordinate(z));
    }

    private static void GetCubeCornerMicroCell(int face, int vertex,
            out int x, out int y, out int z)
    {
        Vector3 corner = BlockDb.FaceVerts[face * 4 + vertex];
        int normalAxis = _normalAxis[face];
        Vector3 direction = BlockDb.FaceDirs[face];
        x = normalAxis == 0 ? (direction.X > 0.0f ? 1 : 0) : (corner.X > 0.5f ? 1 : 0);
        y = normalAxis == 1 ? (direction.Y > 0.0f ? 1 : 0) : (corner.Y > 0.5f ? 1 : 0);
        z = normalAxis == 2 ? (direction.Z > 0.0f ? 1 : 0) : (corner.Z > 0.5f ? 1 : 0);
    }

    /// <summary>
    /// Samples ambient occlusion and packed voxel light at one emitted face
    /// corner. The same half-voxel coordinates are used by cubes, clipped
    /// cube faces, and slab/stair micro-faces.
    /// </summary>
    private static void SampleFaceCorner(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[]? directLightPad,
            int sourceIndex, int face, int cellX, int cellY, int cellZ, int vertex,
            bool applyAo, out byte packedLight, out byte bouncedSky, out int ao)
    {
        Vector3 corner = BlockDb.FaceVerts[face * 4 + vertex];
        Vector3 direction = BlockDb.FaceDirs[face];
        int x = cellX + (int)direction.X;
        int y = cellY + (int)direction.Y;
        int z = cellZ + (int)direction.Z;
        int signU = corner[_uAxis[face]] > 0.5f ? 1 : -1;
        int signV = corner[_vAxis[face]] > 0.5f ? 1 : -1;

        int ux = x + (_uAxis[face] == 0 ? signU : 0);
        int uy = y + (_uAxis[face] == 1 ? signU : 0);
        int uz = z + (_uAxis[face] == 2 ? signU : 0);
        int vx = x + (_vAxis[face] == 0 ? signV : 0);
        int vy = y + (_vAxis[face] == 1 ? signV : 0);
        int vz = z + (_vAxis[face] == 2 ? signV : 0);

        int baseIndex = MicroSampleIndex(sourceIndex, x, y, z);
        int sideUIndex = MicroSampleIndex(sourceIndex, ux, uy, uz);
        int sideVIndex = MicroSampleIndex(sourceIndex, vx, vy, vz);
        int cornerIndex = MicroSampleIndex(sourceIndex, ux + (_vAxis[face] == 0 ? signV : 0),
                uy + (_vAxis[face] == 1 ? signV : 0),
                uz + (_vAxis[face] == 2 ? signV : 0));

        byte combinedLight = VoxelLighting.AverageCorner(
                lightPad[baseIndex], lightPad[sideUIndex],
                lightPad[sideVIndex], lightPad[cornerIndex]);
        byte directSky = directLightPad == null
                ? (byte)(combinedLight >> 4)
                : VoxelLighting.AverageLevelCorner(
                        directLightPad[baseIndex], directLightPad[sideUIndex],
                        directLightPad[sideVIndex], directLightPad[cornerIndex]);
        bouncedSky = VoxelLighting.SkyBounce((byte)(combinedLight >> 4), directSky);
        packedLight = VoxelLighting.Pack(directSky, (byte)(combinedLight & 0x0F));

        if (!applyAo)
        {
            ao = 3;
            return;
        }

        int side1 = IsMicroOccluder(pad, statePad, sourceIndex, ux, uy, uz) ? 1 : 0;
        int side2 = IsMicroOccluder(pad, statePad, sourceIndex, vx, vy, vz) ? 1 : 0;
        int diagonal = IsMicroOccluder(pad, statePad, sourceIndex,
                ux + (_vAxis[face] == 0 ? signV : 0),
                uy + (_vAxis[face] == 1 ? signV : 0),
                uz + (_vAxis[face] == 2 ? signV : 0)) ? 1 : 0;
        ao = side1 == 1 && side2 == 1 ? 0 : 3 - side1 - side2 - diagonal;
    }

    /// <summary>
    /// Build the render payload and, for gameplay chunks, its collision geometry.
    /// The epoch is captured before any data is read; the caller discards the
    /// payload if the world was edited meanwhile.
    /// </summary>
    public static MeshPayload Build(VoxelWorld world, Vector2I cp)
    {
        EnsureTables();
        (bool greedy, long meshingRevision) = CaptureConfiguration();
        long epoch;
        int boundaryMask;
        byte[] pad;
        byte[] statePad;
        byte[] lightPad;
        byte[] directLightPad;
        int sy = world.Height;
        lock (world.SnapshotLock)
        {
            epoch = world.EditEpoch;
            byte[] data = world.ChunkData(cp);
            byte[] states = world.ChunkBlockStates(cp);
            pad = BuildPadded(world, cp, data, sy);
            statePad = BuildPaddedStates(world, cp, states, sy);
        }
        // Collider availability is independent of the voxel snapshot and is
        // revalidated when the payload is applied.
        boundaryMask = world.MissingColliderBoundaryMask(cp);
        // Derived lighting is solved outside the world snapshot critical section.
        lightPad = BuildLightPadded(world, cp, sy, out directLightPad);

        SurfaceData opaque = PayloadPool.RentSurface();
        SurfaceData glass = PayloadPool.RentSurface();
        SurfaceData water = PayloadPool.RentSurface();
        SurfaceData lava = PayloadPool.RentSurface();
        bool buildCollision = world.BuildCollision;
        List<Vector3> collision = PayloadPool.RentCollision();

        // Reused per face; all four entries are always (re)assigned before use.
        Span<float> kk = stackalloc float[4];
        Span<byte> cornerLight = stackalloc byte[4];
        Span<byte> cornerBounce = stackalloc byte[4];

        // Greedy mode: merged coplanar quads for the opaque surface (using the
        // texture-array material). The per-face loop below then only emits the
        // remaining surfaces and the collision for opaque blocks.
        if (greedy)
        {
            BuildOpaqueGreedy(pad, statePad, lightPad, directLightPad, sy, opaque);
        }

        for (int y = 1; y <= sy; y++)
        {
            for (int z = 1; z <= SZ; z++)
            {
                for (int x = 1; x <= SX; x++)
                {
                    int i = (y * PZ + z) * PX + x;
                    BlockId id = (BlockId)pad[i];
                    if (id == BlockId.Air)
                    {
                        continue;
                    }

                    // Cross plants (grass/flowers): two double-sided diagonal
                    // quads, no cube faces, no collision, no AO.
                    if (BlockDb.IsCross(id))
                    {
                        bool wetPlant = BlockDb.IsShorelineVegetation(id)
                                && HasHorizontalWaterNeighborAtOrBelow(pad, i);
                        byte combinedLight = lightPad[i];
                        byte directSky = directLightPad[i];
                        byte bouncedSky = VoxelLighting.SkyBounce(
                                (byte)(combinedLight >> 4), directSky);
                        Color directColor = VoxelLighting.VertexColor(
                                VoxelLighting.Pack(directSky, (byte)(combinedLight & 0x0F)), 1.0f);
                        AddCross(opaque, new Vector3(x - 1, y - 1, z - 1),
                                BlockDb.IconTile(id), directColor, bouncedSky / 15.0f, wetPlant, greedy);
                        continue;
                    }

                    byte state = statePad[i];
                    bool fullCube = BlockShapes.IsFullCube(id, state);
                    // Skip only when every adjacent cell completely covers the
                    // corresponding face; partial shapes cannot hide a whole face.
                    bool missingColliderFrontier = buildCollision
                            && (IsMissingColliderBoundary(boundaryMask, x - 1, z - 1, 2)
                                    || IsMissingColliderBoundary(boundaryMask, x - 1, z - 1, 3)
                                    || IsMissingColliderBoundary(boundaryMask, x - 1, z - 1, 4)
                                    || IsMissingColliderBoundary(boundaryMask, x - 1, z - 1, 5));
                    if (fullCube && BlockDb.Occl[(int)id] == 1
                            && FullyEnclosedByFullCubes(pad, statePad, i)
                            && !missingColliderFrontier)
                        continue;

                    // Shape geometry is deliberately separate from the cube
                    // fast path; it shares its lighting/AO/material and collision.
                    if (BlockShapes.IsShape(id) && !fullCube)
                    {
                        AddShapeGeometry(pad, statePad, lightPad, directLightPad, i,
                                new Vector3(x - 1, y - 1, z - 1), x - 1, z - 1,
                                id, state, opaque, buildCollision ? collision : null,
                                boundaryMask, greedy, kk, cornerLight, cornerBounce);
                        continue;
                    }

                    int baseIdx = (int)id * 6;
                    bool isTrans = BlockDb.TransMask[(int)id] == 1;
                    bool isWater = BlockDb.IsWater(id);
                    bool greedyBase = greedy && !isTrans && !isWater && id != BlockId.Lava && fullCube;
                    // Ladder atlas transparency is a cutout mask, not blended glass.
                    bool blended = isTrans && id != BlockId.Ladder;
                    SurfaceData surf = isWater ? water : id == BlockId.Lava ? lava : blended ? glass : opaque;
                    float waterTop = isWater ? BlockDb.WaterHeight(id) : 1.0f;
                    if (isWater && BlockDb.IsWater((BlockId)pad[i + FaceOff[0]]))
                        waterTop = 1.0f;

                    Vector3 b = new(x - 1, y - 1, z - 1);
                    for (int f = 0; f < 6; f++)
                    {
                        int tile = BlockDb.Tiles[baseIdx + f];
                        if (tile < 0)
                        {
                            continue;
                        }

                        int neighborIndex = i + FaceOff[f];
                        int nb = pad[neighborIndex];
                        bool renderFace = BlockDb.Render[(int)id * BlockDb.BlockCount + nb] == 1;
                        BlockId neighborId = (BlockId)nb;
                        bool partialNeighbor = BlockShapes.IsShape(neighborId)
                                && !BlockShapes.IsFullCube(neighborId, statePad[neighborIndex])
                                && BlockDb.IsSolid(neighborId) && !BlockDb.IsTransparent(neighborId);
                        if (partialNeighbor && fullCube && !isTrans)
                            renderFace = true;
                        float waterBase = 0.0f;
                        if (isWater && BlockDb.IsWater((BlockId)nb))
                        {
                            renderFace = false;
                            if (f >= 2)
                            {
                                waterBase = WaterSurfaceHeight(pad, neighborIndex);
                                renderFace = waterTop > waterBase;
                            }
                        }
                        if (!renderFace)
                        {
                            if (buildCollision && BlockDb.SolidMask[(int)id] == 1
                                    && IsMissingColliderBoundary(boundaryMask, x - 1, z - 1, f))
                                AppendCollisionFace(collision, b, f);
                            continue;
                        }

                        int vi = f * 4;
                        bool greedyFace = greedyBase && !partialNeighbor;
                        if (greedyFace)
                        {
                            // Quad already emitted by BuildOpaqueGreedy; here we
                            // only need the collision triangle pair.
                            if (buildCollision && BlockDb.SolidMask[(int)id] == 1)
                            {
                                collision.Add(b + BlockDb.FaceVerts[vi]);
                                collision.Add(b + BlockDb.FaceVerts[vi + 1]);
                                collision.Add(b + BlockDb.FaceVerts[vi + 2]);
                                collision.Add(b + BlockDb.FaceVerts[vi]);
                                collision.Add(b + BlockDb.FaceVerts[vi + 2]);
                                collision.Add(b + BlockDb.FaceVerts[vi + 3]);
                            }

                            continue;
                        }

                        if (partialNeighbor && fullCube && !isTrans)
                        {
                            EmitCubeFaceAgainstPartial(pad, statePad, lightPad, directLightPad,
                                    i, neighborIndex, f, tile, id, state, b, opaque,
                                    buildCollision ? collision : null, greedy, kk, cornerLight, cornerBounce);
                            continue;
                        }

                        int ui = (tile * 6 + f) * 4;
                        float shade = BlockDb.FaceColors[f].R;
                        for (int v4 = 0; v4 < 4; v4++)
                        {
                            GetCubeCornerMicroCell(f, v4, out int cellX, out int cellY, out int cellZ);
                            SampleFaceCorner(pad, statePad, lightPad, directLightPad, i, f,
                                    cellX, cellY, cellZ, v4, !isTrans,
                                    out cornerLight[v4], out cornerBounce[v4], out int ao);
                            kk[v4] = shade * AoFactors[ao];
                        }

                        Vector3 normal = BlockDb.FaceNormals[f];
                        foreach (int t in TriOrder)
                        {
                            Vector3 vertex = BlockDb.FaceVerts[vi + t];
                            if (isWater)
                                vertex.Y = f >= 2 ? waterBase + vertex.Y * (waterTop - waterBase) : vertex.Y * waterTop;
                            surf.Vertex.Add(b + vertex);
                            surf.Normal.Add(normal);
                            Color litColor = VoxelLighting.VertexColor(cornerLight[t], kk[t]);
                            SurfaceFamily family = MaterialResponse.ForTile(tile);
                            bool wetFace = BlockDb.IsWettable(id) && BlockDb.IsWater(neighborId);
                            surf.Color.Add(new Color(litColor.R, litColor.G, litColor.B,
                                    MaterialResponse.VertexTag(family, wetFace,
                                            cornerBounce[t] / 15.0f)));
                            surf.Uv.Add(greedy && ReferenceEquals(surf, opaque)
                                    ? BlockDb.FaceUvs[vi + t]
                                    : BlockDb.UvLut[ui + t]);
                            if (greedy && ReferenceEquals(surf, opaque))
                                surf.Uv2.Add(new Vector2(tile, 0.0f));
                        }

                        if (buildCollision && BlockDb.SolidMask[(int)id] == 1)
                        {
                            collision.Add(b + BlockDb.FaceVerts[vi]);
                            collision.Add(b + BlockDb.FaceVerts[vi + 1]);
                            collision.Add(b + BlockDb.FaceVerts[vi + 2]);
                            collision.Add(b + BlockDb.FaceVerts[vi]);
                            collision.Add(b + BlockDb.FaceVerts[vi + 2]);
                            collision.Add(b + BlockDb.FaceVerts[vi + 3]);
                        }
                    }
                }
            }
        }

        return new MeshPayload(opaque, glass, water, lava, collision, epoch,
                meshingRevision, boundaryMask);
    }


    private static bool IsMissingColliderBoundary(
            int boundaryMask, int localX, int localZ, int face) => face switch
    {
        2 => localX == SX - 1 && (boundaryMask & (1 << 2)) != 0,
        3 => localX == 0 && (boundaryMask & (1 << 3)) != 0,
        4 => localZ == SZ - 1 && (boundaryMask & (1 << 4)) != 0,
        5 => localZ == 0 && (boundaryMask & (1 << 5)) != 0,
        _ => false,
    };

    private static void AppendCollisionFace(List<Vector3> collision, Vector3 origin, int face)
    {
        int vi = face * 4;
        collision.Add(origin + BlockDb.FaceVerts[vi]);
        collision.Add(origin + BlockDb.FaceVerts[vi + 1]);
        collision.Add(origin + BlockDb.FaceVerts[vi + 2]);
        collision.Add(origin + BlockDb.FaceVerts[vi]);
        collision.Add(origin + BlockDb.FaceVerts[vi + 2]);
        collision.Add(origin + BlockDb.FaceVerts[vi + 3]);
    }

    private static void AppendCollisionFace(List<Vector3> collision, Span<Vector3> points)
    {
        collision.Add(points[0]);
        collision.Add(points[1]);
        collision.Add(points[2]);
        collision.Add(points[0]);
        collision.Add(points[2]);
        collision.Add(points[3]);
    }

    private static bool FullyEnclosedByFullCubes(byte[] pad, byte[] statePad, int index)
    {
        for (int f = 0; f < 6; f++)
        {
            int neighbor = index + FaceOff[f];
            BlockId id = (BlockId)pad[neighbor];
            if (BlockDb.Occl[(int)id] != 1 || !BlockShapes.IsFullCube(id, statePad[neighbor]))
                return false;
        }
        return true;
    }

    private static bool NeighborOccupiesFaceCell(
            byte[] pad, byte[] statePad, int neighborIndex, int face, int u, int v)
    {
        BlockId neighbor = (BlockId)pad[neighborIndex];
        if (!BlockDb.IsSolid(neighbor) || BlockDb.IsTransparent(neighbor)) return false;
        int nAxis = _normalAxis[face];
        int uAxis = _uAxis[face];
        int vAxis = _vAxis[face];
        Span<int> cell = stackalloc int[3];
        cell[nAxis] = BlockDb.FaceDirs[face][nAxis] > 0.0f ? 0 : 1;
        cell[uAxis] = u;
        cell[vAxis] = v;
        return BlockShapes.Occupied(neighbor, statePad[neighborIndex], cell[0], cell[1], cell[2]);
    }

    private static void EmitCubeFaceAgainstPartial(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[] directLightPad,
            int sourceIndex, int neighborIndex, int face, int tile,
            BlockId blockId, byte blockState, Vector3 blockOrigin,
            SurfaceData surf, List<Vector3>? collision, bool greedy,
            Span<float> kk, Span<byte> cornerLight, Span<byte> cornerBounce)
    {
        int vi = face * 4;
        int normalAxis = _normalAxis[face];
        int uAxis = _uAxis[face];
        int vAxis = _vAxis[face];
        Span<int> microCell = stackalloc int[3];
        microCell[normalAxis] = BlockDb.FaceDirs[face][normalAxis] > 0.0f ? 1 : 0;
        Span<Vector3> points = stackalloc Vector3[4];
        for (int v = 0; v < 2; v++)
        for (int u = 0; u < 2; u++)
        {
            if (NeighborOccupiesFaceCell(pad, statePad, neighborIndex, face, u, v)) continue;
            microCell[uAxis] = u;
            microCell[vAxis] = v;
            for (int vertex = 0; vertex < 4; vertex++)
            {
                Vector3 p = BlockDb.FaceVerts[vi + vertex];
                p[uAxis] = (u + p[uAxis]) * 0.5f;
                p[vAxis] = (v + p[vAxis]) * 0.5f;
                points[vertex] = blockOrigin + p;
            }
            bool wetFace = BlockDb.IsWettable(blockId)
                    && BlockDb.IsWater((BlockId)pad[neighborIndex]);
            EmitLitFace(pad, statePad, lightPad, directLightPad, sourceIndex, face, tile,
                    blockId, blockState, blockOrigin, points, surf, collision,
                    microCell[0], microCell[1], microCell[2], false, true,
                    kk, cornerLight, cornerBounce, wetFace, greedy);
        }
    }

    private static void AddShapeGeometry(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[] directLightPad,
            int index, Vector3 blockOrigin, int localX, int localZ,
            BlockId id, byte state, SurfaceData surf, List<Vector3>? collision,
            int boundaryMask, bool greedy,
            Span<float> kk, Span<byte> cornerLight, Span<byte> cornerBounce)
    {
        Span<Vector3> points = stackalloc Vector3[4];
        for (int cy = 0; cy < 2; cy++)
        for (int cz = 0; cz < 2; cz++)
        for (int cx = 0; cx < 2; cx++)
        {
            if (!BlockShapes.Occupied(id, state, cx, cy, cz)) continue;
            for (int face = 0; face < 6; face++)
            {
                bool externalFace = face switch
                {
                    0 => cy == 1,
                    1 => cy == 0,
                    2 => cx == 1,
                    3 => cx == 0,
                    4 => cz == 1,
                    _ => cz == 0,
                };
                int neighborIndex = externalFace ? index + FaceOff[face] : index;
                BlockId neighbor = externalFace ? (BlockId)pad[neighborIndex] : BlockId.Air;
                byte neighborState = externalFace ? statePad[neighborIndex] : (byte)0;
                bool faceExposed = BlockShapes.FaceExposed(
                        id, state, cx, cy, cz, face, neighbor, neighborState);
                bool collisionOnly = !faceExposed && collision != null && externalFace
                        && IsMissingColliderBoundary(boundaryMask, localX, localZ, face);
                if (!faceExposed && !collisionOnly) continue;

                int tile = BlockDb.ShapeFaceTile(id, state, face);
                if (tile < 0) continue;
                Vector3 cellOrigin = blockOrigin + new Vector3(cx * 0.5f, cy * 0.5f, cz * 0.5f);
                int vi = face * 4;
                for (int vertex = 0; vertex < 4; vertex++)
                    points[vertex] = cellOrigin + BlockDb.FaceVerts[vi + vertex] * 0.5f;
                if (collisionOnly)
                {
                    AppendCollisionFace(collision!, points);
                    continue;
                }

                bool wetFace = externalFace && BlockDb.IsWettable(id)
                        && BlockDb.IsWater(neighbor);
                EmitLitFace(pad, statePad, lightPad, directLightPad, index, face, tile, id, state,
                        blockOrigin, points, surf, collision,
                        cx, cy, cz, false, true, kk, cornerLight, cornerBounce, wetFace, greedy);
            }
        }
    }

    private static void EmitLitFace(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[] directLightPad,
            int sourceIndex, int face, int textureTile, BlockId blockId, byte blockState,
            Vector3 blockOrigin, Span<Vector3> points, SurfaceData surf, List<Vector3>? collision,
            int cellX, int cellY, int cellZ, bool deriveCellFromVertex, bool applyAo,
            Span<float> kk, Span<byte> cornerLight, Span<byte> cornerBounce, bool wet, bool greedy)
    {
        float shade = BlockDb.FaceColors[face].R;
        for (int vertex = 0; vertex < 4; vertex++)
        {
            int sampleX = cellX;
            int sampleY = cellY;
            int sampleZ = cellZ;
            if (deriveCellFromVertex)
                GetCubeCornerMicroCell(face, vertex, out sampleX, out sampleY, out sampleZ);

            SampleFaceCorner(pad, statePad, lightPad, directLightPad, sourceIndex, face,
                    sampleX, sampleY, sampleZ, vertex, applyAo,
                    out cornerLight[vertex], out cornerBounce[vertex], out int ao);
            kk[vertex] = shade * AoFactors[ao];
        }

        Vector3 normal = BlockDb.FaceNormals[face];
        foreach (int tri in TriOrder)
        {
            surf.Vertex.Add(points[tri]);
            surf.Normal.Add(normal);
            Color litColor = VoxelLighting.VertexColor(cornerLight[tri], kk[tri]);
            surf.Color.Add(new Color(litColor.R, litColor.G, litColor.B,
                    MaterialResponse.VertexTag(MaterialResponse.ForTile(textureTile), wet,
                            cornerBounce[tri] / 15.0f)));
            Vector2 localUv = BlockDb.BlockFaceUvAt(blockId, blockState, face,
                    points[tri] - blockOrigin);
            surf.Uv.Add(greedy
                    ? localUv
                    : BlockDb.MapUv(BlockDb.TileUvRect(textureTile), localUv));
            if (greedy)
                surf.Uv2.Add(new Vector2(textureTile, 0.0f));
        }

        if (collision != null)
        {
            collision.Add(points[0]);
            collision.Add(points[1]);
            collision.Add(points[2]);
            collision.Add(points[0]);
            collision.Add(points[2]);
            collision.Add(points[3]);
        }
    }

    private static bool HasHorizontalWaterNeighborAtOrBelow(byte[] pad, int index) =>
            BlockDb.IsWater((BlockId)pad[index + 1])
            || BlockDb.IsWater((BlockId)pad[index - 1])
            || BlockDb.IsWater((BlockId)pad[index + PX])
            || BlockDb.IsWater((BlockId)pad[index - PX])
            || BlockDb.IsWater((BlockId)pad[index - PLANE + 1])
            || BlockDb.IsWater((BlockId)pad[index - PLANE - 1])
            || BlockDb.IsWater((BlockId)pad[index - PLANE + PX])
            || BlockDb.IsWater((BlockId)pad[index - PLANE - PX]);

    private static float WaterSurfaceHeight(byte[] pad, int index)
    {
        BlockId id = (BlockId)pad[index];
        if (!BlockDb.IsWater(id)) return 0.0f;
        return BlockDb.IsWater((BlockId)pad[index + FaceOff[0]]) ? 1.0f : BlockDb.WaterHeight(id);
    }

    // -----------------------------------------------------------------------
    // Greedy meshing (opaque terrain): for each face direction, sweep slices
    // and merge equal neighbour faces into the largest possible rectangles.
    // The merge key carries block id, tile and the four corner AO levels, so
    // merges never change shading. UVs repeat one tile per block (the shader
    // fract()s them against the texture array); collision stays per block in
    // the main loop.
    // -----------------------------------------------------------------------

    private static readonly int[] AxisStep = [1, PLANE, PX];

    internal readonly record struct FaceMergeKey(long Surface, ulong Bounce)
    {
        public bool IsEmpty => Surface == 0L;
    }

    // Worker threads build one mesh at a time, so per-thread scratch buffers
    // are safe and avoid a large allocation per streamed chunk.
    [System.ThreadStatic] private static FaceMergeKey[]? _keyScratch;
    [System.ThreadStatic] private static byte[]? _padScratch;
    [System.ThreadStatic] private static byte[]? _lightPadScratch;
    [System.ThreadStatic] private static byte[]? _directLightPadScratch;
    [System.ThreadStatic] private static byte[]? _statePadScratch;

    private static FaceMergeKey[] RentKeys(int length)
    {
        if (_keyScratch is null || _keyScratch.Length < length)
            _keyScratch = new FaceMergeKey[length];
        return _keyScratch;
    }

    private static byte[] RentPad(int length)
    {
        if (_padScratch is null || _padScratch.Length < length)
        {
            _padScratch = new byte[length];
        }
        else
        {
            System.Array.Clear(_padScratch, 0, length);
        }

        return _padScratch;
    }

    private static byte[] RentLightPad(int length)
    {
        if (_lightPadScratch is null || _lightPadScratch.Length < length)
            _lightPadScratch = new byte[length];
        else
            System.Array.Clear(_lightPadScratch, 0, length);
        return _lightPadScratch;
    }

    private static byte[] RentDirectLightPad(int length)
    {
        if (_directLightPadScratch is null || _directLightPadScratch.Length < length)
            _directLightPadScratch = new byte[length];
        else
            System.Array.Clear(_directLightPadScratch, 0, length);
        return _directLightPadScratch;
    }

    private static byte[] RentStatePad(int length)
    {
        if (_statePadScratch is null || _statePadScratch.Length < length)
            _statePadScratch = new byte[length];
        else
            System.Array.Clear(_statePadScratch, 0, length);
        return _statePadScratch;
    }

    private static void BuildOpaqueGreedy(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[] directLightPad,
            int sy, SurfaceData opaque)
    {
        int[] axisSize = [SX, sy, SZ];
        FaceMergeKey[] keys = RentKeys(SX * sy);
        int baseIdx = PLANE + PX + 1;   // padded (1,1,1)
        for (int f = 0; f < 6; f++)
        {
            int nAxis = _normalAxis[f];
            int uAxis = _uAxis[f];
            int vAxis = _vAxis[f];
            int nStep = AxisStep[nAxis];
            int uStep = AxisStep[uAxis];
            int vStep = AxisStep[vAxis];
            int nExt = axisSize[nAxis];
            int uExt = axisSize[uAxis];
            int vExt = axisSize[vAxis];
            int uvAxisU = _uvAxisOf[f * 2];
            int uvAxisV = _uvAxisOf[f * 2 + 1];
            bool positive = BlockDb.FaceDirs[f][nAxis] > 0.0f;
            for (int sn = 1; sn <= nExt; sn++)
            {
                // Padded coordinate sn along the normal axis (base is padded 1,
                // so sn-1 steps from it). sn also equals the plane of the
                // positive face, since a block at padded sn occupies local sn-1.
                int slice = baseIdx + (sn - 1) * nStep;
                for (int vv = 0; vv < vExt; vv++)
                {
                    for (int uu = 0; uu < uExt; uu++)
                    {
                        keys[vv * uExt + uu] = BuildFaceMergeKey(
                                pad, statePad, lightPad, directLightPad,
                                slice + uu * uStep + vv * vStep, f);
                    }
                }

                for (int vv = 0; vv < vExt; vv++)
                {
                    for (int uu = 0; uu < uExt;)
                    {
                        FaceMergeKey k = keys[vv * uExt + uu];
                        if (k.IsEmpty)
                        {
                            uu++;
                            continue;
                        }

                        int w = 1;
                        while (uu + w < uExt && keys[vv * uExt + uu + w] == k)
                        {
                            w++;
                        }

                        int h = 1;
                        while (vv + h < vExt)
                        {
                            bool row = true;
                            int rowBase = (vv + h) * uExt + uu;
                            for (int x2 = 0; x2 < w; x2++)
                            {
                                if (keys[rowBase + x2] != k)
                                {
                                    row = false;
                                    break;
                                }
                            }

                            if (!row)
                            {
                                break;
                            }

                            h++;
                        }

                        EmitGreedyQuad(opaque, f, k, uu, vv, w, h, sn, positive,
                                uAxis, vAxis, nAxis, uvAxisU, uvAxisV);
                        for (int y2 = 0; y2 < h; y2++)
                        {
                            int rowBase = (vv + y2) * uExt + uu;
                            for (int x2 = 0; x2 < w; x2++)
                            {
                                keys[rowBase + x2] = default;
                            }
                        }

                        uu += w;
                    }
                }
            }
        }
    }

    /// <summary>Compatibility surface key used by existing merge-identity tests.</summary>
    internal static long FaceKey(byte[] pad, byte[] statePad, byte[] lightPad, int i, int f) =>
            BuildFaceMergeKey(pad, statePad, lightPad, null, i, f).Surface;

    /// <summary>
    /// Full merge key, including the per-corner cave-spill nibbles. Greedy and
    /// per-face topology share one sampling routine, so an identical key is what
    /// makes a merged quad carry exactly the corner lighting of its constituents.
    /// </summary>
    internal static FaceMergeKey MergeKey(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[] directLightPad, int i, int f) =>
            BuildFaceMergeKey(pad, statePad, lightPad, directLightPad, i, f);

    /// <summary>Full merge key includes the corner-wise cave-bounce values.</summary>
    private static FaceMergeKey BuildFaceMergeKey(
            byte[] pad, byte[] statePad, byte[] lightPad, byte[]? directLightPad, int i, int f)
    {
        BlockId id = (BlockId)pad[i];
        if (id == BlockId.Air || BlockDb.IsCross(id)
                || !BlockShapes.IsFullCube(id, statePad[i]))
        {
            return default;
        }

        if (BlockDb.TransMask[(int)id] == 1 || BlockDb.IsWater(id) || id == BlockId.Lava)
        {
            return default;   // non-opaque surfaces keep the per-face path
        }

        int adjacentIndex = i + FaceOff[f];
        BlockId adjacentId = (BlockId)pad[adjacentIndex];
        if (BlockShapes.IsShape(adjacentId)
                && !BlockShapes.IsFullCube(adjacentId, statePad[adjacentIndex]))
            return default;

        int tile = BlockDb.Tiles[(int)id * 6 + f];
        if (tile < 0)
        {
            return default;
        }

        int nb = pad[i + FaceOff[f]];
        if (BlockDb.Render[(int)id * BlockDb.BlockCount + nb] == 0)
        {
            return default;
        }

        long key = (uint)id | ((long)tile << 8);
        if (BlockDb.IsWettable(id) && BlockDb.IsWater(adjacentId))
            key |= 1L << 56;
        ulong bounceKey = 0;
        for (int v4 = 0; v4 < 4; v4++)
        {
            GetCubeCornerMicroCell(f, v4, out int cellX, out int cellY, out int cellZ);
            SampleFaceCorner(pad, statePad, lightPad, directLightPad, i, f,
                    cellX, cellY, cellZ, v4, true,
                    out byte light, out byte bouncedSky, out int ao);
            key |= (long)ao << (16 + (v4 * 2));
            key |= (long)(light >> 4) << (24 + v4 * 4);
            key |= (long)(light & 0x0F) << (40 + v4 * 4);
            bounceKey |= (ulong)bouncedSky << (v4 * 4);
        }

        return new FaceMergeKey(key, bounceKey);
    }

    private static void EmitGreedyQuad(
            SurfaceData surf, int f, FaceMergeKey key, int uu, int vv, int w, int h, int sn, bool positive,
            int uAxis, int vAxis, int nAxis, int uvAxisU, int uvAxisV)
    {
        // Local-space rect: a block at padded coordinate p occupies local
        // [p-1, p]; the face plane sits at the block's +1 boundary for positive
        // faces and at its base for negative ones, matching FaceVerts' units.
        Vector3 min = Vector3.Zero;
        Vector3 size = Vector3.Zero;
        min[uAxis] = uu;
        min[vAxis] = vv;
        min[nAxis] = positive ? sn : sn - 1;
        size[uAxis] = w;
        size[vAxis] = h;
        float su = uvAxisU == uAxis ? w : h;
        float sv = uvAxisV == uAxis ? w : h;
        long surfaceKey = key.Surface;
        int tile = (int)((surfaceKey >> 8) & 0xFF);
        float shade = BlockDb.FaceColors[f].R;
        Vector3 normal = BlockDb.FaceNormals[f];
        foreach (int c in TriOrder)
        {
            Vector3 unit = BlockDb.FaceVerts[f * 4 + c];
            surf.Vertex.Add(min + new Vector3(unit.X * size.X, unit.Y * size.Y, unit.Z * size.Z));
            surf.Normal.Add(normal);
            float k = shade * AoFactors[(int)(surfaceKey >> (16 + (c * 2))) & 3];
            byte sky = (byte)((surfaceKey >> (24 + c * 4)) & 0x0F);
            byte block = (byte)((surfaceKey >> (40 + c * 4)) & 0x0F);
            Color litColor = VoxelLighting.VertexColor(VoxelLighting.Pack(sky, block), k);
            bool wet = (surfaceKey & (1L << 56)) != 0;
            float bouncedSky = (float)((key.Bounce >> (c * 4)) & 0x0F) / 15.0f;
            surf.Color.Add(new Color(litColor.R, litColor.G, litColor.B,
                    MaterialResponse.VertexTag(MaterialResponse.ForTile(tile), wet, bouncedSky)));
            Vector2 duv = BlockDb.FaceUvs[f * 4 + c];
            surf.Uv.Add(new Vector2(duv.X * su, duv.Y * sv));
            surf.Uv2.Add(new Vector2(tile, 0.0f));
        }
    }

    /// <summary>
    /// Cross plant geometry: two diagonal planes, each emitted with both
    /// windings so the blades read from every angle (backface culling on the
    /// opaque alpha-scissor material would otherwise hide one side). In greedy
    /// mode the UVs are quad-local 0..1 and the tile travels in UV2, matching
    /// the texture-array material.
    /// </summary>
    private static void AddCross(
            SurfaceData surf, Vector3 b, int tile, Color lightColor, float bouncedSky, bool wet,
            bool greedy)
    {
        Vector2[] uv;
        if (greedy)
        {
            uv = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];
        }
        else
        {
            Rect2 rect = BlockDb.TileUvRect(tile);
            uv =
            [
                BlockDb.MapUv(rect, new Vector2(0, 1)), BlockDb.MapUv(rect, new Vector2(1, 1)),
                BlockDb.MapUv(rect, new Vector2(1, 0)), BlockDb.MapUv(rect, new Vector2(0, 0)),
            ];
        }
        Vector3[] planeA =
        [
            b, b + new Vector3(1, 0, 1), b + new Vector3(1, 1, 1), b + new Vector3(0, 1, 0),
        ];
        Vector3[] planeB =
        [
            b + new Vector3(1, 0, 0), b + new Vector3(0, 0, 1), b + new Vector3(0, 1, 1), b + new Vector3(1, 1, 0),
        ];
        EmitDoubleSided(surf, planeA, uv, tile, lightColor, bouncedSky, wet, greedy);
        EmitDoubleSided(surf, planeB, uv, tile, lightColor, bouncedSky, wet, greedy);
    }

    private static void EmitDoubleSided(
            SurfaceData surf, Vector3[] p, Vector2[] uv, int tile,
            Color lightColor, float bouncedSky, bool wet, bool greedy)
    {
        Vector3 n = (p[1] - p[0]).Cross(p[3] - p[0]).Normalized();
        SurfaceFamily family = MaterialResponse.ForTile(tile);
        Color white = new(lightColor.R, lightColor.G, lightColor.B,
                MaterialResponse.VertexTag(family, wet, bouncedSky));
        for (int pass = 0; pass < 2; pass++)
        {
            // Godot uses clockwise front-face winding; reverse the geometric
            // cross product for the canonical pass and invert it on the back pass.
            Vector3 normal = pass == 0 ? -n : n;
            for (int t = 0; t < 6; t++)
            {
                int idx = pass == 0 ? TriOrder[t] : TriOrder[5 - t];
                surf.Vertex.Add(p[idx]);
                surf.Normal.Add(normal);
                surf.Color.Add(white);
                surf.Uv.Add(uv[idx]);
                if (greedy)
                    surf.Uv2.Add(new Vector2(tile, 0.0f));
            }
        }
    }

    /// <summary>
    /// Padded copy of the chunk's block array (1-cell shell, y padded with air).
    /// Neighbour data comes from world.ChunkData (built chunk or generated
    /// cache), so this is plain array reads. The buffer is rented from a
    /// per-thread scratch pool and zeroed first; cardinal and diagonal borders
    /// are copied so corner AO resolves the actual neighbouring voxels.
    /// </summary>
    private static byte[] BuildPadded(VoxelWorld world, Vector2I cp, byte[] data, int sy)
    {
        byte[] pad = RentPad(PX * (sy + 2) * PZ);
        for (int y = 0; y < sy; y++)
        {
            for (int z = 0; z < SZ; z++)
            {
                int src = (y * SZ + z) * SX;
                int dst = ((y + 1) * PZ + (z + 1)) * PX + 1;
                for (int x = 0; x < SX; x++)
                {
                    pad[dst + x] = data[src + x];
                }
            }
        }

        byte[] west = world.ChunkData(cp + new Vector2I(-1, 0));
        byte[] east = world.ChunkData(cp + new Vector2I(1, 0));
        byte[] north = world.ChunkData(cp + new Vector2I(0, -1));
        byte[] south = world.ChunkData(cp + new Vector2I(0, 1));
        byte[] northWest = world.ChunkData(cp + new Vector2I(-1, -1));
        byte[] northEast = world.ChunkData(cp + new Vector2I(1, -1));
        byte[] southWest = world.ChunkData(cp + new Vector2I(-1, 1));
        byte[] southEast = world.ChunkData(cp + new Vector2I(1, 1));
        for (int y = -1; y <= sy; y++)
        {
            if (y < 0 || y >= sy)
            {
                continue;
            }

            int row = (y + 1) * PZ;
            int nrow = y * SZ;
            for (int z = -1; z <= SZ; z++)
            {
                int zi = Mathf.Clamp(z, 0, SZ - 1);
                int col = row + z + 1;
                if (west.Length > 0)
                    pad[col * PX] = west[(nrow + zi) * SX + SX - 1];
                if (east.Length > 0)
                    pad[col * PX + PX - 1] = east[(nrow + zi) * SX];
            }
        }

        for (int y = -1; y <= sy; y++)
        {
            if (y < 0 || y >= sy)
            {
                continue;
            }

            int row = (y + 1) * PZ;
            int nrow = y * SZ;
            for (int x = -1; x <= SX; x++)
            {
                int xi = Mathf.Clamp(x, 0, SX - 1);
                if (north.Length > 0)
                    pad[row * PX + x + 1] = north[(nrow + SZ - 1) * SX + xi];
                if (south.Length > 0)
                    pad[(row + PZ - 1) * PX + x + 1] = south[nrow * SX + xi];
            }

            // The four AO corner samples must come from their actual diagonal
            // chunks, not from a clamped edge of one of the cardinal chunks.
            if (northWest.Length > 0)
                pad[row * PX] = northWest[(nrow + SZ - 1) * SX + SX - 1];
            if (northEast.Length > 0)
                pad[row * PX + PX - 1] = northEast[(nrow + SZ - 1) * SX];
            if (southWest.Length > 0)
                pad[(row + PZ - 1) * PX] = southWest[nrow * SX + SX - 1];
            if (southEast.Length > 0)
                pad[(row + PZ - 1) * PX + PX - 1] = southEast[nrow * SX];
        }

        return pad;
    }


    private static byte[] BuildPaddedStates(VoxelWorld world, Vector2I cp, byte[] data, int sy)
    {
        byte[] west = world.ChunkBlockStates(cp + new Vector2I(-1, 0));
        byte[] east = world.ChunkBlockStates(cp + new Vector2I(1, 0));
        byte[] north = world.ChunkBlockStates(cp + new Vector2I(0, -1));
        byte[] south = world.ChunkBlockStates(cp + new Vector2I(0, 1));
        byte[] northWest = world.ChunkBlockStates(cp + new Vector2I(-1, -1));
        byte[] northEast = world.ChunkBlockStates(cp + new Vector2I(1, -1));
        byte[] southWest = world.ChunkBlockStates(cp + new Vector2I(-1, 1));
        byte[] southEast = world.ChunkBlockStates(cp + new Vector2I(1, 1));
        byte[] pad = RentStatePad(PX * (sy + 2) * PZ);
        if (data.Length == 0 && west.Length == 0 && east.Length == 0
                && north.Length == 0 && south.Length == 0 && northWest.Length == 0
                && northEast.Length == 0 && southWest.Length == 0 && southEast.Length == 0)
            return pad;

        for (int y = 0; y < sy; y++)
        for (int z = 0; z < SZ; z++)
        {
            int src = (y * SZ + z) * SX;
            int dst = ((y + 1) * PZ + (z + 1)) * PX + 1;
            if (data.Length > 0)
                System.Array.Copy(data, src, pad, dst, SX);
        }

        for (int y = 0; y < sy; y++)
        {
            int row = (y + 1) * PZ;
            int nrow = y * SZ;
            for (int z = -1; z <= SZ; z++)
            {
                int zi = Mathf.Clamp(z, 0, SZ - 1);
                int col = row + z + 1;
                if (west.Length > 0)
                    pad[col * PX] = west[(nrow + zi) * SX + SX - 1];
                if (east.Length > 0)
                    pad[col * PX + PX - 1] = east[(nrow + zi) * SX];
            }
        }
        for (int y = 0; y < sy; y++)
        {
            int row = (y + 1) * PZ;
            int nrow = y * SZ;
            for (int x = -1; x <= SX; x++)
            {
                int xi = Mathf.Clamp(x, 0, SX - 1);
                if (north.Length > 0)
                    pad[row * PX + x + 1] = north[(nrow + SZ - 1) * SX + xi];
                if (south.Length > 0)
                    pad[(row + PZ - 1) * PX + x + 1] = south[nrow * SX + xi];
            }

            if (northWest.Length > 0)
                pad[row * PX] = northWest[(nrow + SZ - 1) * SX + SX - 1];
            if (northEast.Length > 0)
                pad[row * PX + PX - 1] = northEast[(nrow + SZ - 1) * SX];
            if (southWest.Length > 0)
                pad[(row + PZ - 1) * PX] = southWest[nrow * SX + SX - 1];
            if (southEast.Length > 0)
                pad[(row + PZ - 1) * PX + PX - 1] = southEast[nrow * SX];
        }
        return pad;
    }

    /// <summary>
    /// One-cell packed-light shell matching BuildPadded. Cardinal and diagonal
    /// maps keep vertex-light interpolation continuous at chunk corners.
    /// </summary>
    private static byte[] BuildLightPadded(
            VoxelWorld world, Vector2I cp, int sy, out byte[] directPad)
    {
        int length = PX * (sy + 2) * PZ;
        byte[] pad = RentLightPad(length);
        directPad = RentDirectLightPad(length);
        VoxelLightData[] neighbourhood = world.LightDataNeighborhood(cp);
        VoxelLightData center = neighbourhood[4];
        VoxelLightData west = neighbourhood[3];
        VoxelLightData east = neighbourhood[5];
        VoxelLightData north = neighbourhood[1];
        VoxelLightData south = neighbourhood[7];
        VoxelLightData northWest = neighbourhood[0];
        VoxelLightData northEast = neighbourhood[2];
        VoxelLightData southWest = neighbourhood[6];
        VoxelLightData southEast = neighbourhood[8];

        for (int y = 0; y < sy; y++)
        {
            int srcLayer = y * SX * SZ;
            int dstLayer = (y + 1) * PLANE;
            for (int z = 0; z < SZ; z++)
            {
                int src = srcLayer + z * SX;
                int dst = dstLayer + (z + 1) * PX + 1;
                for (int x = 0; x < SX; x++)
                {
                    pad[dst + x] = center.Packed[src + x];
                    directPad[dst + x] = center.DirectSkyAt(x, y, z, sy);
                }
                pad[dst - 1] = west.Packed[src + SX - 1];
                directPad[dst - 1] = west.DirectSkyAt(SX - 1, y, z, sy);
                pad[dst + SX] = east.Packed[src];
                directPad[dst + SX] = east.DirectSkyAt(0, y, z, sy);
            }

            for (int x = 0; x < SX; x++)
            {
                int dst = dstLayer + PX + x + 1;
                pad[dst - PX] = north.Packed[(SZ - 1) * SX + x];
                directPad[dst - PX] = north.DirectSkyAt(x, y, SZ - 1, sy);
                pad[dst + SZ * PX] = south.Packed[x];
                directPad[dst + SZ * PX] = south.DirectSkyAt(x, y, 0, sy);
            }

            pad[dstLayer] = northWest.Packed[(SZ - 1) * SX + SX - 1];
            directPad[dstLayer] = northWest.DirectSkyAt(SX - 1, y, SZ - 1, sy);
            pad[dstLayer + PX - 1] = northEast.Packed[(SZ - 1) * SX];
            directPad[dstLayer + PX - 1] = northEast.DirectSkyAt(0, y, SZ - 1, sy);
            pad[dstLayer + (SZ + 1) * PX] = southWest.Packed[SX - 1];
            directPad[dstLayer + (SZ + 1) * PX] = southWest.DirectSkyAt(SX - 1, y, 0, sy);
            pad[dstLayer + (SZ + 1) * PX + PX - 1] = southEast.Packed[0];
            directPad[dstLayer + (SZ + 1) * PX + PX - 1] = southEast.DirectSkyAt(0, y, 0, sy);
        }

        // The sky above the world is a permanent skylight source.
        int topLayer = (sy + 1) * PLANE;
        for (int z = 0; z < PZ; z++)
        for (int x = 0; x < PX; x++)
        {
            int index = topLayer + z * PX + x;
            pad[index] = 0xF0;
            directPad[index] = VoxelLighting.MaxLevel;
        }
        return pad;
    }
}

/// <summary>One render surface being built on a worker thread.</summary>
public sealed class SurfaceData
{
    public readonly List<Vector3> Vertex = [];
    public readonly List<Vector3> Normal = [];
    public readonly List<Color> Color = [];
    public readonly List<Vector2> Uv = [];
    public readonly List<Vector2> Uv2 = [];   // greedy mode only: texture-array layer in .x

    // Managed List backing arrays are what a queued payload retains until the
    // main thread returns it to PayloadPool. Godot mesh storage is estimated
    // separately from item counts after the payload has been applied.
    public long EstimatedBufferBytes => (long)Vertex.Capacity * 12
            + (long)Normal.Capacity * 12 + (long)Color.Capacity * 16
            + (long)Uv.Capacity * 8 + (long)Uv2.Capacity * 8;

    public long EstimatedResourceBytes => (long)Vertex.Count * 12
            + (long)Normal.Count * 12 + (long)Color.Count * 16
            + (long)Uv.Count * 8 + (long)Uv2.Count * 8;
}

/// <summary>Worker result of a chunk mesh build; applied on the main thread.
/// Buffers come from PayloadPool and are returned there by Release().</summary>
public sealed class MeshPayload(
        SurfaceData opaque,
        SurfaceData glass,
        SurfaceData water,
        SurfaceData lava,
        List<Vector3> collision,
        long epoch,
        long meshingRevision,
        int boundaryMask)
{
    public readonly SurfaceData Opaque = opaque;
    public readonly SurfaceData Glass = glass;
    public readonly SurfaceData Water = water;
    public readonly SurfaceData Lava = lava;
    public readonly List<Vector3> Collision = collision;
    public readonly long Epoch = epoch;
    public readonly long MeshingRevision = meshingRevision;
    public readonly int BoundaryMask = boundaryMask;
    public bool Released;

    public long EstimatedBufferBytes => Opaque.EstimatedBufferBytes + Glass.EstimatedBufferBytes
            + Water.EstimatedBufferBytes + Lava.EstimatedBufferBytes
            + (long)Collision.Capacity * 12;

    public long EstimatedResourceBytes => Opaque.EstimatedResourceBytes + Glass.EstimatedResourceBytes
            + Water.EstimatedResourceBytes + Lava.EstimatedResourceBytes
            + (long)Collision.Count * 12;
}

/// <summary>
/// Reuses mesher buffers across builds: streaming allocates thousands of
/// vertex lists, and keeping their backing arrays alive removes most of the
/// GC churn. Rented on the worker, released on the main thread after applying.
/// </summary>
public static class PayloadPool
{
    // Keep normal streaming reuse, but put a hard ceiling on process-wide
    // backing-array retention. Buffers above the per-item ceiling are dropped.
    internal const long MaxRetainedBufferBytes = 32L * 1024 * 1024;
    internal const long MaxRetainedItemBytes = 8L * 1024 * 1024;

    private static readonly System.Collections.Concurrent.ConcurrentBag<SurfaceData> Surfaces = new();
    private static readonly System.Collections.Concurrent.ConcurrentBag<List<Vector3>> Collisions = new();
    private static long _retainedBufferBytes;
    private static int _retainedSurfaceCount;
    private static int _retainedCollisionCount;

    /// <summary>Capacity bytes retained by reusable mesher buffers across all active worlds.</summary>
    public static long RetainedBufferBytes => Interlocked.Read(ref _retainedBufferBytes);
    public static int RetainedSurfaceCount => Volatile.Read(ref _retainedSurfaceCount);
    public static int RetainedCollisionCount => Volatile.Read(ref _retainedCollisionCount);

    public static SurfaceData RentSurface()
    {
        if (!Surfaces.TryTake(out SurfaceData? surface)) return new SurfaceData();
        Interlocked.Add(ref _retainedBufferBytes, -surface.EstimatedBufferBytes);
        Interlocked.Decrement(ref _retainedSurfaceCount);
        return surface;
    }

    public static List<Vector3> RentCollision()
    {
        if (!Collisions.TryTake(out List<Vector3>? collision)) return [];
        Interlocked.Add(ref _retainedBufferBytes, -(long)collision.Capacity * 12);
        Interlocked.Decrement(ref _retainedCollisionCount);
        return collision;
    }

    public static void Release(MeshPayload payload)
    {
        if (payload.Released) return;

        payload.Released = true;
        ReturnSurface(payload.Opaque);
        ReturnSurface(payload.Glass);
        ReturnSurface(payload.Water);
        ReturnSurface(payload.Lava);
        payload.Collision.Clear();

        long collisionBytes = (long)payload.Collision.Capacity * 12;
        if (TryReserve(collisionBytes))
        {
            Interlocked.Increment(ref _retainedCollisionCount);
            Collisions.Add(payload.Collision);
        }
    }

    private static void ReturnSurface(SurfaceData surf)
    {
        surf.Vertex.Clear();
        surf.Normal.Clear();
        surf.Color.Clear();
        surf.Uv.Clear();
        surf.Uv2.Clear();

        long bytes = surf.EstimatedBufferBytes;
        if (!TryReserve(bytes)) return;
        Interlocked.Increment(ref _retainedSurfaceCount);
        Surfaces.Add(surf);
    }

    private static bool TryReserve(long bytes)
    {
        if (bytes <= 0 || bytes > MaxRetainedItemBytes) return false;

        while (true)
        {
            long retained = Interlocked.Read(ref _retainedBufferBytes);
            if (retained > MaxRetainedBufferBytes - bytes) return false;
            if (Interlocked.CompareExchange(ref _retainedBufferBytes,
                    retained + bytes, retained) == retained)
                return true;
        }
    }

}
