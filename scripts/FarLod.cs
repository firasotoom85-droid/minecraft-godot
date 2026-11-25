using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

namespace Minecraft;

/// <summary>
/// Far-view LOD ring: coarse heightmap tiles fill every area without a committed
/// full-resolution chunk mesh. The handoff is tracked per chunk, not inferred
/// from the requested view-distance square, so terrain never disappears while
/// its detailed replacement is still queued or building.
///
/// Builds run on WorkerThreadPool workers (terrain sampling, edit-cache lookup
/// and array math); meshes are applied on the main thread.
/// </summary>
public partial class FarLod : Node3D
{
    private const float CellSize = 4.0f;                 // metres per LOD cell
    private const float Region = 128.0f;                 // metres per tile
    private const int Cells = (int)(Region / CellSize);  // 32 cells per side
    private const int TileChunks = (int)(Region / 16.0f);
    private const int BuildLimit = 3;

    public VoxelWorld? World;

    private sealed class Tile
    {
        public MeshInstance3D? Node;
        public ulong CoverageMask;
        public long Generation;
        public long Revision;
        public int FailureCount;
    }

    private readonly record struct BuildToken(long Generation, long Revision, long RequestId, ulong CoverageMask);
    private readonly record struct InFlightBuild(long TaskId, BuildToken Token);
    private sealed record LodPayload(Vector3[] Verts, Vector3[] Normals, Color[] Colors);

    private readonly Dictionary<Vector2I, Tile> _tiles = new();  // Vector2I -> tile
    private readonly FarLodWorkQueue _queue = new();
    private readonly Dictionary<Vector2I, InFlightBuild> _inFlight = new();
    private readonly HashSet<Vector2I> _coveredChunks = [];
    private readonly HashSet<Vector2I> _dirtyCoverageTiles = [];
    private long _nextTileGeneration;
    private long _nextBuildRequestId;
    private readonly CancellationTokenSource _workerShutdown = new();
    private volatile bool _alive = true;

    private StandardMaterial3D _material = null!;
    private readonly Color[] _topColors = new Color[BlockDb.BlockCount];
    private readonly Color[] _sideColors = new Color[BlockDb.BlockCount];

    public void Setup(VoxelWorld world)
    {
        World = world;
        _material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        // LOD surfaces must sample the actual upward-facing tile rather than
        // the icon tile (which is usually a side face). Cache all face averages
        // here so worker mesh builds never touch the atlas Image or allocate.
        for (int i = 0; i < BlockDb.BlockCount; i++)
        {
            BlockId id = (BlockId)i;
            if (!BlockDb.IsDefined(id))
            {
                continue;
            }

            _topColors[i] = BlockDb.AverageFaceColor(id, 0);
            _sideColors[i] = BlockDb.AverageFaceColor(id, 2);
        }
    }

    public override void _ExitTree()
    {
        _alive = false;
        _workerShutdown.Cancel();
        foreach (InFlightBuild build in _inFlight.Values)
        {
            WorkerThreadPool.WaitForTaskCompletion(build.TaskId);
        }

        _inFlight.Clear();
        _queue.Clear();
        _workerShutdown.Dispose();
    }

