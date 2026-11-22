using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Audits the generated standalone item-icon style while pinning block-backed
/// icons to their existing atlas regions.
/// </summary>
public static class ItemIconTests
{
    private const int IconPx = 32;
    private const int PaddingPx = 4;
    private const int Columns = 9;
    private const int CellPx = 56;
    private const int PreviewPx = 48;

    public static void Run()
    {
        CheckSharedStyleFixture();
        CheckTallSilhouetteLighting();
        CheckNoneSentinel();
        CheckEveryItemIcon();
        CaptureContactSheetIfRequested();
    }

    private static void CheckSharedStyleFixture()
    {
        Image source = Image.CreateEmpty(IconPx, IconPx, false, Image.Format.Rgba8);
        source.Fill(Colors.Transparent);
        Color sample = new(0.44f, 0.46f, 0.48f, 1.0f);
        for (int y = 10; y < 22; y++)
        {
            for (int x = 10; x < 22; x++)
            {
                source.SetPixel(x, y, sample);
            }
        }

        Image styled = ItemDb.StyleGeneratedIcon(source);
        TestAssert.Equal(new Vector2I(IconPx, IconPx), styled.GetSize(),
                "shared item-icon finish keeps the 32px canvas");
        TestAssert.Check(ColorBrightness(styled.GetPixel(5, 5)) > ColorBrightness(source.GetPixel(10, 10)),
                "shared icon key light brightens the upper-left silhouette edge");
        TestAssert.Check(ColorBrightness(styled.GetPixel(26, 26)) < ColorBrightness(source.GetPixel(21, 21)),
                "shared icon fill shades the lower-right silhouette edge");
        TestAssert.Check(ColorsNear(styled.GetPixel(4, 10), ItemDb.GeneratedIconOutlineColor),
                "shared item-icon contour is one pixel and uses the canonical outline");
        TestAssert.Check(styled.GetPixel(3, 10).A < 0.001f
                        && styled.GetPixel(28, 10).A < 0.001f,
                "shared icon finish keeps four transparent pixels outside its contour");
        TestAssert.Check(styled.HasMipmaps(),
                "generated standalone item icons carry a mip chain");
        TestAssert.Equal(5, styled.GetMipmapCount(),
                "32px item icons have the complete 16/8/4/2/1 mip chain");
    }

    private static void CheckTallSilhouetteLighting()
    {
        Image source = Image.CreateEmpty(IconPx, IconPx, false, Image.Format.Rgba8);
        source.Fill(Colors.Transparent);
        Color sample = new(0.44f, 0.46f, 0.48f, 1.0f);
        for (int y = 6; y < 26; y++)
        {
            for (int x = 12; x < 20; x++)
            {
                source.SetPixel(x, y, sample);
            }
        }

        Image styled = ItemDb.StyleGeneratedIcon(source);
        Color topLeft = styled.GetPixel(11, 5);
        Color bottomRight = styled.GetPixel(19, 26);
        TestAssert.Check(ColorBrightness(topLeft) > ColorBrightness(source.GetPixel(12, 6)),
                "tall silhouettes receive the same upper-left key light regardless of canvas centering");
        TestAssert.Check(ColorBrightness(bottomRight) < ColorBrightness(source.GetPixel(19, 25)),
                "tall silhouettes receive lower-right shading relative to their own bounds");
    }

    private static void CheckNoneSentinel()
    {
        Image empty = ItemDb.Icon(ItemId.None).GetImage();
        bool fullyTransparent = true;
        for (int y = 0; y < IconPx; y++)
        {
            for (int x = 0; x < IconPx; x++)
            {
                if (empty.GetPixel(x, y).A > 0.001f)
                {
                    fullyTransparent = false;
                    break;
                }
            }

            if (!fullyTransparent)
            {
                break;
            }
        }

        TestAssert.Check(fullyTransparent,
                "the None sentinel remains a fully transparent icon");
        TestAssert.Check(!empty.HasMipmaps(),
                "the empty None sentinel does not allocate an unnecessary mip chain");
    }

