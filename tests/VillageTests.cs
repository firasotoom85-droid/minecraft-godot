using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Minecraft;

public static class VillageTests
{
    public static void Run()
    {
        CheckBiomeThemes();
        CheckResidentAndTraderLifecycleRules();
        CheckDeterministicVillageStampAndChunkSeams();
        CheckIrrigatedFarmStaysContained();
        CheckRosterAndSchedules();
        CheckGoldTrades();
        CheckQuestStages();
        CheckQuestStageFeedback();
        CheckVillageQuestSaveRoundTrip();
        CheckMerchantQuestReconciliation();
        CheckVillageRemovalScratch();
    }

    private static void CheckResidentAndTraderLifecycleRules()
    {
        TestAssert.Check(VillagerLifecycleRules.CanInteract(false, false),
                "living village residents remain interactable");
        TestAssert.Check(!VillagerLifecycleRules.CanInteract(true, false)
                        && !VillagerLifecycleRules.CanInteract(false, true)
                        && !VillagerLifecycleRules.CanInteract(true, true),
                "dead or exiting village residents cannot open trading");
        TestAssert.Check(TraderLifecycleRules.CanSpawn(false, false),
                "wandering trader can spawn when no tracked instance exists");
        TestAssert.Check(!TraderLifecycleRules.CanSpawn(true, false),
                "wandering trader cannot duplicate an existing live tracked instance");
        TestAssert.Check(TraderLifecycleRules.CanSpawn(true, true),
                "queued-for-deletion trader does not permanently block a later visit");
    }

    private static void CheckBiomeThemes()
    {
        TestAssert.Equal<VillageTheme?>(VillageTheme.Plains, VillageGenerator.ThemeForBiome(Biome.Plains), "plains use plains theme");
        TestAssert.Equal<VillageTheme?>(VillageTheme.Plains, VillageGenerator.ThemeForBiome(Biome.FlowerPlains), "flower plains use plains theme");
        TestAssert.Equal<VillageTheme?>(VillageTheme.Forest, VillageGenerator.ThemeForBiome(Biome.Birch), "birch forest uses forest theme");
        TestAssert.Equal<VillageTheme?>(VillageTheme.Forest, VillageGenerator.ThemeForBiome(Biome.DenseForest), "dense forest uses forest theme");
        TestAssert.Equal<VillageTheme?>(VillageTheme.Snow, VillageGenerator.ThemeForBiome(Biome.Snow), "snow uses snow theme");
        TestAssert.Equal<VillageTheme?>(VillageTheme.Snow, VillageGenerator.ThemeForBiome(Biome.Taiga), "taiga uses snow theme");
        TestAssert.Equal<VillageTheme?>(VillageTheme.Desert, VillageGenerator.ThemeForBiome(Biome.Desert), "desert uses desert theme");
        TestAssert.Check(VillageGenerator.ThemeForBiome(Biome.Ocean) == null, "ocean does not host villages");
        TestAssert.Check(VillageGenerator.ThemeForBiome(Biome.Mountain) == null, "mountains do not host villages");
    }