    /// <summary>
    /// Re-evaluate the ring around the player's chunk: queue missing tiles
    /// nearest-first, rebuild edge tiles whose streamed-area overlap changed,
    /// free tiles now out of range.
    /// </summary>
    public void UpdateAround(Vector2I pcp)
    {
        float radius = FarLodLifecycleRules.EffectiveRadius(
                World!.LodRadius, World.ViewDistance, VoxelWorld.ChunkX);
        if (radius <= 0.0f)
        {
            foreach (Tile tile in _tiles.Values)
            {
                tile.Node?.QueueFree();
            }

            _tiles.Clear();
            _queue.Clear();
            _dirtyCoverageTiles.Clear();
            return;
        }

        Vector2 center = new((pcp.X + 0.5f) * 16.0f, (pcp.Y + 0.5f) * 16.0f);
        int tcx = (int)System.Math.Floor(center.X / Region);
        int tcz = (int)System.Math.Floor(center.Y / Region);
        int ring = (int)System.Math.Ceiling((radius + Region * 0.7f) / Region);
        HashSet<Vector2I> keep = [];
        for (int dz = -ring; dz <= ring; dz++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                Vector2I tc = new(tcx + dx, tcz + dz);
                Vector2 mid = new((tc.X + 0.5f) * Region, (tc.Y + 0.5f) * Region);
                if (mid.DistanceTo(center) > radius + Region * 0.7f)
                {
                    continue;
                }

                keep.Add(tc);
                ulong coverageMask = CoverageMaskFor(tc);
                if (_tiles.TryGetValue(tc, out Tile? tile))
                {
                    if (tile.CoverageMask != coverageMask)
                    {
                        tile.CoverageMask = coverageMask;
                        tile.Revision++;
                        tile.FailureCount = 0;
                        // Keep the old coarse surface until its replacement is
                        // ready. Its slight vertical inset prevents z-fighting
                        // with a newly committed detailed chunk during handoff.
                        Enqueue(tc);
                    }
                }
                else
                {
                    _tiles[tc] = new Tile
                    {
                        Node = null,
                        CoverageMask = coverageMask,
                        Generation = ++_nextTileGeneration,
                    };
                    Enqueue(tc);
                }
            }
        }

        foreach (Vector2I key in _tiles.Keys.ToList())
        {
            if (!keep.Contains(key))
            {
                _tiles[key].Node?.QueueFree();
                _tiles.Remove(key);
                _dirtyCoverageTiles.Remove(key);
            }
        }

