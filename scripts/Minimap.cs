using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Top-down minimap + compass. Terrain is rebuilt in small row slices so a
/// 96×96 map never stalls a frame, with hill shading, biome/water colours,
/// campfire/spawn/respawn waypoints, a heading arrow and zoom levels.
/// </summary>
public partial class Minimap : Control
{
    private const int Px = 128;
    private const int SliceRows = 16;
    private static readonly float[] ZoomLevels = [1.0f, 2.0f, 4.0f, 8.0f];

    public VoxelWorld? World;
    public Player? PlayerRef;

    private Image _img = null!;
    private ImageTexture _tex = null!;
    private readonly int[] _heights = new int[Px * Px];
    private readonly Biome[] _biomes = new Biome[Px * Px];
    private readonly bool[] _edited = new bool[Px * Px];
    private double _timer;
    private Vector2 _lastCentre;
    private int _zoom = 1;
    private int _buildRow;
    private bool _building;
    private bool _rebuildDirty = true;
    private int _mutationVersion;
    private int _buildMutationVersion;
    private float _lastYaw = float.NaN;
    private VoxelWorld? _subscribedWorld;
    private int _buildCx;
    private int _buildCz;
    private VoxelWorld? _spawnWorld;
    private Vector3 _spawn;

    private Color _sand = new(0.86f, 0.81f, 0.61f);
    private Color _grass = new(0.34f, 0.56f, 0.24f);
    private Color _birch = new(0.48f, 0.68f, 0.30f);
    private Color _denseForest = new(0.12f, 0.34f, 0.18f);
    private Color _taiga = new(0.25f, 0.43f, 0.35f);
    private Color _jungle = new(0.10f, 0.40f, 0.22f);
    private Color _swamp = new(0.28f, 0.38f, 0.20f);
    private Color _flowerPlains = new(0.62f, 0.68f, 0.30f);
    private Color _meadow = new(0.48f, 0.70f, 0.34f);
    private Color _snow = new(0.88f, 0.92f, 0.97f);
    private Color _shallow = new(0.20f, 0.48f, 0.72f);
    private Color _deep = new(0.08f, 0.22f, 0.48f);
    private Color _stone = new(0.55f, 0.55f, 0.57f);

    internal static bool HeadingChanged(float previous, float current) =>
            !float.IsFinite(previous)
            || Mathf.Abs(Mathf.Wrap(current - previous, -Mathf.Pi, Mathf.Pi)) > 0.001f;

    internal static bool IsPositionWithinMap(Vector3I position, Vector2 centre, float halfExtent) =>
            Mathf.Abs(position.X - centre.X) <= halfExtent
            && Mathf.Abs(position.Z - centre.Y) <= halfExtent;

