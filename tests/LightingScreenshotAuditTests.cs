using Godot;

namespace Minecraft;

/// <summary>
/// Synthetic regressions for the image-space lighting check. These tests do not
/// need a GPU and deliberately include dark reference areas to avoid false positives.
/// </summary>
public static class LightingScreenshotAuditTests
{
    public static void Run()
    {
        Image reference = Image.CreateEmpty(320, 240, false, Image.Format.Rgb8);
        reference.Fill(new Color(0.02f, 0.025f, 0.035f));
        FillRect(reference, 80, 80, 40, 40, new Color(0.76f, 0.68f, 0.56f));

        LightingScreenshotAudit.Result unchanged =
                LightingScreenshotAudit.Compare(reference, reference);
        TestAssert.Check(unchanged.Passed,
                "unchanged screenshot passes the lighting audit");
        TestAssert.Check(unchanged.ComparedTiles > 0,
                "lighting audit compares visible reference regions");
        TestAssert.Equal(0, unchanged.RegressedTiles,
                "unchanged screenshot has no regressed tiles");

        Image partialMissingLight = (Image)reference.Duplicate();
        // One quarter of a normally lit tile becomes nearly black. The tile mean
        // remains too high for the full-tile rule, so the pixel-coverage rule must
        // catch this severe local loss.
        FillRect(partialMissingLight, 80, 80, 10, 40,
                new Color(0.02f, 0.025f, 0.035f));
        LightingScreenshotAudit.Result partial =
                LightingScreenshotAudit.Compare(partialMissingLight, reference);
        TestAssert.Check(!partial.Passed && partial.RegressedTiles > 0,
                "a broad dark patch inside a normally lit region is detected");

        Image collapsedTile = (Image)reference.Duplicate();
        FillRect(collapsedTile, 80, 80, 40, 40,
                new Color(0.02f, 0.025f, 0.035f));
        LightingScreenshotAudit.Result collapsed =
                LightingScreenshotAudit.Compare(collapsedTile, reference);
        TestAssert.Check(!collapsed.Passed,
                "a whole missing-light tile fails the screenshot audit");

        Image slightlyDimmed = (Image)reference.Duplicate();
        FillRect(slightlyDimmed, 80, 80, 40, 40,
                new Color(0.67f, 0.60f, 0.49f));
        LightingScreenshotAudit.Result tolerant =
                LightingScreenshotAudit.Compare(slightlyDimmed, reference);
        TestAssert.Check(tolerant.Passed,
                "small luminance changes remain within the screenshot tolerance");

        Image wrongSize = Image.CreateEmpty(160, 120, false, Image.Format.Rgb8);
        wrongSize.Fill(new Color(0.5f, 0.5f, 0.5f));
        LightingScreenshotAudit.Result dimensions =
                LightingScreenshotAudit.Compare(wrongSize, reference);
        TestAssert.Check(!dimensions.Passed &&
                        dimensions.Details.Contains("dimensions differ"),
                "a screenshot with mismatched dimensions is rejected");

        LightingScreenshotAudit.Result missing =
                LightingScreenshotAudit.Compare(reference, null);
        TestAssert.Check(!missing.Passed && missing.Details.Contains("missing or empty"),
                "a missing reference screenshot is rejected");
        Image empty = new();
        LightingScreenshotAudit.Result emptyReference =
                LightingScreenshotAudit.Compare(reference, empty);
        TestAssert.Check(!emptyReference.Passed && emptyReference.Details.Contains("missing or empty"),
                "an empty reference screenshot is rejected");

        // Large dark areas in the reference are intentional in the cave and night
        // scenes; unchanged darkness must not be treated as a missing-light defect.
        TestAssert.Check(unchanged.Details.Contains("no severe missing-light"),
                "intentionally dark reference regions remain accepted");
    }

    private static void FillRect(Image image, int left, int top, int width, int height, Color color)
    {
        for (int y = top; y < top + height; y++)
        for (int x = left; x < left + width; x++)
            image.SetPixel(x, y, color);
    }
}