        // A moving player can leave queued coordinates behind while every
        // worker slot is occupied. Drop entries for evicted tiles before
        // dispatch so pending work stays bounded by the live tile set.
        _queue.Prune(_tiles);
        Dispatch();
    }

    /// <summary>
    /// Publish a chunk only after its full-resolution render payload has committed.
    /// Calls are cheap; tile rebuilds are coalesced until FlushCoverageUpdates().
    /// </summary>
    public void SetChunkCoverage(Vector2I chunkCoordinate, bool covered)
    {
        bool changed = covered
                ? _coveredChunks.Add(chunkCoordinate)
                : _coveredChunks.Remove(chunkCoordinate);
        if (!changed)
        {
            return;
        }

        Vector2I tileCoordinate = FarLodLifecycleRules.TileForChunk(chunkCoordinate);
        if (_tiles.ContainsKey(tileCoordinate))
        {
            _dirtyCoverageTiles.Add(tileCoordinate);
        }
    }

    /// <summary>
    /// Apply all chunk-ready/unloaded changes once per world frame. This avoids
    /// rebuilding the same 128 m tile repeatedly when several chunks commit in
    /// one staged-application budget.
    /// </summary>
    public void FlushCoverageUpdates()
    {
        if (!_alive || _dirtyCoverageTiles.Count == 0)
        {
            return;
        }

        foreach (Vector2I tc in _dirtyCoverageTiles)
        {
            if (!_tiles.TryGetValue(tc, out Tile? tile))
            {
                continue;
            }

            ulong coverageMask = CoverageMaskFor(tc);
            if (coverageMask == tile.CoverageMask)
            {
                continue;
            }

            tile.CoverageMask = coverageMask;
            tile.Revision++;
            tile.FailureCount = 0;
            Enqueue(tc);
        }

        _dirtyCoverageTiles.Clear();
        Dispatch();
    }

    private ulong CoverageMaskFor(Vector2I tileCoordinate)
    {
        ulong mask = 0;
        int originX = tileCoordinate.X * TileChunks;
        int originZ = tileCoordinate.Y * TileChunks;
        for (int z = 0; z < TileChunks; z++)
        {
            for (int x = 0; x < TileChunks; x++)
            {
                if (_coveredChunks.Contains(new Vector2I(originX + x, originZ + z)))
                {
                    mask |= 1UL << (z * TileChunks + x);
                }
            }
        }

        return mask;
    }

    private void Enqueue(Vector2I tc)
    {
        if (_inFlight.ContainsKey(tc) || !_tiles.ContainsKey(tc))
        {
            return;
        }

        _queue.Enqueue(tc);
    }

    private void Dispatch()
    {
        while (FarLodLifecycleRules.CanDispatch(
                _inFlight.Count, _queue.Count, BuildLimit, _alive))
        {
            if (!FarLodLifecycleRules.TryDequeueEligibleTile(
                    _queue, _tiles, _inFlight, out Vector2I tc, out Tile tile))
            {
                break;
            }

            // Capture the tile's requested state, not a fresh global sample.
            // A subsequent UpdateAround() increments the revision and makes
            // this result stale even when the coordinate itself is unchanged.
            BuildToken token = new(tile.Generation, tile.Revision, ++_nextBuildRequestId, tile.CoverageMask);
            long tid = WorkerThreadPool.AddTask(
                    Callable.From(() => BuildTask(tc, token)), true, $"lod_{tc.X}_{tc.Y}");
            _inFlight[tc] = new InFlightBuild(tid, token);
        }
    }

    private void BuildTask(Vector2I tc, BuildToken token)
    {
        // The same exception-capture path is exercised by the deterministic
        // worker-failure regression. An exception becomes a terminal result
        // instead of bypassing the deferred main-thread completion callback.
        CancellationToken cancellationToken = _workerShutdown.Token;
        FarLodBuildOutcome<LodPayload> outcome =
                FarLodLifecycleRules.CaptureBuild(
                        () => BuildArrays(tc, token.CoverageMask, cancellationToken), cancellationToken);

        // Shutdown cancellation is not a tile build failure: the node is
        // leaving, no result should be posted, and no retry may be scheduled.
        if (_alive && !outcome.Cancelled)
        {
            Callable.From(() => ApplyTile(tc, token, outcome.Payload, outcome.Failure)).CallDeferred();
        }
    }

    // -----------------------------------------------------------------------
    // Mesh building (worker thread: noise, edit-cache lookup and arrays)
    // -----------------------------------------------------------------------

    private LodPayload? BuildArrays(
            Vector2I tc, ulong coverageMask, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int x0 = tc.X * (int)Region;
        int z0 = tc.Y * (int)Region;
        int sea = World!.SeaLevel;
        List<Vector3> verts = [];
        List<Vector3> normals = [];
        List<Color> colors = [];
        for (int j = 0; j < Cells; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int cz0 = z0 + j * (int)CellSize;
            for (int i = 0; i < Cells; i++)
            {
                int cx0 = x0 + i * (int)CellSize;
                bool hasIsland = !World.IsSky || World.IslandAt(cx0 + 2, cz0 + 2);
                if (!FarLodLifecycleRules.ShouldEmitCell(
                        coverageMask, tc, cx0, cz0, World.IsSky, hasIsland))
                {
                    continue;
                }

                int h00 = World.SurfaceHeight(cx0, cz0);
                int h10 = World.SurfaceHeight(cx0 + (int)CellSize, cz0);
                int h01 = World.SurfaceHeight(cx0, cz0 + (int)CellSize);
                int h11 = World.SurfaceHeight(cx0 + (int)CellSize, cz0 + (int)CellSize);
                int hc = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
                int relief = hc - Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11));
                BlockId surfaceBlock = LandBlock(World.BiomeAt(cx0 + 2, cz0 + 2, hc), relief);

                // Player edits win over noise, including their material identity.
                // This keeps exposed dirt, tilled soil and built surfaces visible
                // in the far ring after their source chunks have been unloaded.
                if (World.TryEditTop(cx0 + 2, cz0 + 2, out int editedTop, out BlockId editedBlock))
                {
                    hc = editedTop;
                    surfaceBlock = editedBlock;
                }

                bool isWater = hc <= sea;
                // Keep fallback terrain fractionally below the detailed surface
                // during the short interval before the tile's coverage rebuild applies.
                float y = (isWater ? sea + 1 : hc + 1) - 0.025f;
                Vector3 b = new(cx0, y, cz0);
                bool wetShoreCell = IsWetShoreCell(isWater, hc, sea, h00, h10, h01, h11);
                Color topColor = isWater ? _topColors[(int)BlockId.Water] : TopColor(surfaceBlock);
                Color capSideColor = SideColor(surfaceBlock);
                if (wetShoreCell)
                {
                    topColor = MaterialResponse.WetTint(topColor);
                    capSideColor = MaterialResponse.WetTint(capSideColor);
                }
                Color lowerSideColor = SideColor(UnderBlock(surfaceBlock));
                AddQuad(verts, normals, colors,
                        b, b + new Vector3(CellSize, 0, 0),
                        b + new Vector3(CellSize, 0, CellSize), b + new Vector3(0, 0, CellSize),
                        Vector3.Up, topColor, 1.0f);
                if (isWater)
                {
                    continue;
                }

                // Coarse skirts retain a one-block surface cap, then switch to
                // the exposed substrate. This mirrors the grass/farmland-to-dirt
                // transition without adding geometry to streamed voxel chunks.
                float cx1 = cx0 + CellSize;
                float cz1 = cz0 + CellSize;
                int drop = hc - Mathf.Max(h10, h11);
                if (drop > 0)
                    AddSkirt(verts, normals, colors,
                            new Vector3(cx1, y, cz0), new Vector3(cx1, y, cz1),
                            new Vector3(cx1, y - drop, cz0), new Vector3(cx1, y - drop, cz1),
                            Vector3.Right, false, drop, capSideColor, lowerSideColor);

                // -X
                drop = hc - Mathf.Max(h00, h01);
                if (drop > 0)
                    AddSkirt(verts, normals, colors,
                            new Vector3(cx0, y, cz0), new Vector3(cx0, y, cz1),
                            new Vector3(cx0, y - drop, cz0), new Vector3(cx0, y - drop, cz1),
                            Vector3.Left, true, drop, capSideColor, lowerSideColor);

                // +Z
                drop = hc - Mathf.Max(h01, h11);
                if (drop > 0)
                    AddSkirt(verts, normals, colors,
                            new Vector3(cx0, y, cz1), new Vector3(cx1, y, cz1),
                            new Vector3(cx0, y - drop, cz1), new Vector3(cx1, y - drop, cz1),
                            Vector3.Back, true, drop, capSideColor, lowerSideColor);

                // -Z
                drop = hc - Mathf.Max(h00, h10);
                if (drop > 0)
                    AddSkirt(verts, normals, colors,
                            new Vector3(cx1, y, cz0), new Vector3(cx0, y, cz0),
                            new Vector3(cx1, y - drop, cz0), new Vector3(cx0, y - drop, cz0),
                            Vector3.Forward, true, drop, capSideColor, lowerSideColor);
            }
        }

        if (verts.Count == 0)
        {
            return null;
        }

        return new LodPayload(verts.ToArray(), normals.ToArray(), colors.ToArray());
    }

    internal static bool IsWetShoreCell(bool isWater, int height, int seaLevel,
            int h00, int h10, int h01, int h11) =>
            !isWater && height <= seaLevel + 2
            && (h00 <= seaLevel || h10 <= seaLevel
                    || h01 <= seaLevel || h11 <= seaLevel);

    private static BlockId LandBlock(Biome biome, int relief)
    {
        if (relief >= 3)
        {
            return BlockId.Stone;
        }

        return biome switch
        {
            Biome.Desert => BlockId.Sand,
            Biome.Snow => BlockId.SnowGrass,
            _ => BlockId.Grass,
        };
    }

    private static BlockId UnderBlock(BlockId surface) => surface switch
    {
        BlockId.Grass or BlockId.SnowGrass or BlockId.Farmland => BlockId.Dirt,
        _ => surface,
    };

    private Color TopColor(BlockId id) =>
        (uint)id < (uint)_topColors.Length && BlockDb.IsDefined(id)
                ? _topColors[(int)id] : _topColors[(int)BlockId.Grass];

    private Color SideColor(BlockId id) =>
        (uint)id < (uint)_sideColors.Length && BlockDb.IsDefined(id)
                ? _sideColors[(int)id] : _sideColors[(int)BlockId.Dirt];

    private static void AddSkirt(List<Vector3> verts, List<Vector3> normals,
            List<Color> colors, Vector3 topA, Vector3 topB, Vector3 bottomA, Vector3 bottomB,
            Vector3 normal, bool reverse, float drop, Color capColor, Color lowerColor)
    {
        float capDrop = Mathf.Min(1.0f, drop);
        Vector3 capA = topA + Vector3.Down * capDrop;
        Vector3 capB = topB + Vector3.Down * capDrop;
        if (reverse)
        {
            AddQuad(verts, normals, colors, capA, topA, topB, capB, normal, capColor, 0.72f);
        }
        else
        {
            AddQuad(verts, normals, colors, topA, capA, capB, topB, normal, capColor, 0.72f);
        }

        if (drop > capDrop)
        {
            if (reverse)
            {
                AddQuad(verts, normals, colors, bottomA, capA, capB, bottomB, normal, lowerColor, 0.72f);
            }
            else
            {
                AddQuad(verts, normals, colors, capA, bottomA, bottomB, capB, normal, lowerColor, 0.72f);
            }
        }
    }

    private static void AddQuad(List<Vector3> verts, List<Vector3> normals,
            List<Color> colors, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector3 nrm, Color col, float shade)
    {
        Color c = new(col.R * shade, col.G * shade, col.B * shade);
        Span<Vector3> points = stackalloc Vector3[4] { p0, p1, p2, p3 };
        foreach (int idx in Mesher.TriOrder)
        {
            verts.Add(points[idx]);
            normals.Add(nrm);
            colors.Add(c);
        }
    }

    private void ApplyTile(Vector2I tc, BuildToken token, LodPayload? payload, string? failure)
    {
        try
        {
            if (!_alive
                    || !_inFlight.TryGetValue(tc, out InFlightBuild active)
                    || active.Token != token)
            {
                return;
            }

            bool tileExists = _tiles.TryGetValue(tc, out Tile? tile) && tile != null;
            bool tokenIsCurrent = tile != null && FarLodLifecycleRules.IsCurrent(
                    tile.Generation, tile.Revision, tile.CoverageMask,
                    token.Generation, token.Revision, token.CoverageMask);
            if (!FarLodLifecycleRules.TryCompleteInFlight(
                    _inFlight, tc, active, tileExists, tokenIsCurrent, failure != null,
                    out FarLodCompletionDisposition disposition))
            {
                return;
            }

            switch (disposition)
            {
                case FarLodCompletionDisposition.Discard:
                    // The tile was evicted while its worker was running.
                    // The finally block below still releases queue capacity.
                    return;

                case FarLodCompletionDisposition.RebuildLatest:
                    // The coordinate may now refer to a changed or recreated
                    // tile. Never apply a payload from its previous generation.
                    Enqueue(tc);
                    return;

                case FarLodCompletionDisposition.Failed:
                    {
                        int attempt = ++tile!.FailureCount;
                        if (FarLodLifecycleRules.CanRetry(attempt - 1))
                        {
                            GD.PushWarning(
                                    $"Far LOD build failed for tile {tc}; retry {attempt}/{FarLodLifecycleRules.MaxBuildRetries}: {failure}");
                            Enqueue(tc);
                        }
                        else
                        {
                            GD.PushError(
                                    $"Far LOD build exhausted its {FarLodLifecycleRules.MaxBuildRetries} retries for tile {tc}; no replacement surface was produced: {failure}");
                        }

                        return;
                    }

                case FarLodCompletionDisposition.Apply:
                    {
                        Tile currentTile = tile!;
                        currentTile.FailureCount = 0;
                        ApplyPayload(tc, currentTile, payload);
                        return;
                    }
            }
        }
        finally
        {
            // Dispatch after every terminal outcome: success, empty result,
            // failure, stale result, and completion for an evicted tile.
            if (_alive)
            {
                Dispatch();
            }
        }
    }

    private void ApplyPayload(Vector2I tc, Tile tile, LodPayload? payload)
    {
        if (payload == null)
        {
            if (tile.Node != null)
            {
                tile.Node.Mesh = null;
            }

            return;
        }

        MeshInstance3D? node = tile.Node;
        if (node == null)
        {
            node = new MeshInstance3D
            {
                Name = $"lod_{tc.X}_{tc.Y}",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = _material,
            };
            AddChild(node);
            tile.Node = node;
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = payload.Verts;
        arrays[(int)Mesh.ArrayType.Normal] = payload.Normals;
        arrays[(int)Mesh.ArrayType.Color] = payload.Colors;
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        node.Mesh = mesh;
    }
}