    private static void CheckEveryItemIcon()
    {
        int blockBacked = 0;
        int standalone = 0;

        foreach (ItemId id in Enum.GetValues<ItemId>())
        {
            if (id == ItemId.None)
            {
                continue;
            }

            Texture2D texture = ItemDb.Icon(id);
            TestAssert.Check(GodotObject.IsInstanceValid(texture),
                    $"{id} returns a valid icon texture");

            if (ItemDb.BlockFor(id) is { } block)
            {
                blockBacked++;
                TestAssert.Check(texture is AtlasTexture,
                        $"{id} remains backed by the shared atlas");
                if (texture is AtlasTexture atlasIcon)
                {
                    int tile = BlockDb.IconTile(block);
                    Rect2 expected = new(
                            (tile % BlockDb.AtlasCols) * BlockDb.TilePx,
                            (tile / BlockDb.AtlasCols) * BlockDb.TilePx,
                            BlockDb.TilePx, BlockDb.TilePx);
                    TestAssert.Check(atlasIcon.Atlas == BlockDb.AtlasTexture(),
                            $"{id} references the original block atlas");
                    TestAssert.Equal(expected, atlasIcon.Region,
                            $"{id} retains its original 32px atlas tile");
                }

                continue;
            }

            standalone++;
            TestAssert.Check(texture is ImageTexture,
                    $"{id} uses the cached generated-icon texture");
            if (texture is not ImageTexture)
            {
                continue;
            }

            TestAssert.Check(ReferenceEquals(texture, ItemDb.Icon(id)),
                    $"{id} generated icon texture is cached after first creation");
            Image image = texture.GetImage();
            TestAssert.Equal(new Vector2I(IconPx, IconPx), image.GetSize(),
                    $"{id} keeps a 32px generated icon");
            TestAssert.Check(image.HasMipmaps(),
                    $"{id} has generated mipmaps");
            TestAssert.Equal(5, image.GetMipmapCount(),
                    $"{id} has every mip level down to one pixel");

            int minX = IconPx;
            int minY = IconPx;
            int maxX = -1;
            int maxY = -1;
            int outlinePixels = 0;
            bool binaryAlpha = true;
            bool transparentPadding = true;

            for (int y = 0; y < IconPx; y++)
            {
                for (int x = 0; x < IconPx; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    if (pixel.A > 0.001f && pixel.A < 0.999f)
                    {
                        binaryAlpha = false;
                    }

                    if (pixel.A <= 0.001f)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                    if (ColorsNear(pixel, ItemDb.GeneratedIconOutlineColor))
                    {
                        outlinePixels++;
                    }

                    if (x < PaddingPx || y < PaddingPx
                            || x >= IconPx - PaddingPx || y >= IconPx - PaddingPx)
                    {
                        transparentPadding = false;
                    }
                }
            }

            TestAssert.Check(maxX >= minX && maxY >= minY,
                    $"{id} has a visible silhouette");
            TestAssert.Check(minX >= PaddingPx && minY >= PaddingPx
                            && maxX < IconPx - PaddingPx && maxY < IconPx - PaddingPx,
                    $"{id} silhouette and contour fit the shared transparent frame");
            TestAssert.Check(binaryAlpha,
                    $"{id} retains crisp binary silhouette alpha");
            TestAssert.Check(transparentPadding,
                    $"{id} keeps a four-pixel transparent outer edge");
            TestAssert.Check(outlinePixels > 0,
                    $"{id} has a consistent one-pixel silhouette contour");
        }

        TestAssert.Check(blockBacked > 0,
                "block-backed items continue to use their atlas");
        TestAssert.Check(standalone > 0,
                "standalone items use the shared generated-icon style");
        GD.Print($"ITEM_ICON_POPULATION atlasBacked={blockBacked} standalone={standalone}");
        TestAssert.Equal(Enum.GetValues<ItemId>().Length - 1,
                blockBacked + standalone,
                "every persisted item except the None sentinel resolves to one icon path");
    }

    private static float ColorBrightness(Color color) =>
        color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;

    private static bool ColorsNear(Color actual, Color expected, float tolerance = 0.01f) =>
        Math.Abs(actual.R - expected.R) <= tolerance
        && Math.Abs(actual.G - expected.G) <= tolerance
        && Math.Abs(actual.B - expected.B) <= tolerance
        && Math.Abs(actual.A - expected.A) <= tolerance;

    private static void CaptureContactSheetIfRequested()
    {
        string? outputPath = System.Environment.GetEnvironmentVariable("MINECRAFT_ICON_AUDIT_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        ItemId[] items = (ItemId[])Enum.GetValues<ItemId>();
        int itemCount = items.Length - 1;
        int rows = (itemCount + Columns - 1) / Columns;
        Image sheet = Image.CreateEmpty(Columns * CellPx, rows * CellPx,
                false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.16f, 0.18f, 0.21f, 1.0f));

        int visibleIndex = 0;
        foreach (ItemId id in items)
        {
            if (id == ItemId.None)
            {
                continue;
            }

            int column = visibleIndex % Columns;
            int row = visibleIndex / Columns;
            Image icon = ItemDb.Icon(id).GetImage();
            icon.ClearMipmaps();
            icon.Resize(PreviewPx, PreviewPx, Image.Interpolation.Nearest);
            int targetX = column * CellPx + 4;
            int targetY = row * CellPx + 4;
            for (int y = 0; y < PreviewPx; y++)
            {
                for (int x = 0; x < PreviewPx; x++)
                {
                    Color foreground = icon.GetPixel(x, y);
                    if (foreground.A <= 0.001f)
                    {
                        continue;
                    }

                    int destinationX = targetX + x;
                    int destinationY = targetY + y;
                    Color background = sheet.GetPixel(destinationX, destinationY);
                    float inverseAlpha = 1.0f - foreground.A;
                    sheet.SetPixel(destinationX, destinationY, new Color(
                            foreground.R * foreground.A + background.R * inverseAlpha,
                            foreground.G * foreground.A + background.G * inverseAlpha,
                            foreground.B * foreground.A + background.B * inverseAlpha,
                            1.0f));
                }
            }
            GD.Print($"ITEM_ICON_AUDIT {visibleIndex:D3} {id}");
            visibleIndex++;
        }

        Error error = sheet.SavePng(outputPath);
        TestAssert.Equal(Error.Ok, error,
                $"item icon audit contact sheet saves to {outputPath}");
    }
}
