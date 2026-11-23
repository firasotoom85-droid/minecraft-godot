using Godot;

namespace Minecraft;

/// <summary>
/// Canonical validation policy for persisted item stacks. Invalid records are
/// skipped, while unknown future IDs remain opaque and retain their original slot.
/// </summary>
public static class ItemStackSaveValidator
{
    public static bool IsUnrecognizedItemId(int savedId)
    {
        if (savedId <= (int)ItemId.None || savedId > byte.MaxValue)
        {
            return false;
        }

        ItemId id = (ItemId)(byte)savedId;
        return !System.Enum.IsDefined(typeof(ItemId), id);
    }

    public static bool TryCreate(ItemStackSave saved, out ItemStack stack, out string reason)
    {
        stack = ItemStack.Empty;
        reason = string.Empty;

        if (saved.Id < byte.MinValue || saved.Id > byte.MaxValue)
        {
            reason = "item ID is outside the persisted byte range";
            return false;
        }

        ItemId id = (ItemId)saved.Id;
        if (id == ItemId.None || !System.Enum.IsDefined(typeof(ItemId), id))
        {
            reason = "item ID is not registered";
            return false;
        }

        if (saved.Count <= 0 || saved.Count > ItemDb.MaxStack(id))
        {
            reason = "stack count is outside the item's maximum stack size";
            return false;
        }

        int maxDurability = EquipmentProgression.MaxDurability(id);
        if (maxDurability == 0)
        {
            if (saved.Durability != 0)
            {
                reason = "non-equipment item has durability metadata";
                return false;
            }

            if (saved.Sharpness != 0 || saved.Protection != 0 || saved.Unbreaking != 0)
            {
                reason = "non-equipment item has enchantment metadata";
                return false;
            }
        }
        else
        {
            if (saved.Count != 1 || saved.Durability <= 0 || saved.Durability > maxDurability)
            {
                reason = "equipment count or durability is outside its valid range";
                return false;
            }

            if (!ValidEnchantment(id, Enchantment.Sharpness, saved.Sharpness)
                    || !ValidEnchantment(id, Enchantment.Protection, saved.Protection)
                    || !ValidEnchantment(id, Enchantment.Unbreaking, saved.Unbreaking))
            {
                reason = "enchantment metadata is unsupported or outside its valid level range";
                return false;
            }
        }

        stack = new ItemStack(id, saved.Count, saved.Durability,
                saved.Sharpness, saved.Protection, saved.Unbreaking);
        return true;
    }

    public static bool FitsInventorySlot(int slot, ItemStack stack)
    {
        if (slot < Inventory.SlotCount || slot >= Inventory.SaveSlotCount)
        {
            return true;
        }

        int armorSlot = slot - Inventory.SlotCount;
        return ItemDb.ArmorOf(stack.Id) is { } armor && armor.Slot == armorSlot;
    }

    public static bool FitsFurnaceSlot(int slot, ItemStack stack) => slot switch
    {
        0 => ItemDb.SmeltsTo(stack.Id) != null,
        1 => ItemDb.FuelSeconds(stack.Id) > 0.0f,
        2 => IsSmeltResult(stack.Id),
        _ => false,
    };

    private static bool IsSmeltResult(ItemId id) => id is
            ItemId.IronIngot or ItemId.GoldIngot or ItemId.Glass or ItemId.Stone
            or ItemId.CookedMutton or ItemId.CookedBeef or ItemId.CookedPork
            or ItemId.CookedChicken or ItemId.CookedFish;

    public static void ReportUnrecognized(string owner, ItemStackSave saved) =>
            GD.PushWarning($"minecraft: preserved unrecognized {owner} item save at slot {saved.Slot} "
                    + $"(id={saved.Id}, count={saved.Count}) as opaque data for content-version compatibility");

    public static void ReportInvalid(string owner, ItemStackSave saved, string reason) =>
            GD.PushWarning($"minecraft: skipped invalid {owner} item save at slot {saved.Slot} "
                    + $"(id={saved.Id}, count={saved.Count}): {reason}");

    private static bool ValidEnchantment(ItemId id, Enchantment enchantment, int level) =>
            level >= 0 && level <= EquipmentProgression.MaxLevel(enchantment)
            && (level == 0 || EquipmentProgression.CanEnchant(id, enchantment));
}
