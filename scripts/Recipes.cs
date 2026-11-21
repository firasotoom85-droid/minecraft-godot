using System.Collections.Generic;
using System.Linq;

namespace Minecraft;

/// <summary>A shaped crafting recipe: pattern rows ('#' style keys, '.' = empty).</summary>
public sealed record Recipe(
    string Name,
    string[] Pattern,
    Dictionary<char, ItemId> Key,
    ItemId Result,
    int ResultCount);

/// <summary>
/// Crafting: recipe data, grid matching (patterns slide anywhere in the grid),
/// inventory-side craftability for the one-click recipe book.
/// </summary>
public static class Recipes
{
    private static Dictionary<char, ItemId> Key(params (char, ItemId)[] pairs) =>
        pairs.ToDictionary(p => p.Item1, p => p.Item2);

    public static readonly IReadOnlyList<Recipe> All =
    [
        new("Planks", ["#"], Key(('#', ItemId.Log)), ItemId.Planks, 4),
        new("Planks", ["#"], Key(('#', ItemId.BirchLog)), ItemId.Planks, 4),
        new("Sticks", ["#", "#"], Key(('#', ItemId.Planks)), ItemId.Stick, 4),
        new("Crafting Table", ["##", "##"], Key(('#', ItemId.Planks)), ItemId.CraftingTable, 1),
        new("Torches", ["C", "S"],
                Key(('C', ItemId.Coal), ('S', ItemId.Stick)), ItemId.Torch, 4),
        new("Chest", ["###", "#.#", "###"],
                Key(('#', ItemId.Planks)), ItemId.Chest, 1),
        new("Furnace", ["###", "#.#", "###"],
                Key(('#', ItemId.Cobblestone)), ItemId.Furnace, 1),

        new("Iron Helmet", ["III", "I.I"],
                Key(('I', ItemId.IronIngot)), ItemId.IronHelmet, 1),
        new("Iron Chestplate", ["I.I", "III", "III"],
                Key(('I', ItemId.IronIngot)), ItemId.IronChestplate, 1),
        new("Iron Leggings", ["III", "I.I", "I.I"],
                Key(('I', ItemId.IronIngot)), ItemId.IronLegs, 1),
        new("Iron Boots", ["I.I", "I.I"],
                Key(('I', ItemId.IronIngot)), ItemId.IronBoots, 1),

        new("Diamond Helmet", ["DDD", "D.D"],
                Key(('D', ItemId.Diamond)), ItemId.DiamondHelmet, 1),
        new("Diamond Chestplate", ["D.D", "DDD", "DDD"],
                Key(('D', ItemId.Diamond)), ItemId.DiamondChestplate, 1),
        new("Diamond Leggings", ["DDD", "D.D", "D.D"],
                Key(('D', ItemId.Diamond)), ItemId.DiamondLegs, 1),
        new("Diamond Boots", ["D.D", "D.D"],
                Key(('D', ItemId.Diamond)), ItemId.DiamondBoots, 1),

        new("Wooden Hoe", ["PP", ".S", ".S"],
                Key(('P', ItemId.Planks), ('S', ItemId.Stick)), ItemId.WoodHoe, 1),
        new("Stone Hoe", ["PP", ".S", ".S"],
                Key(('P', ItemId.Cobblestone), ('S', ItemId.Stick)), ItemId.StoneHoe, 1),
        new("Iron Hoe", ["PP", ".S", ".S"],
                Key(('P', ItemId.IronIngot), ('S', ItemId.Stick)), ItemId.IronHoe, 1),
        new("Diamond Hoe", ["PP", ".S", ".S"],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondHoe, 1),

        new("Bread", ["WWW"], Key(('W', ItemId.Wheat)), ItemId.Bread, 1),
        new("Bed", ["WWW", "PPP"],
                Key(('W', ItemId.Wool), ('P', ItemId.Planks)), ItemId.Bed, 1),
        new("Ladders", ["S.S", "SSS", "S.S"],
                Key(('S', ItemId.Stick)), ItemId.Ladder, 3),
        new("Fishing Rod", ["S..", "SWW", "S.."],
                Key(('S', ItemId.Stick), ('W', ItemId.Wool)), ItemId.FishingRod, 1),

        new("Wooden Pickaxe", ["PPP", ".S.", ".S."],
                Key(('P', ItemId.Planks), ('S', ItemId.Stick)), ItemId.WoodPick, 1),
        new("Wooden Axe", ["PP", "PS", ".S"],
                Key(('P', ItemId.Planks), ('S', ItemId.Stick)), ItemId.WoodAxe, 1),
        new("Wooden Shovel", [".P.", ".S.", ".S."],
                Key(('P', ItemId.Planks), ('S', ItemId.Stick)), ItemId.WoodShovel, 1),

        new("Stone Pickaxe", ["PPP", ".S.", ".S."],
                Key(('P', ItemId.Cobblestone), ('S', ItemId.Stick)), ItemId.StonePick, 1),
        new("Stone Axe", ["PP", "PS", ".S"],
                Key(('P', ItemId.Cobblestone), ('S', ItemId.Stick)), ItemId.StoneAxe, 1),
        new("Stone Shovel", [".P.", ".S.", ".S."],
                Key(('P', ItemId.Cobblestone), ('S', ItemId.Stick)), ItemId.StoneShovel, 1),

        new("Iron Pickaxe", ["PPP", ".S.", ".S."],
                Key(('P', ItemId.IronIngot), ('S', ItemId.Stick)), ItemId.IronPick, 1),
        new("Iron Axe", ["PP", "PS", ".S"],
                Key(('P', ItemId.IronIngot), ('S', ItemId.Stick)), ItemId.IronAxe, 1),
        new("Iron Shovel", [".P.", ".S.", ".S."],
                Key(('P', ItemId.IronIngot), ('S', ItemId.Stick)), ItemId.IronShovel, 1),

        new("Diamond Pickaxe", ["PPP", ".S.", ".S."],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondPick, 1),
        new("Diamond Axe", ["PP", "PS", ".S"],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondAxe, 1),
        new("Diamond Shovel", [".P.", ".S.", ".S."],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondShovel, 1),
        new("Wooden Sword", ["P", "P", "S"], Key(('P', ItemId.Planks), ('S', ItemId.Stick)), ItemId.WoodSword, 1),
        new("Stone Sword", ["P", "P", "S"], Key(('P', ItemId.Cobblestone), ('S', ItemId.Stick)), ItemId.StoneSword, 1),
        new("Iron Sword", ["I", "I", "S"], Key(('I', ItemId.IronIngot), ('S', ItemId.Stick)), ItemId.IronSword, 1),
        new("Diamond Sword", ["D", "D", "S"], Key(('D', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondSword, 1),
        new("Bow", [".S.", "S.W", ".S."], Key(('S', ItemId.Stick), ('W', ItemId.Wool)), ItemId.Bow, 1),
        new("Arrows", ["G", "S", "F"], Key(('G', ItemId.Gravel), ('S', ItemId.Stick), ('F', ItemId.Feather)), ItemId.Arrow, 4),
        new("Shield", ["IPI", "IPI", ".I."], Key(('I', ItemId.IronIngot), ('P', ItemId.Planks)), ItemId.Shield, 1),
        new("Ember Blade", [".D.", "DSD", ".E."], Key(('D', ItemId.Diamond), ('S', ItemId.Stick), ('E', ItemId.EmberCore)), ItemId.EmberBlade, 1),
        new("Enchanting Table", [".D.", "ILI", ".D."], Key(('D', ItemId.Diamond), ('I', ItemId.IronIngot), ('L', ItemId.LapisLazuli)), ItemId.EnchantingTable, 1),
        new("Bucket", ["I.I", ".I."], Key(('I', ItemId.IronIngot)), ItemId.EmptyBucket, 1),
        new("Wooden Boat", ["P.P", "PPP"], Key(('P', ItemId.Planks)), ItemId.WoodenBoat, 1),

        new("Oak Slabs", ["PPP"], Key(('P', ItemId.Planks)), ItemId.PlanksSlab, 6),
        new("Cobblestone Slabs", ["CCC"], Key(('C', ItemId.Cobblestone)), ItemId.CobblestoneSlab, 6),
        new("Brick Slabs", ["BBB"], Key(('B', ItemId.Brick)), ItemId.BrickSlab, 6),
        new("Oak Stairs", ["P..", "PP.", "PPP"], Key(('P', ItemId.Planks)), ItemId.PlanksStairs, 4),
        new("Cobblestone Stairs", ["C..", "CC.", "CCC"], Key(('C', ItemId.Cobblestone)), ItemId.CobblestoneStairs, 4),
        new("Brick Stairs", ["B..", "BB.", "BBB"], Key(('B', ItemId.Brick)), ItemId.BrickStairs, 4),
    ];

    /// <summary>
    /// Match a size×size crafting grid. Returns the recipe and the top-left
    /// cell it matched at (so consuming knows which cells to take from).
    /// </summary>
    public static (Recipe Recipe, int Row, int Col)? Match(ItemStack[] grid, int size)
    {
        int minR = size;
        int maxR = -1;
        int minC = size;
        int maxC = -1;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (grid[r * size + c].IsEmpty)
                {
                    continue;
                }

                minR = System.Math.Min(minR, r);
                maxR = System.Math.Max(maxR, r);
                minC = System.Math.Min(minC, c);
                maxC = System.Math.Max(maxC, c);
            }
        }

        if (maxR < 0)
        {
            return null;
        }

        int h = maxR - minR + 1;
        int w = maxC - minC + 1;
        foreach (Recipe recipe in All)
        {
            // Compare content bounds, not raw string lengths: shovel patterns
            // deliberately leave the side columns empty (".P." / ".S." / ".S.")
            // and the grid slides to the pattern's own bounding box.
            (int rr, int rc, int rh, int rw) = Bounds(recipe.Pattern);
            if (rh != h || rw != w)
            {
                continue;
            }

            bool ok = true;
            for (int r = 0; r < rh && ok; r++)
            {
                for (int c = 0; c < rw && ok; c++)
                {
                    int pr = rr + r;
                    int pc = rc + c;
                    char ch = pc < recipe.Pattern[pr].Length ? recipe.Pattern[pr][pc] : '.';
                    ItemStack cell = grid[(minR + r) * size + (minC + c)];
                    if (ch == '.')
                    {
                        ok = cell.IsEmpty;
                    }
                    else
                    {
                        ok = !cell.IsEmpty && cell.Id == recipe.Key[ch];
                    }
                }
            }

            if (ok)
            {
                return (recipe, minR, minC);
            }
        }

        return null;
    }

