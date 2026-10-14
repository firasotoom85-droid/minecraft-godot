using Godot;

namespace Minecraft;

/// <summary>
/// Block break / place particles: two reusable one-shot GPU particle
/// systems, tinted with the actual block tile from the atlas.
/// </summary>
public partial class Fx : Node
{
    public GpuParticles3D BreakParticles = null!;
    public GpuParticles3D PlaceParticles = null!;
    public GpuParticles3D PuffParticles = null!;

    public void Setup(Player player)
    {
        BreakParticles = MakeSystem(24, 3.4f, 0.16f);
        PlaceParticles = MakeSystem(10, 1.6f, 0.22f);
        PuffParticles = MakeSystem(18, 2.4f, 0.11f);
        player.BlockBroken += OnBroken;
        player.BlockPlaced += OnPlaced;
        player.Splashed += pos => Puff(pos + new Vector3(0, -0.2f, 0), new Color(0.75f, 0.88f, 1.0f, 0.9f));
        player.Landed += (pos, impact) =>
        {
            float k = Mathf.Clamp(impact * 0.06f, 0.0f, 0.5f);
            Puff(pos, new Color(0.62f, 0.60f, 0.56f, 0.35f + k));
        };
    }

    private GpuParticles3D MakeSystem(int amount, float speed, float scale)
    {
        ParticleProcessMaterial pm = new()
        {
            Direction = new Vector3(0, 1, 0),
            Spread = 55.0f,
            InitialVelocityMin = speed * 0.6f,
            InitialVelocityMax = speed * 1.5f,
            Gravity = new Vector3(0, -9.0f, 0),
            ScaleMin = scale * 0.7f,
            ScaleMax = scale * 1.3f,
        };
        QuadMesh quad = new()
        {
            Size = new Vector2(0.16f, 0.16f),
            Material = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
        };
        GpuParticles3D gpu = new()
        {
            Amount = amount,
            Lifetime = 0.9,
            OneShot = true,
            Explosiveness = 0.95f,
            LocalCoords = false,
            ProcessMaterial = pm,
            DrawPass1 = quad,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
        };
        AddChild(gpu);
        return gpu;
    }

    private static void Emit(GpuParticles3D sys, Vector3 pos, BlockId blockId)
    {
        QuadMesh quad = (QuadMesh)sys.DrawPass1;
        StandardMaterial3D mat = (StandardMaterial3D)quad.Material;
        int tile = BlockDb.IconTile(blockId);
        mat.AlbedoTexture = new AtlasTexture
        {
            Atlas = BlockDb.AtlasTexture(),
            Region = new Rect2(
                (tile % BlockDb.AtlasCols) * BlockDb.TilePx,
                (tile / BlockDb.AtlasCols) * BlockDb.TilePx,
                BlockDb.TilePx, BlockDb.TilePx),
        };
        Color col = BlockDb.AverageTileColor(blockId);
        Gradient ramp = new();
        ramp.SetColor(0, col.WithAlpha(1.0f));
        ramp.SetColor(1, col.WithAlpha(0.0f));
        ((ParticleProcessMaterial)sys.ProcessMaterial).ColorRamp = new GradientTexture1D
        {
            Gradient = ramp,
        };
        sys.GlobalPosition = pos;
        sys.Restart();
        sys.Emitting = true;
    }

    private void OnBroken(Vector3 pos, int blockId)
    {
        Emit(BreakParticles, pos, (BlockId)blockId);
    }

    private void OnPlaced(Vector3 pos, int blockId)
    {
        Emit(PlaceParticles, pos + new Vector3(0, -0.35f, 0), (BlockId)blockId);
    }

    /// <summary>Public burst (mobs, other systems): one-shot particle puff tinted by block.</summary>
    public void Burst(Vector3 pos, BlockId blockId)
    {
        Emit(BreakParticles, pos, blockId);
    }

    /// <summary>Plain coloured one-shot puff (mob hit sparks, hearts, smoke…).</summary>
    public void Puff(Vector3 pos, Color color)
    {
        QuadMesh quad = (QuadMesh)PuffParticles.DrawPass1;
        StandardMaterial3D mat = (StandardMaterial3D)quad.Material;
        mat.AlbedoTexture = null;
        Gradient ramp = new();
        ramp.SetColor(0, color.WithAlpha(1.0f));
        ramp.SetColor(1, color.WithAlpha(0.0f));
        ((ParticleProcessMaterial)PuffParticles.ProcessMaterial).ColorRamp = new GradientTexture1D
        {
            Gradient = ramp,
        };
        PuffParticles.GlobalPosition = pos;
        PuffParticles.Restart();
        PuffParticles.Emitting = true;
    }
}
