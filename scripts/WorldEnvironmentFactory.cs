using Godot;

namespace Minecraft;

/// <summary>Shared sky/lighting environment used by the game and the menu backdrop.</summary>
public static class WorldEnvironmentFactory
{
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

        return new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.66f, 0.78f, 0.95f),
            FogDensity = improvedWorldgenLighting ? 0.00065f : 0.0035f,
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
            GlowIntensity = 0.5f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 0.9f,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = improvedWorldgenLighting ? 0.00025f : 0.0028f,
            VolumetricFogLength = improvedWorldgenLighting ? 180.0f : 140.0f,
            VolumetricFogAlbedo = new Color(0.9f, 0.95f, 1.0f),
        };
    }
}
