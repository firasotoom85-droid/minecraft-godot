using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Campfires: one deterministic site per 64x64 area on dry plains/birch/desert
/// (blocks placed by world generation). While the centre log block exists, the
/// site gets fire and smoke particles. Its warm illumination is baked through
/// VoxelWorld's shared emitter map, matching torch colour without a light node
/// per site. Sites are recomputed from the world seed as the player moves.
/// </summary>
public partial class Campfires : Node
{
    public const int Cell = 64;
    private const float KeepRadius = 120.0f;
    private const double CheckInterval = 0.8;

    public VoxelWorld? World;
    public Player? PlayerRef;

    private sealed class Site
    {
        public Node3D Root = null!;
    }

    private readonly Dictionary<Vector2I, Site> _fires = new();  // grid cell -> site
    private double _timer;
    private ImageTexture _dot = null!;

    public void Setup(VoxelWorld world, Player player)
    {
        World = world;
        PlayerRef = player;
        _dot = ImageTexture.CreateFromImage(DotImage());
    }

    public override void _Process(double delta)
    {
        if (PlayerRef == null || World == null)
        {
            return;
        }

        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = CheckInterval;
        Refresh();
    }

    private void Refresh()
    {
        Vector3 p = PlayerRef!.GlobalPosition;
        int ccx = Mathf.FloorToInt(p.X / Cell);
        int ccz = Mathf.FloorToInt(p.Z / Cell);
        HashSet<Vector2I> keep = [];
        for (int dz = -2; dz <= 2; dz++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                Vector2I cell = new(ccx + dx, ccz + dz);
                if (World!.GetCampfireSite(cell.X, cell.Y) is not { } site)
                {
                    continue;
                }

                float d = new Vector2(site.X + 0.5f, site.Z + 0.5f).DistanceTo(new Vector2(p.X, p.Z));
                if (d > KeepRadius)
                {
                    continue;
                }

                keep.Add(cell);
                if (!_fires.ContainsKey(cell))
                {
                    Build(cell, site);
                }

                _fires[cell].Root.Visible = World.GetBlock(site.X, site.Y, site.Z) == BlockId.Log;
            }
        }

        // Snapshot the keys: the dictionary is modified while walking them.
        foreach (Vector2I cell in new List<Vector2I>(_fires.Keys))
        {
            if (!keep.Contains(cell))
            {
                _fires[cell].Root.QueueFree();
                _fires.Remove(cell);
            }
        }
    }

    private void Build(Vector2I cell, Vector3I site)
    {
        Node3D node = new()
        {
            Name = $"campfire_{cell.X}_{cell.Y}",
            Position = new Vector3(site.X + 0.5f, site.Y + 1.05f, site.Z + 0.5f),
        };
        AddChild(node);

        node.AddChild(FireParticles());

        GpuParticles3D smoke = SmokeParticles();
        smoke.Position = new Vector3(0, 0.35f, 0);
        node.AddChild(smoke);

        _fires[cell] = new Site
        {
            Root = node,
        };
    }

    private QuadMesh MakeQuad(float size, Color col, bool additive, bool glow)
    {
        StandardMaterial3D mat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = _dot,
            AlbedoColor = col,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableReceiveShadows = true,
        };
        if (additive)
        {
            mat.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        }

        if (glow)
        {
            mat.EmissionEnabled = true;
            mat.Emission = new Color(col.R, col.G, col.B);
            mat.EmissionEnergyMultiplier = 2.0f;
        }

        return new QuadMesh
        {
            Size = new Vector2(size, size),
            Material = mat,
        };
    }

    private GpuParticles3D FireParticles()
    {
        ParticleProcessMaterial pm = new()
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.12f, 0.04f, 0.12f),
            Direction = new Vector3(0, 1, 0),
            Spread = 10.0f,
            InitialVelocityMin = 0.9f,
            InitialVelocityMax = 1.7f,
            Gravity = new Vector3(0, 0.5f, 0),
        };
        Gradient g = new();
        g.SetColor(0, new Color(1.0f, 0.82f, 0.35f, 0.95f));
        g.SetColor(1, new Color(1.0f, 0.28f, 0.06f, 0.0f));
        pm.ColorRamp = new GradientTexture1D
        {
            Gradient = g,
        };
        return new GpuParticles3D
        {
            ProcessMaterial = pm,
            DrawPass1 = MakeQuad(0.3f, new Color(1, 1, 1, 1), true, false),
            Amount = 24,
            Lifetime = 0.6,
            Preprocess = 0.6f,
            LocalCoords = false,
            Emitting = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-1, -0.5f, -1), new Vector3(2, 3.5f, 2)),
        };
    }

    private GpuParticles3D SmokeParticles()
    {
        ParticleProcessMaterial pm = new()
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.1f, 0.05f, 0.1f),
            Direction = new Vector3(0.25f, 1.0f, 0.12f),
            Spread = 9.0f,
            InitialVelocityMin = 0.55f,
            InitialVelocityMax = 0.95f,
            Gravity = new Vector3(0.05f, 0.22f, 0.02f),
        };
        Gradient g = new();
        g.SetColor(0, new Color(0.35f, 0.35f, 0.37f, 0.0f));
        g.SetColor(1, new Color(0.30f, 0.30f, 0.32f, 0.0f));
        g.AddPoint(0.35f, new Color(0.44f, 0.44f, 0.47f, 0.22f));
        pm.ColorRamp = new GradientTexture1D
        {
            Gradient = g,
        };
        Curve curve = new();
        curve.AddPoint(new Vector2(0.0f, 0.5f));
        curve.AddPoint(new Vector2(1.0f, 2.0f));
        pm.ScaleCurve = new CurveTexture
        {
            Curve = curve,
        };
        return new GpuParticles3D
        {
            ProcessMaterial = pm,
            DrawPass1 = MakeQuad(0.42f, new Color(1, 1, 1, 1), false, false),
            Amount = 30,
            Lifetime = 4.2,
            Preprocess = 2.0f,
            LocalCoords = false,
            Emitting = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-3, -1, -3), new Vector3(6, 8, 6)),
        };
    }

    private static Image DotImage()
    {
        Image img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        Vector2 c = new(7.5f, 7.5f);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float d = new Vector2(x, y).DistanceTo(c) / 8.0f;
                float a = Mathf.Pow(Mathf.Clamp(1.0f - d, 0.0f, 1.0f), 1.6f);
                img.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }

        return img;
    }
}
