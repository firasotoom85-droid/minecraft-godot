using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Minecraft;

/// <summary>Tool behaviour: kind, tier (1 wood .. 4 diamond), mining speed, durability.</summary>
public readonly record struct ToolInfo(ToolKind Kind, int Tier, float Speed, int Durability);

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
        _ => null,
    };

    /// <summary>Campfire cooking: raw food and ore in, cooked item out.</summary>
    public static ItemId? CooksTo(ItemId id) => id switch
    {
        ItemId.RawMutton => ItemId.CookedMutton,
        _ => null,
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
        _ => null,
    };

    public static int MaxStack(ItemId id) => ToolOf(id) != null ? 1 : 64;

    /// <summary>A fresh stack; tools start at full durability.</summary>
    public static ItemStack New(ItemId id, int count) =>
        ToolOf(id) is { } tool ? new ItemStack(id, 1, tool.Durability) : new ItemStack(id, count, 0);

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
