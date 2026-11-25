using Godot;

namespace Minecraft;

/// <summary>
/// Pins the shared item rarity/enchantment treatment, cache keys and atlas boundary.
/// </summary>
public static class ItemVisualQualityTests
{
    public static void Run()
    {
        CheckRarityClassification();
        CheckCachedQualityIcons();
        CheckFirstPersonVisualSignature();
        CheckAtlasCompatibility();
        CheckSlotRarityMarker();
    }

    private static void CheckRarityClassification()
    {
        TestAssert.Equal(ItemRarity.Common, ItemDb.RarityOf(ItemId.WoodSword),
                "starter equipment remains common");
        TestAssert.Equal(ItemRarity.Uncommon, ItemDb.RarityOf(ItemId.IronSword),
                "iron equipment is uncommon");
        TestAssert.Equal(ItemRarity.Uncommon, ItemDb.RarityOf(ItemId.GoldIngot),
                "gold materials are uncommon");
        TestAssert.Equal(ItemRarity.Uncommon, ItemDb.RarityOf(ItemId.LapisLazuli),
                "enchanting lapis is uncommon");
        TestAssert.Equal(ItemRarity.Rare, ItemDb.RarityOf(ItemId.Diamond),
                "diamond material is rare");
        TestAssert.Equal(ItemRarity.Rare, ItemDb.RarityOf(ItemId.DiamondOre),
                "diamond ore is rare without changing its block identity");
        TestAssert.Equal(ItemRarity.Rare, ItemDb.RarityOf(ItemId.DiamondChestplate),
                "diamond armor is rare");
        TestAssert.Equal(ItemRarity.Legendary, ItemDb.RarityOf(ItemId.EmberCore),
                "the Warden trophy is legendary");
        TestAssert.Equal(ItemRarity.Legendary, ItemDb.RarityOf(ItemId.EmberBlade),
                "the end-game blade is legendary");

        TestAssert.Check(ItemDb.RarityColor(ItemRarity.Uncommon)
                        != ItemDb.RarityColor(ItemRarity.Rare)
                        && ItemDb.RarityColor(ItemRarity.Rare)
                                != ItemDb.RarityColor(ItemRarity.Legendary),
                "rarity bands have distinct, shared accent colours");
    }

    private static void CheckCachedQualityIcons()
    {
        ItemStack diamondSword = ItemDb.New(ItemId.DiamondSword, 1);
        Texture2D baseSword = ItemDb.Icon(ItemId.DiamondSword);
        Texture2D rareSword = ItemDb.Icon(diamondSword);
        Texture2D cachedRareSword = ItemDb.Icon(diamondSword);
        TestAssert.Check(rareSword is ImageTexture,
                "rare equipment remains on the standalone generated-icon path");
        TestAssert.Check(!ReferenceEquals(baseSword, rareSword),
                "rare equipment receives a quality accent over its original pixels");
        TestAssert.Check(ReferenceEquals(rareSword, cachedRareSword),
                "identical item quality reuses its cached texture");

        ItemStack sharpSword = diamondSword with { Sharpness = 2 };
        Texture2D enchantedSword = ItemDb.Icon(sharpSword);
        TestAssert.Check(!ReferenceEquals(rareSword, enchantedSword),
                "Sharpness creates a distinct enchanted icon variant");
        TestAssert.Check(HasPixelDifference(((ImageTexture)rareSword).GetImage(),
                        ((ImageTexture)enchantedSword).GetImage()),
                "enchantment glint changes visible icon pixels");
        TestAssert.Check(ReferenceEquals(enchantedSword,
                        ItemDb.Icon(sharpSword with { Sharpness = 3 })),
                "enchantment levels share one visual band while tooltip levels remain exact");
        TestAssert.Check(ReferenceEquals(rareSword,
                        ItemDb.Icon(diamondSword with { Protection = 1 })),
                "unsupported enchant metadata cannot create an invalid visual treatment");

        ItemStack protectedArmor = ItemDb.New(ItemId.DiamondChestplate, 1)
                with
        { Protection = 3 };
        Texture2D protectedIcon = ItemDb.Icon(protectedArmor);
        TestAssert.Check(HasPixelDifference(
                        ((ImageTexture)ItemDb.Icon(ItemDb.New(ItemId.DiamondChestplate, 1))).GetImage(),
                        ((ImageTexture)protectedIcon).GetImage()),
                "Protection creates a distinct armor icon treatment beyond its rare facet");

        ItemStack reinforcedPick = ItemDb.New(ItemId.DiamondPick, 1)
                with
        { Unbreaking = 3 };
        Texture2D reinforcedIcon = ItemDb.Icon(reinforcedPick);
        TestAssert.Check(HasPixelDifference(
                        ((ImageTexture)ItemDb.Icon(ItemDb.New(ItemId.DiamondPick, 1))).GetImage(),
                        ((ImageTexture)reinforcedIcon).GetImage()),
                "Unbreaking creates a distinct tool icon treatment beyond its rare facet");

        Texture2D wornEnchanted = ItemDb.Icon(sharpSword with
        {
            Durability = EquipmentProgression.MaxDurability(ItemId.DiamondSword) / 2,
        });
        TestAssert.Check(!ReferenceEquals(wornEnchanted, enchantedSword),
                "wear and enchantment compose into one cached icon variant");
        Image finalPixels = ((ImageTexture)wornEnchanted).GetImage();
        TestAssert.Check(finalPixels.HasMipmaps() && finalPixels.GetMipmapCount() == 5,
                "combined rare, enchanted and worn icons retain the complete mip chain");
    }

