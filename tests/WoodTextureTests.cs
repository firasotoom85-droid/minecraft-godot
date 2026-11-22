using Godot;

namespace Minecraft;

public static class WoodTextureTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();

        BlockDb.BlockDef oakLog = BlockDb.DefOf(BlockId.Log);
        BlockDb.BlockDef birchLog = BlockDb.DefOf(BlockId.BirchLog);
        BlockDb.BlockDef planks = BlockDb.DefOf(BlockId.Planks);
        BlockDb.BlockDef slab = BlockDb.DefOf(BlockId.PlanksSlab);
        BlockDb.BlockDef stairs = BlockDb.DefOf(BlockId.PlanksStairs);

        TestAssert.Equal(oakLog.Tiles[0], oakLog.Tiles[1], "oak log uses the same end grain on both cut ends");
        TestAssert.Check(oakLog.Tiles[0] != oakLog.Tiles[2], "oak log separates end grain from bark");
        TestAssert.Equal(birchLog.Tiles[0], birchLog.Tiles[1], "birch log uses matching cut ends");
        TestAssert.Check(birchLog.Tiles[0] != birchLog.Tiles[2], "birch log separates end grain from bark");

        TestAssert.Check(planks.Tiles[0] != planks.Tiles[2], "plank top and cut-end grain use different tiles");
        TestAssert.Check(planks.Tiles[2] != planks.Tiles[4], "plank end grain and long grain use different tiles");
        for (int face = 0; face < 6; face++)
        {
            TestAssert.Equal(planks.Tiles[face], slab.Tiles[face],
                    $"oak slab face {face} shares the plank grain layout");
            TestAssert.Equal(planks.Tiles[face], stairs.Tiles[face],
                    $"oak stair face {face} shares the plank grain layout");
        }

        TestAssert.Equal(planks.Tiles[4], BlockDb.IconTile(BlockId.Planks),
                "plank icon and held-item viewmodel use readable side grain");
        TestAssert.Equal(slab.Tiles[4], BlockDb.IconTile(BlockId.PlanksSlab),
                "slab icon and held-item viewmodel use readable side grain");
        TestAssert.Equal(stairs.Tiles[4], BlockDb.IconTile(BlockId.PlanksStairs),
                "stair icon and held-item viewmodel use readable side grain");

        CheckBlockItemIcon(BlockId.Planks, planks.Tiles[4]);
        CheckVoxelShapeItemIcon(BlockId.PlanksSlab);
        CheckVoxelShapeItemIcon(BlockId.PlanksStairs);

        int[] distinctNewTiles = [planks.Tiles[0], planks.Tiles[4], planks.Tiles[2]];
        TestAssert.Check(distinctNewTiles[0] >= 49 && distinctNewTiles[0] <= 51,
                "new plank top tile is appended after existing atlas content");
        TestAssert.Check(distinctNewTiles[1] >= 49 && distinctNewTiles[1] <= 51,
                "new plank side tile is appended after existing atlas content");
        TestAssert.Check(distinctNewTiles[2] >= 49 && distinctNewTiles[2] <= 51,
                "new plank end tile is appended after existing atlas content");
        TestAssert.Check(distinctNewTiles[0] != distinctNewTiles[1]
                && distinctNewTiles[0] != distinctNewTiles[2]
                && distinctNewTiles[1] != distinctNewTiles[2],
                "top, side and end grain each have a dedicated atlas tile");

        // Greedy terrain samples a texture array, not the packed atlas. Every
        // layer must have an isolated mip chain so distant filtering cannot
        // shimmer or bleed detail from an adjacent atlas tile.
        Texture2DArray tileArray = BlockDb.TileArrayTexture();
        TestAssert.Equal(BlockDb.TileCount, tileArray.GetLayers(),
                "mipmapped tile array preserves the full atlas layer layout");
        TestAssert.Check(tileArray.HasMipmaps(),
                "texture-array layers include generated mipmaps");

        TestAssert.Equal(2, BlockDb.TextureDetailPx,
                "procedural material noise shares a two-texel minimum detail unit");
        Image atlas = BlockDb.AtlasTexture().GetImage();
        int snowTile = BlockDb.DefOf(BlockId.Snow).Tiles[0];
        int snowX = snowTile % BlockDb.AtlasCols * BlockDb.TilePx;
        int snowY = snowTile / BlockDb.AtlasCols * BlockDb.TilePx;
        for (int y = 0; y < BlockDb.TilePx; y += BlockDb.TextureDetailPx)
        for (int x = 0; x < BlockDb.TilePx; x += BlockDb.TextureDetailPx)
        {
            Color sample = atlas.GetPixel(snowX + x, snowY + y);
            for (int dy = 0; dy < BlockDb.TextureDetailPx; dy++)
            for (int dx = 0; dx < BlockDb.TextureDetailPx; dx++)
            {
                Color neighbour = atlas.GetPixel(snowX + x + dx, snowY + y + dy);
                TestAssert.Check(Mathf.IsEqualApprox(sample.R, neighbour.R)
                        && Mathf.IsEqualApprox(sample.G, neighbour.G)
                        && Mathf.IsEqualApprox(sample.B, neighbour.B)
                        && Mathf.IsEqualApprox(sample.A, neighbour.A),
                        $"snow noise keeps a coherent {BlockDb.TextureDetailPx}x{BlockDb.TextureDetailPx} texel cluster at {x},{y}");
            }
        }
        CheckTileHasVariation(atlas, oakLog.Tiles[0], "oak log end grain");
        CheckTileHasVariation(atlas, oakLog.Tiles[2], "oak bark");
        CheckTileHasVariation(atlas, birchLog.Tiles[0], "birch log end grain");
        CheckTileHasVariation(atlas, planks.Tiles[0], "plank top grain");
        CheckTileHasVariation(atlas, planks.Tiles[2], "plank end grain");
        CheckTileHasVariation(atlas, planks.Tiles[4], "plank side grain");

        for (int face = 0; face < 6; face++)
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 actual = BlockDb.FaceUvAt(face, BlockDb.FaceVerts[face * 4 + corner]);
            Vector2 expected = BlockDb.FaceUvs[face * 4 + corner];
            TestAssert.Check((actual - expected).Length() < 0.0001f,
                    $"continuous face UV mapping preserves canonical face {face} corner {corner}");
        }

        Vector3 topPoint = new(0.25f, 1.0f, 0.75f);
        TestAssert.Check((BlockDb.BlockFaceUvAt(BlockId.PlanksStairs, 0, 0, topPoint)
                - new Vector2(0.25f, 0.75f)).Length() < 0.0001f,
                "north/south stair tread keeps the default board direction");
        TestAssert.Check((BlockDb.BlockFaceUvAt(BlockId.PlanksStairs, 1, 0, topPoint)
                - new Vector2(0.75f, 0.75f)).Length() < 0.0001f,
                "east/west stair tread rotates its board direction");
        TestAssert.Check((BlockDb.BlockFaceUvAt(BlockId.PlanksStairs, 7, 1, topPoint)
                - new Vector2(0.75f, 0.75f)).Length() < 0.0001f,
                "upside-down east/west stair underside keeps rotated grain");
        TestAssert.Equal(stairs.Tiles[4],
                BlockDb.ShapeFaceTile(BlockId.PlanksStairs, 1, 2),
                "east-facing stair uses long grain on its X-facing walls");
        TestAssert.Equal(stairs.Tiles[2],
                BlockDb.ShapeFaceTile(BlockId.PlanksStairs, 1, 4),
                "east-facing stair uses end grain on its Z-facing walls");
        TestAssert.Check((BlockDb.FaceUvAt(0, new Vector3(0.5f, 1.0f, 0.5f))
                - new Vector2(0.5f, 0.5f)).Length() < 0.0001f,
                "half-block face points retain half-tile UV coordinates");
    }

    private static void CheckBlockItemIcon(BlockId blockId, int expectedTile)
    {
        ItemId itemId = ItemDb.BlockItem(blockId);
        Texture2D icon = ItemDb.Icon(itemId);
        TestAssert.Check(icon is AtlasTexture,
                $"{blockId} first-person/inventory icon uses the existing atlas path");
        if (icon is not AtlasTexture atlasIcon) return;

        Rect2 region = atlasIcon.Region;
        TestAssert.Check(Mathf.IsEqualApprox(region.Position.X,
                        expectedTile % BlockDb.AtlasCols * BlockDb.TilePx)
                && Mathf.IsEqualApprox(region.Position.Y,
                        expectedTile / BlockDb.AtlasCols * BlockDb.TilePx)
                && Mathf.IsEqualApprox(region.Size.X, BlockDb.TilePx)
                && Mathf.IsEqualApprox(region.Size.Y, BlockDb.TilePx),
                $"{blockId} held-item icon selects the long-grain atlas tile");
    }

    private static void CheckVoxelShapeItemIcon(BlockId blockId)
    {
        ItemId itemId = ItemDb.BlockItem(blockId);
        Texture2D icon = ItemDb.Icon(itemId);
        TestAssert.Check(ItemDb.IsVoxelShapeIcon(itemId) && icon is ImageTexture,
                $"{blockId} first-person/inventory icon includes its registered voxel shape");
        if (icon is ImageTexture shapeIcon)
        {
            Image image = shapeIcon.GetImage();
            TestAssert.Equal(new Vector2I(32, 32), image.GetSize(),
                    $"{blockId} shape preview keeps the shared icon dimensions");
            TestAssert.Check(image.HasMipmaps(),
                    $"{blockId} shape preview retains the standalone icon mip chain");
        }
    }

    private static void CheckTileHasVariation(Image atlas, int tile, string label)
    {
        float minimum = 1.0f;
        float maximum = 0.0f;
        float minimumAlpha = 1.0f;
        int x0 = tile % BlockDb.AtlasCols * BlockDb.TilePx;
        int y0 = tile / BlockDb.AtlasCols * BlockDb.TilePx;
        for (int y = 0; y < BlockDb.TilePx; y += 2)
        for (int x = 0; x < BlockDb.TilePx; x += 2)
        {
            Color color = atlas.GetPixel(x0 + x, y0 + y);
            float luminance = color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
            minimum = Mathf.Min(minimum, luminance);
            maximum = Mathf.Max(maximum, luminance);
            minimumAlpha = Mathf.Min(minimumAlpha, color.A);
        }

        TestAssert.Check(maximum - minimum > 0.07f,
                $"{label} has visible tonal structure at native atlas scale");
        TestAssert.Check(minimumAlpha > 0.99f,
                $"{label} remains opaque for the shared alpha-scissor atlas material");
    }
}
