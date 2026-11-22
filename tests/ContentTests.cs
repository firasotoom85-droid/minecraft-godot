using System;
using Godot;

namespace Minecraft;

public static class ContentTests
{
    // Persisted identifiers are append-only. These tables pin every id that has
    // shipped in a save or a chunk array; changing a value here is a data-loss
    // bug, not a refactor.
    private static readonly (BlockId Id, int Value)[] PersistedBlocks =
    [
        (BlockId.Air, 0),
        (BlockId.Grass, 1),
        (BlockId.Dirt, 2),
        (BlockId.Stone, 3),
        (BlockId.Sand, 4),
        (BlockId.Log, 5),
        (BlockId.Leaves, 6),
        (BlockId.Water, 7),
        (BlockId.Planks, 8),
        (BlockId.Cobblestone, 9),
        (BlockId.Brick, 10),
        (BlockId.CoalOre, 11),
        (BlockId.IronOre, 12),
        (BlockId.Glass, 13),
        (BlockId.Bedrock, 14),
        (BlockId.SnowGrass, 15),
        (BlockId.Snow, 16),
        (BlockId.Gravel, 17),
        (BlockId.GoldOre, 18),
        (BlockId.DiamondOre, 19),
        (BlockId.BirchLog, 20),
        (BlockId.BirchLeaves, 21),
        (BlockId.Cactus, 22),
        (BlockId.CraftingTable, 23),
        (BlockId.Wool, 24),
        (BlockId.Lava, 25),
        (BlockId.TallGrass, 26),
        (BlockId.FlowerRed, 27),
        (BlockId.FlowerYellow, 28),
        (BlockId.Torch, 29),
        (BlockId.Chest, 30),
        (BlockId.Furnace, 31),
        (BlockId.Farmland, 32),
        (BlockId.Wheat0, 33),
        (BlockId.Wheat1, 34),
        (BlockId.Wheat2, 35),
        (BlockId.Bed, 36),
        (BlockId.Ladder, 37),
        (BlockId.FlowingWater1, 38),
        (BlockId.FlowingWater2, 39),
        (BlockId.FlowingWater3, 40),
        (BlockId.FlowingWater4, 41),
        (BlockId.FlowingWater5, 42),
        (BlockId.FlowingWater6, 43),
        (BlockId.FlowingWater7, 44),
        (BlockId.EnchantingTable, 45),
        (BlockId.PlanksSlab, 46),
        (BlockId.CobblestoneSlab, 47),
        (BlockId.BrickSlab, 48),
        (BlockId.PlanksStairs, 49),
        (BlockId.CobblestoneStairs, 50),
        (BlockId.BrickStairs, 51),
    ];

