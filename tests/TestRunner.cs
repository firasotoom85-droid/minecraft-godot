using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Tiny headless test runner. Run with:
///   godot --headless --path . res://tests/TestRunner.tscn
/// Exits non-zero on failure so CI/verify.sh can gate a build.
/// </summary>
public partial class TestRunner : Node
{
    public override void _Ready()
    {
        GD.Print("minecraft tests: start");
        RunSuite("content", ContentTests.Run);
        RunSuite("lighting", LightingTests.Run);
        RunSuite("mining", MiningTests.Run);
        RunSuite("inventory", InventoryTests.Run);
        RunSuite("containers", ContainerTests.Run);
        RunSuite("furnaces", FurnaceTests.Run);
        RunSuite("armor", ArmorTests.Run);
        RunSuite("equipment", EquipmentTests.Run);
        RunSuite("farming", FarmingTests.Run);
        RunSuite("mobs", MobContentTests.Run);
        RunSuite("achievements", AchievementTests.Run);
        RunSuite("dungeon", () => DungeonTests.Run(this));
        RunSuite("fishing", FishingTests.Run);
        RunSuite("keybinds", KeybindTests.Run);
        RunSuite("recipes", RecipeTests.Run);
        RunSuite("stats", StatsTests.Run);
        RunSuite("fluids", FluidTests.Run);
        RunSuite("fluid-world", () => FluidWorldTests.Run(this));
        RunSuite("save", SaveTests.Run);
        RunSuite("world-archives", WorldArchiveTests.Run);
        RunSuite("worldgen", () => WorldgenTests.Run(this));
        GD.Print($"minecraft tests: {TestAssert.Checks} checks, {TestAssert.Failures} failures");
        GetTree().Quit(TestAssert.Failures == 0 ? 0 : 1);
    }

    private static void RunSuite(string name, Action action)
    {
        int before = TestAssert.Failures;
        try
        {
            action();
        }
        catch (Exception e)
        {
            TestAssert.Failures++;
            GD.PushError($"FAIL {name}: unexpected exception {e}");
        }

        GD.Print(TestAssert.Failures == before ? $"PASS {name}" : $"FAIL {name}");
    }
}

public static class TestAssert
{
    public static int Checks;
    public static int Failures;

    public static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition)
        {
            Failures++;
            GD.PushError($"FAIL: {message}");
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        Check(Equals(expected, actual), $"{message} (expected {expected}, got {actual})");
    }

    public static void Empty(System.Collections.Generic.List<string> errors, string message)
    {
        Check(errors.Count == 0, $"{message}: {string.Join("; ", errors)}");
    }
}
