using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Minecraft;

/// <summary>
/// Far-view LOD ring: coarse heightmap tiles beyond the streamed chunk area so
/// the horizon shows distant terrain instead of pop-in. Cells sample terrain
/// height / biome straight from the world noise (no chunk data, no edits, no
/// collision). Cells inside the streamed chunk square are skipped, so tiles
/// touching it rebuild only when the square advances across them.
///
/// Builds run on WorkerThreadPool workers (pure noise + array math); meshes
/// are applied on the main thread.
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
    private bool _alive = true;

    private StandardMaterial3D _material = null!;
    private Color _colGrass = Colors.White;
    private Color _colSand = Colors.White;
    private Color _colSnow = Colors.White;
    private Color _colWater = Colors.White;
    private Color _colStone = Colors.White;

    public void Setup(VoxelWorld world)
    {
        World = world;
        _material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        _colGrass = BlockDb.AverageTileColor(BlockId.Grass);
        _colSand = BlockDb.AverageTileColor(BlockId.Sand);
        _colSnow = BlockDb.AverageTileColor(BlockId.SnowGrass);
        _colWater = BlockDb.AverageTileColor(BlockId.Water);
        _colStone = BlockDb.AverageTileColor(BlockId.Stone);
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
    // Mesh building (worker thread: pure noise + arrays)
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
                // Player edits win over noise: the topmost edited block of the
                // cell's column keeps built/dig changes visible after unload.
                if (World.TryEditTop(cx0 + 2, cz0 + 2, out int editedTop))
                {
                    hc = editedTop;
                }

                bool isWater = hc <= sea;
                float y = isWater ? sea + 1 : hc + 1;
                Vector3 b = new(cx0, y, cz0);
                Color col = isWater
                        ? _colWater
                        : hc <= sea + 1
                                ? _colSand
                                : LandColor(World.BiomeAt(cx0 + 2, cz0 + 2, hc),
                                        hc - Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11)));
                AddQuad(verts, normals, colors,
                        [b, b + new Vector3(CellSize, 0, 0), b + new Vector3(CellSize, 0, CellSize),
                         b + new Vector3(0, 0, CellSize)],
                        Vector3.Up, col, 1.0f);
                if (isWater)
                {
                    continue;
                }

                // Skirts toward lower shared edges: one quad per side, using the
                // exact winding/normals BlockDb uses for its +/- X/Z faces.
                float cx1 = cx0 + CellSize;
                float cz1 = cz0 + CellSize;
                // +X
                int drop = hc - Mathf.Max(h10, h11);
                if (drop > 0)
                {
                    float d = drop;
                    AddQuad(verts, normals, colors,
                            [new Vector3(cx1, y, cz0), new Vector3(cx1, y - d, cz0),
                             new Vector3(cx1, y - d, cz1), new Vector3(cx1, y, cz1)],
                            Vector3.Right, col, 0.72f);
                }

                // -X
                drop = hc - Mathf.Max(h00, h01);
                if (drop > 0)
                {
                    float d = drop;
                    AddQuad(verts, normals, colors,
                            [new Vector3(cx0, y - d, cz0), new Vector3(cx0, y, cz0),
                             new Vector3(cx0, y, cz1), new Vector3(cx0, y - d, cz1)],
                            Vector3.Left, col, 0.72f);
                }

                // +Z
                drop = hc - Mathf.Max(h01, h11);
                if (drop > 0)
                {
                    float d = drop;
                    AddQuad(verts, normals, colors,
                            [new Vector3(cx0, y - d, cz1), new Vector3(cx0, y, cz1),
                             new Vector3(cx1, y, cz1), new Vector3(cx1, y - d, cz1)],
                            Vector3.Back, col, 0.72f);
                }

                // -Z
                drop = hc - Mathf.Max(h00, h10);
                if (drop > 0)
                {
                    float d = drop;
                    AddQuad(verts, normals, colors,
                            [new Vector3(cx1, y - d, cz0), new Vector3(cx1, y, cz0),
                             new Vector3(cx0, y, cz0), new Vector3(cx0, y - d, cz0)],
                            Vector3.Forward, col, 0.72f);
                }
            }
        }

        if (verts.Count == 0)
        {
            return null;
        }

        return new LodPayload(verts.ToArray(), normals.ToArray(), colors.ToArray());
    }

    private Color LandColor(Biome biome, int relief)
    {
        if (relief >= 3)
        {
            return _colStone;
        }

        return biome switch
        {
            Biome.Desert => _colSand,
            Biome.Snow => _colSnow,
            _ => _colGrass,
        };
    }

    private static void AddQuad(List<Vector3> verts, List<Vector3> normals,
            List<Color> colors, Vector3[] p, Vector3 nrm, Color col, float shade)
    {
        Color c = new(col.R * shade, col.G * shade, col.B * shade);
        foreach (int idx in Mesher.TriOrder)
        {
            verts.Add(p[idx]);
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
