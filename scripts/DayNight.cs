using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Day/night cycle: animated sun + moon, sky and fog colours lerped through
/// dawn/noon/dusk/night, a procedural starfield sphere, a drifting cloud
/// layer that follows the camera, and the underwater fog override.
/// </summary>
public partial class DayNight : Node
{
    public VoxelWorld? World;
    public Player? PlayerRef;
    public DirectionalLight3D? Sun;
    public WorldEnvironment? WorldEnv;

    private const double DayLength = 600.0;   // seconds for a full day
    private const float SunAzimuthDegrees = -35.0f;
    private const int CausticFrames = 6;

    internal readonly record struct SkyPalette(
            double Daylight,
            double Night,
            double Twilight,
            Color Top,
            Color Horizon,
            Color GroundHorizon,
            float SkyLightEnergy,
            float AmbientLightEnergy,
            float SunEnergy,
            float MoonEnergy,
            double FogDensityMultiplier,
            Vector3 SunDirection,
            Color SunColor,
            Color SkyLightColor);

    /// <summary>
    /// Deterministic atmosphere palette shared by gameplay and visual tests.
    /// Twilight uses narrow dawn/dusk envelopes while daylight follows the sun
    /// elevation, avoiding a hard colour jump at either horizon crossing.
    /// </summary>
    internal static SkyPalette EvaluatePalette(double timeOfDay, double cloudiness, double flash)
    {
        double t = timeOfDay - System.Math.Floor(timeOfDay);
        double angle = (t - 0.25) * System.Math.Tau;
        double elevation = System.Math.Sin(angle);
        double day = SmoothStep(-0.14, 0.28, elevation);
        double night = 1.0 - day;
        double sunrise = GaussianPulse(t, 0.25, 0.065);
        double sunset = GaussianPulse(t, 0.75, 0.065);
        double twilight = System.Math.Clamp(System.Math.Max(sunrise, sunset), 0.0, 1.0);
        double cover = System.Math.Clamp(cloudiness, 0.0, 1.0);
        double clouds = SmoothStep(0.12, 0.92, cover);
        double lightning = System.Math.Clamp(flash, 0.0, 1.0);

        Color top = new Color(0.012f, 0.020f, 0.055f)
                .Lerp(new Color(0.22f, 0.44f, 0.80f), (float)day);
        Color horizon = new Color(0.04f, 0.065f, 0.14f)
                .Lerp(new Color(0.66f, 0.78f, 0.95f), (float)day);
        Color ground = new Color(0.012f, 0.018f, 0.032f)
                .Lerp(new Color(0.25f, 0.30f, 0.26f), (float)day);

        double duskShare = sunset / System.Math.Max(sunrise + sunset, 0.0001);
        Color twilightHorizon = new Color(1.0f, 0.56f, 0.34f)
                .Lerp(new Color(1.0f, 0.36f, 0.25f), (float)duskShare);
        top = top.Lerp(new Color(0.39f, 0.24f, 0.40f), (float)(twilight * 0.68));
        horizon = horizon.Lerp(twilightHorizon, (float)(twilight * 0.92));
        ground = ground.Lerp(new Color(0.24f, 0.13f, 0.17f), (float)(twilight * 0.32));

        // Overcast skies lose saturation and brightness without becoming a
        // flat grey; a small horizon lift preserves distance readability.
        top = top.Lerp(new Color(0.27f, 0.31f, 0.39f), (float)(clouds * 0.72));
        horizon = horizon.Lerp(new Color(0.50f, 0.56f, 0.64f), (float)(clouds * 0.62));
        ground = ground.Lerp(new Color(0.17f, 0.19f, 0.22f), (float)(clouds * 0.28));

        top = top.Lerp(new Color(0.88f, 0.92f, 1.0f), (float)(lightning * 0.85));
        horizon = horizon.Lerp(new Color(0.95f, 0.97f, 1.0f), (float)(lightning * 0.90));
        ground = ground.Lerp(new Color(0.78f, 0.84f, 0.94f), (float)(lightning * 0.55));

        float skyLight = (float)System.Math.Clamp(
                day * (1.0 - cover * 0.65) + night * 0.10, 0.0, 1.25);
        float ambient = (float)((0.10 + day * 0.90) * (1.0 - cover * 0.30)
                + lightning * 2.0);
        float sunEnergy = (float)(day * 1.15 * (1.0 - cover * 0.60));
        float moonEnergy = (float)(night * 0.22 * (1.0 - cover * 0.50));

        Color dawnSun = new(1.0f, 0.66f, 0.40f);
        Color duskSun = new(1.0f, 0.49f, 0.31f);
        Color twilightSun = dawnSun.Lerp(duskSun, (float)duskShare);
        Color sunColor = new Color(1.0f, 0.96f, 0.89f)
                .Lerp(twilightSun, (float)(twilight * 0.96));
        sunColor = sunColor.Lerp(new Color(0.82f, 0.88f, 0.98f), (float)(night * 0.65));
        sunColor = sunColor.Lerp(Colors.White, (float)(lightning * 0.85));

        Color skyLightColor = new Color(0.88f, 0.94f, 1.0f)
                .Lerp(new Color(0.42f, 0.52f, 0.78f), (float)(night * 0.72));
        skyLightColor = skyLightColor.Lerp(twilightSun, (float)(twilight * 0.20));
        skyLightColor = skyLightColor.Lerp(new Color(0.72f, 0.79f, 0.90f), (float)(clouds * 0.45));
        skyLightColor = skyLightColor.Lerp(Colors.White, (float)(lightning * 0.55));

        return new SkyPalette(day, night, twilight, top, horizon, ground,
                skyLight, ambient, sunEnergy, moonEnergy, 1.0 + cover * 1.6,
                EvaluateSunDirection(t), sunColor, skyLightColor);
    }

