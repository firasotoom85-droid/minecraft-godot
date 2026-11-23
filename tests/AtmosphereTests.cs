using System;
using Godot;

namespace Minecraft;

/// <summary>Deterministic regression coverage for sky, weather and water presentation.</summary>
public static class AtmosphereTests
{
    public static void Run()
    {
        TestDayNightPalette();
        TestWeatherSkyCloudPalette();
        TestCaveExposure();
        TestWaterAndBiomeExposure();
        TestWaterFogTransition();
        TestSunlightDirectionAndBakedChannels();
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
        TestAssert.Check(midnight.SkyLightEnergy <= 0.066f
                && midnight.AmbientLightEnergy <= 0.066f,
                "clear midnight keeps baked skylight and global ambient below the restrained night-fill ceiling");
        TestAssert.Check(midnight.MoonEnergy > 0.17f && midnight.MoonEnergy <= 0.18f,
                "night readability retains a directional moon key rather than compensating with global ambient");
        TestAssert.Equal(1.0f, noon.AmbientLightEnergy,
                "night contrast tuning does not reduce clear midday environment fill");
        DayNight.SkyPalette cloudyMidnight = DayNight.EvaluatePalette(0.0, 1.0, 0.0);
        TestAssert.Check(cloudyMidnight.AmbientLightEnergy < midnight.AmbientLightEnergy
                && cloudyMidnight.MoonEnergy < midnight.MoonEnergy,
                "cloud cover attenuates the already restrained night fill and moon key");
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

    private static void TestWeatherSkyCloudPalette()
    {
        Weather.State[] states =
        [
            Weather.State.Clear,
            Weather.State.Cloudy,
            Weather.State.Rain,
            Weather.State.Storm,
        ];
        double previousCloudBrightness = double.MaxValue;
        double previousSkyBrightness = double.MaxValue;
        float previousOpacity = 0.0f;

        foreach (Weather.State state in states)
        {
            double cover = Weather.TargetCloudinessFor(state);
            DayNight.SkyPalette palette = DayNight.EvaluatePalette(0.5, cover, 0.0);
            double cloudBrightness = Luminance(palette.CloudColor);
            double skyBrightness = Luminance(palette.Top);

            TestAssert.Check(double.IsFinite(cloudBrightness)
                    && palette.CloudOpacity >= 0.0f && palette.CloudOpacity <= 1.0f,
                    $"{state} cloud tint and opacity remain finite and bounded");
            TestAssert.Check(palette.CloudOpacity > previousOpacity,
                    $"{state} increases cloud coverage over the previous weather state");
            TestAssert.Check(cloudBrightness < previousCloudBrightness,
                    $"{state} clouds darken progressively with heavier weather");
            TestAssert.Check(skyBrightness < previousSkyBrightness,
                    $"{state} upper sky dims progressively with heavier weather");
            TestAssert.Check(Luminance(palette.Horizon) <= Luminance(
                            DayNight.EvaluatePalette(0.5, 0.05, 0.0).Horizon) + 0.00001,
                    $"{state} horizon is no brighter than clear noon");

            TestAssert.Check(IsFinite(palette.Top) && IsFinite(palette.Horizon)
                    && IsFinite(palette.GroundHorizon) && IsFinite(palette.CloudColor),
                    $"{state} sky, horizon and cloud colours are finite");

            previousCloudBrightness = cloudBrightness;
            previousSkyBrightness = skyBrightness;
            previousOpacity = palette.CloudOpacity;
        }

        DayNight.SkyPalette clearNoon = DayNight.EvaluatePalette(
                0.5, Weather.TargetCloudinessFor(Weather.State.Clear), 0.0);
        DayNight.SkyPalette clearNight = DayNight.EvaluatePalette(
                0.0, Weather.TargetCloudinessFor(Weather.State.Clear), 0.0);
        DayNight.SkyPalette storm = DayNight.EvaluatePalette(
                0.5, Weather.TargetCloudinessFor(Weather.State.Storm), 0.0);
        DayNight.SkyPalette stormFlash = DayNight.EvaluatePalette(
                0.5, Weather.TargetCloudinessFor(Weather.State.Storm), 0.8);

        TestAssert.Check(Luminance(clearNoon.CloudColor) > Luminance(clearNight.CloudColor),
                "clear clouds retain daylight brightness and cool down at night");
        TestAssert.Check(Luminance(stormFlash.CloudColor) > Luminance(storm.CloudColor)
                && Luminance(stormFlash.Horizon) > Luminance(storm.Horizon),
                "lightning briefly lifts storm cloud and horizon brightness");

        DayNight.SkyPalette nonFinite = DayNight.EvaluatePalette(
                double.NaN, double.NaN, double.PositiveInfinity);
        TestAssert.Equal(clearNoon.Top, nonFinite.Top,
                "non-finite time, cloudiness and flash use a stable clear-noon fallback");
        TestAssert.Equal(clearNoon.CloudColor, nonFinite.CloudColor,
                "non-finite weather inputs do not poison cloud material colour");

        foreach (string weatherName in Weather.StateNames)
        {
            TestAssert.Check(AtmosphereVisualCapture.TryParseWeather(
                            weatherName, out Weather.State parsed)
                    && Enum.IsDefined(typeof(Weather.State), parsed),
                    $"visual capture accepts {weatherName} weather");
        }

        TestAssert.Check(AtmosphereVisualCapture.TryParseWeather("sToRm", out Weather.State stormState)
                && stormState == Weather.State.Storm,
                "visual capture weather parsing is case-insensitive and includes storms");
        TestAssert.Check(!AtmosphereVisualCapture.TryParseWeather("snowstorm", out _)
                && !AtmosphereVisualCapture.TryParseWeather("42", out _),
                "visual capture rejects unknown and undefined weather values");
        TestAssert.Equal(8.0, AtmosphereVisualCapture.SettleSecondsForPreset(Gfx.Presets[0]),
                "standard temporal/non-temporal presets use the normal capture warm-up");
        TestAssert.Equal(16.0, AtmosphereVisualCapture.SettleSecondsForPreset(Gfx.Presets[5]),
                "FSR2 gets a longer temporal-history warm-up before visual sampling");
        TestAssert.Check(!AtmosphereVisualCapture.UsesProductionExposure("none", false),
                "silhouette and weather captures keep deterministic fixed exposure");
        TestAssert.Check(AtmosphereVisualCapture.UsesProductionExposure("none", true),
                "cave captures exercise the production adaptive exposure");
        TestAssert.Check(AtmosphereVisualCapture.UsesProductionExposure("water-exit", false)
                        && AtmosphereVisualCapture.UsesProductionExposure("biome-pair", false),
                "water and biome exposure captures exercise the production adaptive exposure");

        DayNight.SkyPalette reference = default;
        for (int presetIndex = 0; presetIndex < Gfx.Presets.Count; presetIndex++)
        {
            Gfx.Preset preset = Gfx.Presets[presetIndex];
            bool lowCost = presetIndex <= 1;
            TestAssert.Check(!lowCost || (!preset.Ssao && !preset.Ssr
                            && !preset.Glow && !preset.VolFog),
                    $"{preset.Name} keeps expensive atmosphere post-effects disabled");
            TestAssert.Check(preset.Scale > 0.0f && preset.Scale <= 1.25f,
                    $"{preset.Name} retains a supported render scale");

            foreach (Weather.State state in states)
            {
                DayNight.SkyPalette palette = DayNight.EvaluatePalette(
                        0.5, Weather.TargetCloudinessFor(state), 0.0);
                TestAssert.Check(IsFinite(palette.Top) && IsFinite(palette.Horizon)
                        && IsFinite(palette.CloudColor)
                        && float.IsFinite(palette.CloudOpacity),
                        $"{preset.Name} / {state} atmosphere remains finite");
                if (presetIndex == 0 && state == Weather.State.Clear)
                {
                    reference = palette;
                }
                else if (state == Weather.State.Clear)
                {
                    TestAssert.Equal(reference.Top, palette.Top,
                            $"{preset.Name} keeps the same clear-sky palette as LOW");
                    TestAssert.Equal(reference.CloudColor, palette.CloudColor,
                            $"{preset.Name} keeps the same clear-cloud tint as LOW");
                }
            }
        }

        Godot.Environment sharedEnvironment = WorldEnvironmentFactory.Build();
        TestAssert.Check(sharedEnvironment.FogEnabled,
                "all quality presets retain the shared inexpensive base fog path");
    }

    private static bool IsFinite(Color color) =>
            float.IsFinite(color.R) && float.IsFinite(color.G)
            && float.IsFinite(color.B) && float.IsFinite(color.A);

    private static void TestCaveExposure()
    {
        float openSky = DayNight.EvaluateCaveDarkness(15.0f);
        float caveEntrance = DayNight.EvaluateCaveDarkness(8.0f);
        float enclosedCave = DayNight.EvaluateCaveDarkness(0.0f);
        TestAssert.Equal(0.0f, openSky,
                "full baked skylight retains the outdoor ambient and fog response");
        TestAssert.Check(caveEntrance > 0.0f && caveEntrance < 1.0f,
                "partial baked skylight blends rather than switching abruptly at a cave mouth");
        TestAssert.Equal(1.0f, enclosedCave,
                "an enclosed cave reaches the full cave-darkness response");
        TestAssert.Equal(1.0f, DayNight.EvaluateCaveDarkness(-4.0f),
                "negative skylight input clamps to fully sheltered");
        TestAssert.Equal(0.0f, DayNight.EvaluateCaveDarkness(20.0f),
                "skylight above the nibble range clamps to outdoor exposure");

        TestAssert.Equal(DayNight.OutdoorAutoExposureMaxSensitivity,
                DayNight.EvaluateAutoExposureMaxSensitivity(openSky),
                "open sky retains the existing outdoor auto-exposure range");
        TestAssert.Equal(DayNight.CaveAutoExposureMaxSensitivity,
                DayNight.EvaluateAutoExposureMaxSensitivity(enclosedCave),
                "zero-skylight caves hold the camera at its minimum sensitivity");
        TestAssert.Equal(DayNight.MinimumAutoExposureSensitivity,
                DayNight.CaveAutoExposureMaxSensitivity,
                "deep cave exposure has no upward camera-sensitivity headroom");
        TestAssert.Check(DayNight.EvaluateAutoExposureMaxSensitivity(caveEntrance)
                        < DayNight.OutdoorAutoExposureMaxSensitivity
                        && DayNight.EvaluateAutoExposureMaxSensitivity(caveEntrance)
                        > DayNight.CaveAutoExposureMaxSensitivity,
                "camera sensitivity follows the partial skylight transition at cave entrances");
        TestAssert.Equal(1.0f, DayNight.EvaluateCaveAmbientScale(openSky),
                "open sky keeps the full environment ambient response");
        TestAssert.Check(Math.Abs(DayNight.EvaluateCaveAmbientScale(enclosedCave)
                        - DayNight.CaveAmbientScale) < 0.00001f,
                "sealed caves retain only the deliberate low ambient floor");
        TestAssert.Check(DayNight.EvaluateCaveAmbientScale(caveEntrance) < 1.0f
                        && DayNight.EvaluateCaveAmbientScale(caveEntrance) > DayNight.CaveAmbientScale,
                "environment ambient fill eases through the cave entrance");
        TestAssert.Check(DayNight.EvaluateAutoExposureMaxSensitivity(0.5f)
                        > DayNight.EvaluateAutoExposureMaxSensitivity(0.75f)
                        && DayNight.EvaluateCaveAmbientScale(0.5f)
                        > DayNight.EvaluateCaveAmbientScale(0.75f),
                "deeper cave adaptation reduces both global exposure headroom and ambient fill");

        float previous = enclosedCave;
        for (int level = 1; level <= VoxelLighting.MaxLevel * 4; level++)
        {
            float exposure = level / 4.0f;
            float darkness = DayNight.EvaluateCaveDarkness(exposure);
            TestAssert.Check(float.IsFinite(darkness) && darkness >= 0.0f && darkness <= 1.0f,
                    $"cave exposure response remains finite and bounded at skylight {exposure:F2}");
            TestAssert.Check(darkness <= previous,
                    $"increasing skylight never increases cave darkness at {exposure:F2}");
            float cameraSensitivity = DayNight.EvaluateAutoExposureMaxSensitivity(darkness);
            float ambientScale = DayNight.EvaluateCaveAmbientScale(darkness);
            TestAssert.Check(float.IsFinite(cameraSensitivity)
                            && cameraSensitivity >= DayNight.CaveAutoExposureMaxSensitivity
                            && cameraSensitivity <= DayNight.OutdoorAutoExposureMaxSensitivity
                            && float.IsFinite(ambientScale)
                            && ambientScale >= DayNight.CaveAmbientScale
                            && ambientScale <= 1.0f,
                    $"camera exposure and ambient fill remain finite and bounded at skylight {exposure:F2}");
            previous = darkness;
        }

        float before = DayNight.EvaluateCaveDarkness(7.5f);
        float after = DayNight.EvaluateCaveDarkness(8.5f);
        TestAssert.Check(System.Math.Abs(before - after) < 0.15f,
                "fractional skylight interpolation changes cave exposure smoothly between adjacent cells");
    }

    private static void TestWaterAndBiomeExposure()
    {
        DayNight.ExposureLimits outdoor = DayNight.EvaluateExposureLimits(0.0f, 0.0f, 0.0f, 0.0f);
        DayNight.ExposureLimits underwater = DayNight.EvaluateExposureLimits(0.0f, 1.0f, 0.0f, 0.0f);
        DayNight.ExposureLimits snow = DayNight.EvaluateExposureLimits(0.0f, 0.0f, 1.0f, 0.0f);
        DayNight.ExposureLimits forest = DayNight.EvaluateExposureLimits(0.0f, 0.0f, 0.0f, 1.0f);
        DayNight.ExposureLimits cave = DayNight.EvaluateExposureLimits(1.0f, 1.0f, 1.0f, 1.0f);

        TestAssert.Equal(DayNight.MinimumAutoExposureSensitivity, outdoor.MinimumSensitivity,
                "outdoor minimum sensitivity retains the established camera baseline");
        TestAssert.Equal(DayNight.OutdoorAutoExposureMaxSensitivity, outdoor.MaximumSensitivity,
                "outdoor maximum sensitivity retains the established camera baseline");
        TestAssert.Equal(DayNight.WaterAutoExposureMinSensitivity, underwater.MinimumSensitivity,
                "full water immersion limits the bright-side sensitivity range");
        TestAssert.Equal(DayNight.WaterAutoExposureMaxSensitivity, underwater.MaximumSensitivity,
                "full water immersion prevents excessive dark-scene exposure lift");
        TestAssert.Equal(DayNight.SnowAutoExposureMinSensitivity, snow.MinimumSensitivity,
                "bright snow permits lower ISO so highlights can settle without washing out");
        TestAssert.Equal(DayNight.SnowAutoExposureMaxSensitivity, snow.MaximumSensitivity,
                "bright snow reduces the upper exposure ceiling");
        TestAssert.Equal(DayNight.MinimumAutoExposureSensitivity, forest.MinimumSensitivity,
                "shaded forest preserves the normal lower exposure boundary");
        TestAssert.Equal(DayNight.ForestAutoExposureMaxSensitivity, forest.MaximumSensitivity,
                "shaded forest permits a modestly higher upper exposure ceiling");
        TestAssert.Equal(DayNight.MinimumAutoExposureSensitivity, cave.MinimumSensitivity,
                "cave shelter overrides water and biome minimum-sensitivity modifiers");
        TestAssert.Equal(DayNight.CaveAutoExposureMaxSensitivity, cave.MaximumSensitivity,
                "cave shelter always wins over bright snow, water and forest exposure modifiers");

        TestAssert.Check(DayNight.EvaluateExposureDarkness(0.0f, 1.0f, 0.0f, 0.0f) > 0.0f,
                "water immersion contributes to the dark-adaptation context");
        TestAssert.Check(DayNight.EvaluateExposureDarkness(0.0f, 0.0f, 0.0f, 1.0f)
                        > DayNight.EvaluateExposureDarkness(0.0f, 0.0f, 0.0f, 0.0f),
                "dense forest is treated as a darker exposure context than open terrain");
        TestAssert.Equal(0.0f, DayNight.EvaluateExposureDarkness(0.0f, 0.0f, 1.0f, 0.0f),
                "bright snow offsets the shaded-scene adaptation bias");
        TestAssert.Check(DayNight.EvaluateAutoExposureSpeed(0.0f, 0.5f)
                        < DayNight.EvaluateAutoExposureSpeed(0.5f, 0.0f),
                "entering a darker scene adapts more slowly than emerging into brightness");
        TestAssert.Equal(DayNight.SteadyAdaptationSpeed,
                DayNight.EvaluateAutoExposureSpeed(0.25f, 0.25f),
                "stable lighting uses the neutral camera adaptation speed");
        TestAssert.Equal(DayNight.SteadyAdaptationSpeed,
                DayNight.EvaluateAutoExposureSpeed(-1.0f, 1.0f),
                "the first camera update starts from the neutral adaptation speed");

        DayNight.BiomeLightingProfile snowProfile =
                DayNight.EvaluateBiomeLightingProfile(Biome.Snow);
        DayNight.BiomeLightingProfile forestProfile =
                DayNight.EvaluateBiomeLightingProfile(Biome.DenseForest);
        DayNight.BiomeLightingProfile taigaProfile =
                DayNight.EvaluateBiomeLightingProfile(Biome.Taiga);
        TestAssert.Equal(1.0f, snowProfile.SnowWeight,
                "snow biome selects the bright-surface exposure profile");
        TestAssert.Equal(0.0f, snowProfile.ForestWeight,
                "snow biome does not inherit the canopy exposure modifier");
        TestAssert.Equal(1.0f, forestProfile.ForestWeight,
                "dense forest selects the shaded-canopy exposure profile");
        TestAssert.Check(taigaProfile.ForestWeight > 0.0f
                        && taigaProfile.ForestWeight < forestProfile.ForestWeight,
                "taiga transitions through a milder forest exposure profile");

        float blend60 = DayNight.EvaluateTransitionBlend(1.0 / 60.0, 2.8f);
        float blend30 = DayNight.EvaluateTransitionBlend(1.0 / 30.0, 2.8f);
        float value60 = 0.0f;
        float value30 = 0.0f;
        for (int frame = 0; frame < 60; frame++)
            value60 = Mathf.Lerp(value60, 1.0f, blend60);
        for (int frame = 0; frame < 30; frame++)
            value30 = Mathf.Lerp(value30, 1.0f, blend30);
        TestAssert.Check(Mathf.Abs(value60 - value30) < 0.0001f,
                "biome presentation transitions are independent of 30/60 FPS frame rate");
        TestAssert.Equal(0.0f, DayNight.EvaluateTransitionBlend(0.0, 2.8f),
                "a paused frame does not advance the biome transition");
        TestAssert.Equal(0.0f, DayNight.EvaluateTransitionBlend(0.1, 0.0f),
                "a disabled transition rate remains stable");
    }

    private static void TestWaterFogTransition()
    {
        Color airColor = new(0.42f, 0.58f, 0.76f);
        const float airDensity = 0.42f;
        DayNight.WaterFogProfile air = DayNight.EvaluateWaterFog(airColor, airDensity, 0.0f);
        DayNight.WaterFogProfile halfway = DayNight.EvaluateWaterFog(airColor, airDensity, 0.5f);
        DayNight.WaterFogProfile water = DayNight.EvaluateWaterFog(airColor, airDensity, 1.0f);

        TestAssert.Equal(airColor.R, air.Color.R, "air fog starts with the live world red channel");
        TestAssert.Equal(airColor.G, air.Color.G, "air fog starts with the live world green channel");
        TestAssert.Equal(airColor.B, air.Color.B, "air fog starts with the live world blue channel");
        TestAssert.Equal(airDensity, air.Density, "air fog starts with the current weather density");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeBegin, air.DepthBegin,
                "air profile retains the full outdoor haze start");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeEnd, air.DepthEnd,
                "air profile retains the full outdoor haze range");

