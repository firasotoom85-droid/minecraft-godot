using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>Regression coverage for the live atlas report and malformed synthetic registries.</summary>
public static class TextureAtlasValidationTests
{
    public static void Run()
    {
        TextureValidationReport current = TextureAtlasValidator.ValidateCurrent();
        TestAssert.Check(current.IsClean,
                $"live procedural atlas has no missing tiles, invalid references or accidental duplicates: {string.Join("; ", current.Issues)}");
        TestAssert.Equal(512, current.AtlasWidth, "packed atlas retains its 512px width");
        TestAssert.Equal(512, current.AtlasHeight, "packed atlas retains its 512px height");
        TestAssert.Equal(16, current.AtlasColumns, "packed atlas retains its 16-column layout");
        TestAssert.Equal(32, current.TilePixels, "packed atlas retains its 32px source tiles");
        TestAssert.Equal(BlockDb.TileCount, current.TileArrayLayers,
                "greedy texture-array keeps one layer per atlas slot");
        TestAssert.Check(current.TileArrayHasMipmaps,
                "greedy texture-array retains isolated per-tile mipmaps for distant viewing");
        TestAssert.Equal(ItemDb.Placeable.Count, current.PlaceableItems,
                "every placeable item is included in the icon mapping audit");
        TestAssert.Check(current.ReferencedTiles > 0 && current.PaintedTiles >= current.ReferencedTiles,
                "all referenced atlas tiles are painted");
        TestAssert.Check(HasIssue(current, "atlas.tile.legacy-orphan"),
                "the retained plank-building art is explicitly documented as a legacy atlas tile");
        TestAssert.Check(current.ToMarkdown().Contains("Block face mappings", StringComparison.Ordinal)
                && current.ToMarkdown().Contains("Placeable item icon mappings", StringComparison.Ordinal),
                "Markdown report includes block and first-person item mappings");

        TestInvalidReferencesAndMissingTiles();
        TestAccidentalDuplicateDetection();
        TestIntentionalSharedDefinitions();
        TestDirectionalFaceRegression();
    }

    private static void TestInvalidReferencesAndMissingTiles()
    {
        Image atlas = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        atlas.Fill(new Color(0, 0, 0, 0));
        FillTile(atlas, 0, 0, 2, new Color(0.8f, 0.2f, 0.2f));
        FillTile(atlas, 1, 0, 2, new Color(0.2f, 0.8f, 0.2f));

        List<AtlasBlockMapping> blocks =
        [
            new(BlockId.Air, "Air", [-1, -1, -1, -1, -1, -1]),
            new(BlockId.Stone, "Stone", [4, 4, 4, 4, 4, 4]),
            new(BlockId.Dirt, "Dirt", [-2, -1, 0, 0, 0, 0]),
            new(BlockId.Grass, "Grass", [3, 3, 3, 3, 3, 3]),
            new((BlockId)250, "Unregistered", [1, 1, 1, 1, 1, 1]),
        ];
        TextureValidationReport report = TextureAtlasValidator.Analyze(
                atlas, null, 2, 4, blocks, null);

        TestAssert.Check(HasIssue(report, "mapping.tile.invalid"),
                "tile indices at and below the invalid range are rejected");
        TestAssert.Check(HasIssue(report, "mapping.face.missing"),
                "missing face references on rendered blocks are rejected");
        TestAssert.Check(HasIssue(report, "atlas.tile.unpainted"),
                "referenced but transparent atlas tiles are reported as missing");
        TestAssert.Check(HasIssue(report, "mapping.block.invalid-id"),
                "unregistered persisted block IDs are rejected by the material mapping audit");
        TestAssert.Check(!report.IsValid, "invalid references make the validation report fail");
        atlas.Dispose();
    }