internal readonly record struct FarLodBuildOutcome<TPayload>(
        TPayload? Payload, string? Failure, bool Cancelled = false)
        where TPayload : class;

internal enum FarLodCompletionDisposition
{
    Discard,
    RebuildLatest,
    Failed,
    Apply,
}

/// <summary>
/// Pure lifecycle rules shared with the Far-LOD regression tests. The caller
/// supplies VoxelWorld.UnloadMargin so full-resolution ownership and Far LOD
/// geometry use the same authoritative retention radius.
/// </summary>
internal static class FarLodLifecycleRules
{
    internal const int MaxBuildRetries = 2;
    internal const int ChunksPerTile = 8;

    /// <summary>
    /// Far LOD must cover at least the configured detailed-chunk radius so an
    /// unbuilt or still-queued chunk never leaves a hole in the visible world.
    /// A larger explicit LOD radius may extend the horizon further.
    /// </summary>
    internal static float EffectiveRadius(float configuredRadius, int viewDistance, int chunkSize)
    {
        float streamedRadius = Math.Max(2, viewDistance) * (float)Math.Max(1, chunkSize);
        return Math.Max(Math.Max(0.0f, configuredRadius), streamedRadius);
    }

    internal static Vector2I TileForChunk(Vector2I chunk) => new(
            Mathf.FloorToInt(chunk.X / (float)ChunksPerTile),
            Mathf.FloorToInt(chunk.Y / (float)ChunksPerTile));

