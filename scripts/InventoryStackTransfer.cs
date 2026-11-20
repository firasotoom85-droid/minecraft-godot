namespace Minecraft;

/// <summary>
/// Pure stack transfer rules shared by inventory drag-and-drop and regression tests.
/// Slot ownership and UI refresh remain in InventoryUi.
/// </summary>
public static class InventoryStackTransfer
{
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
