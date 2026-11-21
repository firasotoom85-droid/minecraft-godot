using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Minecraft;

/// <summary>Tool behaviour: kind, tier (1 wood .. 4 diamond), mining speed, durability.</summary>
public readonly record struct ToolInfo(ToolKind Kind, int Tier, float Speed, int Durability);

/// <summary>Armor behaviour: slot 0 helmet .. 3 boots, defense points and durability.</summary>
public readonly record struct ArmorInfo(int Slot, int Points, int Durability, int Tier);
public readonly record struct WeaponInfo(float Damage, float AttackSpeed, int Durability, int Tier, float Knockback = 3.0f);

/// <summary>
/// Item database: names, stack sizes, block↔item mapping, tool stats and
/// procedurally painted icons (block items reuse the atlas; tools and
/// materials are drawn as pixel art here).
/// </summary>
public static class ItemDb
{
    private static readonly (ItemId Item, BlockId Block)[] BlockItemPairs =
    [
        (ItemId.Grass, BlockId.Grass), (ItemId.Dirt, BlockId.Dirt), (ItemId.Stone, BlockId.Stone),
        (ItemId.Sand, BlockId.Sand), (ItemId.Gravel, BlockId.Gravel), (ItemId.Log, BlockId.Log),
        (ItemId.BirchLog, BlockId.BirchLog), (ItemId.Leaves, BlockId.Leaves),
        (ItemId.BirchLeaves, BlockId.BirchLeaves), (ItemId.SnowGrass, BlockId.SnowGrass),
        (ItemId.Snow, BlockId.Snow), (ItemId.Glass, BlockId.Glass), (ItemId.Brick, BlockId.Brick),
        (ItemId.Cactus, BlockId.Cactus), (ItemId.Planks, BlockId.Planks),
        (ItemId.CraftingTable, BlockId.CraftingTable), (ItemId.Cobblestone, BlockId.Cobblestone),
        (ItemId.CoalOre, BlockId.CoalOre), (ItemId.IronOre, BlockId.IronOre),
        (ItemId.GoldOre, BlockId.GoldOre), (ItemId.DiamondOre, BlockId.DiamondOre),
        (ItemId.Wool, BlockId.Wool),
        (ItemId.Lava, BlockId.Lava), (ItemId.TallGrass, BlockId.TallGrass),
        (ItemId.FlowerRed, BlockId.FlowerRed), (ItemId.FlowerYellow, BlockId.FlowerYellow),
        (ItemId.Torch, BlockId.Torch),
        (ItemId.Chest, BlockId.Chest),
        (ItemId.Furnace, BlockId.Furnace),
        (ItemId.Bed, BlockId.Bed),
        (ItemId.Ladder, BlockId.Ladder),
        (ItemId.EnchantingTable, BlockId.EnchantingTable),
        (ItemId.PlanksSlab, BlockId.PlanksSlab),
        (ItemId.CobblestoneSlab, BlockId.CobblestoneSlab),
        (ItemId.BrickSlab, BlockId.BrickSlab),
        (ItemId.PlanksStairs, BlockId.PlanksStairs),
        (ItemId.CobblestoneStairs, BlockId.CobblestoneStairs),
        (ItemId.BrickStairs, BlockId.BrickStairs),
    ];

    private static readonly Dictionary<BlockId, ItemId> BlockToItem =
        BlockItemPairs.ToDictionary(p => p.Block, p => p.Item);

    private static readonly Dictionary<ItemId, BlockId> ItemToBlock =
        BlockItemPairs.ToDictionary(p => p.Item, p => p.Block);

    private static readonly Dictionary<ItemId, ImageTexture> IconCache = [];

    public static BlockId? BlockFor(ItemId id) =>
        ItemToBlock.TryGetValue(id, out BlockId block) ? block : null;

    public static ItemId BlockItem(BlockId id) =>
        BlockToItem.TryGetValue(id, out ItemId item) ? item : ItemId.None;

    /// <summary>All placeable block items, for the palette browser.</summary>
    public static IReadOnlyList<ItemId> Placeable => BlockItemPairs.Select(p => p.Item).ToArray();

