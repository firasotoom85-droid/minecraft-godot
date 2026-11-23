using System.Collections.Generic;
using System.Text.Json;

namespace Minecraft;

/// <summary>Regression coverage for item persistence and transactional transfer boundaries.</summary>
public static class InventoryPersistenceRegressionTests
{
    public static void Run()
    {
        ValidateInventoryContainerAndFurnaceLoads();
        KeepCraftingResultsAtomic();
        PersistCursorAndCraftingGridItems();
        PreserveContainerTransferRemainders();
    }

    private static void ValidateInventoryContainerAndFurnaceLoads()
    {
        Inventory inventory = new();
        inventory.LoadFrom([
            new ItemStackSave(0, 255, 1),
            new ItemStackSave(1, (int)ItemId.None, 1),
            new ItemStackSave(2, (int)ItemId.Stone, 65),
            new ItemStackSave(3, (int)ItemId.WoodPick, 2, 60),
            new ItemStackSave(4, (int)ItemId.WoodPick, 1, 61),
            new ItemStackSave(5, (int)ItemId.IronPick, 1, 250, Sharpness: 1),
            new ItemStackSave(6, (int)ItemId.IronSword, 1, 321),
            new ItemStackSave(7, (int)ItemId.IronSword, 1, 120, Sharpness: 2, Unbreaking: 3),
            new ItemStackSave(8, (int)ItemId.Stone, 3),
            new ItemStackSave(8, (int)ItemId.Dirt, 4),
            new ItemStackSave(36, (int)ItemId.IronChestplate, 1, 300),
            new ItemStackSave(37, (int)ItemId.IronChestplate, 1, 300),
            new ItemStackSave(40, (int)ItemId.Log, 2),
            new ItemStackSave(49, (int)ItemId.Planks, 7),
        ]);

        for (int slot = 0; slot <= 6; slot++)
        {
            TestAssert.Check(inventory.Get(slot).IsEmpty,
                    $"invalid inventory record in slot {slot} is rejected");
        }

        TestAssert.Equal(ItemId.IronSword, inventory.Get(7).Id,
                "valid enchanted equipment remains loadable");
        TestAssert.Equal(2, inventory.Get(7).Sharpness,
                "valid equipment enchantment metadata is preserved");
        TestAssert.Equal(3, inventory.Get(7).Unbreaking,
                "valid equipment unbreaking metadata is preserved");
        TestAssert.Equal(ItemId.Stone, inventory.Get(8).Id,
                "first valid duplicate-slot record retains slot ownership");
        TestAssert.Equal(3, inventory.Get(8).Count,
                "later duplicate record does not overwrite the first stack");
        TestAssert.Check(inventory.Get(36).IsEmpty,
                "armor item in the wrong equipment slot is rejected");
        TestAssert.Equal(ItemId.IronChestplate, inventory.Get(37).Id,
                "armor item in its matching equipment slot is accepted");
        TestAssert.Equal(ItemId.Log, inventory.CraftingGrid[0].Id,
                "saved crafting-grid item is restored");
        TestAssert.Equal(2, inventory.CraftingGrid[0].Count,
                "crafting-grid count is restored");
        TestAssert.Equal(ItemId.Planks, inventory.CursorStack.Id,
                "saved cursor-held item is restored");
        TestAssert.Equal(7, inventory.CursorStack.Count,
                "cursor-held count is restored");

        Container container = new();
        container.LoadFrom([
            new ItemStackSave(0, (int)ItemId.Stone, 65),
            new ItemStackSave(0, (int)ItemId.Dirt, 2),
            new ItemStackSave(1, (int)ItemId.Stone, 3),
            new ItemStackSave(1, (int)ItemId.Dirt, 4),
            new ItemStackSave(2, (int)ItemId.IronSword, 1, 120, Sharpness: 1),
        ]);
        TestAssert.Equal(ItemId.Dirt, container.Slots[0].Id,
                "valid container record can recover a slot after an invalid record");
        TestAssert.Equal(2, container.Slots[0].Count,
                "container loader rejects overstacked records");
        TestAssert.Equal(ItemId.Stone, container.Slots[1].Id,
                "container loader keeps the first valid duplicate-slot record");
        TestAssert.Equal(3, container.Slots[1].Count,
                "container duplicate cannot overwrite an existing stack");
        TestAssert.Equal(1, container.Slots[2].Sharpness,
                "container loader preserves valid equipment metadata");

        FurnaceData furnace = new();
        furnace.LoadFrom([
            new ItemStackSave(0, (int)ItemId.Coal, 1),
            new ItemStackSave(0, (int)ItemId.RawIron, 2),
            new ItemStackSave(1, (int)ItemId.IronSword, 1, 120),
            new ItemStackSave(1, (int)ItemId.Coal, 65),
            new ItemStackSave(1, (int)ItemId.Coal, 1),
            new ItemStackSave(2, (int)ItemId.IronSword, 1, 120),
            new ItemStackSave(2, (int)ItemId.IronIngot, 65),
            new ItemStackSave(2, (int)ItemId.IronIngot, 1),
            new ItemStackSave(2, (int)ItemId.GoldIngot, 1),
            new ItemStackSave(3, (int)ItemId.Stone, 1),
        ]);
        TestAssert.Equal(ItemId.RawIron, furnace.Input.Id,
                "furnace accepts a valid input stack");
        TestAssert.Equal(2, furnace.Input.Count,
                "furnace input count remains valid");
        TestAssert.Equal(ItemId.Coal, furnace.Fuel.Id,
                "furnace can recover a later valid fuel record after an invalid one");
        TestAssert.Equal(1, furnace.Fuel.Count,
                "furnace rejects fuel overstacks");
        TestAssert.Equal(ItemId.IronIngot, furnace.Output.Id,
                "furnace loader rejects items that do not belong in input, fuel, or output slots");
        TestAssert.Equal(1, furnace.Output.Count,
                "furnace loader rejects overstacked output records");
        TestAssert.Check(!ItemStackSaveValidator.TryCreate(
                        new ItemStackSave(0, 500, 1), out _, out _),
                "item IDs outside the byte range are rejected");

        Inventory opaqueInventory = new();
        opaqueInventory.LoadFrom([new ItemStackSave(0, 110, 5)]);
        TestAssert.Check(opaqueInventory.Get(0).IsEmpty,
                "unrecognized future item IDs never become runtime item stacks");
        TestAssert.Check(opaqueInventory.HasUnrecognizedSaveSlot(0),
                "unrecognized future item reserves its original inventory slot");
        TestAssert.Equal(0, opaqueInventory.Add(ItemId.Stone, 3),
                "automatic inventory insertion skips a preserved future item slot");
        List<ItemStackSave> opaqueInventorySave = opaqueInventory.ToSave();
        TestAssert.Check(opaqueInventorySave.Exists(s => s.Slot == 0 && s.Id == 110 && s.Count == 5),
                "unrecognized future item is preserved unchanged in player save data");
        TestAssert.Equal(ItemId.Stone, opaqueInventory.Get(1).Id,
                "known items are inserted into the next non-reserved inventory slot");
        TestAssert.Equal(2, opaqueInventory.FirstEmptySlot(),
                "first-empty lookup skips preserved future item slots");

        Inventory opaqueToolInventory = new();
        opaqueToolInventory.LoadFrom([new ItemStackSave(0, 110, 1)]);
        TestAssert.Equal(0, opaqueToolInventory.Add(ItemDb.New(ItemId.WoodPick, 1)),
                "non-stackable insertion skips preserved future item slots");
        TestAssert.Equal(ItemId.WoodPick, opaqueToolInventory.Get(1).Id,
                "non-stackable item is stored without overwriting opaque save data");

        PlayerSave opaquePlayerSave = new([0.0f, 0.0f, 0.0f], Items: opaqueInventory.ToSave());
        string opaquePlayerJson = JsonSerializer.Serialize(opaquePlayerSave, WorldStore.Json);
        PlayerSave? opaquePlayerReload = JsonSerializer.Deserialize<PlayerSave>(
                opaquePlayerJson, WorldStore.Json);
        Inventory opaqueInventoryReload = new();
        opaqueInventoryReload.LoadFrom(opaquePlayerReload?.Items);
        TestAssert.Check(opaqueInventoryReload.HasUnrecognizedSaveSlot(0),
                "future item reservation survives serialized player save/load");
        TestAssert.Check(opaqueInventoryReload.ToSave().Exists(
                        s => s.Slot == 0 && s.Id == 110 && s.Count == 5),
                "future item fields survive serialized player save/load unchanged");

        Inventory opaqueTransientSlots = new();
        opaqueTransientSlots.LoadFrom([
            new ItemStackSave(Inventory.CraftingGridSaveSlot + 2, 110, 2),
            new ItemStackSave(Inventory.CursorSaveSlot, 110, 1),
        ]);
        TestAssert.Check(opaqueTransientSlots.HasUnrecognizedCraftingGridItem(),
                "unrecognized crafting-grid items block crafting instead of being discarded");
        TestAssert.Check(opaqueTransientSlots.HasUnrecognizedSaveSlot(Inventory.CursorSaveSlot),
                "unrecognized cursor stack remains reserved");
        List<ItemStackSave> opaqueTransientSave = opaqueTransientSlots.ToSave();
        TestAssert.Check(opaqueTransientSave.Exists(
                        s => s.Slot == Inventory.CraftingGridSaveSlot + 2 && s.Id == 110 && s.Count == 2)
                && opaqueTransientSave.Exists(
                        s => s.Slot == Inventory.CursorSaveSlot && s.Id == 110 && s.Count == 1),
                "unrecognized crafting and cursor records are preserved in player saves");

        Container opaqueContainer = new();
        opaqueContainer.LoadFrom([new ItemStackSave(4, 110, 3)]);
        TestAssert.Check(opaqueContainer.Slots[4].IsEmpty,
                "unrecognized container item never becomes a runtime stack");
        TestAssert.Check(opaqueContainer.HasUnrecognizedSaveSlot(4),
                "unrecognized container item reserves its source slot");
        TestAssert.Check(!opaqueContainer.IsEmpty,
                "container with a preserved unrecognized item is not considered empty");
        TestAssert.Equal(0, opaqueContainer.Add(ItemId.Stone, 3),
                "container insertion skips a preserved future item slot");
        TestAssert.Check(opaqueContainer.ToSave().Exists(s => s.Slot == 4 && s.Id == 110 && s.Count == 3),
                "unrecognized container item is preserved unchanged");

        FurnaceData opaqueFurnace = new();
        opaqueFurnace.LoadFrom([
            new ItemStackSave(0, (int)ItemId.RawIron, 1),
            new ItemStackSave(1, (int)ItemId.Coal, 1),
            new ItemStackSave(2, 110, 2),
        ]);
        TestAssert.Check(opaqueFurnace.Output.IsEmpty,
                "unrecognized furnace output never becomes a runtime item stack");
        TestAssert.Check(opaqueFurnace.HasUnrecognizedSaveSlot(2),
                "unrecognized furnace output reserves its slot");
        TestAssert.Check(!opaqueFurnace.IsEmpty,
                "furnace with preserved unrecognized contents is not considered empty");
        TestAssert.Check(!opaqueFurnace.Tick(30.0f),
                "furnace does not consume fuel or inputs while output slot is reserved");
        TestAssert.Equal(1, opaqueFurnace.Input.Count,
                "reserved furnace output retains the input stack");
        TestAssert.Equal(1, opaqueFurnace.Fuel.Count,
                "reserved furnace output retains the fuel stack");
        TestAssert.Check(opaqueFurnace.ToSave().Exists(s => s.Slot == 2 && s.Id == 110 && s.Count == 2),
                "unrecognized furnace output is preserved unchanged");
    }