    public float MetresPerPx => ZoomLevels[_zoom];
    public string ScaleLabel => $"{Mathf.RoundToInt(Px * MetresPerPx)} m";

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(176, 176);
        MouseFilter = MouseFilterEnum.Ignore;
        _img = Image.CreateEmpty(Px, Px, false, Image.Format.Rgba8);
        _tex = ImageTexture.CreateFromImage(_img);
    }

    public void ZoomIn()
    {
        _zoom = Mathf.Max(_zoom - 1, 0);
        StartRebuild();
    }

    public void ZoomOut()
    {
        _zoom = Mathf.Min(_zoom + 1, ZoomLevels.Length - 1);
        StartRebuild();
    }

    public override void _Process(double delta)
    {
        if (World != _subscribedWorld)
        {
            if (_subscribedWorld != null)
                _subscribedWorld.BlockChanged -= OnBlockChanged;
            _subscribedWorld = World;
            if (_subscribedWorld != null)
                _subscribedWorld.BlockChanged += OnBlockChanged;
            _building = false;
            _rebuildDirty = true;
            _lastCentre = Vector2.Zero;
            _timer = 0.0;
        }

        if (World == null || PlayerRef == null)
        {
            if (_building)
            {
                _building = false;
                _rebuildDirty = true;
            }
            return;
        }

        float yaw = PlayerRef.Rotation.Y;
        if (HeadingChanged(_lastYaw, yaw))
        {
            _lastYaw = yaw;
            QueueRedraw();
        }

        if (!IsVisibleInTree())
        {
            if (_building)
            {
                _building = false;
                _rebuildDirty = true;
            }
            return;
        }

        if (_building)
        {
            BuildSlice();
            return;
        }

        Vector2 here = new(PlayerRef.GlobalPosition.X, PlayerRef.GlobalPosition.Z);
        bool moved = _lastCentre.DistanceTo(here) > 2.0f;
        if (_rebuildDirty)
        {
            StartRebuild();
            return;
        }

        _timer -= delta;
        if (_timer > 0.0)
            return;

        _timer = moved ? 0.55 : 2.5;
        if (moved)
            StartRebuild();
    }

    private void OnBlockChanged(Vector3I position)
    {
        Vector2 centre = _building ? new Vector2(_buildCx, _buildCz) : _lastCentre;
        float halfExtent = Px * MetresPerPx * 0.5f;
        if (!IsPositionWithinMap(position, centre, halfExtent))
            return;

        _mutationVersion++;
        _rebuildDirty = true;
        _timer = 0.0;
    }

    public override void _ExitTree()
    {
        if (_subscribedWorld != null)
        {
            _subscribedWorld.BlockChanged -= OnBlockChanged;
            _subscribedWorld = null;
        }
    }

    private void StartRebuild()
    {
        if (World == null || PlayerRef == null)
        {
            return;
        }

        _buildCx = Mathf.FloorToInt(PlayerRef.GlobalPosition.X);
        _buildCz = Mathf.FloorToInt(PlayerRef.GlobalPosition.Z);
        _buildMutationVersion = _mutationVersion;
        _buildRow = 0;
        _building = true;
        _rebuildDirty = true;
    }

    private void BuildSlice()
    {
        if (World == null)
        {
            _building = false;
            return;
        }

        int end = Mathf.Min(_buildRow + SliceRows, Px);
        for (int j = _buildRow; j < end; j++)
        {
            for (int i = 0; i < Px; i++)
            {
                int wx = _buildCx + (int)((i - Px / 2) * MetresPerPx);
                int wz = _buildCz + (int)((j - Px / 2) * MetresPerPx);
                (int h, Biome biome) = World.SurfaceSampleAt(wx, wz);
                bool edited = World.TryEditTop(wx, wz, out int editedTop);
                if (edited)
                {
                    h = editedTop;
                }

                int index = i + j * Px;
                _heights[index] = h;
                _biomes[index] = biome;
                _edited[index] = edited;
            }
        }

        _buildRow = end;
        if (_buildRow < Px)
        {
            return;
        }

        Colorize();
        _tex.Update(_img);
        _building = false;
        if (_buildMutationVersion == _mutationVersion)
        {
            _lastCentre = new Vector2(_buildCx, _buildCz);
            _rebuildDirty = false;
        }
        else
        {
            _rebuildDirty = true;
        }
        _timer = 2.5;
        QueueRedraw();
    }

    private void Colorize()
    {
        int sea = World!.SeaLevel;
        for (int j = 0; j < Px; j++)
        {
            for (int i = 0; i < Px; i++)
            {
                int index = i + j * Px;
                int h = _heights[index];
                bool edited = _edited[index];
                int wx = _buildCx + (int)((i - Px / 2) * MetresPerPx);
                int wz = _buildCz + (int)((j - Px / 2) * MetresPerPx);

                Color c;
                if (World.IsSky && !edited && !World.IslandAt(wx, wz))
                {
                    c = new Color(0.04f, 0.06f, 0.11f);
                }
                else if (h <= sea)
                {
                    float depth = Mathf.Clamp((sea - h) / 12.0f, 0.0f, 1.0f);
                    c = _shallow.Lerp(_deep, depth);
                }
                else if (h <= sea + 1)
                {
                    c = _sand;
                }
                else
                {
                    c = _biomes[index] switch
                    {
                        Biome.Desert or Biome.Beach => _sand,
                        Biome.Snow => _snow,
                        Biome.Birch => _birch,
                        Biome.DenseForest => _denseForest,
                        Biome.Taiga => _taiga,
                        Biome.Jungle => _jungle,
                        Biome.Swamp => _swamp,
                        Biome.Mountain => _stone,
                        Biome.FlowerPlains => _flowerPlains,
                        Biome.Meadow => _meadow,
                        _ => _grass,
                    };
                    float lift = Mathf.Clamp((h - sea) / 20.0f, 0.0f, 1.0f);
                    c = c.Lerp(_stone, lift * 0.55f).Lightened(lift * 0.12f);
                }

                // Hill shade from the two nearest neighbours; this is what
                // turns a flat colour map into readable terrain.
                int right = _heights[Math.Min(i + 1, Px - 1) + j * Px];
                int down = _heights[i + Math.Min(j + 1, Px - 1) * Px];
                float shade = 1.0f + (h - right) * 0.06f + (h - down) * 0.06f;
                shade = Mathf.Clamp(shade, 0.72f, 1.18f);
                c = new Color(
                        Mathf.Clamp(c.R * shade, 0.0f, 1.0f),
                        Mathf.Clamp(c.G * shade, 0.0f, 1.0f),
                        Mathf.Clamp(c.B * shade, 0.0f, 1.0f),
                        c.A);
                _img.SetPixel(i, j, c);
            }
        }
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        Vector2 origin = (size - new Vector2(Px, Px)) / 2.0f;
        Rect2 mapRect = new(origin, new Vector2(Px, Px));
        DrawTextureRect(_tex, mapRect, false);
        DrawRect(mapRect, new Color(0.95f, 0.97f, 1.0f, 0.18f), false, 1.0f);

        Font font = GetThemeDefaultFont();
        DrawCompass(font, origin, "N", new Vector2(Px / 2.0f, 12), HorizontalAlignment.Center);
        DrawCompass(font, origin, "E", new Vector2(Px - 8, Px / 2.0f + 4), HorizontalAlignment.Right);
        DrawCompass(font, origin, "S", new Vector2(Px / 2.0f, Px - 3), HorizontalAlignment.Center);
        DrawCompass(font, origin, "W", new Vector2(6, Px / 2.0f + 4), HorizontalAlignment.Left);

        if (World == null || PlayerRef == null)
        {
            return;
        }

        Vector3 pp = PlayerRef.GlobalPosition;
        Vector2 centre = origin + new Vector2(Px / 2.0f, Px / 2.0f);

        foreach (Vector3I site in World.NearbyCampfireSites(pp, Px * MetresPerPx * 0.5f))
        {
            DrawWaypoint(centre, mapRect, site.X + 0.5f, site.Z + 0.5f, pp,
                    new Color(1.0f, 0.62f, 0.22f), 2.4f);
        }

        if (PlayerRef.RespawnPoint is { } respawn)
        {
            DrawWaypoint(centre, mapRect, respawn.X, respawn.Z, pp,
                    new Color(0.95f, 0.35f, 0.25f), 3.0f, true);
        }

        Vector3 spawn = SpawnPoint();
        DrawWaypoint(centre, mapRect, spawn.X, spawn.Z, pp,
                new Color(0.75f, 0.92f, 1.0f), 2.8f, true);

        // Player heading arrow with a dark outline for readability.
        float yaw = PlayerRef.Rotation.Y;
        Vector2 dir = new(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        Vector2 perp = new(-dir.Y, dir.X);
        Vector2[] outline =
        [
            centre + dir * 10.0f,
            centre - dir * 5.0f + perp * 5.0f,
            centre - dir * 5.0f - perp * 5.0f,
        ];
        Vector2[] arrow =
        [
            centre + dir * 8.0f,
            centre - dir * 3.5f + perp * 3.5f,
            centre - dir * 3.5f - perp * 3.5f,
        ];
        DrawColoredPolygon(outline, new Color(0, 0, 0, 0.65f));
        DrawColoredPolygon(arrow, Colors.White);

        string scale = ScaleLabel;
        DrawString(font, origin + new Vector2(4, Px - 5), scale,
                HorizontalAlignment.Left, -1, 10, new Color(1, 1, 1, 0.8f));
    }

    private Vector3 SpawnPoint()
    {
        if (_spawnWorld != World)
        {
            _spawnWorld = World;
            _spawn = World?.FindSpawn() ?? Vector3.Zero;
        }

        return _spawn;
    }

    private void DrawWaypoint(Vector2 centre, Rect2 mapRect, float x, float z, Vector3 player,
            Color color, float radius, bool ring = false)
    {
        Vector2 p = centre + new Vector2(x - player.X, z - player.Z) / MetresPerPx;
        if (!mapRect.Grow(2.0f).HasPoint(p))
        {
            return;
        }

        if (ring)
        {
            DrawCircle(p, radius + 2.0f, new Color(0, 0, 0, 0.55f));
            DrawCircle(p, radius, color);
            DrawArc(p, radius + 2.5f, 0.0f, Mathf.Tau, 20, new Color(1, 1, 1, 0.85f), 1.0f);
        }
        else
        {
            DrawCircle(p, radius + 1.0f, new Color(0, 0, 0, 0.45f));
            DrawCircle(p, radius, color);
        }
    }

    private void DrawCompass(Font font, Vector2 origin, string text, Vector2 at,
            HorizontalAlignment alignment)
    {
        Vector2 pos = origin + at;
        DrawString(font, pos + new Vector2(1, 1), text, alignment, -1, 10, new Color(0, 0, 0, 0.6f));
        DrawString(font, pos, text, alignment, -1, 10, new Color(1, 1, 1, 0.75f));
    }
}