    public static string NameOf(ItemId id)
    {
        if (BlockFor(id) is { } block)
        {
            return BlockDb.NameOf(block);
        }

        return id switch
        {
            ItemId.None => "Nothing",
            ItemId.Coal => "Coal",
            ItemId.RawIron => "Raw Iron",
            ItemId.RawGold => "Raw Gold",
            ItemId.IronIngot => "Iron Ingot",
            ItemId.GoldIngot => "Gold Ingot",
            ItemId.Diamond => "Diamond",
            ItemId.Stick => "Stick",
            ItemId.WoodPick => "Wooden Pickaxe",
            ItemId.WoodAxe => "Wooden Axe",
            ItemId.WoodShovel => "Wooden Shovel",
            ItemId.StonePick => "Stone Pickaxe",
            ItemId.StoneAxe => "Stone Axe",
            ItemId.StoneShovel => "Stone Shovel",
            ItemId.IronPick => "Iron Pickaxe",
            ItemId.IronAxe => "Iron Axe",
            ItemId.IronShovel => "Iron Shovel",
            ItemId.DiamondPick => "Diamond Pickaxe",
            ItemId.DiamondAxe => "Diamond Axe",
            ItemId.DiamondShovel => "Diamond Shovel",
            ItemId.Apple => "Apple",
            ItemId.RawMutton => "Raw Mutton",
            ItemId.CookedMutton => "Cooked Mutton",
            ItemId.Wool => "Wool",
            ItemId.IronHelmet => "Iron Helmet",
            ItemId.IronChestplate => "Iron Chestplate",
            ItemId.IronLegs => "Iron Leggings",
            ItemId.IronBoots => "Iron Boots",
            ItemId.DiamondHelmet => "Diamond Helmet",
            ItemId.DiamondChestplate => "Diamond Chestplate",
            ItemId.DiamondLegs => "Diamond Leggings",
            ItemId.DiamondBoots => "Diamond Boots",
            ItemId.Seeds => "Wheat Seeds",
            ItemId.Wheat => "Wheat",
            ItemId.Bread => "Bread",
            ItemId.WoodHoe => "Wooden Hoe",
            ItemId.StoneHoe => "Stone Hoe",
            ItemId.IronHoe => "Iron Hoe",
            ItemId.DiamondHoe => "Diamond Hoe",
            ItemId.RawBeef => "Raw Beef",
            ItemId.CookedBeef => "Steak",
            ItemId.RawPork => "Raw Porkchop",
            ItemId.CookedPork => "Cooked Porkchop",
            ItemId.RawChicken => "Raw Chicken",
            ItemId.CookedChicken => "Cooked Chicken",
            ItemId.Leather => "Leather",
            ItemId.Feather => "Feather",
            ItemId.EmberCore => "Ember Core",
            ItemId.FishingRod => "Fishing Rod",
            ItemId.RawFish => "Raw Fish",
            ItemId.CookedFish => "Cooked Fish",
            ItemId.WoodSword => "Wooden Sword", ItemId.StoneSword => "Stone Sword", ItemId.IronSword => "Iron Sword", ItemId.DiamondSword => "Diamond Sword",
            ItemId.Bow => "Bow", ItemId.Arrow => "Arrow", ItemId.Shield => "Shield", ItemId.EmberBlade => "Ember Blade", ItemId.LapisLazuli => "Lapis Lazuli",
            ItemId.EmptyBucket => "Bucket", ItemId.WaterBucket => "Water Bucket",
            ItemId.LavaBucket => "Lava Bucket", ItemId.WoodenBoat => "Wooden Boat",
            _ => id.ToString(),
        };
    }

    /// <summary>Food value: hunger points restored, plus a small heal for cooked food.</summary>
    public readonly record struct FoodInfo(float Hunger, float Heal = 0.0f);

    public static FoodInfo? FoodOf(ItemId id) => id switch
    {
        ItemId.Apple => new FoodInfo(3.0f),
        ItemId.RawMutton => new FoodInfo(2.0f),
        ItemId.CookedMutton => new FoodInfo(6.0f, 1.0f),
        ItemId.Bread => new FoodInfo(5.0f, 1.0f),
        ItemId.RawBeef => new FoodInfo(2.0f),
        ItemId.CookedBeef => new FoodInfo(7.0f, 1.0f),
        ItemId.RawPork => new FoodInfo(2.0f),
        ItemId.CookedPork => new FoodInfo(7.0f, 1.0f),
        ItemId.RawChicken => new FoodInfo(1.0f),
        ItemId.CookedChicken => new FoodInfo(5.0f, 1.0f),
        ItemId.RawFish => new FoodInfo(2.0f),
        ItemId.CookedFish => new FoodInfo(5.0f, 1.0f),
        _ => null,
    };

