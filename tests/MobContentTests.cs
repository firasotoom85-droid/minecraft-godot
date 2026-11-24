
using Godot;

namespace Minecraft;

public static class MobContentTests
{
    public static void Run(Node host)
    {
        Cow cow = new();
        TestAssert.Equal("Cow", cow.DisplayName, "cow name");
        TestAssert.Equal(10.0f, cow.MaxHealth, "cow health");
        TestAssert.Check(cow.Loot.Length >= 2, "cow drops beef and leather");

        Pig pig = new();
        TestAssert.Equal("Pig", pig.DisplayName, "pig name");
        TestAssert.Equal(ItemId.RawPork, pig.Loot[0].Id, "pig drops pork");

        Chicken chicken = new();
        TestAssert.Equal("Chicken", chicken.DisplayName, "chicken name");
        TestAssert.Equal(4.0f, chicken.MaxHealth, "chicken health");

        Rot rot = new();
        TestAssert.Equal("Rot", rot.DisplayName, "rot name");
        TestAssert.Equal(14.0f, rot.MaxHealth, "rot health");

        Hollow hollow = new();
        TestAssert.Equal("Hollow", hollow.DisplayName, "hollow name");
        TestAssert.Equal(12.0f, hollow.MaxHealth, "hollow health");

        TestAssert.Equal(7.0f, ItemDb.FoodOf(ItemId.CookedBeef)?.Hunger, "steak restores seven hunger");
        TestAssert.Equal(ItemId.CookedBeef, ItemDb.CooksTo(ItemId.RawBeef), "campfire cooks beef");
        TestAssert.Equal(ItemId.CookedPork, ItemDb.SmeltsTo(ItemId.RawPork), "furnace cooks pork");
        TestAssert.Equal(64, ItemDb.MaxStack(ItemId.Leather), "leather stacks to 64");

        TestAssert.Check(Mobs.IsBabySpawnCellSafe(BlockId.Grass, BlockId.Air, BlockId.Air, BlockId.Air),
                "newborn spawn accepts solid floor with adult head clearance");
        TestAssert.Check(!Mobs.IsBabySpawnCellSafe(BlockId.Air, BlockId.Air, BlockId.Air, BlockId.Air),
                "newborn spawn rejects unsupported air");
        TestAssert.Check(!Mobs.IsBabySpawnCellSafe(BlockId.Grass, BlockId.Stone, BlockId.Air, BlockId.Air),
                "newborn spawn rejects a blocked feet cell");
        TestAssert.Check(!Mobs.IsBabySpawnCellSafe(BlockId.Grass, BlockId.Air, BlockId.Stone, BlockId.Air),
                "newborn spawn rejects a blocked body cell");
        TestAssert.Check(!Mobs.IsBabySpawnCellSafe(BlockId.Grass, BlockId.Air, BlockId.Air, BlockId.Stone),
                "newborn spawn reserves clearance for later growth");

        TestAssert.Check(MobCombatRules.CanAttack(0.0f, true),
                "hostile attack becomes available when cooldown expires in sight");
        TestAssert.Check(!MobCombatRules.CanAttack(0.01f, true),
                "hostile attack cooldown prevents repeated hits");
        TestAssert.Check(!MobCombatRules.CanAttack(0.0f, false),
                "hostile melee cannot hit through blocked line of sight");
        TestAssert.Check(MobCombatRules.CanBeginTelegraph(0.0f, 6.0f, 8.5f, true),
                "pounce telegraph starts inside range when ready and visible");
        TestAssert.Check(!MobCombatRules.CanBeginTelegraph(1.0f, 6.0f, 8.5f, true),
                "pounce telegraph waits for its cooldown");
        TestAssert.Check(!MobCombatRules.CanBeginTelegraph(0.0f, 9.0f, 8.5f, true),
                "pounce telegraph does not start outside range");
        TestAssert.Check(!MobCombatRules.CanBeginTelegraph(0.0f, 6.0f, 8.5f, false),
                "pounce telegraph requires clear line of sight");

        Sheep sheep = new();
        Mobs.HerdProfile chickenHerd = Mobs.HerdProfile.For(chicken);
        Mobs.HerdProfile cowHerd = Mobs.HerdProfile.For(cow);
        TestAssert.Check(chickenHerd.Minimum > cowHerd.Minimum
                && chickenHerd.Maximum > cowHerd.Maximum,
                "chickens form larger flocks than cattle");
        TestAssert.Check(chickenHerd.Spacing < cowHerd.Spacing,
                "small chicken flocks use tighter spacing");
        TestAssert.Check(Mobs.HerdProfile.For(sheep).Cohesion > cowHerd.Cohesion,
                "sheep use stronger herd cohesion than cattle");

        TestAssert.Equal(99.82f, Wanderer.DrainHunger(100.0f, 10.0f),
                "animal hunger drains gradually with elapsed time");
        TestAssert.Equal(100.0f, Wanderer.RestoreHunger(80.0f, 42.0f),
                "grazing restores hunger without exceeding its cap");
        TestAssert.Equal(0.72f, Wanderer.MovementMultiplierForHunger(10.0f),
                "starving animals move more slowly");
        TestAssert.Equal(1.0f, Wanderer.MovementMultiplierForHunger(70.0f),
                "well-fed animals retain normal movement speed");
        TestAssert.Check(Wanderer.IsGrazeable(BlockId.Grass)
                && Wanderer.IsGrazeable(BlockId.SnowGrass),
                "grass and snowy grass support grazing");
        TestAssert.Check(!Wanderer.IsGrazeable(BlockId.Stone)
                && !Wanderer.IsGrazeable(BlockId.Dirt),
                "stone and dirt are not grazing targets");

        TestAssert.Check(Mob.CanDetectAtLight(2, 3, 5),
                "cave creature detects targets in dim light");
        TestAssert.Check(!Mob.CanDetectAtLight(9, 2, 5),
                "bright skylight suppresses dark-adapted cave detection");
        TestAssert.Check(!Mob.CanDetectAtLight(2, 8, 5),
                "bright block light suppresses cave detection");

        TestAssert.Check(MobProjectileRules.ShouldStopOnTerrain(true),
                "projectile terminates on terrain impact");
        TestAssert.Check(!MobProjectileRules.ShouldStopOnTerrain(false),
                "projectile continues without terrain collision");
        TestAssert.Check(MobProjectileRules.ShouldDamagePlayer(true, false),
                "player impact applies projectile damage");
        TestAssert.Check(!MobProjectileRules.ShouldDamagePlayer(true, true),
                "terrain impact prevents projectile damage through walls");
        TestAssert.Check(!MobProjectileRules.ShouldDamagePlayer(false, false),
                "non-player overlap cannot take projectile damage");
        TestAssert.Check(!MobProjectileRules.ShouldDamagePlayer(true, false, true),
                "hostile projectiles cannot damage players in peaceful mode");

        // Wildlife uses the existing food inventory and persisted item ids.
        TestAssert.Check(sheep.IsPassive, "sheep uses passive wildlife behavior");
        TestAssert.Equal(ItemId.Wheat, sheep.BreedingFood, "sheep breeds with wheat");
        TestAssert.Check(cow.IsPassive, "cow uses passive wildlife behavior");
        TestAssert.Equal(ItemId.Wheat, cow.BreedingFood, "cow breeds with wheat");
        TestAssert.Check(pig.IsPassive, "pig uses passive wildlife behavior");
        TestAssert.Equal(ItemId.Wheat, pig.BreedingFood, "pig breeds with wheat");
        TestAssert.Check(chicken.IsPassive, "chicken uses passive wildlife behavior");
        TestAssert.Equal(ItemId.Seeds, chicken.BreedingFood, "chicken breeds with seeds");

        Player sheepFeeder = new();
        sheepFeeder.Inventory.Add(ItemId.Wheat, 1);
        TestAssert.Check(sheep.Interact(sheepFeeder), "sheep accepts wheat feed");
        TestAssert.Check(sheep.InLove, "feeding sheep starts breeding state");
        TestAssert.Equal(0, sheepFeeder.Inventory.CountOf(ItemId.Wheat),
                "sheep feed consumes one wheat");
        TestAssert.Check(!sheep.Tamed, "feeding sheep does not replace taming state");

        Sheep sheepPartner = new();
        Player partnerFeeder = new();
        partnerFeeder.Inventory.Add(ItemId.Wheat, 1);
        TestAssert.Check(sheepPartner.Interact(partnerFeeder),
                "second sheep accepts breeding food");
        TestAssert.Check(sheep.CanBreedWith(sheepPartner),
                "compatible fed adults can enter breeding pair");
        TestAssert.Check(!sheep.CanBreedWith(chicken),
                "different species cannot form breeding pair");
        sheep.MarkBred();
        sheepPartner.MarkBred();
        TestAssert.Check(!sheep.CanBreedWith(sheepPartner),
                "breeding cooldown prevents immediate repeat pairing");

        Player cowFeeder = new();
        cowFeeder.Inventory.Add(ItemId.Wheat, 1);
        TestAssert.Check(cow.Interact(cowFeeder), "cow accepts wheat feed");
        TestAssert.Check(cow.InLove, "feeding cow starts breeding state");

        Player chickenFeeder = new();
        chickenFeeder.Inventory.Add(ItemId.Seeds, 1);
        TestAssert.Check(chicken.Interact(chickenFeeder), "chicken accepts seed feed");
        TestAssert.Check(chicken.InLove, "feeding chicken starts breeding state");

        float adultChickenSpeed = chicken.Speed;
        chicken.SetBaby(30.0f);
        TestAssert.Check(chicken.IsBaby, "newborn animal enters juvenile state");
        TestAssert.Equal(new Vector3(0.65f, 0.65f, 0.65f), chicken.Scale,
                "juvenile animal has reduced scale");
        TestAssert.Check(chicken.Speed < adultChickenSpeed,
                "juvenile animal moves more slowly");

        Briarback briarback = new();
        TestAssert.Equal("Briarback", briarback.DisplayName, "briarback name");
        TestAssert.Equal(20.0f, briarback.MaxHealth, "briarback defensive health");
        TestAssert.Check(briarback.IsPassive, "briarback is a defensive passive creature");
        TestAssert.Equal(ItemId.None, briarback.BreedingFood,
                "briarback does not consume livestock breeding feed");

        CaveStalker stalker = new();
        TestAssert.Equal("Cave Stalker", stalker.DisplayName, "cave stalker name");
        TestAssert.Equal(11.0f, stalker.MaxHealth, "cave stalker health");
        TestAssert.Equal(ItemId.RawPork, stalker.Loot[0].Id, "cave stalker has meat loot");

        CrystalWisp wisp = new();
        TestAssert.Equal("Crystal Wisp", wisp.DisplayName, "crystal wisp name");
        TestAssert.Equal(9.0f, wisp.MaxHealth, "crystal wisp health");
        TestAssert.Equal(ItemId.Coal, wisp.Loot[0].Id, "crystal wisp has cave loot");

        Dreadwing dreadwing = new();
        TestAssert.Equal("Dreadwing", dreadwing.DisplayName, "dreadwing name");
        TestAssert.Equal(16.0f, dreadwing.MaxHealth, "dreadwing health");
        TestAssert.Equal(ItemId.Feather, dreadwing.Loot[0].Id, "dreadwing has feather loot");

        Shade shade = new();
        TestAssert.Equal("Shade", shade.DisplayName, "shade name");
        TestAssert.Check(shade.Loot.Length > 0, "Shade has a loot table");
        TestAssert.Check(rot.Loot.Length > 0, "Rot has a loot table");
        TestAssert.Check(hollow.Loot.Length > 0, "Hollow has a loot table");

        MobLootOverflowTests.Run(host);

        // Direct death paths (sunburn/dawn) do not pass through ApplyDamage.
        // They must still mark the mob dead and raise Died exactly once while
        // QueueFree leaves the node valid for the rest of the frame.
        MobDeathProbe probe = new();
        int deathEvents = 0;
        probe.Died += _ => deathEvents++;
        host.AddChild(probe);
        probe.ForceDie();
        probe.ForceDie();
        TestAssert.Equal(1, deathEvents, "a direct death raises Died only once");
        TestAssert.Check(probe.Dead, "a direct death marks the mob dead before deferred deletion");

        TestAssert.FreeAll(cow, pig, chicken, rot, hollow, sheep, sheepPartner,
                briarback, stalker, wisp, dreadwing, shade,
                sheepFeeder, partnerFeeder, cowFeeder, chickenFeeder);
    }
}

public partial class MobDeathProbe : Mob
{
    public void ForceDie() => Die();
}