    private static void CheckFirstPersonVisualSignature()
    {
        ItemStack sword = ItemDb.New(ItemId.DiamondSword, 1);
        byte pristine = ItemDb.IconVisualState(sword);
        byte sharp = ItemDb.IconVisualState(sword with { Sharpness = 1 });
        byte unbreaking = ItemDb.IconVisualState(sword with { Unbreaking = 1 });
        byte all = ItemDb.IconVisualState(sword with
        {
            Sharpness = 1,
            Protection = 1,
            Unbreaking = 1,
        });
        byte worn = ItemDb.IconVisualState(sword with
        {
            Sharpness = 1,
            Durability = EquipmentProgression.MaxDurability(ItemId.DiamondSword) / 2,
        });

        TestAssert.Check(pristine != sharp && sharp != unbreaking && all != sharp,
                "first-person visual state detects each enchantment family");
        TestAssert.Check(worn != sharp,
                "first-person visual state detects wear changes alongside enchantments");
        TestAssert.Equal(pristine, ItemDb.IconVisualState(sword with { Count = 7 }),
                "stack count does not trigger a held-icon refresh");
        TestAssert.Equal((byte)0, ItemDb.IconVisualState(ItemStack.Empty),
                "empty held slots have a stable zero visual state");
    }

    private static void CheckAtlasCompatibility()
    {
        AtlasTexture original = (AtlasTexture)ItemDb.Icon(ItemId.DiamondOre);
        AtlasTexture stackIcon = (AtlasTexture)ItemDb.Icon(
                new ItemStack(ItemId.DiamondOre, 1, 0, Sharpness: 1));
        int tile = BlockDb.IconTile(BlockId.DiamondOre);
        Rect2 expected = new(
                (tile % BlockDb.AtlasCols) * BlockDb.TilePx,
                (tile / BlockDb.AtlasCols) * BlockDb.TilePx,
                BlockDb.TilePx, BlockDb.TilePx);

        TestAssert.Check(ReferenceEquals(original.Atlas, stackIcon.Atlas),
                "rare block stacks continue sampling the shared block atlas");
        TestAssert.Equal(expected, stackIcon.Region,
                "rare block stacks keep their original atlas tile region");
        TestAssert.Equal(ItemDb.IconVisualState(new ItemStack(ItemId.DiamondOre, 1, 0)),
                ItemDb.IconVisualState(new ItemStack(ItemId.DiamondOre, 1, 0, Sharpness: 1)),
                "block-backed items ignore invalid enchant states that cannot change their atlas pixels");
    }

    private static void CheckSlotRarityMarker()
    {
        DurabilitySlotPanel slot = new();
        int childCount = slot.GetChildCount();
        slot.SetRarity(ItemRarity.Legendary);
        TestAssert.Equal(ItemRarity.Legendary, slot.CurrentRarity,
                "existing slot panel accepts a legendary marker");
        TestAssert.Equal(childCount, slot.GetChildCount(),
                "rarity markers reuse the slot draw pass without child controls");

        slot.SetRarity((ItemRarity)255);
        TestAssert.Equal(ItemRarity.Common, slot.CurrentRarity,
                "invalid rarity metadata safely falls back to common");
        slot.SetDurability(ItemDb.New(ItemId.DiamondSword, 1));
        TestAssert.Equal(childCount, slot.GetChildCount(),
                "rarity and durability presentation keep the original slot hierarchy");
        slot.Free();
    }

    private static bool HasPixelDifference(Image left, Image right)
    {
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
