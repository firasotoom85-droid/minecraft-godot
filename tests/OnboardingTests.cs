using System.Collections.Generic;

namespace Minecraft;

public static class OnboardingTests
{
    public static void Run()
    {
        CheckStepCountAndCoreActions();
        CheckLegacyProgressRestoration();
        CheckMiningAndCraftingRequireRealActions();
        CheckFoodAndShelterAreTransactionalActions();
        CheckCaveVillageAndNightProgression();
    }

    private static void CheckStepCountAndCoreActions()
    {
        TestAssert.Equal(15, OnboardingSystem.StepCount,
                "first-hour guide exposes all contextual steps");

        OnboardingProgress progress = new();
        TestAssert.Check(!progress.IsComplete(OnboardingStep.Move),
                "movement does not complete before the player moves");
        progress.Moved = true;
        TestAssert.Check(progress.IsComplete(OnboardingStep.Move),
                "movement step completes after movement is observed");

        TestAssert.Check(progress.MarkInventoryOpened(),
                "only the inventory's successful open event records onboarding progress");
        TestAssert.Check(progress.IsComplete(OnboardingStep.OpenInventory),
                "the inventory step completes after the UI opens");
        TestAssert.Check(!progress.MarkInventoryOpened(),
                "reopening the inventory does not repeat an already recorded onboarding fact");
    }

    private static void CheckLegacyProgressRestoration()
    {
        Dictionary<string, bool> legacyFlags = new()
        {
            ["onboard/wood"] = true,
            ["onboard/table"] = true,
            ["onboard/pick"] = true,
            ["onboard/stone"] = true,
            ["onboard/torch"] = true,
        };
        OnboardingProgress legacy = OnboardingProgress.FromFlags(legacyFlags);
        TestAssert.Check(legacy.WoodBroken && legacy.CraftingTableCrafted
                && legacy.WoodenPickCrafted && legacy.StoneBroken && legacy.TorchPlaced,
                "legacy First Night action flags restore into matching first-hour facts");
        TestAssert.Check(!legacy.InventoryOpened,
                "a legacy world does not falsely claim it opened the inventory");

        Dictionary<string, bool> currentFlags = new()
        {
            ["onboard/fact/inventory"] = true,
            ["onboard/fact/food"] = true,
            ["onboard/fact/shelter_0"] = true,
            ["onboard/fact/shelter_1"] = true,
            ["onboard/fact/slept"] = true,
            ["onboard/night"] = true,
        };
        OnboardingProgress current = OnboardingProgress.FromFlags(currentFlags);
        TestAssert.Check(current.InventoryOpened && current.FoodGathered,
                "new per-world facts restore independently");
        TestAssert.Equal(2, current.SolidBlocksPlaced,
                "partial shelter construction survives save and reload");
        TestAssert.Check(current.Slept && current.IsComplete(OnboardingStep.SurviveNight),
                "sleep and legacy night completion survive save and reload");
    }

    private static void CheckMiningAndCraftingRequireRealActions()
    {
        OnboardingProgress progress = new();
        TestAssert.Check(!progress.IsComplete(OnboardingStep.GatherWood),
                "starter inventory does not complete the wood-gathering step");
        progress.RecordBlockBroken(BlockId.Log);
        TestAssert.Check(progress.IsComplete(OnboardingStep.GatherWood),
                "breaking an oak log completes the wood step");

        progress.RecordBlockBroken(BlockId.BirchLog);
        TestAssert.Check(progress.IsComplete(OnboardingStep.GatherWood),
                "breaking a birch log also counts as gathering wood");

        TestAssert.Check(!progress.IsComplete(OnboardingStep.CraftTable),
                "owning a crafting table is not treated as crafting one");
        progress.RecordItemCrafted(ItemId.CraftingTable);
        TestAssert.Check(progress.IsComplete(OnboardingStep.CraftTable),
                "craft event completes the crafting table step");

        progress.RecordItemCrafted(ItemId.WoodPick);
        TestAssert.Check(progress.IsComplete(OnboardingStep.CraftPickaxe),
                "craft event completes the wooden pickaxe step");
        TestAssert.Check(!progress.IsComplete(OnboardingStep.MineStone),
                "crafting a pickaxe does not count as mining stone");
        progress.RecordBlockBroken(BlockId.Stone);
        TestAssert.Check(progress.IsComplete(OnboardingStep.MineStone),
                "breaking stone completes the mining step");
    }

    private static void CheckFoodAndShelterAreTransactionalActions()
    {
        OnboardingProgress progress = new();
        progress.RecordItemGathered(ItemId.Apple);
        TestAssert.Check(progress.IsComplete(OnboardingStep.GatherFood),
                "gathering a food item completes the food step");
        TestAssert.Check(!progress.IsComplete(OnboardingStep.EatFood),
                "gathering food does not count as eating it");
        progress.FoodEaten = true;
        TestAssert.Check(progress.IsComplete(OnboardingStep.EatFood),
                "the ate signal completes the eating step");

        progress.RecordBlockPlaced(BlockId.Torch);
        TestAssert.Check(progress.IsComplete(OnboardingStep.PlaceTorch),
                "placing a torch completes the lighting step");
        TestAssert.Equal(0, progress.SolidBlocksPlaced,
                "torch placement does not count toward shelter construction");
        progress.RecordBlockPlaced(BlockId.Farmland);
        TestAssert.Equal(0, progress.SolidBlocksPlaced,
                "tilling farmland does not count toward shelter construction");

        for (int i = 0; i < OnboardingProgress.ShelterBlockGoal - 1; i++)
            progress.RecordBlockPlaced(BlockId.Cobblestone);
        TestAssert.Check(!progress.IsComplete(OnboardingStep.BuildShelter),
                "shelter construction requires the complete block target");
        progress.RecordBlockPlaced(BlockId.Planks);
        TestAssert.Check(progress.IsComplete(OnboardingStep.BuildShelter),
                "placing eight solid blocks completes the shelter step");
    }

    private static void CheckCaveVillageAndNightProgression()
    {
        OnboardingProgress progress = new();
        TestAssert.Check(!progress.IsComplete(OnboardingStep.DefeatHostile),
                "hostile encounter step starts incomplete");
        progress.RecordHostileDefeated(wasPassive: true);
        TestAssert.Check(!progress.IsComplete(OnboardingStep.DefeatHostile),
                "passive creature defeat does not count as hostile combat");
        progress.RecordHostileDefeated(wasPassive: false);
        TestAssert.Check(progress.IsComplete(OnboardingStep.DefeatHostile),
                "hostile creature defeat completes combat step");

        progress.CaveExplored = true;
        TestAssert.Check(progress.IsComplete(OnboardingStep.ExploreCave),
                "natural cave discovery completes exploration step");
        progress.VillageVisited = true;
        TestAssert.Check(progress.IsComplete(OnboardingStep.VisitVillage),
                "resident interaction completes the village step");

        TestAssert.Check(!progress.IsComplete(OnboardingStep.SurviveNight),
                "night step is not completed before sleeping or reaching dawn");
        progress.SawNight = true;
        TestAssert.Check(!progress.IsComplete(OnboardingStep.SurviveNight),
                "being present at night is not sufficient");
        progress.SurvivedNight = true;
        TestAssert.Check(progress.IsComplete(OnboardingStep.SurviveNight),
                "reaching dawn after night completes the survival step");
    }
}
