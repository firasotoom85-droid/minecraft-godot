using System.Linq;
using Godot;

namespace Minecraft;

public static class FarmingTests
{
    public static void Run()
    {
        TestAssert.Check(BlockDb.IsWheat(BlockId.Wheat0), "stage 0 is wheat");
        TestAssert.Check(BlockDb.IsWheat(BlockId.Wheat1), "stage 1 is wheat");
        TestAssert.Check(BlockDb.IsWheat(BlockId.Wheat2), "stage 2 is wheat");
        TestAssert.Check(BlockDb.IsCross(BlockId.Wheat0), "wheat uses the cross mesh");
        TestAssert.Check(BlockDb.DefOf(BlockId.Farmland).Drop == ItemId.Dirt, "farmland drops dirt");

        ToolInfo? hoe = ItemDb.ToolOf(ItemId.WoodHoe);
        TestAssert.Check(hoe is { Kind: ToolKind.Hoe }, "wood hoe is a hoe");
        TestAssert.Equal(60, hoe?.Durability, "wood hoe durability");

        ItemDb.FoodInfo? bread = ItemDb.FoodOf(ItemId.Bread);
        TestAssert.Check(bread is not null, "bread is food");
        TestAssert.Equal(5.0f, bread?.Hunger, "bread restores five hunger");

        Inventory inv = new();
        inv.Add(ItemId.Wheat, 3);
        Recipe breadRecipe = Recipes.All.First(r => r.Result == ItemId.Bread);
        TestAssert.Check(Recipes.Craft(breadRecipe, inv), "bread crafts from three wheat");
        TestAssert.Equal(1, inv.CountOf(ItemId.Bread), "bread recipe yields one");

        Inventory hoeInv = new();
        hoeInv.Add(ItemId.Planks, 2);
        hoeInv.Add(ItemId.Stick, 2);
        Recipe hoeRecipe = Recipes.All.First(r => r.Result == ItemId.WoodHoe);
        TestAssert.Check(Recipes.Craft(hoeRecipe, hoeInv), "wood hoe crafts from planks and sticks");

        Inventory bedInv = new();
        bedInv.Add(ItemId.Wool, 3);
        bedInv.Add(ItemId.Planks, 3);
        Recipe bedRecipe = Recipes.All.First(r => r.Result == ItemId.Bed);
        TestAssert.Check(Recipes.Craft(bedRecipe, bedInv), "bed crafts from wool and planks");

        Inventory ladderInv = new();
        ladderInv.Add(ItemId.Stick, 7);
        Recipe ladderRecipe = Recipes.All.First(r => r.Result == ItemId.Ladder);
        TestAssert.Check(Recipes.Craft(ladderRecipe, ladderInv), "ladders craft from sticks");
        TestAssert.Equal(3, ladderInv.CountOf(ItemId.Ladder), "ladder recipe yields three");
    }
}
