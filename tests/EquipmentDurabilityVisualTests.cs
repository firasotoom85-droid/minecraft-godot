using Godot;

namespace Minecraft;

public static class EquipmentDurabilityVisualTests
{
    public static void Run()
    {
        ItemStack pick = ItemDb.New(ItemId.WoodPick, 1);
        ItemStack sword = ItemDb.New(ItemId.DiamondSword, 1);
        ItemStack chestplate = ItemDb.New(ItemId.IronChestplate, 1);
        ItemStack bow = ItemDb.New(ItemId.Bow, 1);

        TestAssert.Check(EquipmentDurabilityVisual.TryGetFraction(pick, out float pickFraction),
                "tool durability is available to the visual state helper");
        TestAssert.Equal(1.0f, pickFraction, "fresh tool displays full durability");
        TestAssert.Check(EquipmentDurabilityVisual.TryGetFraction(sword, out float swordFraction),
                "weapon durability is available to the visual state helper");
        TestAssert.Equal(1.0f, swordFraction, "fresh weapon displays full durability");
        TestAssert.Check(EquipmentDurabilityVisual.TryGetFraction(chestplate, out float armorFraction),
                "armor durability is available to the visual state helper");
        TestAssert.Equal(1.0f, armorFraction, "fresh armor displays full durability");
        TestAssert.Check(EquipmentDurabilityVisual.TryGetFraction(bow, out _),
                "bow durability is available to the visual state helper");

        TestAssert.Check(!EquipmentDurabilityVisual.TryGetFraction(ItemDb.New(ItemId.Stone, 1), out _),
                "ordinary atlas items do not display a durability meter");
        TestAssert.Check(!EquipmentDurabilityVisual.TryGetFraction(ItemStack.Empty, out _),
                "empty slots do not display a durability meter");
        TestAssert.Equal(0, EquipmentDurabilityVisual.WearStage(100, 0),
                "items without a maximum durability have no wear stage");

        TestAssert.Equal(0, EquipmentDurabilityVisual.WearStage(100, 100),
                "full durability keeps the pristine icon");
        TestAssert.Equal(0, EquipmentDurabilityVisual.WearStage(76, 100),
                "wear below the first threshold does not change the icon");
        TestAssert.Equal(1, EquipmentDurabilityVisual.WearStage(75, 100),
                "seventy-five percent durability uses the first worn icon");
        TestAssert.Equal(2, EquipmentDurabilityVisual.WearStage(50, 100),
                "half durability uses the second worn icon");
        TestAssert.Equal(3, EquipmentDurabilityVisual.WearStage(25, 100),
                "quarter durability uses the most worn icon");
        TestAssert.Equal(3, EquipmentDurabilityVisual.WearStage(0, 100),
                "zero durability remains in the most worn icon state");
        TestAssert.Equal(3, EquipmentDurabilityVisual.WearStage(-10, 100),
                "negative malformed durability clamps to the most worn state");
        TestAssert.Equal(0, EquipmentDurabilityVisual.WearStage(110, 100),
                "durability above maximum clamps to pristine");

        TestAssert.Equal(new Color(0.28f, 0.84f, 0.36f),
                EquipmentDurabilityVisual.BarColor(0.75f), "healthy equipment uses green meter fill");
        TestAssert.Equal(new Color(1.0f, 0.72f, 0.18f),
                EquipmentDurabilityVisual.BarColor(0.50f), "mid-life equipment uses amber meter fill");
        TestAssert.Equal(new Color(0.94f, 0.24f, 0.18f),
                EquipmentDurabilityVisual.BarColor(0.25f), "low durability uses red meter fill");
        TestAssert.Equal(EquipmentDurabilityVisual.BarColor(0.0f),
                EquipmentDurabilityVisual.BarColor(-1.0f), "meter color clamps invalid lower values");
        TestAssert.Equal(EquipmentDurabilityVisual.BarColor(1.0f),
                EquipmentDurabilityVisual.BarColor(2.0f), "meter color clamps invalid upper values");

        Image widerImage = Image.CreateEmpty(33, 32, false, Image.Format.Rgba8);
        Image regularImage = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        TestAssert.Check(HasPixelDifference(widerImage, regularImage),
                "pixel comparison treats mismatched image dimensions as a visual difference");

        Texture2D pristine = ItemDb.Icon(pick);
        Texture2D worn = ItemDb.Icon(pick with { Durability = 45 });
        Texture2D sameWearBand = ItemDb.Icon(pick with { Durability = 44 });
        Texture2D moreWorn = ItemDb.Icon(pick with { Durability = 30 });
        TestAssert.Check(!ReferenceEquals(pristine, worn),
                "worn tool uses a visual variant instead of the pristine icon");
        TestAssert.Check(ReferenceEquals(worn, sameWearBand),
                "tool icons share the cached texture within a wear band");
        TestAssert.Check(!ReferenceEquals(worn, moreWorn),
                "lower durability selects a distinct cached wear band");
        TestAssert.Check(HasPixelDifference(((ImageTexture)pristine).GetImage(),
                        ((ImageTexture)worn).GetImage()),
                "worn tool variant changes original icon pixels");

        foreach (ItemId id in new[]
                { ItemId.WoodSword, ItemId.Bow, ItemId.Shield, ItemId.IronHelmet,
                  ItemId.DiamondChestplate, ItemId.IronLegs, ItemId.DiamondBoots })
        {
            ItemStack equipment = ItemDb.New(id, 1);
            int maximum = EquipmentProgression.MaxDurability(id);
            Texture2D pristineEquipment = ItemDb.Icon(equipment);
            Texture2D damaged = ItemDb.Icon(equipment with { Durability = maximum / 2 });
            TestAssert.Check(damaged is ImageTexture,
                    $"{id} keeps its procedural equipment icon path when worn");
            TestAssert.Check(!ReferenceEquals(pristineEquipment, damaged),
                    $"{id} selects a separate cached icon when worn");
            if (pristineEquipment is ImageTexture pristinePixels
                    && damaged is ImageTexture wornPixels)
            {
                TestAssert.Check(HasPixelDifference(pristinePixels.GetImage(), wornPixels.GetImage()),
                        $"{id} receives visible pixel wear marks");
            }
        }

        AtlasTexture grassPristine = (AtlasTexture)ItemDb.Icon(ItemDb.New(ItemId.Grass, 64));
        AtlasTexture grassCompatibility = (AtlasTexture)ItemDb.Icon(ItemDb.New(ItemId.Grass, 64));
        TestAssert.Equal(grassPristine.Region, grassCompatibility.Region,
                "block icon region remains unchanged by the equipment wear overload");
        TestAssert.Check(ReferenceEquals(grassPristine.Atlas, grassCompatibility.Atlas),
                "block icon still samples the shared procedural atlas");

        DurabilitySlotPanel slot = new();
        int originalChildren = slot.GetChildCount();
        slot.SetDurability(pick with { Durability = 15 });
        TestAssert.Equal(originalChildren, slot.GetChildCount(),
                "durability meter reuses the existing slot panel without child controls");
        slot.SetDurability(ItemDb.New(ItemId.Stone, 1));
        TestAssert.Equal(originalChildren, slot.GetChildCount(),
                "non-durable item keeps the same lightweight slot structure");
        slot.Free();
    }

    private static bool HasPixelDifference(Image left, Image right)
    {
        // Item icon variants should be the same dimensions, but fail closed
        // before sampling if a future painter accidentally changes its canvas.
        if (left.GetSize() != right.GetSize())
        {
            return true;
        }

        for (int y = 0; y < left.GetHeight(); y++)
        {
            for (int x = 0; x < left.GetWidth(); x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
