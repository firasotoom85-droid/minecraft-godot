using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class LightningStrikeRules
{
    public static Vector3 NearStrikePosition(Vector3 playerPosition, int surfaceHeight) =>
            new(Mathf.FloorToInt(playerPosition.X) + 0.5f, surfaceHeight + 1.0f,
                    Mathf.FloorToInt(playerPosition.Z) + 0.5f);

    public static bool CanDamagePlayer(Vector3 hit, Vector3 playerPosition,
            int surfaceHeight, int seaLevel) =>
            surfaceHeight > seaLevel && hit.DistanceTo(playerPosition) < 2.5f;
}

/// <summary>
/// Dynamic weather: clear -> cloudy -> rain/snow -> storm. Owns the state
/// machine, precipitation particles, ground wetness, lightning and the rain /
/// thunder audio hooks. The sky / fog / light side of weather is applied by
/// DayNight through its <c>Cloudiness</c> and <c>SkyFlash</c> inputs.
/// </summary>
public partial class Weather : Node
{
    public enum State
    {
        Clear,
        Cloudy,
        Rain,
        Storm,
    }

    public static readonly string[] StateNames = ["clear", "cloudy", "rain", "storm"];

    private const double LightningFlashDecayPerSecond = 4.5;

    /// <summary>Combines overlapping lightning pulses without additive over-brightening.</summary>
    internal static double CombineFlashIntensity(double current, double incoming)
    {
        double safeCurrent = double.IsFinite(current)
                ? System.Math.Clamp(current, 0.0, 1.0) : 0.0;
        double safeIncoming = double.IsFinite(incoming)
                ? System.Math.Clamp(incoming, 0.0, 1.0) : 0.0;
        return System.Math.Max(safeCurrent, safeIncoming);
    }

    /// <summary>Linearly decays a flash using the existing 4.5-per-second envelope.</summary>
    internal static double DecayFlashIntensity(double intensity, double delta)
    {
        double safeIntensity = double.IsFinite(intensity)
                ? System.Math.Clamp(intensity, 0.0, 1.0) : 0.0;
        double elapsed = double.IsFinite(delta) ? System.Math.Max(delta, 0.0) : 0.0;
        return System.Math.Max(safeIntensity - elapsed * LightningFlashDecayPerSecond, 0.0);
    }

    private static readonly IReadOnlyDictionary<State, float> TargetCloudiness = new Dictionary<State, float>
    {
        [State.Clear] = 0.05f,
        [State.Cloudy] = 0.55f,
        [State.Rain] = 0.78f,
        [State.Storm] = 0.95f,
    };

    private static readonly IReadOnlyDictionary<State, Vector2> Durations = new Dictionary<State, Vector2>
    {
        [State.Clear] = new(90.0f, 240.0f),
        [State.Cloudy] = new(60.0f, 150.0f),
        [State.Rain] = new(50.0f, 120.0f),
        [State.Storm] = new(30.0f, 80.0f),
    };

    public VoxelWorld? World;
    public Player? PlayerRef;
    public DayNight? DayNightRef;
    public Sound? SoundRef;

    public State Current = State.Clear;
    public double Cloudiness = 0.05;

    private GpuParticles3D _rain = null!;
    private GpuParticles3D _snow = null!;
    private GpuParticles3D _groundSplash = null!;
    private MeshInstance3D _bolt = null!;
    private double _boltTime;
    private double _timer = 20.0;
    private double _wet;
    private double _flash;
    private double _flashAgain = -1.0;
    private double _lightningTimer = 6.0;
    private bool _snowing;
    private int _lastSplashSampleX = int.MinValue;
    private int _lastSplashSampleZ = int.MinValue;
    private bool _hasBiomeSample;
    private int _lastBiomeX;
    private int _lastBiomeZ;
    private readonly RandomNumberGenerator _rng = new();

    public void Setup(VoxelWorld world, Player player, DayNight daynight, Sound sound)
    {
        World = world;
        PlayerRef = player;
        DayNightRef = daynight;
        SoundRef = sound;
        _hasBiomeSample = false;
        _rng.Randomize();
        BuildParticles();
        BuildBolt();
        PickNextState();
    }

