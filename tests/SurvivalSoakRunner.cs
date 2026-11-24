using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;

namespace Minecraft;

/// <summary>
/// Opt-in integration soak: runs the real main scene, feeds live herds, and
/// alternates surface/cave and day/night conditions. Default duration is one hour.
/// </summary>
public partial class SurvivalSoakRunner : Node
{
    private Node _main = null!;
    private VoxelWorld _world = null!;
    private Player _player = null!;
    private Mobs _mobs = null!;
    private FluidSystem _fluids = null!;
    private Process _process = null!;
    private long _peakRssBytes;
    private long _peakManagedBytes;
    private long _peakStaticBytes;
    private long _peakOrphanNodes;
    private long _peakVoxelCacheBytes;
    private long _voxelCacheBudgetBytes;
    private long _peakPooledMeshBytes;
    private long _peakFluidCells;
    private long _peakFluidSources;
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
    private int _noiseStimuli;
    private int _nonFinitePositionObservations;
    private bool _breedingPairStaged;
    private Wanderer? _breedingParentA;
    private Wanderer? _breedingParentB;
    private bool _finishing;
    private Task<int>? _completionTask; // Owns the asynchronous run result and prevents duplicate finalization.

    // Narrow lifecycle-test seams. Production leaves these null and uses the
    // real frame, worker and filesystem operations.
    internal Func<Task>? WaitForFrameOverride;
    internal Func<string, bool>? DeleteWorldOverride;
    internal Func<bool>? WorkersDrainedOverride;
    internal Action<int>? RequestExitOverride;
    internal Action<Exception>? ReportFailureOverride;
    internal int DrainFrameLimit = 60;

    private bool _soakPassed;
    private bool _savesReloaded;
    private bool _temporaryWorldCleaned;
    private bool _payloadsDrained;
    private bool _boundedPayloads;
    private bool _cacheWithinBudget;
    private int _finalPassiveCount;
    private int _finalHostileCount;
    private int _finalNoiseInvestigations;
    private string _soakWorldDir = "";

