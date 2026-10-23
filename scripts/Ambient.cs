using Godot;

namespace Minecraft;

/// <summary>
/// Air life: sunlit dust motes, night fireflies and underwater bubbles. Three
/// camera-following particle rigs whose visibility follows time of day,
/// weather and submersion. Purely cosmetic, all procedural textures.
/// </summary>
public partial class Ambient : Node
{
    public Player? PlayerRef;
    public VoxelWorld? World;
    public Weather? WeatherRef;

    /// <summary>
    /// One particle rig's configuration. A record makes the three rigs below
    /// readable named-argument call sites instead of eleven positional
    /// arguments each — impossible to mis-order, self-documenting.
    /// </summary>
    private readonly record struct RigSpec(
        string Name,
        int Amount,
        float Lifetime,
        Vector3 HalfExtents,
        float VelMin,
        float VelMax,
        float Size,
        Color Color,
        bool Glow,
        Vector3 Gravity,
        float GlowEnergy);

    private GpuParticles3D _motes = null!;
    private GpuParticles3D _fireflies = null!;
    private GpuParticles3D _bubbles = null!;
    private GpuParticles3D _butterflies = null!;
    private GpuParticles3D _birds = null!;
    private ImageTexture _dot = null!;

    public void Setup(Player player, VoxelWorld world, Weather weather)
    {
        PlayerRef = player;
        World = world;
        WeatherRef = weather;
        _dot = ImageTexture.CreateFromImage(DotImage());
        _motes = MakeRig(new RigSpec(
            Name: "motes", Amount: 220, Lifetime: 7.0f, HalfExtents: new Vector3(11, 5, 11),
            VelMin: 0.08f, VelMax: 0.35f, Size: 0.028f, Color: new Color(1.0f, 0.98f, 0.9f, 0.45f),
            Glow: false, Gravity: new Vector3(0, 0.02f, 0), GlowEnergy: 0.0f));
        _fireflies = MakeRig(new RigSpec(
            Name: "fireflies", Amount: 130, Lifetime: 11.0f, HalfExtents: new Vector3(13, 3.5f, 13),
            VelMin: 0.05f, VelMax: 0.28f, Size: 0.09f, Color: new Color(0.75f, 1.0f, 0.4f, 0.95f),
            Glow: true, Gravity: new Vector3(0, 0.05f, 0), GlowEnergy: 3.0f));
        _bubbles = MakeRig(new RigSpec(
            Name: "bubbles", Amount: 70, Lifetime: 2.2f, HalfExtents: new Vector3(2.5f, 1.5f, 2.5f),
            VelMin: 0.7f, VelMax: 1.4f, Size: 0.03f, Color: new Color(0.85f, 0.93f, 1.0f, 0.4f),
            Glow: false, Gravity: new Vector3(0, 0.7f, 0), GlowEnergy: 0.0f));
        _butterflies = MakeRig(new RigSpec(
            Name: "butterflies", Amount: 22, Lifetime: 9.0f, HalfExtents: new Vector3(8, 2, 8),
            VelMin: 0.15f, VelMax: 0.55f, Size: 0.075f, Color: new Color(1.0f, 0.72f, 0.28f, 0.9f),
            Glow: false, Gravity: new Vector3(0, 0.03f, 0), GlowEnergy: 0.0f));
        _birds = MakeRig(new RigSpec(
            Name: "birds", Amount: 12, Lifetime: 12.0f, HalfExtents: new Vector3(24, 8, 24),
            VelMin: 0.6f, VelMax: 1.5f, Size: 0.11f, Color: new Color(0.30f, 0.34f, 0.38f, 0.8f),
            Glow: false, Gravity: new Vector3(0, 0.01f, 0), GlowEnergy: 0.0f));
    }

    public override void _Process(double delta)
    {
        if (PlayerRef == null || World == null)
        {
            return;
        }

        Vector3 pos = PlayerRef.GlobalPosition + new Vector3(0, 1.0f, 0);
        Vector3 snap = new(Mathf.Snapped(pos.X, 2.0f), Mathf.Snapped(pos.Y, 2.0f), Mathf.Snapped(pos.Z, 2.0f));
        _motes.GlobalPosition = snap;
        _fireflies.GlobalPosition = snap;
        _bubbles.GlobalPosition = snap;
        _butterflies.GlobalPosition = snap;
        _birds.GlobalPosition = snap;
        double t = World.TimeOfDay;
        bool isNight = t > 0.72 || t < 0.26;
        bool raining = WeatherRef != null && (int)WeatherRef.Current >= 2;
        bool uw = PlayerRef.Underwater;
        Vector3 pp = PlayerRef.GlobalPosition;
        bool underground = pp.Y < World.SurfaceHeight(Mathf.FloorToInt(pp.X), Mathf.FloorToInt(pp.Z)) - 3.0f;
        _motes.Emitting = !uw && !isNight && !underground;
        _fireflies.Emitting = isNight && !raining && !uw;
        _bubbles.Emitting = uw;
        _butterflies.Emitting = !uw && !isNight && !raining && !underground;
        _birds.Emitting = !uw && !isNight && !raining;
    }

    private GpuParticles3D MakeRig(RigSpec spec)
    {
        StandardMaterial3D mat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = _dot,
            AlbedoColor = spec.Color,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableReceiveShadows = true,
        };
        if (spec.Glow)
        {
            mat.EmissionEnabled = true;
            mat.Emission = new Color(spec.Color.R, spec.Color.G, spec.Color.B);
            mat.EmissionEnergyMultiplier = spec.GlowEnergy;
        }

        QuadMesh quad = new()
        {
            Size = new Vector2(spec.Size, spec.Size),
            Material = mat,
        };
        GpuParticles3D p = new()
        {
            Name = spec.Name,
        };
        ParticleProcessMaterial pm = new()
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = spec.HalfExtents,
            Direction = new Vector3(0, 1, 0),
            Spread = 180.0f,
            InitialVelocityMin = spec.VelMin,
            InitialVelocityMax = spec.VelMax,
            Gravity = spec.Gravity,
        };
        p.ProcessMaterial = pm;
        p.DrawPass1 = quad;
        p.Amount = spec.Amount;
        p.Lifetime = spec.Lifetime;
        p.Preprocess = 0.0f;
        p.LocalCoords = false;
        p.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        p.VisibilityAabb = new Aabb(-spec.HalfExtents - new Vector3(2, 2, 2),
                spec.HalfExtents * 2.0f + new Vector3(4, 4, 4));
        AddChild(p);
        return p;
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
                float a = Mathf.Pow(Mathf.Clamp(1.0f - d, 0.0f, 1.0f), 2.0f);
                img.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }

        return img;
    }
}