    internal static ulong SetCoverageBit(
            ulong mask, int localChunkX, int localChunkZ, bool covered)
    {
        if ((uint)localChunkX >= ChunksPerTile || (uint)localChunkZ >= ChunksPerTile)
        {
            return mask;
        }

        ulong bit = 1UL << (localChunkZ * ChunksPerTile + localChunkX);
        return covered ? mask | bit : mask & ~bit;
    }

    internal static ulong SetChunkCoverage(
            ulong mask, Vector2I tile, Vector2I chunk, bool covered)
    {
        int localX = chunk.X - tile.X * ChunksPerTile;
        int localZ = chunk.Y - tile.Y * ChunksPerTile;
        return SetCoverageBit(mask, localX, localZ, covered);
    }

    internal static bool IsChunkCovered(ulong mask, int localChunkX, int localChunkZ)
    {
        if ((uint)localChunkX >= ChunksPerTile || (uint)localChunkZ >= ChunksPerTile)
        {
            return false;
        }

        ulong bit = 1UL << (localChunkZ * ChunksPerTile + localChunkX);
        return (mask & bit) != 0;
    }

    internal static bool ShouldEmitCell(
            ulong coverageMask, Vector2I tile, int worldX, int worldZ,
            bool isSky, bool hasIsland)
    {
        int chunkX = Mathf.FloorToInt(worldX / (float)VoxelWorld.ChunkX);
        int chunkZ = Mathf.FloorToInt(worldZ / (float)VoxelWorld.ChunkZ);
        int localX = chunkX - tile.X * ChunksPerTile;
        int localZ = chunkZ - tile.Y * ChunksPerTile;
        bool detailedChunkReady = IsChunkCovered(coverageMask, localX, localZ);
        return !detailedChunkReady && (!isSky || hasIsland);
    }

