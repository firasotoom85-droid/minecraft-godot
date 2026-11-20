using System.Linq;
namespace Minecraft;
public static class EquipmentTests
{
    private static int ExperienceAtLevelForTest(int level) => PlayerStats.ExperienceAtLevel(level);

    public static void Run()
    {
        TestAssert.Check(ItemDb.WeaponOf(ItemId.WoodSword) is { Damage: 4.0f, Durability: 70 }, "wood sword stats");
        TestAssert.Check(ItemDb.WeaponOf(ItemId.StoneSword)!.Value.Damage > ItemDb.WeaponOf(ItemId.WoodSword)!.Value.Damage, "stone improves damage");
        TestAssert.Check(ItemDb.WeaponOf(ItemId.IronSword)!.Value.Durability > ItemDb.WeaponOf(ItemId.StoneSword)!.Value.Durability, "iron improves durability");
        TestAssert.Check(ItemDb.WeaponOf(ItemId.DiamondSword)!.Value.Damage > ItemDb.WeaponOf(ItemId.IronSword)!.Value.Damage, "diamond is best standard sword");
        TestAssert.Equal(1, ItemDb.MaxStack(ItemId.Shield), "shield is unstackable");
        TestAssert.Equal(384, ItemDb.New(ItemId.Bow, 1).Durability, "bow starts full");
        TestAssert.Check(!string.IsNullOrWhiteSpace(ItemDb.DescriptionOf(ItemId.DiamondSword)), "weapon description exists");
        Inventory inv = new(); inv.Add(ItemId.DiamondSword, 1); inv.DamageEquipment(0); TestAssert.Equal(899, inv.Get(0).Durability, "weapon wears on hit");
        Inventory loaded = new(); loaded.LoadFrom(inv.ToSave()); TestAssert.Equal(899, loaded.Get(0).Durability, "weapon durability persists");
        foreach(ItemId id in new[]{ItemId.WoodSword,ItemId.StoneSword,ItemId.IronSword,ItemId.DiamondSword,ItemId.Bow,ItemId.Arrow,ItemId.Shield,ItemId.EmberBlade})
            TestAssert.Check(Recipes.All.Any(r=>r.Result==id), $"recipe exists for {id}");
        Inventory mats=new();mats.Add(ItemId.Diamond,1);mats.Add(ItemId.Stick,1);Recipe ds=Recipes.All.First(r=>r.Result==ItemId.DiamondSword);
        TestAssert.Check(!Recipes.CanCraft(ds,mats), "diamond sword requires two diamonds");mats.Add(ItemId.Diamond,1);TestAssert.Check(Recipes.CanCraft(ds,mats), "diamond sword crafts with two diamonds");
        TestAssert.Check(Recipes.All.Any(r => r.Result == ItemId.EnchantingTable), "enchanting table has a crafting recipe");
        TestAssert.Equal(BlockId.EnchantingTable, ItemDb.BlockFor(ItemId.EnchantingTable) ?? BlockId.Air, "enchanting table item maps to block");

        PlayerStats xp = new();
        xp.AddExperience(ExperienceAtLevelForTest(5));
        Inventory enchanted = new();
        enchanted.Add(ItemDb.New(ItemId.WoodSword, 1));
        enchanted.Add(ItemId.LapisLazuli, 8);
        TestAssert.Check(EquipmentProgression.TryEnchant(enchanted, 0, xp, Enchantment.Sharpness, 1, out _), "sharpness I enchant applies");
        TestAssert.Equal(1, enchanted.Get(0).Sharpness, "sharpness level persists in stack");
        TestAssert.Check(EquipmentProgression.TryEnchant(enchanted, 0, xp, Enchantment.Unbreaking, 1, out _), "unbreaking I enchant applies");
        TestAssert.Equal(1, enchanted.Get(0).Unbreaking, "unbreaking level stored");
        enchanted.Add(ItemId.Cobblestone, 2);
        TestAssert.Check(EquipmentProgression.TryUpgrade(enchanted, 0, out _), "wood sword upgrades to stone");
        TestAssert.Equal(ItemId.StoneSword, enchanted.Get(0).Id, "upgrade replaces item in same slot");
        TestAssert.Equal(1, enchanted.Get(0).Sharpness, "upgrade preserves sharpness");
        TestAssert.Equal(1, enchanted.Get(0).Unbreaking, "upgrade preserves unbreaking");
        Inventory enchantLoaded = new();
        enchantLoaded.LoadFrom(enchanted.ToSave());
        TestAssert.Equal(1, enchantLoaded.Get(0).Sharpness, "enchantments survive inventory save/load");
        TestAssert.Equal(1, enchantLoaded.Get(0).Unbreaking, "unbreaking survives inventory save/load");
        ItemStack unbreakingThree = enchanted.Get(0) with { Unbreaking = 3 };
        TestAssert.Equal(0, EquipmentProgression.DurabilityDamage(unbreakingThree, 0.5f), "unbreaking level III negates roll");
        TestAssert.Equal(1, EquipmentProgression.DurabilityDamage(unbreakingThree, 0.95f), "unbreaking level III still wears sometimes");
        Inventory armorEnchant = new();
        armorEnchant.Set(Inventory.SlotCount, ItemDb.New(ItemId.IronChestplate, 1));
        armorEnchant.Add(ItemId.LapisLazuli, 1);
        PlayerStats armorXp = new();
        armorXp.AddExperience(ExperienceAtLevelForTest(2));
        TestAssert.Check(EquipmentProgression.TryEnchant(armorEnchant, Inventory.SlotCount, armorXp, Enchantment.Protection, 1, out _), "protection applies to armor slot");
        TestAssert.Equal(1, armorEnchant.Get(Inventory.SlotCount).Protection, "protection level stored");
        TestAssert.Check(!EquipmentProgression.TryEnchant(armorEnchant, Inventory.SlotCount, armorXp, Enchantment.Sharpness, 1, out _), "sharpness rejected on armor");
        TestAssert.Equal(1, EquipmentProgression.Cost(Enchantment.Sharpness, 1).Lapis, "enchant cost has lapis");
        TestAssert.Equal(3, EquipmentProgression.Cost(Enchantment.Unbreaking, 2).Levels, "unbreaking cost scales by level");
        PlayerStats levelTest = new();
        levelTest.AddExperience(PlayerStats.ExperienceAtLevel(5));
        TestAssert.Equal(5, levelTest.ExperienceLevel, "experience points convert to levels");
        TestAssert.Check(levelTest.TrySpendLevels(2), "experience levels can be spent");
        TestAssert.Equal(3, levelTest.ExperienceLevel, "spending levels preserves remaining level progress");
        Inventory armorUpgrade = new();
        armorUpgrade.Set(Inventory.SlotCount, ItemDb.New(ItemId.IronHelmet, 1));
        armorUpgrade.Add(ItemId.Diamond, 1);
        TestAssert.Check(EquipmentProgression.TryUpgrade(armorUpgrade, Inventory.SlotCount, out _), "iron armor upgrades from armor row");
        TestAssert.Equal(ItemId.DiamondHelmet, armorUpgrade.Get(Inventory.SlotCount).Id, "armor upgrade result is diamond helmet");
        Inventory toolUpgrade = new();
        toolUpgrade.Add(ItemDb.New(ItemId.WoodPick, 1));
        toolUpgrade.Add(ItemId.Cobblestone, 2);
        TestAssert.Check(EquipmentProgression.TryUpgrade(toolUpgrade, 0, out _), "wood pick upgrades to stone");
        TestAssert.Equal(ItemId.StonePick, toolUpgrade.Get(0).Id, "tool upgrade advances one tier");
        ItemStack shield = ItemDb.New(ItemId.Shield, 1);
        DamageInfo frontArrow = new(4, DamageKind.Projectile, new Godot.Vector3(0, 0, -3));
        DamageInfo rearArrow = new(4, DamageKind.Projectile, new Godot.Vector3(0, 0, 3));
        TestAssert.Check(EquipmentProgression.BlocksWithShield(shield, new Godot.Vector3(0, 0, -1), Godot.Vector3.Zero, frontArrow), "shield blocks frontal arrows");
        TestAssert.Check(!EquipmentProgression.BlocksWithShield(shield, new Godot.Vector3(0, 0, -1), Godot.Vector3.Zero, rearArrow), "shield does not block rear arrows");
        TestAssert.Check(!EquipmentProgression.BlocksWithShield(shield, new Godot.Vector3(0, 0, -1), Godot.Vector3.Zero, new DamageInfo(4, DamageKind.Fire)), "shield does not block environmental damage");
        TestAssert.Equal(3.0f, EquipmentProgression.SharpnessBonus(enchanted.Get(0) with { Sharpness = 3 }), "sharpness III adds three damage");
        TestAssert.Equal(0.12f, EquipmentProgression.ProtectionBonus(3), "protection III adds twelve percent mitigation");
        Godot.Vector3 fallingArrow = ArrowProjectile.AdvanceVelocity(new Godot.Vector3(0, 10, 0), 0.5f);
        TestAssert.Check(fallingArrow.Y < 10.0f, "arrow gravity reduces upward velocity");
        TestAssert.Check(ArrowProjectile.AdvanceVelocity(Godot.Vector3.Zero, 0.5f).Y < 0.0f, "arrow accelerates downward from rest");


    }
}