    private static void KeepCraftingResultsAtomic()
    {
        Inventory partialCapacity = FilledInventory();
        partialCapacity.Set(0, ItemDb.New(ItemId.Planks, 63));
        ItemStack[] partialGrid = new ItemStack[4];
        partialGrid[0] = ItemDb.New(ItemId.Log, 1);
        ItemStack incompatibleCursor = ItemDb.New(ItemId.Stone, 1);
        (Recipe Recipe, int Row, int Col)? partialMatch = Recipes.Match(partialGrid, 2);
        TestAssert.Check(partialMatch is not null, "Log matches the four-planks crafting result");

        if (partialMatch is { } partial)
        {
            bool collected = InventoryStackTransfer.TryTakeCraftingResult(
                    partialGrid, 2, partial, incompatibleCursor, partialCapacity,
                    out ItemStack cursorAfter);
            TestAssert.Check(!collected,
                    "craft result refuses partial inventory capacity when cursor is incompatible");
            TestAssert.Equal(63, partialCapacity.CountOf(ItemId.Planks),
                    "failed craft does not insert the partial output");
            TestAssert.Equal(ItemId.Log, partialGrid[0].Id,
                    "failed craft leaves the source ingredient in the grid");
            TestAssert.Equal(1, partialGrid[0].Count,
                    "failed craft leaves the full ingredient count unchanged");
            TestAssert.Equal(ItemId.Stone, cursorAfter.Id,
                    "failed craft preserves the incompatible cursor item");
            TestAssert.Equal(1, cursorAfter.Count,
                    "failed craft preserves the complete cursor quantity");

            ItemStack fullMatchingCursor = ItemDb.New(ItemId.Planks, 63);
            collected = InventoryStackTransfer.TryTakeCraftingResult(
                    partialGrid, 2, partial, fullMatchingCursor, partialCapacity,
                    out cursorAfter);
            TestAssert.Check(!collected,
                    "matching cursor overflow falls back atomically when inventory lacks room");
            TestAssert.Equal(63, cursorAfter.Count,
                    "failed matching-cursor collection does not partially increase the cursor");
            TestAssert.Equal(63, partialCapacity.CountOf(ItemId.Planks),
                    "failed matching-cursor collection does not partially insert into inventory");
            TestAssert.Equal(ItemId.Log, partialGrid[0].Id,
                    "failed matching-cursor collection retains the ingredients");
        }

        Inventory cursorInventory = FilledInventory();
        ItemStack[] emptyCursorGrid = new ItemStack[4];
        emptyCursorGrid[0] = ItemDb.New(ItemId.Log, 1);
        (Recipe Recipe, int Row, int Col)? emptyCursorMatch = Recipes.Match(emptyCursorGrid, 2);
        if (emptyCursorMatch is { } emptyMatch)
        {
            bool collected = InventoryStackTransfer.TryTakeCraftingResult(
                    emptyCursorGrid, 2, emptyMatch, ItemStack.Empty, cursorInventory,
                    out ItemStack cursorAfter);
            TestAssert.Check(collected, "empty cursor accepts the complete crafting result");
            TestAssert.Equal(ItemId.Planks, cursorAfter.Id,
                    "empty cursor receives the recipe output");
            TestAssert.Equal(4, cursorAfter.Count,
                    "empty cursor receives all four planks");
            TestAssert.Check(emptyCursorGrid[0].IsEmpty,
                    "successful collection consumes the matched log ingredient");
            TestAssert.Check(Recipes.Match(emptyCursorGrid, 2) is null,
                    "consumed ingredients cannot produce the same result a second time");
            TestAssert.Equal(0, cursorInventory.CountOf(ItemId.Planks),
                    "cursor delivery does not also insert result items into inventory");
        }

        ItemStack[] matchingCursorGrid = new ItemStack[4];
        matchingCursorGrid[0] = ItemDb.New(ItemId.Log, 1);
        (Recipe Recipe, int Row, int Col)? matchingCursorMatch = Recipes.Match(matchingCursorGrid, 2);
        if (matchingCursorMatch is { } match)
        {
            bool collected = InventoryStackTransfer.TryTakeCraftingResult(
                    matchingCursorGrid, 2, match, ItemDb.New(ItemId.Planks, 60),
                    cursorInventory, out ItemStack cursorAfter);
            TestAssert.Check(collected, "matching cursor accepts an output that fits");
            TestAssert.Equal(64, cursorAfter.Count,
                    "matching cursor receives the complete result without overstacking");
            TestAssert.Check(matchingCursorGrid[0].IsEmpty,
                    "matching-cursor collection consumes ingredients exactly once");
        }

        Inventory oneFreeSlot = FilledInventory();
        oneFreeSlot.Set(Inventory.SlotCount - 1, ItemStack.Empty);
        ItemStack[] fallbackGrid = new ItemStack[4];
        fallbackGrid[0] = ItemDb.New(ItemId.Log, 1);
        (Recipe Recipe, int Row, int Col)? fallbackMatch = Recipes.Match(fallbackGrid, 2);
        if (fallbackMatch is { } matched)
        {
            bool collected = InventoryStackTransfer.TryTakeCraftingResult(
                    fallbackGrid, 2, matched, incompatibleCursor, oneFreeSlot,
                    out ItemStack cursorAfter);
            TestAssert.Check(collected,
                    "incompatible cursor falls back to inventory when the entire result fits");
            TestAssert.Equal(ItemId.Stone, cursorAfter.Id,
                    "inventory fallback preserves the incompatible cursor stack");
            TestAssert.Equal(4, oneFreeSlot.CountOf(ItemId.Planks),
                    "inventory fallback receives the entire crafting result");
            TestAssert.Check(fallbackGrid[0].IsEmpty,
                    "successful inventory fallback consumes the matched ingredient");
        }
    }

