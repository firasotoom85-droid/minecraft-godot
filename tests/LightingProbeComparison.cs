using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Minecraft;

/// <summary>
/// Deterministic 3x3x3 spatial sample layout and comparison against the
/// versioned historical voxel-light reference. Light values remain the
/// production packed sky/block nibbles; no alternate lighting solver is used.
/// </summary>
public static class LightingProbeComparison
{
    public const string ReferencePath = "res://docs/visual/lighting-probe-reference-v1.json";
    public const int ReferenceVersion = 1;
    public const float DefaultTolerance = 1.0f / VoxelLighting.MaxLevel;

    private static readonly Vector3I[] Offsets = BuildOffsets();

    public sealed record Sample(
            string Id,
            int X,
            int Y,
            int Z,
            byte Sky,
            byte Block,
            string BlockType);

    public sealed record ReferenceSample(string Id, byte Sky, byte Block);

    public sealed record Drift(
            int CapturedSamples,
            int ReferenceSamples,
            int ComparedSamples,
            int UnreferencedSamples,
            int MissingSamples,
            double ReferenceCoverage,
            double MeanAbsoluteChannelDrift,
            double MaxChannelDrift,
            double MeanAbsoluteColorDrift,
            double MaxColorDrift,
            double MeanAbsoluteIntensityDrift,
            double MaxIntensityDrift,
            int OutsideToleranceSamples,
            double Tolerance);

    public static IReadOnlyList<Vector3I> GridOffsets => Offsets;

    public static string IdForOffset(Vector3I offset) =>
            offset == Vector3I.Zero
                    ? "center"
                    : $"x{offset.X:+0;-0;0}_y{offset.Y:+0;-0;0}_z{offset.Z:+0;-0;0}";

    public static IReadOnlyList<ReferenceSample> LoadReference(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
            throw new ArgumentException("A lighting scenario id is required.", nameof(scenario));

        string json = FileAccess.GetFileAsString(ReferencePath);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Could not read lighting reference data at {ReferencePath}.");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();
        if (version != ReferenceVersion)
            throw new InvalidOperationException(
                    $"Unsupported lighting reference version {version}; expected {ReferenceVersion}.");

        JsonElement scenarios = root.GetProperty("scenarios");
        if (!scenarios.TryGetProperty(scenario, out JsonElement entries)
                || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                    $"Lighting reference data has no sample array for scenario '{scenario}'.");

        List<ReferenceSample> samples = new(entries.GetArrayLength());
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("Lighting reference sample id cannot be null.");
            int sky = entry.GetProperty("sky").GetInt32();
            int block = entry.GetProperty("block").GetInt32();
            if (sky is < 0 or > VoxelLighting.MaxLevel
                    || block is < 0 or > VoxelLighting.MaxLevel)
                throw new InvalidOperationException(
                        $"Lighting reference sample '{id}' has a channel outside 0–{VoxelLighting.MaxLevel}.");
            samples.Add(new ReferenceSample(id, (byte)sky, (byte)block));
        }

        return samples;
    }

    public static Drift Compare(
            IReadOnlyList<Sample> captured,
            IReadOnlyList<ReferenceSample> reference,
            float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(reference);
        if (!float.IsFinite(tolerance) || tolerance < 0.0f || tolerance > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(tolerance),
                    "The normalized color tolerance must be finite and between zero and one.");

        Dictionary<string, Sample> actualById = new(StringComparer.Ordinal);
        foreach (Sample sample in captured)
        {
            if (sample == null || string.IsNullOrWhiteSpace(sample.Id))
                throw new ArgumentException("Captured probe ids must be non-empty.", nameof(captured));
            if (sample.Sky > VoxelLighting.MaxLevel || sample.Block > VoxelLighting.MaxLevel)
                throw new ArgumentException(
                        $"Captured probe '{sample.Id}' has a channel outside 0–{VoxelLighting.MaxLevel}.",
                        nameof(captured));
            if (!actualById.TryAdd(sample.Id, sample))
                throw new ArgumentException($"Duplicate captured probe id '{sample.Id}'.", nameof(captured));
        }

        HashSet<string> referenceIds = new(StringComparer.Ordinal);
        foreach (ReferenceSample sample in reference)
        {
            if (sample == null || string.IsNullOrWhiteSpace(sample.Id))
                throw new ArgumentException("Reference probe ids must be non-empty.", nameof(reference));
            if (sample.Sky > VoxelLighting.MaxLevel || sample.Block > VoxelLighting.MaxLevel)
                throw new ArgumentException(
                        $"Reference probe '{sample.Id}' has a channel outside 0–{VoxelLighting.MaxLevel}.",
                        nameof(reference));
            if (!referenceIds.Add(sample.Id))
                throw new ArgumentException($"Duplicate reference probe id '{sample.Id}'.", nameof(reference));
        }

        double channelTotal = 0.0;
        double maxChannel = 0.0;
        double colorTotal = 0.0;
        double maxColor = 0.0;
        double intensityTotal = 0.0;
        double maxIntensity = 0.0;
        int compared = 0;
        int outsideTolerance = 0;
        int missing = 0;

        foreach (ReferenceSample expected in reference)
        {
            if (!actualById.TryGetValue(expected.Id, out Sample? actual))
            {
                missing++;
                continue;
            }

            compared++;
            int skyDelta = Math.Abs(actual.Sky - expected.Sky);
            int blockDelta = Math.Abs(actual.Block - expected.Block);
            channelTotal += skyDelta + blockDelta;
            maxChannel = Math.Max(maxChannel, Math.Max(skyDelta, blockDelta));

            Color expectedColor = VoxelLighting.VertexColor(
                    VoxelLighting.Pack(expected.Sky, expected.Block), 1.0f);
            Color actualColor = VoxelLighting.VertexColor(
                    VoxelLighting.Pack(actual.Sky, actual.Block), 1.0f);
            double red = Math.Abs(actualColor.R - expectedColor.R);
            double green = Math.Abs(actualColor.G - expectedColor.G);
            double blue = Math.Abs(actualColor.B - expectedColor.B);
            colorTotal += red + green + blue;
            maxColor = Math.Max(maxColor, Math.Max(red, Math.Max(green, blue)));

            double intensity = Math.Abs(Luminance(actualColor) - Luminance(expectedColor));
            intensityTotal += intensity;
            maxIntensity = Math.Max(maxIntensity, intensity);

            if (Math.Max(red, Math.Max(green, blue)) > tolerance + 0.000001f)
                outsideTolerance++;
        }

        int unreferenced = captured.Count - compared;
        return new Drift(
                captured.Count,
                reference.Count,
                compared,
                unreferenced,
                missing,
                reference.Count == 0 ? 0.0 : (double)compared / reference.Count,
                compared == 0 ? 0.0 : channelTotal / (compared * 2),
                maxChannel,
                compared == 0 ? 0.0 : colorTotal / (compared * 3),
                maxColor,
                compared == 0 ? 0.0 : intensityTotal / compared,
                maxIntensity,
                outsideTolerance,
                tolerance);
    }

    private static double Luminance(Color color) =>
            color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;

    private static Vector3I[] BuildOffsets()
    {
        List<Vector3I> offsets = new(27);
        for (int y = -2; y <= 2; y += 2)
        for (int z = -2; z <= 2; z += 2)
        for (int x = -2; x <= 2; x += 2)
            offsets.Add(new Vector3I(x, y, z));
        return offsets.ToArray();
    }
}