    /// <summary>Consume one item from every pattern cell of a matched recipe.</summary>
    public static void ConsumeFromGrid(ItemStack[] grid, int size, Recipe recipe, int row, int col)
    {
        (int rr, int rc, int rh, int rw) = Bounds(recipe.Pattern);
        for (int r = 0; r < rh; r++)
        {
            for (int c = 0; c < rw; c++)
            {
                int pr = rr + r;
                int pc = rc + c;
                char ch = pc < recipe.Pattern[pr].Length ? recipe.Pattern[pr][pc] : '.';
                if (ch == '.')
                {
                    continue;
                }

                int i = (row + r) * size + col + c;
                grid[i] = grid[i].Count <= 1 ? ItemStack.Empty : grid[i] with { Count = grid[i].Count - 1 };
            }
        }
    }

    /// <summary>Content bounds of a pattern, ignoring empty borders.</summary>
    private static (int Row, int Col, int Height, int Width) Bounds(string[] pattern)
    {
        int row0 = pattern.Length;
        int row1 = -1;
        int col0 = int.MaxValue;
        int col1 = -1;
        for (int r = 0; r < pattern.Length; r++)
        {
            for (int c = 0; c < pattern[r].Length; c++)
            {
                if (pattern[r][c] == '.')
                {
                    continue;
                }

                row0 = System.Math.Min(row0, r);
                row1 = System.Math.Max(row1, r);
                col0 = System.Math.Min(col0, c);
                col1 = System.Math.Max(col1, c);
            }
        }

        return row1 < 0 ? (0, 0, 0, 0) : (row0, col0, row1 - row0 + 1, col1 - col0 + 1);
    }

