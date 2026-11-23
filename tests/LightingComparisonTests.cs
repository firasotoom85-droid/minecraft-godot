using System.Collections.Generic;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Guards the repeatable scenario resources and the shared low-cost renderer
/// contract. GPU output is checked by running each real-scene capture.
/// </summary>
public static class LightingComparisonTests
{
    public static void Run()
    {
        TestAssert.Equal(6, LightingComparisonProfiles.All.Count,
                "lighting comparison suite exposes six deterministic environments");
        TestAssert.Equal(1280, LightingComparisonProfiles.CaptureWidth,
                "all lighting comparisons use a fixed 1280-pixel width");
        TestAssert.Equal(720, LightingComparisonProfiles.CaptureHeight,
                "all lighting comparisons use a fixed 720-pixel height");
        TestAssert.Equal(3, LightingComparisonProfiles.ReferenceViewDistance,
                "all lighting comparisons use the same bounded chunk streaming radius");
        TestAssert.Equal(2, LightingComparisonProfiles.ChunkBuildRadius,
                "all lighting comparisons build the same 5x5 preview chunk footprint");
        TestAssert.Equal(180, LightingComparisonProfiles.WarmupFrames,
                "all lighting comparisons wait for the same render warm-up");

        HashSet<string> ids = [];
        foreach (LightingComparisonProfiles.Profile profile in LightingComparisonProfiles.All)
        {
            TestAssert.Check(ids.Add(profile.Id),
                    $"lighting profile id '{profile.Id}' is unique");
            TestAssert.Check(profile.TimeOfDay >= 0.0 && profile.TimeOfDay < 1.0,
                    $"{profile.DisplayName} uses a valid fixed time of day");
            TestAssert.Check(profile.FieldOfView is >= 45.0f and <= 90.0f,
                    $"{profile.DisplayName} uses a controlled camera field of view");
            TestAssert.Check(profile.CameraOffset.LengthSquared() > 0.0f,
                    $"{profile.DisplayName} camera is offset from its look target");

            if (profile.ReferenceScreenshot is { } reference)
            {
                string referencePath = ProjectSettings.GlobalizePath(reference);
                TestAssert.Check(File.Exists(referencePath),
                        $"{profile.DisplayName} has a committed screenshot reference");
                Image referenceImage = Image.LoadFromFile(referencePath);
                TestAssert.Check(!referenceImage.IsEmpty(),
                        $"{profile.DisplayName} reference screenshot can be decoded");
                if (!referenceImage.IsEmpty())
                {
                    TestAssert.Equal(LightingComparisonProfiles.CaptureWidth, referenceImage.GetWidth(),
                            $"{profile.DisplayName} reference screenshot has the fixed width");
                    TestAssert.Equal(LightingComparisonProfiles.CaptureHeight, referenceImage.GetHeight(),
                            $"{profile.DisplayName} reference screenshot has the fixed height");
                }
            }
            else
            {
                // Only an explicitly new, unphotographed fixture may skip the
                // audit; an established scene must never silently lose its
                // regression reference.
                TestAssert.Equal("pale-surfaces", profile.Id,
                        $"{profile.DisplayName} is the only profile captured without a reference");
            }

            TestAssert.Check(LightingComparisonProfiles.TryGet(profile.Id, out _),
                    $"{profile.DisplayName} can be selected by its stable id");
            PackedScene scene = GD.Load<PackedScene>($"res://tests/{profile.SceneFile}")
                    ?? throw new System.InvalidOperationException(
                            $"Could not load comparison scene {profile.SceneFile}.");
            Node root = scene.Instantiate();
            TestAssert.Check(root is LightingComparisonCapture,
                    $"{profile.SceneFile} uses the shared real-scene capture runner");
            if (root is LightingComparisonCapture capture)
                TestAssert.Equal(profile.Id, capture.Scenario,
                        $"{profile.SceneFile} selects its matching scenario profile");
            TestAssert.Equal(0, root.GetChildCount(),
                    $"{profile.SceneFile} adds no fixture-specific light or emitter nodes");
            TestAssert.FreeAll(root);
        }

        TestAssert.Check(!LightingComparisonProfiles.TryGet("not-a-scenario", out _),
                "unknown lighting scenarios are rejected rather than mapped to a different fixture");

        TestAssert.Check(LightingComparisonProfiles.TryGet("pale-surfaces", out var paleProfile)
                        && paleProfile.SceneFile == "PaleSurfaceLighting.tscn"
                        && paleProfile.TimeOfDay == 0.50,
                "pale-material comparison uses the fixed clear-noon profile");

        Gfx.Preset low = Gfx.Presets[Gfx.DefaultPreset];
        TestAssert.Equal("LOW", low.Name,
                "comparison captures default to the production MX230-friendly preset");
        TestAssert.Check(!low.Ssao && !low.Ssr && !low.Glow && !low.VolFog,
                "the low-end comparison path leaves expensive screen-space and volumetric effects disabled");

        Godot.Environment environment = WorldEnvironmentFactory.Build();
        TestAssert.Equal(Godot.Environment.ToneMapper.Filmic, environment.TonemapMode,
                "comparison scenes inherit the shared Filmic world response");
        TestAssert.Equal(1.0f, environment.AmbientLightEnergy,
                "comparison scenes preserve neutral shared sky ambient");
        TestAssert.Equal(Godot.Environment.FogModeEnum.Depth, environment.FogMode,
                "current-generation comparison scenes retain camera-depth distance haze");

        Color channels = VoxelLighting.VertexColor(VoxelLighting.Pack(5, 7), 0.75f);
        TestAssert.Equal(5.0f / 15.0f, channels.R,
                "the scene probe keeps direct skylight in the shader red channel");
        TestAssert.Equal(7.0f / 15.0f, channels.G,
                "the scene probe keeps baked warm block light in the shader green channel");
        TestAssert.Equal(0.75f, channels.B,
                "the scene probe keeps ambient occlusion/face shade in the shader blue channel");
        float channelTag = MaterialResponse.VertexTag(SurfaceFamily.Stone, false,
                VoxelLighting.SkyBounce(11, 5) / 15.0f);
        TestAssert.Equal(6.0f / 15.0f,
                (channelTag - Mathf.Floor(channelTag + 0.001f)) * 16.0f,
                "the comparison mesh retains indirect sky spill separately from direct sun");
        TestAssert.Equal(11.0f, VoxelLighting.MaxLevel
                * (channels.R + (channelTag - Mathf.Floor(channelTag + 0.001f)) * 16.0f),
                "direct plus indirect sky preserves the combined probe level");
    }
}