        TestAssert.Check(halfway.Color.R < air.Color.R && halfway.Color.B < air.Color.B,
                "partial immersion blends the live air tint toward underwater blue");
        // Interpolation results are float32, so an endpoint can land one ULP
        // away from the declared constant; compare with the house tolerance.
        TestAssert.Check(Mathf.IsEqualApprox(
                        (airDensity + DayNight.UnderwaterFogDensity) * 0.5f, halfway.Density),
                "partial immersion interpolates fog density without a threshold");
        TestAssert.Check(Mathf.IsEqualApprox(
                        WorldEnvironmentFactory.DistanceHazeBegin * 0.5f, halfway.DepthBegin),
                "partial immersion contracts the depth-fog start smoothly");
        TestAssert.Check(Mathf.IsEqualApprox(
                        (WorldEnvironmentFactory.DistanceHazeEnd + 14.0f) * 0.5f, halfway.DepthEnd),
                "partial immersion contracts the depth-fog end smoothly");

        TestAssert.Check(Mathf.IsEqualApprox(0.018f, water.Color.R),
                "full immersion reaches the underwater red tint");
        TestAssert.Check(Mathf.IsEqualApprox(0.105f, water.Color.G),
                "full immersion reaches the underwater green tint");
        TestAssert.Check(Mathf.IsEqualApprox(0.205f, water.Color.B),
                "full immersion reaches the underwater blue tint");
        TestAssert.Check(Mathf.IsEqualApprox(DayNight.UnderwaterFogDensity, water.Density),
                "full immersion reaches the underwater density");
        TestAssert.Check(Mathf.IsEqualApprox(0.0f, water.DepthBegin),
                "full immersion begins depth fog at the camera");
        TestAssert.Check(Mathf.IsEqualApprox(14.0f, water.DepthEnd),
                "full immersion reaches the close underwater depth range");