    private static readonly (ItemId Id, int Value)[] PersistedItems =
    [
        (ItemId.None, 0),
        (ItemId.Grass, 1),
        (ItemId.Dirt, 2),
        (ItemId.Stone, 3),
        (ItemId.Sand, 4),
        (ItemId.Gravel, 5),
        (ItemId.Log, 6),
        (ItemId.BirchLog, 7),
        (ItemId.Leaves, 8),
        (ItemId.BirchLeaves, 9),
        (ItemId.SnowGrass, 10),
        (ItemId.Snow, 11),
        (ItemId.Glass, 12),
        (ItemId.Brick, 13),
        (ItemId.Cactus, 14),
        (ItemId.Planks, 15),
        (ItemId.CraftingTable, 16),
        (ItemId.Cobblestone, 17),
        (ItemId.CoalOre, 18),
        (ItemId.IronOre, 19),
        (ItemId.GoldOre, 20),
        (ItemId.DiamondOre, 21),
        (ItemId.Coal, 22),
        (ItemId.RawIron, 23),
        (ItemId.RawGold, 24),
        (ItemId.Diamond, 25),
        (ItemId.Stick, 26),
        (ItemId.WoodPick, 27),
        (ItemId.WoodAxe, 28),
        (ItemId.WoodShovel, 29),
        (ItemId.StonePick, 30),
        (ItemId.StoneAxe, 31),
        (ItemId.StoneShovel, 32),
        (ItemId.IronPick, 33),
        (ItemId.IronAxe, 34),
        (ItemId.IronShovel, 35),
        (ItemId.DiamondPick, 36),
        (ItemId.DiamondAxe, 37),
        (ItemId.DiamondShovel, 38),
        (ItemId.Apple, 39),
        (ItemId.RawMutton, 40),
        (ItemId.CookedMutton, 41),
        (ItemId.Wool, 42),
        (ItemId.Lava, 43),
        (ItemId.TallGrass, 44),
        (ItemId.FlowerRed, 45),
        (ItemId.FlowerYellow, 46),
        (ItemId.Torch, 47),
        (ItemId.Chest, 48),
        (ItemId.Furnace, 49),
        (ItemId.IronIngot, 50),
        (ItemId.GoldIngot, 51),
        (ItemId.IronHelmet, 52),
        (ItemId.IronChestplate, 53),
        (ItemId.IronLegs, 54),
        (ItemId.IronBoots, 55),
        (ItemId.DiamondHelmet, 56),
        (ItemId.DiamondChestplate, 57),
        (ItemId.DiamondLegs, 58),
        (ItemId.DiamondBoots, 59),
        (ItemId.Seeds, 60),
        (ItemId.Wheat, 61),
        (ItemId.Bread, 62),
        (ItemId.WoodHoe, 63),
        (ItemId.StoneHoe, 64),
        (ItemId.IronHoe, 65),
        (ItemId.DiamondHoe, 66),
        (ItemId.Bed, 67),
        (ItemId.Ladder, 68),
        (ItemId.RawBeef, 69),
        (ItemId.CookedBeef, 70),
        (ItemId.RawPork, 71),
        (ItemId.CookedPork, 72),
        (ItemId.RawChicken, 73),
        (ItemId.CookedChicken, 74),
        (ItemId.Leather, 75),
        (ItemId.Feather, 76),
        (ItemId.EmberCore, 77),
        (ItemId.FishingRod, 78),
        (ItemId.RawFish, 79),
        (ItemId.CookedFish, 80),
        (ItemId.WoodSword, 81),
        (ItemId.StoneSword, 82),
        (ItemId.IronSword, 83),
        (ItemId.DiamondSword, 84),
        (ItemId.Bow, 85),
        (ItemId.Arrow, 86),
        (ItemId.Shield, 87),
        (ItemId.EmberBlade, 88),
        (ItemId.LapisLazuli, 89),
        (ItemId.EnchantingTable, 90),
        (ItemId.PlanksSlab, 91),
        (ItemId.CobblestoneSlab, 92),
        (ItemId.BrickSlab, 93),
        (ItemId.PlanksStairs, 94),
        (ItemId.CobblestoneStairs, 95),
        (ItemId.BrickStairs, 96),
        (ItemId.EmptyBucket, 97),
        (ItemId.WaterBucket, 98),
        (ItemId.LavaBucket, 99),
        (ItemId.WoodenBoat, 100),
    ];