    private static void CheckDeterministicVillageStampAndChunkSeams()
    {
        VillageInfo site = new(VillageTheme.Plains, 14, 60, 14, 0, 0, 0xA17E5u);
        Dictionary<Vector3I, BlockId> whole = [];
        VillageStructures.Stamp(site, (x, y, z, id) => whole[new Vector3I(x, y, z)] = id);
        TestAssert.Check(whole.Count > 1000, "village stamp emits a substantial settlement");
        TestAssert.Check(whole.Values.Contains(BlockId.Bed), "village includes beds");
        TestAssert.Check(whole.Values.Contains(BlockId.Chest), "village includes storage");
        TestAssert.Check(whole.Values.Contains(BlockId.Furnace), "village includes furnaces");
        TestAssert.Check(whole.Values.Contains(BlockId.CraftingTable), "village includes crafting stations");
        TestAssert.Check(whole.Values.Contains(BlockId.Torch), "village includes lighting");
        TestAssert.Check(whole.Values.Contains(BlockId.Farmland), "village includes farmland");
        TestAssert.Check(whole.Values.Contains(BlockId.Wheat0), "village includes planted crops");

        Dictionary<Vector3I, BlockId> clipped = [];
        for (int cz = -1; cz <= 2; cz++)
            for (int cx = -1; cx <= 2; cx++)
            {
                int ox = cx * VoxelWorld.ChunkX;
                int oz = cz * VoxelWorld.ChunkZ;
                VillageStructures.Stamp(site, (x, y, z, id) =>
                {
                    if (x >= ox && x < ox + VoxelWorld.ChunkX
                            && z >= oz && z < oz + VoxelWorld.ChunkZ)
                        clipped[new Vector3I(x, y, z)] = id;
                });
            }

        TestAssert.Equal(whole.Count, clipped.Count, "chunk-clipped village has the same block count");
        foreach ((Vector3I position, BlockId id) in whole)
            TestAssert.Check(clipped.TryGetValue(position, out BlockId clippedId) && clippedId == id,
                    $"village seam preserves block at {position}");
        Dictionary<Vector3I, BlockId> repeated = [];
        VillageStructures.Stamp(site, (x, y, z, id) => repeated[new Vector3I(x, y, z)] = id);
        TestAssert.Check(whole.OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.Z)
                .SequenceEqual(repeated.OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.Z)),
                "village block output is deterministic");
    }

    private static void CheckIrrigatedFarmStaysContained()
    {
        VillageInfo site = new(VillageTheme.Plains, 32, 64, 32, 0, 0, 0xFA44u);
        Dictionary<Vector3I, BlockId> blocks = [];
        VillageStructures.Stamp(site, (x, y, z, id) => blocks[new Vector3I(x, y, z)] = id);

        // Farm centre is a water channel level with the farmland; the crop row
        // above it must stay clear and the outer dirt rim must hold the water.
        int farmX = site.X + 17;
        int farmZ = site.Z + 1;
        TestAssert.Equal(BlockId.Water, blocks[new Vector3I(farmX, site.Y, farmZ)],
                "farm channel is a source at farmland height");
        TestAssert.Check(!blocks.ContainsKey(new Vector3I(farmX, site.Y + 1, farmZ)),
                "no water sits above the farm channel");
        TestAssert.Equal(BlockId.Dirt, blocks[new Vector3I(farmX + 4, site.Y, farmZ)],
                "farm rim is solid dirt beside the channel");
        BlockId crop = blocks.GetValueOrDefault(new Vector3I(farmX + 1, site.Y + 1, farmZ + 1));
        TestAssert.Check(crop is BlockId.Wheat0 or BlockId.Wheat1,
                "crops occupy the farmland row");
    }

    private static void CheckRosterAndSchedules()
    {
        VillageInfo site = new(VillageTheme.Forest, -420, 72, 128, -2, 0, 654321u);
        VillagerSpawnInfo[] first = VillageGenerator.Roster(site);
        VillagerSpawnInfo[] second = VillageGenerator.Roster(site);
        TestAssert.Equal(5, first.Length, "village has five residents");
        for (int i = 0; i < first.Length; i++)
        {
            TestAssert.Equal(first[i].Id, second[i].Id, "resident ID is deterministic");
            TestAssert.Equal(first[i].Name, second[i].Name, "resident name is deterministic");
            TestAssert.Equal(first[i].Profession, second[i].Profession, "resident profession is deterministic");
        }
        TestAssert.Equal(VillagerSchedule.Activity.Home, VillagerSchedule.ActivityAt(0.1), "night schedule returns home");
        TestAssert.Equal(VillagerSchedule.Activity.Work, VillagerSchedule.ActivityAt(0.4), "morning schedule goes to work");
        TestAssert.Equal(VillagerSchedule.Activity.Gathering, VillagerSchedule.ActivityAt(0.62), "afternoon schedule gathers");
        TestAssert.Equal(VillagerSchedule.Activity.Home, VillagerSchedule.ActivityAt(0.8), "evening schedule returns home");

        Villager detached = new(first[0], first[0].Id);
        detached.Position = new Vector3(3.25f, 74.5f, -8.75f);
        VillagerSave snapshot = detached.ToSave();
        Vector3 savedPosition = new(snapshot.Position![0], snapshot.Position[1], snapshot.Position[2]);
        TestAssert.Equal(new Vector3(3.25f, 74.5f, -8.75f), savedPosition,
                "resident snapshot uses a valid local transform when saved off-tree");
        detached.Free();
    }

    private static void CheckGoldTrades()
    {
        foreach (VillagerProfession profession in System.Enum.GetValues<VillagerProfession>())
        {
            IReadOnlyList<VillageTrade> offers = VillageTradeCatalog.For(profession);
            TestAssert.Check(offers.Count >= 2, $"{profession} has profession-specific trades");
            foreach (VillageTrade offer in offers)
                TestAssert.Check(offer.Give == ItemId.GoldIngot || offer.Get == ItemId.GoldIngot,
                        $"{profession} offers use the existing gold economy");
        }
    }

    private static void CheckQuestStages()
    {
        QuestDefinition quest = QuestCatalog.Find("village/farmers-harvest")!;
        QuestProgressSave state = new(0, 0);
        state = QuestProgression.Apply(state, quest, QuestObjectiveKind.Gather, nameof(ItemId.Wheat), 7);
        TestAssert.Equal(0, state.Stage, "quest stays on stage one while incomplete");
        TestAssert.Equal(7, state.Progress, "quest stores partial objective progress");
        state = QuestProgression.Apply(state, quest, QuestObjectiveKind.Gather, nameof(ItemId.Wheat), 1);
        TestAssert.Equal(1, state.Stage, "quest advances after its first objective");
        TestAssert.Check(!state.Completed, "multi-stage quest remains active");
        state = QuestProgression.Apply(state, quest, QuestObjectiveKind.Craft, nameof(ItemId.Bread), 2);
        TestAssert.Check(state.Completed, "second objective completes quest");
        QuestProgressSave wrong = QuestProgression.Apply(new QuestProgressSave(0, 0), quest,
                QuestObjectiveKind.Craft, nameof(ItemId.WoodPick), 1);
        TestAssert.Equal(0, wrong.Progress, "unrelated events do not advance quests");
    }

    private static void CheckQuestStageFeedback()
    {
        VoxelWorld world = new();
        Player player = new();
        Mobs mobs = new();
        QuestSystem quests = new();
        quests.Setup(world, player, mobs);

        QuestDefinition definition = QuestCatalog.Find("village/farmers-harvest")!;
        TestAssert.Check(quests.Accept(definition.Id), "quest can be accepted for stage feedback test");
        int stageEvents = 0;
        int lastStage = -1;
        bool completed = false;
        quests.StageCompleted += (quest, progress) =>
        {
            stageEvents++;
            lastStage = progress.Stage;
            completed = progress.Completed;
        };

        quests.Report(QuestObjectiveKind.Gather, nameof(ItemId.Wheat), 7);
        TestAssert.Equal(0, stageEvents, "partial objective progress does not emit a stage-complete event");
        quests.Report(QuestObjectiveKind.Gather, nameof(ItemId.Wheat), 1);
        TestAssert.Equal(1, stageEvents, "finishing an objective emits stage feedback");
        TestAssert.Equal(1, lastStage, "stage feedback reports the next stage index");
        TestAssert.Check(!completed, "intermediate stage feedback does not report quest completion");

        quests.Report(QuestObjectiveKind.Craft, nameof(ItemId.Bread), 2);
        TestAssert.Equal(2, stageEvents, "finishing the final objective emits completion feedback");
        TestAssert.Equal(2, lastStage, "final stage feedback reports the terminal stage index");
        TestAssert.Check(completed, "final stage feedback marks the quest completed");

        TestAssert.FreeAll(quests, mobs, player, world);
    }

    private static void CheckVillageQuestSaveRoundTrip()
    {
        VillagerSave resident = new("village:1:2/resident-0", "Alden Ashford",
                (int)VillagerProfession.Farmer,
                [1.5f, 42.0f, 2.5f], [1.5f, 42.0f, 2.5f], [17.5f, 42.0f, 3.5f],
                18.0f, 4);
        WorldSave save = new(WorldStore.WorldFormatVersion, null, 0.5,
                Villagers: new Dictionary<string, VillagerSave> { [resident.Id] = resident },
                Quests: new Dictionary<string, QuestProgressSave>
                {
                    ["village/farmers-harvest"] = new QuestProgressSave(1, 0),
                });
        string json = JsonSerializer.Serialize(save, WorldStore.Json);
        WorldSave? loaded = JsonSerializer.Deserialize<WorldSave>(json, WorldStore.Json);
        TestAssert.Equal(resident.Name, loaded?.Villagers?[resident.Id].Name, "resident name survives save/load");
        TestAssert.Equal(4, loaded?.Villagers?[resident.Id].TradesCompleted ?? -1, "resident trades survive save/load");
        TestAssert.Equal(1, loaded?.Quests?["village/farmers-harvest"].Stage ?? -1, "quest stage survives save/load");
        TestAssert.Check(loaded?.Villagers?.ContainsKey(resident.Id) == true, "resident remains in world data");
        WorldSave? old = JsonSerializer.Deserialize<WorldSave>(
                "{\"version\":3,\"edits\":{},\"time\":0.5}", WorldStore.Json);
        TestAssert.Check(old?.Villagers == null && old?.Quests == null,
                "V3 saves load without village/quest fields");
    }

    private static void CheckMerchantQuestReconciliation()
    {
        const string questId = "village/merchant-routes";
        VoxelWorld world = new();
        Player player = new();
        Mobs mobs = new();
        QuestSystem quests = new();
        world.Flags["explored/Plains"] = true;
        world.Flags["explored/Desert"] = true;
        quests.Setup(world, player, mobs);

        TestAssert.Equal(2, QuestProgression.CountExploredBiomes(world.Flags),
                "merchant reconciliation counts prior unique biome flags");
        TestAssert.Check(quests.Accept(questId), "merchant quest accepts after prior exploration");
        QuestProgressSave? progress = quests.ProgressOf(questId);
        TestAssert.Equal(0, progress?.Stage ?? -1,
                "two pre-accepted discoveries keep the merchant on the exploration objective");
        TestAssert.Equal(2, progress?.Progress ?? -1,
                "merchant quest starts with its two previously explored biomes counted");

        quests.DiscoverBiomeForTests(Biome.Plains);
        TestAssert.Equal(2, quests.ProgressOf(questId)?.Progress ?? -1,
                "revisiting a biome does not duplicate objective progress");
        quests.DiscoverBiomeForTests(Biome.Snow);
        TestAssert.Equal(1, quests.ProgressOf(questId)?.Stage ?? -1,
                "one new biome completes the reconciled three-biome objective");

        VoxelWorld oneBiomeWorld = new();
        Player oneBiomePlayer = new();
        Mobs oneBiomeMobs = new();
        QuestSystem oneBiomeQuests = new();
        oneBiomeWorld.Flags["explored/Birch"] = true;
        oneBiomeQuests.Setup(oneBiomeWorld, oneBiomePlayer, oneBiomeMobs);
        TestAssert.Check(oneBiomeQuests.Accept(questId),
                "merchant quest accepts with one previously explored biome");
        TestAssert.Equal(1, oneBiomeQuests.ProgressOf(questId)?.Progress ?? -1,
                "one prior unique biome is reconciled into merchant progress");

        VoxelWorld exploredWorld = new();
        Player exploredPlayer = new();
        Mobs exploredMobs = new();
        QuestSystem exploredQuests = new();
        exploredWorld.Flags["explored/Plains"] = true;
        exploredWorld.Flags["explored/Desert"] = true;
        exploredWorld.Flags["explored/Snow"] = true;
        exploredQuests.Setup(exploredWorld, exploredPlayer, exploredMobs);
        TestAssert.Check(exploredQuests.Accept(questId),
                "merchant quest accepts when all required biomes were explored beforehand");
        TestAssert.Equal(1, exploredQuests.ProgressOf(questId)?.Stage ?? -1,
                "all prior discoveries advance the accepted merchant quest immediately");
        TestAssert.Equal(0, exploredQuests.ProgressOf(questId)?.Progress ?? -1,
                "completed exploration stage starts the next objective with zero progress");

        TestAssert.FreeAll(quests, mobs, player, world,
                oneBiomeQuests, oneBiomeMobs, oneBiomePlayer, oneBiomeWorld,
                exploredQuests, exploredMobs, exploredPlayer, exploredWorld);
    }

    private static void CheckVillageRemovalScratch()
    {
        VillageSystem system = new();
        List<string> scratch = system.RemovalScratchForTests;
        for (int i = 0; i < 16; i++)
            system.RunRemovalPassForTests(Vector3.Zero);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
            system.RunRemovalPassForTests(Vector3.Zero);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        TestAssert.Check(ReferenceEquals(scratch, system.RemovalScratchForTests),
                "resident removal scan retains the same scratch list between frames");
        TestAssert.Check(allocated <= 64,
                $"resident scan with no removals avoids per-frame managed allocations (allocated {allocated} bytes)");
        TestAssert.Equal(0, scratch.Count, "empty resident scans leave no deferred removals");
        TestAssert.FreeAll(system);
    }

}