        DayNight.WaterFogProfile clampedLow = DayNight.EvaluateWaterFog(airColor, airDensity, -1.0f);
        DayNight.WaterFogProfile clampedHigh = DayNight.EvaluateWaterFog(airColor, airDensity, 2.0f);
        DayNight.WaterFogProfile invalid = DayNight.EvaluateWaterFog(airColor, airDensity, float.NaN);
        TestAssert.Equal(air.Density, clampedLow.Density, "negative immersion clamps to the air profile");
        TestAssert.Equal(water.Density, clampedHigh.Density, "over-range immersion clamps to the water profile");
        TestAssert.Equal(air.Density, invalid.Density, "non-finite immersion safely falls back to air");

        float blend60 = DayNight.EvaluateTransitionBlend(1.0 / 60.0, 2.2f);
        float blend30 = DayNight.EvaluateTransitionBlend(1.0 / 30.0, 2.2f);
        float value60 = 0.0f;
        float value30 = 0.0f;
        for (int frame = 0; frame < 60; frame++)
            value60 = Mathf.Lerp(value60, 1.0f, blend60);
        for (int frame = 0; frame < 30; frame++)
            value30 = Mathf.Lerp(value30, 1.0f, blend30);
        TestAssert.Check(Mathf.Abs(value60 - value30) < 0.0001f,
                "underwater fog target interpolation remains frame-rate independent");
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
                WorldEnvironmentFactory.FogModeFor(true, underground: false),
                "outdoor V8+ views use depth haze");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential,
                WorldEnvironmentFactory.FogModeFor(false, underground: false),
                "legacy outdoor worlds keep exponential fog");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential,
                WorldEnvironmentFactory.FogModeFor(true, underground: true),
                "cave darkness retains close-range exponential fog");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Depth,
                WorldEnvironmentFactory.FogModeFor(true, underground: false),
                "modern underwater transitions retain depth fog while its range contracts");

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
        float previousCloudiness = 0.0f;
        foreach (Weather.State state in Enum.GetValues<Weather.State>())
        {
            float target = Weather.TargetCloudinessFor(state);
            TestAssert.Check(target > previousCloudiness && target <= 1.0f,
                    $"{state} has an ordered, bounded cloudiness target");
            previousCloudiness = target;
        }
        TestAssert.Equal(0.05f, Weather.TargetCloudinessFor(Weather.State.Clear),
                "clear weather keeps its configured low cloud target");
        TestAssert.Equal(0.55f, Weather.TargetCloudinessFor(Weather.State.Cloudy),
                "cloudy weather keeps its configured mid cloud target");
        TestAssert.Equal(0.78f, Weather.TargetCloudinessFor(Weather.State.Rain),
                "rain keeps its configured heavy cloud target");
        TestAssert.Equal(0.95f, Weather.TargetCloudinessFor(Weather.State.Storm),
                "storm keeps its configured maximum cloud target");
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
