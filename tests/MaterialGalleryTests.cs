using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Validates the material-gallery inventory against the real block registry,
/// atlas mapping and shared response palette. The rendered scene is exercised
/// separately by MaterialGallery.tscn because headless tests cannot judge GPU output.
/// </summary>
public static class MaterialGalleryTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();

        BlockId[] registered = Enum.GetValues<BlockId>();
        TestAssert.Equal(registered.Length, MaterialGallery.RegisteredBlockCount,
                "gallery discovers every registered BlockId");
        TestAssert.Equal(8, MaterialGallery.Columns,
                "gallery uses the fixed low-overhead eight-column layout");
        TestAssert.Equal((registered.Length + MaterialGallery.Columns - 1) / MaterialGallery.Columns,
                MaterialGallery.Rows, "gallery row count covers the registry exactly");

        HashSet<Vector2I> occupiedSlots = [];
        HashSet<Vector3I> occupiedOffsets = [];
        for (int index = 0; index < registered.Length; index++)
        {
            BlockId id = registered[index];
            TestAssert.Equal(index, (int)id,
                    $"registered block ids remain contiguous at registry index {index}");
            TestAssert.Check(BlockDb.IsDefined(id), $"gallery registry entry {id} has a BlockDef");

            Vector2I slot = MaterialGallery.SlotFor(id);
            Vector3I offset = MaterialGallery.OffsetFor(id);
            TestAssert.Check(slot.X >= 0 && slot.X < MaterialGallery.Columns
                    && slot.Y >= 0 && slot.Y < MaterialGallery.Rows,
                    $"gallery slot for {id} is inside the layout");
            TestAssert.Check(occupiedSlots.Add(slot), $"gallery slot for {id} is unique");
            TestAssert.Check(occupiedOffsets.Add(offset), $"gallery world offset for {id} is unique");

            bool specialPreview = id is BlockId.Air or BlockId.Bedrock;
            TestAssert.Equal(specialPreview, MaterialGallery.UsesPreviewGeometry(id),
                    $"{id} uses a display-only proxy only when the world API cannot place it");

            BlockDb.BlockDef definition = BlockDb.DefOf(id);
            TestAssert.Equal(6, definition.Tiles.Length,
                    $"{id} exposes all six canonical face tile slots");
            int validFaces = 0;
            for (int face = 0; face < 6; face++)
            {
                int tile = BlockDb.FaceTile(id, face);
                if (tile < 0)
                    continue;

                validFaces++;
                TestAssert.Check(tile < BlockDb.TileCount,
                        $"{id} face {face} references a valid atlas tile");
                Rect2 uv = BlockDb.TileUvRect(tile);
                TestAssert.Check(uv.Position.X >= 0.0f && uv.Position.Y >= 0.0f
                        && uv.End.X <= 1.0f && uv.End.Y <= 1.0f,
                        $"{id} face {face} UV rectangle remains inside the packed atlas");
                TestAssert.Check(Enum.IsDefined(MaterialResponse.ForTile(tile)),
                        $"{id} face {face} resolves to a known material response family");
            }

            TestAssert.Equal(id == BlockId.Air ? 0 : 6, validFaces,
                    $"{id} face coverage is explicit (Air is intentionally empty)");
        }

        TestAssert.Equal(registered.Length, occupiedSlots.Count,
                "every registered block receives exactly one gallery cell");
        TestAssert.Check(MaterialGallery.UsesPreviewGeometry(BlockId.Air),
                "Air remains represented by an empty-space preview");
        TestAssert.Check(MaterialGallery.UsesPreviewGeometry(BlockId.Bedrock),
                "Bedrock remains represented without bypassing the world mutation guard");

        Image atlas = BlockDb.AtlasTexture().GetImage();
        TestAssert.Equal(BlockDb.AtlasCols * BlockDb.TilePx, atlas.GetWidth(),
                "gallery atlas width follows the shared atlas layout");
        TestAssert.Equal(BlockDb.AtlasCols * BlockDb.TilePx, atlas.GetHeight(),
                "gallery atlas height follows the shared atlas layout");

        Texture2DArray tileArray = BlockDb.TileArrayTexture();
        TestAssert.Equal(BlockDb.TileCount, tileArray.GetLayers(),
                "gallery-compatible array retains every packed atlas tile");
        TestAssert.Check(tileArray.HasMipmaps(),
                "gallery-compatible array keeps isolated mip chains for distance views");

        foreach (SurfaceFamily family in Enum.GetValues<SurfaceFamily>())
        {
            SurfaceResponse response = MaterialResponse.Get(family);
            TestAssert.Check(float.IsFinite(response.Roughness)
                    && response.Roughness is >= 0.0f and <= 1.0f,
                    $"{family} roughness remains finite and normalized");
            TestAssert.Check(float.IsFinite(response.Metallic)
                    && response.Metallic is >= 0.0f and <= 1.0f,
                    $"{family} metallic response remains finite and normalized");
            TestAssert.Check(float.IsFinite(response.Specular)
                    && response.Specular is >= 0.0f and <= 1.0f,
                    $"{family} specular response remains finite and normalized");
        }

        foreach (ItemId item in ItemDb.Placeable)
        {
            BlockId? block = ItemDb.BlockFor(item);
            TestAssert.Check(block.HasValue && BlockDb.IsDefined(block.Value),
                    $"placeable item {item} maps back to a registered gallery block");
            if (block is { } mapped)
            {
                TestAssert.Equal(item, ItemDb.BlockItem(mapped),
                        $"placeable gallery mapping round-trips for {item}");
                int iconTile = BlockDb.IconTile(mapped);
                Texture2D icon = ItemDb.Icon(item);
                TestAssert.Check(icon is AtlasTexture,
                        $"first-person icon for {item} uses the shared atlas path");
                if (icon is AtlasTexture atlasIcon)
                {
                    TestAssert.Check(ReferenceEquals(atlasIcon.Atlas, BlockDb.AtlasTexture()),
                            $"first-person icon for {item} references the production atlas");
                    Rect2 expectedRegion = new(
                            (iconTile % BlockDb.AtlasCols) * BlockDb.TilePx,
                            (iconTile / BlockDb.AtlasCols) * BlockDb.TilePx,
                            BlockDb.TilePx, BlockDb.TilePx);
                    TestAssert.Check(atlasIcon.Region == expectedRegion,
                            $"first-person icon for {item} uses the registered block tile");
                }

                SurfaceFamily heldFamily = MaterialResponse.ForItem(item);
                TestAssert.Check(Enum.IsDefined(heldFamily),
                        $"first-person held item {item} resolves a shared material response");
            }
        }
    }
}