    /// <summary>
    /// Direction from a shaded surface toward the sun, matching the existing
    /// runtime light rotation and its fixed 35-degree azimuth.
    /// </summary>
    internal static Vector3 EvaluateSunDirection(double timeOfDay)
    {
        if (!double.IsFinite(timeOfDay))
            return Vector3.Down;

        double t = timeOfDay - System.Math.Floor(timeOfDay);
        double angle = (t - 0.25) * System.Math.Tau;
        float azimuthRadians = SunAzimuthDegrees * (Mathf.Pi / 180.0f);
        float horizontal = (float)System.Math.Cos(angle);
        return new Vector3(
                Mathf.Sin(azimuthRadians) * horizontal,
                (float)System.Math.Sin(angle),
                Mathf.Cos(azimuthRadians) * horizontal).Normalized();
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        double x = System.Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }

    private static double GaussianPulse(double value, double center, double width)
    {
        double distance = (value - center) / width;
        return System.Math.Exp(-distance * distance);
    }

    /// <summary>
    /// Converts baked skylight (0..15) into a smooth cave-darkness weight.
    /// The soft transition preserves some ambient fill at cave mouths while
    /// fully sheltered spaces still reach the established 20% ambient level.
    /// </summary>
    internal static float EvaluateCaveDarkness(float skyLight)
    {
        float exposure = Mathf.Clamp(skyLight / VoxelLighting.MaxLevel, 0.0f, 1.0f);
        return 1.0f - (float)SmoothStep(0.12, 0.88, exposure);
    }

    private const double CausticFrameTime = 0.1;

    private readonly DirectionalLight3D _moon = new();
    private MeshInstance3D _stars = null!;
    private MeshInstance3D _clouds = null!;
    private MeshInstance3D _cloudsHigh = null!;
    private StandardMaterial3D _starMaterial = null!;

    private ProceduralSkyMaterial _skyMat = null!;
    private float _fogDensityAir = WorldEnvironmentFactory.LegacyAirFogDensity;
    private bool _usesDistanceHaze;
    private readonly float _fogDensityUnderwater = 0.07f;
    public double Cloudiness;      // 0..1 driven by Weather (clear -> storm)
    public double SkyFlash;        // 0..1 lightning flash driven by Weather
    public bool Paused;            // photo mode freezes the clock (time still scrubs)
    private readonly List<ImageTexture> _caustics = [];
    private int _causticsFrame;
    private double _causticsTimer;

