using Godot;

namespace Minecraft;

/// <summary>
/// Opt-in integration soak: runs the real main scene, feeds live herds, and
/// alternates surface/cave and day/night conditions. Default duration is one hour.
/// </summary>
public partial class SurvivalSoakRunner : Node
{
    private Main _main = null!;
    private VoxelWorld _world = null!;
    private Player _player = null!;
    private Mobs _mobs = null!;
    private Vector3 _surfacePosition;
    private Vector3 _cavePosition;
    private bool _hasCave;
    private double _duration = 3600.0;
    private double _elapsed;
    private double _startup;
    private double _reportTimer;
    private double _feedTimer;
    private double _phaseTimer;
    private bool _started;
    private bool _inCave;
    private int _caveEncounters;
    private int _babiesObserved;
    private bool _finishing;
    private string _soakWorldDir = "";

    public override void _Ready()
    {
        // A new profile defaults to MAX quality / 12 chunks. That is useful
        // for a fresh player, but needlessly floods a hosted headless soak with
        // hundreds of colliders and far-LOD tiles before survival starts.
        // Keep the real scene, physics and mob systems enabled at a deterministic
        // low preset and a two-chunk view radius for a reproducible soak load.
        Settings.Load();
        Settings.GfxPreset = 0;
        Settings.ViewDistance = 2;
        Settings.AutoQuality = false;
        Settings.Difficulty = Settings.DifficultyNormal;

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--duration=")
                    && double.TryParse(arg["--duration=".Length..], out double seconds))
            {
                _duration = Mathf.Clamp((float)seconds, 60.0f, 86400.0f);
            }
        }

        // Never borrow the user's most-recent world for an automated soak.
        // A private directory keeps this world out of the world picker, and its
        // save files are verified then removed when the run finishes.
        const int soakSeed = -90817263; // Previously verified to contain nearby V7 caves.
        _soakWorldDir = $"user://tests/survival-soak-{Time.GetTicksUsec()}";
        long now = WorldStore.NowUtc();
        WorldManifest manifest = new("Temporary survival soak", soakSeed,
                WorldGenV7.Version, WorldStore.DeriveNoiseSeeds(soakSeed), now, now,
                "survival", null);
        if (!WorldStore.WriteManifest(_soakWorldDir, manifest))
        {
            GD.PushError("SURVIVAL SOAK: could not create isolated test world manifest");
            CleanupSoakWorld();
            GetTree().Quit(1);
            return;
        }

        GameSession.Instance.SelectWorld(_soakWorldDir);
        PackedScene game = GD.Load<PackedScene>("res://scenes/main.tscn");
        _main = game.Instantiate<Main>();
        AddChild(_main);
        _world = _main.GetNode<VoxelWorld>("World");
        _player = _main.GetNode<Player>("Player");
        _mobs = _main.GetNode<Mobs>("Mobs");
        Settings.Difficulty = Settings.DifficultyNormal;
        _player.SetMode(GameMode.Survival);
        _surfacePosition = _world.FindSpawn();
        _player.GlobalPosition = _surfacePosition;
        GD.Print($"SURVIVAL SOAK: initialized main scene, duration={_duration:0}s, " +
                $"viewDistance={_world.ViewDistance}, meshWorkers={_world.MaxInFlight}, " +
                $"lodRadius={_world.LodRadius:0}, difficulty=normal");
    }

    public override void _Process(double delta)
    {
        if (_finishing)
        {
            return;
        }

        if (!_started)
        {
            _startup += delta;
            if (_startup >= 8.0)
            {
                StartScenario();
            }

            return;
        }

        _elapsed += delta;
        _phaseTimer += delta;
        _feedTimer += delta;
        _reportTimer += delta;

        // Keep hostile encounters recurring while still exercising daylight cleanup.
        _world.TimeOfDay = _phaseTimer % 180.0 < 110.0 ? 0.78 : 0.32;
        _mobs.WeatherRef!.Current = _phaseTimer % 240.0 < 70.0
                ? Weather.State.Storm : Weather.State.Clear;

        if (_phaseTimer >= 150.0)
        {
            _phaseTimer = 0.0;
            if (_hasCave)
            {
                _inCave = !_inCave;
                _player.GlobalPosition = _inCave ? _cavePosition : _surfacePosition;
                if (_inCave)
                {
                    int stalkers = _mobs.SpawnKind("stalker", 2);
                    int wisps = _mobs.SpawnKind("crystalwisp", 2);
                    _caveEncounters += stalkers + wisps;
                }
                else
                {
                    _mobs.SpawnKind("shade", 2);
                    _mobs.SpawnKind("rot", 2);
                }
            }
        }

        if (_feedTimer >= 10.0)
        {
            _feedTimer = 0.0;
            FeedHerds();
        }

        foreach (Node child in _mobs.GetChildren())
        {
            if (child is Mob { IsBaby: true })
            {
                _babiesObserved++;
                break;
            }
        }

        if (_reportTimer >= 60.0)
        {
            _reportTimer = 0.0;
            GD.Print($"SOAK minute={_elapsed / 60.0:0} passive={_mobs.PassiveCount} hostile={_mobs.HostileCount} feeds={_feedSuccesses} caveEncounters={_caveEncounters}");
        }

        if (_elapsed >= _duration)
        {
            Finish();
        }
    }

    private void StartScenario()
    {
        Settings.Difficulty = Settings.DifficultyNormal;
        _player.SetMode(GameMode.Survival);
        _started = true;
        _world.TimeOfDay = 0.78;
        _mobs.WeatherRef!.Current = Weather.State.Storm;
        _hasCave = TryFindCave(out _cavePosition);
        if (_hasCave)
        {
            _player.GlobalPosition = _cavePosition;
            _inCave = true;
        }

        // Let the population manager create grouped, naturally spaced herds.
        // Its reserved offspring slots make the real feeding/breeding loop viable.
        _mobs.SpawnKind("shade", 2);
        _mobs.SpawnKind("rot", 2);
        _mobs.SpawnKind("hollow", 2);
        _mobs.SpawnKind("dreadwing", 1);
        if (_hasCave)
        {
            _caveEncounters += _mobs.SpawnKind("stalker", 2);
            _caveEncounters += _mobs.SpawnKind("crystalwisp", 2);
        }

        FeedHerds();
        GD.Print($"SURVIVAL SOAK: scenario started; caveFound={_hasCave}, caveEncounters={_caveEncounters}");
    }

    private bool TryFindCave(out Vector3 position)
    {
        position = Vector3.Zero;
        Vector3 origin = _surfacePosition;
        int ox = Mathf.FloorToInt(origin.X);
        int oz = Mathf.FloorToInt(origin.Z);
        for (int radius = 0; radius <= 18; radius += 3)
        {
            for (int dz = -radius; dz <= radius; dz += 2)
            {
                for (int dx = -radius; dx <= radius; dx += 2)
                {
                    int x = ox + dx;
                    int z = oz + dz;
                    Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                            Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
                    if (!_world.HasChunk(cp)) continue;
                    int surface = _world.SurfaceHeight(x, z);
                    for (int y = Mathf.Min(surface - 7, _world.Height - 4); y >= 8; y--)
                    {
                        if (!_world.IsCave(x, y, z, surface)
                                || _world.GetBlock(x, y, z) != BlockId.Air
                                || _world.GetBlock(x, y + 1, z) != BlockId.Air
                                || !BlockDb.DefOf(_world.GetBlock(x, y - 1, z)).Solid) continue;
                        position = new Vector3(x + 0.5f, y + 0.1f, z + 0.5f);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void FeedHerds()
    {
        // New worlds fill all nine hotbar slots with the starter kit. Reserve
        // the final two slots explicitly; Inventory.Add would put food in the
        // backpack, while Mob.TryFeed intentionally requires the selected
        // hotbar stack. Restock only this private test inventory as needed.
        EnsureHotbarFeedReserve(ItemId.Wheat, 8, Inventory.HotbarSize - 2);
        EnsureHotbarFeedReserve(ItemId.Seeds, 8, Inventory.HotbarSize - 1);
        int wheatSlot = FindHotbarSlot(ItemId.Wheat);
        int seedSlot = FindHotbarSlot(ItemId.Seeds);
        foreach (Node child in _mobs.GetChildren())
        {
            if (child is not Wanderer animal || animal.IsBaby || animal.InLove) continue;
            int slot = animal is Chicken ? seedSlot : wheatSlot;
            if (slot >= 0)
            {
                _player.Select(slot);
                if (animal.Interact(_player))
                {
                    _feedSuccesses++;
                }
            }
        }
    }

    private int _feedSuccesses;

    private void EnsureHotbarFeedReserve(ItemId id, int reserve, int slot)
    {
        ItemStack current = _player.Inventory.Hotbar[slot];
        if (current.Id != id || current.Count < reserve)
        {
            _player.Inventory.Set(slot, ItemDb.New(id, reserve));
        }
    }

    private int FindHotbarSlot(ItemId id)
    {
        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            if (_player.Inventory.Hotbar[i].Id == id) return i;
        }

        return -1;
    }

    private async void Finish()
    {
        if (_finishing)
        {
            return;
        }

        _finishing = true;
        _world.SaveNow();
        WorldSave? savedWorld = WorldStore.ReadJson<WorldSave>(
                WorldStore.WorldSavePath(_soakWorldDir), WorldStore.Json);
        PlayerSave? savedPlayer = WorldStore.ReadJson<PlayerSave>(
                WorldStore.PlayerSavePath(_soakWorldDir), WorldStore.Json);
        bool savesReloaded = savedWorld is not null && savedPlayer is not null
                && savedPlayer.Mode == "survival";
        bool passed = _hasCave && _caveEncounters > 0 && _feedSuccesses >= 2
                && _babiesObserved > 0 && _mobs.PassiveCount > 0 && savesReloaded;
        int passive = _mobs.PassiveCount;
        int hostile = _mobs.HostileCount;

        // Let VoxelWorld/FarLod exit hooks wait for and release every worker task
        // group before the engine starts global shutdown.
        _main.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        bool cleaned = CleanupSoakWorld();
        passed &= cleaned;
        GD.Print($"SURVIVAL SOAK {(passed ? "PASS" : "INCOMPLETE")}: elapsed={_elapsed:0}s passive={passive} hostile={hostile} feeds={_feedSuccesses} caveEncounters={_caveEncounters} babyObservations={_babiesObserved} savesReloaded={savesReloaded} tempWorldCleaned={cleaned}");
        GetTree().Quit(passed ? 0 : 1);
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        // Also remove a private test world if the soak is interrupted.
        CleanupSoakWorld();
    }

    private bool CleanupSoakWorld()
    {
        if (_soakWorldDir.Length == 0)
        {
            return true;
        }

        string directory = _soakWorldDir;
        bool success = RemoveSoakDirectory(directory);
        if (success)
        {
            _soakWorldDir = "";
        }
        else
        {
            GD.PushError($"SURVIVAL SOAK: failed to remove temporary world at {directory}");
        }

        return success;
    }

    private static bool RemoveSoakDirectory(string path)
    {
        string absolutePath = ProjectSettings.GlobalizePath(path);
        if (!DirAccess.DirExistsAbsolute(absolutePath))
        {
            return true;
        }

        string[] files;
        string[] directories;
        using (DirAccess? access = DirAccess.Open(path))
        {
            if (access == null)
            {
                GD.PushError($"SURVIVAL SOAK: could not open temporary directory {path}");
                return false;
            }

            files = access.GetFiles();
            directories = access.GetDirectories();
        }

        bool success = true;
        foreach (string file in files)
        {
            string filePath = ProjectSettings.GlobalizePath($"{path}/{file}");
            if (DirAccess.RemoveAbsolute(filePath) != Error.Ok
                    && FileAccess.FileExists($"{path}/{file}"))
            {
                success = false;
            }
        }

        foreach (string childDirectory in directories)
        {
            if (!RemoveSoakDirectory($"{path}/{childDirectory}"))
            {
                success = false;
            }
        }

        if (!success)
        {
            return false;
        }

        Error removed = DirAccess.RemoveAbsolute(absolutePath);
        return removed == Error.Ok || !DirAccess.DirExistsAbsolute(absolutePath);
    }
}
