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

    // UI-owned stacks are inventory state too: shutdown and focus-loss saves
    // must retain cursor-held and crafting-grid items even when the backpack is full.
    public const int CraftingGridSize = 9;
    public const int CraftingGridSaveSlot = SaveSlotCount;
    public const int CursorSaveSlot = CraftingGridSaveSlot + CraftingGridSize;
    public const int PersistentSlotCount = CursorSaveSlot + 1;

    public readonly ItemStack[] Hotbar = new ItemStack[HotbarSize];
    public readonly ItemStack[] Backpack = new ItemStack[BackpackSize];
    public readonly ItemStack[] Armor = new ItemStack[ArmorSlots];  // helmet, chest, legs, boots

    // These stacks are not capacity-bearing backpack slots, but they remain
    // owned by the player and are serialized beside ordinary inventory slots.
    public readonly ItemStack[] CraftingGrid = new ItemStack[CraftingGridSize];
    public ItemStack CursorStack { get; set; } = ItemStack.Empty;

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

    /// <summary>
    /// Consume one durability point from a tool in an inventory slot.
    /// Returns true only when the tool breaks and its slot is cleared.
    /// </summary>
    public bool DamageEquipment(int slot)
    {
        if ((uint)slot >= SlotCount) return false;
        ItemStack stack = Get(slot);
        int max = ItemDb.ToolOf(stack.Id)?.Durability ?? ItemDb.ArmorOf(stack.Id)?.Durability ?? ItemDb.WeaponOf(stack.Id)?.Durability ?? 0;
        if (stack.IsEmpty || max <= 0) return false;
        if (EquipmentProgression.DurabilityDamage(stack, Godot.GD.Randf()) == 0) return false;
        int durability = stack.Durability - 1; bool broke = durability <= 0;
        Set(slot, broke ? ItemStack.Empty : stack with { Durability = durability }); Changed?.Invoke(); return broke;
    }
    public bool DamageTool(int slot) => DamageEquipment(slot);

    /// <summary>Add a stack, preserving durability (tools). Returns leftover count.</summary>
    public int Add(ItemStack stack)
    {
        if (stack.IsEmpty)
        {
            return 0;
        }

        if (!ItemDb.IsRegistered(stack.Id))
        {
            return stack.Count;
        }

        if (ItemDb.MaxStack(stack.Id) == 1)
        {
            // Non-stackable items occupy one slot each. The old fast path
            // stored the whole incoming stack in one slot, which could turn a
            // malformed or transferred multi-count tool into an impossible
            // stack and hide the real leftover from callers.
            int unstacked = stack.Count;
            for (int i = 0; i < SlotCount && unstacked > 0; i++)
            {
                if (!Get(i).IsEmpty)
                {
                    continue;
                }

                Set(i, stack with { Count = 1 });
                unstacked--;
            }

            if (unstacked != stack.Count)
            {
                Changed?.Invoke();
            }

            return unstacked;
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

        if (!ItemDb.IsRegistered(id))
        {
            return count;
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

    /// <summary>True when <paramref name="count"/> of an item fits in the hotbar/backpack.</summary>
    public bool CanFit(ItemId id, int count)
    {
        if (id == ItemId.None || count <= 0)
        {
            return true;
        }

        if (!ItemDb.IsRegistered(id))
        {
            return false;
        }

        int max = ItemDb.MaxStack(id);
        int space = 0;
        space += SpaceIn(id, count, max, Hotbar);
        space += SpaceIn(id, count, max, Backpack);
        return space >= count;
    }

    private static int SpaceIn(ItemId id, int count, int max, ItemStack[] slots)
    {
        int space = 0;
        foreach (ItemStack s in slots)
        {
            if (s.IsEmpty)
            {
                space += max;
            }
            else if (s.Id == id)
            {
                space += Math.Max(max - s.Count, 0);
            }

            if (space >= count)
            {
                return space;
            }
        }

        return space;
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
        if (id == ItemId.None || count < 0)
        {
            return false;
        }

        if (count == 0)
        {
            return true;
        }

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
        System.Array.Fill(CraftingGrid, ItemStack.Empty);
        CursorStack = ItemStack.Empty;
    }

    public bool TryAddAtomic(ItemStack stack)
    {
        if (stack.IsEmpty)
        {
            return true;
        }

        if (!ItemDb.IsRegistered(stack.Id) || !CanFit(stack.Id, stack.Count))
        {
            return false;
        }

        // CanFit and Add use the same hotbar/backpack stack rules and execute
        // synchronously on the main thread, so a successful preflight cannot
        // leave a partial result behind.
        return Add(stack) == 0;
    }

    public int ReturnCraftingGridItems()
    {
        int leftoverTotal = 0;
        for (int i = 0; i < CraftingGrid.Length; i++)
        {
            ItemStack stack = CraftingGrid[i];
            if (stack.IsEmpty)
            {
                continue;
            }

            int leftover = Add(stack);
            CraftingGrid[i] = leftover <= 0
                    ? ItemStack.Empty
                    : stack with { Count = leftover };
            leftoverTotal += leftover;
        }

        return leftoverTotal;
    }

    public int ReturnCursorItem()
    {
        if (CursorStack.IsEmpty)
        {
            return 0;
        }

        ItemStack stack = CursorStack;
        int leftover = Add(stack);
        CursorStack = leftover <= 0
                ? ItemStack.Empty
                : stack with { Count = leftover };
        return leftover;
    }

    public List<ItemStackSave> ToSave()
    {
        List<ItemStackSave> list = [];
        for (int i = 0; i < SaveSlotCount; i++)
        {
            ItemStack stack = Get(i);
            if (!stack.IsEmpty)
            {
                list.Add(ToSave(i, stack));
            }
        }

        for (int i = 0; i < CraftingGrid.Length; i++)
        {
            if (!CraftingGrid[i].IsEmpty)
            {
                list.Add(ToSave(CraftingGridSaveSlot + i, CraftingGrid[i]));
            }
        }

        if (!CursorStack.IsEmpty)
        {
            list.Add(ToSave(CursorSaveSlot, CursorStack));
        }

        return list;
    }

    private static ItemStackSave ToSave(int slot, ItemStack stack) =>
            new(slot, (int)stack.Id, stack.Count, stack.Durability,
                    stack.Sharpness, stack.Protection, stack.Unbreaking);

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
            bool[] loadedSlots = new bool[PersistentSlotCount];
            foreach (ItemStackSave saved in items)
            {
                if (saved.Slot < 0 || saved.Slot >= PersistentSlotCount)
                {
                    ItemStackSaveValidator.ReportInvalid("inventory", saved, "slot is out of range");
                    continue;
                }

                if (!ItemStackSaveValidator.TryCreate(saved, out ItemStack stack, out string reason))
                {
                    ItemStackSaveValidator.ReportInvalid("inventory", saved, reason);
                    continue;
                }

                if (!ItemStackSaveValidator.FitsInventorySlot(saved.Slot, stack))
                {
                    ItemStackSaveValidator.ReportInvalid("inventory", saved, "item does not match the equipment slot");
                    continue;
                }

                if (loadedSlots[saved.Slot])
                {
                    ItemStackSaveValidator.ReportInvalid("inventory", saved, "duplicate slot record");
                    continue;
                }

                loadedSlots[saved.Slot] = true;
                if (saved.Slot < SaveSlotCount)
                {
                    Set(saved.Slot, stack);
                }
                else if (saved.Slot < CursorSaveSlot)
                {
                    CraftingGrid[saved.Slot - CraftingGridSaveSlot] = stack;
                }
                else
                {
                    CursorStack = stack;
                }
            }
        }

        Changed?.Invoke();
    }
}