    /// <summary>Campfire cooking: raw food and ore in, cooked item out.</summary>
    public static ItemId? CooksTo(ItemId id) => id switch
    {
        ItemId.RawMutton => ItemId.CookedMutton,
        ItemId.RawBeef => ItemId.CookedBeef,
        ItemId.RawPork => ItemId.CookedPork,
        ItemId.RawChicken => ItemId.CookedChicken,
        ItemId.RawFish => ItemId.CookedFish,
        _ => null,
    };

    /// <summary>Furnace recipes (one input item becomes one result).</summary>
    public static ItemId? SmeltsTo(ItemId id) => id switch
    {
        ItemId.RawIron => ItemId.IronIngot,
        ItemId.RawGold => ItemId.GoldIngot,
        ItemId.Sand => ItemId.Glass,
        ItemId.Cobblestone => ItemId.Stone,
        ItemId.RawMutton => ItemId.CookedMutton,
        ItemId.RawBeef => ItemId.CookedBeef,
        ItemId.RawPork => ItemId.CookedPork,
        ItemId.RawChicken => ItemId.CookedChicken,
        ItemId.RawFish => ItemId.CookedFish,
        _ => null,
    };

    /// <summary>Seconds one fuel item keeps a furnace burning; 0 = not a fuel.</summary>
    public static float FuelSeconds(ItemId id) => id switch
    {
        ItemId.Coal => 80.0f,
        ItemId.Log => 60.0f,
        ItemId.BirchLog => 60.0f,
        ItemId.Planks => 15.0f,
        ItemId.Stick => 5.0f,
        _ => 0.0f,
    };

    public static ToolInfo? ToolOf(ItemId id) => id switch
    {
        ItemId.WoodPick => new(ToolKind.Pick, 1, 2.5f, 60),
        ItemId.WoodAxe => new(ToolKind.Axe, 1, 2.5f, 60),
        ItemId.WoodShovel => new(ToolKind.Shovel, 1, 2.5f, 60),
        ItemId.StonePick => new(ToolKind.Pick, 2, 4.0f, 130),
        ItemId.StoneAxe => new(ToolKind.Axe, 2, 4.0f, 130),
        ItemId.StoneShovel => new(ToolKind.Shovel, 2, 4.0f, 130),
        ItemId.IronPick => new(ToolKind.Pick, 3, 6.0f, 250),
        ItemId.IronAxe => new(ToolKind.Axe, 3, 6.0f, 250),
        ItemId.IronShovel => new(ToolKind.Shovel, 3, 6.0f, 250),
        ItemId.DiamondPick => new(ToolKind.Pick, 4, 8.0f, 450),
        ItemId.DiamondAxe => new(ToolKind.Axe, 4, 8.0f, 450),
        ItemId.DiamondShovel => new(ToolKind.Shovel, 4, 8.0f, 450),
        ItemId.WoodHoe => new(ToolKind.Hoe, 1, 2.5f, 60),
        ItemId.StoneHoe => new(ToolKind.Hoe, 2, 4.0f, 130),
        ItemId.IronHoe => new(ToolKind.Hoe, 3, 6.0f, 250),
        ItemId.DiamondHoe => new(ToolKind.Hoe, 4, 8.0f, 450),
        _ => null,
    };

    public static ArmorInfo? ArmorOf(ItemId id) => id switch
    {
        ItemId.IronHelmet => new(0, 2, 200, 3),
        ItemId.IronChestplate => new(1, 6, 300, 3),
        ItemId.IronLegs => new(2, 5, 250, 3),
        ItemId.IronBoots => new(3, 2, 180, 3),
        ItemId.DiamondHelmet => new(0, 3, 400, 4),
        ItemId.DiamondChestplate => new(1, 8, 600, 4),
        ItemId.DiamondLegs => new(2, 6, 500, 4),
        ItemId.DiamondBoots => new(3, 3, 350, 4),
        _ => null,
    };

