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
    private Dictionary<int, ItemStackSave>? _unrecognizedSavedSlots;

    public bool HasUnrecognizedSaveSlot(int slot) =>
            _unrecognizedSavedSlots?.ContainsKey(slot) ?? false;

    public void ClearUnrecognizedSaveSlot(int slot)
    {
        if (_unrecognizedSavedSlots == null)
        {
            return;
        }

        _unrecognizedSavedSlots.Remove(slot);
        if (_unrecognizedSavedSlots.Count == 0)
        {
            _unrecognizedSavedSlots = null;
        }
    }

    public bool IsEmpty
    {
        get
        {
            if (_unrecognizedSavedSlots is { Count: > 0 })
            {
                return false;
            }

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
                data.Add(new ItemStackSave(i, (int)s.Id, s.Count, s.Durability, s.Sharpness, s.Protection, s.Unbreaking));
            }
        }

        if (_unrecognizedSavedSlots != null)
        {
            foreach (KeyValuePair<int, ItemStackSave> entry in _unrecognizedSavedSlots)
            {
                if (Slots[entry.Key].IsEmpty)
                {
                    data.Add(entry.Value);
                }
            }
        }

        return data;
    }

    public void LoadFrom(List<ItemStackSave>? items)
    {
        Array.Fill(Slots, ItemStack.Empty);
        _unrecognizedSavedSlots = null;
        if (items == null)
        {
            return;
        }

        bool[] loadedSlots = new bool[Slots.Length];
        foreach (ItemStackSave saved in items)
        {
            if (saved.Slot < 0 || saved.Slot >= Slots.Length)
            {
                ItemStackSaveValidator.ReportInvalid("container", saved, "slot is out of range");
                continue;
            }

            if (ItemStackSaveValidator.IsUnrecognizedItemId(saved.Id))
            {
                if (loadedSlots[saved.Slot])
                {
                    ItemStackSaveValidator.ReportInvalid("container", saved, "duplicate slot record");
                    continue;
                }

                loadedSlots[saved.Slot] = true;
                _unrecognizedSavedSlots ??= new Dictionary<int, ItemStackSave>();
                _unrecognizedSavedSlots.Add(saved.Slot, saved);
                ItemStackSaveValidator.ReportUnrecognized("container", saved);
                continue;
            }

            if (!ItemStackSaveValidator.TryCreate(saved, out ItemStack stack, out string reason))
            {
                ItemStackSaveValidator.ReportInvalid("container", saved, reason);
                continue;
            }

            if (loadedSlots[saved.Slot])
            {
                ItemStackSaveValidator.ReportInvalid("container", saved, "duplicate slot record");
                continue;
            }

            loadedSlots[saved.Slot] = true;
            Slots[saved.Slot] = stack;
        }
    }

    /// <summary>Add items, merging and then filling empty slots. Returns leftover.</summary>
    public int Add(ItemId id, int count)
    {
        if (id == ItemId.None || count <= 0)
        {
            return 0;
        }

        if (!ItemDb.IsRegistered(id))
        {
            return count;
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
            if (!Slots[i].IsEmpty || HasUnrecognizedSaveSlot(i))
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