    private static void PersistCursorAndCraftingGridItems()
    {
        Inventory inventory = new();
        inventory.CraftingGrid[0] = ItemDb.New(ItemId.Log, 2);
        inventory.CraftingGrid[4] = ItemDb.New(ItemId.Stone, 5);
        inventory.CursorStack = ItemDb.New(ItemId.Planks, 7);

        PlayerSave savedPlayer = new([0.0f, 0.0f, 0.0f], Items: inventory.ToSave());
        string json = JsonSerializer.Serialize(savedPlayer, WorldStore.Json);
        PlayerSave? loadedPlayer = JsonSerializer.Deserialize<PlayerSave>(json, WorldStore.Json);
        TestAssert.Check(loadedPlayer?.Items is not null,
                "player save JSON contains the inventory's held and crafting stacks");

        Inventory loaded = new();
        loaded.LoadFrom(loadedPlayer?.Items);
        TestAssert.Equal(ItemId.Log, loaded.CraftingGrid[0].Id,
                "crafting-grid item survives player save/load");
        TestAssert.Equal(2, loaded.CraftingGrid[0].Count,
                "crafting-grid stack count survives player save/load");
        TestAssert.Equal(ItemId.Stone, loaded.CraftingGrid[4].Id,
                "second crafting-grid slot survives player save/load");
        TestAssert.Equal(ItemId.Planks, loaded.CursorStack.Id,
                "cursor-held item survives player save/load");
        TestAssert.Equal(7, loaded.CursorStack.Count,
                "cursor-held stack count survives player save/load");

        Inventory full = FilledInventory();
        full.Set(0, ItemDb.New(ItemId.Stone, 63));
        full.CraftingGrid[0] = ItemDb.New(ItemId.Stone, 4);
        full.CursorStack = ItemDb.New(ItemId.Planks, 2);

        TestAssert.Equal(3, full.ReturnCraftingGridItems(),
                "craft settlement retains the unaccepted crafting remainder");
        TestAssert.Equal(2, full.ReturnCursorItem(),
                "cursor settlement retains items when the inventory is full");
        TestAssert.Equal(64, full.CountOf(ItemId.Stone),
                "settlement transfers only the available matching-stack capacity");
        TestAssert.Equal(3, full.CraftingGrid[0].Count,
                "unaccepted crafting items remain owned by their original slot");
        TestAssert.Equal(2, full.CursorStack.Count,
                "unaccepted cursor items remain owned by the cursor slot");

        full.ReturnCraftingGridItems();
        full.ReturnCursorItem();
        TestAssert.Equal(64, full.CountOf(ItemId.Stone),
                "repeated settlement does not duplicate accepted items");
        TestAssert.Equal(3, full.CraftingGrid[0].Count,
                "repeated settlement preserves the same crafting remainder");
        TestAssert.Equal(2, full.CursorStack.Count,
                "repeated settlement preserves the same cursor remainder");

        Inventory settledReload = new();
        settledReload.LoadFrom(full.ToSave());
        TestAssert.Equal(3, settledReload.CraftingGrid[0].Count,
                "full-inventory crafting remainder is saved");
        TestAssert.Equal(2, settledReload.CursorStack.Count,
                "full-inventory cursor remainder is saved");
    }

    private static void PreserveContainerTransferRemainders()
    {
        Container chest = new();
        chest.Slots[0] = ItemDb.New(ItemId.Stone, 8);
        Inventory inventory = FilledInventory();
        inventory.Set(0, ItemDb.New(ItemId.Stone, 62));

        ItemStack source = chest.Slots[0];
        bool moved = InventoryStackTransfer.TryTransferToInventory(ref source, inventory);
        chest.Slots[0] = source;
        TestAssert.Check(moved, "container transfer moves the quantity that fits");
        TestAssert.Equal(64, inventory.CountOf(ItemId.Stone),
                "container transfer fills only the available stack capacity");
        TestAssert.Equal(6, chest.Slots[0].Count,
                "container retains the unaccepted transfer remainder");

        source = chest.Slots[0];
        moved = InventoryStackTransfer.TryTransferToInventory(ref source, inventory);
        TestAssert.Check(!moved, "full inventory refuses a container transfer");
        TestAssert.Equal(6, source.Count,
                "failed container transfer leaves the complete source stack unchanged");
    }

    private static Inventory FilledInventory()
    {
        Inventory inventory = new();
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            inventory.Set(slot, ItemDb.New(ItemId.Dirt, 64));
        }

        return inventory;
    }
}
