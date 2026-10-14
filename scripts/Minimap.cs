using Godot;

namespace Minecraft;

/// <summary>
/// Top-down minimap + compass: a small cached surface-colour image around the
/// player (noise + biome, rebuilt a few times a second), a heading needle, and
/// waypoint dots for campfires, the world spawn and the respawn anchor.
/// </summary>
public partial class Minimap : Control
{
    private const int Px = 96;                 // image resolution
    private const float MetresPerPx = 2.0f;    // 192 m across

    public VoxelWorld? World;
    public Player? PlayerRef;

    private Image _img = null!;
    private ImageTexture _tex = null!;
    private double _timer;
    private Vector2 _lastCentre;
    private Color _sand = new(0.86f, 0.81f, 0.61f);
    private Color _grass = new(0.34f, 0.56f, 0.24f);
    private Color _snow = new(0.88f, 0.92f, 0.97f);
    private Color _water = new(0.16f, 0.33f, 0.62f);
    private Color _stone = new(0.55f, 0.55f, 0.57f);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(150, 150);
        MouseFilter = MouseFilterEnum.Ignore;
        _img = Image.CreateEmpty(Px, Px, false, Image.Format.Rgba8);
        _tex = ImageTexture.CreateFromImage(_img);
    }

    public override void _Process(double delta)
    {
        if (World == null || PlayerRef == null || !IsVisibleInTree())
        {
            return;   // hidden HUD or minimap costs nothing
        }

        _timer -= delta;
        if (_timer <= 0.0)
        {
            // A rebuild is 96x96 columns of terrain noise: only when the player
            // actually moved, or occasionally in place (edits, waypoints).
            Vector2 here = new(PlayerRef.GlobalPosition.X, PlayerRef.GlobalPosition.Z);
            bool moved = _lastCentre.DistanceTo(here) > 2.0f;
            _timer = moved ? 0.5 : 3.0;
            _lastCentre = here;
            Rebuild();
            QueueRedraw();
        }
    }

    private void Rebuild()
    {
        int cx = Mathf.FloorToInt(PlayerRef!.GlobalPosition.X);
        int cz = Mathf.FloorToInt(PlayerRef.GlobalPosition.Z);
        int half = Px / 2;
        int sea = World!.SeaLevel;
        for (int j = 0; j < Px; j++)
        {
            for (int i = 0; i < Px; i++)
            {
                int wx = cx + (int)((i - half) * MetresPerPx);
                int wz = cz + (int)((j - half) * MetresPerPx);
                int h = World.SurfaceHeight(wx, wz);
                bool editedTop = World.TryEditTop(wx, wz, out int edited);
                if (editedTop)
                {
                    h = edited;
                }

                Color c;
                if (World.IsSky && !editedTop && !World.IslandAt(wx, wz))
                {
                    c = new Color(0.05f, 0.07f, 0.12f);   // open sky below the islands
                }
                else if (h <= sea)
                {
                    c = _water;
                }
                else if (h <= sea + 1)
                {
                    c = _sand;
                }
                else
                {
                    c = World.BiomeAt(wx, wz, h) switch
                    {
                        Biome.Desert => _sand,
                        Biome.Snow => _snow,
                        _ => _grass,
                    };
                    // Higher ground reads brighter; steep ridges go stony.
                    float lift = Mathf.Clamp((h - sea) / 18.0f, 0.0f, 1.0f);
                    c = c.Lerp(_stone, lift * 0.55f).Lightened(lift * 0.15f);
                }

                _img.SetPixel(i, j, c);
            }
        }

        _tex.Update(_img);
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        Vector2 origin = (size - new Vector2(Px, Px)) / 2.0f;
        DrawTextureRect(_tex, new Rect2(origin, new Vector2(Px, Px)), false);

        // Compass: N at the top of the frame.
        Font font = GetThemeDefaultFont();
        DrawString(font, origin + new Vector2(2, 12), "N", HorizontalAlignment.Left, -1, 11, Colors.White);
        DrawLine(origin + new Vector2(Px / 2.0f, 4), origin + new Vector2(Px / 2.0f, 10),
                new Color(1, 1, 1, 0.4f), 1.0f);

        if (World == null || PlayerRef == null)
        {
            return;
        }

        Vector2 centre = size / 2.0f;
        Vector3 pp = PlayerRef.GlobalPosition;

        // Waypoints: campfires (warm dots), respawn anchor (ringed), spawn (star-ish).
        foreach (Vector3I site in World.NearbyCampfireSites(pp, Px * MetresPerPx * 0.5f))
        {
            Vector2 d = new Vector2(site.X + 0.5f - pp.X, site.Z + 0.5f - pp.Z) / MetresPerPx;
            DrawCircle(centre + d, 2.4f, new Color(1.0f, 0.62f, 0.22f));
        }

        if (PlayerRef.RespawnPoint is { } respawn)
        {
            Vector2 d = new Vector2(respawn.X - pp.X, respawn.Z - pp.Z) / MetresPerPx;
            Vector2 p = centre + d;
            DrawCircle(p, 3.2f, new Color(0.95f, 0.35f, 0.25f));
            DrawArc(p, 5.0f, 0.0f, Mathf.Tau, 24, new Color(1, 1, 1, 0.8f), 1.2f);
        }

        // Heading needle: a triangle pointing where the player looks.
        float yaw = PlayerRef.Rotation.Y;
        Vector2 dir = new(-Mathf.Sin(yaw), -Mathf.Cos(yaw));   // north (-Z) is up
        Vector2 perp = new(-dir.Y, dir.X);
        Vector2 tip = centre + dir * 8.0f;
        DrawColoredPolygon(
                [tip, centre - dir * 4.0f + perp * 4.0f, centre - dir * 4.0f - perp * 4.0f],
                Colors.White);
    }
}
