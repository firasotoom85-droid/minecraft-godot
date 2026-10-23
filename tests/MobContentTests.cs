
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
    }
}
