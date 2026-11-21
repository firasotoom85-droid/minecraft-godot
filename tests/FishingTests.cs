using System.Linq;
using Godot;

namespace Minecraft;

public static class FishingTests
{
    public static void Run()
    {
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.FishingRod), "fishing rods do not stack");
        TestAssert.Equal(2.0f, ItemDb.FoodOf(ItemId.RawFish)?.Hunger, "raw fish restores hunger");
        TestAssert.Equal(5.0f, ItemDb.FoodOf(ItemId.CookedFish)?.Hunger, "cooked fish restores more hunger");
        TestAssert.Equal(ItemId.CookedFish, ItemDb.CooksTo(ItemId.RawFish), "campfire cooks fish");
        TestAssert.Equal(ItemId.CookedFish, ItemDb.SmeltsTo(ItemId.RawFish), "furnace cooks fish");

        Inventory inv = new();
        inv.Add(ItemId.Stick, 3);
        inv.Add(ItemId.Wool, 2);
        Recipe rod = Recipes.All.First(r => r.Result == ItemId.FishingRod);
        TestAssert.Check(Recipes.Craft(rod, inv), "fishing rod crafts from sticks and wool");
        TestAssert.Equal(1, inv.CountOf(ItemId.FishingRod), "rod recipe yields one");

        Trader trader = new();
        TestAssert.Equal("Wanderer", trader.DisplayName, "trader name");
        TestAssert.Equal(20.0f, trader.MaxHealth, "trader health");
        trader.Free();
    }
}