    // Biome tint and cave depth both use the same terrain column. Sampling it
    // once per player voxel cell avoids two generator queries per frame.
    private int _terrainSampleX = int.MinValue;
    private int _terrainSampleZ = int.MinValue;
    private int _terrainSurfaceHeight;
    private Color _terrainBiomeTint = new(1.02f, 1.02f, 0.98f);

    /// <summary>Night spans the dark half of the cycle (sun below the horizon).</summary>
    public static bool IsNight(double t) => t < 0.25 || t >= 0.75;

    public void Setup(VoxelWorld world, Player player, DirectionalLight3D sun, WorldEnvironment env)
    {
        World = world;
        PlayerRef = player;
        Sun = sun;
        WorldEnv = env;
        _usesDistanceHaze = world.WorldGenVersion >= WorldGenV8.Version;
        _fogDensityAir = _usesDistanceHaze
                ? WorldEnvironmentFactory.DistanceHazeDensity
                : WorldEnvironmentFactory.LegacyAirFogDensity;
        _skyMat = (ProceduralSkyMaterial)WorldEnv.Environment.Sky.SkyMaterial;
        _moon.Name = "Moon";
        _moon.LightColor = new Color(0.55f, 0.65f, 1.0f);
        _moon.LightEnergy = 0.0f;
        _moon.ShadowEnabled = true;
        _moon.DirectionalShadowMaxDistance = 120.0f;
        // Keep the existing four blended cascades for the moving sun. Filter
        // quality, sun angular size and sun blur are selected by Gfx so LOW
        // does not inherit an expensive global SoftHigh/PCSS configuration.
        Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        Sun.DirectionalShadowBlendSplits = true;
        Sun.ShadowNormalBias = 1.0f;
        _moon.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        _moon.DirectionalShadowBlendSplits = true;
        // The moon is a low-energy fill light. Keep its source angular size at
        // zero so the night path uses the selected soft filter without a
        // second PCSS penalty; its small blur still softens the filtered edge.
        _moon.LightAngularDistance = 0.0f;
        _moon.ShadowBlur = 0.35f;
        _moon.ShadowNormalBias = 1.0f;
        Sun.AddSibling(_moon);
        BuildStars();
        BuildClouds();
        BuildCaustics();
    }

