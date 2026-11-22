using System;
using Godot;

namespace Minecraft;

/// <summary>Deterministic regression coverage for sky, weather and water presentation.</summary>
public static class AtmosphereTests
{
    public static void Run()
    {
        TestDayNightPalette();
        TestSunlightDirectionAndBakedChannels();
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

    private static void TestSunlightDirectionAndBakedChannels()
    {
        Vector3 dawnDirection = DayNight.EvaluateSunDirection(0.25);
        Vector3 noonDirection = DayNight.EvaluateSunDirection(0.50);
        Vector3 sunsetDirection = DayNight.EvaluateSunDirection(0.75);
        Vector3 nightDirection = DayNight.EvaluateSunDirection(0.00);

        TestAssert.Check(dawnDirection.X < 0.0f && dawnDirection.Z > 0.0f
                        && Mathf.Abs(dawnDirection.Y) < 0.0001f,
                "dawn sun direction rises from the eastern horizon");
        TestAssert.Check(noonDirection.DistanceTo(Vector3.Up) < 0.0001f,
                "noon sun direction is overhead");
        TestAssert.Check(sunsetDirection.X > 0.0f && sunsetDirection.Z < 0.0f
                        && Mathf.Abs(sunsetDirection.Y) < 0.0001f,
                "sunset sun direction crosses the western horizon");
        TestAssert.Check(nightDirection.DistanceTo(Vector3.Down) < 0.0001f,
                "midnight sun direction is below the horizon");
        TestAssert.Check(Mathf.Abs(dawnDirection.Length() - 1.0f) < 0.0001f
                        && Mathf.Abs(noonDirection.Length() - 1.0f) < 0.0001f
                        && Mathf.Abs(sunsetDirection.Length() - 1.0f) < 0.0001f
                        && Mathf.Abs(nightDirection.Length() - 1.0f) < 0.0001f,
                "sun direction stays normalized at all four day phases");
        TestAssert.Check(DayNight.EvaluateSunDirection(1.5).DistanceTo(noonDirection) < 0.0001f
                        && DayNight.EvaluateSunDirection(-0.75).DistanceTo(dawnDirection) < 0.0001f,
                "sun direction wraps consistently for scrubbed and negative clock values");

        DayNight.SkyPalette dawn = DayNight.EvaluatePalette(0.25, 0.0, 0.0);
        DayNight.SkyPalette noon = DayNight.EvaluatePalette(0.50, 0.0, 0.0);
        DayNight.SkyPalette sunset = DayNight.EvaluatePalette(0.75, 0.0, 0.0);
        DayNight.SkyPalette midnight = DayNight.EvaluatePalette(0.00, 0.0, 0.0);
        DayNight.SkyPalette overcast = DayNight.EvaluatePalette(0.25, 1.0, 0.0);

        TestAssert.Check(dawn.SunColor.R > dawn.SunColor.G
                        && dawn.SunColor.G > dawn.SunColor.B,
                "dawn directional sunlight is warm amber");
        TestAssert.Check(sunset.SunColor.R > sunset.SunColor.G
                        && sunset.SunColor.G > sunset.SunColor.B,
                "sunset directional sunlight is warmer than the noon baseline");
        TestAssert.Check((dawn.SunColor.R - dawn.SunColor.B)
                        > (noon.SunColor.R - noon.SunColor.B),
                "low-angle sunlight is warmer than noon sunlight");
        TestAssert.Check(midnight.SkyLightColor.B > midnight.SkyLightColor.R,
                "night baked skylight retains a cool blue fill");
        TestAssert.Check(overcast.SunEnergy < dawn.SunEnergy,
                "overcast attenuates the directional sunlight contribution");

        Vector3 previous = DayNight.EvaluateSunDirection(0.0);
        for (int i = 1; i <= 100; i++)
        {
            Vector3 current = DayNight.EvaluateSunDirection(i / 100.0);
            TestAssert.Check(current.DistanceTo(previous) < 0.07f,
                    $"sun direction remains continuous across the day at {i / 100.0:F2}");
            previous = current;
        }

        string[] shaderSources =
        [
            BlockDb.MakeOpaqueMaterial().Shader?.Code ?? string.Empty,
            BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty,
            ChunkMaterials.Glass.Shader?.Code ?? string.Empty,
            ChunkMaterials.Lava.Shader?.Code ?? string.Empty,
            (ChunkMaterials.Water as ShaderMaterial)?.Shader?.Code ?? string.Empty,
        ];
        string[] shaderNames = ["atlas", "texture-array", "glass", "lava", "water"];
        for (int i = 0; i < shaderSources.Length; i++)
        {
            TestAssert.Check(shaderSources[i].Contains("uniform vec3 sun_direction")
                            && shaderSources[i].Contains("uniform vec3 sun_color")
                            && shaderSources[i].Contains("uniform vec3 sky_light_color")
                            && shaderSources[i].Contains("uniform float sunlight_energy")
                            && shaderSources[i].Contains("directional_sky_light"),
                    $"{shaderNames[i]} shader consumes the shared directional baked-sky channels");
        }

        bool originalGreedy = Mesher.Greedy;
        ShaderMaterial atlasOpaque;
        ShaderMaterial arrayOpaque;
        try
        {
            Mesher.Greedy = false;
            atlasOpaque = (ShaderMaterial)ChunkMaterials.Opaque;
            Mesher.Greedy = true;
            arrayOpaque = (ShaderMaterial)ChunkMaterials.Opaque;
        }
        finally
        {
            Mesher.Greedy = originalGreedy;
        }

        Vector3 testDirection = new Vector3(1.0f, 0.5f, -0.25f).Normalized();
        Color testSun = new(1.0f, 0.45f, 0.25f);
        Color testSky = new(0.62f, 0.74f, 1.0f);
        ChunkMaterials.SetSkyLighting(0.37f, testDirection, testSun, testSky, 0.64f);

        ShaderMaterial[] sharedMaterials =
        [
            atlasOpaque,
            arrayOpaque,
            ChunkMaterials.Glass,
            ChunkMaterials.Lava,
            (ShaderMaterial)ChunkMaterials.Water,
        ];
        foreach (ShaderMaterial material in sharedMaterials)
        {
            Vector3 actualDirection = material.GetShaderParameter("sun_direction").AsVector3();
            Vector3 actualSun = material.GetShaderParameter("sun_color").AsVector3();
            Vector3 actualSky = material.GetShaderParameter("sky_light_color").AsVector3();
            TestAssert.Check(actualDirection.DistanceTo(testDirection) < 0.0001f,
                    "existing shared chunk materials receive the current normalized sun direction");
            TestAssert.Equal(new Vector3(testSun.R, testSun.G, testSun.B), actualSun,
                    "existing shared chunk materials receive the current sun colour");
            TestAssert.Equal(new Vector3(testSky.R, testSky.G, testSky.B), actualSky,
                    "existing shared chunk materials receive the current sky-fill colour");
            TestAssert.Equal(0.37f, material.GetShaderParameter("sky_light_energy").AsSingle(),
                    "shared material updates retain the dynamic baked-sky energy scalar");
        }

        ChunkMaterials.SetSkyLighting(noon.SkyLightEnergy, noon.SunDirection,
                noon.SunColor, noon.SkyLightColor, noon.SunEnergy);
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
