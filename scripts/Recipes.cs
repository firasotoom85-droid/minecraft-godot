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
                Key(('P', ItemId.RawIron), ('S', ItemId.Stick)), ItemId.IronPick, 1),
        new("Iron Axe", ["PP", "PS", ".S"],
                Key(('P', ItemId.RawIron), ('S', ItemId.Stick)), ItemId.IronAxe, 1),
        new("Iron Shovel", [".P.", ".S.", ".S."],
                Key(('P', ItemId.RawIron), ('S', ItemId.Stick)), ItemId.IronShovel, 1),

        new("Diamond Pickaxe", ["PPP", ".S.", ".S."],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondPick, 1),
        new("Diamond Axe", ["PP", "PS", ".S"],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondAxe, 1),
        new("Diamond Shovel", [".P.", ".S.", ".S."],
                Key(('P', ItemId.Diamond), ('S', ItemId.Stick)), ItemId.DiamondShovel, 1),
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
            int rh = recipe.Pattern.Length;
            int rw = recipe.Pattern.Max(p => p.Length);
            if (rh != h || rw != w)
            {
                continue;
            }

            bool ok = true;
            for (int r = 0; r < rh && ok; r++)
            {
                for (int c = 0; c < rw && ok; c++)
                {
                    char ch = c < recipe.Pattern[r].Length ? recipe.Pattern[r][c] : '.';
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
        for (int r = 0; r < recipe.Pattern.Length; r++)
        {
            for (int c = 0; c < recipe.Pattern[r].Length; c++)
            {
                if (recipe.Pattern[r][c] == '.')
                {
                    continue;
                }

                int i = (row + r) * size + col + c;
                grid[i] = grid[i].Count <= 1 ? ItemStack.Empty : grid[i] with { Count = grid[i].Count - 1 };
            }
        }
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
        if (!CanCraft(recipe, inv))
        {
            return false;
        }

        foreach ((ItemId id, int n) in Ingredients(recipe))
        {
            inv.Consume(id, n);
        }

        inv.Add(recipe.Result, recipe.ResultCount);
        return true;
    }

    public static string IngredientText(Recipe recipe) =>
        string.Join(" + ", Ingredients(recipe).Select(kv => $"{ItemDb.NameOf(kv.Key)} ×{kv.Value}"));
}