    public override void _Process(double delta)
    {
        if (PlayerRef == null || DayNightRef == null)
        {
            return;
        }

        _timer -= delta;
        if (_timer <= 0.0)
        {
            PickNextState();
        }

        Cloudiness = Mathf.Lerp((float)Cloudiness, TargetCloudiness[Current], (float)System.Math.Clamp(delta * 0.12, 0.0, 1.0));
        UpdatePrecipitationBiome();

        bool precip = Current is State.Rain or State.Storm;
        Vector3 pp = PlayerRef.GlobalPosition;
        Vector3 anchor = new(Mathf.Snapped(pp.X, 8.0f), pp.Y + 24.0f, Mathf.Snapped(pp.Z, 8.0f));
        _rain.GlobalPosition = anchor;
        _snow.GlobalPosition = anchor;
        _rain.Emitting = precip && !_snowing;
        _snow.Emitting = precip && _snowing;

        // Ground splashes are visible only during rain. Their anchor moves in
        // four-metre cells, so terrain height is sampled only when that cell
        // changes rather than on every frame (and never during clear weather).
        bool raining = precip && !_snowing && World != null;
        _groundSplash.Emitting = raining;
        if (raining)
        {
            float splashX = Mathf.Snapped(pp.X, 4.0f);
            float splashZ = Mathf.Snapped(pp.Z, 4.0f);
            int sampleX = Mathf.FloorToInt(splashX);
            int sampleZ = Mathf.FloorToInt(splashZ);
            if (sampleX != _lastSplashSampleX || sampleZ != _lastSplashSampleZ)
            {
                _lastSplashSampleX = sampleX;
                _lastSplashSampleZ = sampleZ;
                int groundHeight = World!.SurfaceHeight(sampleX, sampleZ);
                _groundSplash.GlobalPosition = new Vector3(
                        splashX, groundHeight + 0.2f, splashZ);
            }
        }
        else
        {
            // Force a fresh height sample when rain resumes, even if the
            // player and the snapped anchor have not moved in the meantime.
            _lastSplashSampleX = int.MinValue;
            _lastSplashSampleZ = int.MinValue;
        }

        // Ground darkens while rain soaks in (seconds) and dries out slowly.
        double wetTarget = precip && !_snowing ? 1.0 : 0.0;
        _wet = Mathf.MoveToward(_wet, wetTarget, delta * (wetTarget > 0.0 ? 0.25 : 0.02));
        float mul = (float)(1.0 - _wet * 0.26);
        Color wetTint = new(mul, mul, Mathf.Min(mul * 1.04f, 1.0f));
        Material opaqueMat = ChunkMaterials.Opaque;
        if (opaqueMat is ShaderMaterial wire)
        {
            wire.SetShaderParameter("tint", wetTint);   // greedy (texture-array) material
        }
        else if (opaqueMat is StandardMaterial3D std)
        {
            std.AlbedoColor = wetTint;
        }
        if (ChunkMaterials.Water is ShaderMaterial water)
        {
            water.SetShaderParameter("dim", 1.0 - Cloudiness * 0.3);
        }

        if (Current == State.Storm)
        {
            _lightningTimer -= delta;
            if (_lightningTimer <= 0.0)
            {
                _lightningTimer = _rng.RandfRange(5.0f, 18.0f);
                _flash = CombineFlashIntensity(_flash, 1.0);
                if (_rng.Randf() < 0.5f)
                {
                    _flashAgain = 0.12;
                }

                Strike();
                SoundRef?.Thunder(_rng.RandfRange(0.3f, 1.8f));
            }
        }

        if (_boltTime > 0.0)
        {
            _boltTime -= delta;
            if (_boltTime <= 0.0)
            {
                _bolt.Visible = false;
            }
        }

        if (_flashAgain > 0.0)
        {
            _flashAgain -= delta;
            if (_flashAgain <= 0.0)
            {
                _flash = CombineFlashIntensity(_flash, 0.75);
                _flashAgain = -1.0;
            }
        }

        _flash = DecayFlashIntensity(_flash, delta);
        DayNightRef.Cloudiness = Cloudiness;
        DayNightRef.SkyFlash = _flash;

        SoundRef?.SetRain((float)((precip ? Cloudiness : 0.0) * 0.9));
    }

