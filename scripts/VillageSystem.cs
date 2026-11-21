using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

public partial class VillageSystem : Node
{
    private const float SpawnRadius = 82.0f;
    private const float DespawnRadius = 118.0f;
    private VoxelWorld? _world;
    private Player? _player;
    private Mobs? _mobs;
    private readonly Dictionary<string, Villager> _active = [];
    private double _scanTimer;
    private double _saveTimer;

    public int ActiveResidents => _active.Count;

    public void Setup(VoxelWorld world, Player player, Mobs mobs)
    {
        if (_world != null) _world.BeforeSave -= SyncResidents;
        _world = world;
        _world.BeforeSave += SyncResidents;
        _player = player;
        _mobs = mobs;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null || _mobs == null) return;
        _scanTimer -= delta;
        if (_scanTimer <= 0.0)
        {
            _scanTimer = 1.25;
            foreach (VillageInfo village in _world.NearbyVillages(_player.GlobalPosition, SpawnRadius))
                EnsureResidents(village);
        }

        _saveTimer += delta;
        if (_saveTimer >= 4.0)
        {
            _saveTimer = 0.0;
            SyncResidents();
        }

        Vector3 playerPosition = _player.GlobalPosition;
        List<string> remove = [];
        foreach ((string id, Villager villager) in _active)
        {
            if (!GodotObject.IsInstanceValid(villager))
            {
                remove.Add(id);
                continue;
            }
            if (villager.GlobalPosition.DistanceSquaredTo(playerPosition) > DespawnRadius * DespawnRadius)
            {
                SaveResident(villager);
                villager.QueueFree();
                remove.Add(id);
            }
        }
        foreach (string id in remove) _active.Remove(id);
    }

    private void EnsureResidents(VillageInfo village)
    {
        if (_world == null || _mobs == null) return;
        foreach (VillagerSpawnInfo spawn in VillageGenerator.Roster(village))
        {
            if (_active.ContainsKey(spawn.Id)) continue;
            VillagerSave? saved = _world.Villagers.GetValueOrDefault(spawn.Id);
            if (saved != null && saved.Health <= 0.0f) continue;
            VillagerSpawnInfo restored = RestoreSpawn(spawn, saved);
            Vector2I chunk = new(Mathf.FloorToInt(restored.Home.X / VoxelWorld.ChunkX),
                    Mathf.FloorToInt(restored.Home.Z / VoxelWorld.ChunkZ));
            if (!_world.HasChunk(chunk)) continue;

            Villager villager = new(restored, spawn.Id, saved?.TradesCompleted ?? 0);
            AddChild(villager);
            villager.Setup(_world, _mobs, restored.Home);
            villager.GlobalPosition = saved?.Position is { Length: 3 } position
                    ? new Vector3(position[0], position[1], position[2]) : restored.Home;
            villager.Health = Mathf.Clamp(saved?.Health ?? 20.0f, 0.0f, villager.MaxHealth);
            villager.Died += OnResidentDied;
            _active.Add(spawn.Id, villager);
            _world.Villagers[spawn.Id] = villager.ToSave();
            _world.MarkDirty();
        }
    }

    private static VillagerSpawnInfo RestoreSpawn(VillagerSpawnInfo spawn, VillagerSave? saved)
    {
        if (saved == null) return spawn;
        Vector3 home = saved.Home is { Length: 3 }
                ? new Vector3(saved.Home[0], saved.Home[1], saved.Home[2]) : spawn.Home;
        Vector3 work = saved.Work is { Length: 3 }
                ? new Vector3(saved.Work[0], saved.Work[1], saved.Work[2]) : spawn.Work;
        VillagerProfession profession = Enum.IsDefined(typeof(VillagerProfession), saved.Profession)
                ? (VillagerProfession)saved.Profession : spawn.Profession;
        return spawn with
        {
            Name = string.IsNullOrWhiteSpace(saved.Name) ? spawn.Name : saved.Name,
            Profession = profession,
            Home = home,
            Work = work,
        };
    }

    private void OnResidentDied(Mob mob)
    {
        if (mob is Villager villager)
        {
            SaveResident(villager);
            _world?.MarkDirty();
        }
    }

    private void SyncResidents()
    {
        foreach (Villager villager in _active.Values)
            if (GodotObject.IsInstanceValid(villager)) SaveResident(villager);
    }

    private void SaveResident(Villager villager)
    {
        if (_world == null) return;
        VillagerSave next = villager.ToSave();
        bool changed = !_world.Villagers.TryGetValue(villager.ResidentId, out VillagerSave? old)
                || old.Health != next.Health || old.TradesCompleted != next.TradesCompleted
                || !Near(old.Position, next.Position, 0.7f);
        _world.Villagers[villager.ResidentId] = next;
        if (changed) _world.MarkDirty();
    }

    private static bool Near(float[]? a, float[] b, float distance)
    {
        if (a is not { Length: 3 }) return false;
        float dx = a[0] - b[0];
        float dy = a[1] - b[1];
        float dz = a[2] - b[2];
        return dx * dx + dy * dy + dz * dz <= distance * distance;
    }

    public override void _ExitTree()
    {
        if (_world == null || !GodotObject.IsInstanceValid(_world)) return;
        _world.BeforeSave -= SyncResidents;
        SyncResidents();
    }
}
