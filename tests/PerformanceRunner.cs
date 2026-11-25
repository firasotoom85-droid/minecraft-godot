using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Minecraft;

/// <summary>
/// Opt-in rendered performance benchmark. Uses a disposable world generated
/// by the repository's current generator and a fixed, precomputed camera route.
/// Run each scenario as a separate process so measurements remain comparable.
/// </summary>
public partial class PerformanceRunner : Node
{
    private const int RoutePointCount = 24;
    private const int DefaultSeed = 1337;
    private const string OutputDirectory = "user://benchmarks";

    private readonly List<double> _frameElapsedSeconds = [];
    private readonly List<double> _frameTimesMs = [];
    private readonly List<double> _processTimesMs = [];
    private readonly List<double> _physicsTimesMs = [];
    private readonly List<int> _chunkCounts = [];
    private readonly List<int> _freshQueues = [];
    private readonly List<int> _rebuildQueues = [];
    private readonly List<int> _dataChunks = [];
    private readonly Vector3[] _route = new Vector3[RoutePointCount];

    private Node? _main;
    private VoxelWorld? _world;
    private Player? _player;
    private Weather? _weather;
    private Mobs? _mobs;
    private Process? _process;

    private string _scenario = "open-terrain";
    private string _commit = "unknown";
    private string _build = "unknown";
    private string _worldDir = "";
    private string _outputBase = "";
    private int _seed = DefaultSeed;
    private int _preset = Gfx.DefaultPreset;
    private int _viewDistance = Gfx.Presets[Gfx.DefaultPreset].View;
    private int _windowWidth = 1280;
    private int _windowHeight = 720;
    private bool _headless;
    private double _warmupSeconds = 15.0;
    private double _durationSeconds = 30.0;
    private float _routeRadius = 24.0f;
    private float _routeSpeed = 2.4f;
    private bool _routeRadiusSpecified;
    private bool _routeSpeedSpecified;

    private double _startupElapsed;
    private double _warmupElapsed;
    private double _measurementElapsed;
    private string _measurementStartedAtUtc = "";
    private double _memorySampleElapsed;
    private double _routeElapsed;
    private bool _started;
    private bool _finishing;
    private Task<int>? _completionTask; // Owns the asynchronous run result and prevents duplicate finalization.

    // Narrow lifecycle-test seams. Production leaves these null and uses the
    // real frame, scene, worker and filesystem operations.
    internal Func<Task>? WaitForFrameOverride;
    internal Func<Task>? WaitForSettleOverride;
    internal Func<string, bool>? DeleteWorldOverride;
    internal Func<bool>? WorkersDrainedOverride;
    internal Action<int>? RequestExitOverride;
    internal Action<Exception>? ReportFailureOverride;
    internal int DrainFrameLimit = 60;

    private string _reportJsonPath = "";
    private string _reportCsvPath = "";

    private long _managedAllocatedAtStart;
    private int _gc0AtStart;
    private int _gc1AtStart;
    private int _gc2AtStart;
    private long _maxWorkingSetBytes;
    private long _maxManagedHeapBytes;
    private long _maxStaticMemoryBytes;
    private long _maxVideoMemoryBytes;
    private int _maxNodeCount;
    private int _maxObjectCount;
    private int _maxDrawCalls;
    private int _maxPrimitives;
    private int _maxChunkCount;
    private int _maxFreshQueued;
    private int _maxRebuildQueued;
    private int _maxDataChunks;
    private int _requestedHostileCount;
    private int _passiveCount;
    private int _hostileCount;
    private bool _workerWasActive;
    private Vector3 _center;
    private string _scenarioNote = "";

#if DEBUG
    private const string DefaultBuildConfiguration = "Debug";
#else
    private const string DefaultBuildConfiguration = "Release";
#endif

