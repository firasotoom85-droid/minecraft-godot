using System;
using Godot;

namespace Minecraft;

public static class ContentTests
{
    public static void Run()
    {
        TestAssert.Equal(128, BlockDb.BlockCount, "block table stride");
        TestAssert.Equal(16, BlockDb.AtlasCols, "atlas columns");
        TestAssert.Equal(256, BlockDb.TileCount, "atlas tile count");

        TestAssert.Empty(ContentValidation.Validate(), "content tables validate");

        BlockDb.EnsureTables();
        TestAssert.Check(BlockDb.IsDefined(BlockId.Air), "air defined");
        TestAssert.Check(BlockDb.IsDefined(BlockId.Torch), "torch defined");
        TestAssert.Check(BlockDb.DefOf(BlockId.Torch).Light > 0.0f, "torch emits light");
        TestAssert.Check(BlockDb.LightEmission[(int)BlockId.Torch] > 0, "torch has a voxel emission level");
        TestAssert.Check(BlockDb.LightEmission[(int)BlockId.Lava] > 0, "lava has a voxel emission level");
        TestAssert.Equal((byte)15, BlockDb.LightOpacity[(int)BlockId.Stone], "stone blocks voxel light");
        TestAssert.Equal((byte)0, BlockDb.LightOpacity[(int)BlockId.Glass], "glass transmits voxel light");
        TestAssert.Check(BlockDb.IsCross(BlockId.Torch), "torch uses the cross mesh");
        TestAssert.Check(!BlockDb.IsDefined((BlockId)127), "unused ids stay undefined");

        foreach (BlockId id in Enum.GetValues<BlockId>())
        {
            if (!BlockDb.IsDefined(id))
            {
                TestAssert.Check(false, $"undefined BlockId {id}");
                continue;
            }

            BlockDb.BlockDef def = BlockDb.DefOf(id);
            TestAssert.Check(!string.IsNullOrWhiteSpace(def.Name), $"block {id} has a name");
            if (id != BlockId.Air)
            {
                TestAssert.Check(BlockDb.IconTile(id) >= 0, $"block {id} has an icon tile");
            }
            foreach (int tile in def.Tiles)
            {
                TestAssert.Check(tile >= -1 && tile < BlockDb.TileCount, $"block {id} tile {tile} in range");
            }
        }

        TestAssert.Check(ItemDb.Placeable.Count > 0, "placeable item list is populated");
        TestAssert.Check(Recipes.All.Count > 0, "recipe list is populated");
        TestAssert.Check(BlockDb.Hotbar.Length == Inventory.HotbarSize, "hotbar defaults fill nine slots");
    }
}