    internal static bool IsCurrent(
            long tileGeneration, long tileRevision, ulong tileCoverageMask,
            long buildGeneration, long buildRevision, ulong buildCoverageMask) =>
            tileGeneration == buildGeneration
            && tileRevision == buildRevision
            && tileCoverageMask == buildCoverageMask;

    internal static FarLodCompletionDisposition ClassifyCompletion(
            bool tileExists, bool tokenIsCurrent, bool failed)
    {
        if (!tileExists) return FarLodCompletionDisposition.Discard;
        if (!tokenIsCurrent) return FarLodCompletionDisposition.RebuildLatest;
        return failed ? FarLodCompletionDisposition.Failed : FarLodCompletionDisposition.Apply;
    }

    internal static bool IsMatchingBuildRequest(long activeRequestId, long completedRequestId) =>
            activeRequestId == completedRequestId;

    internal static bool TryCompleteInFlight<TKey, TValue>(
            Dictionary<TKey, TValue> registry, TKey key, TValue expected,
            bool tileExists, bool tokenIsCurrent, bool failed,
            out FarLodCompletionDisposition disposition)
            where TKey : notnull
            where TValue : notnull
    {
        if (!TryReleaseInFlight(registry, key, expected))
        {
            disposition = FarLodCompletionDisposition.Discard;
            return false;
        }

        disposition = ClassifyCompletion(tileExists, tokenIsCurrent, failed);
        return true;
    }

