using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Minecraft;

/// <summary>
/// Far-view LOD ring: coarse heightmap tiles beyond the streamed chunk area so
/// the horizon shows distant terrain instead of pop-in. Cells sample terrain
/// height / biome from world noise and sparse edit-top data (no chunk data or
/// collision). Edited surface IDs preserve dirt/farmland colors after unload.
/// Cells inside the streamed chunk square are skipped, so tiles touching it
/// rebuild only when the square advances across them.
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
        public Rect2I Skip;
    }

    private sealed record LodPayload(Vector3[] Verts, Vector3[] Normals, Color[] Colors);

    private readonly Dictionary<Vector2I, Tile> _tiles = new();  // Vector2I -> tile
    private readonly List<Vector2I> _queue = [];
    private readonly Dictionary<Vector2I, long> _inFlight = new();
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
        foreach (long tid in _inFlight.Values)
        {
            WorkerThreadPool.WaitForTaskCompletion(tid);
        }

        _inFlight.Clear();
    }

    /// <summary>
    /// Re-evaluate the ring around the player's chunk: queue missing tiles
    /// nearest-first, rebuild edge tiles whose streamed-area overlap changed,
    /// free tiles now out of range.
    /// </summary>
    public void UpdateAround(Vector2I pcp)
    {
        float radius = World!.LodRadius;
        if (radius <= 0.0f)
        {
            foreach (Tile tile in _tiles.Values)
            {
                tile.Node?.QueueFree();
            }

            _tiles.Clear();
            _queue.Clear();
            return;
        }

        Vector2 center = new((pcp.X + 0.5f) * 16.0f, (pcp.Y + 0.5f) * 16.0f);
        int tcx = (int)System.Math.Floor(center.X / Region);
        int tcz = (int)System.Math.Floor(center.Y / Region);
        int ring = (int)System.Math.Ceiling((radius + Region * 0.7f) / Region);
        Rect2I streamed = StreamedChunks(pcp);
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
                Rect2I skip = TileSkip(tc, streamed);
                if (_tiles.TryGetValue(tc, out Tile? tile))
                {
                    if (tile.Skip != skip)
                    {
                        tile.Skip = skip;
                        Enqueue(tc);
                    }
                }
                else
                {
                    _tiles[tc] = new Tile { Node = null, Skip = skip };
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
            }
        }

        Dispatch();
    }

    private Rect2I StreamedChunks(Vector2I pcp)
    {
        int r = World!.ViewDistance;
        return new Rect2I(pcp.X - r, pcp.Y - r, r * 2 + 1, r * 2 + 1);
    }

    /// <summary>
    /// Streamed-chunk intersection with this tile, in chunk coords; empty when the
    /// streamed square does not reach the tile, which keeps far tiles stable.
    /// </summary>
    private static Rect2I TileSkip(Vector2I tc, Rect2I streamed)
    {
        Rect2I tileRect = new(tc.X * TileChunks, tc.Y * TileChunks, TileChunks, TileChunks);
        return tileRect.Intersection(streamed);
    }

    private void Enqueue(Vector2I tc)
    {
        if (_inFlight.ContainsKey(tc) || _queue.Contains(tc))
        {
            return;
        }

        _queue.Add(tc);
    }

    private void Dispatch()
    {
        while (_inFlight.Count < BuildLimit && _queue.Count > 0)
        {
            Vector2I tc = _queue[0];
            _queue.RemoveAt(0);
            if (_inFlight.ContainsKey(tc))
            {
                continue;
            }

            Rect2I skip = TileSkip(tc, StreamedChunks(World!.PlayerChunk()));
            long tid = WorkerThreadPool.AddTask(
                    Callable.From(() => BuildTask(tc, skip)), true, $"lod_{tc.X}_{tc.Y}");
            _inFlight[tc] = tid;
        }
    }

    private void BuildTask(Vector2I tc, Rect2I skip)
    {
        LodPayload? payload = BuildArrays(tc, skip);
        if (_alive)
        {
            Callable.From(() => ApplyTile(tc, payload)).CallDeferred();
        }
    }

    // -----------------------------------------------------------------------
    // Mesh building (worker thread: noise, edit-cache lookup and arrays)
    // -----------------------------------------------------------------------

    private LodPayload? BuildArrays(Vector2I tc, Rect2I skip)
    {
        int x0 = tc.X * (int)Region;
        int z0 = tc.Y * (int)Region;
        int sea = World!.SeaLevel;
        List<Vector3> verts = [];
        List<Vector3> normals = [];
        List<Color> colors = [];
        for (int j = 0; j < Cells; j++)
        {
            int cz0 = z0 + j * (int)CellSize;
            for (int i = 0; i < Cells; i++)
            {
                int cx0 = x0 + i * (int)CellSize;
                Vector2I cellChunk = new(Mathf.FloorToInt(cx0 / 16.0f), Mathf.FloorToInt(cz0 / 16.0f));
                if (skip.HasPoint(cellChunk))
                {
                    continue;
                }

                if (World.IsSky && !World.IslandAt(cx0 + 2, cz0 + 2))
                {
                    continue;   // open sky below the floating islands
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
                float y = isWater ? sea + 1 : hc + 1;
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

    private void ApplyTile(Vector2I tc, LodPayload? payload)
    {
        _inFlight.Remove(tc);
        if (!_alive || !_tiles.TryGetValue(tc, out Tile? tile))
        {
            return;
        }

        if (payload == null)
        {
            if (tile.Node != null)
            {
                tile.Node.Mesh = null;
            }

            Dispatch();
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
        Dispatch();
    }
}
