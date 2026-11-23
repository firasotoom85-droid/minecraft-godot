using Godot;

namespace Minecraft;

public static class GraphicsTests
{
    private static void TestEnvironmentPresetMatrix()
    {
        for (int i = 0; i < Gfx.Presets.Count; i++)
        {
            Gfx.Preset preset = Gfx.Presets[i];
            Godot.Environment environment = WorldEnvironmentFactory.Build(WorldGenV11.Version, i);
            TestAssert.Check(environment.SsaoEnabled == preset.Ssao,
                    $"{preset.Name} environment SSAO matches its renderer preset");
            TestAssert.Check(environment.SsrEnabled == preset.Ssr,
                    $"{preset.Name} environment SSR matches its renderer preset");
            TestAssert.Check(environment.GlowEnabled == preset.Glow,
                    $"{preset.Name} environment glow matches its renderer preset");
            TestAssert.Check(environment.VolumetricFogEnabled == preset.VolFog,
                    $"{preset.Name} environment volumetric fog matches its renderer preset");
            TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeDensity, environment.FogDensity,
                    $"{preset.Name} preserves the modern world's depth-haze density");
        }

        Godot.Environment legacyLow = WorldEnvironmentFactory.Build(WorldGenV7.Version, Gfx.DefaultPreset);
        TestAssert.Equal(Godot.Environment.FogModeEnum.Exponential, legacyLow.FogMode,
                "preset selection does not change legacy world fog mode");
        TestAssert.Equal(WorldEnvironmentFactory.LegacyAirFogDensity, legacyLow.FogDensity,
                "preset selection preserves legacy world fog density");

        Godot.Environment modernLow = WorldEnvironmentFactory.Build(WorldGenV11.Version, Gfx.DefaultPreset);
        TestAssert.Check(!modernLow.SsaoEnabled && !modernLow.SsrEnabled
                        && !modernLow.GlowEnabled && !modernLow.VolumetricFogEnabled,
                "LOW environment disables all four optional post effects at construction");
        TestAssert.Equal(WorldEnvironmentFactory.DistanceHazeDensity, modernLow.FogDensity,
                "LOW keeps the modern depth-haze profile");

