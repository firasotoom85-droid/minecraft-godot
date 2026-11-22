using System;
using System.Threading.Tasks;
using Godot;

namespace Minecraft;

/// <summary>
/// Tiny headless test runner. Run with:
///   godot --headless --path . res://tests/TestRunner.tscn
/// Exits non-zero on failure so CI/verify.sh can gate a build.
/// </summary>
public partial class TestRunner : Node
{
    public override async void _Ready()
    {
        GD.Print("minecraft tests: start");
        RunSuite("content", ContentTests.Run);
        RunSuite("item-icons", ItemIconTests.Run);
        RunSuite("performance-benchmark", PerformanceBenchmarkTests.Run);
        RunSuite("voxel-streaming", VoxelStreamingTests.Run);
        RunSuite("graphics", GraphicsTests.Run);
        RunSuite("biome-foliage", () => FoliageTests.Run(this));
        RunSuite("surface-variation", SurfaceVariationTests.Run);
        RunSuite("wood-textures", WoodTextureTests.Run);
        RunSuite("terrain-materials", TerrainMaterialTests.Run);
        RunSuite("texture-scale", TextureScaleTests.Run);
        RunSuite("block-shapes", () => BlockShapeTests.Run(this));
        RunSuite("selection-outline", SelectionOutlineTests.Run);
        RunSuite("lighting", LightingTests.Run);
        RunSuite("transparent-materials", () => GlassTests.Run(this));
        RunSuite("atmosphere", AtmosphereTests.Run);
        RunSuite("mining", MiningTests.Run);
        RunSuite("inventory", InventoryTests.Run);
        RunSuite("containers", ContainerTests.Run);
        RunSuite("furnaces", FurnaceTests.Run);
        RunSuite("armor", ArmorTests.Run);
        RunSuite("equipment", EquipmentTests.Run);
        RunSuite("equipment-durability-visuals", EquipmentDurabilityVisualTests.Run);
        RunSuite("survival-progression", SurvivalProgressionTests.Run);
        RunSuite("farming", FarmingTests.Run);
        RunSuite("mobs", () => MobContentTests.Run(this));
        RunSuite("achievements", AchievementTests.Run);
        RunSuite("dungeon", () => DungeonTests.Run(this));
        RunSuite("structure-weathering", () => WeatheringTests.Run(this));
        RunSuite("villages-quests", VillageTests.Run);
        RunSuite("fishing", FishingTests.Run);
        RunSuite("keybinds", KeybindTests.Run);
        RunSuite("first-person-animation", FirstPersonAnimationTests.Run);
        RunSuite("onboarding", OnboardingTests.Run);
        RunSuite("recipes", RecipeTests.Run);
        RunSuite("stats", StatsTests.Run);
        RunSuite("fluids", FluidTests.Run);
        RunSuite("fluid-world", () => FluidWorldTests.Run(this));
        RunSuite("boats-buckets", () => BoatBucketTests.Run(this));
        RunSuite("save", SaveTests.Run);
        RunSuite("integration", () => IntegrationTests.Run(this));
        RunSuite("world-archives", WorldArchiveTests.Run);
        RunSuite("worldgen", () => WorldgenTests.Run(this));
        await RunSuiteAsync("mob-perception", () => MobPerceptionTests.Run(this));

        // Tests queue-free their temporary worlds, mobs and physics bodies.
        // Give the tree two frames so deferred deletion runs before shutdown;
        // otherwise Godot reports those still-queued nodes as leaked objects.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        GD.Print($"minecraft tests: {TestAssert.Checks} checks, {TestAssert.Failures} failures");
        GetTree().Quit(TestAssert.Failures == 0 ? 0 : 1);
    }

    private static async Task RunSuiteAsync(string name, Func<Task> action)
    {
        int before = TestAssert.Failures;
        try
        {
            await action();
        }
        catch (Exception e)
        {
            TestAssert.Failures++;
            GD.PushError($"FAIL {name}: unexpected exception {e}");
        }

        GD.Print(TestAssert.Failures == before ? $"PASS {name}" : $"FAIL {name}");
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

    /// <summary>Release synchronously created node fixtures at the end of a suite.</summary>
    public static void FreeAll(params GodotObject[] objects)
    {
        foreach (GodotObject value in objects)
        {
            if (GodotObject.IsInstanceValid(value)) value.Free();
        }
    }
}