    public override void _Process(double delta)
    {
        if (World == null)
        {
            return;
        }

        if (!Paused)
        {
            World.TimeOfDay = (World.TimeOfDay + delta / DayLength) % 1.0;
        }

        double t = World.TimeOfDay;
        double a = (t - 0.25) * System.Math.Tau;   // 0.25 sunrise, 0.5 noon, 0.75 sunset
        double sinA = System.Math.Sin(a);
        SkyPalette palette = EvaluatePalette(t, Cloudiness, SkyFlash);
        double day = palette.Daylight;
        double night = palette.Night;

        // Smooth per-frame sun/moon angle: the PSSM near cascade has such fine
        // texels that each frame's movement is sub-pixel on screen, so the
        // shadows drift continuously instead of dancing in visible steps.
        Sun!.RotationDegrees = new Vector3(
                -Mathf.RadToDeg((float)a), SunAzimuthDegrees, 0.0f);
        Sun.LightColor = palette.SunColor;
        Sun.LightEnergy = palette.SunEnergy;
        Sun.Visible = sinA > -0.15;
        _moon.RotationDegrees = new Vector3(-Mathf.RadToDeg((float)(a + System.Math.PI)), -35.0f, 0.0f);
        _moon.LightEnergy = palette.MoonEnergy;
        _moon.Visible = night > 0.02;

        double flash = System.Math.Clamp(SkyFlash, 0.0, 1.0);
        _skyMat.SkyTopColor = palette.Top;
        _skyMat.SkyHorizonColor = palette.Horizon;
        _skyMat.GroundHorizonColor = palette.GroundHorizon;

        Godot.Environment env = WorldEnv!.Environment;
        ChunkMaterials.SetSkyLighting(palette.SkyLightEnergy, palette.SunDirection,
                palette.SunColor, palette.SkyLightColor, palette.SunEnergy);
        env.AmbientLightEnergy = palette.AmbientLightEnergy;
        Color fogColor = palette.Horizon;
        double fogDensity = _fogDensityAir * palette.FogDensityMultiplier;

        // Biome tint and cave depth share one cached generator sample.
        bool hasTerrainSample = World != null && PlayerRef != null;
        if (hasTerrainSample)
        {
            UpdateTerrainSample();
            fogColor = new Color(
                    Mathf.Clamp(fogColor.R * _terrainBiomeTint.R, 0.0f, 1.0f),
                    Mathf.Clamp(fogColor.G * _terrainBiomeTint.G, 0.0f, 1.0f),
                    Mathf.Clamp(fogColor.B * _terrainBiomeTint.B, 0.0f, 1.0f));
        }

        // Sample baked skylight at the camera, not just terrain height: light
        // already fades through an entrance and around cave corners. Trilinear
        // sampling avoids a one-voxel brightness step while the cache-only
        // lookup keeps the frame path bounded and allocation-free.
        float caveDarkness = 0.0f;
        if (hasTerrainSample && PlayerRef is { Underwater: false })
        {
            if (World!.TrySampleCachedSkyLight(PlayerRef.Camera.GlobalPosition, out float skyLight))
            {
                caveDarkness = EvaluateCaveDarkness(skyLight);
            }
            else
            {
                // The first mesh may not have populated the baked light cache
                // yet. Keep the initial response continuous until it is ready.
                float depth = _terrainSurfaceHeight - PlayerRef.Camera.GlobalPosition.Y;
                caveDarkness = (float)SmoothStep(1.0, 7.0, depth);
            }
        }

        if (caveDarkness > 0.001f)
        {
            Color caveFog = new Color(0.015f, 0.02f, 0.03f)
                    .Lerp(fogColor, (float)(day * 0.25));
            fogColor = fogColor.Lerp(caveFog, caveDarkness);
            fogDensity = Mathf.Lerp((float)fogDensity, 0.035f, caveDarkness);
            env.AmbientLightEnergy *= Mathf.Lerp(1.0f, 0.20f, caveDarkness);
        }

        bool underwater = PlayerRef is { Underwater: true };
        if (underwater)
        {
            fogColor = new Color(0.04f, 0.18f, 0.38f);
            fogDensity = _fogDensityUnderwater;
        }

        if (flash > 0.001)
        {
            fogColor = fogColor.Lerp(new Color(1, 1, 1), (float)(flash * 0.7));
        }

        // The smooth cave weight is also the shelter predicate for fog mode:
        // any enclosed sample keeps the established close-quarters exponential
        // profile, while open sky receives the shared camera-depth distance haze.
        bool sheltered = caveDarkness > 0.001f;

        // Depth fog is a shared camera-space effect for all terrain geometry.
        // Keep close-quarters cave and immersion fog exponential so those
        // established high-density profiles still close in around the player.
        Godot.Environment.FogModeEnum fogMode = WorldEnvironmentFactory.FogModeFor(
                _usesDistanceHaze, sheltered, underwater);
        if (env.FogMode != fogMode)
        {
            env.FogMode = fogMode;
        }

        if (_usesDistanceHaze && !sheltered && !underwater)
        {
            fogDensity = System.Math.Clamp(fogDensity,
                    0.0, WorldEnvironmentFactory.MaxDistanceHazeDensity);
        }

        float blend = (float)System.Math.Clamp(delta * 5.0, 0.0, 1.0);
        env.FogLightColor = env.FogLightColor.Lerp(fogColor, blend);
        env.FogDensity = Mathf.Lerp(env.FogDensity, (float)fogDensity, blend);

        _starMaterial.AlbedoColor = _starMaterial.AlbedoColor.WithAlpha(
                (float)System.Math.Clamp(night * 1.4 - 0.3, 0.0, 1.0));
        UpdateCaustics(delta);
        Camera3D? cam = GetViewport().GetCamera3D();
        if (cam != null)
        {
            // Clouds ride just above the world ceiling (100/118 on the classic
            // 64-high worlds, higher over gen v5's 128).
            float cloudBase = (World?.Height ?? 64) + 36.0f;
            _stars.GlobalPosition = cam.GlobalPosition;
            _clouds.GlobalPosition = new Vector3(
                Mathf.Snapped(cam.GlobalPosition.X, 16.0f), cloudBase,
                Mathf.Snapped(cam.GlobalPosition.Z, 16.0f));
            _cloudsHigh.GlobalPosition = new Vector3(
                Mathf.Snapped(cam.GlobalPosition.X, 16.0f), cloudBase + 18.0f,
                Mathf.Snapped(cam.GlobalPosition.Z, 16.0f));
        }

        UpdateCloudLayer(_clouds, day, Cloudiness, palette.Horizon, palette.Twilight,
                0.85 + Cloudiness * 0.15, 0.004, delta);
        UpdateCloudLayer(_cloudsHigh, day, Cloudiness, palette.Horizon, palette.Twilight,
                0.5 + Cloudiness * 0.2, 0.0068, delta);
    }

