
using Godot;

namespace Minecraft;

public static class MobContentTests
{
    public static void Run()
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

        // Wildlife uses the existing food inventory and persisted item ids.
        Sheep sheep = new();
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
    }
}