    private static void TestAccidentalDuplicateDetection()
    {
        Image atlas = BuildSyntheticAtlas();
        List<AtlasBlockMapping> duplicatedArt =
        [
            new(BlockId.Air, "Air", [-1, -1, -1, -1, -1, -1]),
            new(BlockId.Stone, "Stone", [0, 0, 0, 0, 0, 0]),
            new(BlockId.Dirt, "Dirt", [1, 1, 1, 1, 1, 1]),
            new(BlockId.Sand, "Sand", [2, 2, 2, 2, 2, 2]),
        ];
        TextureValidationReport duplicatePixels = TextureAtlasValidator.Analyze(
                atlas, null, 2, 4, duplicatedArt, null);
        TestAssert.Check(HasIssue(duplicatePixels, "atlas.tile.duplicate-pixels"),
                "identical pixels assigned to different atlas coordinates are reported");
        TestAssert.Check(HasIssue(duplicatePixels, "mapping.definition.duplicate") == false,
                "different tile coordinates are not mistaken for duplicate block mappings");

        List<AtlasBlockMapping> duplicatedMapping =
        [
            new(BlockId.Air, "Air", [-1, -1, -1, -1, -1, -1]),
            new(BlockId.Stone, "Stone", [0, 0, 0, 0, 0, 0]),
            new(BlockId.Dirt, "Dirt", [0, 0, 0, 0, 0, 0]),
        ];
        TextureValidationReport duplicateReferences = TextureAtlasValidator.Analyze(
                atlas, null, 2, 4, duplicatedMapping, null);
        TestAssert.Check(HasIssue(duplicateReferences, "mapping.definition.duplicate"),
                "unrelated blocks with an identical six-face signature are reported");
        atlas.Dispose();
    }

    private static void TestIntentionalSharedDefinitions()
    {
        Image atlas = BuildSyntheticAtlas();
        FillTile(atlas, 0, 1, 2, new Color(0.8f, 0.8f, 0.2f));
        List<AtlasBlockMapping> intentionalAliases =
        [
            new(BlockId.Water, "Water", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater1, "Flowing Water 1", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater2, "Flowing Water 2", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater3, "Flowing Water 3", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater4, "Flowing Water 4", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater5, "Flowing Water 5", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater6, "Flowing Water 6", [0, 0, 0, 0, 0, 0]),
            new(BlockId.FlowingWater7, "Flowing Water 7", [0, 0, 0, 0, 0, 0]),
            new(BlockId.Planks, "Planks", [0, 0, 1, 1, 2, 2]),
            new(BlockId.PlanksSlab, "Planks Slab", [0, 0, 1, 1, 2, 2]),
            new(BlockId.PlanksStairs, "Planks Stairs", [0, 0, 1, 1, 2, 2]),
            new(BlockId.CobblestoneSlab, "Cobblestone Slab", [1, 1, 1, 1, 1, 1]),
            new(BlockId.CobblestoneStairs, "Cobblestone Stairs", [1, 1, 1, 1, 1, 1]),
            new(BlockId.BrickSlab, "Brick Slab", [2, 2, 2, 2, 2, 2]),
            new(BlockId.BrickStairs, "Brick Stairs", [2, 2, 2, 2, 2, 2]),
        ];
        TextureValidationReport report = TextureAtlasValidator.Analyze(
                atlas, null, 2, 4, intentionalAliases, null);
        TestAssert.Check(!HasIssue(report, "mapping.definition.duplicate"),
                "intentional water, plank, cobblestone and brick mappings remain explicitly allowed");
        atlas.Dispose();
    }

    private static void TestDirectionalFaceRegression()
    {
        Image atlas = BuildSyntheticAtlas();
        List<AtlasBlockMapping> collapsedLog =
        [
            new(BlockId.Log, "Oak Log", [0, 0, 0, 0, 0, 0]),
        ];
        TextureValidationReport report = TextureAtlasValidator.Analyze(
                atlas, null, 2, 4, collapsedLog, null);
        TestAssert.Check(HasIssue(report, "mapping.directional-faces.collapsed"),
                "directional log ends cannot silently collapse to the bark tile");
        atlas.Dispose();
    }

    private static Image BuildSyntheticAtlas()
    {
        Image atlas = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        atlas.Fill(new Color(0, 0, 0, 0));
        FillTile(atlas, 0, 0, 2, new Color(0.8f, 0.2f, 0.2f));
        FillTile(atlas, 1, 0, 2, new Color(0.2f, 0.8f, 0.2f));
        FillTile(atlas, 0, 1, 2, new Color(0.8f, 0.2f, 0.2f));
        FillTile(atlas, 1, 1, 2, new Color(0.2f, 0.2f, 0.8f));
        return atlas;
    }

    private static void FillTile(Image atlas, int column, int row, int columns, Color color)
    {
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            atlas.SetPixel(column * 4 + x, row * 4 + y, color);
    }

    private static bool HasIssue(TextureValidationReport report, string code)
    {
        foreach (TextureValidationIssue issue in report.Issues)
        {
            if (issue.Code == code)
                return true;
        }
        return false;
    }
}
