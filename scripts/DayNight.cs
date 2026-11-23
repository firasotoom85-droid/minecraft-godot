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

    // Keep eye adaptation responsive outdoors, but prevent a sealed cave from
    // being lifted toward daylight by the camera's global luminance histogram.
    // The minimum sensitivity remains owned by Player's camera configuration.
    internal const float MinimumAutoExposureSensitivity = 40.0f;
    internal const float OutdoorAutoExposureMaxSensitivity = 140.0f;
    internal const float CaveAutoExposureMaxSensitivity = MinimumAutoExposureSensitivity;
    internal const float CaveAmbientScale = 0.08f;
    internal const float WaterAutoExposureMinSensitivity = 36.0f;
    internal const float WaterAutoExposureMaxSensitivity = 108.0f;
    internal const float SnowAutoExposureMinSensitivity = 32.0f;
    internal const float SnowAutoExposureMaxSensitivity = 124.0f;
    internal const float ForestAutoExposureMaxSensitivity = 156.0f;
    internal const float DarkAdaptationSpeed = 0.70f;
    internal const float BrightAdaptationSpeed = 1.40f;
    internal const float SteadyAdaptationSpeed = 0.90f;
    private const float BiomeTransitionRate = 2.8f;

    internal readonly record struct ExposureLimits(
            float MinimumSensitivity, float MaximumSensitivity);
    internal readonly record struct WaterFogProfile(
            Color Color, float Density, float DepthBegin, float DepthEnd);
    internal readonly record struct BiomeLightingProfile(
            Color FogTint, float SnowWeight, float ForestWeight);

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
            Color SkyLightColor,
            Vector3 MoonDirection,
            Color MoonColor,
            Color CloudColor,
            float CloudOpacity);

    /// <summary>
    /// Deterministic atmosphere palette shared by gameplay and visual tests.
    /// Twilight uses narrow dawn/dusk envelopes while daylight follows the sun
    /// elevation, avoiding a hard colour jump at either horizon crossing.
    /// </summary>
    internal static SkyPalette EvaluatePalette(double timeOfDay, double cloudiness, double flash)
    {
        double safeTime = double.IsFinite(timeOfDay) ? timeOfDay : 0.5;
        double t = safeTime - System.Math.Floor(safeTime);
        double angle = (t - 0.25) * System.Math.Tau;
        double elevation = System.Math.Sin(angle);
        double day = SmoothStep(-0.14, 0.28, elevation);
        double night = 1.0 - day;
        double sunrise = GaussianPulse(t, 0.25, 0.065);
        double sunset = GaussianPulse(t, 0.75, 0.065);
        double twilight = System.Math.Clamp(System.Math.Max(sunrise, sunset), 0.0, 1.0);
        double cover = double.IsFinite(cloudiness)
                ? System.Math.Clamp(cloudiness, 0.0, 1.0) : 0.05;
        double clouds = SmoothStep(0.12, 0.92, cover);
        double lightning = double.IsFinite(flash)
                ? System.Math.Clamp(flash, 0.0, 1.0) : 0.0;

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

        // Keep the night sky and environment fill restrained. The existing
        // directional moon remains the broad outdoor key; hostile materials
        // gain a small light-dependent rim response rather than raising global
        // ambient and washing every terrain surface out of the dark.
        float skyLight = (float)System.Math.Clamp(
                day * (1.0 - cover * 0.65) + night * 0.065, 0.0, 1.25);
        float ambient = (float)((0.065 + day * 0.935) * (1.0 - cover * 0.30)
                + lightning * 2.0);
        float sunEnergy = (float)(day * 1.15 * (1.0 - cover * 0.60));
        float moonEnergy = (float)(night * 0.18 * (1.0 - cover * 0.50));

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

        // Clouds share the sky's day/night, overcast, twilight and lightning
        // inputs. Coverage is weather-driven, not a quality setting: the same
        // two existing layers remain available on LOW without extra draw nodes.
        Color cloudColor = new Color(0.22f, 0.27f, 0.40f)
                .Lerp(new Color(0.99f, 0.99f, 1.0f), (float)day);
        cloudColor = cloudColor.Lerp(new Color(0.34f, 0.38f, 0.46f),
                (float)(clouds * 0.86));
        cloudColor = cloudColor.Lerp(twilightSun, (float)(twilight * 0.12));
        cloudColor = cloudColor.Lerp(Colors.White, (float)(lightning * 0.92));
        float cloudOpacity = (float)(0.18 + clouds * 0.80);

        return new SkyPalette(day, night, twilight, top, horizon, ground,
                skyLight, ambient, sunEnergy, moonEnergy, 1.0 + cover * 1.6,
                EvaluateSunDirection(t), sunColor, skyLightColor,
                EvaluateMoonDirection(t), new Color(0.62f, 0.74f, 1.0f),
                cloudColor, cloudOpacity);
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

    /// <summary>Direction from a shaded surface toward the moon, opposite the sun.</summary>
    internal static Vector3 EvaluateMoonDirection(double timeOfDay) =>
            -EvaluateSunDirection(timeOfDay);

    /// <summary>
    /// Star visibility after twilight and cloud occlusion. Clouds obscure the
    /// existing procedural star layer without lifting terrain or cave ambient.
    /// </summary>
    internal static float EvaluateStarVisibility(double night, double cloudiness)
    {
        double darknessFade = System.Math.Clamp(night * 1.4 - 0.3, 0.0, 1.0);
        double cloudCover = System.Math.Clamp(cloudiness, 0.0, 1.0);
        double cloudOcclusion = SmoothStep(0.25, 0.95, cloudCover);
        return (float)System.Math.Clamp(darknessFade * (1.0 - cloudOcclusion * 0.92), 0.0, 1.0);
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
    /// fully sheltered spaces settle at the authored 8% ambient floor.
    /// </summary>
    internal static float EvaluateCaveDarkness(float skyLight)
    {
        float exposure = Mathf.Clamp(skyLight / VoxelLighting.MaxLevel, 0.0f, 1.0f);
        return 1.0f - (float)SmoothStep(0.12, 0.88, exposure);
    }

    /// <summary>
    /// Caps the camera's dark-scene exposure range using the same cached-sky
    /// darkness signal as cave fog. A zero-skylight room reaches the camera's
    /// minimum sensitivity, locking exposure so a torch cannot lift the room.
    /// Block light deliberately does not affect this cap.
    /// </summary>
    internal static float EvaluateAutoExposureMaxSensitivity(float caveDarkness)
    {
        float darkness = Mathf.Clamp(caveDarkness, 0.0f, 1.0f);
        return Mathf.Clamp(Mathf.Lerp(OutdoorAutoExposureMaxSensitivity,
                CaveAutoExposureMaxSensitivity, darkness),
                CaveAutoExposureMaxSensitivity, OutdoorAutoExposureMaxSensitivity);
    }

    /// <summary>Scale indirect environment fill down smoothly in enclosed caves.</summary>
    internal static float EvaluateCaveAmbientScale(float caveDarkness)
    {
        float darkness = Mathf.Clamp(caveDarkness, 0.0f, 1.0f);
        return Mathf.Clamp(Mathf.Lerp(1.0f, CaveAmbientScale, darkness),
                CaveAmbientScale, 1.0f);
    }

    /// <summary>
    /// Selects the bounded auto-exposure range for the current visual context.
    /// Cave shelter has final priority; water and biome modifiers only shape the
    /// outdoor range and never allow a cave to regain global exposure headroom.
    /// </summary>
    internal static ExposureLimits EvaluateExposureLimits(
            float caveDarkness, float waterBlend, float snowWeight, float forestWeight)
    {
        float cave = Mathf.Clamp(caveDarkness, 0.0f, 1.0f);
        float water = Mathf.Clamp(waterBlend, 0.0f, 1.0f);
        float snow = Mathf.Clamp(snowWeight, 0.0f, 1.0f);
        float forest = Mathf.Clamp(forestWeight, 0.0f, 1.0f);

        float minimum = Mathf.Lerp(MinimumAutoExposureSensitivity,
                SnowAutoExposureMinSensitivity, snow);
        float maximum = Mathf.Lerp(OutdoorAutoExposureMaxSensitivity,
                SnowAutoExposureMaxSensitivity, snow);
        maximum = Mathf.Lerp(maximum, ForestAutoExposureMaxSensitivity, forest);
        minimum = Mathf.Lerp(minimum, WaterAutoExposureMinSensitivity, water);
        maximum = Mathf.Lerp(maximum, WaterAutoExposureMaxSensitivity, water);
        minimum = Mathf.Lerp(minimum, MinimumAutoExposureSensitivity, cave);
        maximum = Mathf.Lerp(maximum, CaveAutoExposureMaxSensitivity, cave);
        maximum = Mathf.Max(minimum, maximum);
        return new ExposureLimits(minimum, maximum);
    }

    /// <summary>
    /// Darkness signal used only to choose adaptation speed. The environment
    /// gets darker in caves, water and dense canopy; snow offsets a little of
    /// that bias so the move into a bright snowfield is treated as brightening.
    /// </summary>
    internal static float EvaluateExposureDarkness(
            float caveDarkness, float waterBlend, float snowWeight, float forestWeight)
    {
        float darkness = Mathf.Max(
                Mathf.Clamp(caveDarkness, 0.0f, 1.0f),
                Mathf.Max(Mathf.Clamp(waterBlend, 0.0f, 1.0f) * 0.45f,
                        Mathf.Clamp(forestWeight, 0.0f, 1.0f) * 0.22f));
        return Mathf.Clamp(darkness - Mathf.Clamp(snowWeight, 0.0f, 1.0f) * 0.12f,
                0.0f, 1.0f);
    }

    /// <summary>Dark adaptation is deliberate; bright exits settle more quickly.</summary>
    internal static float EvaluateAutoExposureSpeed(float previousDarkness, float currentDarkness)
    {
        if (!float.IsFinite(previousDarkness) || previousDarkness < 0.0f)
            return SteadyAdaptationSpeed;
        if (currentDarkness > previousDarkness + 0.002f)
            return DarkAdaptationSpeed;
        if (currentDarkness < previousDarkness - 0.002f)
            return BrightAdaptationSpeed;
        return SteadyAdaptationSpeed;
    }

    /// <summary>Frame-rate-independent interpolation amount for cached presentation targets.</summary>
    internal static float EvaluateTransitionBlend(double delta, float rate)
    {
        if (!double.IsFinite(delta) || !float.IsFinite(rate) || delta <= 0.0 || rate <= 0.0f)
            return 0.0f;
        return Mathf.Clamp(1.0f - (float)System.Math.Exp(-delta * rate), 0.0f, 1.0f);
    }

    /// <summary>
    /// Blends immersion fog on the shared world environment. Modern worlds keep
    /// camera-depth fog active and shorten its range; legacy worlds retain their
    /// exponential profile and blend its density. This avoids swapping camera
    /// environments at the waterline.
    /// </summary>
    internal static WaterFogProfile EvaluateWaterFog(
            Color airColor, float airDensity, float waterBlend)
    {
        float blend = float.IsFinite(waterBlend)
                ? Mathf.Clamp(waterBlend, 0.0f, 1.0f) : 0.0f;
        Color waterColor = new(0.018f, 0.105f, 0.205f);
        return new WaterFogProfile(
                airColor.Lerp(waterColor, blend),
                Mathf.Lerp(airDensity, UnderwaterFogDensity, blend),
                Mathf.Lerp(WorldEnvironmentFactory.DistanceHazeBegin, 0.0f, blend),
                Mathf.Lerp(WorldEnvironmentFactory.DistanceHazeEnd, 14.0f, blend));
    }

    internal static BiomeLightingProfile EvaluateBiomeLightingProfile(Biome biome) => biome switch
    {
        Biome.Desert => new(new Color(1.06f, 0.98f, 0.82f), 0.0f, 0.0f),
        Biome.Snow => new(new Color(0.88f, 0.95f, 1.06f), 1.0f, 0.0f),
        Biome.Birch => new(new Color(0.94f, 1.05f, 0.92f), 0.0f, 0.65f),
        Biome.DenseForest => new(new Color(0.94f, 1.02f, 0.92f), 0.0f, 1.0f),
        Biome.Taiga => new(new Color(0.91f, 0.97f, 1.03f), 0.0f, 0.85f),
        Biome.Jungle => new(new Color(0.94f, 1.03f, 0.90f), 0.0f, 0.75f),
        Biome.Swamp => new(new Color(0.94f, 1.02f, 0.91f), 0.0f, 0.55f),
        _ => new(new Color(1.02f, 1.02f, 0.98f), 0.0f, 0.0f),
    };

    private const double CausticFrameTime = 0.1;

    private readonly DirectionalLight3D _moon = new();
    private MeshInstance3D _stars = null!;
    private MeshInstance3D _clouds = null!;
    private MeshInstance3D _cloudsHigh = null!;
    private StandardMaterial3D _starMaterial = null!;

    private ProceduralSkyMaterial _skyMat = null!;
    private float _fogDensityAir = WorldEnvironmentFactory.LegacyAirFogDensity;
    private bool _usesDistanceHaze;
    internal const float UnderwaterFogDensity = 0.07f;
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
    private Color _targetTerrainBiomeTint = new(1.02f, 1.02f, 0.98f);
    private float _snowExposureWeight;
    private float _targetSnowExposureWeight;
    private float _forestExposureWeight;
    private float _targetForestExposureWeight;
    private CameraAttributesPractical? _cameraAttributes;
    private float _lastAutoExposureMinSensitivity = -1.0f;
    private float _lastAutoExposureMaxSensitivity = -1.0f;
    private float _lastAutoExposureSpeed = -1.0f;
    private float _lastExposureDarkness = -1.0f;
    private float _exposureSpeedHoldTimer;
    private float _transitionExposureSpeed = SteadyAdaptationSpeed;

    /// <summary>Night spans the dark half of the cycle (sun below the horizon).</summary>
    public static bool IsNight(double t) => t < 0.25 || t >= 0.75;

    public void Setup(VoxelWorld world, Player player, DirectionalLight3D sun, WorldEnvironment env)
    {
        World = world;
        PlayerRef = player;
        Sun = sun;
        WorldEnv = env;
        _cameraAttributes = player.Camera.Attributes as CameraAttributesPractical;
        _lastAutoExposureMinSensitivity = -1.0f;
        _lastAutoExposureMaxSensitivity = -1.0f;
        _lastAutoExposureSpeed = -1.0f;
        _lastExposureDarkness = -1.0f;
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
        _moon.LightColor = palette.MoonColor;
        _moon.LightEnergy = palette.MoonEnergy;
        _moon.Visible = night > 0.02;

        double flash = System.Math.Clamp(SkyFlash, 0.0, 1.0);
        _skyMat.SkyTopColor = palette.Top;
        _skyMat.SkyHorizonColor = palette.Horizon;
        _skyMat.GroundHorizonColor = palette.GroundHorizon;

        Godot.Environment env = WorldEnv!.Environment;
        ChunkMaterials.SetSkyLighting(palette.SkyLightEnergy, palette.SunDirection,
                palette.SunColor, palette.SkyLightColor, palette.SunEnergy,
                palette.MoonDirection, palette.MoonColor, palette.MoonEnergy);
        env.AmbientLightEnergy = palette.AmbientLightEnergy;
        Color fogColor = palette.Horizon;
        double fogDensity = _fogDensityAir * palette.FogDensityMultiplier;

        // Biome tint and cave depth share one cached generator sample.
        bool hasTerrainSample = World != null && PlayerRef != null;
        if (hasTerrainSample)
        {
            UpdateTerrainSample();
            BlendTerrainPresentation(delta);
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

        if (_cameraAttributes != null && GodotObject.IsInstanceValid(_cameraAttributes))
        {
            float waterBlend = PlayerRef?.WaterViewBlend ?? 0.0f;
            ExposureLimits limits = EvaluateExposureLimits(caveDarkness, waterBlend,
                    _snowExposureWeight, _forestExposureWeight);
            if (Mathf.Abs(limits.MinimumSensitivity - _lastAutoExposureMinSensitivity) >= 0.5f)
            {
                _cameraAttributes.AutoExposureMinSensitivity = limits.MinimumSensitivity;
                _lastAutoExposureMinSensitivity = limits.MinimumSensitivity;
            }

            if (Mathf.Abs(limits.MaximumSensitivity - _lastAutoExposureMaxSensitivity) >= 0.5f)
            {
                _cameraAttributes.AutoExposureMaxSensitivity = limits.MaximumSensitivity;
                _lastAutoExposureMaxSensitivity = limits.MaximumSensitivity;
            }

            float exposureDarkness = EvaluateExposureDarkness(caveDarkness, waterBlend,
                    _snowExposureWeight, _forestExposureWeight);
            float transitionSpeed = EvaluateAutoExposureSpeed(
                    _lastExposureDarkness, exposureDarkness);
            if (Mathf.Abs(transitionSpeed - SteadyAdaptationSpeed) >= 0.05f)
            {
                _transitionExposureSpeed = transitionSpeed;
                _exposureSpeedHoldTimer = 1.0f;
            }
            else
            {
                _exposureSpeedHoldTimer = Mathf.Max(
                        _exposureSpeedHoldTimer - (float)delta, 0.0f);
                if (_exposureSpeedHoldTimer <= 0.0f)
                    _transitionExposureSpeed = SteadyAdaptationSpeed;
            }

            float adaptationSpeed = _transitionExposureSpeed;
            if (Mathf.Abs(adaptationSpeed - _lastAutoExposureSpeed) >= 0.05f)
            {
                _cameraAttributes.AutoExposureSpeed = adaptationSpeed;
                _lastAutoExposureSpeed = adaptationSpeed;
            }
            _lastExposureDarkness = exposureDarkness;
        }

        if (caveDarkness > 0.001f)
        {
            Color caveFog = new Color(0.015f, 0.02f, 0.03f)
                    .Lerp(fogColor, (float)(day * 0.25));
            fogColor = fogColor.Lerp(caveFog, caveDarkness);
            fogDensity = Mathf.Lerp((float)fogDensity, 0.035f, caveDarkness);
            env.AmbientLightEnergy *= EvaluateCaveAmbientScale(caveDarkness);
        }

        float waterBlend = PlayerRef?.WaterViewBlend ?? 0.0f;
        WaterFogProfile waterFog = EvaluateWaterFog(fogColor, (float)fogDensity, waterBlend);
        fogColor = waterFog.Color;
        fogDensity = waterFog.Density;
        bool underwater = waterBlend > 0.001f;

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
                _usesDistanceHaze, sheltered, underwater && !_usesDistanceHaze);
        if (env.FogMode != fogMode)
        {
            env.FogMode = fogMode;
        }

        if (_usesDistanceHaze && !sheltered)
        {
            fogDensity = System.Math.Clamp(fogDensity,
                    0.0, WorldEnvironmentFactory.MaxDistanceHazeDensity);
            // Keep the shared depth-fog mode while its range contracts from
            // outdoor haze to the close underwater profile.
            env.FogDepthBegin = waterFog.DepthBegin;
            env.FogDepthEnd = Mathf.Max(waterFog.DepthBegin + 1.0f, waterFog.DepthEnd);
        }

        float blend = (float)System.Math.Clamp(delta * 5.0, 0.0, 1.0);
        env.FogLightColor = env.FogLightColor.Lerp(fogColor, blend);
        env.FogDensity = Mathf.Lerp(env.FogDensity, (float)fogDensity, blend);

        _starMaterial.AlbedoColor = _starMaterial.AlbedoColor.WithAlpha(
                EvaluateStarVisibility(night, Cloudiness));
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

        UpdateCloudLayer(_clouds, palette.CloudColor, palette.CloudOpacity * 0.92f,
                0.004, delta);
        UpdateCloudLayer(_cloudsHigh, palette.CloudColor, palette.CloudOpacity * 0.58f,
                0.0068, delta);
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
        BiomeLightingProfile profile = EvaluateBiomeLightingProfile(biome);
        _targetTerrainBiomeTint = profile.FogTint;
        _targetSnowExposureWeight = profile.SnowWeight;
        _targetForestExposureWeight = profile.ForestWeight;
    }

    private void BlendTerrainPresentation(double delta)
    {
        float blend = EvaluateTransitionBlend(delta, BiomeTransitionRate);
        _terrainBiomeTint = _terrainBiomeTint.Lerp(_targetTerrainBiomeTint, blend);
        _snowExposureWeight = Mathf.Lerp(_snowExposureWeight, _targetSnowExposureWeight, blend);
        _forestExposureWeight = Mathf.Lerp(_forestExposureWeight, _targetForestExposureWeight, blend);
    }

    private static void UpdateCloudLayer(
            MeshInstance3D layer, Color cloudColor, float alpha, double speed, double delta)
    {
        StandardMaterial3D cloudMat = (StandardMaterial3D)layer.MaterialOverride;
        cloudMat.AlbedoColor = cloudColor.WithAlpha(alpha);
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
            EmissionEnabled = true,
            Emission = new Color(0.72f, 0.82f, 1.0f),
            EmissionEnergyMultiplier = 0.55f,
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
        // Deterministic colour/size variation adds visible points and depth
        // while retaining the single texture, sphere and material.
        for (int i = 0; i < 960; i++)
        {
            float brightness = rng.RandfRange(0.32f, 0.90f);
            Color tint = rng.Randf() < 0.72f
                    ? new Color(0.82f, 0.89f, 1.0f)
                    : new Color(1.0f, 0.88f, 0.72f);
            Color star = new(tint.R * brightness, tint.G * brightness,
                    tint.B * brightness, brightness);
            img.SetPixel(rng.RandiRange(0, w - 1), rng.RandiRange(0, h - 1), star);
        }

        for (int i = 0; i < 52; i++)
        {
            int x = rng.RandiRange(0, w - 2);
            int y = rng.RandiRange(0, h - 2);
            float brightness = rng.RandfRange(0.72f, 1.0f);
            Color star = new(brightness * 0.90f, brightness * 0.94f,
                    brightness, 0.88f);
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
                img.SetPixel(x + dx, y + dy, star);
        }

        for (int i = 0; i < 10; i++)
        {
            int x = rng.RandiRange(1, w - 3);
            int y = rng.RandiRange(1, h - 3);
            for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
                img.SetPixel(x + dx, y + dy, new Color(0.86f, 0.92f, 1.0f, 0.78f));
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
