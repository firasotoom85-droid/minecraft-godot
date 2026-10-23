using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class InventoryTests
{
    public static void Run()
    {
        Inventory inv = new();
        TestAssert.Equal(0, inv.Add(ItemId.Stone, 64), "first stack fits");
        TestAssert.Equal(64, inv.Get(0).Count, "hotbar fills first");
        TestAssert.Equal(0, inv.Add(ItemId.Stone, 64), "second stack fits");
        TestAssert.Equal(64, inv.Get(1).Count, "second stack uses next slot");
        TestAssert.Equal(128, inv.CountOf(ItemId.Stone), "counts across slots");

        TestAssert.Check(inv.Consume(ItemId.Stone, 70), "consume across slots");
        TestAssert.Equal(58, inv.CountOf(ItemId.Stone), "consume leaves remainder");
        TestAssert.Check(!inv.Consume(ItemId.Stone, 100), "short consume refuses");
        TestAssert.Equal(58, inv.CountOf(ItemId.Stone), "failed consume changes nothing");

        Inventory tools = new();
        TestAssert.Equal(0, tools.Add(ItemId.WoodPick, 1), "tool enters inventory");
        TestAssert.Equal(1, tools.Get(0).Count, "tools do not stack");
        TestAssert.Equal(60, tools.Get(0).Durability, "new tool starts at full durability");
        TestAssert.Equal(0, tools.Add(ItemId.WoodPick, 1), "second tool takes another slot");
        TestAssert.Equal(1, tools.Get(1).Count, "second tool is a separate stack");

        Inventory full = new();
        int leftover = full.Add(ItemId.Stone, Inventory.SlotCount * 64 + 7);
        TestAssert.Equal(7, leftover, "overflow is reported");
        TestAssert.Equal(Inventory.SlotCount * 64, full.CountOf(ItemId.Stone), "inventory fills completely");

        Inventory saved = new();
        saved.Add(ItemId.Stone, 10);
        saved.Add(ItemId.WoodAxe, 1);
        List<ItemStackSave> data = saved.ToSave();
        Inventory loaded = new();
        loaded.LoadFrom(data);
        TestAssert.Equal(10, loaded.CountOf(ItemId.Stone), "inventory save round-trips blocks");
        TestAssert.Equal(1, loaded.CountOf(ItemId.WoodAxe), "inventory save round-trips tools");

        Inventory starter = new();
        starter.LoadFrom(null);
        TestAssert.Check(starter.CountOf(ItemId.Grass) == 64, "legacy save gets the starter kit");
    }
}