    public override void _Ready()
    {
        try
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
            const int soakSeed = -90817263; // Retained deterministic cave seed; use the current generator version.
            _soakWorldDir = $"user://tests/survival-soak-{Time.GetTicksUsec()}";
            long now = WorldStore.NowUtc();
            WorldManifest manifest = new("Temporary survival soak", soakSeed,
                    WorldStore.CurrentGenVersion, WorldStore.DeriveNoiseSeeds(soakSeed), now, now,
                    "survival", null);
            if (!WorldStore.WriteManifest(_soakWorldDir, manifest))
            {
                _completionTask = Fail("Could not create the isolated survival-soak world manifest.");
                return;
            }

            GameSession.Instance.SelectWorld(_soakWorldDir);
            PackedScene game = GD.Load<PackedScene>("res://scenes/main.tscn");
            _main = game.Instantiate<Main>();
            AddChild(_main);
            _world = _main.GetNode<VoxelWorld>("World");
            _player = _main.GetNode<Player>("Player");
            _mobs = _main.GetNode<Mobs>("Mobs");
            _fluids = _main.GetNode<FluidSystem>("FluidSystem");
            _process = Process.GetCurrentProcess();
            Settings.Difficulty = Settings.DifficultyNormal;
            _player.SetMode(GameMode.Survival);
            _surfacePosition = _world.FindSpawn();
            _player.GlobalPosition = _surfacePosition;
            GD.Print($"SURVIVAL SOAK: initialized main scene, duration={_duration:0}s, " +
                    $"viewDistance={_world.ViewDistance}, meshWorkers={_world.MaxInFlight}, " +
                    $"lodRadius={_world.LodRadius:0}, difficulty=normal");
        }
        catch (Exception exception)
        {
            _completionTask = Fail("initialization failed", exception);
        }
    }

    public override void _Process(double delta)
    {
        try
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
                if (child is Mob mob)
                {
                    Vector3 position = mob.GlobalPosition;
                    if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
                            || !float.IsFinite(position.Z))
                        _nonFinitePositionObservations++;

                    if (mob.IsBaby)
                        _babiesObserved++;
                }
            }

            if (_reportTimer >= 60.0)
            {
                _reportTimer = 0.0;
                ReportSoakMetrics();
            }

            if (_elapsed >= _duration)
            {
                _completionTask = Finish();
            }
        }
        catch (Exception exception)
        {
            _completionTask = Fail("execution failed", exception);
        }
    }

    private void ReportSoakMetrics()
    {
        VoxelCacheMemorySnapshot cache = _world.GetCacheMemorySnapshot();
        Profiler.RecordCacheMemory(cache);
        long rss = _process.WorkingSet64;
        long managed = GC.GetTotalMemory(false);
        long staticBytes = Math.Max(0L, (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic));
        long objects = Math.Max(0L, (long)Performance.GetMonitor(Performance.Monitor.ObjectCount));
        long nodes = Math.Max(0L, (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        long orphanNodes = Math.Max(0L, (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));
        long videoBytes = Math.Max(0L, (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed));

        _peakRssBytes = Math.Max(_peakRssBytes, rss);
        _peakManagedBytes = Math.Max(_peakManagedBytes, managed);
        _peakStaticBytes = Math.Max(_peakStaticBytes, staticBytes);
        _peakOrphanNodes = Math.Max(_peakOrphanNodes, orphanNodes);
        _peakVoxelCacheBytes = Math.Max(_peakVoxelCacheBytes, cache.VoxelArrayBytes);
        _voxelCacheBudgetBytes = cache.VoxelArrayBudgetBytes;
        _peakPooledMeshBytes = Math.Max(_peakPooledMeshBytes, cache.PooledMeshBufferBytes);
        _peakFluidCells = Math.Max(_peakFluidCells, _fluids.ActiveCellCount);
        _peakFluidSources = Math.Max(_peakFluidSources, _fluids.ActiveSourceCount);

        GD.Print($"SOAK minute={_elapsed / 60.0:0} passive={_mobs.PassiveCount} hostile={_mobs.HostileCount} " +
                $"feeds={_feedSuccesses} caveEncounters={_caveEncounters} babiesObserved={_babiesObserved} " +
                $"noiseInvestigations={_mobs.NoiseInvestigations} invalidPositions={_nonFinitePositionObservations} " +
                $"rssMiB={rss / 1048576.0:0.0} managedMiB={managed / 1048576.0:0.0} " +
                $"godotStaticMiB={staticBytes / 1048576.0:0.0} videoMiB={videoBytes / 1048576.0:0.0} " +
                $"objects={objects} nodes={nodes} orphanNodes={orphanNodes} " +
                $"voxelCacheMiB={cache.VoxelArrayBytes / 1048576.0:0.0}/{cache.VoxelArrayBudgetBytes / 1048576.0:0.0} " +
                $"cacheIndexMiB={cache.CacheIndexBytesEstimate / 1048576.0:0.0} cropRegistryMiB={cache.CropRegistryBytesEstimate / 1048576.0:0.0} " +
                $"sourceScanMiB={cache.SourceScanRegistryBytesEstimate / 1048576.0:0.0} cropScanMiB={cache.CropScanRegistryBytesEstimate / 1048576.0:0.0} " +
                $"campfireSitesMiB={cache.CampfireSiteCacheBytesEstimate / 1048576.0:0.0} " +
                $"structureSitesMiB={cache.StructureSiteCacheBytesEstimate / 1048576.0:0.0} editTopArraysMiB={cache.EditTopArrayBytes / 1048576.0:0.0} " +
                $"editTopIndexMiB={cache.EditTopIndexBytesEstimate / 1048576.0:0.0} savedRegistriesMiB={cache.WorldStateRegistryBytesEstimate / 1048576.0:0.0} " +
                $"chunkIndexMiB={cache.ChunkIndexBytesEstimate / 1048576.0:0.0} editMetaMiB={cache.EditMetadataBytesEstimate / 1048576.0:0.0} " +
                $"lightSourcesMiB={cache.LightSourceBytesEstimate / 1048576.0:0.0} appliedMeshMiB={cache.AppliedMeshBytesEstimate / 1048576.0:0.0} " +
                $"pooledMeshMiB={cache.PooledMeshBufferBytes / 1048576.0:0.0} trackedManagedMiB={cache.TrackedManagedBytesEstimate / 1048576.0:0.0} " +
                $"dataChunks={Profiler.DataChunks} workers={Profiler.WorkersActive} " +
                $"freshQueue={Profiler.FreshQueued} rebuildQueue={Profiler.RebuildQueued} " +
                $"pendingPayloads={Profiler.PendingPayloadCount}/{Profiler.PendingPayloadHighWaterCount} " +
                $"pendingPayloadMiB={Profiler.PendingPayloadBytes / 1048576.0:0.0} " +
                $"fluidCells={_fluids.ActiveCellCount} fluidSources={_fluids.ActiveSourceCount} " +
                $"gc={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
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

        // Use naturally streamed herds instead of overfilling the manager's
        // passive cap with debug spawns. Stage one real nearby pair so breeding
        // can be observed deterministically during shorter smoke-soak runs.
        _breedingPairStaged = StageBreedingPair();
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
        GD.Print($"SURVIVAL SOAK: scenario started; caveFound={_hasCave}, caveEncounters={_caveEncounters}, breedingPairStaged={_breedingPairStaged}, parentsInLove={_breedingParentA?.InLove == true && _breedingParentB?.InLove == true}");
    }

    private bool StageBreedingPair()
    {
        Wanderer? first = null;
        Wanderer? second = null;
        foreach (Node child in _mobs.GetChildren())
        {
            if (child is not Wanderer animal || animal.IsBaby)
                continue;

            if (first == null)
                first = animal;
            else if (animal.GetType() == first.GetType())
            {
                second = animal;
                break;
            }
        }

        if (first == null || second == null
                || !TryFindBreedingPairPositions(out Vector3 firstPosition,
                        out Vector3 secondPosition))
            return false;

        first.GlobalPosition = firstPosition;
        second.GlobalPosition = secondPosition;
        first.Home = firstPosition;
        second.Home = secondPosition;
        _breedingParentA = first;
        _breedingParentB = second;

        // Reserve exactly one slot for the newborn without exceeding the
        // production population cap; all unrelated surviving herds remain intact.
        int excess = Mathf.Max(0, _mobs.PassiveCount - 9);
        foreach (Node child in _mobs.GetChildren())
        {
            if (excess <= 0) break;
            if (child is Wanderer animal && animal != first && animal != second && !animal.IsBaby)
            {
                animal.QueueFree();
                excess--;
            }
        }

        return true;
    }

    private bool TryFindBreedingPairPositions(out Vector3 first, out Vector3 second)
    {
        first = Vector3.Zero;
        second = Vector3.Zero;
        int originX = Mathf.FloorToInt(_surfacePosition.X);
        int originZ = Mathf.FloorToInt(_surfacePosition.Z);
        for (int radius = 0; radius <= 18; radius += 2)
        {
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = originX + dx;
                    int z = originZ + dz;
                    int yA = _world.SurfaceHeight(x, z);
                    int yB = _world.SurfaceHeight(x + 1, z);
                    if (yA != yB || !HasBreedingSpace(x, yA, z)
                            || !HasBreedingSpace(x + 1, yB, z))
                        continue;

                    first = new Vector3(x + 0.5f, yA + 1.0f, z + 0.5f);
                    second = new Vector3(x + 1.5f, yB + 1.0f, z + 0.5f);
                    return true;
                }
        }

        return false;
    }

    private bool HasBreedingSpace(int x, int surface, int z)
    {
        Vector2I chunk = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
        if (!_world.HasChunk(chunk) || surface < 1 || surface >= _world.Height - 3)
            return false;

        return BlockDb.DefOf(_world.GetBlock(x, surface, z)).Solid
                && _world.GetBlock(x, surface + 1, z) == BlockId.Air
                && _world.GetBlock(x, surface + 2, z) == BlockId.Air
                && _world.GetBlock(x, surface + 3, z) == BlockId.Air;
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
        Vector3 soundOrigin = _player.GlobalPosition + new Vector3(0, 1.0f, 0);
        _player.EmitSignal(Player.SignalName.MiningTick, soundOrigin, (int)BlockId.Stone);
        _player.EmitSignal(Player.SignalName.BlockPlaced, soundOrigin, (int)BlockId.Torch);
        _noiseStimuli += 2;

        // New worlds fill all nine hotbar slots with the starter kit. Reserve
        // the final two slots explicitly; Inventory.Add would put food in the
        // backpack, while Mob.TryFeed intentionally requires the selected
        // hotbar stack. Restock only this private test inventory as needed.
        EnsureHotbarFeedReserve(ItemId.Wheat, 8, Inventory.HotbarSize - 2);
        EnsureHotbarFeedReserve(ItemId.Seeds, 8, Inventory.HotbarSize - 1);
        int wheatSlot = FindHotbarSlot(ItemId.Wheat);
        int seedSlot = FindHotbarSlot(ItemId.Seeds);
        if (wheatSlot < 0)
        {
            wheatSlot = 0;
            _player.Inventory.Hotbar[wheatSlot] = ItemDb.New(ItemId.Wheat, 16);
        }

        if (seedSlot < 0)
        {
            seedSlot = wheatSlot == 1 ? 0 : 1;
            _player.Inventory.Hotbar[seedSlot] = ItemDb.New(ItemId.Seeds, 8);
        }

        _player.Inventory.RaiseChanged();
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

    private Task<int> Finish()
    {
        return BeginCompletion(SaveAndValidateSoakAsync);
    }

    private Task SaveAndValidateSoakAsync()
    {
        ReportSoakMetrics();
        _world.SaveNow();
        WorldSave? savedWorld = WorldStore.ReadJson<WorldSave>(
                WorldStore.WorldSavePath(_soakWorldDir), WorldStore.Json);
        PlayerSave? savedPlayer = WorldStore.ReadJson<PlayerSave>(
                WorldStore.PlayerSavePath(_soakWorldDir), WorldStore.Json);
        _savesReloaded = savedWorld is not null && savedPlayer is not null
                && savedPlayer.Mode == "survival";
        _finalPassiveCount = _mobs.PassiveCount;
        _finalHostileCount = _mobs.HostileCount;
        _finalNoiseInvestigations = _mobs.NoiseInvestigations;

        // Creature runtime, acoustic perception, feeding, breeding and save
        // reload all have to hold for a soak to count as a pass.
        _soakPassed = _hasCave && _caveEncounters > 0 && _feedSuccesses >= 2
                && _babiesObserved > 0 && _finalPassiveCount > 0 && _savesReloaded
                && _noiseStimuli > 0 && _finalNoiseInvestigations > 0
                && _nonFinitePositionObservations == 0;
        return Task.CompletedTask;
    }

    internal Task<int> BeginCompletion(Func<Task>? operation)
    {
        if (_completionTask != null)
            return _completionTask;

        _finishing = true;
        _completionTask = RunnerCompletion.ExecuteAsync(
                operation,
                [
                    QueueMainForDeletionAsync,
                    WaitForProcessFrameAsync,
                    WaitForProcessFrameAsync,
                    WaitForWorkerDrainAsync,
                    CleanupSoakWorldAsync,
                    ValidateSoakCompletionAsync,
                    ReleaseSceneReferencesAsync,
                ],
                exception =>
                {
                    if (ReportFailureOverride != null)
                    {
                        ReportFailureOverride(exception);
                        return;
                    }

                    GD.PushError($"SURVIVAL SOAK: completion failed: {exception}");
                },
                exitCode =>
                {
                    if (RequestExitOverride != null)
                    {
                        RequestExitOverride(exitCode);
                        return;
                    }

                    GetTree().Quit(exitCode);
                },
                exitCode =>
                {
                    GD.Print($"SURVIVAL SOAK {(exitCode == 0 ? "PASS" : "INCOMPLETE")}: elapsed={_elapsed:0}s " +
                            $"passive={_finalPassiveCount} hostile={_finalHostileCount} feeds={_feedSuccesses} " +
                            $"caveEncounters={_caveEncounters} babyObservations={_babiesObserved} " +
                            $"noiseStimuli={_noiseStimuli} noiseInvestigations={_finalNoiseInvestigations} " +
                            $"invalidPositions={_nonFinitePositionObservations} savesReloaded={_savesReloaded} " +
                            $"tempWorldCleaned={_temporaryWorldCleaned} payloadsDrained={_payloadsDrained} " +
                            $"payloadHighWater={Profiler.PendingPayloadHighWaterCount}/{StreamingApplyBudget.MaxPendingPayloads} " +
                            $"cachePeakMiB={Profiler.VoxelCacheArrayHighWaterBytes / 1048576.0:0.0} " +
                            $"generatorSeedArrayPeakMiB={Profiler.GeneratorSeedArrayHighWaterBytes / 1048576.0:0.0} " +
                            $"cacheBudgetMiB={_voxelCacheBudgetBytes / 1048576.0:0.0} " +
                            $"cacheIndexPeakMiB={Profiler.CacheIndexBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"cropRegistryPeakMiB={Profiler.CropRegistryBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"sourceScanPeakMiB={Profiler.SourceScanRegistryBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"cropScanPeakMiB={Profiler.CropScanRegistryBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"campfireSitesPeakMiB={Profiler.CampfireSiteCacheBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"structureSitesPeakMiB={Profiler.StructureSiteCacheBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"villageSitesPeakMiB={Profiler.VillageSiteCacheBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"editTopArrayPeakMiB={Profiler.EditTopArrayHighWaterBytes / 1048576.0:0.0} " +
                            $"savedRegistriesPeakMiB={Profiler.WorldStateRegistryBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"chunkIndexPeakMiB={Profiler.ChunkIndexBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"editMetadataPeakMiB={Profiler.EditMetadataBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"lightSourcesPeakMiB={Profiler.LightSourceBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"appliedMeshPeakMiB={Profiler.AppliedMeshBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"pooledMeshRetainedPeakMiB={Profiler.PooledMeshBufferHighWaterBytes / 1048576.0:0.0} " +
                            $"streamingQueuePeakMiB={Profiler.StreamingQueueBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"persistentMetadataPeakMiB={Profiler.PersistentMetadataBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"trackedManagedPeakMiB={Profiler.TrackedManagedBytesHighWaterEstimate / 1048576.0:0.0} " +
                            $"pooledMeshPeakMiB={_peakPooledMeshBytes / 1048576.0:0.0} " +
                            $"peakRssMiB={_peakRssBytes / 1048576.0:0.0} " +
                            $"peakManagedMiB={_peakManagedBytes / 1048576.0:0.0} " +
                            $"peakGodotStaticMiB={_peakStaticBytes / 1048576.0:0.0} " +
                            $"peakOrphanNodes={_peakOrphanNodes} peakFluidCells={_peakFluidCells} peakFluidSources={_peakFluidSources}");
                    SetProcess(false);
                });
        return _completionTask;
    }

    private Task<int> Fail(string message) =>
            BeginCompletion(() => Task.FromException(new InvalidOperationException(message)));

    private Task<int> Fail(string message, Exception exception) =>
            BeginCompletion(() => Task.FromException(
                    new InvalidOperationException(message, exception)));

    private Task QueueMainForDeletionAsync()
    {
        if (_main != null && GodotObject.IsInstanceValid(_main)
                && !_main.IsQueuedForDeletion())
            _main.QueueFree();
        return Task.CompletedTask;
    }

    internal async Task WaitForProcessFrameAsync()
    {
        if (WaitForFrameOverride != null)
        {
            await WaitForFrameOverride();
            return;
        }

        if (IsInsideTree())
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool WorkersDrained() => WorkersDrainedOverride?.Invoke()
            ?? (Profiler.PendingPayloadCount == 0 && Profiler.PendingPayloadBytes == 0
                && !Profiler.WorkersActive);

    // QueueFree waits for the world's worker group in VoxelWorld._ExitTree.
    // Give the queued scene time to shut those workers down before validating
    // the drain result; the temporary world is only deleted after this step.
    private async Task WaitForWorkerDrainAsync()
    {
        bool drained = WorkersDrained();
        for (int attempt = 0; attempt < DrainFrameLimit && !drained; attempt++)
        {
            await WaitForProcessFrameAsync();
            drained = WorkersDrained();
        }

        _payloadsDrained = drained;
        if (!drained)
            throw new InvalidOperationException(
                    "Meshing workers or completed payloads remained at soak shutdown.");
    }

    private Task CleanupSoakWorldAsync()
    {
        _temporaryWorldCleaned = CleanupSoakWorld();
        if (!_temporaryWorldCleaned)
            throw new System.IO.IOException("Temporary soak-world cleanup did not complete.");
        return Task.CompletedTask;
    }

    private Task ValidateSoakCompletionAsync()
    {
        _boundedPayloads = Profiler.PendingPayloadHighWaterCount
                <= StreamingApplyBudget.MaxPendingPayloads;
        _cacheWithinBudget = Profiler.VoxelCacheArrayHighWaterBytes <= _voxelCacheBudgetBytes;
        _soakPassed &= _temporaryWorldCleaned && _payloadsDrained
                && _boundedPayloads && _cacheWithinBudget;

        if (!_payloadsDrained)
            throw new InvalidOperationException("Meshing workers or completed payloads remained at soak shutdown.");
        if (!_boundedPayloads)
            throw new InvalidOperationException("The completed-payload high-water mark exceeded its configured bound.");
        if (!_cacheWithinBudget)
            throw new InvalidOperationException("The voxel byte-array cache exceeded its configured budget.");
        if (!_soakPassed)
            throw new InvalidOperationException("One or more survival soak scenario checks failed.");
        return Task.CompletedTask;
    }

    private Task ReleaseSceneReferencesAsync()
    {
        Process? process = _process;
        _process = null!;
        process?.Dispose();
        _main = null!;
        _world = null!;
        _player = null!;
        _mobs = null!;
        _fluids = null!;
        _breedingParentA = null;
        _breedingParentB = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Task.CompletedTask;
    }

    public override void _ExitTree()
    {
        // This is a final idempotent fallback for externally interrupted runs.
        try
        {
            Process? process = _process;
            _process = null!;
            process?.Dispose();
            _main = null!;
            _world = null!;
            _player = null!;
            _mobs = null!;
            _fluids = null!;
            _breedingParentA = null;
            _breedingParentB = null;
        }
        catch (Exception exception)
        {
            GD.PushError($"SURVIVAL SOAK: emergency reference release failed: {exception}");
        }

        try
        {
            CleanupSoakWorld();
        }
        catch (Exception exception)
        {
            GD.PushError($"SURVIVAL SOAK: emergency temporary-world cleanup failed: {exception}");
        }
    }

    private bool CleanupSoakWorld()
    {
        if (_soakWorldDir.Length == 0)
        {
            return true;
        }

        string directory = _soakWorldDir;
        bool success = DeleteWorldOverride?.Invoke(directory) ?? WorldStore.DeleteTemporaryWorld(directory);
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
