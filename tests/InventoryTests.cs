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
        TestAssert.Check(!inv.Consume(ItemId.Stone, -1), "negative consume count is rejected");
        TestAssert.Check(inv.Consume(ItemId.Stone, 0), "zero consume is a successful no-op");
        TestAssert.Equal(58, inv.CountOf(ItemId.Stone), "invalid and no-op consumes preserve inventory");

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

        Inventory roomy = new();
        TestAssert.Check(roomy.CanFit(ItemId.Stone, Inventory.SlotCount * 64), "empty inventory fits full stacks");
        TestAssert.Check(!roomy.CanFit(ItemId.Stone, Inventory.SlotCount * 64 + 1), "no room past the slot count");
        ItemStack dragSource = ItemDb.New(ItemId.Stone, 10);
        ItemStack dragTarget = ItemStack.Empty;
        TestAssert.Check(InventoryStackTransfer.TryTransfer(ref dragSource, ref dragTarget),
                "drag moves a stack into an empty slot");
        TestAssert.Check(dragSource.IsEmpty, "drag empties the source slot");
        TestAssert.Equal(10, dragTarget.Count, "drag preserves the full stack count");

        dragSource = ItemDb.New(ItemId.Stone, 10);
        dragTarget = ItemDb.New(ItemId.Stone, 60);
        TestAssert.Check(InventoryStackTransfer.TryTransfer(ref dragSource, ref dragTarget),
                "drag merges matching stacks");
        TestAssert.Equal(64, dragTarget.Count, "drag merge respects max stack size");
        TestAssert.Equal(6, dragSource.Count, "drag merge leaves the correct remainder");

        dragSource = ItemDb.New(ItemId.Dirt, 3);
        dragTarget = ItemDb.New(ItemId.Stone, 5);
        TestAssert.Check(InventoryStackTransfer.TryTransfer(ref dragSource, ref dragTarget),
                "drag swaps different item stacks");
        TestAssert.Equal(ItemId.Stone, dragSource.Id, "swap returns the previous target to source");
        TestAssert.Equal(ItemId.Dirt, dragTarget.Id, "swap moves source item to target");

        dragSource = ItemDb.New(ItemId.IronHelmet, 1);
        dragTarget = ItemStack.Empty;
        TestAssert.Check(!InventoryStackTransfer.TryTransfer(ref dragSource, ref dragTarget, -1, 1),
                "armor cannot be dragged into the wrong equipment slot");
        TestAssert.Equal(ItemId.IronHelmet, dragSource.Id, "invalid armor drop preserves source");
        TestAssert.Check(dragTarget.IsEmpty, "invalid armor drop leaves target empty");
        TestAssert.Check(InventoryStackTransfer.TryTransfer(ref dragSource, ref dragTarget, -1, 0),
                "armor can be dragged into its matching equipment slot");
        TestAssert.Check(dragSource.IsEmpty, "valid armor drop clears source");
        TestAssert.Equal(ItemId.IronHelmet, dragTarget.Id, "valid armor drop equips the correct item");


        Inventory partial = new();
        partial.Set(0, ItemDb.New(ItemId.Stone, 60));
        TestAssert.Check(partial.CanFit(ItemId.Stone, 68), "partial stack plus empty slots fits");

        Inventory oneGap = new();
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            oneGap.Set(i, ItemDb.New(ItemId.Dirt, 64));
        }

        oneGap.Set(Inventory.SlotCount - 1, ItemStack.Empty);
        TestAssert.Check(oneGap.CanFit(ItemId.WoodPick, 1), "one empty slot fits one tool");
        TestAssert.Check(!oneGap.CanFit(ItemId.WoodPick, 2), "tools need one empty slot each");

        Inventory oneGapStack = new();
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            oneGapStack.Set(i, ItemDb.New(ItemId.Dirt, 64));
        }

        oneGapStack.Set(Inventory.SlotCount - 1, ItemStack.Empty);
        int toolRemainder = oneGapStack.Add(new ItemStack(ItemId.WoodPick, 2, 60));
        TestAssert.Equal(1, toolRemainder,
                "a multi-count non-stackable item reports the slot it could not fit");
        TestAssert.Equal(1, oneGapStack.CountOf(ItemId.WoodPick),
                "a non-stackable item never stores more than one per slot");
        TestAssert.Equal(1, oneGapStack.Get(Inventory.SlotCount - 1).Count,
                "the stored tool occupies exactly one slot with count one");

        Inventory selectedCook = new();
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            selectedCook.Set(slot, ItemDb.New(ItemId.Dirt, 64));
        selectedCook.Set(0, ItemDb.New(ItemId.RawPork, 2));
        selectedCook.Set(1, ItemDb.New(ItemId.RawPork, 1));
        TestAssert.Check(Player.TryCook(selectedCook, 1, ItemId.RawPork),
                "campfire cooking can use the selected raw-food slot as its output space");
        TestAssert.Equal(2, selectedCook.Get(0).Count,
                "cooking leaves an earlier matching raw-food stack untouched");
        TestAssert.Equal(ItemId.CookedPork, selectedCook.Get(1).Id,
                "cooked output occupies the selected stack's freed slot");

        InventoryPersistenceRegressionTests.Run();
    }
}
