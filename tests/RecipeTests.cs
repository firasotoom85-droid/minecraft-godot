using System.Linq;
using Godot;

namespace Minecraft;

public static class RecipeTests
{
    public static void Run()
    {
        ItemStack[] grid = new ItemStack[9];
        grid[4] = new ItemStack(ItemId.Log, 1, 0);
        (Recipe Recipe, int Row, int Col)? match = Recipes.Match(grid, 3);
        TestAssert.Check(match is not null, "single log matches planks");
        if (match is { } m)
        {
            TestAssert.Equal(ItemId.Planks, m.Recipe.Result, "planks result");
            TestAssert.Equal(1, m.Row, "pattern can slide vertically");
            TestAssert.Equal(1, m.Col, "pattern can slide horizontally");
            Recipes.ConsumeFromGrid(grid, 3, m.Recipe, m.Row, m.Col);
            TestAssert.Check(grid[4].IsEmpty, "craft consumes the log");
        }

        ItemStack[] tableGrid = new ItemStack[4];
        for (int i = 0; i < tableGrid.Length; i++)
        {
            tableGrid[i] = new ItemStack(ItemId.Planks, 1, 0);
        }

        TestAssert.Check(Recipes.Match(tableGrid, 2) is { } table && table.Recipe.Result == ItemId.CraftingTable,
                "2x2 planks match a crafting table");

        Inventory inv = new();
        inv.Add(ItemId.Log, 1);
        Recipe planks = Recipes.All.First(r => r.Result == ItemId.Planks && r.Pattern.Length == 1);
        TestAssert.Check(Recipes.CanCraft(planks, inv), "recipe book sees craftable planks");
        TestAssert.Check(Recipes.Craft(planks, inv), "one-click craft succeeds");
        TestAssert.Equal(4, inv.CountOf(ItemId.Planks), "one-click craft yields four planks");
        TestAssert.Equal(0, inv.CountOf(ItemId.Log), "one-click craft consumes the log");

        Inventory bare = new();
        TestAssert.Check(!Recipes.CanCraft(planks, bare), "recipe book refuses missing ingredients");

        Recipe torch = Recipes.All.First(r => r.Result == ItemId.Torch);
        Inventory torchInv = new();
        torchInv.Add(ItemId.Coal, 1);
        torchInv.Add(ItemId.Stick, 1);
        TestAssert.Check(Recipes.Craft(torch, torchInv), "torches craft from coal and a stick");
        TestAssert.Equal(4, torchInv.CountOf(ItemId.Torch), "torch recipe yields four");

        Recipe chest = Recipes.All.First(r => r.Result == ItemId.Chest);
        Inventory chestInv = new();
        chestInv.Add(ItemId.Planks, 8);
        TestAssert.Check(Recipes.Craft(chest, chestInv), "chest crafts from eight planks");
        TestAssert.Equal(1, chestInv.CountOf(ItemId.Chest), "chest recipe yields one");

        Recipe furnace = Recipes.All.First(r => r.Result == ItemId.Furnace);
        Inventory furnaceInv = new();
        furnaceInv.Add(ItemId.Cobblestone, 8);
        TestAssert.Check(Recipes.Craft(furnace, furnaceInv), "furnace crafts from cobblestone");
        TestAssert.Equal(1, furnaceInv.CountOf(ItemId.Furnace), "furnace recipe yields one");

        Recipe ironPick = Recipes.All.First(r => r.Result == ItemId.IronPick);
        Inventory pickInv = new();
        pickInv.Add(ItemId.IronIngot, 3);
        pickInv.Add(ItemId.Stick, 2);
        TestAssert.Check(Recipes.Craft(ironPick, pickInv), "iron pick crafts from ingots");
        TestAssert.Equal(1, pickInv.CountOf(ItemId.IronPick), "iron pick recipe yields one");

        Recipe helmet = Recipes.All.First(r => r.Result == ItemId.IronHelmet);
        Inventory armorInv = new();
        armorInv.Add(ItemId.IronIngot, 5);
        TestAssert.Check(Recipes.Craft(helmet, armorInv), "iron helmet crafts from ingots");
        TestAssert.Equal(1, armorInv.CountOf(ItemId.IronHelmet), "helmet recipe yields one");
    }
}
