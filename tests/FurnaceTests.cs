using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class FurnaceTests
{
    public static void Run()
    {
        FurnaceData furnace = new()
        {
            Input = new ItemStack(ItemId.RawIron, 2, 0),
            Fuel = new ItemStack(ItemId.Coal, 1, 0),
        };
        TestAssert.Equal(1, furnace.Fuel.Count, "fuel starts in the slot");
        TestAssert.Check(furnace.Tick(0.1f), "furnace starts burning");
        TestAssert.Equal(0, furnace.Fuel.Count, "fuel is consumed to start the burn");
        TestAssert.Check(furnace.IsBurning, "furnace reports burning");
        TestAssert.Check(furnace.Tick(10.0f), "furnace smelts");
        TestAssert.Equal(1, furnace.Output.Count, "one ore becomes one ingot");
        TestAssert.Equal(ItemId.IronIngot, furnace.Output.Id, "raw iron smelts to iron");
        TestAssert.Equal(1, furnace.Input.Count, "one input is consumed");

        FurnaceData saved = new()
        {
            Input = new ItemStack(ItemId.Sand, 3, 0),
            Fuel = new ItemStack(ItemId.Planks, 2, 0),
            BurnTime = 12.0f,
            BurnMax = 15.0f,
            SmeltTime = 4.0f,
        };
        FurnaceSave save = new(saved.ToSave(), saved.BurnTime, saved.BurnMax, saved.SmeltTime);
        FurnaceData loaded = new()
        {
            BurnTime = save.BurnTime,
            BurnMax = save.BurnMax,
            SmeltTime = save.SmeltTime,
        };
        loaded.LoadFrom(save.Slots);
        TestAssert.Equal(ItemId.Sand, loaded.Input.Id, "furnace save round-trips input");
        TestAssert.Equal(2, loaded.Fuel.Count, "furnace save round-trips fuel");
        TestAssert.Equal(12.0f, loaded.BurnTime, "furnace save round-trips burn time");
        loaded.Tick(0.1f);
        TestAssert.Equal(4.1f, loaded.SmeltTime, "saved smelt progress continues after load");

        FurnaceData empty = new()
        {
            Input = new ItemStack(ItemId.RawGold, 1, 0),
        };
        empty.Tick(30.0f);
        TestAssert.Check(empty.Output.IsEmpty, "no fuel means no smelting");

        // Regression: an idle furnace used to compare null against ItemId.None
        // and report a change on every tick, forcing an autosave forever.
        FurnaceData idle = new();
        TestAssert.Check(!idle.Tick(0.1f), "empty furnace does not report a change");
        TestAssert.Check(!idle.Tick(0.1f), "empty furnace stays quiet on later ticks");

        FurnaceData stoneOnly = new()
        {
            Input = new ItemStack(ItemId.Stone, 1, 0),
        };
        TestAssert.Check(!stoneOnly.Tick(0.1f), "non-smeltable input does not report a change");

        TestAssert.Equal(80.0f, ItemDb.FuelSeconds(ItemId.Coal), "coal is a long fuel");
        TestAssert.Equal(15.0f, ItemDb.FuelSeconds(ItemId.Planks), "planks are a short fuel");
        TestAssert.Equal(0.0f, ItemDb.FuelSeconds(ItemId.Stone), "stone is not fuel");
        TestAssert.Equal(ItemId.Glass, ItemDb.SmeltsTo(ItemId.Sand), "sand smelts to glass");

        // Regression: a large frame/tick that crosses a fuel boundary must
        // carry the five seconds after the first coal into the next coal item.
        FurnaceData fuelBoundary = new()
        {
            Input = new ItemStack(ItemId.RawIron, 9, 0),
            Fuel = new ItemStack(ItemId.Coal, 2, 0),
        };
        TestAssert.Check(fuelBoundary.Tick(85.0f),
                "furnace advances across the first coal fuel boundary");
        TestAssert.Equal(8, fuelBoundary.Output.Count,
                "first eighty seconds of coal smelt eight items");
        TestAssert.Equal(1, fuelBoundary.Input.Count,
                "five seconds after the coal boundary remain partial smelt progress");
        TestAssert.Equal(5.0f, fuelBoundary.SmeltTime,
                "elapsed time after a fuel boundary is carried into the next burn");
        fuelBoundary.Tick(5.0f);
        TestAssert.Equal(9, fuelBoundary.Output.Count,
                "carried partial progress completes the ninth smelt");
        TestAssert.Check(fuelBoundary.Input.IsEmpty,
                "the input is consumed after the carried-progress smelt");

        FurnaceData acceptedSlots = new();
        TestAssert.Check(acceptedSlots.TrySetSlot(0, ItemDb.New(ItemId.RawIron, 2)),
                "furnace runtime accepts a valid smeltable input");
        TestAssert.Check(acceptedSlots.TrySetSlot(1, ItemDb.New(ItemId.Coal, 1)),
                "furnace runtime accepts a valid fuel item");
        TestAssert.Check(!acceptedSlots.TrySetSlot(0, ItemDb.New(ItemId.Planks, 1)),
                "furnace input rejects items that have no smelting recipe");
        TestAssert.Check(!acceptedSlots.TrySetSlot(1, ItemDb.New(ItemId.Stone, 1)),
                "furnace fuel rejects items that cannot burn");
        TestAssert.Equal(ItemId.RawIron, acceptedSlots.Input.Id,
                "invalid input insertion leaves the existing furnace stack unchanged");
        TestAssert.Equal(ItemId.Coal, acceptedSlots.Fuel.Id,
                "invalid fuel insertion leaves the existing furnace stack unchanged");
        TestAssert.Check(!ItemStackSaveValidator.FitsFurnaceSlot(
                        0, ItemDb.New(ItemId.Planks, 1)),
                "persisted furnace validation applies the same input acceptance rule");

        FurnaceData malformedRuntime = new()
        {
            Input = ItemDb.New(ItemId.Planks, 1),
            Fuel = ItemDb.New(ItemId.Coal, 1),
        };
        TestAssert.Check(!malformedRuntime.Tick(10.0f),
                "furnace tick refuses to progress malformed runtime stacks");
        TestAssert.Equal(1, malformedRuntime.Fuel.Count,
                "malformed furnace state does not consume fuel");
        TestAssert.Check(malformedRuntime.Output.IsEmpty,
                "malformed furnace state does not produce output");

        FurnaceData invalidDelta = new()
        {
            Input = ItemDb.New(ItemId.RawIron, 1),
            Fuel = ItemDb.New(ItemId.Coal, 1),
        };
        TestAssert.Check(!invalidDelta.Tick(float.NaN),
                "furnace rejects NaN elapsed time");
        TestAssert.Equal(1, invalidDelta.Fuel.Count,
                "invalid elapsed time cannot consume furnace fuel");
    }
}