    public static void Run()
    {
        TestAssert.Equal(128, BlockDb.BlockCount, "block table stride");
        TestAssert.Equal(16, BlockDb.AtlasCols, "atlas columns");
        TestAssert.Equal(256, BlockDb.TileCount, "atlas tile count");

        CheckMaterialResponses();

        TestAssert.Empty(ContentValidation.Validate(), "content tables validate");

        CheckPersistedIds();

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

        // Ore remains visible through its shared atlas tiles, but never acts as
        // a light source or weakens opaque cave-wall occlusion.
        foreach (BlockId ore in new[]
        {
            BlockId.CoalOre, BlockId.IronOre, BlockId.GoldOre, BlockId.DiamondOre,
        })
        {
            TestAssert.Check(BlockDb.IsSolid(ore) && !BlockDb.IsTransparent(ore),
                    $"{ore} remains an opaque solid block");
            TestAssert.Equal((byte)0, BlockDb.LightEmission[(int)ore],
                    $"{ore} does not glow through cave walls");
            TestAssert.Equal((byte)15, BlockDb.LightOpacity[(int)ore],
                    $"{ore} fully occludes voxel light");
        }

        Image atlas = BlockDb.AtlasTexture().GetImage();
        foreach ((int tile, Func<Color, bool> isOrePixel, string name) in new[]
        {
            (12, (Func<Color, bool>)(c => c.R < 0.28f && c.G < 0.28f && c.B < 0.30f), "coal"),
            (13, (Func<Color, bool>)(c => c.R > 0.65f && c.G > 0.32f && c.G < 0.78f && c.B < 0.62f), "iron"),
            (19, (Func<Color, bool>)(c => c.R > 0.78f && c.G > 0.55f && c.B < 0.52f), "gold"),
            (20, (Func<Color, bool>)(c => c.R < 0.62f && c.G > 0.68f && c.B > 0.68f), "diamond"),
        })
        {
            int originX = tile % BlockDb.AtlasCols * BlockDb.TilePx;
            int originY = tile / BlockDb.AtlasCols * BlockDb.TilePx;
            int visiblePixels = 0;
            for (int y = 0; y < BlockDb.TilePx; y++)
            for (int x = 0; x < BlockDb.TilePx; x++)
                if (isOrePixel(atlas.GetPixel(originX + x, originY + y))) visiblePixels++;
            TestAssert.Check(visiblePixels >= 55,
                    $"{name} ore has enough high-contrast mineral pixels for distant readability");
        }

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

    private static void CheckMaterialResponses()
    {
        SurfaceResponse stone = MaterialResponse.Get(SurfaceFamily.Stone);
        SurfaceResponse wood = MaterialResponse.Get(SurfaceFamily.Wood);
        SurfaceResponse soil = MaterialResponse.Get(SurfaceFamily.Soil);
        SurfaceResponse ore = MaterialResponse.Get(SurfaceFamily.Ore);
        SurfaceResponse foliage = MaterialResponse.Get(SurfaceFamily.Foliage);
        SurfaceResponse glass = MaterialResponse.Get(SurfaceFamily.Glass);
        SurfaceResponse metal = MaterialResponse.Get(SurfaceFamily.Metal);

        TestAssert.Check(stone.Roughness > 0.7f && stone.Metallic < 0.1f,
                "stone response stays rough and non-metallic");
        TestAssert.Check(wood.Roughness > 0.7f && wood.Metallic == 0.0f,
                "wood response stays matte and non-metallic");
        TestAssert.Check(soil.Roughness > wood.Roughness,
                "soil is more diffuse than wood");
        TestAssert.Check(ore.Metallic > stone.Metallic && ore.Roughness < stone.Roughness,
                "ore response has denser mineral highlights");
        TestAssert.Check(foliage.Roughness > 0.85f && foliage.Metallic == 0.0f,
                "foliage response remains matte");
        TestAssert.Check(glass.Roughness < 0.25f && glass.Specular > stone.Specular,
                "glass response remains smooth and reflective");
        TestAssert.Check(metal.Metallic > 0.6f && metal.Roughness < 0.4f,
                "metal response has a distinct polished metal finish");

        TestAssert.Equal(SurfaceFamily.Stone, MaterialResponse.ForTile(3),
                "stone atlas tile maps to stone response");
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForTile(5),
                "log atlas tile maps to wood response");
        TestAssert.Equal(SurfaceFamily.Soil, MaterialResponse.ForTile(2),
                "dirt atlas tile maps to soil response");
        TestAssert.Equal(SurfaceFamily.Ore, MaterialResponse.ForTile(19),
                "gold ore atlas tile maps to ore response");
        TestAssert.Equal(SurfaceFamily.Foliage, MaterialResponse.ForTile(7),
                "leaf atlas tile maps to foliage response");
        TestAssert.Equal(SurfaceFamily.Glass, MaterialResponse.ForTile(14),
                "glass atlas tile maps to glass response");
        TestAssert.Equal(SurfaceFamily.Metal, MaterialResponse.ForItem(ItemId.IronIngot),
                "metal inventory items map to metal response");
        TestAssert.Equal(SurfaceFamily.Metal, MaterialResponse.ForItem(ItemId.IronPick),
                "metal tools use the shared metal response");
        TestAssert.Equal(SurfaceFamily.Wood, MaterialResponse.ForItem(ItemId.Bow),
                "wooden ranged items use the shared wood response");
        TestAssert.Equal((byte)0, (byte)SurfaceFamily.Stone, "stone response shader tag remains zero");
        TestAssert.Equal((byte)6, (byte)SurfaceFamily.Metal, "metal response shader tag remains six");
    }

    private static void CheckPersistedIds()
    {
        foreach ((BlockId id, int expected) in PersistedBlocks)
        {
            TestAssert.Equal(expected, (int)id,
                    $"persisted block id {id} remains {expected}");
        }

        foreach ((ItemId id, int expected) in PersistedItems)
        {
            TestAssert.Equal(expected, (int)id,
                    $"persisted item id {id} remains {expected}");
        }

        // The two independently proposed ranges must not collide or alias:
        // building shapes occupy 46-51 / 91-96, transportation occupies 97-100.
        TestAssert.Equal(52, PersistedBlocks.Length, "persisted block table covers every block id");
        TestAssert.Equal(101, PersistedItems.Length, "persisted item table covers every item id");
        TestAssert.Equal(PersistedBlocks.Length, Enum.GetValues<BlockId>().Length,
                "no block id aliases another");
        TestAssert.Equal(PersistedItems.Length, Enum.GetValues<ItemId>().Length,
                "no item id aliases another");
        TestAssert.Check((int)ItemId.BrickStairs < (int)ItemId.EmptyBucket,
                "transportation ids follow every building id");
    }
}