    public override void _Ready()
    {
        try
        {
            ParseArguments();
            if (!IsSupportedScenario(_scenario))
            {
                _completionTask = Fail($"Unknown benchmark scenario '{_scenario}'.");
                return;
            }

            Settings.Load();
            ConfigureDisplay();
            Settings.GfxPreset = _preset;
            Settings.ViewDistance = _viewDistance;
            Settings.AutoQuality = false;
            Settings.GreedyMeshing = true;
            Settings.Difficulty = Settings.DifficultyNormal;

            _frameTimesMs.Capacity = Math.Max(1200, (int)(_durationSeconds * 240.0) + 512);
            _frameElapsedSeconds.Capacity = _frameTimesMs.Capacity;
            _processTimesMs.Capacity = _frameTimesMs.Capacity;
            _physicsTimesMs.Capacity = _frameTimesMs.Capacity;
            _chunkCounts.Capacity = _frameTimesMs.Capacity;
            _freshQueues.Capacity = _frameTimesMs.Capacity;
            _rebuildQueues.Capacity = _frameTimesMs.Capacity;
            _dataChunks.Capacity = _frameTimesMs.Capacity;

            _worldDir = WorldStore.Create(
                    $"Temporary performance benchmark ({_scenario})", _seed, "creative", "normal");
            if (_worldDir.Length == 0 || WorldStore.ReadManifest(_worldDir) == null)
            {
                _completionTask = Fail("Could not create the isolated benchmark world.");
                return;
            }

            GameSession.Instance.SelectWorld(_worldDir);
            PackedScene scene = GD.Load<PackedScene>("res://scenes/main.tscn");
            if (scene == null)
            {
                _completionTask = Fail("Could not load res://scenes/main.tscn.");
                return;
            }

            _main = scene.Instantiate<Main>();
            AddChild(_main);

            _world = _main.GetNode<VoxelWorld>("World");
            _player = _main.GetNode<Player>("Player");
            _weather = _main.GetNode<Weather>("Weather");
            _mobs = _main.GetNode<Mobs>("Mobs");

            _player.SetMode(GameMode.Creative);
            _player.InputLocked = true;
            _player.Velocity = Vector3.Zero;
            _world.SetViewDistance(_viewDistance);
            _world.SetLodRadius(Gfx.Presets[_preset].Lod);

            _process = Process.GetCurrentProcess();
            GD.Print($"PERF BENCHMARK: scene ready scenario={_scenario} seed={_seed} " +
                    $"gen=V{_world.WorldGenVersion} preset={Gfx.Presets[_preset].Name} " +
                    $"viewDistance={_viewDistance} warmup={_warmupSeconds:0}s duration={_durationSeconds:0}s");
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
            if (_finishing || _world == null || _player == null)
            {
                return;
            }

            if (!_started)
            {
                _startupElapsed += delta;
                if (_startupElapsed >= 8.0)
                {
                    try
                    {
                        StartScenario();
                    }
                    catch (Exception exception)
                    {
                        _completionTask = Fail("Scenario setup failed.", exception);
                    }
                }

                return;
            }

            _routeElapsed += delta;
            UpdateRoute();

            if (_warmupElapsed < _warmupSeconds)
            {
                _warmupElapsed += delta;
                if (_warmupElapsed >= _warmupSeconds)
                {
                    BeginMeasurement();
                }

                return;
            }

            _measurementElapsed += delta;
            CaptureFrame(delta);
            _memorySampleElapsed += delta;
            if (_memorySampleElapsed >= 1.0)
            {
                _memorySampleElapsed = 0.0;
                CaptureMemoryAndSceneCounters();
            }

            if (_measurementElapsed >= _durationSeconds)
            {
                _completionTask = Finish();
            }
        }
        catch (Exception exception)
        {
            _completionTask = Fail("execution failed", exception);
        }
    }

    private void ConfigureDisplay()
    {
        _headless = DisplayServer.GetName() == "headless";
        if (_headless)
        {
            return;
        }

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        DisplayServer.WindowSetSize(new Vector2I(_windowWidth, _windowHeight));
        Engine.MaxFps = 0;
    }

