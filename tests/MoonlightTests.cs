using Godot;

namespace Minecraft;

/// <summary>Focused regressions for exposed moonlit surfaces and cloud-obscured stars.</summary>
public static class MoonlightTests
{
    public static void Run()
    {
        TestMoonPaletteAndNightDarkness();
        TestStarVisibility();
        TestSharedShaderChannels();
    }

    private static void TestMoonPaletteAndNightDarkness()
    {
        DayNight.SkyPalette night = DayNight.EvaluatePalette(0.0, 0.05, 0.0);
        DayNight.SkyPalette cloudyNight = DayNight.EvaluatePalette(0.0, 0.95, 0.0);
        DayNight.SkyPalette day = DayNight.EvaluatePalette(0.5, 0.05, 0.0);

        Vector3 expectedMoon = -DayNight.EvaluateSunDirection(0.0);
        TestAssert.Check(night.MoonDirection.DistanceTo(expectedMoon) < 0.0001f,
                "moon direction remains opposite the sun at midnight");
        TestAssert.Check(Mathf.Abs(night.MoonDirection.Length() - 1.0f) < 0.0001f,
                "moon direction stays normalized for shader use");
        TestAssert.Check(night.MoonColor.B > night.MoonColor.R,
                "moonlight keeps a cool blue-white tint");
        TestAssert.Check(night.MoonEnergy > 0.17f && day.MoonEnergy < 0.0001f,
                "the directional moon remains a restrained outdoor key, absent at midday");
        TestAssert.Check(cloudyNight.MoonEnergy < night.MoonEnergy,
                "cloud cover attenuates directional moonlight");
        TestAssert.Check(night.SkyLightEnergy <= 0.066f
                        && night.AmbientLightEnergy <= 0.066f,
                "moon lighting does not lift the low global ambient or baked night floor");

        for (int i = 0; i <= 100; i++)
        {
            double time = i / 100.0;
            Vector3 sun = DayNight.EvaluateSunDirection(time);
            Vector3 moon = DayNight.EvaluateMoonDirection(time);
            TestAssert.Check(moon.DistanceTo(-sun) < 0.0001f
                            && Mathf.Abs(moon.Length() - 1.0f) < 0.0001f,
                    $"moon direction remains unit-length and opposite the sun at {time:F2}");
        }
    }

    private static void TestStarVisibility()
    {
        DayNight.SkyPalette night = DayNight.EvaluatePalette(0.0, 0.05, 0.0);
        DayNight.SkyPalette day = DayNight.EvaluatePalette(0.5, 0.05, 0.0);
        TestAssert.Equal(0.0f, DayNight.EvaluateStarVisibility(day.Night, 0.05),
                "stars remain hidden during daylight");

        float clear = DayNight.EvaluateStarVisibility(night.Night, 0.05);
        float cloudy = DayNight.EvaluateStarVisibility(night.Night, 0.55);
        float rain = DayNight.EvaluateStarVisibility(night.Night, 0.78);
        float storm = DayNight.EvaluateStarVisibility(night.Night, 0.95);
        TestAssert.Check(clear > cloudy && cloudy > rain && rain > storm,
                "cloud and rain cover progressively obscure the existing starfield");
        TestAssert.Check(clear > 0.9f && storm > 0.0f,
                "clear nights retain visible stars while storms leave only a faint trace");

        float previous = 1.0f;
        for (int i = 0; i <= 100; i++)
        {
            double cover = i / 100.0;
            float visibility = DayNight.EvaluateStarVisibility(1.0, cover);
            TestAssert.Check(float.IsFinite(visibility)
                            && visibility >= 0.0f && visibility <= 1.0f,
                    $"star visibility stays finite and bounded at cloud cover {cover:F2}");
            TestAssert.Check(visibility <= previous,
                    $"more cloud cover never reveals more stars at {cover:F2}");
            previous = visibility;
        }
    }

    private static void TestSharedShaderChannels()
    {
        string[] shaders =
        [
            BlockDb.MakeOpaqueMaterial().Shader?.Code ?? string.Empty,
            BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty,
            ChunkMaterials.Glass.Shader?.Code ?? string.Empty,
            ChunkMaterials.Lava.Shader?.Code ?? string.Empty,
            (ChunkMaterials.Water as ShaderMaterial)?.Shader?.Code ?? string.Empty,
        ];
        string[] names = ["atlas", "greedy-array", "glass", "lava", "water"];
        for (int i = 0; i < shaders.Length; i++)
        {
            string shader = shaders[i];
            TestAssert.Check(shader.Contains("uniform vec3 moon_direction")
                            && shader.Contains("uniform vec3 moon_color")
                            && shader.Contains("uniform float moonlight_energy")
                            && shader.Contains("sky_exposure")
                            && shader.Contains("moon_fill"),
                    $"{names[i]} shader gates directional moon fill with baked sky exposure");
        }

        bool previousGreedy = Mesher.Greedy;
        ShaderMaterial atlas;
        ShaderMaterial array;
        try
        {
            Mesher.Greedy = false;
            atlas = (ShaderMaterial)ChunkMaterials.Opaque;
            Mesher.Greedy = true;
            array = (ShaderMaterial)ChunkMaterials.Opaque;
        }
        finally
        {
            Mesher.Greedy = previousGreedy;
        }

        ShaderMaterial[] materials =
        [
            atlas,
            array,
            ChunkMaterials.Glass,
            ChunkMaterials.Lava,
            (ShaderMaterial)ChunkMaterials.Water,
        ];
        Vector3 moonDirection = new Vector3(-0.2f, 0.8f, 0.4f).Normalized();
        Color moonColor = new(0.48f, 0.62f, 1.0f);
        ChunkMaterials.SetSkyLighting(0.37f, Vector3.Up, Colors.White,
                new Color(0.62f, 0.74f, 1.0f), 0.64f,
                moonDirection, moonColor, 0.18f);

        foreach (ShaderMaterial material in materials)
        {
            Vector3 actualDirection = material.GetShaderParameter("moon_direction").AsVector3();
            Vector3 actualColor = material.GetShaderParameter("moon_color").AsVector3();
            float actualEnergy = material.GetShaderParameter("moonlight_energy").AsSingle();
            TestAssert.Check(actualDirection.DistanceTo(moonDirection) < 0.0001f,
                    "shared atlas/greedy/transparent materials receive the normalized moon direction");
            TestAssert.Check(actualColor.DistanceTo(new Vector3(moonColor.R, moonColor.G, moonColor.B)) < 0.0001f,
                    "shared atlas/greedy/transparent materials receive the moon colour");
            TestAssert.Equal(0.18f, actualEnergy,
                    "shared atlas/greedy/transparent materials receive bounded moon energy");
            TestAssert.Equal(0.37f, material.GetShaderParameter("sky_light_energy").AsSingle(),
                    "moon response preserves the low baked-skylight multiplier");
        }

        DayNight.SkyPalette noon = DayNight.EvaluatePalette(0.5, 0.05, 0.0);
        ChunkMaterials.SetSkyLighting(noon.SkyLightEnergy, noon.SunDirection,
                noon.SunColor, noon.SkyLightColor, noon.SunEnergy,
                noon.MoonDirection, noon.MoonColor, noon.MoonEnergy);
    }
}