    public static WeaponInfo? WeaponOf(ItemId id) => id switch
    {
        ItemId.WoodSword => new(4, 0.85f, 70, 1, 2.8f), ItemId.StoneSword => new(5, 0.72f, 150, 2, 3.2f),
        ItemId.IronSword => new(6, 0.62f, 320, 3, 3.5f), ItemId.DiamondSword => new(7, 0.55f, 900, 4, 4.0f),
        ItemId.EmberBlade => new(8, 0.52f, 1200, 5, 4.5f), ItemId.Bow => new(5, 0.8f, 384, 2, 0),
        ItemId.Shield => new(0, 0, 420, 2, 0), _ => null,
    };
    public static string DescriptionOf(ItemId id) => id switch
    {
        ItemId.WoodSword => "Fast starter weapon; limited durability.", ItemId.StoneSword => "Reliable cave weapon with improved stopping force.",
        ItemId.IronSword => "Durable blade forged from smelted iron.", ItemId.DiamondSword => "Rare, powerful blade for deep expeditions.",
        ItemId.EmberBlade => "A Warden-forged end-game blade.", ItemId.Bow => "Ranged weapon; consumes one arrow per shot.",
        ItemId.Arrow => "Bow ammunition crafted from gravel, sticks and feathers.", ItemId.Shield => "Blocks frontal melee and ranged hits while held; blocking wears it.", ItemId.LapisLazuli => "A blue mineral used to power enchantments.", ItemId.EnchantingTable => "A crafted station for enchanting and upgrading equipment.",
        ItemId.PlanksSlab or ItemId.CobblestoneSlab or ItemId.BrickSlab => "A half-height building block. Place against a face to choose the top or bottom half; stack a matching slab to make a full block.",
        ItemId.PlanksStairs or ItemId.CobblestoneStairs or ItemId.BrickStairs => "A stepped building block. Facing follows your view; upper-half placement turns the stairs upside down.",
        ItemId.EmptyBucket => "Collects source water or lava. Flowing water cannot be scooped.",
        ItemId.WaterBucket => "Places one water source into a valid open voxel.",
        ItemId.LavaBucket => "Places one lava source into a valid open voxel.",
        ItemId.WoodenBoat => "Place on water, right-click to board, Space to dismount, Shift to brake.",
        _ => id.ToString(),
    };
    public static int MaxStack(ItemId id) =>
        id is ItemId.FishingRod or ItemId.EmptyBucket or ItemId.WaterBucket
                or ItemId.LavaBucket or ItemId.WoodenBoat
                || ToolOf(id) != null || ArmorOf(id) != null || WeaponOf(id) != null ? 1 : 64;

    /// <summary>A fresh stack; tools and armor start at full durability.</summary>
    public static ItemStack New(ItemId id, int count) =>
        ToolOf(id) is { } tool ? new ItemStack(id, 1, tool.Durability)
        : ArmorOf(id) is { } armor ? new ItemStack(id, 1, armor.Durability)
        : WeaponOf(id) is { } weapon ? new ItemStack(id, 1, weapon.Durability)
        : new ItemStack(id, count, 0);

    /// <summary>The pre-items hotbar, handed to saves that never had an inventory.</summary>
    public static (int Slot, ItemId Id, int Count)[] StarterKit() =>
    [
        (0, ItemId.Grass, 64), (1, ItemId.Dirt, 64), (2, ItemId.Stone, 64), (3, ItemId.Sand, 64),
        (4, ItemId.Log, 64), (5, ItemId.Planks, 64), (6, ItemId.Cobblestone, 64),
        (7, ItemId.Glass, 64), (8, ItemId.Leaves, 64),
    ];

    /// <summary>Loose name lookup for the dev console (/give).</summary>
    public static ItemId ByName(string query)
    {
        string q = Normalize(query);
        if (q.Length == 0)
        {
            return ItemId.None;
        }

        foreach (ItemId id in Enum.GetValues<ItemId>())
        {
            if (id != ItemId.None && (Normalize(id.ToString()) == q || Normalize(NameOf(id)) == q))
            {
                return id;
            }
        }

        return ItemId.None;
    }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // -----------------------------------------------------------------------
    // Icons
    // -----------------------------------------------------------------------

    public static Texture2D Icon(ItemId id)
    {
        if (BlockFor(id) is { } block)
        {
            int tile = BlockDb.IconTile(block);
            return new AtlasTexture
            {
                Atlas = BlockDb.AtlasTexture(),
                Region = new Rect2(
                    (tile % BlockDb.AtlasCols) * BlockDb.TilePx,
                    (tile / BlockDb.AtlasCols) * BlockDb.TilePx,
                    BlockDb.TilePx, BlockDb.TilePx),
            };
        }

        if (!IconCache.TryGetValue(id, out ImageTexture? texture))
        {
            texture = ImageTexture.CreateFromImage(BuildIconImage(id));
            IconCache[id] = texture;
        }

        return texture;
    }

