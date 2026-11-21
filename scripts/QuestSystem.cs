using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

public enum QuestObjectiveKind { ExploreBiome, Gather, Craft, Trade, Kill, DungeonClear }
public sealed record QuestObjective(QuestObjectiveKind Kind, string Target, int Count);
public sealed record QuestStage(string Description, QuestObjective Objective);
public sealed record QuestDefinition(
        string Id, string Title, VillagerProfession Giver, QuestStage[] Stages,
        ItemId Reward, int RewardCount, int Experience);

public static class QuestCatalog
{
    public static readonly IReadOnlyList<QuestDefinition> All =
    [
        new("village/farmers-harvest", "A Better Harvest", VillagerProfession.Farmer,
        [
            new("Bring the farmer 8 wheat", new(QuestObjectiveKind.Gather, nameof(ItemId.Wheat), 8)),
            new("Bake 2 bread", new(QuestObjectiveKind.Craft, nameof(ItemId.Bread), 2)),
        ], ItemId.GoldIngot, 2, 20),
        new("village/merchant-routes", "Roads Beyond the Village", VillagerProfession.Merchant,
        [
            new("Discover 3 different biomes", new(QuestObjectiveKind.ExploreBiome, "any", 3)),
            new("Complete 2 trades with any villager", new(QuestObjectiveKind.Trade, "any", 2)),
        ], ItemId.GoldIngot, 3, 30),
        new("village/blacksmith-blade", "A Smith's Request", VillagerProfession.Blacksmith,
        [
            new("Gather 4 raw iron", new(QuestObjectiveKind.Gather, nameof(ItemId.RawIron), 4)),
            new("Craft an iron sword", new(QuestObjectiveKind.Craft, nameof(ItemId.IronSword), 1)),
        ], ItemId.GoldIngot, 4, 35),
        new("village/librarian-lore", "Fragments of Knowledge", VillagerProfession.Librarian,
        [
            new("Gather 2 lapis lazuli", new(QuestObjectiveKind.Gather, nameof(ItemId.LapisLazuli), 2)),
            new("Craft an enchanting table", new(QuestObjectiveKind.Craft, nameof(ItemId.EnchantingTable), 1)),
        ], ItemId.GoldIngot, 5, 45),
        new("village/guard-watch", "Keep the Roads Safe", VillagerProfession.Guard,
        [
            new("Defeat 3 Shades", new(QuestObjectiveKind.Kill, nameof(Shade), 3)),
            new("Defeat the Ember Warden", new(QuestObjectiveKind.DungeonClear, "ember", 1)),
        ], ItemId.GoldIngot, 8, 60),
    ];

    public static QuestDefinition? Find(string id)
    {
        foreach (QuestDefinition quest in All)
            if (quest.Id == id) return quest;
        return null;
    }
}

public static class QuestProgression
{
    public static QuestProgressSave Apply(QuestProgressSave progress, QuestDefinition definition,
            QuestObjectiveKind kind, string target, int amount = 1)
    {
        if (progress.Completed || progress.Claimed || amount <= 0
                || progress.Stage < 0 || progress.Stage >= definition.Stages.Length) return progress;
        QuestObjective objective = definition.Stages[progress.Stage].Objective;
        if (objective.Kind != kind || (objective.Target != "any"
                && !string.Equals(objective.Target, target, StringComparison.OrdinalIgnoreCase))) return progress;
        int nextProgress = Math.Min(objective.Count, progress.Progress + amount);
        if (nextProgress < objective.Count) return progress with { Progress = nextProgress };
        int nextStage = progress.Stage + 1;
        return new QuestProgressSave(nextStage, 0, nextStage >= definition.Stages.Length, false);
    }
}

public partial class QuestSystem : Node
{
    private VoxelWorld? _world;
    private Player? _player;
    private double _exploreTimer;

    public event Action? ProgressChanged;

