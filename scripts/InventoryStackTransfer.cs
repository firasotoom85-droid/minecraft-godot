namespace Minecraft;

/// <summary>
/// Pure stack transfer rules shared by inventory drag-and-drop and regression tests.
/// Slot ownership and UI refresh remain in InventoryUi.
/// </summary>
public static class InventoryStackTransfer
{
    /// <summary>
    /// Move as much as fits into the player inventory and leave every unaccepted
    /// unit in the source slot. The caller commits the updated source immediately.
    /// </summary>
    public static bool TryTransferToInventory(ref ItemStack source, Inventory inventory)
    {
        if (source.IsEmpty)
        {
            return false;
        }

        int originalCount = source.Count;
        int leftover = inventory.Add(source);
        if (leftover >= originalCount)
        {
            return false;
        }

        source = leftover <= 0
                ? ItemStack.Empty
                : source with { Count = leftover };
        return true;
    }

    /// <summary>
    /// Deliver a complete crafting result either to the cursor or to inventory.
    /// Inventory insertion is preflighted so a failed result collection mutates
    /// neither the inventory nor the cursor.
    /// </summary>
    public static bool TryCollectCraftingResult(ItemStack result, ItemStack cursor,
            Inventory inventory, out ItemStack updatedCursor)
    {
        updatedCursor = cursor;
        if (result.IsEmpty || !ItemDb.IsRegistered(result.Id)
                || result.Count > ItemDb.MaxStack(result.Id))
        {
            return false;
        }

        if (cursor.IsEmpty)
        {
            updatedCursor = result;
            return true;
        }

        if (cursor.Id == result.Id
                && cursor.Count + result.Count <= ItemDb.MaxStack(result.Id))
        {
            updatedCursor = cursor with { Count = cursor.Count + result.Count };
            return true;
        }

        return inventory.TryAddAtomic(result);
    }

    public static bool TryTransfer(ref ItemStack source, ref ItemStack target,
            int sourceArmorSlot = -1, int targetArmorSlot = -1)
    {
        if (source.IsEmpty)
            return false;

        if (targetArmorSlot >= 0 && !FitsArmor(source, targetArmorSlot))
            return false;
        if (sourceArmorSlot >= 0 && !target.IsEmpty && !FitsArmor(target, sourceArmorSlot))
            return false;

        if (target.IsEmpty)
        {
            target = source;
            source = ItemStack.Empty;
            return true;
        }

        if (source.Id == target.Id)
        {
            int maxStack = ItemDb.MaxStack(target.Id);
            if (maxStack > 1)
            {
                if (target.Count >= maxStack)
                    return false;

                int amount = System.Math.Min(maxStack - target.Count, source.Count);
                target = target with { Count = target.Count + amount };
                source = source.Count == amount
                        ? ItemStack.Empty
                        : source with { Count = source.Count - amount };
                return amount > 0;
            }
        }

        if (targetArmorSlot >= 0 && !FitsArmor(source, targetArmorSlot))
            return false;
        if (sourceArmorSlot >= 0 && !FitsArmor(target, sourceArmorSlot))
            return false;

        (source, target) = (target, source);
        return true;
    }

    private static bool FitsArmor(ItemStack stack, int slot) =>
        !stack.IsEmpty && stack.Count == 1
        && ItemDb.ArmorOf(stack.Id) is { } armor && armor.Slot == slot;
}
