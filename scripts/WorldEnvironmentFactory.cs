using Godot;

namespace Minecraft;

/// <summary>
/// Shared sky/lighting environment used by gameplay and the menu backdrop.
/// World fog is camera-depth based for modern worlds so every streamed chunk
/// and far-LOD surface receives the same smooth distance fade.
/// </summary>
public static class WorldEnvironmentFactory
{
    public const float LegacyAirFogDensity = 0.0035f;
    public const float ImmersionAirFogDensity = 0.00065f;
    public const float DistanceHazeDensity = 0.42f;
    public const float MaxDistanceHazeDensity = 0.68f;
    public const float DistanceHazeBegin = 96.0f;
    public const float DistanceHazeEnd = 330.0f;
    public const float DistanceHazeCurve = 2.2f;

    internal static Godot.Environment.FogModeEnum FogModeFor(
            bool usesDistanceHaze, bool underground, bool underwater) =>
            usesDistanceHaze && !underground && !underwater
                    ? Godot.Environment.FogModeEnum.Depth
                    : Godot.Environment.FogModeEnum.Exponential;

    public static Godot.Environment Build(int generatorVersion = WorldStore.CurrentGenVersion)
    {
        bool improvedWorldgenLighting = generatorVersion >= WorldGenV8.Version;
        ProceduralSkyMaterial skyMat = new()
        {
            SkyTopColor = new Color(0.22f, 0.44f, 0.80f),
            SkyHorizonColor = new Color(0.66f, 0.78f, 0.95f),
            GroundBottomColor = new Color(0.25f, 0.30f, 0.25f),
            GroundHorizonColor = new Color(0.66f, 0.78f, 0.95f),
            SunAngleMax = 18.0f,
            SunCurve = 0.12f,
            SkyCurve = 0.2f,
        };
        Sky sky = new()
        {
            SkyMaterial = skyMat,
            RadianceSize = Sky.RadianceSizeEnum.Size128,
        };

        Godot.Environment environment = new()
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            // Keep ambient sky neutral; warm block light is localized by the
            // baked voxel channel rather than tinting the whole environment.
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogMode = improvedWorldgenLighting
                    ? Godot.Environment.FogModeEnum.Depth
                    : Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = new Color(0.66f, 0.78f, 0.95f),
            FogDensity = improvedWorldgenLighting ? DistanceHazeDensity : LegacyAirFogDensity,
            FogSkyAffect = improvedWorldgenLighting ? 0.08f : 0.15f,
            FogAerialPerspective = improvedWorldgenLighting ? 0.20f : 0.5f,
            FogHeight = 30.0f,
            FogHeightDensity = improvedWorldgenLighting ? 0.0015f : 0.012f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = improvedWorldgenLighting ? 1.25f : 1.4f,
            SsrEnabled = true,
            SsrMaxSteps = 64,
            GlowEnabled = true,
            GlowIntensity = 0.38f,
            GlowBloom = 0.025f,
            GlowHdrThreshold = 1.05f,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = improvedWorldgenLighting ? 0.00025f : 0.0028f,
            VolumetricFogLength = improvedWorldgenLighting ? 180.0f : 140.0f,
            VolumetricFogAlbedo = new Color(0.9f, 0.95f, 1.0f),
        };

        if (improvedWorldgenLighting)
        {
            environment.FogDepthBegin = DistanceHazeBegin;
            environment.FogDepthEnd = DistanceHazeEnd;
            environment.FogDepthCurve = DistanceHazeCurve;
        }

        return environment;
    }

    /// <summary>
    /// Mirrors Godot's depth-fog smoothstep/power profile for deterministic
    /// regression checks. The exponent delays most haze until the far range;
    /// density is bounded so even overcast weather cannot create a solid wall.
    /// </summary>
    internal static float EvaluateDistanceHaze(float distance, float density)
    {
        float normalized = (float)System.Math.Clamp(
                (distance - DistanceHazeBegin) / (DistanceHazeEnd - DistanceHazeBegin),
                0.0, 1.0);
        float smooth = normalized * normalized * (3.0f - 2.0f * normalized);
        float boundedDensity = (float)System.Math.Clamp(
                density, 0.0, MaxDistanceHazeDensity);
        return System.MathF.Pow(smooth, DistanceHazeCurve) * boundedDensity;
    }
}
