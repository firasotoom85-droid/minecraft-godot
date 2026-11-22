using System;
using Godot;

namespace Minecraft;

/// <summary>Deterministic regression coverage for sky, weather and water presentation.</summary>
public static class AtmosphereTests
{
    public static void Run()
    {
        TestDayNightPalette();
        TestDistanceHazeProfile();
        TestWeatherPrecipitationSelection();
        TestWaterDepthSpace();
    }

    private static void TestDayNightPalette()
    {
        DayNight.SkyPalette midnight = DayNight.EvaluatePalette(0.0, 0.0, 0.0);
        DayNight.SkyPalette sunrise = DayNight.EvaluatePalette(0.25, 0.0, 0.0);
        DayNight.SkyPalette noon = DayNight.EvaluatePalette(0.5, 0.0, 0.0);
        DayNight.SkyPalette sunset = DayNight.EvaluatePalette(0.75, 0.0, 0.0);
        DayNight.SkyPalette overcast = DayNight.EvaluatePalette(0.5, 0.95, 0.0);
        DayNight.SkyPalette lightning = DayNight.EvaluatePalette(0.5, 0.95, 0.8);
        DayNight.SkyPalette fullOvercast = DayNight.EvaluatePalette(0.5, 1.0, 0.0);
        DayNight.SkyPalette overClouded = DayNight.EvaluatePalette(0.5, 1.5, 0.0);
        DayNight.SkyPalette negativeCloudiness = DayNight.EvaluatePalette(0.5, -0.5, 0.0);

        TestAssert.Check(Luminance(noon.Top) > Luminance(midnight.Top),
                "daytime sky is brighter than the midnight sky");
        TestAssert.Check(noon.Top.B > noon.Top.R,
                "midday sky retains a blue upper-sky identity");
        TestAssert.Check(sunrise.Horizon.R > sunrise.Horizon.B,
                "sunrise horizon has a warm colour");
        TestAssert.Check(sunset.Horizon.R > sunset.Horizon.B,
                "sunset horizon has a warm colour");
        TestAssert.Check(sunrise.Twilight > 0.9 && sunset.Twilight > 0.9,
                "dawn and dusk receive a distinct twilight envelope");
        TestAssert.Check(noon.SkyLightEnergy > midnight.SkyLightEnergy,
                "voxel sky light is stronger during the day");
        TestAssert.Check(overcast.SkyLightEnergy < noon.SkyLightEnergy
                && overcast.AmbientLightEnergy < noon.AmbientLightEnergy,
                "heavy cloud cover softens sky and ambient lighting");
        TestAssert.Check(overcast.FogDensityMultiplier > noon.FogDensityMultiplier,
                "cloud cover increases atmospheric depth");
        TestAssert.Equal(fullOvercast.AmbientLightEnergy, overClouded.AmbientLightEnergy,
                "cloud cover above one is clamped to the full-overcast value");
        TestAssert.Equal(noon.AmbientLightEnergy, negativeCloudiness.AmbientLightEnergy,
                "negative cloud cover is clamped to a clear sky");
        TestAssert.Check(lightning.AmbientLightEnergy > overcast.AmbientLightEnergy
                && Luminance(lightning.Top) > Luminance(overcast.Top),
                "lightning temporarily lifts sky and ambient illumination");

        for (int i = 0; i <= 100; i++)
        {
            double time = i / 100.0;
            DayNight.SkyPalette sample = DayNight.EvaluatePalette(time, 0.55, 0.0);
            TestAssert.Check(double.IsFinite(sample.Daylight)
                    && double.IsFinite(sample.Night)
                    && float.IsFinite(sample.AmbientLightEnergy)
                    && sample.Daylight >= 0.0 && sample.Daylight <= 1.0
                    && sample.Night >= 0.0 && sample.Night <= 1.0,
                    $"atmosphere palette remains finite and bounded at time {time:F2}");
        }
    }

