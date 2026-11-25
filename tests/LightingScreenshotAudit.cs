using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Compares one fixed production-scene capture to its committed visual reference.
/// This runs only in the standalone capture path; it does not participate in gameplay.
/// </summary>
public static class LightingScreenshotAudit
{
    private const int GridColumns = 8;
    private const int GridRows = 6;
    private const int SamplesPerTileEdge = 20;

    // Ignore reference pixels that are already intentionally dark (caves, night,
    // shadowed terrain). Only visibly lit reference regions can report a light loss.
    private const float MinimumReferencePixelLuminance = 32.0f / 255.0f;
    private const float MinimumReferenceTileLuminance = 28.0f / 255.0f;
    private const float MinimumAbsoluteDrop = 18.0f / 255.0f;
    private const float MaximumRetainedPixelLuminance = 0.35f;
    private const float MaximumRetainedTileLuminance = 0.55f;
    private const float MinimumSeverelyDarkenedTileCoverage = 0.20f;

    public readonly record struct Result(
            bool Passed,
            int ComparedTiles,
            int RegressedTiles,
            float WorstTileDrop,
            string Details);

    /// <summary>
    /// Samples a fixed 8x6 grid. A tile fails when its mean collapses by at least
    /// 45%, or when at least 20% of its samples lose 65% or more of their
    /// reference luminance. The absolute 18/255 floor absorbs small renderer noise.
    /// </summary>
    public static Result Compare(Image? captured, Image? reference)
    {
        if (captured == null || captured.IsEmpty())
            return new Result(false, 0, 0, 0.0f, "captured screenshot is empty");
        if (reference == null || reference.IsEmpty())
            return new Result(false, 0, 0, 0.0f, "reference screenshot is missing or empty");

        int width = reference.GetWidth();
        int height = reference.GetHeight();
        if (captured.GetWidth() != width || captured.GetHeight() != height)
            return new Result(false, 0, 0, 0.0f,
                    $"screenshot dimensions differ: captured {captured.GetWidth()}x{captured.GetHeight()}, " +
                    $"reference {width}x{height}");
        if (width < GridColumns || height < GridRows)
            return new Result(false, 0, 0, 0.0f,
                    $"screenshot is too small for the {GridColumns}x{GridRows} audit grid");

        int comparedTiles = 0;
        int regressedTiles = 0;
        float worstTileDrop = 0.0f;
        List<string> regressions = [];

        for (int row = 0; row < GridRows; row++)
            for (int column = 0; column < GridColumns; column++)
            {
                int left = column * width / GridColumns;
                int right = (column + 1) * width / GridColumns;
                int top = row * height / GridRows;
                int bottom = (row + 1) * height / GridRows;
                int sampleStep = Math.Max(1,
                        (Math.Min(right - left, bottom - top) + SamplesPerTileEdge - 1) /
                        SamplesPerTileEdge);

                double referenceSum = 0.0;
                double capturedSum = 0.0;
                int sampleCount = 0;
                int severelyDarkenedSamples = 0;

                for (int y = top + sampleStep / 2; y < bottom; y += sampleStep)
                    for (int x = left + sampleStep / 2; x < right; x += sampleStep)
                    {
                        float referenceLuminance = Luminance(reference.GetPixel(x, y));
                        float capturedLuminance = Luminance(captured.GetPixel(x, y));
                        referenceSum += referenceLuminance;
                        capturedSum += capturedLuminance;
                        sampleCount++;

                        float drop = referenceLuminance - capturedLuminance;
                        if (referenceLuminance >= MinimumReferencePixelLuminance &&
                                drop >= MinimumAbsoluteDrop &&
                                capturedLuminance <= referenceLuminance * MaximumRetainedPixelLuminance)
                        {
                            severelyDarkenedSamples++;
                        }
                    }

                if (sampleCount == 0)
                    continue;

                float referenceMean = (float)(referenceSum / sampleCount);
                float capturedMean = (float)(capturedSum / sampleCount);
                if (referenceMean < MinimumReferenceTileLuminance)
                    continue;

                comparedTiles++;
                float meanDrop = referenceMean - capturedMean;
                worstTileDrop = Math.Max(worstTileDrop, meanDrop);
                float severeCoverage = (float)severelyDarkenedSamples / sampleCount;
                bool tileMeanCollapsed = meanDrop >= MinimumAbsoluteDrop &&
                        capturedMean <= referenceMean * MaximumRetainedTileLuminance;
                bool illuminatedPatchCollapsed =
                        severeCoverage >= MinimumSeverelyDarkenedTileCoverage;

                if (!tileMeanCollapsed && !illuminatedPatchCollapsed)
                    continue;

                regressedTiles++;
                regressions.Add(
                        $"({column},{row}) ref={referenceMean * 255.0f:F1} " +
                        $"now={capturedMean * 255.0f:F1} darkened={severeCoverage:P0}");
            }

        if (comparedTiles == 0)
            return new Result(false, 0, 0, worstTileDrop,
                    "reference contains no sufficiently visible regions to validate");

        string details = regressedTiles == 0
                ? "no severe missing-light regions detected"
                : $"{regressedTiles} of {comparedTiles} lit tiles regressed: {string.Join(", ", regressions)}";
        return new Result(regressedTiles == 0, comparedTiles, regressedTiles, worstTileDrop, details);
    }

    private static float Luminance(Color color) =>
            0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
}
