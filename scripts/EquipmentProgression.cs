using Godot;

namespace Minecraft;

public enum Enchantment : byte
{
    Sharpness,
    Protection,
    Unbreaking,
}

/// <summary>Deterministic rules for enchanting, upgrading and durability balance.</summary>
public static class EquipmentProgression
{
    public static int MaxLevel(Enchantment enchantment) => 3;

    public static bool CanEnchant(ItemId item, Enchantment enchantment) => enchantment switch
    {
        Enchantment.Sharpness => ItemDb.WeaponOf(item) is { Damage: > 0 } && item != ItemId.Bow,
        Enchantment.Protection => ItemDb.ArmorOf(item) != null,
        Enchantment.Unbreaking => ItemDb.WeaponOf(item) != null || ItemDb.ToolOf(item) != null || ItemDb.ArmorOf(item) != null,
        _ => false,
    };

    public static int LevelOf(ItemStack stack, Enchantment enchantment) => enchantment switch
    {
        Enchantment.Sharpness => stack.Sharpness,
        Enchantment.Protection => stack.Protection,
        Enchantment.Unbreaking => stack.Unbreaking,
        _ => 0,
    };

    public static (int Levels, int Lapis) Cost(Enchantment enchantment, int level)
    {
        if (level is < 1 or > 3) return (int.MaxValue, int.MaxValue);
        return enchantment switch
        {
            Enchantment.Sharpness => (level, level),
            Enchantment.Protection => (level, level),
            Enchantment.Unbreaking => (level + 1, level),
            _ => (int.MaxValue, int.MaxValue),
        };
    }

    public static bool TryEnchant(Inventory inventory, int slot, PlayerStats stats,
            Enchantment enchantment, int level, out string reason)
    {
        reason = "";
        if ((uint)slot >= Inventory.SaveSlotCount) { reason = "Select an inventory item"; return false; }
        ItemStack stack = inventory.Get(slot);
        if (stack.IsEmpty || !CanEnchant(stack.Id, enchantment)) { reason = "This item cannot receive that enchantment"; return false; }
        int current = LevelOf(stack, enchantment);
        if (level != current + 1 || level > MaxLevel(enchantment)) { reason = "Enchantments must be applied one level at a time"; return false; }
        (int levels, int lapis) = Cost(enchantment, level);
        if (stats.ExperienceLevel < levels) { reason = $"Requires {levels} experience levels"; return false; }
        if (inventory.CountOf(ItemId.LapisLazuli) < lapis) { reason = $"Requires {lapis} lapis lazuli"; return false; }
        if (!stats.TrySpendLevels(levels)) { reason = "Not enough experience"; return false; }
        inventory.Consume(ItemId.LapisLazuli, lapis);
        ItemStack enchanted = enchantment switch
        {
            Enchantment.Sharpness => stack with { Sharpness = level },
            Enchantment.Protection => stack with { Protection = level },
            Enchantment.Unbreaking => stack with { Unbreaking = level },
            _ => stack,
        };
        inventory.Set(slot, enchanted);
        inventory.RaiseChanged();
        reason = $"{enchantment} {level} applied";
        return true;
    }

    public static int DurabilityDamage(ItemStack stack, float roll)
    {
        int unbreaking = Mathf.Clamp(stack.Unbreaking, 0, 3);
        return roll < (float)unbreaking / (unbreaking + 1) ? 0 : 1;
    }

    public static bool BlocksWithShield(ItemStack held, Vector3 forward, Vector3 playerPosition, in DamageInfo info)
    {
        if (held.Id != ItemId.Shield || info.Kind is not (DamageKind.Melee or DamageKind.Projectile) || info.Source == Vector3.Zero) return false;
        Vector3 incoming = playerPosition - info.Source;
        incoming.Y = 0.0f;
        forward.Y = 0.0f;
        return incoming.LengthSquared() > 0.001f && forward.LengthSquared() > 0.001f
            && forward.Normalized().Dot(incoming.Normalized()) >= 0.35f;
    }

    public static float ProtectionMultiplier(ItemStack stack) =>
        1.0f - Mathf.Clamp(stack.Protection, 0, 3) * 0.04f;