    /// <summary>
    /// Markov-style weather transitions, expressed as one relational-pattern
    /// switch instead of a nested if/match chain — the whole state table reads
    /// as a single expression.
    /// </summary>
    private void PickNextState()
    {
        float r = _rng.Randf();
        Current = Current switch
        {
            State.Clear => r < 0.7f ? State.Cloudy : State.Clear,
            State.Cloudy => r < 0.6f ? State.Rain : State.Clear,
            State.Rain => r switch
            {
                < 0.35f => State.Storm,
                < 0.65f => State.Cloudy,
                _ => State.Clear,
            },
            State.Storm => r < 0.7f ? State.Rain : State.Cloudy,
            _ => Current,
        };
        Vector2 dur = Durations[Current];
        _timer = _rng.RandfRange(dur.X, dur.Y);
        _hasBiomeSample = false;
        UpdatePrecipitationBiome();
    }

    internal void ConfigureVisualCapture(State state)
    {
        Current = state;
        Cloudiness = TargetCloudiness[state];
        _timer = double.MaxValue;
        _lightningTimer = double.MaxValue;
        _flash = 0.0;
        _flashAgain = -1.0;
        if (DayNightRef != null)
        {
            DayNightRef.Cloudiness = Cloudiness;
            DayNightRef.SkyFlash = 0.0;
        }

        _hasBiomeSample = false;
        UpdatePrecipitationBiome();
    }

    internal static float TargetCloudinessFor(State state) => TargetCloudiness[state];

    internal static bool UsesSnowPrecipitation(Biome biome) => biome == Biome.Snow;

    internal static bool NeedsBiomeSample(
            bool hasSample, int sampledX, int sampledZ, int playerX, int playerZ) =>
            !hasSample || sampledX != playerX || sampledZ != playerZ;

    private void UpdatePrecipitationBiome()
    {
        if (World == null || PlayerRef == null)
        {
            return;
        }

        Vector3 position = PlayerRef.GlobalPosition;
        int x = Mathf.FloorToInt(position.X);
        int z = Mathf.FloorToInt(position.Z);
        if (!NeedsBiomeSample(_hasBiomeSample, _lastBiomeX, _lastBiomeZ, x, z))
        {
            return;
        }

        _hasBiomeSample = true;
        _lastBiomeX = x;
        _lastBiomeZ = z;
        _snowing = UsesSnowPrecipitation(World.BiomeAt(x, z));
    }

    // -----------------------------------------------------------------------
    // Lightning strike: brief emissive beam + (rarely) real damage.
    // -----------------------------------------------------------------------