    private void UpdateTerrainSample()
    {
        Vector3 playerPosition = PlayerRef!.GlobalPosition;
        int sampleX = Mathf.FloorToInt(playerPosition.X);
        int sampleZ = Mathf.FloorToInt(playerPosition.Z);
        if (sampleX == _terrainSampleX && sampleZ == _terrainSampleZ)
        {
            return;
        }

        _terrainSampleX = sampleX;
        _terrainSampleZ = sampleZ;
        _terrainSurfaceHeight = World!.SurfaceHeight(sampleX, sampleZ);
        Biome biome = World.BiomeAt(sampleX, sampleZ, _terrainSurfaceHeight);
        _terrainBiomeTint = biome switch
        {
            Biome.Desert => new Color(1.06f, 0.98f, 0.82f),
            Biome.Snow => new Color(0.88f, 0.95f, 1.06f),
            Biome.Birch => new Color(0.94f, 1.05f, 0.92f),
            _ => new Color(1.02f, 1.02f, 0.98f),
        };
    }

    private static void UpdateCloudLayer(
            MeshInstance3D layer, double day, double cloudiness,
            Color twilightHorizon, double twilight, double alpha, double speed, double delta)
    {
        StandardMaterial3D cloudMat = (StandardMaterial3D)layer.MaterialOverride;
        Color cloudColor = new Color(1, 1, 1).Lerp(
                new Color(0.24f, 0.28f, 0.42f), (float)(1.0 - day));
        cloudColor = cloudColor.Lerp(
                new Color(0.42f, 0.45f, 0.52f), (float)cloudiness);
        cloudColor = cloudColor.Lerp(twilightHorizon, (float)(twilight * 0.18))
                .WithAlpha((float)alpha);
        cloudMat.AlbedoColor = cloudColor;
        Vector3 uvOffset = cloudMat.Uv1Offset;
        cloudMat.Uv1Offset = uvOffset.WithX((float)((uvOffset.X + delta * speed) % 1.0));
    }

    // -----------------------------------------------------------------------
    // Procedural sky extras (no external assets)
    // -----------------------------------------------------------------------

    private void BuildStars()
    {
        _starMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = ImageTexture.CreateFromImage(MakeStarImage()),
            AlbedoColor = new Color(1, 1, 1, 0.0f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableReceiveShadows = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        SphereMesh sphere = new()
        {
            Radius = 240.0f,
            Height = 480.0f,
            FlipFaces = true,
            RadialSegments = 32,
            Rings = 16,
        };
        _stars = new MeshInstance3D
        {
            Name = "Stars",
            Mesh = sphere,
            MaterialOverride = _starMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_stars);
    }

    private static Image MakeStarImage()
    {
        int w = 1024;
        int h = 512;
        Image img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        RandomNumberGenerator rng = new()
        {
            Seed = 424242,
        };
        for (int i = 0; i < 700; i++)
        {
            float b = rng.RandfRange(0.35f, 1.0f);
            img.SetPixel(rng.RandiRange(0, w - 1), rng.RandiRange(0, h - 1), new Color(b, b, b * 1.05f, b));
        }

        for (int i = 0; i < 40; i++)
        {
            int x = rng.RandiRange(0, w - 2);
            int y = rng.RandiRange(0, h - 2);
            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    img.SetPixel(x + dx, y + dy, new Color(1, 1, 1, 0.9f));
                }
            }
        }