    private const int IconPx = 32;

    private static Image BuildIconImage(ItemId id)
    {
        Image img = Image.CreateEmpty(IconPx, IconPx, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        if (ToolOf(id) is { } tool)
        {
            PaintTool(img, tool);
        }
        else if (ArmorOf(id) is { } armor)
        {
            PaintArmor(img, armor);
        }
        else if (WeaponOf(id) is { } weapon)
        {
            PaintWeapon(img, id, weapon);
        }
        else
        {
            switch (id)
            {
                case ItemId.Coal:
                    PaintBlob(img, new Color(0.12f, 0.12f, 0.14f), new Color(0.05f, 0.05f, 0.06f), 121);
                    break;
                case ItemId.RawIron:
                    PaintBlob(img, new Color(0.82f, 0.58f, 0.44f), new Color(0.62f, 0.40f, 0.30f), 122);
                    break;
                case ItemId.RawGold:
                    PaintBlob(img, new Color(0.92f, 0.76f, 0.28f), new Color(0.70f, 0.55f, 0.16f), 123);
                    break;
                case ItemId.IronIngot:
                    PaintIngot(img, new Color(0.82f, 0.82f, 0.86f), new Color(0.58f, 0.58f, 0.64f));
                    break;
                case ItemId.GoldIngot:
                    PaintIngot(img, new Color(0.95f, 0.80f, 0.30f), new Color(0.72f, 0.54f, 0.12f));
                    break;
                case ItemId.Seeds:
                    PaintSeeds(img);
                    break;
                case ItemId.Wheat:
                    PaintWheatItem(img);
                    break;
                case ItemId.Bread:
                    PaintBread(img);
                    break;
                case ItemId.RawBeef:
                    PaintMutton(img, new Color(0.72f, 0.18f, 0.18f), new Color(0.90f, 0.52f, 0.48f));
                    break;
                case ItemId.CookedBeef:
                    PaintMutton(img, new Color(0.42f, 0.20f, 0.09f), new Color(0.70f, 0.42f, 0.18f));
                    break;
                case ItemId.RawPork:
                    PaintMutton(img, new Color(0.88f, 0.46f, 0.48f), new Color(0.98f, 0.72f, 0.72f));
                    break;
                case ItemId.CookedPork:
                    PaintMutton(img, new Color(0.50f, 0.24f, 0.12f), new Color(0.75f, 0.46f, 0.22f));
                    break;
                case ItemId.RawChicken:
                    PaintMutton(img, new Color(0.92f, 0.62f, 0.52f), new Color(0.98f, 0.82f, 0.72f));
                    break;
                case ItemId.CookedChicken:
                    PaintMutton(img, new Color(0.70f, 0.46f, 0.18f), new Color(0.90f, 0.68f, 0.34f));
                    break;
                case ItemId.Leather:
                    Rect(img, 7, 10, 18, 13, new Color(0.48f, 0.28f, 0.13f));
                    Rect(img, 9, 12, 14, 9, new Color(0.62f, 0.38f, 0.18f));
                    break;
                case ItemId.Feather:
                    Rect(img, 15, 5, 2, 22, new Color(0.90f, 0.90f, 0.86f));
                    for (int y = 7; y < 20; y += 3)
                    {
                        Rect(img, 11, y, 5, 2, new Color(0.96f, 0.96f, 0.92f));
                        Rect(img, 16, y + 1, 5, 2, new Color(0.88f, 0.88f, 0.84f));
                    }
                    break;
                case ItemId.EmberCore:
                    PaintDiamond(img);
                    Rect(img, 12, 12, 8, 8, new Color(1.0f, 0.55f, 0.12f));
                    Rect(img, 14, 14, 4, 4, new Color(1.0f, 0.92f, 0.55f));
                    break;
                case ItemId.FishingRod:
                    PaintFishingRod(img);
                    break;
                case ItemId.RawFish:
                    PaintMutton(img, new Color(0.52f, 0.72f, 0.78f), new Color(0.78f, 0.90f, 0.92f));
                    break;
                case ItemId.CookedFish:
                    PaintMutton(img, new Color(0.70f, 0.52f, 0.26f), new Color(0.92f, 0.78f, 0.46f));
                    break;
                case ItemId.Arrow:
                    Rect(img, 6, 15, 20, 2, new Color(0.72f, 0.72f, 0.74f)); Rect(img, 24, 12, 5, 8, new Color(0.78f, 0.78f, 0.80f)); break;
                case ItemId.EmptyBucket:
                    PaintBucket(img, new Color(0.76f, 0.78f, 0.82f));
                    break;
                case ItemId.WaterBucket:
                    PaintBucket(img, new Color(0.20f, 0.55f, 0.92f));
                    break;
                case ItemId.LavaBucket:
                    PaintBucket(img, new Color(1.0f, 0.38f, 0.08f));
                    break;
                case ItemId.WoodenBoat:
                    PaintBoat(img);
                    break;
                case ItemId.LapisLazuli:
                    PaintBlob(img, new Color(0.12f, 0.30f, 0.86f), new Color(0.06f, 0.14f, 0.52f), 125);
                    break;
                case ItemId.Diamond:
                    PaintDiamond(img);
                    break;
                case ItemId.Stick:
                    PaintStick(img);
                    break;
                case ItemId.Apple:
                    PaintApple(img);
                    break;
                case ItemId.RawMutton:
                    PaintMutton(img, new Color(0.85f, 0.36f, 0.38f), new Color(0.94f, 0.68f, 0.66f));
                    break;
                case ItemId.CookedMutton:
                    PaintMutton(img, new Color(0.55f, 0.32f, 0.16f), new Color(0.78f, 0.55f, 0.30f));
                    break;
            }
        }

        return img;
    }

    private static Color TierColor(int tier) => tier switch
    {
        1 => new Color(0.64f, 0.47f, 0.28f),
        2 => new Color(0.56f, 0.56f, 0.58f),
        3 => new Color(0.85f, 0.85f, 0.88f),
        4 => new Color(0.36f, 0.88f, 0.88f),
        _ => Colors.White,
    };

    private static void PaintTool(Image img, ToolInfo tool)
    {
        Color handle = new(0.45f, 0.32f, 0.20f);
        Color head = TierColor(tool.Tier);
        // Diagonal handle from bottom-right to upper-left.
        for (int i = 0; i < 20; i++)
        {
            int x = 25 - i;
            int y = 27 - i;
            for (int t = 0; t < 3; t++)
            {
                Rect(img, x + t, y, 1, 1, handle);
            }
        }

        switch (tool.Kind)
        {
            case ToolKind.Pick:
                Rect(img, 4, 8, 17, 4, head);
                Rect(img, 2, 10, 4, 4, head);
                Rect(img, 19, 10, 4, 4, head);
                break;
            case ToolKind.Axe:
                Rect(img, 8, 3, 12, 12, head);
                Rect(img, 8, 13, 7, 3, head);
                break;
            case ToolKind.Shovel:
                Rect(img, 8, 3, 9, 10, head);
                Rect(img, 10, 11, 5, 3, head);
                break;
            case ToolKind.Hoe:
                Rect(img, 4, 5, 17, 5, head);
                Rect(img, 18, 8, 4, 4, head);
                break;
        }
    }

    private static void PaintWeapon(Image img, ItemId id, WeaponInfo weapon)
    {
        Color metal = weapon.Tier switch { 1 => new Color(0.62f, 0.43f, 0.25f), 2 => new Color(0.62f, 0.64f, 0.68f), 3 => new Color(0.84f, 0.86f, 0.90f), 4 => new Color(0.30f, 0.88f, 0.90f), _ => new Color(1, 0.48f, 0.12f) };
        Color wood = new(0.42f, 0.25f, 0.13f);
        if (id == ItemId.Bow) { for (int y=3;y<29;y++) Rect(img, y<16?10-(y-3)/3:6+(y-16)/3, y, 3, 2, wood); Rect(img,12,3,1,26,Colors.Beige); return; }
        if (id == ItemId.Shield) { Rect(img,6,4,20,23,wood); Rect(img,9,7,14,17,metal); return; }
        for(int i=0;i<18;i++) Rect(img,24-i,27-i,3,3,wood);
        Rect(img,5,4,6,22,metal); Rect(img,2,5,12,4,metal.Lightened(0.12f)); Rect(img,8,3,7,4,metal);
        if(id==ItemId.EmberBlade) Rect(img,4,8,8,3,new Color(1,0.55f,0.1f));
    }

    private static void PaintBucket(Image img, Color liquid)
    {
        Color metal = new(0.70f, 0.73f, 0.77f);
        Rect(img, 7, 7, 18, 3, metal);
        Rect(img, 9, 10, 3, 14, metal);
        Rect(img, 20, 10, 3, 14, metal);
        Rect(img, 11, 23, 10, 3, metal);
        Rect(img, 12, 15, 8, 7, liquid);
        Rect(img, 12, 13, 8, 2, liquid.Lightened(0.18f));
    }

    private static void PaintBoat(Image img)
    {
        Color wood = new(0.48f, 0.28f, 0.14f);
        Rect(img, 5, 14, 22, 4, wood.Lightened(0.16f));
        Rect(img, 7, 18, 18, 5, wood);
        Rect(img, 10, 23, 12, 3, wood.Darkened(0.16f));
        Rect(img, 8, 11, 3, 5, wood);
        Rect(img, 21, 11, 3, 5, wood);
        Rect(img, 13, 11, 7, 3, wood.Lightened(0.1f));
    }

    private static void PaintFishingRod(Image img)
    {
        Color rod = new(0.55f, 0.38f, 0.18f);
        Color line = new(0.86f, 0.86f, 0.82f);
        for (int i = 0; i < 20; i++)
        {
            Rect(img, 7 + i, 24 - i, 2, 2, rod);
        }

        for (int i = 0; i < 12; i++)
        {
            Rect(img, 26, 5 + i, 1, 1, line);
        }

        Rect(img, 25, 16, 2, 2, line);
        Rect(img, 24, 17, 2, 2, line);
        Rect(img, 25, 19, 2, 2, new Color(0.70f, 0.70f, 0.66f));
    }

    private static void PaintSeeds(Image img)
    {
        Color seed = new(0.56f, 0.68f, 0.22f);
        Rect(img, 10, 10, 4, 3, seed);
        Rect(img, 17, 9, 4, 3, seed.Lightened(0.1f));
        Rect(img, 13, 17, 4, 3, seed.Darkened(0.1f));
        Rect(img, 19, 18, 3, 3, seed);
        Rect(img, 8, 20, 3, 3, seed.Darkened(0.15f));
    }

    private static void PaintWheatItem(Image img)
    {
        Color stem = new(0.72f, 0.56f, 0.18f);
        Color grain = new(0.95f, 0.80f, 0.26f);
        for (int y = 8; y < 28; y++)
        {
            Rect(img, 15, y, 2, 1, stem);
        }

        for (int y = 8; y < 20; y += 4)
        {
            Rect(img, 10, y, 6, 3, grain);
            Rect(img, 17, y + 2, 6, 3, grain.Darkened(0.08f));
        }
    }

    private static void PaintBread(Image img)
    {
        Color crust = new(0.58f, 0.34f, 0.14f);
        Color top = new(0.80f, 0.54f, 0.25f);
        for (int y = 12; y < 24; y++)
        {
            for (int x = 6; x < 26; x++)
            {
                float d = new Vector2((x - 16) / 10.0f, (y - 18) / 6.0f).Length();
                if (d <= 1.0f)
                {
                    Rect(img, x, y, 1, 1, y < 17 ? top : crust);
                }
            }
        }

        Rect(img, 10, 14, 4, 2, top.Lightened(0.15f));
        Rect(img, 18, 14, 3, 2, top.Lightened(0.1f));
    }

    private static void PaintIngot(Image img, Color light, Color dark)
    {
        // A small isometric bar: top face, front face and a darker side.
        for (int y = 10; y < 20; y++)
        {
            for (int x = 7; x < 25; x++)
            {
                bool top = y < 14;
                bool side = x > 20;
                Color c = top ? light : side ? dark : light.Darkened(0.12f);
                Rect(img, x, y, 1, 1, c);
            }
        }

        Rect(img, 9, 11, 14, 2, light.Lightened(0.15f));
    }

    private static void PaintArmor(Image img, ArmorInfo armor)
    {
        Color metal = TierColor(armor.Tier);
        Color shade = metal.Darkened(0.25f);
        Color dark = metal.Darkened(0.45f);

        switch (armor.Slot)
        {
            case 0: // helmet
                Rect(img, 8, 8, 16, 7, metal);
                Rect(img, 6, 13, 20, 5, metal);
                Rect(img, 10, 14, 12, 5, dark);
                Rect(img, 8, 9, 3, 3, metal.Lightened(0.2f));
                break;
            case 1: // chestplate
                Rect(img, 7, 8, 18, 14, metal);
                Rect(img, 4, 10, 5, 9, shade);
                Rect(img, 23, 10, 5, 9, shade);
                Rect(img, 12, 17, 8, 7, dark);
                Rect(img, 9, 9, 4, 3, metal.Lightened(0.2f));
                break;
            case 2: // leggings
                Rect(img, 8, 8, 16, 6, metal);
                Rect(img, 9, 14, 5, 14, metal);
                Rect(img, 18, 14, 5, 14, metal);
                Rect(img, 10, 24, 3, 4, dark);
                Rect(img, 19, 24, 3, 4, dark);
                break;
            default: // boots
                Rect(img, 8, 16, 6, 10, metal);
                Rect(img, 18, 16, 6, 10, metal);
                Rect(img, 6, 24, 8, 4, shade);
                Rect(img, 18, 24, 8, 4, shade);
                break;
        }
    }

    private static void PaintBlob(Image img, Color baseColor, Color dark, int seed)
    {
        RandomNumberGenerator rng = new()
        {
            Seed = (ulong)seed,
        };
        Vector2 center = new(16, 17);
        for (int y = 0; y < IconPx; y++)
        {
            for (int x = 0; x < IconPx; x++)
            {
                float d = new Vector2(x - center.X, y - center.Y).Length();
                float edge = 8.5f + rng.RandfRange(-1.6f, 1.6f);
                if (d < edge)
                {
                    Color c = d > edge - 3.0f ? dark : baseColor;
                    img.SetPixel(x, y, c.Lightened(rng.RandfRange(0.0f, 0.12f)));
                }
            }
        }
    }

    private static void PaintDiamond(Image img)
    {
        Color light = new(0.62f, 0.98f, 0.98f);
        Color baseColor = new(0.30f, 0.84f, 0.84f);
        for (int y = 0; y < IconPx; y++)
        {
            for (int x = 0; x < IconPx; x++)
            {
                int dx = Math.Abs(x - 16);
                int dy = Math.Abs(y - 16);
                if (dx + dy <= 10)
                {
                    Color c = dx + dy > 7 ? baseColor : light;
                    img.SetPixel(x, y, c);
                }
            }
        }
    }

    private static void PaintStick(Image img)
    {
        Color c = new(0.48f, 0.35f, 0.22f);
        for (int i = 0; i < 18; i++)
        {
            Rect(img, 22 - i, 24 - i, 3, 3, c);
        }
    }

    private static void PaintApple(Image img)
    {
        Color body = new(0.80f, 0.14f, 0.16f);
        Color light = new(0.92f, 0.32f, 0.28f);
        for (int y = 0; y < IconPx; y++)
        {
            for (int x = 0; x < IconPx; x++)
            {
                float d = new Vector2(x - 16.0f, y - 18.5f).Length();
                if (d < 9.5f)
                {
                    img.SetPixel(x, y, x < 14 && y < 16 ? light : body);
                }
            }
        }

        Rect(img, 15, 4, 2, 7, new Color(0.42f, 0.28f, 0.15f));           // stem
        Rect(img, 17, 5, 5, 3, new Color(0.24f, 0.50f, 0.18f));           // leaf
    }

    private static void PaintMutton(Image img, Color meat, Color fat)
    {
        for (int y = 0; y < IconPx; y++)
        {
            for (int x = 0; x < IconPx; x++)
            {
                float d = new Vector2(x - 15.0f, y - 17.0f).Length();
                if (d < 9.0f)
                {
                    bool marbled = ((x * 5 + y * 3) % 9) < 2;
                    img.SetPixel(x, y, marbled ? fat : meat);
                }
            }
        }

        Rect(img, 21, 20, 3, 3, new Color(0.90f, 0.88f, 0.82f));          // bone
        Rect(img, 23, 22, 3, 3, new Color(0.86f, 0.84f, 0.78f));
    }

    private static void Rect(Image img, int x0, int y0, int w, int h, Color c)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                if (x >= 0 && x < IconPx && y >= 0 && y < IconPx)
                {
                    img.SetPixel(x, y, c);
                }
            }
        }
    }
}
