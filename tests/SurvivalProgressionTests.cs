using System.Linq;
using Godot;

namespace Minecraft;

/// <summary>Deterministic cross-system survival progression and balance regressions.</summary>
public static class SurvivalProgressionTests
{
    private static bool Craft(Inventory inventory, ItemId result)
    {
        Recipe? recipe = Recipes.All.FirstOrDefault(candidate => candidate.Result == result);
        return recipe is not null && Recipes.Craft(recipe, inventory);
    }

    private static void Equip(Inventory inventory, ItemId id)
    {
        int sourceSlot = inventory.FirstSlotOf(id);
        ArmorInfo armor = ItemDb.ArmorOf(id)!.Value;
        int targetSlot = Inventory.SlotCount + armor.Slot;
        inventory.Set(targetSlot, inventory.Get(sourceSlot));
        inventory.Set(sourceSlot, ItemStack.Empty);
    }

    public static void Run()
    {
        // 1. A genuine empty inventory, followed by the first wood/tool chain.
        Inventory inventory = new();
        TestAssert.Equal(0, inventory.FirstEmptySlot(), "new survival inventory starts empty");
        TestAssert.Equal(1.0f, EquipmentProgression.ArmorDamageMultiplier(inventory),
                "unarmored player receives full incoming damage");
        inventory.Add(ItemId.Log, 4);
        for (int i = 0; i < 4; i++)
        {
            TestAssert.Check(Craft(inventory, ItemId.Planks), "one log crafts into four planks");
        }

        TestAssert.Equal(16, inventory.CountOf(ItemId.Planks), "four logs produce sixteen planks");
        TestAssert.Check(Craft(inventory, ItemId.Stick), "starter sticks craft from planks");
        TestAssert.Check(Craft(inventory, ItemId.WoodPick), "starter wooden pickaxe crafts");
        TestAssert.Check(Craft(inventory, ItemId.WoodSword), "starter wooden sword crafts");
        TestAssert.Check(EquipmentProgression.ShouldConsumeMeleeDurability(ItemId.WoodPick),
                "a tool used as a melee weapon must consume durability");
        TestAssert.Check(EquipmentProgression.ShouldConsumeMeleeDurability(ItemId.WoodSword),
                "a sword melee hit consumes durability");
        TestAssert.Check(!EquipmentProgression.ShouldConsumeMeleeDurability(ItemId.Log),
                "ordinary materials do not consume melee durability");

        // 2. Stone advances the pickaxe and unlocks iron ore harvest.
        inventory.Add(ItemId.Cobblestone, 24);
        int woodenPickSlot = inventory.FirstSlotOf(ItemId.WoodPick);
        TestAssert.Check(EquipmentProgression.TryUpgrade(inventory, woodenPickSlot, out _),
                "wooden pickaxe upgrades to stone with two cobblestone");
        TestAssert.Equal(ItemId.StonePick, inventory.Get(woodenPickSlot).Id,
                "stone pickaxe occupies the upgraded slot");
        TestAssert.Check(!BlockDb.CanHarvest(BlockDb.DefOf(BlockId.IronOre), ItemDb.ToolOf(ItemId.WoodPick)),
                "wood pick cannot harvest iron ore");
        TestAssert.Check(BlockDb.CanHarvest(BlockDb.DefOf(BlockId.IronOre), ItemDb.ToolOf(ItemId.StonePick)),
                "stone pick unlocks iron ore");
        TestAssert.Check(!BlockDb.CanHarvest(BlockDb.DefOf(BlockId.GoldOre), ItemDb.ToolOf(ItemId.StonePick)),
                "stone pick cannot harvest gold ore");
        TestAssert.Check(BlockDb.CanHarvest(BlockDb.DefOf(BlockId.DiamondOre), ItemDb.ToolOf(ItemId.IronPick)),
                "iron pick unlocks diamond ore");

        // 3. Furnace economics: one coal burns for eighty seconds, one item takes ten.
        FurnaceData furnace = new()
        {
            Input = new ItemStack(ItemId.RawIron, 32, 0),
            Fuel = new ItemStack(ItemId.Coal, 4, 0),
        };
        TestAssert.Check(furnace.Tick(0.1f), "furnace consumes initial fuel and starts");
        for (int i = 0; i < 32; i++)
        {
            furnace.Tick(FurnaceData.SmeltSeconds);
        }

        TestAssert.Equal(32, furnace.Output.Count, "four coal smelt thirty-two iron over the full burn budget");
        TestAssert.Check(furnace.Input.IsEmpty, "all input ore is consumed after the smelt run");
        TestAssert.Equal(80.0f, ItemDb.FuelSeconds(ItemId.Coal), "coal burn duration remains eighty seconds");
        TestAssert.Equal(10.0f, FurnaceData.SmeltSeconds, "smelt time remains ten seconds per item");
        TestAssert.Equal(0, inventory.Add(furnace.Output), "smelted ingots fit in the player inventory");
        FurnaceData blockedFurnace = new()
        {
            Input = new ItemStack(ItemId.RawIron, 1, 0),
            Fuel = new ItemStack(ItemId.Diamond, 1, 0),
            SmeltTime = 4.0f,
        };
        blockedFurnace.Tick(0.1f);
        TestAssert.Equal(0.0f, blockedFurnace.SmeltTime,
                "invalid fuel cannot preserve partial smelt progress without active burn time");

        // 4. Craft a full iron armor set, a sword and an iron pick for cave progression.
        inventory.Add(ItemId.Stick, 2);
        foreach (ItemId item in new[]
        {
            ItemId.IronHelmet, ItemId.IronChestplate, ItemId.IronLegs,
            ItemId.IronBoots, ItemId.IronSword, ItemId.IronPick,
        })
        {
            TestAssert.Check(Craft(inventory, item), $"iron progression crafts {item}");
        }

        foreach (ItemId armor in new[]
        {
            ItemId.IronHelmet, ItemId.IronChestplate, ItemId.IronLegs, ItemId.IronBoots,
        })
        {
            Equip(inventory, armor);
        }

        TestAssert.Check(Mathf.IsEqualApprox(0.40f,
                EquipmentProgression.ArmorDamageMultiplier(inventory)),
                "full iron armor mitigates sixty percent of incoming damage");
        TestAssert.Check(Craft(inventory, ItemId.Arrow) == false,
                "arrow crafting is correctly unavailable without gravel and feathers");
        inventory.Add(ItemId.Gravel, 1);
        inventory.Add(ItemId.Feather, 1);
        inventory.Add(ItemId.Stick, 1);
        TestAssert.Check(Craft(inventory, ItemId.Arrow), "gravel, stick and feather craft four arrows");
        TestAssert.Equal(4, inventory.CountOf(ItemId.Arrow), "one arrow recipe batch produces four arrows");
        inventory.Add(ItemId.Bow, 1);
        int bowSlot = inventory.FirstSlotOf(ItemId.Bow);
        TestAssert.Check(inventory.Consume(ItemId.Arrow, 1), "firing a survival bow consumes one arrow");
        inventory.DamageEquipment(bowSlot);
        TestAssert.Equal(3, inventory.CountOf(ItemId.Arrow), "four crafted arrows permit four one-arrow shots");
        TestAssert.Equal(383, inventory.Get(bowSlot).Durability,
                "a successful survival bow shot consumes one bow durability point");

        // 5. Cave rewards give the first lapis; the trader is a costly, reliable fallback.
        CrystalWisp wisp = new();
        TestAssert.Check(wisp.Loot.Any(drop => drop.Id == ItemId.LapisLazuli && drop.Chance > 0.0f),
                "crystal wisp remains a renewable lapis source");
        wisp.Free();
        inventory.Add(ItemId.GoldIngot, 14);
        for (int i = 0; i < 7; i++)
        {
            TestAssert.Check(TraderTradeRules.TryExecute(inventory, ItemId.GoldIngot, 2,
                    ItemId.LapisLazuli, 1), "two gold ingots barter for one lapis");
        }

        inventory.Add(ItemId.Diamond, 2);
        inventory.Add(ItemId.IronIngot, 2);
        TestAssert.Check(Craft(inventory, ItemId.EnchantingTable),
                "two diamonds, two iron ingots and one lapis craft the Enchanting Table");
        TestAssert.Equal(6, inventory.CountOf(ItemId.LapisLazuli),
                "fourteen gold ingots provide the table lapis plus six tiered enchantment lapis");

        // 6. Experience levels are spent cumulatively without losing level-relative progress.
        PlayerStats stats = new();
        stats.AddExperience(PlayerStats.ExperienceAtLevel(8));
        int swordSlot = inventory.FirstSlotOf(ItemId.IronSword);
        for (int level = 1; level <= 3; level++)
        {
            TestAssert.Check(EquipmentProgression.TryEnchant(inventory, swordSlot, stats,
                    Enchantment.Sharpness, level, out _), $"Sharpness {level} applies at its level cost");
        }

        TestAssert.Equal(3, inventory.Get(swordSlot).Sharpness, "Sharpness III is persisted on the weapon stack");
        TestAssert.Equal(2, stats.ExperienceLevel, "Sharpness I–III spend six levels from level eight");
        stats.AddExperience(PlayerStats.ExperienceAtLevel(6));
        inventory.Add(ItemId.GoldIngot, 4);
        for (int i = 0; i < 2; i++)
        {
            TestAssert.Check(TraderTradeRules.TryExecute(inventory, ItemId.GoldIngot, 2,
                    ItemId.LapisLazuli, 1), "trader supplies lapis for durability and armor enchantments");
        }

        TestAssert.Check(EquipmentProgression.TryEnchant(inventory, swordSlot, stats,
                Enchantment.Unbreaking, 1, out _), "Unbreaking I applies to the weapon");
        TestAssert.Equal(1, inventory.Get(swordSlot).Unbreaking, "Unbreaking I is persisted");
        inventory.Add(ItemId.Diamond, 2);
        TestAssert.Check(EquipmentProgression.TryUpgrade(inventory, swordSlot, out _),
                "enchanted iron sword upgrades to diamond");
        TestAssert.Equal(ItemId.DiamondSword, inventory.Get(swordSlot).Id,
                "weapon upgrade advances one tier");
        TestAssert.Equal(3, inventory.Get(swordSlot).Sharpness,
                "weapon upgrade retains Sharpness III");
        TestAssert.Equal(1, inventory.Get(swordSlot).Unbreaking,
                "weapon upgrade retains Unbreaking I");
        stats.AddExperience(PlayerStats.ExperienceAtLevel(2));
        int chestSlot = Inventory.SlotCount + 1;
        TestAssert.Check(EquipmentProgression.TryEnchant(inventory, chestSlot, stats,
                Enchantment.Protection, 1, out _), "Protection I applies to equipped iron chestplate");
        TestAssert.Equal(1, inventory.Get(chestSlot).Protection,
                "Protection I is persisted on equipped armor");

        // 7. Armor values, protection stacking and damage-type durability rules.
        Inventory diamondArmor = new();
        diamondArmor.Armor[0] = ItemDb.New(ItemId.DiamondHelmet, 1);
        diamondArmor.Armor[1] = ItemDb.New(ItemId.DiamondChestplate, 1);
        diamondArmor.Armor[2] = ItemDb.New(ItemId.DiamondLegs, 1);
        diamondArmor.Armor[3] = ItemDb.New(ItemId.DiamondBoots, 1);
        float diamondDamage = EquipmentProgression.ArmorDamageMultiplier(diamondArmor);
        TestAssert.Check(Mathf.IsEqualApprox(0.20f, diamondDamage),
                "full diamond armor caps base mitigation at eighty percent");
        for (int i = 0; i < diamondArmor.Armor.Length; i++)
        {
            diamondArmor.Armor[i] = diamondArmor.Armor[i] with { Protection = 3 };
        }

        TestAssert.Check(EquipmentProgression.ArmorDamageMultiplier(diamondArmor) < diamondDamage,
                "Protection improves defense beyond the unenchanted armor set");
        TestAssert.Check(!EquipmentProgression.ShouldDamageArmor(DamageKind.Drown),
                "drowning bypasses armor wear");
        TestAssert.Check(!EquipmentProgression.ShouldDamageArmor(DamageKind.Starve),
                "starvation bypasses armor wear");
        TestAssert.Check(EquipmentProgression.ShouldDamageArmor(DamageKind.Melee)
                && EquipmentProgression.ShouldDamageArmor(DamageKind.Projectile)
                && EquipmentProgression.ShouldDamageArmor(DamageKind.Fall),
                "physical damage types consume armor durability");

        TestAssert.Check(!EquipmentProgression.ShouldProcessIncomingDamage(4.0f,
                GameMode.Survival, true),
                "respawn invulnerability rejects incoming damage before equipment resolution");
        TestAssert.Check(!EquipmentProgression.ShouldProcessIncomingDamage(0.0f,
                GameMode.Survival, false), "zero-damage hits do not enter equipment resolution");
        TestAssert.Check(!EquipmentProgression.ShouldProcessIncomingDamage(4.0f,
                GameMode.Creative, false), "creative mode rejects incoming damage");
        TestAssert.Check(EquipmentProgression.ShouldProcessIncomingDamage(4.0f,
                GameMode.Survival, false), "normal survival hits enter the damage pipeline");

        ItemStack shield = ItemDb.New(ItemId.Shield, 1);
        DamageInfo frontHit = new(4.0f, DamageKind.Projectile, new Vector3(0, 0, -3));
        TestAssert.Check(EquipmentProgression.BlocksWithShield(shield, new Vector3(0, 0, -1),
                Vector3.Zero, frontHit), "a shield blocks a projectile inside its frontal arc");
        DamageInfo rearHit = new(4.0f, DamageKind.Projectile, new Vector3(0, 0, 3));
        TestAssert.Check(!EquipmentProgression.BlocksWithShield(shield, new Vector3(0, 0, -1),
                Vector3.Zero, rearHit), "a shield does not block an attack from behind");
        DamageInfo fireVolley = new(2.0f, DamageKind.Fire, new Vector3(0, 0, -3));
        TestAssert.Check(!EquipmentProgression.BlocksWithShield(shield, new Vector3(0, 0, -1),
                Vector3.Zero, fireVolley), "the Warden's direct fire pulse bypasses a physical shield block");
        Inventory shieldInventory = new();
        shieldInventory.Set(0, shield);
        TestAssert.Check(!shieldInventory.DamageEquipment(0),
                "a fresh successful shield block does not break the shield");
        TestAssert.Equal(419, shieldInventory.Get(0).Durability,
                "a successful shield block consumes one durability point");

        // 8. Melee and ranged hits both use the shared mob damage interface.
        Shade meleeTarget = new();
        meleeTarget.Health = meleeTarget.MaxHealth;
        TestAssert.Check(meleeTarget.ApplyDamage(new DamageInfo(5.0f, DamageKind.Melee)),
                "melee damage is accepted by a hostile target");
        TestAssert.Equal(15.0f, meleeTarget.Health, "melee hit removes the expected health");
        TestAssert.Equal(5, MobCombatRules.ExperienceReward(meleeTarget),
                "hostile mob defeats award five experience points");
        Briarback defensiveAnimal = new();
        TestAssert.Equal(1, MobCombatRules.ExperienceReward(defensiveAnimal),
                "provoked Briarbacks retain passive-mob experience rewards");
        defensiveAnimal.Free();
        meleeTarget.Free();
        CrystalWisp rangedTarget = new();
        rangedTarget.Health = rangedTarget.MaxHealth;
        TestAssert.Check(rangedTarget.ApplyDamage(new DamageInfo(5.0f, DamageKind.Projectile)),
                "arrow projectile damage is accepted by a cave target");
        TestAssert.Equal(4.0f, rangedTarget.Health, "five-damage arrow reduces a nine-health wisp to four");
        rangedTarget.Free();

        // 9. Boss stats/reward and consistent normal/hard attack tuning.
        EmberWarden warden = new();
        warden.Health = warden.MaxHealth;
        TestAssert.Equal(120.0f, warden.MaxHealth, "Ember Warden has its intended boss health pool");
        TestAssert.Equal(1, EmberWardenRules.PhaseForHealth(120.0f, 120.0f),
                "Warden starts in phase one above the sixty-six-percent threshold");
        TestAssert.Equal(1, EmberWardenRules.PhaseForHealth(79.21f, 120.0f),
                "Warden remains in phase one just above sixty-six percent");
        TestAssert.Equal(2, EmberWardenRules.PhaseForHealth(79.19f, 120.0f),
                "Warden enters phase two below sixty-six percent health");
        TestAssert.Equal(2, EmberWardenRules.PhaseForHealth(39.61f, 120.0f),
                "Warden remains in phase two just above thirty-three percent");
        TestAssert.Equal(3, EmberWardenRules.PhaseForHealth(39.59f, 120.0f),
                "Warden enters phase three below thirty-three percent health");
        TestAssert.Equal(2, EmberWardenRules.AddsForPhase(2),
                "Warden phase two calls two hostile adds");
        TestAssert.Equal(3, EmberWardenRules.AddsForPhase(3),
                "Warden phase three calls three hostile adds");
        TestAssert.Equal(0, EmberWardenRules.AddsForPhase(1),
                "Warden phase one does not summon adds");
        TestAssert.Equal(ItemId.EmberCore, warden.Loot.Single().Id,
                "Ember Warden guarantees its progression reward");
        TestAssert.Equal(25, MobCombatRules.ExperienceReward(warden),
                "Ember Warden awards twenty-five experience points");
        TestAssert.Check(warden.ApplyDamage(new DamageInfo(10.0f, DamageKind.Projectile)),
                "Ember Warden accepts ranged damage through the common combat interface");
        TestAssert.Equal(110.0f, warden.Health,
                "a ten-damage hit reduces Warden health without bypassing its boss pool");
        int wardenDefeats = 0;
        warden.Died += _ => wardenDefeats++;
        while (!warden.Dead)
        {
            TestAssert.Check(warden.ApplyDamage(new DamageInfo(10.0f, DamageKind.Projectile)),
                    "ranged combat advances the Warden boss fight");
        }

        TestAssert.Equal(1, wardenDefeats, "defeating the Warden emits exactly one death event");
        TestAssert.Check(warden.Loot.Any(drop => drop.Id == ItemId.EmberCore && drop.Chance == 1.0f),
                "Warden defeat always grants its Ember Core");
        warden.Free();
        int previousDifficulty = Settings.Difficulty;
        try
        {
            // 9a. Food values and survival drain/regeneration are difficulty-aware.
            ItemDb.FoodInfo rawBeef = ItemDb.FoodOf(ItemId.RawBeef)!.Value;
            ItemDb.FoodInfo cookedBeef = ItemDb.FoodOf(ItemId.CookedBeef)!.Value;
            TestAssert.Equal(2.0f, rawBeef.Hunger, "raw beef restores two hunger points");
            TestAssert.Equal(7.0f, cookedBeef.Hunger, "cooked beef restores seven hunger points");
            TestAssert.Equal(1.0f, cookedBeef.Heal, "cooked beef heals one health point");
            TestAssert.Equal(72, PlayerStats.ExperienceAtLevel(6),
                    "six experience levels require seventy-two total XP from level zero");
            TestAssert.Equal((3, 3), EquipmentProgression.Cost(Enchantment.Sharpness, 3),
                    "Sharpness III requires three levels and three lapis for its final step");
            TestAssert.Equal((4, 3), EquipmentProgression.Cost(Enchantment.Unbreaking, 3),
                    "Unbreaking III requires four levels and three lapis for its final step");

            Settings.Difficulty = Settings.DifficultyNormal;
            PlayerStats movingHunger = new();
            movingHunger.Update(90.0, false, true, GameMode.Survival);
            TestAssert.Equal(19.0f, movingHunger.Hunger,
                    "normal survival drains one hunger point per ninety moving seconds");
            PlayerStats regeneration = new();
            regeneration.ApplyDamage(4.0f, GameMode.Survival);
            regeneration.Update(3.5, false, false, GameMode.Survival);
            TestAssert.Equal(17.0f, regeneration.Health,
                    "well-fed survival regenerates one health point every three-and-a-half seconds");
            TestAssert.Equal(19.0f, regeneration.Hunger,
                    "each well-fed regeneration point costs one hunger point");

            Settings.Difficulty = Settings.DifficultyPeaceful;
            PlayerStats peacefulHunger = new();
            peacefulHunger.Update(90.0, true, true, GameMode.Survival);
            TestAssert.Equal(20.0f, peacefulHunger.Hunger,
                    "peaceful mode disables hunger drain");

            Settings.Difficulty = Settings.DifficultyNormal;
            PlayerStats normalStarvation = new();
            normalStarvation.SetHunger(0.0f);
            normalStarvation.ApplyDamage(19.0f, GameMode.Survival);
            normalStarvation.Update(4.0, false, false, GameMode.Survival);
            TestAssert.Equal(1.0f, normalStarvation.Health,
                    "normal-mode starvation cannot reduce health below one");
            TestAssert.Check(!normalStarvation.Dead,
                    "normal-mode starvation is non-lethal");

            Settings.Difficulty = Settings.DifficultyHard;
            PlayerStats hardStarvation = new();
            hardStarvation.SetHunger(0.0f);
            hardStarvation.ApplyDamage(19.0f, GameMode.Survival);
            hardStarvation.Update(4.0, false, false, GameMode.Survival);
            TestAssert.Check(hardStarvation.Dead,
                    "hard-mode starvation can kill a player at one health");

            Settings.Difficulty = Settings.DifficultyNormal;
            float normalHit = MobCombatRules.DamageForDifficulty(2.5f, 3.25f);
            TestAssert.Equal(7, MobCombatRules.HostilePopulationCap,
                    "normal difficulty caps active hostile population at seven");
            Settings.Difficulty = Settings.DifficultyHard;
            float hardHit = MobCombatRules.DamageForDifficulty(2.5f, 3.25f);
            TestAssert.Equal(10, MobCombatRules.HostilePopulationCap,
                    "hard difficulty raises active hostile population cap to ten");
            TestAssert.Equal(2.5f, normalHit, "normal difficulty uses normal enemy damage");
            TestAssert.Equal(3.25f, hardHit, "hard difficulty increases the same enemy's attack damage");
            TestAssert.Check(MobCombatRules.DamageForDifficulty(4.0f, 5.0f) > 4.0f,
                    "hard-mode Warden slam is stronger than normal");
            Settings.Difficulty = Settings.DifficultyPeaceful;
            TestAssert.Equal(0, MobCombatRules.HostilePopulationCap,
                    "peaceful mode permits no hostile population");
            TestAssert.Equal(0.0f, MobCombatRules.DamageForDifficulty(2.5f, 3.25f),
                    "peaceful mode cancels hostile attacks even during an existing wind-up");
            Mobs population = new();
            TestAssert.Equal(0, population.SpawnKind("shade", 1),
                    "peaceful mode refuses hostile population spawns");
            TestAssert.Check(population.SpawnWarden(Vector3.Zero) is null,
                    "peaceful mode cannot wake an Ember Warden");
            population.Free();
        }
        finally
        {
            Settings.Difficulty = previousDifficulty;
        }

        // 10. Death/respawn is softcore, preserves XP and the player's worn equipment.
        inventory.Set(swordSlot, inventory.Get(swordSlot) with { Durability = 899 });
        int durabilityBeforeDeath = inventory.Get(swordSlot).Durability;
        PlayerStats death = new();
        death.AddExperience(PlayerStats.ExperienceAtLevel(3));
        int savedExperience = death.Experience;
        TestAssert.Check(death.ApplyDamage(PlayerStats.MaxHealth, GameMode.Survival),
                "lethal survival damage is applied");
        TestAssert.Check(death.Dead, "lethal damage enters the dead state");
        death.ResetForRespawn();
        TestAssert.Check(!death.Dead, "respawn restores an active player");
        TestAssert.Equal(savedExperience, death.Experience, "respawn preserves earned experience");
        TestAssert.Equal(durabilityBeforeDeath, inventory.Get(swordSlot).Durability,
                "softcore death preserves worn weapon durability");

        // Inventory overflow must return leftovers without silently deleting resources.
        Inventory full = new();
        full.Set(0, new ItemStack(ItemId.GoldIngot, 10, 0));
        for (int slot = 1; slot < Inventory.SlotCount; slot++)
        {
            full.Set(slot, ItemDb.New(ItemId.DiamondSword, 1));
        }

        TestAssert.Check(!TraderTradeRules.TryExecute(full, ItemId.GoldIngot, 2,
                ItemId.LapisLazuli, 1), "trader rejects an output that cannot fit");
        TestAssert.Equal(10, full.CountOf(ItemId.GoldIngot),
                "failed barter leaves the offered currency untouched");
        TestAssert.Equal(8, full.Add(ItemId.Diamond, 8),
                "full inventory reports all unaccepted items as leftovers");

        // Save/load retains durability, enchantments, equipped armor and XP fields.
        Inventory loaded = new();
        loaded.LoadFrom(inventory.ToSave());
        TestAssert.Equal(ItemId.DiamondSword, loaded.Get(swordSlot).Id,
                "progression inventory survives serialization");
        TestAssert.Equal(durabilityBeforeDeath, loaded.Get(swordSlot).Durability,
                "worn equipment durability survives death and save/load");
        TestAssert.Equal(3, loaded.Get(swordSlot).Sharpness,
                "Sharpness survives save/load in the end-to-end chain");
        TestAssert.Equal(1, loaded.Get(swordSlot).Unbreaking,
                "Unbreaking survives save/load in the end-to-end chain");
        TestAssert.Equal(1, loaded.Get(chestSlot).Protection,
                "equipped Protection survives save/load");
        PlayerStats statsLoaded = new();
        statsLoaded.Load(PlayerStats.MaxHealth, PlayerStats.MaxHunger, stats.Experience);
        TestAssert.Equal(stats.Experience, statsLoaded.Experience,
                "experience points survive player save/load");
    }
}