    public static float ProtectionBonus(int totalLevels) => Mathf.Clamp(totalLevels, 0, 12) * 0.04f;

    public static float SharpnessBonus(ItemStack stack) => Mathf.Clamp(stack.Sharpness, 0, 3) * 1.0f;

    public static ItemId UpgradeTarget(ItemId id) => id switch
    {
        ItemId.WoodSword => ItemId.StoneSword, ItemId.StoneSword => ItemId.IronSword,
        ItemId.IronSword => ItemId.DiamondSword,
        ItemId.WoodPick => ItemId.StonePick, ItemId.WoodAxe => ItemId.StoneAxe,
        ItemId.WoodShovel => ItemId.StoneShovel, ItemId.WoodHoe => ItemId.StoneHoe,
        ItemId.StonePick => ItemId.IronPick, ItemId.StoneAxe => ItemId.IronAxe,
        ItemId.StoneShovel => ItemId.IronShovel, ItemId.StoneHoe => ItemId.IronHoe,
        ItemId.IronHelmet => ItemId.DiamondHelmet,
        ItemId.IronChestplate => ItemId.DiamondChestplate, ItemId.IronLegs => ItemId.DiamondLegs,
        ItemId.IronBoots => ItemId.DiamondBoots, ItemId.IronPick => ItemId.DiamondPick,
        ItemId.IronAxe => ItemId.DiamondAxe, ItemId.IronShovel => ItemId.DiamondShovel,
        ItemId.IronHoe => ItemId.DiamondHoe, _ => ItemId.None,
    };

    public static ItemId UpgradeMaterial(ItemId id) => id switch
    {
        ItemId.WoodSword or ItemId.WoodPick or ItemId.WoodAxe or ItemId.WoodShovel or ItemId.WoodHoe => ItemId.Cobblestone,
        ItemId.StoneSword or ItemId.StonePick or ItemId.StoneAxe or ItemId.StoneShovel or ItemId.StoneHoe => ItemId.IronIngot,
        ItemId.IronSword => ItemId.Diamond,
        ItemId.IronHelmet or ItemId.IronChestplate or ItemId.IronLegs or ItemId.IronBoots
            or ItemId.IronPick or ItemId.IronAxe or ItemId.IronShovel or ItemId.IronHoe => ItemId.Diamond,
        _ => ItemId.None,
    };

    public static int UpgradeMaterialCount(ItemId id) => id is ItemId.WoodSword or ItemId.StoneSword or ItemId.IronSword
        or ItemId.WoodPick or ItemId.WoodAxe or ItemId.StonePick or ItemId.StoneAxe or ItemId.IronPick or ItemId.IronAxe ? 2 : 1;

    public static bool TryUpgrade(Inventory inventory, int slot, out string reason)
    {
        reason = "";
        if ((uint)slot >= Inventory.SaveSlotCount) { reason = "Select an inventory item"; return false; }
        ItemStack source = inventory.Get(slot);
        ItemId target = UpgradeTarget(source.Id);
        ItemId material = UpgradeMaterial(source.Id);
        if (source.IsEmpty || target == ItemId.None || material == ItemId.None)
        { reason = "This equipment has no available upgrade"; return false; }
        int amount = UpgradeMaterialCount(source.Id);
        if (inventory.CountOf(material) < amount) { reason = $"Requires {amount} {ItemDb.NameOf(material)}"; return false; }
        int sourceMax = MaxDurability(source.Id);
        int targetMax = MaxDurability(target);
        int durability = sourceMax > 0 ? Mathf.Clamp(Mathf.RoundToInt((float)source.Durability / sourceMax * targetMax), 1, targetMax) : targetMax;
        inventory.Consume(material, amount);
        inventory.Set(slot, new ItemStack(target, 1, durability, source.Sharpness, source.Protection, source.Unbreaking));
        inventory.RaiseChanged();
        reason = $"Upgraded to {ItemDb.NameOf(target)}";
        return true;
    }

    public static int MaxDurability(ItemId id) =>
        ItemDb.WeaponOf(id)?.Durability ?? ItemDb.ToolOf(id)?.Durability ?? ItemDb.ArmorOf(id)?.Durability ?? 0;
}
