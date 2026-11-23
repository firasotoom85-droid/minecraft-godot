using System;
using System.Collections.Generic;
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
        TestAssert.Equal(5, LightingComparisonProfiles.All.Count,
                "lighting comparison suite exposes all five requested environments");
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

        byte packed = VoxelLighting.Pack(11, 7);
        Color channels = VoxelLighting.VertexColor(packed, 0.75f);
        TestAssert.Equal(11.0f / 15.0f, channels.R,
                "the scene probe keeps baked skylight in the shader red channel");
        TestAssert.Equal(7.0f / 15.0f, channels.G,
                "the scene probe keeps baked warm block light in the shader green channel");
        TestAssert.Equal(0.75f, channels.B,
                "the scene probe keeps ambient occlusion/face shade in the shader blue channel");

        TestSpatialProbeGrid();
        TestProbeDriftMetrics();
        TestPartialReferenceCoverage();
        TestHistoricalReferenceAnchors();
    }

    private static void TestSpatialProbeGrid()
    {
        IReadOnlyList<Vector3I> offsets = LightingProbeComparison.GridOffsets;
        TestAssert.Equal(27, offsets.Count,
                "spatial comparison samples a bounded 3x3x3 voxel grid");
        bool containsCentre = false;
        HashSet<string> ids = [];
        foreach (Vector3I offset in offsets)
        {
            containsCentre |= offset == Vector3I.Zero;
            TestAssert.Check(ids.Add(LightingProbeComparison.IdForOffset(offset)),
                    "every spatial probe offset has a unique stable id");
        }
        TestAssert.Check(containsCentre,
                "spatial comparison grid includes its historical centre anchor");
        TestAssert.Equal("center", LightingProbeComparison.IdForOffset(Vector3I.Zero),
                "the zero offset maps to the stable reference id");
        TestAssert.Equal("x-2_y0_z2",
                LightingProbeComparison.IdForOffset(new Vector3I(-2, 0, 2)),
                "spatial probe ids encode their signed voxel offsets deterministically");
    }

    private static void TestProbeDriftMetrics()
    {
        LightingProbeComparison.ReferenceSample[] reference =
        [
            new("center", 8, 7),
            new("east", 6, 3),
            new("north", 4, 2),
        ];
        LightingProbeComparison.Sample[] captured =
        [
            new("center", 0, 30, 0, 10, 7, "Stone"),
            new("east", 2, 30, 0, 5, 4, "Air"),
            new("orphan", 4, 30, 0, 1, 0, "Air"),
        ];

        LightingProbeComparison.Drift drift = LightingProbeComparison.Compare(
                captured, reference, 1.0f / 15.0f);
        TestAssert.Equal(3, drift.CapturedSamples,
                "drift report retains all captured grid samples");
        TestAssert.Equal(3, drift.ReferenceSamples,
                "drift report retains the complete reference sample count");
        TestAssert.Equal(2, drift.ComparedSamples,
                "drift metrics compare matching stable probe ids only");
        TestAssert.Equal(1, drift.UnreferencedSamples,
                "new grid points without a baseline are counted rather than treated as zero");
        TestAssert.Equal(1, drift.MissingSamples,
                "reference points absent from a capture are reported as missing");
        TestAssert.Check(Math.Abs(drift.ReferenceCoverage - (2.0 / 3.0)) < 0.000001,
                "reference coverage reports the compared fraction");
        TestAssert.Check(Math.Abs(drift.MeanAbsoluteChannelDrift - 1.0) < 0.000001,
                "mean channel drift is averaged in 4-bit light levels");
        TestAssert.Check(Math.Abs(drift.MaxChannelDrift - 2.0) < 0.000001,
                "maximum channel drift exposes the worst nibble difference");
        TestAssert.Check(Math.Abs(drift.MeanAbsoluteSkyDrift - 1.5) < 0.000001
                && Math.Abs(drift.MaxSkyDrift - 2.0) < 0.000001,
                "skylight mean and maximum drift are reported independently");
        TestAssert.Check(Math.Abs(drift.MeanAbsoluteBlockDrift - 0.5) < 0.000001
                && Math.Abs(drift.MaxBlockDrift - 1.0) < 0.000001,
                "block-light mean and maximum drift are reported independently");
        TestAssert.Check(Math.Abs(drift.MeanAbsoluteRedDrift - (1.5 / 15.0)) < 0.000001
                && Math.Abs(drift.MeanAbsoluteGreenDrift - (0.5 / 15.0)) < 0.000001
                && drift.MaxBlueDrift == 0.0,
                "red, green and neutral-blue shader input drift are reported per channel");
        TestAssert.Check(Math.Abs(drift.MaxColorDrift - (2.0 / 15.0)) < 0.000001,
                "RGB drift uses the actual packed shader vertex channels");
        TestAssert.Check(drift.OutsideToleranceSamples == 1,
                "one-level nibble changes remain within tolerance and larger drift is flagged");
        TestAssert.Check(drift.MaxIntensityDrift > 0.0
                && drift.MeanAbsoluteIntensityDrift > 0.0,
                "drift report includes luminance-weighted intensity differences");

        bool duplicateRejected = false;
        try
        {
            LightingProbeComparison.Compare(
                    [captured[0], captured[0]], reference, 0.1f);
        }
        catch (ArgumentException)
        {
            duplicateRejected = true;
        }
        TestAssert.Check(duplicateRejected,
                "duplicate captured probe ids are rejected instead of double-counted");

        bool invalidToleranceRejected = false;
        try
        {
            LightingProbeComparison.Compare(captured, reference, float.NaN);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidToleranceRejected = true;
        }
        TestAssert.Check(invalidToleranceRejected,
                "non-finite comparison tolerances are rejected");
    }

    private static void TestPartialReferenceCoverage()
    {
        List<LightingProbeComparison.Sample> captured = new(27);
        foreach (Vector3I offset in LightingProbeComparison.GridOffsets)
        {
            captured.Add(new LightingProbeComparison.Sample(
                    LightingProbeComparison.IdForOffset(offset),
                    offset.X, offset.Y, offset.Z, 8, 4, "Air"));
        }

        LightingProbeComparison.Drift drift = LightingProbeComparison.Compare(
                captured, [new LightingProbeComparison.ReferenceSample("center", 8, 4)]);
        TestAssert.Equal(1, drift.ComparedSamples,
                "a centre-only historical baseline compares exactly one spatial sample");
        TestAssert.Equal(26, drift.UnreferencedSamples,
                "the other 26 spatial samples remain explicitly unreferenced");
        TestAssert.Check(Math.Abs(drift.ReferenceCoverage - (1.0 / 27.0)) < 0.000001,
                "reference coverage is measured against all captured spatial probes");
    }

    private static void TestHistoricalReferenceAnchors()
    {
        Dictionary<string, (byte Sky, byte Block)> expected = new()
        {
            ["forest-canopy"] = (0, 0),
            ["torch-cave"] = (10, 14),
            ["river-valley"] = (13, 0),
            ["village-street"] = (11, 14),
            ["dungeon-arena"] = (6, 14),
        };

        foreach (LightingComparisonProfiles.Profile profile in LightingComparisonProfiles.All)
        {
            IReadOnlyList<LightingProbeComparison.ReferenceSample> reference =
                    LightingProbeComparison.LoadReference(profile.Id);
            TestAssert.Equal(27, reference.Count,
                    $"{profile.DisplayName} reference covers the complete 3x3x3 probe grid");
            HashSet<string> referenceIds = [];
            LightingProbeComparison.ReferenceSample? centre = null;
            foreach (LightingProbeComparison.ReferenceSample sample in reference)
            {
                TestAssert.Check(referenceIds.Add(sample.Id),
                        $"{profile.DisplayName} reference contains unique probe ids");
                if (sample.Id == "center")
                    centre = sample;
            }

            foreach (Vector3I offset in LightingProbeComparison.GridOffsets)
                TestAssert.Check(referenceIds.Contains(LightingProbeComparison.IdForOffset(offset)),
                        $"{profile.DisplayName} reference includes probe {LightingProbeComparison.IdForOffset(offset)}");

            TestAssert.Check(centre != null,
                    $"{profile.DisplayName} reference retains its historical centre anchor");
            if (centre != null)
            {
                TestAssert.Equal(expected[profile.Id].Sky, centre.Sky,
                        $"{profile.DisplayName} centre skylight matches the original capture report");
                TestAssert.Equal(expected[profile.Id].Block, centre.Block,
                        $"{profile.DisplayName} centre block light matches the original capture report");
            }

            List<LightingProbeComparison.Sample> identicalCapture = [];
            foreach (LightingProbeComparison.ReferenceSample sample in reference)
                identicalCapture.Add(new LightingProbeComparison.Sample(
                        sample.Id, 0, 0, 0, sample.Sky, sample.Block, "Reference"));

            LightingProbeComparison.Drift drift = LightingProbeComparison.Compare(
                    identicalCapture, reference);
            TestAssert.Equal(27, drift.ComparedSamples,
                    $"{profile.DisplayName} compares all 27 measured spatial probes");
            TestAssert.Equal(0, drift.UnreferencedSamples,
                    $"{profile.DisplayName} has no unreferenced spatial probes");
            TestAssert.Equal(0, drift.MissingSamples,
                    $"{profile.DisplayName} has no missing spatial reference probes");
            TestAssert.Equal(1.0, drift.ReferenceCoverage,
                    $"{profile.DisplayName} has complete spatial reference coverage");
            TestAssert.Equal(0.0, drift.MeanAbsoluteChannelDrift,
                    $"{profile.DisplayName} reference reproduces zero channel drift");
            TestAssert.Equal(0.0, drift.MaxColorDrift,
                    $"{profile.DisplayName} reference reproduces zero shader RGB drift");
        }
    }
}