    internal static bool CanRetry(int previousFailureCount) =>
            previousFailureCount < MaxBuildRetries;

    internal static bool TryReleaseInFlight<TKey, TValue>(
            Dictionary<TKey, TValue> registry, TKey key, TValue expected)
            where TKey : notnull
            where TValue : notnull
    {
        if (!registry.TryGetValue(key, out TValue? active)
                || active is null
                || !EqualityComparer<TValue>.Default.Equals(active, expected))
        {
            return false;
        }

        return registry.Remove(key);
    }

    internal static FarLodBuildOutcome<TPayload> CaptureBuild<TPayload>(Func<TPayload?> build)
            where TPayload : class =>
            CaptureBuild(build, CancellationToken.None);

    internal static FarLodBuildOutcome<TPayload> CaptureBuild<TPayload>(
            Func<TPayload?> build, CancellationToken cancellationToken)
            where TPayload : class
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TPayload? payload = build();
            cancellationToken.ThrowIfCancellationRequested();
            return new FarLodBuildOutcome<TPayload>(payload, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new FarLodBuildOutcome<TPayload>(null, null, Cancelled: true);
        }
        catch (Exception exception)
        {
            return new FarLodBuildOutcome<TPayload>(null, exception.ToString());
        }
    }

    internal static bool TryDequeueEligibleTile<TTile, TBuild>(
            FarLodWorkQueue queue, IReadOnlyDictionary<Vector2I, TTile> tiles,
            IReadOnlyDictionary<Vector2I, TBuild> inFlight,
            out Vector2I coordinate, out TTile tile)
            where TTile : notnull
            where TBuild : notnull
    {
        while (queue.TryDequeue(out coordinate))
        {
            if (!inFlight.ContainsKey(coordinate)
                    && tiles.TryGetValue(coordinate, out tile!))
            {
                return true;
            }
        }

        coordinate = default;
        tile = default!;
        return false;
    }

    internal static bool CanDispatch(
            int inFlightCount, int queuedCount, int buildLimit, bool alive) =>
            alive && inFlightCount < buildLimit && queuedCount > 0;
}

/// <summary>
/// FIFO of pending Far-LOD tile coordinates. It keeps membership O(1), avoids
/// shifting the whole queue for each dispatch, and can compact evicted tiles
/// after an update without allocating a replacement collection.
/// </summary>
internal sealed class FarLodWorkQueue
{
    private readonly List<Vector2I> _items = [];
    private readonly HashSet<Vector2I> _queued = [];
    private int _head;

    internal int Count => _items.Count - _head;

    internal bool Enqueue(Vector2I coordinate)
    {
        if (!_queued.Add(coordinate))
        {
            return false;
        }

        _items.Add(coordinate);
        return true;
    }

    internal bool TryDequeue(out Vector2I coordinate)
    {
        if (_head >= _items.Count)
        {
            coordinate = default;
            return false;
        }

        coordinate = _items[_head++];
        _queued.Remove(coordinate);
        if (_head >= 64 && _head * 2 >= _items.Count)
        {
            _items.RemoveRange(0, _head);
            _head = 0;
        }

        return true;
    }

    internal void Clear()
    {
        _items.Clear();
        _queued.Clear();
        _head = 0;
    }

    internal void Prune<TValue>(IReadOnlyDictionary<Vector2I, TValue> liveTiles)
    {
        int write = _head;
        for (int read = _head; read < _items.Count; read++)
        {
            Vector2I coordinate = _items[read];
            if (liveTiles.ContainsKey(coordinate))
            {
                _items[write++] = coordinate;
            }
            else
            {
                _queued.Remove(coordinate);
            }
        }

        if (write < _items.Count)
        {
            _items.RemoveRange(write, _items.Count - write);
        }

        if (_head > 0)
        {
            _items.RemoveRange(0, _head);
            _head = 0;
        }
    }
}