    private void ParseArguments()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            int equals = argument.IndexOf('=');
            if (equals < 0) continue;
            string key = argument[..equals];
            string value = argument[(equals + 1)..];
            switch (key)
            {
                case "--scenario":
                    _scenario = value.Trim().ToLowerInvariant();
                    break;
                case "--seed":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                        _seed = seed;
                    break;
                case "--warmup":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double warmup))
                        _warmupSeconds = Math.Clamp(warmup, 0.0, 300.0);
                    break;
                case "--duration":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                        _durationSeconds = Math.Clamp(duration, 5.0, 600.0);
                    break;
                case "--preset":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int preset))
                        _preset = Mathf.Clamp(preset, 0, Gfx.Presets.Count - 1);
                    break;
                case "--view-distance":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int viewDistance))
                        _viewDistance = Mathf.Clamp(viewDistance, 2, 24);
                    break;
                case "--resolution":
                    string[] dimensions = value.Split('x', StringSplitOptions.TrimEntries);
                    if (dimensions.Length == 2
                            && int.TryParse(dimensions[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                            && int.TryParse(dimensions[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                    {
                        _windowWidth = Math.Clamp(width, 640, 7680);
                        _windowHeight = Math.Clamp(height, 480, 4320);
                    }
                    break;
                case "--route-radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                    {
                        _routeRadius = Mathf.Clamp(radius, 4.0f, 512.0f);
                        _routeRadiusSpecified = true;
                    }
                    break;
                case "--route-speed":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
                    {
                        _routeSpeed = Mathf.Clamp(speed, 0.1f, 32.0f);
                        _routeSpeedSpecified = true;
                    }
                    break;
                case "--commit":
                    _commit = value;
                    break;
                case "--build":
                    _build = value;
                    break;
            }
        }

        if (_build == "unknown")
        {
            _build = DefaultBuildConfiguration;
        }

        if (_scenario == "fast-exploration")
        {
            if (!_routeRadiusSpecified) _routeRadius = 180.0f;
            if (!_routeSpeedSpecified) _routeSpeed = 18.0f;
        }
    }

    private static bool IsSupportedScenario(string scenario) => scenario is
            "open-terrain" or "dense-forest" or "mountain-river" or "village"
            or "deep-cave" or "ocean" or "heavy-rain" or "hostile-encounter"
            or "fast-exploration";

    private void StartScenario()
    {
        if (_world == null || _player == null || _weather == null || _mobs == null)
        {
            throw new InvalidOperationException("The main scene has not finished wiring its systems.");
        }

        Vector3 spawn = _world.FindSpawn();
        _center = _scenario switch
        {
            "open-terrain" => FindBiome(spawn, 1024, IsOpenBiome, "open terrain"),
            "dense-forest" => FindForest(spawn),
            "mountain-river" => FindMountainNearRiver(spawn),
            "village" => FindVillage(spawn),
            "deep-cave" => FindCave(spawn),
            "ocean" => FindOcean(spawn),
            _ => FindBiome(spawn, 1024, IsOpenBiome, "open terrain"),
        };

        if (_scenario == "deep-cave")
        {
            _routeRadius = Math.Min(_routeRadius, 3.0f);
        }

        if (_scenario == "village")
        {
            _routeRadius = Math.Min(_routeRadius, 48.0f);
        }

        if (_scenario == "ocean")
        {
            _center.Y = _world.SeaLevel - 2.5f;
        }

        if (_scenario == "heavy-rain")
        {
            _weather.Current = Weather.State.Storm;
            _weather.Cloudiness = 0.95;
        }
        else
        {
            _weather.Current = Weather.State.Clear;
        }

        BuildRoute();
        _player.GlobalPosition = _route[0];
        _player.Velocity = Vector3.Zero;

        if (_scenario == "hostile-encounter")
        {
            // Surface hostiles retreat at dawn in the real game. Stage this
            // workload at night so the encounter persists through measurement.
            _world.TimeOfDay = 0.82;
            _scenarioNote = "night-time surface encounter";
            // Reassert Normal so a player's saved Peaceful preference cannot
            // suppress this explicit benchmark workload.
            Settings.Difficulty = Settings.DifficultyNormal;
            _requestedHostileCount += _mobs.SpawnKind("shade", 4);
            _requestedHostileCount += _mobs.SpawnKind("rot", 2);
            _requestedHostileCount += _mobs.SpawnKind("hollow", 2);
        }
        _player.Velocity = Vector3.Zero;
        _player.Rotation = Vector3.Zero;
        _player.Camera.Rotation = new Vector3(-0.08f, 0.0f, 0.0f);
        _started = true;
        GD.Print($"PERF BENCHMARK: scenario staged center=({_center.X:0.0},{_center.Y:0.0},{_center.Z:0.0}) " +
                $"note='{_scenarioNote}' mobs={_mobs.PassiveCount} passive/{_mobs.HostileCount} hostile");
    }

    private static bool IsOpenBiome(Biome biome) =>
            biome is Biome.Plains or Biome.FlowerPlains or Biome.Meadow;

    private Vector3 FindBiome(Vector3 origin, int radius, Func<Biome, bool> predicate,
            string description)
    {
        if (_world == null) throw new InvalidOperationException("World unavailable.");
        int originX = Mathf.FloorToInt(origin.X);
        int originZ = Mathf.FloorToInt(origin.Z);
        int bestDistance = int.MaxValue;
        Vector3 best = Vector3.Zero;
        bool found = false;
        for (int z = -radius; z <= radius; z += 16)
        {
            for (int x = -radius; x <= radius; x += 16)
            {
                int wx = originX + x;
                int wz = originZ + z;
                (int height, Biome biome) = _world.SurfaceSampleAt(wx, wz);
                if (!predicate(biome)) continue;
                int distance = x * x + z * z;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = new Vector3(wx + 0.5f, height + 1.0f, wz + 0.5f);
                found = true;
            }
        }

        return found ? best : throw new InvalidOperationException(
                $"Could not find {description} within {radius} blocks of the fixed seed origin.");
    }

    private Vector3 FindForest(Vector3 origin)
    {
        try
        {
            return FindBiome(origin, 1536, biome => biome == Biome.DenseForest, "dense forest");
        }
        catch (InvalidOperationException)
        {
            _scenarioNote = "DenseForest was absent in the search window; using a wooded biome.";
            return FindBiome(origin, 1536,
                    biome => biome is Biome.Birch or Biome.Taiga or Biome.Jungle,
                    "wooded biome");
        }
    }

    private Vector3 FindMountainNearRiver(Vector3 origin)
    {
        if (_world == null)
            throw new InvalidOperationException("World unavailable.");
        WorldGenV8 terrain = new(_seed, "normal");
        int originX = Mathf.FloorToInt(origin.X);
        int originZ = Mathf.FloorToInt(origin.Z);
        float bestRiverStrength = -1.0f;
        Vector3 bestMountain = Vector3.Zero;
        for (int z = -2048; z <= 2048; z += 16)
        {
            for (int x = -2048; x <= 2048; x += 16)
            {
                int wx = originX + x;
                int wz = originZ + z;
                (int height, Biome biome) = _world.SurfaceSampleAt(wx, wz);
                if (biome != Biome.Mountain) continue;
                float river = terrain.ClimateAt(wx, wz, height).RiverStrength;
                if (river <= bestRiverStrength) continue;
                bestRiverStrength = river;
                bestMountain = new Vector3(wx + 0.5f, height + 1.0f, wz + 0.5f);
            }
        }

        if (bestRiverStrength < 0.0f)
            throw new InvalidOperationException("No mountain biome found in the 4096-block search window.");
        _scenarioNote = bestRiverStrength >= 0.5f
                ? $"Mountain sample with a strong local river signal ({bestRiverStrength:0.00})."
                : $"No strong mountain/river overlap found in the search window; using the strongest local signal ({bestRiverStrength:0.00}).";
        return bestMountain;
    }

    private Vector3 FindVillage(Vector3 origin)
    {
        if (_world == null)
            throw new InvalidOperationException("World unavailable.");
        int cellX = Mathf.FloorToInt(origin.X / VoxelWorld.VillageCell);
        int cellZ = Mathf.FloorToInt(origin.Z / VoxelWorld.VillageCell);
        VillageInfo? nearest = null;
        int bestDistance = int.MaxValue;
        for (int radius = 0; radius <= 16; radius++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (radius > 0 && Math.Abs(dx) != radius && Math.Abs(dz) != radius)
                        continue;
                    if (_world.GetVillageSite(cellX + dx, cellZ + dz) is not { } site)
                        continue;
                    int distance = dx * dx + dz * dz;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    nearest = site;
                }
            }
        }

        return nearest is { } village
                ? new Vector3(village.X + 0.5f, village.Y + 1.0f, village.Z + 0.5f)
                : throw new InvalidOperationException("No generated V8 village found within 16 village cells.");
    }

    private Vector3 FindOcean(Vector3 origin)
    {
        return FindBiome(origin, 2048, biome => biome == Biome.Ocean, "ocean");
    }

    private Vector3 FindCave(Vector3 origin)
    {
        if (_world == null) throw new InvalidOperationException("World unavailable.");
        int originX = Mathf.FloorToInt(origin.X);
        int originZ = Mathf.FloorToInt(origin.Z);
        for (int radius = 0; radius <= 512; radius += 16)
        {
            for (int dz = -radius; dz <= radius; dz += 16)
            {
                for (int dx = -radius; dx <= radius; dx += 16)
                {
                    if (radius > 0 && Math.Abs(dx) != radius && Math.Abs(dz) != radius)
                        continue;
                    int x = originX + dx;
                    int z = originZ + dz;
                    int surface = _world.SurfaceHeight(x, z);
                    for (int depth = 16; depth <= 64; depth += 4)
                    {
                        int y = surface - depth;
                        if (y <= 4 || !_world.IsCave(x, y, z, surface)) continue;
                        if (_world.GetBlock(x, y, z) != BlockId.Air
                                || _world.GetBlock(x, y + 1, z) != BlockId.Air
                                || !BlockDb.DefOf(_world.GetBlock(x, y - 1, z)).Solid)
                            continue;
                        return new Vector3(x + 0.5f, y + 0.2f, z + 0.5f);
                    }
                }
            }
        }

        throw new InvalidOperationException("No walkable cave floor found within the 512-block search window.");
    }

    private void BuildRoute()
    {
        if (_world == null) throw new InvalidOperationException("World unavailable.");
        bool underground = _scenario == "deep-cave";
        for (int i = 0; i < RoutePointCount; i++)
        {
            float angle = Mathf.Tau * i / RoutePointCount;
            float x = _center.X + Mathf.Cos(angle) * _routeRadius;
            float z = _center.Z + Mathf.Sin(angle) * _routeRadius;
            float y = underground ? _center.Y
                    : _scenario == "ocean" ? _world.SeaLevel - 2.5f
                    : _world.SurfaceHeight(Mathf.FloorToInt(x), Mathf.FloorToInt(z)) + 1.0f;
            _route[i] = new Vector3(x, y, z);
        }
    }

    private void UpdateRoute()
    {
        if (_player == null) return;
        float circumference = Mathf.Tau * _routeRadius;
        float travelled = (float)(_routeElapsed * _routeSpeed);
        float distance = circumference > 0.0f ? travelled % circumference : 0.0f;
        float pointPosition = distance / circumference * RoutePointCount;
        int index = Mathf.FloorToInt(pointPosition) % RoutePointCount;
        float blend = pointPosition - Mathf.Floor(pointPosition);
        Vector3 from = _route[index];
        Vector3 to = _route[(index + 1) % RoutePointCount];
        _player.GlobalPosition = from.Lerp(to, blend);
        _player.Velocity = Vector3.Zero;
        Vector3 direction = to - from;
        if (direction.LengthSquared() > 0.0001f)
            _player.Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.X, -direction.Z), 0.0f);
    }

    private void BeginMeasurement()
    {
        _warmupElapsed = _warmupSeconds;
        _measurementElapsed = 0.0;
        _memorySampleElapsed = 0.0;
        Profiler.ResetBuildSamples();
        Profiler.ResetApplyStepSamples();
        _managedAllocatedAtStart = GC.GetTotalAllocatedBytes(false);
        _gc0AtStart = GC.CollectionCount(0);
        _gc1AtStart = GC.CollectionCount(1);
        _gc2AtStart = GC.CollectionCount(2);
        _measurementStartedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        CaptureMemoryAndSceneCounters();
        GD.Print("PERF BENCHMARK: measurement started");
    }

    private void CaptureFrame(double delta)
    {
        if (_world == null || _mobs == null) return;
        _frameElapsedSeconds.Add(_measurementElapsed);
        _frameTimesMs.Add(delta * 1000.0);
        _processTimesMs.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);
        _physicsTimesMs.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);
        _chunkCounts.Add(_world.ChunkCount);
        _freshQueues.Add(Profiler.FreshQueued);
        _rebuildQueues.Add(Profiler.RebuildQueued);
        _dataChunks.Add(Profiler.DataChunks);

        _maxChunkCount = Math.Max(_maxChunkCount, _world.ChunkCount);
        _maxFreshQueued = Math.Max(_maxFreshQueued, Profiler.FreshQueued);
        _maxRebuildQueued = Math.Max(_maxRebuildQueued, Profiler.RebuildQueued);
        _maxDataChunks = Math.Max(_maxDataChunks, Profiler.DataChunks);
        _maxDrawCalls = Math.Max(_maxDrawCalls,
                (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        _maxPrimitives = Math.Max(_maxPrimitives,
                (int)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        _workerWasActive |= Profiler.WorkersActive;
        _passiveCount = _mobs.PassiveCount;
        _hostileCount = _mobs.HostileCount;
    }

    private void CaptureMemoryAndSceneCounters()
    {
        if (_process == null) return;
        if (_world != null)
            Profiler.RecordCacheMemory(_world.GetCacheMemorySnapshot());
        _maxWorkingSetBytes = Math.Max(_maxWorkingSetBytes, _process.WorkingSet64);
        _maxManagedHeapBytes = Math.Max(_maxManagedHeapBytes, GC.GetTotalMemory(false));
        _maxStaticMemoryBytes = Math.Max(_maxStaticMemoryBytes,
                (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic));
        _maxVideoMemoryBytes = Math.Max(_maxVideoMemoryBytes,
                (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed));
        _maxNodeCount = Math.Max(_maxNodeCount,
                (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        _maxObjectCount = Math.Max(_maxObjectCount,
                (int)Performance.GetMonitor(Performance.Monitor.ObjectCount));
        _maxDrawCalls = Math.Max(_maxDrawCalls,
                (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        _maxPrimitives = Math.Max(_maxPrimitives,
                (int)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
    }

    private Task<int> Finish()
    {
        return BeginCompletion(WriteBenchmarkReportsAsync);
    }

    private Task WriteBenchmarkReportsAsync()
    {
        CaptureMemoryAndSceneCounters();
        string reportJson = BuildJsonReport();
        string frameCsv = BuildFrameCsv();
        string absoluteDirectory = ProjectSettings.GlobalizePath(OutputDirectory);
        Error directoryError = DirAccess.MakeDirRecursiveAbsolute(absoluteDirectory);
        if (directoryError != Error.Ok && !DirAccess.DirExistsAbsolute(absoluteDirectory))
            throw new IOException($"Could not create benchmark output directory: {directoryError}");

        _reportJsonPath = $"{OutputDirectory}/{_outputBase}.json";
        _reportCsvPath = $"{OutputDirectory}/{_outputBase}-frames.csv";
        if (!WriteText(_reportJsonPath, reportJson) || !WriteText(_reportCsvPath, frameCsv))
            throw new IOException("A benchmark report could not be written.");
        return Task.CompletedTask;
    }

    private Task<int> Fail(string message, Exception? cause = null)
    {
        Exception failure = cause == null
                ? new InvalidOperationException(message)
                : new InvalidOperationException(message, cause);
        return BeginCompletion(() => Task.FromException(failure));
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
                    WaitForSettleDelayAsync,
                    WaitForWorkerDrainAsync,
                    ReleaseSceneReferencesAsync,
                    CleanupWorldAsync,
                ],
                exception =>
                {
                    if (ReportFailureOverride != null)
                    {
                        ReportFailureOverride(exception);
                        return;
                    }

                    GD.PushError($"PERF BENCHMARK: completion failed: {exception}");
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
                    bool passed = exitCode == 0;
                    string report = _reportJsonPath.Length == 0
                            ? "unavailable"
                            : ProjectSettings.GlobalizePath(_reportJsonPath);
                    string frames = _reportCsvPath.Length == 0
                            ? "unavailable"
                            : ProjectSettings.GlobalizePath(_reportCsvPath);
                    GD.Print($"PERF BENCHMARK {(passed ? "PASS" : "INCOMPLETE")}: " +
                            $"scenario={_scenario} fps={_frameTimesMs.Count / Math.Max(0.001, _measurementElapsed):0.0} " +
                            $"p50={BenchmarkStatistics.Percentile(_frameTimesMs, 0.50):0.00}ms " +
                            $"p95={BenchmarkStatistics.Percentile(_frameTimesMs, 0.95):0.00}ms " +
                            $"p99={BenchmarkStatistics.Percentile(_frameTimesMs, 0.99):0.00}ms " +
                            $"rssMax={_maxWorkingSetBytes / 1048576.0:0}MiB report={report} frames={frames}");
                    SetProcess(false);
                });
        return _completionTask;
    }

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

    private async Task WaitForSettleDelayAsync()
    {
        if (WaitForSettleOverride != null)
        {
            await WaitForSettleOverride();
            return;
        }

        if (IsInsideTree())
            await ToSignal(GetTree().CreateTimer(0.25), Timer.SignalName.Timeout);
    }

    private bool WorkersDrained() => WorkersDrainedOverride?.Invoke()
            ?? (Profiler.PendingPayloadCount == 0 && Profiler.PendingPayloadBytes == 0
                && !Profiler.WorkersActive);

    // QueueFree waits for the world's worker group in VoxelWorld._ExitTree;
    // allow those frames to settle before releasing the scene and deleting the
    // temporary world.
    private async Task WaitForWorkerDrainAsync()
    {
        bool drained = WorkersDrained();
        for (int attempt = 0; attempt < DrainFrameLimit && !drained; attempt++)
        {
            await WaitForProcessFrameAsync();
            drained = WorkersDrained();
        }

        if (!drained)
            throw new InvalidOperationException(
                    "Meshing workers or completed payloads remained at benchmark shutdown.");
    }

    private Task ReleaseSceneReferencesAsync()
    {
        ReleaseSceneReferences();
        return Task.CompletedTask;
    }

    private Task CleanupWorldAsync()
    {
        if (!CleanupWorld())
            throw new IOException($"Failed to remove disposable benchmark world '{_worldDir}'.");
        return Task.CompletedTask;
    }

    private string BuildJsonReport()
    {
        double meanFrameMs = Mean(_frameTimesMs);
        BuildTimeStats mesh = Profiler.BuildDistribution();
        BuildTimeStats applySteps = Profiler.ApplyStepDistribution();
        BuildTimeStats collisionSteps = Profiler.CollisionDistribution();
        string runStamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        _outputBase = $"perf-{_scenario}-{_seed}-{runStamp}";
        var report = new
        {
            schemaVersion = 1,
            runId = _outputBase,
            startedAtUtc = _measurementStartedAtUtc,
            endedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            commit = _commit,
            buildConfiguration = _build,
            godotVersion = Engine.GetVersionInfo()["string"].ToString(),
            operatingSystem = OS.GetName(),
            cpu = OS.GetProcessorName(),
            logicalCpuCount = OS.GetProcessorCount(),
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            renderingDriver = RenderingServer.GetCurrentRenderingDriverName(),
            displayServer = DisplayServer.GetName(),
            headless = _headless,
            renderingHardwareAvailable = !_headless,
            gpu = _headless ? null : RenderingServer.GetVideoAdapterName(),
            gpuVendor = _headless ? null : RenderingServer.GetVideoAdapterVendor(),
            resolution = _headless ? "not-applicable" : DisplayServer.WindowGetSize().ToString(),
            vsyncMode = _headless ? "not-applicable"
                    : DisplayServer.WindowGetVsyncMode().ToString(),
            maxFpsLimit = _headless ? Engine.MaxFps : 0,
            graphicsPreset = Gfx.Presets[_preset].Name,
            graphicsPresetIndex = _preset,
            viewDistance = _viewDistance,
            greedyMeshing = Settings.GreedyMeshing,
            autoQuality = Settings.AutoQuality,
            worldSeed = _seed,
            worldGenerationVersion = _world?.WorldGenVersion ?? -1,
            scenario = _scenario,
            scenarioNote = _scenarioNote,
            scenarioCenter = new { x = _center.X, y = _center.Y, z = _center.Z },
            cameraRoute = new
            {
                pointCount = RoutePointCount,
                radiusMetres = _routeRadius,
                speedMetresPerSecond = _routeSpeed,
            },
            scenarioState = new
            {
                weather = _weather?.Current.ToString() ?? "unavailable",
                playerInWater = _player?.InWater ?? false,
                playerUnderwater = _player?.Underwater ?? false,
                passiveCreatures = _passiveCount,
                hostileCreatures = _hostileCount,
                requestedHostileCreaturesSpawned = _requestedHostileCount,
                difficulty = Settings.DifficultyNames[Settings.Difficulty],
            },
            warmupSeconds = _warmupSeconds,
            measuredSeconds = _measurementElapsed,
            frameCount = _frameTimesMs.Count,
            frameTimeMs = new
            {
                average = meanFrameMs,
                p50 = BenchmarkStatistics.Percentile(_frameTimesMs, 0.50),
                p95 = BenchmarkStatistics.Percentile(_frameTimesMs, 0.95),
                p99 = BenchmarkStatistics.Percentile(_frameTimesMs, 0.99),
                worst = Maximum(_frameTimesMs),
                averageFps = _frameTimesMs.Count / Math.Max(0.001, _measurementElapsed),
            },
            engineTimingMs = new
            {
                processP50 = BenchmarkStatistics.Percentile(_processTimesMs, 0.50),
                processP95 = BenchmarkStatistics.Percentile(_processTimesMs, 0.95),
                processP99 = BenchmarkStatistics.Percentile(_processTimesMs, 0.99),
                physicsP50 = BenchmarkStatistics.Percentile(_physicsTimesMs, 0.50),
                physicsP95 = BenchmarkStatistics.Percentile(_physicsTimesMs, 0.95),
                physicsP99 = BenchmarkStatistics.Percentile(_physicsTimesMs, 0.99),
            },
            rendering = new
            {
                maxDrawCallsInFrame = _maxDrawCalls,
                maxPrimitivesInFrame = _maxPrimitives,
                maxVideoMemoryBytes = _maxVideoMemoryBytes,
                gpuFrameTimeMs = (double?)null,
                gpuFrameTimeNote = "Godot's built-in Performance monitors do not expose a portable GPU frame-time counter.",
            },
            memory = new
            {
                resourceSampleIntervalSeconds = 1.0,
                maxProcessRssBytes = _maxWorkingSetBytes,
                maxManagedHeapBytes = _maxManagedHeapBytes,
                allocatedManagedBytesDuringMeasurement =
                        Math.Max(0L, GC.GetTotalAllocatedBytes(false) - _managedAllocatedAtStart),
                maxGodotStaticMemoryBytes = _maxStaticMemoryBytes,
                godotStaticMemoryNote = "Godot reports this monitor in Debug builds only; Release reports zero.",
                gc0Collections = GC.CollectionCount(0) - _gc0AtStart,
                gc1Collections = GC.CollectionCount(1) - _gc1AtStart,
                gc2Collections = GC.CollectionCount(2) - _gc2AtStart,
                voxelArrayBytes = Profiler.VoxelCacheArrayBytes,
                voxelArrayHighWaterBytes = Profiler.VoxelCacheArrayHighWaterBytes,
                generatorSeedArrayBytes = Profiler.GeneratorSeedArrayBytes,
                generatorSeedArrayHighWaterBytes = Profiler.GeneratorSeedArrayHighWaterBytes,
                voxelArrayBudgetBytes = Profiler.VoxelCacheBudgetBytes,
                cacheIndexBytesEstimate = Profiler.CacheIndexBytesEstimate,
                cacheIndexHighWaterBytesEstimate = Profiler.CacheIndexBytesHighWaterEstimate,
                cropRegistryBytesEstimate = Profiler.CropRegistryBytesEstimate,
                cropRegistryHighWaterBytesEstimate = Profiler.CropRegistryBytesHighWaterEstimate,
                sourceScanRegistryBytesEstimate = Profiler.SourceScanRegistryBytesEstimate,
                sourceScanRegistryHighWaterBytesEstimate = Profiler.SourceScanRegistryBytesHighWaterEstimate,
                cropScanRegistryBytesEstimate = Profiler.CropScanRegistryBytesEstimate,
                cropScanRegistryHighWaterBytesEstimate = Profiler.CropScanRegistryBytesHighWaterEstimate,
                campfireSiteCacheBytesEstimate = Profiler.CampfireSiteCacheBytesEstimate,
                campfireSiteCacheHighWaterBytesEstimate = Profiler.CampfireSiteCacheBytesHighWaterEstimate,
                structureSiteCacheBytesEstimate = Profiler.StructureSiteCacheBytesEstimate,
                structureSiteCacheHighWaterBytesEstimate = Profiler.StructureSiteCacheBytesHighWaterEstimate,
                villageSiteCacheBytesEstimate = Profiler.VillageSiteCacheBytesEstimate,
                villageSiteCacheHighWaterBytesEstimate = Profiler.VillageSiteCacheBytesHighWaterEstimate,
                editTopArrayBytes = Profiler.EditTopArrayBytes,
                editTopArrayHighWaterBytes = Profiler.EditTopArrayHighWaterBytes,
                editTopIndexBytesEstimate = Profiler.EditTopIndexBytesEstimate,
                editTopIndexHighWaterBytesEstimate = Profiler.EditTopIndexBytesHighWaterEstimate,
                worldStateRegistryBytesEstimate = Profiler.WorldStateRegistryBytesEstimate,
                worldStateRegistryHighWaterBytesEstimate = Profiler.WorldStateRegistryBytesHighWaterEstimate,
                chunkIndexBytesEstimate = Profiler.ChunkIndexBytesEstimate,
                chunkIndexHighWaterBytesEstimate = Profiler.ChunkIndexBytesHighWaterEstimate,
                editMetadataBytesEstimate = Profiler.EditMetadataBytesEstimate,
                editMetadataHighWaterBytesEstimate = Profiler.EditMetadataBytesHighWaterEstimate,
                lightSourceBytesEstimate = Profiler.LightSourceBytesEstimate,
                lightSourceHighWaterBytesEstimate = Profiler.LightSourceBytesHighWaterEstimate,
                appliedMeshBytesEstimate = Profiler.AppliedMeshBytesEstimate,
                appliedMeshHighWaterBytesEstimate = Profiler.AppliedMeshBytesHighWaterEstimate,
                pooledMeshBufferBytes = Profiler.PooledMeshBufferBytes,
                pooledMeshBufferHighWaterBytes = Profiler.PooledMeshBufferHighWaterBytes,
                streamingQueueBytesEstimate = Profiler.StreamingQueueBytesEstimate,
                streamingQueueHighWaterBytesEstimate = Profiler.StreamingQueueBytesHighWaterEstimate,
                persistentMetadataBytesEstimate = Profiler.PersistentMetadataBytesEstimate,
                persistentMetadataHighWaterBytesEstimate = Profiler.PersistentMetadataBytesHighWaterEstimate,
                trackedManagedBytesEstimate = Profiler.TrackedManagedBytesEstimate,
                trackedManagedHighWaterBytesEstimate = Profiler.TrackedManagedBytesHighWaterEstimate,
                cacheAccountingNote = VoxelCacheMemorySnapshot.AccountingNote,
            },
            sceneAndStreaming = new
            {
                maxSceneNodes = _maxNodeCount,
                maxGodotObjects = _maxObjectCount,
                maxRenderedChunks = _maxChunkCount,
                maxDataChunks = _maxDataChunks,
                maxFreshQueueDepth = _maxFreshQueued,
                maxRebuildQueueDepth = _maxRebuildQueued,
                anyWorkerActiveDuringMeasurement = _workerWasActive,
                finalPassiveCreatures = _passiveCount,
                finalHostileCreatures = _hostileCount,
            },
            meshBuildsLastWindow = new
            {
                count = mesh.Count,
                averageMs = mesh.AverageMs,
                p50Ms = mesh.P50Ms,
                p95Ms = mesh.P95Ms,
                p99Ms = mesh.P99Ms,
                maxMs = mesh.MaxMs,
            },
            chunkPayloadApply = new
            {
                cooperativeBudgetMs = 2.0,
                maximumVerticesPerResourceStep = StreamingApplyBudget.MaxVerticesPerStep,
                totalPayloadAverageMs = Profiler.ApplyAverageMs,
                totalPayloadMaximumMs = Profiler.ApplyMaximumMs,
                resourceSteps = new
                {
                    count = applySteps.Count,
                    averageMs = applySteps.AverageMs,
                    p50Ms = applySteps.P50Ms,
                    p95Ms = applySteps.P95Ms,
                    p99Ms = applySteps.P99Ms,
                    maxMs = applySteps.MaxMs,
                },
                collisionSteps = new
                {
                    count = collisionSteps.Count,
                    averageMs = collisionSteps.AverageMs,
                    p50Ms = collisionSteps.P50Ms,
                    p95Ms = collisionSteps.P95Ms,
                    p99Ms = collisionSteps.P99Ms,
                    maxMs = collisionSteps.MaxMs,
                    totalVertices = Profiler.CollisionVerticesApplied,
                },
                note = "The 2 ms budget is checked between bounded steps; one Godot resource creation call may exceed it.",
            },
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildFrameCsv()
    {
        StringBuilder csv = new();
        csv.AppendLine("elapsed_seconds,frame_ms,process_ms,physics_ms,rendered_chunks,data_chunks,fresh_queue,rebuild_queue");
        int count = _frameTimesMs.Count;
        for (int i = 0; i < count; i++)
        {
            csv.Append(_frameElapsedSeconds[i].ToString("F4", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(_frameTimesMs[i].ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(_processTimesMs[i].ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(_physicsTimesMs[i].ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(_chunkCounts[i]).Append(',')
                .Append(_dataChunks[i]).Append(',')
                .Append(_freshQueues[i]).Append(',')
                .Append(_rebuildQueues[i]).AppendLine();
        }

        return csv.ToString();
    }

    private static bool WriteText(string path, string content)
    {
        using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file == null) return false;
        file.StoreString(content);
        file.Flush();
        return file.GetError() == Error.Ok;
    }

    private bool CleanupWorld()
    {
        if (_worldDir.Length == 0) return true;
        string directory = _worldDir;
        bool deleted = DeleteWorldOverride?.Invoke(directory) ?? WorldStore.Delete(directory);
        if (deleted) _worldDir = "";
        else GD.PushError($"PERF BENCHMARK: failed to remove disposable world '{directory}'.");
        return deleted;
    }

    private void ReleaseSceneReferences()
    {
        _main = null;
        _world = null;
        _player = null;
        _weather = null;
        _mobs = null;
        Process? process = _process;
        _process = null;
        process?.Dispose();
    }

    public override void _ExitTree()
    {
        try
        {
            ReleaseSceneReferences();
        }
        catch (Exception exception)
        {
            GD.PushError($"PERF BENCHMARK: emergency reference release failed: {exception}");
        }

        try
        {
            CleanupWorld();
        }
        catch (Exception exception)
        {
            GD.PushError($"PERF BENCHMARK: emergency world cleanup failed: {exception}");
        }
    }

    private static double Mean(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        double sum = 0.0;
        foreach (double value in values) sum += value;
        return sum / values.Count;
    }

    private static double Maximum(List<double> values)
    {
        double maximum = 0.0;
        foreach (double value in values) maximum = Math.Max(maximum, value);
        return maximum;
    }
}
