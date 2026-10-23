using System;
using System.Collections.Generic;

namespace Minecraft;

/// <summary>Player inventory: 9 hotbar + 27 backpack slots of item stacks.</summary>
public sealed class Inventory
{
    public const int HotbarSize = 9;
    public const int BackpackSize = 27;
    public const int SlotCount = HotbarSize + BackpackSize;
    public const int ArmorSlots = 4;
    public const int SaveSlotCount = SlotCount + ArmorSlots;

    public readonly ItemStack[] Hotbar = new ItemStack[HotbarSize];
    public readonly ItemStack[] Backpack = new ItemStack[BackpackSize];
    public readonly ItemStack[] Armor = new ItemStack[ArmorSlots];  // helmet, chest, legs, boots

    public event Action? Changed;

    public ItemStack Get(int slot) => slot < HotbarSize
            ? Hotbar[slot]
            : slot < SlotCount
                    ? Backpack[slot - HotbarSize]
                    : Armor[slot - SlotCount];

    public void Set(int slot, ItemStack stack)
    {
        if (slot < HotbarSize)
        {
            Hotbar[slot] = stack;
        }
        else if (slot < SlotCount)
        {
            Backpack[slot - HotbarSize] = stack;
        }
        else
        {
            Armor[slot - SlotCount] = stack;
        }
    }

    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Add a stack, preserving durability (tools). Returns leftover count.</summary>
    public int Add(ItemStack stack)
    {
        if (stack.IsEmpty)
        {
            return 0;
        }

        if (ItemDb.MaxStack(stack.Id) == 1)
        {
            int slot = FirstEmptySlot();
            if (slot < 0)
            {
                return stack.Count;
            }

            Set(slot, stack);
            Changed?.Invoke();
            return 0;
        }

        return Add(stack.Id, stack.Count);
    }

    /// <summary>Add items, filling hotbar first; returns the leftover count.</summary>
    public int Add(ItemId id, int count)
    {
        if (id == ItemId.None || count <= 0)
        {
            return 0;
        }

        int max = ItemDb.MaxStack(id);
        int remaining = count;
        remaining = Merge(id, remaining, max, Hotbar);
        remaining = Merge(id, remaining, max, Backpack);
        remaining = FillEmpty(id, remaining, max, Hotbar);
        remaining = FillEmpty(id, remaining, max, Backpack);
        Changed?.Invoke();
        return remaining;
    }

    private static int Merge(ItemId id, int remaining, int max, ItemStack[] slots)
    {
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (slots[i].Id != id || slots[i].Count >= max)
            {
                continue;
            }

            int take = Math.Min(max - slots[i].Count, remaining);
            slots[i] = slots[i] with { Count = slots[i].Count + take };
            remaining -= take;
        }

        return remaining;
    }

    private static int FillEmpty(ItemId id, int remaining, int max, ItemStack[] slots)
    {
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (!slots[i].IsEmpty)
            {
                continue;
            }

            int take = Math.Min(max, remaining);
            slots[i] = ItemDb.New(id, take);
            remaining -= take;
        }

        return remaining;
    }

    public int CountOf(ItemId id)
    {
        int n = 0;
        foreach (ItemStack s in Hotbar)
        {
            if (s.Id == id)
            {
                n += s.Count;
            }
        }

        foreach (ItemStack s in Backpack)
        {
            if (s.Id == id)
            {
                n += s.Count;
            }
        }

        return n;
    }

    /// <summary>Remove items wherever they are; false (and no change) when short.</summary>
    public bool Consume(ItemId id, int count)
    {
        if (CountOf(id) < count)
        {
            return false;
        }

        int remaining = count;
        remaining = ConsumeFrom(id, remaining, Hotbar);
        ConsumeFrom(id, remaining, Backpack);
        Changed?.Invoke();
        return true;
    }

    private static int ConsumeFrom(ItemId id, int remaining, ItemStack[] slots)
    {
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (slots[i].Id != id)
            {
                continue;
            }

            int take = Math.Min(slots[i].Count, remaining);
            slots[i] = slots[i].Count - take <= 0 ? ItemStack.Empty : slots[i] with { Count = slots[i].Count - take };
            remaining -= take;
        }

        return remaining;
    }

    public int FirstSlotOf(ItemId id)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (Get(i).Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    public int FirstEmptySlot()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (Get(i).IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }

    public void Clear()
    {
        System.Array.Fill(Hotbar, ItemStack.Empty);
        System.Array.Fill(Backpack, ItemStack.Empty);
        System.Array.Fill(Armor, ItemStack.Empty);
    }

    public List<ItemStackSave> ToSave()
    {
        List<ItemStackSave> list = [];
        for (int i = 0; i < SaveSlotCount; i++)
        {
            ItemStack s = Get(i);
            if (!s.IsEmpty)
            {
                list.Add(new ItemStackSave(i, (int)s.Id, s.Count, s.Durability));
            }
        }

        return list;
    }

    /// <summary>Load saved stacks; null (an old save) gets the starter kit.</summary>
    public void LoadFrom(List<ItemStackSave>? items)
    {
        Clear();
        if (items == null)
        {
            foreach ((int slot, ItemId id, int count) in ItemDb.StarterKit())
            {
                Set(slot, ItemDb.New(id, count));
            }
        }
        else
        {
            foreach (ItemStackSave s in items)
            {
                if (s.Slot >= 0 && s.Slot < SaveSlotCount && s.Count > 0)
                {
                    Set(s.Slot, new ItemStack((ItemId)s.Id, s.Count, s.Durability));
                }
            }
        }

        Changed?.Invoke();
    }
}