public static class ArmorTests
{
    public static void Run()
    {
        ArmorInfo? chest = ItemDb.ArmorOf(ItemId.IronChestplate);
        TestAssert.Check(chest is not null, "iron chestplate is armor");
        if (chest is { } info)
        {
            TestAssert.Equal(1, info.Slot, "chestplate uses the chest slot");
            TestAssert.Equal(6, info.Points, "chestplate defense points");
            TestAssert.Equal(300, info.Durability, "chestplate durability");
        }

        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.DiamondHelmet), "armor does not stack");
        TestAssert.Check(ItemDb.ArmorOf(ItemId.IronIngot) is null, "ingots are not armor");

        Inventory inv = new();
        inv.Armor[0] = ItemDb.New(ItemId.IronHelmet, 1);
        inv.Armor[1] = ItemDb.New(ItemId.DiamondChestplate, 1);
        List<ItemStackSave> data = inv.ToSave();
        Inventory loaded = new();
        loaded.LoadFrom(data);
        TestAssert.Equal(ItemId.IronHelmet, loaded.Armor[0].Id, "helmet save round-trips");
        TestAssert.Equal(ItemId.DiamondChestplate, loaded.Armor[1].Id, "chestplate save round-trips");
        TestAssert.Equal(600, loaded.Armor[1].Durability, "armor durability save round-trips");
    }
}
