using Godot;

namespace Minecraft;

/// <summary>
/// Block break / place / mining feedback: reusable one-shot GPU particle
/// systems, tinted with the actual block tile from the atlas.
/// </summary>
public partial class Fx : Node
{
    private static readonly AtlasTexture?[] BlockTextures = new AtlasTexture?[BlockDb.BlockCount];
    private static readonly GradientTexture1D?[] BlockColorRamps = new GradientTexture1D?[BlockDb.BlockCount];

    public GpuParticles3D BreakParticles = null!;
    public GpuParticles3D PlaceParticles = null!;
    public GpuParticles3D MiningParticles = null!;
    public GpuParticles3D PuffParticles = null!;

    public void Setup(Player player)
    {
        BreakParticles = MakeSystem(24, 3.4f, 0.16f);
        PlaceParticles = MakeSystem(10, 1.6f, 0.22f);
        MiningParticles = MakeSystem(6, 1.2f, 0.055f);
        PuffParticles = MakeSystem(18, 2.4f, 0.11f);
        player.BlockBroken += OnBroken;
        player.BlockPlaced += OnPlaced;
        player.MiningTick += OnMiningTick;
        player.Splashed += pos => Puff(pos + new Vector3(0, -0.2f, 0), new Color(0.75f, 0.88f, 1.0f, 0.9f));
        player.SwimStroke += pos => Bubbles(pos - new Vector3(0, 0.25f, 0));
        player.LavaEntered += pos => Puff(pos, new Color(1.0f, 0.42f, 0.12f, 0.95f));
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
        ParticleProcessMaterial particles = (ParticleProcessMaterial)sys.ProcessMaterial;
        switch (blockId)
        {
            case BlockId.Glass:
                particles.Direction = new Vector3(0.0f, 0.6f, 0.0f);
                particles.Spread = 82.0f;
                particles.InitialVelocityMin = 2.0f;
                particles.InitialVelocityMax = 3.8f;
                particles.Gravity = new Vector3(0.0f, -13.0f, 0.0f);
                particles.ScaleMin = 0.045f;
                particles.ScaleMax = 0.085f;
                break;
            case BlockId.Leaves:
            case BlockId.BirchLeaves:
            case BlockId.TallGrass:
            case BlockId.FlowerRed:
            case BlockId.FlowerYellow:
            case BlockId.Cactus:
            case BlockId.Wheat0:
            case BlockId.Wheat1:
            case BlockId.Wheat2:
                particles.Direction = new Vector3(0.0f, 1.0f, 0.0f);
                particles.Spread = 75.0f;
                particles.InitialVelocityMin = 0.45f;
                particles.InitialVelocityMax = 1.5f;
                particles.Gravity = new Vector3(0.0f, -2.2f, 0.0f);
                particles.ScaleMin = 0.055f;
                particles.ScaleMax = 0.105f;
                break;
            case BlockId.Sand:
            case BlockId.Gravel:
            case BlockId.Snow:
            case BlockId.SnowGrass:
                particles.Direction = new Vector3(0.0f, 1.0f, 0.0f);
                particles.Spread = 68.0f;
                particles.InitialVelocityMin = 0.7f;
                particles.InitialVelocityMax = 2.0f;
                particles.Gravity = new Vector3(0.0f, -5.0f, 0.0f);
                particles.ScaleMin = 0.04f;
                particles.ScaleMax = 0.09f;
                break;
            default:
                particles.Direction = new Vector3(0.0f, 1.0f, 0.0f);
                particles.Spread = 55.0f;
                particles.InitialVelocityMin = 1.2f;
                particles.InitialVelocityMax = 2.8f;
                particles.Gravity = new Vector3(0.0f, -9.0f, 0.0f);
                particles.ScaleMin = 0.065f;
                particles.ScaleMax = 0.13f;
                break;
        }

        mat.AlbedoTexture = TextureFor(blockId);
        particles.ColorRamp = ColorRampFor(blockId);
        sys.GlobalPosition = pos;
        sys.Restart();
        sys.Emitting = true;
    }

    private static AtlasTexture TextureFor(BlockId blockId)
    {
        int index = (int)blockId;
        if (BlockTextures[index] is { } cached)
        {
            return cached;
        }

        int tile = BlockDb.IconTile(blockId);
        AtlasTexture texture = new()
        {
            Atlas = BlockDb.AtlasTexture(),
            Region = new Rect2(
                (tile % BlockDb.AtlasCols) * BlockDb.TilePx,
                (tile / BlockDb.AtlasCols) * BlockDb.TilePx,
                BlockDb.TilePx, BlockDb.TilePx),
        };
        BlockTextures[index] = texture;
        return texture;
    }

    private static GradientTexture1D ColorRampFor(BlockId blockId)
    {
        int index = (int)blockId;
        if (BlockColorRamps[index] is { } cached)
        {
            return cached;
        }

        Color color = BlockDb.AverageTileColor(blockId);
        Gradient gradient = new();
        gradient.SetColor(0, color.WithAlpha(1.0f));
        gradient.SetColor(1, color.WithAlpha(0.0f));
        GradientTexture1D ramp = new()
        {
            Gradient = gradient,
        };
        BlockColorRamps[index] = ramp;
        return ramp;
    }

    private void OnBroken(Vector3 pos, int blockId)
    {
        Emit(BreakParticles, pos, (BlockId)blockId);
    }

    private void OnMiningTick(Vector3 pos, int blockId)
    {
        Emit(MiningParticles, pos, (BlockId)blockId);
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
    public void Puff(Vector3 pos, Color color) => EmitPuff(pos, color, new Vector3(0, -9.0f, 0));

    private void Bubbles(Vector3 pos) => EmitPuff(pos, new Color(0.55f, 0.82f, 1.0f, 0.8f), new Vector3(0, 2.8f, 0));

    private void EmitPuff(Vector3 pos, Color color, Vector3 gravity)
    {
        QuadMesh quad = (QuadMesh)PuffParticles.DrawPass1;
        StandardMaterial3D mat = (StandardMaterial3D)quad.Material;
        mat.AlbedoTexture = null;
        Gradient ramp = new();
        ramp.SetColor(0, color.WithAlpha(1.0f));
        ramp.SetColor(1, color.WithAlpha(0.0f));
        ParticleProcessMaterial process = (ParticleProcessMaterial)PuffParticles.ProcessMaterial;
        process.ColorRamp = new GradientTexture1D { Gradient = ramp };
        process.Gravity = gravity;
        process.InitialVelocityMin = gravity.Y > 0.0f ? 0.25f : 1.0f;
        process.InitialVelocityMax = gravity.Y > 0.0f ? 0.9f : 3.6f;
        PuffParticles.GlobalPosition = pos;
        PuffParticles.Restart();
        PuffParticles.Emitting = true;
    }
}