        return img;
    }

    private void BuildClouds()
    {
        // Two layers at different heights and drift speeds for parallax depth.
        _clouds = MakeCloudLayer(1100.0f, 6.0f, 380.0f, 520.0f);
        _cloudsHigh = MakeCloudLayer(1500.0f, 11.0f, 600.0f, 760.0f);
    }

    private MeshInstance3D MakeCloudLayer(float sizeM, float uvScale, float fadeMin, float fadeMax)
    {
        StandardMaterial3D mat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = ImageTexture.CreateFromImage(MakeCloudImage()),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Uv1Scale = new Vector3(uvScale, uvScale, 1),
            DisableReceiveShadows = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
            DistanceFadeMinDistance = fadeMin,
            DistanceFadeMaxDistance = fadeMax,
        };
        PlaneMesh plane = new()
        {
            Size = new Vector2(sizeM, sizeM),
        };
        MeshInstance3D mi = new()
        {
            Mesh = plane,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        return mi;
    }

    private static Image MakeCloudImage()
    {
        int size = 256;
        Image img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        FastNoiseLite noise = new()
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
            Seed = 777,
            Frequency = 0.018f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = noise.GetNoise2D(x, y);
                float alpha = Mathf.Clamp((n - 0.28f) * 5.0f, 0.0f, 1.0f);
                img.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }

        return img;
    }

    /// <summary>
    /// Underwater caustics: while the player is submerged the sun projects a
    /// caustic web texture; six pre-generated frames cycle to fake the flow.
    /// </summary>
    private void UpdateCaustics(double delta)
    {
        if (PlayerRef == null)
        {
            return;
        }

        if (!PlayerRef.Underwater)
        {
            if (Sun!.LightProjector != null)
            {
                Sun.LightProjector = null;
            }

            SetWaterCaustics(0.0f);
            return;
        }

        if (_caustics.Count == 0)
        {
            return;
        }

        _causticsTimer += delta;
        if (_causticsTimer >= CausticFrameTime)
        {
            _causticsTimer = 0.0;
            _causticsFrame = (_causticsFrame + 1) % _caustics.Count;
        }

        Sun!.LightProjector = _caustics[_causticsFrame];
        SetWaterCaustics(1.0f);
    }

    private static void SetWaterCaustics(float strength)
    {
        if (ChunkMaterials.Water is ShaderMaterial water)
        {
            water.SetShaderParameter("caustic_strength", strength);
        }
    }

    private void BuildCaustics()
    {
        int size = 128;
        for (int f = 0; f < CausticFrames; f++)
        {
            Image img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            double phase = (double)f / CausticFrames * System.Math.Tau;
            for (int y = 0; y < size; y++)
            {
                double v = (double)y / size * System.Math.Tau * 2.0;
                for (int x = 0; x < size; x++)
                {
                    double u = (double)x / size * System.Math.Tau * 2.0;
                    double a = System.Math.Abs(System.Math.Sin(u + phase) * System.Math.Sin(v * 0.8 - phase));
                    double b = System.Math.Abs(System.Math.Sin(v * 1.3 - phase * 1.4) * System.Math.Sin(u * 1.1 + phase * 0.7));
                    float bright = (float)System.Math.Clamp(0.35 + System.Math.Pow(System.Math.Max(a, b), 2.5) * 0.65, 0.0, 1.0);
                    img.SetPixel(x, y, new Color(bright, bright, bright));
                }
            }

            _caustics.Add(ImageTexture.CreateFromImage(img));
        }
    }
}