    public void Setup(VoxelWorld world, Player player, Mobs mobs)
    {
        _world = world;
        _player = player;
        player.ItemGathered += (id, count) => Report(QuestObjectiveKind.Gather, id.ToString(), count);
        player.ItemCrafted += (id, count) => Report(QuestObjectiveKind.Craft, id.ToString(), count);
        player.TradeCompleted += count => Report(QuestObjectiveKind.Trade, "any", count);
        mobs.MobDefeated += mob => Report(QuestObjectiveKind.Kill, mob.GetType().Name, 1);
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null) return;
        _exploreTimer -= delta;
        if (_exploreTimer > 0.0) return;
        _exploreTimer = 1.5;
        Biome biome = _world.BiomeAt(Mathf.FloorToInt(_player.GlobalPosition.X),
                Mathf.FloorToInt(_player.GlobalPosition.Z));
        string flag = $"explored/{biome}";
        if (_world.Flags.GetValueOrDefault(flag)) return;
        _world.Flags[flag] = true;
        _world.MarkDirty();
        Report(QuestObjectiveKind.ExploreBiome, biome.ToString(), 1);
        Report(QuestObjectiveKind.ExploreBiome, "any", 1);
    }

    public IReadOnlyList<QuestDefinition> AvailableFor(VillagerProfession profession)
    {
        List<QuestDefinition> result = [];
        foreach (QuestDefinition quest in QuestCatalog.All)
            if (quest.Giver == profession) result.Add(quest);
        return result;
    }

    public QuestProgressSave? ProgressOf(string questId) => _world?.Quests.GetValueOrDefault(questId);

    public string StatusOf(QuestDefinition definition)
    {
        QuestProgressSave? state = ProgressOf(definition.Id);
        if (state == null) return "Not accepted";
        if (state.Claimed) return "Reward claimed";
        if (state.Completed) return "Objective complete — claim your reward";
        if (state.Stage < 0 || state.Stage >= definition.Stages.Length) return "Quest data needs recovery";
        QuestObjective objective = definition.Stages[state.Stage].Objective;
        return $"Stage {state.Stage + 1}/{definition.Stages.Length}: "
                + $"{definition.Stages[state.Stage].Description} ({state.Progress}/{objective.Count})";
    }

    public bool Accept(string questId)
    {
        if (_world == null || QuestCatalog.Find(questId) is not { } definition
                || _world.Quests.ContainsKey(questId)) return false;
        _world.Quests[questId] = new QuestProgressSave(0, 0);
        _world.MarkDirty();
        ProgressChanged?.Invoke();
        return true;
    }

    public bool Claim(string questId)
    {
        if (_world == null || _player == null || QuestCatalog.Find(questId) is not { } definition
                || !_world.Quests.TryGetValue(questId, out QuestProgressSave? state)
                || !state.Completed || state.Claimed
                || !_player.Inventory.CanFit(definition.Reward, definition.RewardCount)) return false;
        _player.Inventory.Add(definition.Reward, definition.RewardCount);
        _player.Stats.AddExperience(definition.Experience);
        _player.Inventory.RaiseChanged();
        _world.Quests[questId] = state with { Claimed = true };
        _world.MarkDirty();
        _player.EmitSignal(Player.SignalName.Notice,
                $"Quest reward: {definition.RewardCount} {ItemDb.NameOf(definition.Reward)} and {definition.Experience} XP");
        ProgressChanged?.Invoke();
        return true;
    }

    public void ReportDungeonClear() => Report(QuestObjectiveKind.DungeonClear, "ember", 1);

    public void Report(QuestObjectiveKind kind, string target, int amount = 1)
    {
        if (_world == null || amount <= 0) return;
        bool changed = false;
        foreach (QuestDefinition definition in QuestCatalog.All)
        {
            if (!_world.Quests.TryGetValue(definition.Id, out QuestProgressSave? state)
                    || state.Completed || state.Claimed) continue;
            QuestProgressSave next = QuestProgression.Apply(state, definition, kind, target, amount);
            if (next != state)
            {
                _world.Quests[definition.Id] = next;
                changed = true;
            }
        }
        if (changed)
        {
            _world.MarkDirty();
            ProgressChanged?.Invoke();
        }
    }
}
