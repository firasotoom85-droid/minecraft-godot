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

        Inventory cookFull = new();
        for (int i = 0; i < Inventory.SlotCount; i++)
            cookFull.Set(i, ItemDb.New(ItemId.Dirt, 64));
        cookFull.Set(0, ItemDb.New(ItemId.RawFish, 64));
        TestAssert.Check(!Player.TryCook(cookFull, ItemId.RawFish),
                "campfire cooking refuses when the cooked result cannot fit");
        TestAssert.Equal(64, cookFull.CountOf(ItemId.RawFish),
                "failed campfire cooking restores the consumed raw fish");
        TestAssert.Equal(0, cookFull.CountOf(ItemId.CookedFish),
                "failed campfire cooking creates no phantom output");

        Inventory cookFreed = new();
        for (int i = 0; i < Inventory.SlotCount; i++)
            cookFreed.Set(i, ItemDb.New(ItemId.Dirt, 64));
        cookFreed.Set(0, ItemDb.New(ItemId.RawFish, 1));
        TestAssert.Check(Player.TryCook(cookFreed, ItemId.RawFish),
                "campfire cooking succeeds when consuming the raw item frees its slot");
        TestAssert.Equal(1, cookFreed.CountOf(ItemId.CookedFish),
                "successful campfire cooking adds the cooked fish");

        Trader trader = new();
        TestAssert.Equal("Wanderer", trader.DisplayName, "trader name");
        TestAssert.Equal(20.0f, trader.MaxHealth, "trader health");
        trader.Free();
    }
}
