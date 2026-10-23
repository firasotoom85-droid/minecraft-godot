using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class ContainerTests
{
    public static void Run()
    {
        Container chest = new();
        TestAssert.Check(chest.IsEmpty, "new chest is empty");
        TestAssert.Equal(0, chest.Add(ItemId.Stone, 70), "chest accepts items");
        TestAssert.Equal(64, chest.Slots[0].Count, "chest stacks to max");
        TestAssert.Equal(6, chest.Slots[1].Count, "chest overflows into the next slot");
        TestAssert.Check(!chest.IsEmpty, "chest reports contents");

        TestAssert.Equal(0, chest.Add(ItemId.WoodPick, 1), "chest accepts a tool");
        TestAssert.Equal(1, chest.Slots[2].Count, "tool does not stack");
        TestAssert.Equal(60, chest.Slots[2].Durability, "tool keeps full durability");

        List<ItemStackSave> data = chest.ToSave();
        Container loaded = new();
        loaded.LoadFrom(data);
        TestAssert.Equal(70, loaded.Slots[0].Count + loaded.Slots[1].Count, "chest save round-trips stacks");
        TestAssert.Equal(1, loaded.Slots[2].Count, "chest save round-trips tools");
        TestAssert.Equal(60, loaded.Slots[2].Durability, "chest save round-trips durability");

        Container full = new();
        int leftover = full.Add(ItemId.Stone, Container.Size * 64 + 5);
        TestAssert.Equal(5, leftover, "chest reports overflow");
        TestAssert.Equal(Container.Size * 64, full.Slots[0].Count + full.Slots[1].Count
                + full.Slots[2].Count + full.Slots[3].Count + full.Slots[4].Count
                + full.Slots[5].Count + full.Slots[6].Count + full.Slots[7].Count
                + full.Slots[8].Count + full.Slots[9].Count + full.Slots[10].Count
                + full.Slots[11].Count + full.Slots[12].Count + full.Slots[13].Count
                + full.Slots[14].Count + full.Slots[15].Count + full.Slots[16].Count
                + full.Slots[17].Count + full.Slots[18].Count + full.Slots[19].Count
                + full.Slots[20].Count + full.Slots[21].Count + full.Slots[22].Count
                + full.Slots[23].Count + full.Slots[24].Count + full.Slots[25].Count
                + full.Slots[26].Count, "full chest holds 27 stacks");
    }
}