    private static void TestDistanceHazeProfile()
    {
        Godot.Environment legacy = WorldEnvironmentFactory.Build(WorldGenV7.Version);
        Godot.Environment modern = WorldEnvironmentFactory.Build(WorldGenV8.Version);

        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential, legacy.FogMode,
                "V1–V7 worlds retain the original exponential fog model");
        TestAssert.Equal(WorldEnvironmentFactory.LegacyAirFogDensity, legacy.FogDensity,
                "legacy worlds retain their original air fog density");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Depth, modern.FogMode,
                "V8+ worlds use camera-depth haze for distant terrain");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeDensity, modern.FogDensity,
                "modern worlds start from the restrained distance-haze density");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeBegin, modern.FogDepthBegin,
                "distance haze starts after nearby terrain remains clear");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeEnd, modern.FogDepthEnd,
                "distance haze fades gradually through the visible horizon");
        TestAssert.Check(modern.FogDepthBegin > 0.0f
                && modern.FogDepthEnd > modern.FogDepthBegin,
                "distance haze has a valid positive depth interval");

        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeCurve, modern.FogDepthCurve,
                "modern environment uses the authored gentle depth-fog power curve");

        float nearHaze = WorldEnvironmentFactory.EvaluateDistanceHaze(150.0f,
                WorldEnvironmentFactory.DistanceHazeDensity);
        float midHaze = WorldEnvironmentFactory.EvaluateDistanceHaze(213.0f,
                WorldEnvironmentFactory.DistanceHazeDensity);
        float farHaze = WorldEnvironmentFactory.EvaluateDistanceHaze(300.0f,
                WorldEnvironmentFactory.DistanceHazeDensity);
        float maximumHaze = WorldEnvironmentFactory.EvaluateDistanceHaze(
                WorldEnvironmentFactory.DistanceHazeEnd, 2.0f);
        TestAssert.Check(nearHaze < 0.01f,
                "distance haze leaves near terrain almost completely clear");
        TestAssert.Check(midHaze > nearHaze && midHaze < 0.10f,
                "distance haze stays subtle through the middle distance");
        TestAssert.Check(farHaze > midHaze && farHaze < 0.42f,
                "distant mountain silhouettes receive haze without disappearing");
        TestAssert.Check(maximumHaze <= WorldEnvironmentFactory.MaxDistanceHazeDensity
                && maximumHaze < 1.0f,
                "maximum overcast haze is bounded below full scene occlusion");

        float previous = -1.0f;
        for (int i = 0; i <= 64; i++)
        {
            float distance = WorldEnvironmentFactory.DistanceHazeBegin
                    + (WorldEnvironmentFactory.DistanceHazeEnd
                            - WorldEnvironmentFactory.DistanceHazeBegin) * i / 64.0f;
            float sample = WorldEnvironmentFactory.EvaluateDistanceHaze(
                    distance, WorldEnvironmentFactory.DistanceHazeDensity);
            TestAssert.Check(float.IsFinite(sample) && sample >= 0.0f
                    && sample <= WorldEnvironmentFactory.DistanceHazeDensity,
                    $"distance haze remains finite and bounded at sample {i}/64");
            TestAssert.Check(sample + 0.0001f >= previous,
                    $"distance haze never decreases at sample {i}/64");
            previous = sample;
        }

        TestAssert.Equal(Godot.Environment.FogModeEnum.Depth,
                WorldEnvironmentFactory.FogModeFor(true, underground: false, underwater: false),
                "outdoor V8+ views use depth haze");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential,
                WorldEnvironmentFactory.FogModeFor(false, underground: false, underwater: false),
                "legacy outdoor worlds keep exponential fog");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential,
                WorldEnvironmentFactory.FogModeFor(true, underground: true, underwater: false),
                "cave darkness retains close-range exponential fog");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential,
                WorldEnvironmentFactory.FogModeFor(true, underground: false, underwater: true),
                "underwater immersion retains close-range exponential fog");

        foreach (int presetIndex in new[] { 0, 1 })
        {
            Gfx.Preset preset = Gfx.Presets[presetIndex];
            Godot.Environment lowCost = WorldEnvironmentFactory.Build();
            lowCost.VolumetricFogEnabled = preset.VolFog;
            TestAssert.Check(lowCost.FogEnabled
                    && lowCost.FogMode == Godot.Environment.FogModeEnum.Depth
                    && !lowCost.VolumetricFogEnabled,
                    $"{preset.Name} retains inexpensive depth haze without volumetric fog");
        }
    }

    private static void TestWeatherPrecipitationSelection()
    {
        TestAssert.Check(Weather.UsesSnowPrecipitation(Biome.Snow),
                "snow biome selects snowfall");
        TestAssert.Check(!Weather.UsesSnowPrecipitation(Biome.Plains)
                && !Weather.UsesSnowPrecipitation(Biome.Desert)
                && !Weather.UsesSnowPrecipitation(Biome.DenseForest),
                "non-snow biomes retain rain precipitation");
        TestAssert.Check(Weather.NeedsBiomeSample(false, 12, 24, 12, 24),
                "precipitation samples the player's initial biome cell");
        TestAssert.Check(!Weather.NeedsBiomeSample(true, 12, 24, 12, 24),
                "precipitation does not resample an unchanged biome cell");
        TestAssert.Check(Weather.NeedsBiomeSample(true, 12, 24, 13, 24),
                "precipitation resamples after movement across the X cell boundary");
        TestAssert.Check(Weather.NeedsBiomeSample(true, 12, 24, 12, 25),
                "precipitation resamples after movement across the Z cell boundary");
        TestAssert.Equal(0.05f, Weather.TargetCloudinessFor(Weather.State.Clear),
                "clear weather keeps its configured low cloud target");
        TestAssert.Check(Weather.TargetCloudinessFor(Weather.State.Storm)
                > Weather.TargetCloudinessFor(Weather.State.Rain),
                "storm retains a heavier cloud target than rain");
    }

    private static void TestWaterDepthSpace()
    {
        string shader = FileAccess.GetFileAsString("res://shaders/water.gdshader");
        TestAssert.Check(!string.IsNullOrWhiteSpace(shader),
                "water presentation shader is readable by the project");
        TestAssert.Check(shader.Contains(
                "view_depth = -(VIEW_MATRIX * vec4(world_pos, 1.0)).z;",
                StringComparison.Ordinal),
                "water camera depth is reconstructed from world position");
        TestAssert.Check(shader.Contains(
                "world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;",
                StringComparison.Ordinal),
                "water world position includes the displaced vertex");
        TestAssert.Check(!shader.Contains("VIEW_MATRIX * vec4(VERTEX", StringComparison.Ordinal),
                "water depth never transforms chunk-local vertex coordinates as world space");
    }

    private static float Luminance(Color color) =>
            color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
}