        Godot.Environment defaultEnvironment = WorldEnvironmentFactory.Build();
        TestAssert.Check(defaultEnvironment.SsaoEnabled && defaultEnvironment.SsrEnabled
                        && defaultEnvironment.GlowEnabled && defaultEnvironment.VolumetricFogEnabled,
                "factory calls without a preset preserve historical high-quality defaults");
    }

    public static void Run()
    {
        TestEnvironmentPresetMatrix();
        TestAssert.Equal(6, Gfx.Presets.Count, "graphics preset list includes low through max fsr2");
        TestAssert.Equal(0, Gfx.DefaultPreset, "low is the hardware-friendly default");
        TestAssert.Equal("LOW", Gfx.Presets[Gfx.DefaultPreset].Name, "default preset name is low");
        TestAssert.Equal(5, Gfx.Presets[Gfx.DefaultPreset].View, "default view distance matches low preset");

        Gfx.Preset low = Gfx.Presets[0];
        TestAssert.Equal(45.0f, low.ShadowDistance, "low preset keeps near shadows");
        TestAssert.Equal(1024, low.ShadowSize, "low preset uses a 1k shadow atlas");
        TestAssert.Check(!low.Ssao && !low.Ssr && !low.Glow && !low.VolFog,
                "low preset disables expensive screen-space and volumetric effects");
        TestAssert.Equal(0.80f, low.Scale, "low preset renders below native resolution");

        Gfx.Preset balanced = Gfx.Presets[1];
        TestAssert.Equal("BALANCED", balanced.Name, "balanced preset is explicit");
        TestAssert.Equal(7, balanced.View, "balanced view distance");
        TestAssert.Equal(65.0f, balanced.ShadowDistance, "balanced shadow distance");
        TestAssert.Equal(0.90f, balanced.Scale, "balanced render scale");
        TestAssert.Check(!balanced.Ssao && !balanced.Ssr && !balanced.VolFog,
                "balanced avoids expensive screen-space and volumetric effects");

        TestAssert.Equal(RenderingServer.ShadowQuality.SoftLow, low.ShadowFilterQuality,
                "LOW uses the lowest soft directional filter instead of the old global SoftHigh");
        TestAssert.Equal(0.0f, low.SunAngularDistance,
                "LOW avoids directional PCSS to keep the MX230 path practical");
        TestAssert.Check(low.ShadowBlur > 0.0f,
                "LOW retains a small filtered shadow blur without PCSS");
        TestAssert.Equal(RenderingServer.ShadowQuality.SoftLow,
                balanced.ShadowFilterQuality, "BALANCED also avoids expensive high-sample filtering");
        TestAssert.Equal(0.0f, balanced.SunAngularDistance,
                "BALANCED does not pay the directional PCSS cost");

        Gfx.Preset high = Gfx.Presets[2];
        TestAssert.Equal(RenderingServer.ShadowQuality.SoftMedium, high.ShadowFilterQuality,
                "HIGH raises directional shadow filtering one tier");
        TestAssert.Check(high.SunAngularDistance > 0.0f && high.SunAngularDistance < 0.5f,
                "HIGH introduces restrained PCSS softness below the full solar angular size");
        TestAssert.Check(high.Ssao && high.SsaoRadius < 1.2f && high.SsaoIntensity > 1.25f,
                "HIGH tightens SSAO radius and slightly strengthens contact shading");
        TestAssert.Equal(RenderingServer.ShadowQuality.SoftHigh,
                Gfx.Presets[3].ShadowFilterQuality, "ULTRA retains high-quality directional filtering");
        TestAssert.Equal(RenderingServer.ShadowQuality.SoftUltra,
                Gfx.Presets[4].ShadowFilterQuality, "MAX reserves ultra filtering for high-end hardware");
        TestAssert.Equal(Gfx.Presets[4].ShadowFilterQuality,
                Gfx.Presets[5].ShadowFilterQuality, "MAX FSR2 retains the same shadow profile");

        DirectionalLight3D profileLight = new();
        Godot.Environment profileEnvironment = WorldEnvironmentFactory.Build();
        try
        {
            Gfx.ApplyShadowProfile(low, profileLight);
            TestAssert.Equal(low.SunAngularDistance, profileLight.LightAngularDistance,
                    "applying LOW updates the existing directional light angular size");
            TestAssert.Equal(low.ShadowBlur, profileLight.ShadowBlur,
                    "applying LOW updates the existing directional light blur");
            Gfx.ApplyContactShadingProfile(low, profileEnvironment);
            TestAssert.Check(!profileEnvironment.SsaoEnabled,
                    "LOW disables screen-space AO while retaining the prebuilt environment");

            Gfx.ApplyShadowProfile(high, profileLight);
            TestAssert.Equal(high.SunAngularDistance, profileLight.LightAngularDistance,
                    "switching to HIGH applies its angular softness to the existing sun");
            TestAssert.Equal(high.ShadowBlur, profileLight.ShadowBlur,
                    "switching to HIGH applies its blur to the existing sun");
            Gfx.ApplyContactShadingProfile(high, profileEnvironment);
            TestAssert.Check(profileEnvironment.SsaoEnabled,
                    "HIGH enables screen-space contact AO");
            TestAssert.Equal(high.SsaoRadius, profileEnvironment.SsaoRadius,
                    "HIGH uses its tighter contact AO radius");
            TestAssert.Equal(high.SsaoIntensity, profileEnvironment.SsaoIntensity,
                    "HIGH applies its contact AO intensity");
            TestAssert.Equal(0.0f, profileEnvironment.SsaoLightAffect,
                    "contact AO stays out of direct light instead of dirtying lit surfaces");
            TestAssert.Equal(0.0f, profileEnvironment.SsaoAOChannelAffect,
                    "screen-space AO does not double-darken already baked AO channels");
        }
        finally
        {
            profileLight.Free();
        }

        Godot.Environment defaultEnvironment = WorldEnvironmentFactory.Build();
        TestAssert.Equal(WorldEnvironmentFactory.ContactSsaoRadius, defaultEnvironment.SsaoRadius,
                "modern world environments use the shared local-contact AO radius");
        TestAssert.Equal(WorldEnvironmentFactory.ContactSsaoIntensity, defaultEnvironment.SsaoIntensity,
                "modern world environments use the shared restrained contact AO intensity");

        // Version-1 user settings used LOW/HIGH/ULTRA/MAX/MAX FSR2 indexes.
        TestAssert.Equal(0, Settings.MigrateGraphicsPreset(0, 1), "legacy low index is stable");
        TestAssert.Equal(2, Settings.MigrateGraphicsPreset(1, 1), "legacy high index remains high");
        TestAssert.Equal(3, Settings.MigrateGraphicsPreset(2, 1), "legacy ultra index remains ultra");
        TestAssert.Equal(4, Settings.MigrateGraphicsPreset(3, 1), "legacy max index remains max");
        TestAssert.Equal(5, Settings.MigrateGraphicsPreset(4, 1), "legacy max fsr2 index remains max fsr2");
        TestAssert.Equal(Gfx.DefaultPreset, Settings.MigrateGraphicsPreset(99, 1),
                "invalid legacy preset falls back to the safe default");

        for (int i = 0; i < Gfx.Presets.Count; i++)
        {
            TestAssert.Equal(i, Settings.MigrateGraphicsPreset(i, Settings.GraphicsPresetVersion),
                    $"current graphics preset index {i} is unchanged");
        }

        TestAssert.Equal(0, Settings.MigrateGraphicsPreset(-1, Settings.GraphicsPresetVersion),
                "negative current preset clamps to low");
        TestAssert.Equal(Gfx.Presets.Count - 1,
                Settings.MigrateGraphicsPreset(99, Settings.GraphicsPresetVersion),
                "current preset clamps to the last known preset");

        TestAssert.Equal(9, Settings.MigrateGraphicsViewDistance(8, 1, 1),
                "legacy HIGH default view distance follows the expanded HIGH preset");
        TestAssert.Equal(5, Settings.MigrateGraphicsViewDistance(5, 0, 1),
                "legacy LOW view distance remains low");
        TestAssert.Equal(11, Settings.MigrateGraphicsViewDistance(11, 3, 1),
                "custom legacy view distance is preserved");
        TestAssert.Equal(8, Settings.MigrateGraphicsViewDistance(8, 1, 2),
                "versioned current view distance is not remapped");

        VoxelWorld renderOnlyWorld = new() { BuildCollision = false };
        Chunk renderOnlyChunk = new();
        VoxelWorld gameplayWorld = new();
        Chunk gameplayChunk = new();
        try
        {
            renderOnlyChunk.Setup(renderOnlyWorld, Vector2I.Zero);
            gameplayChunk.Setup(gameplayWorld, Vector2I.Zero);
            TestAssert.Check(renderOnlyChunk.GetNodeOrNull<CollisionShape3D>("Collision") == null,
                    "render-only preview chunk does not create a collision shape");
            TestAssert.Equal(0u, renderOnlyChunk.CollisionLayer,
                    "render-only preview chunk has no physics collision layer");
            TestAssert.Check(gameplayChunk.GetNodeOrNull<CollisionShape3D>("Collision") != null,
                    "gameplay chunks retain their collision shape");
            TestAssert.Equal((uint)PhysicsLayer.Terrain, gameplayChunk.CollisionLayer,
                    "gameplay chunks retain the terrain collision layer");
        }
        finally
        {
            TestAssert.FreeAll(renderOnlyChunk, renderOnlyWorld, gameplayChunk, gameplayWorld);
        }
    }
}