    public static Dictionary<ItemId, int> Ingredients(Recipe recipe)
    {
        Dictionary<ItemId, int> need = [];
        foreach (string rowText in recipe.Pattern)
        {
            foreach (char ch in rowText)
            {
                if (ch == '.')
                {
                    continue;
                }

                ItemId id = recipe.Key[ch];
                need[id] = need.GetValueOrDefault(id) + 1;
            }
        }

        return need;
    }

    public static bool CanCraft(Recipe recipe, Inventory inv) =>
        Ingredients(recipe).All(kv => inv.CountOf(kv.Key) >= kv.Value);

    /// <summary>One-click craft straight from the inventory (recipe book).</summary>
    public static bool Craft(Recipe recipe, Inventory inv)
    {
        if (!CanCraft(recipe, inv) || !inv.CanFit(recipe.Result, recipe.ResultCount))
        {
            return false;
        }

        foreach ((ItemId id, int n) in Ingredients(recipe))
        {
            inv.Consume(id, n);
        }

        return inv.Add(recipe.Result, recipe.ResultCount) == 0;
    }

    public static string IngredientText(Recipe recipe) =>
        string.Join(" + ", Ingredients(recipe).Select(kv => $"{ItemDb.NameOf(kv.Key)} ×{kv.Value}"));
}
