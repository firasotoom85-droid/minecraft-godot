using System;
using Godot;

namespace Minecraft;

/// <summary>Deterministic regression coverage for sky, weather and water presentation.</summary>
public static class AtmosphereTests
{
    public static void Run()
    {
        TestDayNightPalette();
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
