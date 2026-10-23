using System;
using System.Collections.Generic;

namespace Minecraft;

/// <summary>
/// A fixed-size block inventory (currently chests). Kept separate from
/// <see cref="Inventory"/> so chest contents do not inherit hotbar/backpack
/// semantics, and so the save format stays a simple slot list.
/// </summary>
public sealed class Container
{
    public const int Size = 27;

    public readonly ItemStack[] Slots = new ItemStack[Size];

    public bool IsEmpty
    {
        get
        {
            foreach (ItemStack stack in Slots)
            {
                if (!stack.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public List<ItemStackSave> ToSave()
    {
        List<ItemStackSave> data = [];
        for (int i = 0; i < Slots.Length; i++)
        {
            ItemStack s = Slots[i];
            if (!s.IsEmpty)
            {
                data.Add(new ItemStackSave(i, (int)s.Id, s.Count, s.Durability));
            }
        }

        return data;
    }

    public void LoadFrom(List<ItemStackSave>? items)
    {
        Array.Fill(Slots, ItemStack.Empty);
        if (items == null)
        {
            return;
        }

        foreach (ItemStackSave s in items)
        {
            if (s.Slot >= 0 && s.Slot < Slots.Length && s.Count > 0)
            {
                Slots[s.Slot] = new ItemStack((ItemId)s.Id, s.Count, s.Durability);
            }
        }
    }

    /// <summary>Add items, merging and then filling empty slots. Returns leftover.</summary>
    public int Add(ItemId id, int count)
    {
        if (id == ItemId.None || count <= 0)
        {
            return 0;
        }

        int max = ItemDb.MaxStack(id);
        int remaining = count;
        for (int i = 0; i < Slots.Length && remaining > 0; i++)
        {
            if (Slots[i].Id != id || Slots[i].Count >= max)
            {
                continue;
            }

            int take = Math.Min(max - Slots[i].Count, remaining);
            Slots[i] = Slots[i] with { Count = Slots[i].Count + take };
            remaining -= take;
        }

        for (int i = 0; i < Slots.Length && remaining > 0; i++)
        {
            if (!Slots[i].IsEmpty)
            {
                continue;
            }

            int take = Math.Min(max, remaining);
            Slots[i] = ItemDb.New(id, take);
            remaining -= take;
        }

        return remaining;
    }
}