    private void BuildBolt()
    {
        StandardMaterial3D mat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 1.0f, 0.92f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = new Color(1.0f, 1.0f, 0.88f),
            EmissionEnergyMultiplier = 3.0f,
        };
        _bolt = new MeshInstance3D
        {
            Name = "Bolt",
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.16f, 80.0f, 0.16f),
            },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_bolt);
    }

    private void Strike()
    {
        if (World == null || PlayerRef == null)
        {
            return;
        }

        Vector3 playerPosition = PlayerRef.GlobalPosition;
        bool nearStrike = _rng.Randf() < 0.12f;
        int x;
        int z;
        if (nearStrike)
        {
            x = Mathf.FloorToInt(playerPosition.X);
            z = Mathf.FloorToInt(playerPosition.Z);
        }
        else
        {
            float ang = _rng.Randf() * Mathf.Tau;
            float dist = _rng.RandfRange(7.0f, 30.0f);
            x = Mathf.FloorToInt(playerPosition.X + Mathf.Cos(ang) * dist);
            z = Mathf.FloorToInt(playerPosition.Z + Mathf.Sin(ang) * dist);
        }

        int h = World.SurfaceHeight(x, z);
        _bolt.GlobalPosition = new Vector3(x + 0.5f, h + 40.0f, z + 0.5f);
        _bolt.Visible = true;
        _boltTime = 0.15;

        // A small fraction of strikes land in the player's column; ordinary
        // strikes remain distributed 7–30 blocks away.
        Vector3 hit = new(x + 0.5f, h + 1.0f, z + 0.5f);
        if (LightningStrikeRules.CanDamagePlayer(hit, playerPosition, h, World.SeaLevel))
        {
            PlayerRef.ApplyDamage(new DamageInfo(4.0f, DamageKind.Lightning, hit));
        }
    }

    // -----------------------------------------------------------------------
    // Particles
    // -----------------------------------------------------------------------

    private void BuildParticles()
    {
        StandardMaterial3D rainMat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = ImageTexture.CreateFromImage(StreakImage()),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            DisableReceiveShadows = true,
        };
        QuadMesh rainQuad = new()
        {
            Size = new Vector2(0.05f, 0.8f),
            Material = rainMat,
        };
        _rain = MakeEmitter(rainQuad, 2400, 1.6f, new Vector3(46, 2, 46),
                new Vector3(0.12f, -1.0f, 0.05f), 14.0f, 20.0f, 0.7f, 1.3f, new Vector3(0, -4, 0));

        StandardMaterial3D snowMat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = ImageTexture.CreateFromImage(DotImage()),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableReceiveShadows = true,
        };
        QuadMesh snowQuad = new()
        {
            Size = new Vector2(0.12f, 0.12f),
            Material = snowMat,
        };
        _snow = MakeEmitter(snowQuad, 1400, 6.0f, new Vector3(46, 2, 46),
                new Vector3(0.2f, -1.0f, 0.1f), 1.2f, 2.4f, 0.7f, 1.5f, new Vector3(0, -0.2f, 0));

        StandardMaterial3D splashMat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = ImageTexture.CreateFromImage(DotImage()),
            AlbedoColor = new Color(0.85f, 0.92f, 1.0f, 0.8f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableReceiveShadows = true,
        };
        QuadMesh splashQuad = new()
        {
            Size = new Vector2(0.07f, 0.07f),
            Material = splashMat,
        };
        _groundSplash = MakeEmitter(splashQuad, 300, 0.28f, new Vector3(20, 0.2f, 20),
                new Vector3(0, 1, 0), 0.6f, 1.8f, 0.7f, 1.4f, new Vector3(0, -7, 0));
    }

    private GpuParticles3D MakeEmitter(Mesh draw, int amount, float lifetime, Vector3 extents,
            Vector3 dir, float velMin, float velMax, float sMin, float sMax, Vector3 grav)
    {
        ParticleProcessMaterial pm = new()
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = extents,
            Direction = dir,
            Spread = 4.0f,
            InitialVelocityMin = velMin,
            InitialVelocityMax = velMax,
            Gravity = grav,
            ScaleMin = sMin,
            ScaleMax = sMax,
        };
        GpuParticles3D p = new()
        {
            ProcessMaterial = pm,
            DrawPass1 = draw,
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = 0.8f,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-50, -30, -50), new Vector3(100, 60, 100)),
            Emitting = false,
        };
        AddChild(p);
        return p;
    }

    private static Image StreakImage()
    {
        Image img = Image.CreateEmpty(4, 24, false, Image.Format.Rgba8);
        for (int y = 0; y < 24; y++)
        {
            float t = (float)y / 23.0f;
            float a = Mathf.Sin(Mathf.Pi * t) * 0.9f;
            for (int x = 0; x < 4; x++)
            {
                img.SetPixel(x, y, new Color(0.72f, 0.78f, 0.92f, a));
            }
        }

        return img;
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
                float a = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
                img.SetPixel(x, y, new Color(1, 1, 1, a * 0.85f));
            }
        }

        return img;
    }
}
