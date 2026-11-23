using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Godot;

namespace Minecraft;

/// <summary>
/// Voxel world: deterministic terrain generation, chunk streaming,
/// block lookup / modification, player-edit persistence.
///
/// Threading model
/// ---------------
///  - <c>_dataCache</c> holds the block data of every chunk the world knows
///    about (generated lazily). It is guarded by <c>_mutex</c>.
///  - Worker threads (WorkerThreadPool) run Mesher builds. They read data
///    through <c>ChunkData()</c>, which is thread-safe, and never touch the
///    scene tree. Results come back deferred and are applied on the
///    main thread (meshes + Jolt colliders must be created there).
///  - <c>SnapshotLock</c> serialises block edits against mesher snapshots so a
///    payload is either consistent with <c>EditEpoch</c> or discarded.
/// </summary>
public partial class VoxelWorld : Node3D
{
    public const int ChunkX = 16;
    public const int ChunkZ = 16;

    /// <summary>World height in blocks: 64 for gen v1–v4 (byte-parity), 128 for v5+.</summary>
    public int Height { get; private set; } = 64;

    private const int UnloadMargin = 2;
    private static readonly bool UseThreads = true;

    public int ViewDistance = 5;        // chunk radius streamed around the player (gfx presets)
    public int MaxInFlight = 6;         // concurrent mesher workers (gfx presets)
    public bool BuildCollision = true;  // false for render-only previews such as the main menu
    public float LodRadius;             // far-view LOD ring radius in metres (0 = off)
    private FarLod? _farLod;            // FarLod ring node (created in _Ready)

    private const double AutosaveInterval = 4.0;

    private string _worldDir = "";
    private WorldManifest _manifest = null!;
    private int _genVersion = 1;
    private int[] _noiseSeeds = [];
    private string _preset = "normal";  // v5+: normal / flat / amplified / sky

    public int WorldSeed => _manifest.Seed;

    public int WorldGenVersion => _genVersion;

    public int SeaLevel = 28;

    private readonly FastNoiseLite _baseNoise = new();
    private readonly FastNoiseLite _detailNoise = new();
    private readonly FastNoiseLite _caveNoise = new();
    private readonly FastNoiseLite _tempNoise = new();
    private readonly FastNoiseLite _moistNoise = new();

    // Gen v4 layers: relief/continents, rivers, biome warp and caverns.
    // Seeds derive from the master seed pair with frozen salts — no manifest
    // format change.
    private readonly FastNoiseLite _reliefNoise = new();
    private readonly FastNoiseLite _riverNoise = new();
    private readonly FastNoiseLite _warpNoise = new();
    private readonly FastNoiseLite _cavernNoise = new();
    private readonly FastNoiseLite _islandNoise = new();   // gen v5 sky-island mask

    private readonly Dictionary<Vector2I, Chunk> _chunks = new();  // built nodes
    public Player? Player;

    /// <summary>Raised after a successful main-thread block mutation.</summary>
    public event Action<Vector3I>? BlockChanged;
    /// <summary>Systems that cache live state (villagers) flush it before serialization.</summary>
    public event Action? BeforeSave;

    public Vector3? SavedPlayerPos;      // from the save file
    public List<ItemStackSave>? SavedInventory;   // null = pre-items save, gets the starter kit
    public int SavedSelected;
    public string? SavedMode;            // "survival"/"creative"; null = world default
    public float SavedHealth;            // 0 = unset, full bars
    public float SavedHunger = -1.0f;
    public int SavedExperience;
    public Vector3? SavedRespawn;        // campfire anchor, null = world spawn
    public List<BoatSave>? SavedBoats;   // optional vehicle state; absent in older saves
    public BoatSystem? BoatSystem;

    // Save v3: per-block inventories (chests/furnaces) and one-time world
    // flags (dungeon clears, achievements). Keys are stable block coordinates.
    public readonly Dictionary<string, Container> Containers = [];
    public readonly Dictionary<string, FurnaceData> Furnaces = [];
    public readonly Dictionary<string, bool> Flags = [];
    public readonly Dictionary<string, VillagerSave> Villagers = [];
    public readonly Dictionary<string, QuestProgressSave> Quests = [];

    /// <summary>New worlds default to survival; pre-M2 worlds stay creative.</summary>
    public string DefaultMode => _manifest.Mode == "survival" ? "survival" : "creative";

    public long EditEpoch;               // bumped on every block edit
    public double TimeOfDay = 0.38;      // 0..1, 0.5 = noon (DayNight drives this)

    public int ChunkCount => _chunks.Count;

    public bool HasChunk(Vector2I cp) => _chunks.ContainsKey(cp);

    private readonly Dictionary<Vector2I, byte[]> _dataCache = new();
    // Lazy reservations coalesce concurrent cache misses without holding the
    // world mutex during expensive terrain generation.
    private readonly ChunkGenerationReservations _generationInFlight = new();
    private readonly Dictionary<Vector2I, Dictionary<int, BlockId>> _edits = new();  // persisted block IDs
    private readonly Dictionary<Vector2I, Dictionary<int, byte>> _blockStateEdits = new(); // optional persisted shape state
    private readonly Dictionary<Vector2I, byte[]> _blockStateCache = new();
    private readonly Dictionary<Vector3I, byte> _lightSources = [];  // all block emitters, 0..15
    private readonly HashSet<Vector2I> _sourceScannedChunks = [];
    private readonly Dictionary<Vector2I, byte[]> _skyLightCache = [];
    private readonly Dictionary<Vector2I, byte[]> _blockLightCache = [];
    private readonly Dictionary<Vector2I, VoxelLightData> _voxelLightCache = [];
    private readonly HashSet<Vector3I> _crops = [];  // generated and placed wheat, for growth ticks
    private readonly HashSet<Vector2I> _cropScannedChunks = [];
    private readonly Lock _mutex = new();           // guards _dataCache, _edits, EditEpoch
    public Lock SnapshotLock { get; } = new();      // serialises edits vs mesher snapshots

    // Nearest-first build queues: .NET's PriorityQueue replaces the
    // "sort a candidate list" bookkeeping, and the side sets keep them unique.
    // Fresh chunks (streaming) and rebuilds (player edits) are separate so an
    // edit never waits behind hundreds of streamed chunks. Both are guarded by
    // _queueLock because worker-loop threads dequeue while the main thread
    // enqueues.
    private readonly PriorityQueue<Vector2I, float> _queue = new();
    private readonly HashSet<Vector2I> _queued = new();
    private readonly PriorityQueue<Vector2I, float> _rebuildQueue = new();
    private readonly HashSet<Vector2I> _rebuildQueued = new();
    private readonly Lock _queueLock = new();
    private readonly ChunkWorkTracker _workTracker = new();
    private readonly Dictionary<Vector2I, int> _workerFailureAttempts = new();
    private const int MaxWorkerBuildAttempts = 3;
    private Vector2I _streamCenter;
    private int _streamRetentionLimit;
    private long _group;                    // active WorkerThreadPool group
    private bool _groupActive;
    // Worker payloads wait here until the main thread applies them, a few per
    // frame — that keeps mesh/collider creation off the streaming burst's
    // critical path. Rebuilds (player edits) are applied before fresh chunks.
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Vector2I Cp, MeshPayload Payload)> _applyFresh = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Vector2I Cp, MeshPayload Payload)> _applyRebuild = new();
    // Maximum completed payloads matches the old worst-case threshold
    // (6 workers × 3 payloads). Workers reserve a slot before building and
    // wait on this semaphore instead of polling/sleeping when the main thread
    // falls behind.
    private const int MaxCompletedPayloads = StreamingApplyBudget.MaxPendingPayloads;
    private const double ApplyBudgetMilliseconds = 2.0;
    private const double MemorySampleInterval = 1.0;
    private readonly SemaphoreSlim _completedPayloadSlots = new(MaxCompletedPayloads, MaxCompletedPayloads);
    private readonly CancellationTokenSource _workerShutdown = new();
    private double _memorySampleTimer;
    private Vector3 _velocityDir;           // smoothed player velocity (prefetch bias)
    private Vector3 _lastPriorityDirection;
    private Vector3 _lastPlayerPos;
    private Vector2I _lastPlayerChunk = new(999999, 999999);
    private Vector3 _lastSavedPos = Vector3.Zero;
    private volatile bool _alive = true;
    private bool _dirty;
    private double _autosaveTimer;
    private double _unsavedPlaytimeSeconds;
    private string? _worldReadOnlyReason;
    private bool _readOnlyWarningShown;

    public bool IsReadOnly => _worldReadOnlyReason != null;

    public override void _Ready()
    {
        ResolveWorld();
        BlockDb.EnsureTables();

        // Meshing mode is a static consumed by worker threads; set it before
        // any chunk is built.
        Mesher.Greedy = Settings.GreedyMeshing;

        _baseNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _baseNoise.Seed = _noiseSeeds[0];
        _baseNoise.Frequency = 0.009f;
        _baseNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _baseNoise.FractalOctaves = 4;
        _baseNoise.FractalLacunarity = 2.0f;
        _baseNoise.FractalGain = 0.5f;

        _detailNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _detailNoise.Seed = _noiseSeeds[1];
        _detailNoise.Frequency = 0.045f;
        _detailNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _detailNoise.FractalOctaves = 2;

        _caveNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _caveNoise.Seed = _noiseSeeds[2];
        _caveNoise.Frequency = 0.06f;
        _caveNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _caveNoise.FractalOctaves = 2;

        _tempNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _tempNoise.Seed = _noiseSeeds[3];
        _tempNoise.Frequency = 0.0035f;
        _tempNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _tempNoise.FractalOctaves = 3;

        _moistNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _moistNoise.Seed = _noiseSeeds[4];
        _moistNoise.Frequency = 0.005f;
        _moistNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _moistNoise.FractalOctaves = 3;

        _reliefNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _reliefNoise.Seed = DeriveSeed(0x52454C46u);
        _reliefNoise.Frequency = 0.0016f;
        _reliefNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _reliefNoise.FractalOctaves = 4;
        _reliefNoise.FractalLacunarity = 2.0f;
        _reliefNoise.FractalGain = 0.5f;

        _riverNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _riverNoise.Seed = DeriveSeed(0x52495652u);
        _riverNoise.Frequency = 0.0021f;
        _riverNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _riverNoise.FractalOctaves = 2;

        _warpNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _warpNoise.Seed = DeriveSeed(0x57415250u);
        _warpNoise.Frequency = 0.004f;
        _warpNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _warpNoise.FractalOctaves = 2;

        _cavernNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _cavernNoise.Seed = DeriveSeed(0x43415645u);
        _cavernNoise.Frequency = 0.018f;
        _cavernNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _cavernNoise.FractalOctaves = 2;

        _islandNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _islandNoise.Seed = DeriveSeed(0x49534C44u);
        _islandNoise.Frequency = 0.008f;
        _islandNoise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _islandNoise.FractalOctaves = 2;

        // V7 and V8 own independent noise resources; legacy noise stays frozen.
        InitializeWorldGenV7();
        InitializeWorldGenV8();

        _farLod = new FarLod();
        _farLod.Name = "FarLod";
        AddChild(_farLod);
        _farLod.Setup(this);

        LoadSave();
    }

    public override void _ExitTree()
    {
        _alive = false;
        _workerShutdown.Cancel();
        if (_groupActive)
        {
            WorkerThreadPool.WaitForGroupTaskCompletion(_group);
            _groupActive = false;
        }

        // Workers have stopped, so it is now safe to return unconsumed pooled
        // payloads and release the reservations held for those payloads.
        DrainPendingPayloads(_applyRebuild);
        DrainPendingPayloads(_applyFresh);
        Profiler.WorkersActive = false;
        lock (_queueLock)
        {
            Profiler.FreshQueued = 0;
            Profiler.RebuildQueued = 0;
        }
        _completedPayloadSlots.Dispose();
        _workerShutdown.Dispose();
    }

    private void DrainPendingPayloads(
            System.Collections.Concurrent.ConcurrentQueue<(Vector2I Cp, MeshPayload Payload)> queue)
    {
        while (queue.TryDequeue(out (Vector2I Cp, MeshPayload Payload) item))
        {
            Profiler.RecordPayloadDequeued(item.Payload.EstimatedBufferBytes);
            PayloadPool.Release(item.Payload);
            _completedPayloadSlots.Release();
        }
    }

    /// <summary>Bind to the GameSession world: folder, manifest, seeds, gen version.</summary>
    private void ResolveWorld()
    {
        GameSession? session = GameSession.Instance;
        string selectedDir = session?.WorldDir ?? "";
        if (selectedDir.Length > 0 && WorldStore.ReadManifest(selectedDir) == null
                && HasWorldMetadataOrSaveFiles(selectedDir))
        {
            throw new InvalidOperationException(
                    $"World '{selectedDir}' has save data but no readable manifest. " +
                    "The files were left untouched; restore a compatible manifest before opening it.");
        }

        string dir = session?.EnsureWorld() ?? "";
        if (dir.Length == 0)
        {
            dir = $"{WorldStore.WorldsRoot}/default";
        }

        _worldDir = dir;
        WorldManifest? manifest = WorldStore.ReadManifest(dir);
        if (manifest == null)
        {
            if (HasWorldMetadataOrSaveFiles(dir))
            {
                throw new InvalidOperationException(
                        $"World '{dir}' has existing data but no readable manifest. " +
                        "The files were left untouched.");
            }

            manifest = WorldStore.DefaultFor(dir);
        }

        if (!WorldCompatibilityPolicy.IsSupportedGeneratorVersion(
                manifest.GenVersion, WorldStore.CurrentGenVersion))
        {
            throw new InvalidOperationException(
                    $"World '{manifest.Name}' uses generator V{manifest.GenVersion}; " +
                    $"this build supports V1–V{WorldStore.CurrentGenVersion}. " +
                    "No world files were modified.");
        }

        if (!WorldCompatibilityPolicy.HasValidNoiseSeeds(manifest.NoiseSeeds))
        {
            throw new InvalidOperationException(
                    $"World '{manifest.Name}' has incomplete generator noise metadata. " +
                    "No replacement seeds were generated and no world files were modified.");
        }

        _manifest = manifest;
        _genVersion = _manifest.GenVersion;
        _noiseSeeds = (int[])_manifest.NoiseSeeds.Clone();

        // Gen v5 worlds are taller and can carry a shape preset; earlier gens
        // keep Height 64 and sea level 28 so their terrain stays byte-identical.
        Height = _genVersion >= 5 ? 128 : 64;
        SeaLevel = _genVersion >= 5 ? 40 : 28;
        _preset = _genVersion >= 5 ? _manifest.Preset ?? "normal" : "normal";
    }

    private static bool HasWorldMetadataOrSaveFiles(string dir)
    {
        bool manifestExists = HasSaveCandidate(WorldStore.ManifestPath(dir));
        bool worldSaveExists = HasSaveCandidate(WorldStore.WorldSavePath(dir));
        bool playerSaveExists = HasSaveCandidate(WorldStore.PlayerSavePath(dir));
        return WorldCompatibilityPolicy.MustRejectMissingManifest(
                manifestExists, worldSaveExists, playerSaveExists);
    }

    // -----------------------------------------------------------------------
    // Terrain generation (pure functions)
    // -----------------------------------------------------------------------

    /// <summary>Frozen seed derivation for the v4 noise layers (from the master seed pair).</summary>
    private int DeriveSeed(uint salt) =>
        unchecked((int)Mix((uint)_noiseSeeds[0], (uint)_noiseSeeds[1], salt, 0x9E3779B9u));

    public (int Height, Biome Biome) SurfaceSampleAt(int wx, int wz)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            WorldGenV7.SurfaceSample sample = GeneratorV8.SampleSurface(wx, wz, SeaLevel);
            return (sample.Height, sample.Biome);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            WorldGenV7.SurfaceSample sample = GeneratorV7.SampleSurface(wx, wz, SeaLevel);
            return (sample.Height, sample.Biome);
        }

        int height = SurfaceHeight(wx, wz);
        return (height, BiomeAt(wx, wz, height));
    }

    public int SurfaceHeight(int wx, int wz)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            return GeneratorV8.SurfaceHeight(wx, wz, SeaLevel);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            return GeneratorV7.SurfaceHeight(wx, wz, SeaLevel);
        }

        if (_genVersion >= 5)
        {
            // v5: taller worlds (Height 128) with selectable shape presets.
            double b5 = _baseNoise.GetNoise2D(wx, wz);
            double r5 = _reliefNoise.GetNoise2D(wx, wz);
            double d5 = _detailNoise.GetNoise2D(wx, wz);
            switch (_preset)
            {
                case "flat":
                    return SeaLevel + 4;
                case "sky":
                    return Mathf.Clamp(Mathf.RoundToInt((float)(74.0 + b5 * 8.0 + r5 * 20.0 + d5 * 3.0)), 46, 112);
            }

            double relief = _preset == "amplified" ? 44.0 : 30.0;
            double baseH = _preset == "amplified" ? 54.0 : 52.0;
            double h5 = CarveRiver(wx, wz, baseH + b5 * 6.0 + relief * r5 + d5 * 3.0);
            return Mathf.Clamp(Mathf.RoundToInt((float)h5), 3, 118);
        }

        if (_genVersion >= 4)
        {
            // v4: continents/hills from the relief layer over the rolling base,
            // plus winding river channels carved to below sea level.
            double b = _baseNoise.GetNoise2D(wx, wz);
            double r = _reliefNoise.GetNoise2D(wx, wz);
            double d = _detailNoise.GetNoise2D(wx, wz);
            double h = CarveRiver(wx, wz, 35.0 + b * 6.0 + r * 18.0 + d * 2.0);
            return Mathf.Clamp(Mathf.RoundToInt((float)h), 3, 50);
        }

        double b1 = _baseNoise.GetNoise2D(wx, wz);
        double d1 = _detailNoise.GetNoise2D(wx, wz);
        int h1 = Mathf.RoundToInt(29.0 + b1 * 13.0 + d1 * 2.0);
        return Mathf.Clamp(h1, 3, 44);
    }

    /// <summary>Winding river channels carved to just below sea level.</summary>
    private double CarveRiver(int wx, int wz, double h)
    {
        double ridge = 1.0 - System.Math.Abs(_riverNoise.GetNoise2D(wx, wz));
        if (ridge > 0.90)
        {
            double carve = (ridge - 0.90) / 0.10;      // 0 at bank, 1 at channel centre
            h = System.Math.Min(h, SeaLevel + 0.5 - carve * 3.0);
        }

        return h;
    }

    public bool IsCave(int x, int y, int z, int h)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            return GeneratorV8.IsCave(x, y, z, h);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            return GeneratorV7.IsCave(x, y, z, h);
        }

        if (y <= 1 || _preset == "flat")
        {
            return false;
        }

        double n = _caveNoise.GetNoise3D(x, y, z);
        if (_genVersion >= 4)
        {
            // Rare large caverns deep down (taller worlds reach deeper).
            int cavernTop = _genVersion >= 5 ? 50 : 26;
            if (y >= 5 && y <= cavernTop && _cavernNoise.GetNoise3D(x, y, z) > 0.52)
            {
                return true;
            }

            // Guaranteed-ish surface entrances: occasional holes in the top
            // two layers of dry land so caves can always be found from above.
            if (h > SeaLevel && y >= h - 1 && y <= h && n > 0.52)
            {
                return true;
            }
        }

        if (h <= SeaLevel)
        {
            if (y > h - 5)
            {
                return false;
            }

            return n > 0.44;
        }

        if (y >= h - 1)
        {
            return n > 0.58;
        }

        return n > 0.44;
    }

    /// <summary>Temperature/moisture biome map (pure function of x,z).</summary>
    public Biome BiomeAt(int x, int z) => BiomeAt(x, z, SurfaceHeight(x, z));

    /// <summary>Biome with a known surface height — skips the internal
    /// SurfaceHeight call for callers that already paid for it (minimap,
    /// decorators), which halves the noise work per column.</summary>
    public Biome BiomeAt(int x, int z, int h)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            return GeneratorV8.BiomeAt(x, z, h, SeaLevel);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            return GeneratorV7.BiomeAt(x, z, h, SeaLevel);
        }

        if (_genVersion >= 4)
        {
            // Domain-warped climate: biome edges wander instead of grid-locking,
            // and high mountains get snow caps (taller for gen v5 worlds).
            double w = _warpNoise.GetNoise2D(x, z) * 24.0;
            double t = _tempNoise.GetNoise2D((float)(x + w), (float)(z - w));
            double m = _moistNoise.GetNoise2D((float)(x - w), (float)(z + w));
            double snowAt = _genVersion >= 5
                    ? _preset switch
                    {
                        "sky" => 98.0,
                        "amplified" => 86.0,
                        _ => 80.0,
                    }
                    : 44.0;
            if (t < -0.32 || h > snowAt)
            {
                return Biome.Snow;
            }

            if (t > 0.25 && m < 0.15)
            {
                return Biome.Desert;
            }

            if (m > 0.30)
            {
                return Biome.Birch;
            }

            return Biome.Plains;
        }

        double t0 = _tempNoise.GetNoise2D(x, z);
        double m0 = _moistNoise.GetNoise2D(x, z);
        if (t0 < -0.35)
        {
            return Biome.Snow;
        }

        if (t0 > 0.25 && m0 < 0.15)
        {
            return Biome.Desert;
        }

        if (m0 > 0.30)
        {
            return Biome.Birch;
        }

        return Biome.Plains;
    }

    private BlockId OreAt(int x, int y, int z)
    {
        long r = Hash3(x, y, z);
        int diamondTop = _genVersion >= 5 ? 26 : 16;
        int goldTop = _genVersion >= 5 ? 46 : 28;
        int coalTop = _genVersion >= 5 ? 66 : 40;
        int ironTop = _genVersion >= 5 ? 48 : 30;
        if (y < diamondTop && r % 491 < 1)
        {
            return BlockId.DiamondOre;
        }

        if (y < goldTop && r % 331 < 1)
        {
            return BlockId.GoldOre;
        }

        if (y < coalTop && r % 97 < 2)
        {
            return BlockId.CoalOre;
        }

        if (y < ironTop && r % 211 < 1)
        {
            return BlockId.IronOre;
        }

        return BlockId.Stone;
    }

    /// <summary>Terrain + caves + ores for one block (no trees).</summary>
    private BlockId BlockAt(int x, int y, int z, int h, bool beach, Biome biome)
    {
        if (y < 0 || y >= Height)
        {
            return BlockId.Air;
        }

        if (_genVersion >= WorldGenV8.Version)
        {
            return BlockAtV8(x, y, z, h, biome);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            return BlockAtV7(x, y, z, h, biome);
        }

        if (_preset == "sky")
        {
            // Floating islands: a soft mask decides where land exists, and each
            // island is a ~26-block slab floating in the sky (no bedrock, no sea).
            if (y == 0 || !IslandAt(x, z) || y < h - 26)
            {
                return BlockId.Air;
            }
        }
        else if (y == 0)
        {
            return BlockId.Bedrock;
        }

        if (y > h)
        {
            return y <= SeaLevel ? BlockId.Water : BlockId.Air;
        }

        BlockId id = BlockId.Stone;
        bool sandy = beach || biome == Biome.Desert;
        if (y == h)
        {
            if (biome == Biome.Snow && !beach)
            {
                id = BlockId.SnowGrass;
            }
            else
            {
                id = sandy ? BlockId.Sand : BlockId.Grass;
            }
        }
        else if (y > h - 4)
        {
            id = sandy ? BlockId.Sand : BlockId.Dirt;
        }

        if (id == BlockId.Stone)
        {
            id = OreAt(x, y, z);
        }

        if (IsCave(x, y, z, h))
        {
            if (_genVersion >= 4)
            {
                // Liquids pool at cave cores (deep inside a carve blob) and in a
                // molten floor near bedrock — one noise drives both the carve and
                // the fill, so liquids always sit inside real caves.
                double core = _caveNoise.GetNoise3D(x, y, z);
                if (y <= 4 || (y <= 8 && core > 0.55))
                {
                    return BlockId.Lava;
                }

                int pocketTop = _genVersion >= 5 ? 44 : 22;
                if (y >= 9 && y <= pocketTop && core > 0.56)
                {
                    return BlockId.Water;
                }
            }

            id = BlockId.Air;
        }

        return id;
    }

    /// <summary>True in sky-preset worlds where floating land exists at (x, z).</summary>
    public bool IslandAt(int x, int z) => _genVersion >= WorldGenV8.Version
            ? GeneratorV8.IsIsland(x, z)
            : _genVersion >= WorldGenV7.Version
                    ? GeneratorV7.IsIsland(x, z)
                    : _islandNoise.GetNoise2D(x, z) > 0.05;

    /// <summary>True for the gen v5 floating-islands preset (minimap/LOD skip empty sky).</summary>
    public bool IsSky => _preset == "sky";

    /// <summary>Active shape preset name (gen v5+): normal / flat / amplified / sky.</summary>
    public string PresetName => _preset;

    // -----------------------------------------------------------------------
    // Trees (deterministic spawn + shape)
    // -----------------------------------------------------------------------

    public readonly record struct TreeInfo(int X, int Z, int Base, int Trunk, int Kind);

    public readonly record struct CactusInfo(int X, int Z, int Base, int Height);

    public TreeInfo? GetTreeAt(int tx, int tz)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            return GetTreeAtV8(tx, tz);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            return GetTreeAtV7(tx, tz);
        }

        if (IsSky && !IslandAt(tx, tz))
        {
            return null;
        }

        int h = SurfaceHeight(tx, tz);
        if (h <= SeaLevel + 1 || h >= Height - 12)
        {
            return null;   // no trees at the world ceiling
        }

        Biome biome = BiomeAt(tx, tz, h);
        if (biome is Biome.Desert or Biome.Snow)
        {
            return null;
        }

        long r = Hash2(tx, tz);
        long chance = biome == Biome.Birch ? 90 : 160;
        if (r % chance != 0)
        {
            return null;
        }

        int kind = biome == Biome.Birch ? 1 : 0;
        int trunk = 4 + (int)((r >> 8) % 3);
        if (kind == 1)
        {
            trunk += 1;
        }

        return new TreeInfo(tx, tz, h + 1, trunk, kind);
    }

    public CactusInfo? GetCactusAt(int tx, int tz)
    {
        if (IsSky && !IslandAt(tx, tz))
        {
            return null;
        }

        int h;
        Biome biome;
        if (_genVersion >= WorldGenV8.Version)
        {
            WorldGenV7.SurfaceSample surface = GeneratorV8.SampleSurface(tx, tz, SeaLevel);
            h = surface.Height;
            biome = surface.Biome;
        }
        else if (_genVersion >= WorldGenV7.Version)
        {
            WorldGenV7.SurfaceSample surface = GeneratorV7.SampleSurface(tx, tz, SeaLevel);
            h = surface.Height;
            biome = surface.Biome;
        }
        else
        {
            h = SurfaceHeight(tx, tz);
            biome = BiomeAt(tx, tz, h);
        }

        if (h <= SeaLevel + 1 || biome != Biome.Desert)
        {
            return null;
        }

        long r = Hash2(tx + 77, tz - 31);
        if (r % 120 != 0)
        {
            return null;
        }

        return new CactusInfo(tx, tz, h + 1, 2 + (int)((r >> 8) % 3));
    }

    /// <summary>The campfire site for one 64x64 grid cell, or null when the cell has none.</summary>
    public Vector3I? GetCampfireSite(int cx, int cz)
    {
        // v1 worlds keep the original GD.Hash placement so their terrain stays
        // byte-identical forever; newer worlds use our own frozen integer hash.
        ulong rngSeed = _genVersion <= 1
                ? (ulong)((uint)GD.Hash(new Vector2I(cx, cz)) ^ 0x1D872B41u)
                : _genVersion >= WorldGenV8.Version
                        ? GeneratorV8.CoordinateHash(cx, 19532, cz, 0x43414D50u)
                        : _genVersion >= WorldGenV7.Version
                                ? GeneratorV7.CoordinateHash(cx, 19532, cz, 0x43414D50u)
                        : (ulong)(Hash3(cx, 19532, cz) & 0x7FFFFFFFFFFFFFFFL);
        // Thread-local generator: the sequence must stay identical, but the
        // object is pure scratch and is reached ~9 times per generated chunk.
        RandomNumberGenerator rng = _campfireRng ??= new RandomNumberGenerator();
        rng.Seed = rngSeed;
        int x = cx * 64 + rng.RandiRange(6, 57);
        int z = cz * 64 + rng.RandiRange(6, 57);
        if (IsSky && !IslandAt(x, z))
        {
            return null;
        }

        int h;
        Biome biome;
        if (_genVersion >= WorldGenV8.Version)
        {
            WorldGenV7.SurfaceSample surface = GeneratorV8.SampleSurface(x, z, SeaLevel);
            h = surface.Height;
            biome = surface.Biome;
        }
        else if (_genVersion >= WorldGenV7.Version)
        {
            WorldGenV7.SurfaceSample surface = GeneratorV7.SampleSurface(x, z, SeaLevel);
            h = surface.Height;
            biome = surface.Biome;
        }
        else
        {
            h = SurfaceHeight(x, z);
            biome = BiomeAt(x, z, h);
        }

        if (h <= SeaLevel + 2 || h >= Height - 4 || biome == Biome.Snow)
        {
            return null;
        }

        return new Vector3I(x, h, z);
    }

    private readonly Dictionary<long, Vector3I?> _campfireCache = new();
    [ThreadStatic] private static RandomNumberGenerator? _campfireRng;

    /// <summary>Cell-cached campfire site lookup (deterministic per world).</summary>
    private Vector3I? CachedCampfireSite(int cx, int cz)
    {
        long key = ((long)cx << 32) ^ (uint)cz;
        lock (_mutex)
        {
            if (_campfireCache.TryGetValue(key, out Vector3I? cached))
            {
                return cached;
            }
        }

        Vector3I? site = GetCampfireSite(cx, cz);
        lock (_mutex)
        {
            _campfireCache[key] = site;
        }

        return site;
    }

    /// <summary>True when this exact block is a live campfire's log (not just any log).</summary>
    public bool IsCampfireLog(int x, int y, int z)
    {
        if (GetBlock(x, y, z) != BlockId.Log)
        {
            return false;
        }

        return CachedCampfireSite(Mathf.FloorToInt(x / (float)Campfires.Cell),
                Mathf.FloorToInt(z / (float)Campfires.Cell)) is { } site
                && site.X == x && site.Y == y && site.Z == z;
    }

    /// <summary>Campfire sites within radius of a position (minimap waypoints).</summary>
    public List<Vector3I> NearbyCampfireSites(Vector3 pos, float radius)
    {
        List<Vector3I> result = [];
        int c0x = Mathf.FloorToInt((pos.X - radius) / Campfires.Cell);
        int c1x = Mathf.FloorToInt((pos.X + radius) / Campfires.Cell);
        int c0z = Mathf.FloorToInt((pos.Z - radius) / Campfires.Cell);
        int c1z = Mathf.FloorToInt((pos.Z + radius) / Campfires.Cell);
        for (int cz = c0z; cz <= c1z; cz++)
        {
            for (int cx = c0x; cx <= c1x; cx++)
            {
                if (CachedCampfireSite(cx, cz) is { } site
                        && new Vector2(site.X - pos.X, site.Z - pos.Z).Length() <= radius)
                {
                    result.Add(site);
                }
            }
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Structures (gen v4+): one deterministic site per 160x160 cell.
    // -----------------------------------------------------------------------

    public const int StructureCell = 160;

    public readonly record struct StructureInfo(int Kind, int X, int Y, int Z, uint Seed);

    private readonly Dictionary<long, StructureInfo?> _structureCache = new();

    /// <summary>Structure site for one cell, or null (kind 0 tower / 1 ruin / 2 mine).</summary>
    public StructureInfo? GetStructureSite(int cx, int cz)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            return GetStructureSiteV8(cx, cz);
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            return GetStructureSiteV7(cx, cz);
        }

        if (_genVersion < 4)
        {
            return null;
        }

        uint rng = Mix((uint)_noiseSeeds[2], (uint)cx, (uint)cz, 0x53545255u);
        // Gen v6 adds rare Ember Deep dungeons. Keeping the old draw sequence
        // for v4/v5 preserves every existing structure in existing worlds.
        int kind;
        if (_genVersion >= 6)
        {
            kind = Range(ref rng, 0, 9) == 0 ? 3 : Range(ref rng, 0, 2);
        }
        else
        {
            kind = Range(ref rng, 0, 2);
        }
        int x = cx * StructureCell + Range(ref rng, 12, StructureCell - 12);
        int z = cz * StructureCell + Range(ref rng, 12, StructureCell - 12);
        if (IsSky && !IslandAt(x, z))
        {
            return null;
        }

        int h = SurfaceHeight(x, z);
        if (h <= SeaLevel + 2 || h >= Height - 14)
        {
            return null;
        }

        if (BiomeAt(x, z, h) == Biome.Snow)
        {
            return null;
        }

        return new StructureInfo(kind, x, h, z, rng);
    }

    private StructureInfo? CachedStructureSite(int cx, int cz)
    {
        long key = ((long)cx << 32) ^ (uint)cz;
        lock (_mutex)
        {
            if (_structureCache.TryGetValue(key, out StructureInfo? cached))
            {
                return cached;
            }
        }

        StructureInfo? site = GetStructureSite(cx, cz);
        lock (_mutex)
        {
            _structureCache[key] = site;
        }

        return site;
    }

    /// <summary>Nearest structure site of a kind within cellRadius cells (dev teleport).</summary>
    public StructureInfo? FindNearestStructure(int kind, int cellX, int cellZ, int cellRadius)
    {
        StructureInfo? best = null;
        float bestD = float.MaxValue;
        for (int dz = -cellRadius; dz <= cellRadius; dz++)
        {
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                if (GetStructureSite(cellX + dx, cellZ + dz) is not { } site || site.Kind != kind)
                {
                    continue;
                }

                float d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = site;
                }
            }
        }

        return best;
    }

    /// <summary>Nearest lit campfire within maxDist (for repel rings + rest prompts).</summary>
    public Vector3? NearestCampfire(Vector3 pos, float maxDist)
    {
        int ccx = Mathf.FloorToInt(pos.X / Campfires.Cell);
        int ccz = Mathf.FloorToInt(pos.Z / Campfires.Cell);
        Vector3? best = null;
        float bestSq = maxDist * maxDist;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (CachedCampfireSite(ccx + dx, ccz + dz) is not { } site)
                {
                    continue;
                }

                if (GetBlock(site.X, site.Y, site.Z) != BlockId.Log)
                {
                    continue;   // the fire is out (log removed)
                }

                Vector3 centre = new(site.X + 0.5f, site.Y + 1.0f, site.Z + 0.5f);
                float d = centre.DistanceSquaredTo(pos);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = centre;
                }
            }
        }

        return best;
    }

    /// <summary>AIR / LOG / LEAVES that <paramref name="tree"/> contributes at world position (wx, wy, wz).</summary>
    public BlockId TreeBlockFor(TreeInfo tree, int wx, int wy, int wz)
    {
        if (_genVersion >= WorldGenV7.Version)
        {
            return TreeBlockForV7(tree, wx, wy, wz);
        }

        bool birch = tree.Kind == 1;
        BlockId logId = birch ? BlockId.BirchLog : BlockId.Log;
        BlockId leafId = birch ? BlockId.BirchLeaves : BlockId.Leaves;
        if (wx == tree.X && wz == tree.Z && wy >= tree.Base && wy < tree.Base + tree.Trunk)
        {
            return logId;
        }

        int dy = wy - (tree.Base + tree.Trunk - 2);
        if (dy < 0 || dy > 3)
        {
            return BlockId.Air;
        }

        int r = 2;
        if (dy >= 2 || birch)
        {
            r = dy >= 2 ? 1 : 2;
        }

        if (birch && dy == 0)
        {
            r = 1;
        }

        int dx = wx - tree.X;
        int dz = wz - tree.Z;
        if (Mathf.Abs(dx) > r || Mathf.Abs(dz) > r)
        {
            return BlockId.Air;
        }

        if (dx == 0 && dz == 0 && dy < 2)
        {
            return BlockId.Air;
        }

        if (r == 2 && dy == 0 && Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 2)
        {
            return BlockId.Air;
        }

        if (r == 1 && dy == 3 && Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 1)
        {
            return BlockId.Air;
        }

        return leafId;
    }

    // -----------------------------------------------------------------------
    // Chunk data generation + cache (thread-safe)
    // -----------------------------------------------------------------------

    private byte[] GenerateChunkData(Vector2I cp)
    {
        byte[] data = new byte[ChunkX * Height * ChunkZ];
        int ox = cp.X * ChunkX;
        int oz = cp.Y * ChunkZ;
        int strideY = ChunkX * ChunkZ;
        for (int z = 0; z < ChunkZ; z++)
        {
            for (int x = 0; x < ChunkX; x++)
            {
                int wx = ox + x;
                int wz = oz + z;
                int h;
                Biome biome;
                if (_genVersion >= WorldGenV8.Version)
                {
                    WorldGenV7.SurfaceSample sample = GeneratorV8.SampleSurface(wx, wz, SeaLevel);
                    h = sample.Height;
                    biome = sample.Biome;
                }
                else if (_genVersion >= WorldGenV7.Version)
                {
                    WorldGenV7.SurfaceSample sample = GeneratorV7.SampleSurface(wx, wz, SeaLevel);
                    h = sample.Height;
                    biome = sample.Biome;
                }
                else
                {
                    h = SurfaceHeight(wx, wz);
                    biome = BiomeAt(wx, wz, h);
                }

                bool beach = h <= SeaLevel + 1;
                int top = Mathf.Max(h, SeaLevel);
                int baseIdx = x + z * ChunkX;
                for (int y = 0; y <= top; y++)
                {
                    data[baseIdx + y * strideY] = (byte)BlockAt(wx, y, wz, h, beach, biome);
                }
            }
        }

        // Ore veins (gen v3+): deterministic per-chunk blobs in depth bands.
        if (_genVersion >= 3)
        {
            StampOreVeins(cp, data, strideY, ox, oz);
        }

        // Trees from the padded neighbourhood, clipped into this chunk.
        // Only air is replaced, so overlapping trees merge gracefully.
        for (int tz = oz - 2; tz < oz + ChunkZ + 2; tz++)
        {
            for (int tx = ox - 2; tx < ox + ChunkX + 2; tx++)
            {
                if (GetTreeAt(tx, tz) is not { } tree)
                {
                    continue;
                }

                for (int wy = tree.Base; wy < tree.Base + tree.Trunk + 2; wy++)
                {
                    if (wy < 0 || wy >= Height)
                    {
                        continue;
                    }

                    int lz0 = Mathf.Max(tz - 2 - oz, 0);
                    int lz1 = Mathf.Min(tz + 2 - oz, ChunkZ - 1);
                    int lx0 = Mathf.Max(tx - 2 - ox, 0);
                    int lx1 = Mathf.Min(tx + 2 - ox, ChunkX - 1);
                    for (int lz = lz0; lz <= lz1; lz++)
                    {
                        for (int lx = lx0; lx <= lx1; lx++)
                        {
                            BlockId id = TreeBlockFor(tree, ox + lx, wy, oz + lz);
                            if (id == BlockId.Air)
                            {
                                continue;
                            }

                            int i = lx + lz * ChunkX + wy * strideY;
                            if ((BlockId)data[i] == BlockId.Air)
                            {
                                data[i] = (byte)id;
                            }
                        }
                    }
                }
            }
        }

        // Cacti: desert-only columns, clipped into this chunk.
        for (int tz = oz - 1; tz < oz + ChunkZ + 1; tz++)
        {
            for (int tx = ox - 1; tx < ox + ChunkX + 1; tx++)
            {
                if (GetCactusAt(tx, tz) is not { } cactus)
                {
                    continue;
                }

                for (int wy = cactus.Base; wy < cactus.Base + cactus.Height; wy++)
                {
                    int clx = tx - ox;
                    int clz = tz - oz;
                    if (clx < 0 || clx >= ChunkX || clz < 0 || clz >= ChunkZ || wy >= Height)
                    {
                        continue;
                    }

                    int ci = clx + clz * ChunkX + wy * strideY;
                    if ((BlockId)data[ci] == BlockId.Air)
                    {
                        data[ci] = (byte)BlockId.Cactus;
                    }
                }
            }
        }

        // Surface decorators + structures (gen v4+): plants, boulders,
        // watchtowers, ruins and mines — stamped before campfires so fire
        // sites always win.
        if (_genVersion >= 4)
        {
            StampDecorators(data, strideY, ox, oz);
            StampStructures(data, strideY, ox, oz);
            if (_genVersion >= WorldGenV7.Version)
            {
                StampUndergroundStructuresV7(data, strideY, ox, oz);
            }

            if (_genVersion >= 8)
            {
                StampVillagesV8(data, strideY, ox, oz);
            }
        }

        // Campfires: one deterministic site per 64x64 cell; only the cells touching
        // this chunk (plus a one-block skirt) need to be tested.
        Vector2I[] fireOffsets = [new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
        for (int cellz = Mathf.FloorToInt((oz - 1) / 64.0f); cellz <= Mathf.FloorToInt((oz + ChunkZ) / 64.0f); cellz++)
        {
            for (int cellx = Mathf.FloorToInt((ox - 1) / 64.0f); cellx <= Mathf.FloorToInt((ox + ChunkX) / 64.0f); cellx++)
            {
                if (GetCampfireSite(cellx, cellz) is not { } site)
                {
                    continue;
                }

                int cclx = site.X - ox;
                int cclz = site.Z - oz;
                if (cclx < -1 || cclx > ChunkX || cclz < -1 || cclz > ChunkZ)
                {
                    continue;
                }

                foreach (Vector2I off in fireOffsets)
                {
                    int px = cclx + off.X;
                    int pz = cclz + off.Y;
                    if (px < 0 || px >= ChunkX || pz < 0 || pz >= ChunkZ || site.Y >= Height)
                    {
                        continue;
                    }

                    int fi = px + pz * ChunkX + site.Y * strideY;
                    if (off == Vector2I.Zero)
                    {
                        data[fi] = (byte)BlockId.Log;
                    }
                    else if ((BlockId)data[fi] is not BlockId.Air and not BlockId.Water)
                    {
                        data[fi] = (byte)BlockId.Cobblestone;
                    }
                }
            }
        }

        // Player edits on top of the generated terrain.
        lock (_mutex)
        {
            if (_edits.TryGetValue(cp, out Dictionary<int, BlockId>? edits))
            {
                foreach ((int index, BlockId id) in edits)
                {
                    if ((uint)index < (uint)data.Length)
                    {
                        data[index] = (byte)id;
                    }
                }
            }
        }

        return data;
    }

    // -----------------------------------------------------------------------
    // Ore veins (gen v3+): per-chunk deterministic blobs in depth bands.
    // Only stone is replaced, so caves and the surface stay untouched.
    // -----------------------------------------------------------------------

    private void StampOreVeins(Vector2I cp, byte[] data, int strideY, int ox, int oz)
    {
        if (_genVersion >= WorldGenV7.Version)
        {
            StampOreVeinsV7(cp, data, strideY, ox, oz);
            return;
        }

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                StampVeinsOfChunk(cp + new Vector2I(dx, dz), data, strideY, ox, oz);
            }
        }
    }

    private void StampVeinsOfChunk(Vector2I n, byte[] data, int strideY, int ox, int oz)
    {
        uint rng = Mix((uint)_noiseSeeds[0], (uint)n.X, (uint)n.Y, 0x51ED5A17u);
        int cellOx = n.X * ChunkX;
        int cellOz = n.Y * ChunkZ;
        TryVein(ref rng, BlockId.CoalOre, cellOx, cellOz, 8, 54, 6, 4, 10, data, strideY, ox, oz);
        TryVein(ref rng, BlockId.IronOre, cellOx, cellOz, 4, 40, 4, 3, 7, data, strideY, ox, oz);
        TryVein(ref rng, BlockId.GoldOre, cellOx, cellOz, 2, 26, 2, 2, 5, data, strideY, ox, oz);
        TryVein(ref rng, BlockId.DiamondOre, cellOx, cellOz, 2, 14, 1, 2, 4, data, strideY, ox, oz);
    }

    private void TryVein(ref uint rng, BlockId ore, int cellOx, int cellOz,
            int minY, int maxY, int count, int minSize, int maxSize,
            byte[] data, int strideY, int ox, int oz)
    {
        int veins = count + Range(ref rng, 0, 1);
        for (int v = 0; v < veins; v++)
        {
            int gx = cellOx + Range(ref rng, 0, ChunkX - 1);
            int gz = cellOz + Range(ref rng, 0, ChunkZ - 1);
            int gy = Range(ref rng, Mathf.Max(minY, 2), Mathf.Min(maxY, Height - 2));
            int size = Range(ref rng, minSize, maxSize);
            for (int step = 0; step < size; step++)
            {
                StampOre(gx, gy, gz, ore, data, strideY, ox, oz);
                switch (Range(ref rng, 0, 5))
                {
                    case 0:
                        gx += 1;
                        break;
                    case 1:
                        gx -= 1;
                        break;
                    case 2:
                        gy += 1;
                        break;
                    case 3:
                        gy -= 1;
                        break;
                    case 4:
                        gz += 1;
                        break;
                    default:
                        gz -= 1;
                        break;
                }

                gy = Mathf.Clamp(gy, 2, Height - 2);
            }
        }
    }

    private void StampOre(int gx, int gy, int gz, BlockId ore, byte[] data, int strideY, int ox, int oz)
    {
        int lx = gx - ox;
        int lz = gz - oz;
        if (lx < 0 || lx >= ChunkX || lz < 0 || lz >= ChunkZ || gy < 0 || gy >= Height)
        {
            return;
        }

        int i = lx + lz * ChunkX + gy * strideY;
        if ((BlockId)data[i] == BlockId.Stone)
        {
            data[i] = (byte)ore;
        }
    }

    // -----------------------------------------------------------------------
    // Surface decorators (gen v4+): grass tufts, flowers and boulders.
    // Runs over a padded neighbourhood (boulders span chunk borders) and
    // writes only into air above natural grass.
    // -----------------------------------------------------------------------

    private void StampDecorators(byte[] data, int strideY, int ox, int oz)
    {
        if (_genVersion >= WorldGenV8.Version)
        {
            StampDecoratorsV8(data, strideY, ox, oz);
            return;
        }

        if (_genVersion >= WorldGenV7.Version)
        {
            StampDecoratorsV7(data, strideY, ox, oz);
            return;
        }

        for (int wz = oz - 2; wz < oz + ChunkZ + 2; wz++)
        {
            for (int wx = ox - 2; wx < ox + ChunkX + 2; wx++)
            {
                int h = SurfaceHeight(wx, wz);
                if (h <= SeaLevel + 1 || h >= Height - 4)
                {
                    continue;
                }

                BlockId ground = BlockAt(wx, h, wz, h, false, BiomeAt(wx, wz, h));
                if (ground != BlockId.Grass)
                {
                    continue;   // snow/desert/beach stay clean
                }

                long r = Hash2(wx * 3 + 1, wz * 5 + 7);
                if (r % 1024 < 2)
                {
                    StampBoulder(wx, h, wz, data, strideY, ox, oz);
                    continue;
                }

                int clx = wx - ox;
                int clz = wz - oz;
                if (clx < 0 || clx >= ChunkX || clz < 0 || clz >= ChunkZ)
                {
                    continue;
                }

                int above = clx + clz * ChunkX + (h + 1) * strideY;
                if ((BlockId)data[above] != BlockId.Air)
                {
                    continue;   // a tree trunk or structure already occupies it
                }

                if (r % 64 == 0)
                {
                    data[above] = (byte)BlockId.FlowerRed;
                }
                else if (r % 67 == 0)
                {
                    data[above] = (byte)BlockId.FlowerYellow;
                }
                else if (r % 8 == 0)
                {
                    data[above] = (byte)BlockId.TallGrass;
                }
            }
        }
    }

    /// <summary>V11-only reed stamp reuses the V8 decorator's surface sample.</summary>
    private void TryStampShorelineReedV11(int wx, int wz, int height, Biome biome,
            byte[] data, int strideY, int ox, int oz)
    {
        BlockId ground = BlockAtV8(wx, height, wz, height, biome);
        if (!WorldGenV11.IsShorelineColumn(height, SeaLevel, biome, ground,
                adjacentToShallowWater: true))
            return;

        // Reject the common non-reed rolls before the four neighbour-height
        // samples; the hash is independent of those samples, so the generated
        // output is identical while eligible banks pay the full shoreline cost.
        uint hash = GeneratorV8.CoordinateHash(wx, height, wz, 0x52564431u);
        if (!WorldGenV11.ShouldPlaceReed(hash, biome) || !IsShallowWaterNear(wx, wz))
            return;

        int lx = wx - ox;
        int lz = wz - oz;
        if ((uint)lx >= ChunkX || (uint)lz >= ChunkZ) return;
        int below = lx + lz * ChunkX + height * strideY;
        // The terrain sample can disagree with the stamped chunk at village
        // paths, cave mouths and structure foundations. Reeds belong on the
        // actual grass/sand surface, not on a path or over a carved-out hole.
        if ((BlockId)data[below] is not (BlockId.Grass or BlockId.Sand)) return;
        int above = below + strideY;
        if ((BlockId)data[above] == BlockId.Air)
            data[above] = (byte)BlockId.RiverReeds;
    }

    private bool IsShallowWaterNear(int x, int z) =>
            IsShallowWaterColumn(x + 1, z) || IsShallowWaterColumn(x - 1, z)
            || IsShallowWaterColumn(x, z + 1) || IsShallowWaterColumn(x, z - 1);

    private bool IsShallowWaterColumn(int x, int z)
    {
        int height = GeneratorV8.SurfaceHeight(x, z, SeaLevel);
        return WorldGenV11.IsShallowWaterNeighbor(height, SeaLevel);
    }

    /// <summary>2x2x2 ragged cobble/stone pile, partially buried, clipped into this chunk.</summary>
    private void StampBoulder(int wx, int h, int wz, byte[] data, int strideY, int ox, int oz)
    {
        for (int dz = 0; dz < 2; dz++)
        {
            for (int dx = 0; dx < 2; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    long rh = Hash3(wx + dx * 31, h + dy * 7, wz + dz * 53);
                    if (rh % 5 == 0)
                    {
                        continue;   // ragged edges
                    }

                    int lx = wx + dx - ox;
                    int lz = wz + dz - oz;
                    int by = h + dy;
                    if (lx < 0 || lx >= ChunkX || lz < 0 || lz >= ChunkZ || by >= Height)
                    {
                        continue;
                    }

                    int i = lx + lz * ChunkX + by * strideY;
                    BlockId cur = (BlockId)data[i];
                    if (cur is BlockId.Air or BlockId.Grass or BlockId.Dirt or BlockId.SnowGrass)
                    {
                        data[i] = (byte)(rh % 2 == 0 ? BlockId.Cobblestone : BlockId.Stone);
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Structures (gen v4+): templates clipped into the chunk being generated.
    // -----------------------------------------------------------------------

    private void StampStructures(byte[] data, int strideY, int ox, int oz)
    {
        int c0x = Mathf.FloorToInt((ox - 14) / (float)StructureCell);
        int c1x = Mathf.FloorToInt((ox + ChunkX + 14) / (float)StructureCell);
        int c0z = Mathf.FloorToInt((oz - 14) / (float)StructureCell);
        int c1z = Mathf.FloorToInt((oz + ChunkZ + 14) / (float)StructureCell);
        for (int cz = c0z; cz <= c1z; cz++)
        {
            for (int cx = c0x; cx <= c1x; cx++)
            {
                if (CachedStructureSite(cx, cz) is not { } site)
                {
                    continue;
                }

                Structures.Stamp(site, (wx, wy, wz, id) =>
                {
                    int lx = wx - ox;
                    int lz = wz - oz;
                    if (lx < 0 || lx >= ChunkX || lz < 0 || lz >= ChunkZ || wy <= 0 || wy >= Height)
                    {
                        return;
                    }

                    int i = lx + lz * ChunkX + wy * strideY;
                    if ((BlockId)data[i] == BlockId.Bedrock)
                    {
                        return;
                    }

                    if (_genVersion >= WorldGenV9.Version)
                        id = WorldGenV9.WeatheredMaterial(site, id, wx, wy, wz);
                    data[i] = (byte)id;
                });
            }
        }
    }

    // Deterministic helpers: our own integer hash + xorshift, frozen forever.
    private static uint Mix(uint seed, uint x, uint z, uint salt)
    {
        uint h = seed ^ salt;
        h ^= x * 0x9E3779B1u;
        h = (h << 13) | (h >> 19);
        h ^= z * 0x85EBCA77u;
        h = (h << 17) | (h >> 15);
        h *= 0xC2B2AE3Du;
        h ^= h >> 16;
        return h == 0 ? 0x9E3779B9u : h;
    }

    private static uint Next(ref uint s)
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }

    private static int Range(ref uint s, int minInclusive, int maxInclusive) =>
        minInclusive + (int)(Next(ref s) % (uint)(maxInclusive - minInclusive + 1));

    /// <summary>
    /// Block data for any chunk: built node data, cached data, or freshly
    /// generated + cached. Safe to call from worker threads.
    /// </summary>
    public byte[] ChunkData(Vector2I cp)
    {
        Lazy<byte[]> generation;
        lock (_mutex)
        {
            if (_dataCache.TryGetValue(cp, out byte[]? cached))
            {
                RegisterLightSourcesLocked(cp, cached);
                RegisterCropSourcesLocked(cp, cached);
                return cached;
            }

            generation = _generationInFlight.GetOrCreate(cp, () => GenerateChunkData(cp));
        }

        byte[] data;
        try
        {
            // Exactly one caller generates this coordinate. Other workers wait
            // on the same Lazy while unrelated coordinates remain parallel.
            data = generation.Value;
        }
        catch
        {
            lock (_mutex)
            {
                _generationInFlight.Complete(cp, generation);
            }

            throw;
        }

        lock (_mutex)
        {
            if (_dataCache.TryGetValue(cp, out byte[]? existing))
            {
                RegisterLightSourcesLocked(cp, existing);
                RegisterCropSourcesLocked(cp, existing);
                _generationInFlight.Complete(cp, generation);
                return existing;
            }

            _dataCache[cp] = data;
            RegisterLightSourcesLocked(cp, data);
            RegisterCropSourcesLocked(cp, data);
            _generationInFlight.Complete(cp, generation);
            return data;
        }
    }

    /// <summary>Cache a chunk's emitter positions once, including natural lava and saved edits.</summary>
    private void RegisterLightSourcesLocked(Vector2I cp, byte[] blocks)
    {
        if (!_sourceScannedChunks.Add(cp)) return;
        int stride = ChunkX * ChunkZ;
        int ox = cp.X * ChunkX;
        int oz = cp.Y * ChunkZ;
        for (int i = 0; i < blocks.Length; i++)
        {
            byte emission = BlockDb.LightEmission[blocks[i]];
            if (emission == 0) continue;
            int y = i / stride;
            int rem = i - y * stride;
            int z = rem / ChunkX;
            int x = rem - z * ChunkX;
            _lightSources[new Vector3I(ox + x, y, oz + z)] = emission;
        }

        RegisterCampfireLightSourcesLocked(cp, blocks);
    }

    /// <summary>
    /// Generated campfires are represented by a virtual source in the flame
    /// air cell above their log. Only register it while the centre log remains,
    /// so saved edits and later block removal keep baked light authoritative.
    /// </summary>
    private void RegisterCampfireLightSourcesLocked(Vector2I cp, byte[] blocks)
    {
        int ox = cp.X * ChunkX;
        int oz = cp.Y * ChunkZ;
        int stride = ChunkX * ChunkZ;
        int minCellX = Mathf.FloorToInt(ox / (float)Campfires.Cell);
        int maxCellX = Mathf.FloorToInt((ox + ChunkX - 1) / (float)Campfires.Cell);
        int minCellZ = Mathf.FloorToInt(oz / (float)Campfires.Cell);
        int maxCellZ = Mathf.FloorToInt((oz + ChunkZ - 1) / (float)Campfires.Cell);

        for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
        for (int cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            if (GetCampfireSite(cellX, cellZ) is not { } site) continue;

            int localX = site.X - ox;
            int localZ = site.Z - oz;
            if ((uint)localX >= ChunkX || (uint)localZ >= ChunkZ
                    || (uint)site.Y >= Height)
                continue;

            int index = localX + localZ * ChunkX + site.Y * stride;
            Vector3I flame = new(site.X, site.Y + 1, site.Z);
            if (blocks[index] == (byte)BlockId.Log && flame.Y < Height)
                _lightSources[flame] = VoxelLighting.CampfireEmission;
        }
    }

    private byte[] SkyLightBase(Vector2I cp)
    {
        lock (_mutex)
        {
            if (_skyLightCache.TryGetValue(cp, out byte[]? cached)) return cached;
        }
        byte[] built = VoxelLighting.BuildSkyLight(ChunkData(cp), ChunkX, Height, ChunkZ, ChunkBlockStates(cp));
        lock (_mutex)
        {
            if (_skyLightCache.TryGetValue(cp, out byte[]? existing)) return existing;
            _skyLightCache[cp] = built;
            return built;
        }
    }

    private byte[] BlockLightBase(Vector2I cp)
    {
        lock (_mutex)
        {
            if (_blockLightCache.TryGetValue(cp, out byte[]? cached)) return cached;
        }
        byte[] built = VoxelLighting.BuildBlockLightForChunk(this, cp, Height);
        lock (_mutex)
        {
            if (_blockLightCache.TryGetValue(cp, out byte[]? existing)) return existing;
            _blockLightCache[cp] = built;
            return built;
        }
    }

    /// <summary>
    /// Builds the complete 3x3 meshing light neighbourhood from one shared
    /// region solve. A 14-voxel halo on each side is sufficient for block-light
    /// attenuation and prevents nine overlapping per-chunk solves.
    /// </summary>
    public VoxelLightData[] LightDataNeighborhood(Vector2I center)
    {
        lock (SnapshotLock)
        {
            VoxelLightData[] result = new VoxelLightData[9];
            bool complete = true;
            lock (_mutex)
            {
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int slot = (dz + 1) * 3 + dx + 1;
                    if (_voxelLightCache.TryGetValue(center + new Vector2I(dx, dz), out VoxelLightData? cached))
                        result[slot] = cached;
                    else
                        complete = false;
                }
            }
            if (complete) return result;

            const int halo = VoxelLighting.BlockLightRadius;
            const int coreWidth = ChunkX * 3;
            int width = coreWidth + halo * 2;
            int depth = ChunkZ * 3 + halo * 2;
            int originX = center.X * ChunkX - halo - ChunkX;
            int originZ = center.Y * ChunkZ - halo - ChunkZ;

            byte[][] chunks = new byte[25][];
            byte[][]? stateChunks = null;
            bool hasNonDefaultStates = false;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int slot = (dz + 2) * 5 + dx + 2;
                Vector2I cp = center + new Vector2I(dx, dz);
                chunks[slot] = ChunkData(cp);
                byte[] states = ChunkBlockStates(cp);
                if (states.Length > 0)
                {
                    (stateChunks ??= new byte[25][])[slot] = states;
                    hasNonDefaultStates = true;
                }
            }

            byte[] region = new byte[width * Height * depth];
            byte[]? regionStates = hasNonDefaultStates ? new byte[region.Length] : null;
            for (int z = 0; z < depth; z++)
            {
                int wz = originZ + z;
                int cz = Mathf.FloorToInt(wz / (float)ChunkZ);
                int lz = wz - cz * ChunkZ;
                for (int x = 0; x < width; x++)
                {
                    int wx = originX + x;
                    int cx = Mathf.FloorToInt(wx / (float)ChunkX);
                    int lx = wx - cx * ChunkX;
                    int slot = (cz - center.Y + 2) * 5 + cx - center.X + 2;
                    byte[] source = chunks[slot];
                    byte[]? sourceStates = stateChunks?[slot];
                    int srcColumn = lx + lz * ChunkX;
                    int dstColumn = x + z * width;
                    for (int y = 0; y < Height; y++)
                    {
                        int sourceIndex = srcColumn + y * ChunkX * ChunkZ;
                        int destinationIndex = dstColumn + y * width * depth;
                        region[destinationIndex] = source[sourceIndex];
                        if (regionStates != null && sourceStates != null)
                            regionStates[destinationIndex] = sourceStates[sourceIndex];
                    }
                }
            }

            byte[] sky = VoxelLighting.BuildSkyLight(
                    region, width, Height, depth, regionStates, out byte[] directSky);
            int plane = width * depth;
            List<VoxelLightSource> sources = NearbyLightSources(
                    originX, originX + width - 1, originZ, originZ + depth - 1);
            for (int i = 0; i < sources.Count; i++)
            {
                VoxelLightSource source = sources[i];
                Vector3I position = source.Position;
                sources[i] = new VoxelLightSource(
                        new Vector3I(position.X - originX, position.Y, position.Z - originZ),
                        source.Level);
            }

            byte[] block = VoxelLighting.PropagateBlockLight(region, width, Height, depth, sources, regionStates);

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int slot = (dz + 1) * 3 + dx + 1;
                Vector2I cp = center + new Vector2I(dx, dz);
                byte[] packed = new byte[ChunkX * Height * ChunkZ];
                byte[] directPacked = new byte[(packed.Length + 1) / 2];
                int cropX = halo + ChunkX + dx * ChunkX;
                int cropZ = halo + ChunkZ + dz * ChunkZ;
                for (int y = 0; y < Height; y++)
                for (int z = 0; z < ChunkZ; z++)
                {
                    int src = y * plane + (cropZ + z) * width + cropX;
                    int dst = (y * ChunkZ + z) * ChunkX;
                    for (int x = 0; x < ChunkX; x++)
                    {
                        int voxelIndex = dst + x;
                        packed[voxelIndex] = VoxelLighting.Pack(sky[src + x], block[src + x]);
                        int nibbleIndex = voxelIndex >> 1;
                        int shift = (voxelIndex & 1) * 4;
                        directPacked[nibbleIndex] |= (byte)((directSky[src + x] & 0x0F) << shift);
                    }
                }

                VoxelLightData data = new(packed, directPacked);
                lock (_mutex)
                {
                    if (_voxelLightCache.TryGetValue(cp, out VoxelLightData? cached))
                        result[slot] = cached;
                    else
                    {
                        _voxelLightCache[cp] = data;
                        result[slot] = data;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Packed sky/block lighting snapshot for one chunk.</summary>
    public VoxelLightData LightData(Vector2I cp) => LightDataNeighborhood(cp)[4];

    /// <summary>
    /// Samples the already-baked skylight at a world-space position by
    /// interpolating the surrounding voxel centres. This is a cache-only read:
    /// it never generates chunks or blocks the streaming workers. A missing
    /// map returns false so presentation can use its smooth terrain-depth
    /// fallback until the first nearby mesh has populated the light cache.
    /// </summary>
    public bool TrySampleCachedSkyLight(Vector3 worldPosition, out float lightLevel)
    {
        lightLevel = 0.0f;
        float gridX = worldPosition.X - 0.5f;
        float gridY = worldPosition.Y - 0.5f;
        float gridZ = worldPosition.Z - 0.5f;
        int x0 = Mathf.FloorToInt(gridX);
        int y0 = Mathf.FloorToInt(gridY);
        int z0 = Mathf.FloorToInt(gridZ);
        float tx = gridX - x0;
        float ty = gridY - y0;
        float tz = gridZ - z0;

        Span<byte> samples = stackalloc byte[8];
        lock (_mutex)
        {
            if (!TryReadCachedSkyLightLocked(x0, y0, z0, out samples[0])
                    || !TryReadCachedSkyLightLocked(x0 + 1, y0, z0, out samples[1])
                    || !TryReadCachedSkyLightLocked(x0, y0 + 1, z0, out samples[2])
                    || !TryReadCachedSkyLightLocked(x0 + 1, y0 + 1, z0, out samples[3])
                    || !TryReadCachedSkyLightLocked(x0, y0, z0 + 1, out samples[4])
                    || !TryReadCachedSkyLightLocked(x0 + 1, y0, z0 + 1, out samples[5])
                    || !TryReadCachedSkyLightLocked(x0, y0 + 1, z0 + 1, out samples[6])
                    || !TryReadCachedSkyLightLocked(x0 + 1, y0 + 1, z0 + 1, out samples[7]))
            {
                return false;
            }
        }

        lightLevel = VoxelLighting.InterpolateSkyLight(
                samples[0], samples[1], samples[2], samples[3],
                samples[4], samples[5], samples[6], samples[7],
                tx, ty, tz);
        return true;
    }

    // _mutex must be held by the caller. Vertical samples outside the world
    // are known boundaries and do not require a cached chunk.
    private bool TryReadCachedSkyLightLocked(int x, int y, int z, out byte light)
    {
        if (y < 0)
        {
            light = 0;
            return true;
        }
        if (y >= Height)
        {
            light = VoxelLighting.MaxLevel;
            return true;
        }

        int chunkX = Mathf.FloorToInt(x / (float)ChunkX);
        int chunkZ = Mathf.FloorToInt(z / (float)ChunkZ);
        if (!_voxelLightCache.TryGetValue(new Vector2I(chunkX, chunkZ), out VoxelLightData? data))
        {
            light = 0;
            return false;
        }

        int localX = x - chunkX * ChunkX;
        int localZ = z - chunkZ * ChunkZ;
        light = (byte)(data.Packed[VoxelLightData.Index(localX, y, localZ, Height)] >> 4);
        return true;
    }

    // Called while SnapshotLock and _mutex are held by SetBlock.
    // Updates all nine already-cached chunk maps as one volume so propagation
    // remains continuous across chunk seams. Missing cache entries fall back
    // to the existing lazy region solve.
    private bool TryApplyIncrementalBlockLightLocked(
            Vector2I center, int worldX, int y, int worldZ, byte emission)
    {
        const int side = ChunkX * 3;
        int width = side;
        int depth = ChunkZ * 3;
        int plane = width * depth;
        int volume = plane * Height;
        VoxelLightData[] maps = new VoxelLightData[9];
        byte[][] chunks = new byte[9][];
        byte[][]? stateChunks = null;
        bool hasNonDefaultStates = false;

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Vector2I cp = center + new Vector2I(dx, dz);
            int slot = (dz + 1) * 3 + dx + 1;
            if (!_voxelLightCache.TryGetValue(cp, out VoxelLightData? map)
                    || !_dataCache.TryGetValue(cp, out byte[]? blocks))
                return false;
            maps[slot] = map;
            chunks[slot] = blocks;
            byte[] states = ChunkBlockStates(cp);
            if (states.Length > 0)
            {
                (stateChunks ??= new byte[9][])[slot] = states;
                hasNonDefaultStates = true;
            }
        }

        byte[] regionBlocks = ArrayPool<byte>.Shared.Rent(volume);
        byte[] regionLight = ArrayPool<byte>.Shared.Rent(volume);
        byte[]? regionStates = hasNonDefaultStates
                ? ArrayPool<byte>.Shared.Rent(volume) : null;
        try
        {
            if (regionStates != null)
            {
                // Rented arrays may contain data from an earlier use. States
                // are sparse, so clear the complete active volume before copy.
                Array.Clear(regionStates, 0, volume);
            }

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int slot = (dz + 1) * 3 + dx + 1;
                byte[] chunkBlocks = chunks[slot];
                byte[] packed = maps[slot].Packed;
                for (int yLocal = 0; yLocal < Height; yLocal++)
                for (int zLocal = 0; zLocal < ChunkZ; zLocal++)
                {
                    int source = (yLocal * ChunkZ + zLocal) * ChunkX;
                    int target = yLocal * plane
                            + (dz + 1) * ChunkZ * width
                            + zLocal * width
                            + (dx + 1) * ChunkX;
                    Array.Copy(chunkBlocks, source, regionBlocks, target, ChunkX);
                    if (regionStates != null && stateChunks?[slot] is { Length: > 0 } chunkStates)
                        Array.Copy(chunkStates, source, regionStates, target, ChunkX);
                    for (int xLocal = 0; xLocal < ChunkX; xLocal++)
                        regionLight[target + xLocal] = (byte)(packed[source + xLocal] & 0x0F);
                }
            }

            int localX = worldX - (center.X - 1) * ChunkX;
            int localZ = worldZ - (center.Y - 1) * ChunkZ;
            int changedIndex = localX + localZ * width + y * plane;
            VoxelLighting.ApplyIncrementalBlockLight(
                    regionBlocks, width, Height, depth, regionLight, changedIndex, emission, regionStates);

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int slot = (dz + 1) * 3 + dx + 1;
                byte[] packed = maps[slot].Packed;
                for (int yLocal = 0; yLocal < Height; yLocal++)
                for (int zLocal = 0; zLocal < ChunkZ; zLocal++)
                {
                    int source = yLocal * plane
                            + (dz + 1) * ChunkZ * width
                            + zLocal * width
                            + (dx + 1) * ChunkX;
                    int target = (yLocal * ChunkZ + zLocal) * ChunkX;
                    for (int xLocal = 0; xLocal < ChunkX; xLocal++)
                        packed[target + xLocal] = (byte)((packed[target + xLocal] & 0xF0)
                                | regionLight[source + xLocal]);
                }

                _blockLightCache.Remove(center + new Vector2I(dx, dz));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(regionBlocks);
            ArrayPool<byte>.Shared.Return(regionLight);
            if (regionStates != null)
            {
                ArrayPool<byte>.Shared.Return(regionStates);
            }
        }
        return true;
    }

    // Called while SnapshotLock and _mutex are held by SetBlock.
    private void InvalidateBlockLightingLocked(Vector2I cp)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2I affected = cp + new Vector2I(dx, dz);
                _blockLightCache.Remove(affected);
                _voxelLightCache.Remove(affected);
            }
    }

    // Called while SnapshotLock and _mutex are held by SetBlock.
    private void InvalidateLightingLocked(Vector2I cp)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2I affected = cp + new Vector2I(dx, dz);
                _skyLightCache.Remove(affected);
                _blockLightCache.Remove(affected);
                _voxelLightCache.Remove(affected);
            }
    }

    // -----------------------------------------------------------------------
    // Block access
    // -----------------------------------------------------------------------

    /// <summary>Reads an already-streamed chunk without generating chunk data.</summary>
    public bool TryGetLoadedBlock(int x, int y, int z, out BlockId id)
    {
        id = BlockId.Air;
        if (y < 0 || y >= Height) return false;
        Vector2I cp = new(Mathf.FloorToInt(x / (float)ChunkX), Mathf.FloorToInt(z / (float)ChunkZ));
        if (!_chunks.TryGetValue(cp, out Chunk? chunk)) return false;
        int lx = x - cp.X * ChunkX;
        int lz = z - cp.Y * ChunkZ;
        int index = Chunk.Index(lx, y, lz);
        if (index < 0 || index >= chunk.Blocks.Length) return false;
        lock (SnapshotLock) id = (BlockId)chunk.Blocks[index];
        return true;
    }

    public BlockId GetBlock(int x, int y, int z)
    {
        if (y < 0 || y >= Height)
        {
            return BlockId.Air;
        }

        Vector2I cp = new(Mathf.FloorToInt(x / (float)ChunkX), Mathf.FloorToInt(z / (float)ChunkZ));
        int lx = x - cp.X * ChunkX;
        int lz = z - cp.Y * ChunkZ;
        return (BlockId)ChunkData(cp)[lx + lz * ChunkX + y * ChunkX * ChunkZ];
    }

    /// <summary>Backward-compatible reads: absent state entries are state zero.</summary>
    public byte GetBlockState(int x, int y, int z)
    {
        if (y < 0 || y >= Height) return 0;
        Vector2I cp = new(Mathf.FloorToInt(x / (float)ChunkX), Mathf.FloorToInt(z / (float)ChunkZ));
        int lx = x - cp.X * ChunkX;
        int lz = z - cp.Y * ChunkZ;
        int index = lx + lz * ChunkX + y * ChunkX * ChunkZ;
        lock (_mutex)
            return _blockStateEdits.TryGetValue(cp, out Dictionary<int, byte>? states)
                && states.TryGetValue(index, out byte state) ? state : (byte)0;
    }

    /// <summary>State array aligned with ChunkData; default-state chunks allocate no dense buffer.</summary>
    public byte[] ChunkBlockStates(Vector2I cp)
    {
        lock (_mutex)
        {
            if (_blockStateCache.TryGetValue(cp, out byte[]? cached)) return cached;
            if (!_blockStateEdits.TryGetValue(cp, out Dictionary<int, byte>? edits) || edits.Count == 0)
            {
                _blockStateCache[cp] = System.Array.Empty<byte>();
                return System.Array.Empty<byte>();
            }

            byte[] states = new byte[ChunkX * Height * ChunkZ];
            foreach ((int index, byte state) in edits)
                if ((uint)index < (uint)states.Length) states[index] = state;
            _blockStateCache[cp] = states;
            return states;
        }
    }

    public bool SetBlock(int x, int y, int z, BlockId id) => SetBlock(x, y, z, id, 0);

    internal static bool IsMutationBlockIdDefined(BlockId id) => BlockDb.IsDefined(id);

    /// <summary>All block and shape-state edits pass through this mutation seam.</summary>
    public bool SetBlock(int x, int y, int z, BlockId id, byte state)
    {
        // BlockId values are persisted and index several definition tables.
        // Reject unknown values before normalization or any mutation side effect.
        if (IsReadOnly || !IsMutationBlockIdDefined(id))
        {
            return false;
        }

        state = BlockShapes.NormalizeState(id, state);
        if (y <= 0 || y >= Height)
        {
            return false;
        }

        if (id == BlockId.Bedrock)
        {
            return false;
        }

        Vector2I cp = new(Mathf.FloorToInt(x / (float)ChunkX), Mathf.FloorToInt(z / (float)ChunkZ));
        if (!_chunks.TryGetValue(cp, out Chunk? c))
        {
            return false;
        }

        int lx = x - cp.X * ChunkX;
        int lz = z - cp.Y * ChunkZ;
        int i = lx + lz * ChunkZ + y * ChunkX * ChunkZ;
        bool isCampfireCell = IsCampfireSiteCell(x, y, z);
        bool lightingChanged = false;
        lock (SnapshotLock)
        {
            // Chunk.Blocks is also read by worker meshing. Mutate it while
            // holding the snapshot lock so payload data and EditEpoch agree.
            byte oldBlock = c.Blocks[i];
            byte oldState = c.BlockStates.Length > 0 ? c.BlockStates[i] : (byte)0;
            if (!c.SetBlockLocal(lx, y, lz, id, state))
            {
                return false;
            }

            UpdateLightSource(x, y, z, oldBlock, id, isCampfireCell);
            UpdateCropSource(x, y, z, id);
            lock (_mutex)
            {
                if (!_edits.TryGetValue(cp, out Dictionary<int, BlockId>? d))
                {
                    d = new Dictionary<int, BlockId>();
                    _edits[cp] = d;
                }

                d[i] = id;
                _dataCache[cp] = c.Blocks;
                if (state == 0)
                {
                    if (_blockStateEdits.TryGetValue(cp, out Dictionary<int, byte>? existingStates)
                            && existingStates.Remove(i) && existingStates.Count == 0)
                    {
                        _blockStateEdits.Remove(cp);
                        c.ReleaseBlockStates();
                    }
                }
                else
                {
                    if (!_blockStateEdits.TryGetValue(cp, out Dictionary<int, byte>? stateEdits))
                        _blockStateEdits[cp] = stateEdits = [];
                    stateEdits[i] = state;
                }
                _blockStateCache[cp] = c.BlockStates;
                UpdateEditTop(cp, lx, lz, c.Blocks);

                BlockId oldId = (BlockId)oldBlock;
                byte oldOpacity = VoxelLighting.EffectiveOpacity(oldId, oldState);
                byte newOpacity = VoxelLighting.EffectiveOpacity(id, state);
                uint oldLightFaces = VoxelLighting.LightPassageMask(oldId, oldState);
                uint newLightFaces = VoxelLighting.LightPassageMask(id, state);
                bool oldCampfireEmitter = isCampfireCell && oldBlock == (byte)BlockId.Log;
                bool newCampfireEmitter = isCampfireCell && id == BlockId.Log;
                byte oldEmission = oldBlock < BlockDb.LightEmission.Length
                        ? BlockDb.LightEmission[oldBlock] : (byte)0;
                byte newEmission = BlockDb.IsDefined(id)
                        ? BlockDb.LightEmission[(int)id] : (byte)0;
                if (oldCampfireEmitter) oldEmission = VoxelLighting.CampfireEmission;
                if (newCampfireEmitter) newEmission = VoxelLighting.CampfireEmission;

                bool sourcePositionChanged = oldEmission > 0 && newEmission > 0
                        && oldCampfireEmitter != newCampfireEmitter;
                lightingChanged = oldOpacity != newOpacity
                        || oldLightFaces != newLightFaces
                        || sourcePositionChanged
                        || oldEmission != newEmission;
                Vector3I changedSource = oldEmission > 0
                        ? new Vector3I(x, y + (oldCampfireEmitter ? 1 : 0), z)
                        : new Vector3I(x, y + (newCampfireEmitter ? 1 : 0), z);

                if (oldOpacity != newOpacity || oldLightFaces != newLightFaces)
                {
                    // Opacity changes affect direct sun and cave spill. Drop
                    // the packed maps so the next snapshot rebuilds skylight
                    // from the edited block volume.
                    InvalidateLightingLocked(cp);
                }
                else if (sourcePositionChanged)
                {
                    // A log-backed campfire emits from its flame cell above
                    // the log, while a torch block emits from its own cell.
                    // Equal-strength replacement still moves the source.
                    InvalidateBlockLightingLocked(cp);
                }
                else if (oldEmission != newEmission
                        && !TryApplyIncrementalBlockLightLocked(
                                cp, changedSource.X, changedSource.Y, changedSource.Z, newEmission))
                {
                    // No complete cached 3x3 exists yet: keep the lazy full
                    // solve as the safe fallback, without discarding valid sky.
                    InvalidateBlockLightingLocked(cp);
                }

                EditEpoch += 1;
            }
        }

        _dirty = true;
        BlockChanged?.Invoke(new Vector3I(x, y, z));

        // The edited chunk always changes. Geometry-only dependencies are
        // limited to the sampled edge/corner; light-affecting edits retain the
        // full existing 3x3 region for the 14-voxel propagation halo.
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!ChunkStreamingPolicy.AffectsChunk(dx, dz, lx, lz,
                        ChunkX, ChunkZ, lightingChanged))
                {
                    continue;
                }

                Vector2I affected = cp + new Vector2I(dx, dz);
                if (_chunks.ContainsKey(affected))
                {
                    EnqueueRebuild(affected);
                }
            }
        }

        return true;
    }

    public static string ContainerKey(Vector3I pos) => $"{pos.X},{pos.Y},{pos.Z}";

    public Container GetContainer(Vector3I pos)
    {
        string key = ContainerKey(pos);
        if (!Containers.TryGetValue(key, out Container? container))
        {
            container = new Container();
            Containers[key] = container;
        }

        return container;
    }

    public bool TryGetContainer(Vector3I pos, out Container? container) =>
        Containers.TryGetValue(ContainerKey(pos), out container);

    public void RemoveContainer(Vector3I pos) => Containers.Remove(ContainerKey(pos));

    public FurnaceData GetFurnace(Vector3I pos)
    {
        string key = ContainerKey(pos);
        if (!Furnaces.TryGetValue(key, out FurnaceData? furnace))
        {
            furnace = new FurnaceData();
            Furnaces[key] = furnace;
        }

        return furnace;
    }

    public bool TryGetFurnace(Vector3I pos, out FurnaceData? furnace) =>
        Furnaces.TryGetValue(ContainerKey(pos), out furnace);

    public void RemoveFurnace(Vector3I pos) => Furnaces.Remove(ContainerKey(pos));

    /// <summary>All registered emitters, nearest first (debug/legacy API).</summary>
    public List<Vector3I> NearestLights(Vector3 pos, int max, float radius = 32.0f)
    {
        List<(Vector3I Pos, float Dist)> found = [];
        float radiusSq = radius * radius;
        lock (_mutex)
        {
            foreach (Vector3I cell in _lightSources.Keys)
            {
                Vector3 center = new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
                float dist = center.DistanceSquaredTo(pos);
                if (dist <= radiusSq) found.Add((cell, dist));
            }
        }
        found.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        List<Vector3I> result = [];
        for (int i = 0; i < found.Count && i < max; i++) result.Add(found[i].Pos);
        return result;
    }

    /// <summary>Emitter snapshot covering the halo that can influence one chunk.</summary>
    public List<VoxelLightSource> NearbyLightSources(int minX, int maxX, int minZ, int maxZ)
    {
        List<VoxelLightSource> result = [];
        lock (_mutex)
        {
            foreach ((Vector3I pos, byte level) in _lightSources)
                if (level > 0 && pos.X >= minX && pos.X <= maxX && pos.Z >= minZ && pos.Z <= maxZ)
                    result.Add(new VoxelLightSource(pos, level));
        }
        return result;
    }

    private bool IsCampfireSiteCell(int x, int y, int z)
    {
        Vector3I? site = CachedCampfireSite(
                Mathf.FloorToInt(x / (float)Campfires.Cell),
                Mathf.FloorToInt(z / (float)Campfires.Cell));
        return site is { } position
                && position.X == x && position.Y == y && position.Z == z;
    }

    private void UpdateLightSource(
            int x, int y, int z, byte oldBlock, BlockId id, bool isCampfireCell)
    {
        bool wasCampfireEmitter = isCampfireCell && oldBlock == (byte)BlockId.Log;
        bool isCampfireEmitter = isCampfireCell && id == BlockId.Log;
        bool hadSource = wasCampfireEmitter
                || oldBlock < BlockDb.LightEmission.Length && BlockDb.LightEmission[oldBlock] > 0;
        bool hasSource = isCampfireEmitter
                || BlockDb.IsDefined(id) && BlockDb.LightEmission[(int)id] > 0;
        Vector3I oldCell = wasCampfireEmitter
                ? new Vector3I(x, y + 1, z)
                : new Vector3I(x, y, z);
        Vector3I newCell = isCampfireEmitter
                ? new Vector3I(x, y + 1, z)
                : new Vector3I(x, y, z);
        byte emission = isCampfireEmitter
                ? VoxelLighting.CampfireEmission
                : BlockDb.IsDefined(id) ? BlockDb.LightEmission[(int)id] : (byte)0;

        lock (_mutex)
        {
            if (hadSource && (!hasSource || oldCell != newCell))
                _lightSources.Remove(oldCell);
            if (emission > 0) _lightSources[newCell] = emission;
        }
    }

    private void RegisterCropSourcesLocked(Vector2I cp, byte[] blocks)
    {
        if (!_cropScannedChunks.Add(cp))
        {
            return;
        }

        int stride = ChunkX * ChunkZ;
        int ox = cp.X * ChunkX;
        int oz = cp.Y * ChunkZ;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (!BlockDb.IsWheat((BlockId)blocks[i]))
            {
                continue;
            }

            int y = i / stride;
            int rem = i - y * stride;
            int z = rem / ChunkX;
            int x = rem - z * ChunkX;
            _crops.Add(new Vector3I(ox + x, y, oz + z));
        }
    }

    private void UpdateCropSource(int x, int y, int z, BlockId id)
    {
        Vector3I cell = new(x, y, z);
        lock (_mutex)
        {
            if (BlockDb.IsWheat(id))
            {
                _crops.Add(cell);
            }
            else
            {
                _crops.Remove(cell);
            }
        }
    }

    /// <summary>Snapshot for the growth tick (avoids mutating the set mid-loop).</summary>
    public Vector3I[] CropSnapshot()
    {
        lock (_mutex)
        {
            return [.. _crops];
        }
    }

    // -----------------------------------------------------------------------
    // Edit-aware surface heights: per-chunk topmost solid block of every
    // edited column, so the far LOD can show player edits after unload.
    // -----------------------------------------------------------------------

    private readonly Dictionary<Vector2I, byte[]> _editTops = new();
    private readonly Dictionary<Vector2I, byte[]> _editTopBlocks = new();

    /// <summary>Rescan one edited column's topmost solid block (air/water/plants ignored).</summary>
    private void UpdateEditTop(Vector2I cp, int lx, int lz, byte[] blocks)
    {
        int col = lx + lz * ChunkX;
        int top = 0;
        for (int y = Height - 1; y >= 1; y--)
        {
            BlockId id = (BlockId)blocks[col + y * ChunkX * ChunkZ];
            if (id != BlockId.Air && !BlockDb.IsWater(id) && !BlockDb.IsCross(id)
                    && id != BlockId.Leaves && id != BlockId.BirchLeaves)
            {
                top = y;
                break;
            }
        }

        if (!_editTops.TryGetValue(cp, out byte[]? tops))
        {
            tops = new byte[ChunkX * ChunkZ];
            _editTops[cp] = tops;
        }

        if (!_editTopBlocks.TryGetValue(cp, out byte[]? topBlocks))
        {
            topBlocks = new byte[ChunkX * ChunkZ];
            _editTopBlocks[cp] = topBlocks;
        }

        tops[col] = (byte)top;
        topBlocks[col] = top > 0
                ? blocks[col + top * ChunkX * ChunkZ]
                : (byte)BlockId.Air;
    }

    /// <summary>Topmost solid block of a column from player edits (false = no edits here).</summary>
    public bool TryEditTop(int wx, int wz, out int topY) =>
        TryEditTop(wx, wz, out topY, out _);

    /// <summary>Topmost edited column height and its visible block type, without querying generated terrain.</summary>
    public bool TryEditTop(int wx, int wz, out int topY, out BlockId topBlock)
    {
        topY = 0;
        topBlock = BlockId.Air;
        Vector2I cp = new(Mathf.FloorToInt(wx / (float)ChunkX), Mathf.FloorToInt(wz / (float)ChunkZ));
        lock (_mutex)
        {
            if (!_editTops.TryGetValue(cp, out byte[]? tops)
                    || !_editTopBlocks.TryGetValue(cp, out byte[]? topBlocks))
            {
                return false;
            }

            int lx = wx - cp.X * ChunkX;
            int lz = wz - cp.Y * ChunkZ;
            int column = lx + lz * ChunkX;
            topY = tops[column];
            topBlock = (BlockId)topBlocks[column];
            return topY > 0;
        }
    }

    // -----------------------------------------------------------------------
    // Chunk streaming
    // -----------------------------------------------------------------------

    public void SetupInitial()
    {
        Vector2I pcp = PlayerChunk();

        // Only the player's current chunk is required before the first physics
        // step. Its floor/collider is ready synchronously; surrounding chunks
        // enter the existing bounded worker pipeline immediately afterwards.
        BuildNow(pcp);

        _lastPlayerChunk = pcp;
        _lastPlayerPos = Player?.GlobalPosition ?? Vector3.Zero;
        RebuildQueue(pcp);
    }

    /// <summary>
    /// Synchronous chunk build for the menu backdrop. No player is required;
    /// the camera orbits this centre while the menu is open.
    /// </summary>
    public void BuildPreview(Vector3 center, int radius)
    {
        Vector2I pcp = new(
                Mathf.FloorToInt(center.X / ChunkX),
                Mathf.FloorToInt(center.Z / ChunkZ));
        int r = Mathf.Clamp(radius, 1, 5);
        _lastPlayerChunk = pcp;
        _lastPlayerPos = center;
        _velocityDir = Vector3.Zero;
        _lastPriorityDirection = Vector3.Zero;

        lock (_queueLock)
        {
            _streamCenter = pcp;
            _streamRetentionLimit = r + UnloadMargin;
            _queue.Clear();
            _queued.Clear();
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    Vector2I cp = pcp + new Vector2I(dx, dz);
                    if (!_chunks.ContainsKey(cp))
                    {
                        QueueFreshLocked(cp, pcp);
                    }
                }
            }
        }
    }

    /// <summary>Change the streamed radius (gfx presets / F7-F8) and re-evaluate the queue.</summary>
    public void SetViewDistance(int v)
    {
        ViewDistance = Mathf.Clamp(v, 2, 24);
        if (Player == null)
        {
            return;
        }

        Vector2I pcp = PlayerChunk();
        RebuildQueue(pcp);
        UnloadFar(pcp);
        _farLod?.UpdateAround(pcp);
    }

    /// <summary>Change the far-view LOD ring radius in metres (gfx presets; 0 disables it).</summary>
    public void SetLodRadius(float radiusM)
    {
        LodRadius = Mathf.Max(radiusM, 0.0f);
        if (Player != null && _farLod != null)
        {
            _farLod.UpdateAround(PlayerChunk());
        }
    }

    /// <summary>
    /// Snapshot managed voxel-cache byte arrays and estimates for edit/light
    /// metadata. Array sizes are exact; dictionary entry sizes are estimates.
    /// Applied mesh bytes describe source arrays, not driver-specific VRAM.
    /// </summary>
    public VoxelCacheMemorySnapshot GetCacheMemorySnapshot()
    {
        long blocks = 0, states = 0, sky = 0, block = 0, packed = 0;
        long editMetadata = 0, lightSourceEstimate = 0;
        lock (_mutex)
        {
            foreach (byte[] data in _dataCache.Values) blocks += data.LongLength;
            foreach (byte[] data in _blockStateCache.Values) states += data.LongLength;
            foreach (byte[] data in _skyLightCache.Values) sky += data.LongLength;
            foreach (byte[] data in _blockLightCache.Values) block += data.LongLength;
            foreach (VoxelLightData data in _voxelLightCache.Values)
                packed += data.Packed.LongLength + data.DirectSkyPacked.LongLength;
            foreach (Dictionary<int, BlockId> edits in _edits.Values)
                editMetadata += 64L + edits.Count * 24L;
            foreach (Dictionary<int, byte> edits in _blockStateEdits.Values)
                editMetadata += 64L + edits.Count * 16L;
            lightSourceEstimate = _lightSources.Count * 32L;
        }

        long appliedMeshBytes = 0;
        foreach (Chunk chunk in _chunks.Values) appliedMeshBytes += chunk.EstimatedMeshBytes;
        long budget = StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(
                ViewDistance, UnloadMargin, ChunkX, Height, ChunkZ);
        return new VoxelCacheMemorySnapshot(blocks, states, sky, block, packed,
                editMetadata, lightSourceEstimate, appliedMeshBytes,
                PayloadPool.RetainedBufferBytes, budget);
    }

    /// <summary>Synchronous build (startup / when a node must exist immediately).</summary>
    private Chunk BuildNow(Vector2I cp)
    {
        if (_chunks.TryGetValue(cp, out Chunk? existing))
        {
            return existing;
        }

        Chunk c = new();
        AddChild(c);
        c.Setup(this, cp);
        c.Generate();
        MeshPayload payload = Mesher.Build(this, cp);
        c.ApplyPayload(payload);
        PayloadPool.Release(payload);
        _chunks[cp] = c;
        return c;
    }

    /// <summary>
    /// One worker-loop element: pulls chunks (rebuilds first, then nearest
    /// fresh) until both queues are empty, meshes off-thread and hands the
    /// payload to the main thread. MaxInFlight loops run concurrently inside
    /// the active WorkerThreadPool group.
    /// </summary>
    private void WorkerLoop(int element)
    {
        while (_alive)
        {
            Vector2I cp;
            bool rebuild;
            lock (_queueLock)
            {
                if (!TryDequeueWorkLocked(out cp, out rebuild))
                {
                    return;
                }
            }

            bool reserved = false;
            bool published = false;
            bool retryRequest = false;
            MeshPayload? payload = null;
            try
            {
                // Capacity is released by the main thread after applying a
                // payload. Revalidate after waiting because travel can make
                // this request obsolete while the worker is blocked here.
                if (!IsWithinCurrentRetention(cp))
                {
                    return;
                }

                _completedPayloadSlots.Wait(_workerShutdown.Token);
                reserved = true;
                if (!_alive || !IsWithinCurrentRetention(cp)) return;

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                payload = Mesher.Build(this, cp);
                Profiler.RecordBuild(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                if (!_alive || !IsWithinCurrentRetention(cp))
                {
                    PayloadPool.Release(payload);
                    payload = null;
                    return;
                }

                lock (_queueLock)
                {
                    // Publish under the same lock used to change the streaming
                    // center. A completed worker must not slip an obsolete
                    // rebuild payload into the apply queue after pruning ran.
                    if (!_alive || !ChunkStreamingPolicy.IsWithinRetention(
                            cp, _streamCenter, _streamRetentionLimit))
                    {
                        PayloadPool.Release(payload);
                        payload = null;
                        return;
                    }

                    _workerFailureAttempts.Remove(cp);
                    Profiler.RecordPayloadEnqueued(payload.EstimatedBufferBytes);
                    (rebuild ? _applyRebuild : _applyFresh).Enqueue((cp, payload));
                    payload = null;
                    published = true;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                int attempts;
                lock (_queueLock)
                {
                    attempts = _workerFailureAttempts.TryGetValue(cp, out int previous)
                            ? previous + 1 : 1;
                    if (ChunkStreamingPolicy.ShouldRetry(attempts, MaxWorkerBuildAttempts)
                            && _alive)
                    {
                        _workerFailureAttempts[cp] = attempts;
                        retryRequest = true;
                    }
                    else
                    {
                        _workerFailureAttempts.Remove(cp);
                    }
                }

                string retryText = retryRequest
                        ? $" (retry {attempts}/{MaxWorkerBuildAttempts})"
                        : $" (gave up after {attempts} attempts; a later streaming/edit request can retry)";
                GD.PushError($"minecraft: chunk worker failed at ({cp.X},{cp.Y}){retryText}: {exception}");
                if (payload != null)
                {
                    PayloadPool.Release(payload);
                    payload = null;
                }
            }
            finally
            {
                if (reserved && !published)
                {
                    _completedPayloadSlots.Release();
                }

                bool outsideRetention;
                Vector2I streamCenter;
                int cacheLimit;
                lock (_queueLock)
                {
                    bool rebuildRequested = _workTracker.Complete(cp);
                    streamCenter = _streamCenter;
                    cacheLimit = _streamRetentionLimit + 1;
                    outsideRetention = !ChunkStreamingPolicy.IsWithinRetention(
                            cp, _streamCenter, _streamRetentionLimit);
                    if (_alive && !outsideRetention)
                    {
                        if (rebuildRequested || retryRequest && rebuild)
                        {
                            QueueRebuildLocked(cp, _streamCenter);
                        }
                        else if (retryRequest)
                        {
                            QueueFreshLocked(cp, _streamCenter);
                        }
                    }
                }

                // A build can populate a halo cache after UnloadFar has
                // already evicted it. Re-prune stale worker output on completion.
                if (_alive && outsideRetention)
                {
                    PruneWorldCaches(streamCenter, cacheLimit);
                }
            }
        }
    }

    /// <summary>Removes stale heap entries and claims one coordinate exactly once.</summary>
    private bool TryDequeueWorkLocked(out Vector2I cp, out bool rebuild)
    {
        while (true)
        {
            if (_rebuildQueue.TryDequeue(out cp, out _))
            {
                if (!_rebuildQueued.Remove(cp))
                {
                    continue; // stale heap entry superseded by a newer request
                }

                rebuild = true;
            }
            else if (_queue.TryDequeue(out cp, out _))
            {
                if (!_queued.Remove(cp))
                {
                    continue; // e.g. a rebuild superseded this fresh request
                }

                rebuild = false;
            }
            else
            {
                rebuild = false;
                return false;
            }

            if (_workTracker.TryBegin(cp))
            {
                return true;
            }

            // Defensive recovery if duplicate heap entries ever survive a
            // queue transition while another worker owns the coordinate.
            if (rebuild)
            {
                _workTracker.RequestRebuild(cp);
            }
        }
    }

    private bool IsWithinCurrentRetention(Vector2I cp)
    {
        lock (_queueLock)
        {
            return _alive && ChunkStreamingPolicy.IsWithinRetention(
                    cp, _streamCenter, _streamRetentionLimit);
        }
    }

    /// <summary>
    /// Main-thread application of a worker payload: create a streamed chunk,
    /// re-mesh an edited one, or re-queue when the world changed meanwhile.
    /// </summary>
    private void ApplyBuild(Vector2I cp, MeshPayload payload)
    {
        try
        {
            if (!_alive) return;

            Vector2I pcp = Player != null ? PlayerChunk() : _streamCenter;
            int limit = Player != null ? ViewDistance + UnloadMargin : _streamRetentionLimit;
            if (!ChunkStreamingPolicy.IsWithinRetention(cp, pcp, limit))
                return;

            if (payload.Epoch != EditEpoch)
            {
                EnqueueRebuild(cp);
                return;
            }

            if (_chunks.TryGetValue(cp, out Chunk? existing))
            {
                existing.ApplyPayload(payload);   // player-edit remesh
                return;
            }

            Chunk c = new();
            AddChild(c);
            c.Setup(this, cp);
            c.Generate();
            c.ApplyPayload(payload);
            c.FadeIn();
            _chunks[cp] = c;
        }
        finally
        {
            PayloadPool.Release(payload);
        }
    }

    /// <summary>Nearest dry-land column to the origin, so the player never spawns in water.</summary>
    public Vector3 FindSpawn()
    {
        Vector3 best = Vector3.Zero;
        long bestD = long.MaxValue;
        SearchSpawnCandidates(64, 4, ref best, ref bestD);

        // The normal origin search retains its exact historical selection.
        // Only when it finds no valid candidate do we expand deterministically.
        if (bestD == long.MaxValue)
        {
            SearchSpawnCandidates(512, 8, ref best, ref bestD);
        }

        if (bestD == long.MaxValue)
        {
            throw new InvalidOperationException(
                    "Unable to find a safe spawn location within 512 blocks of the world origin.");
        }

        return best;
    }

    private void SearchSpawnCandidates(int radius, int step, ref Vector3 best, ref long bestD)
    {
        for (int x = -radius; x <= radius; x += step)
        {
            for (int z = -radius; z <= radius; z += step)
            {
                int h = SurfaceHeight(x, z);
                if (h <= SeaLevel + 1 || h >= Height - 2
                        || IsSky && !IslandAt(x, z) || SpawnBlocked(x, z))
                {
                    continue;
                }

                long distanceSquared = (long)x * x + (long)z * z;
                if (distanceSquared < bestD)
                {
                    bestD = distanceSquared;
                    best = new Vector3(x + 0.5f, h + 1.5f, z + 0.5f);
                }
            }
        }
    }

    /// <summary>
    /// True when a column is occupied by a tree, cactus, campfire or structure
    /// site — spawning inside one drops the player through the concave mesh.
    /// </summary>
    private bool SpawnBlocked(int x, int z)
    {
        if (GetTreeAt(x, z) != null || GetCactusAt(x, z) != null)
        {
            return true;
        }

        if (_genVersion >= 4 && Hash2(x * 3 + 1, z * 5 + 7) % 1024 < 2)
        {
            return true;   // boulder decorator
        }

        if (CachedCampfireSite(Mathf.FloorToInt(x / (float)Campfires.Cell),
                Mathf.FloorToInt(z / (float)Campfires.Cell)) is { } fire
                && fire.X == x && fire.Z == z)
        {
            return true;
        }

        if (CachedStructureSite(Mathf.FloorToInt(x / (float)StructureCell),
                Mathf.FloorToInt(z / (float)StructureCell)) is { } site
                && Mathf.Abs(site.X - x) <= 6 && Mathf.Abs(site.Z - z) <= 6)
        {
            return true;
        }

        if (_genVersion >= 8
                && GetVillageSite(Mathf.FloorToInt(x / (float)VillageCell),
                        Mathf.FloorToInt(z / (float)VillageCell)) is { } village
                && Mathf.Abs(village.X - x) <= 52 && Mathf.Abs(village.Z - z) <= 52)
        {
            return true;
        }

        return false;
    }

    public Vector2I PlayerChunk()
    {
        if (Player == null)
        {
            return Vector2I.Zero;
        }

        return new Vector2I(
            Mathf.FloorToInt(Player.GlobalPosition.X / ChunkX),
            Mathf.FloorToInt(Player.GlobalPosition.Z / ChunkZ));
    }

    public override void _Process(double delta)
    {
        bool hasPlayer = Player != null;
        if (hasPlayer)
        {
            _unsavedPlaytimeSeconds += Math.Max(0.0, delta);

            // Smoothed velocity drives the prefetch bias: chunks ahead of a moving
            // player jump the queue, so flying toward new terrain streams faster.
            Vector3 pos = Player!.GlobalPosition;
            if (delta > 0.0)
            {
                Vector3 displacement = pos - _lastPlayerPos;
                displacement.Y = 0.0f;
                Vector3 vel = displacement / (float)delta;
                // A discontinuous relocation is not movement intent. Normal
                // walking/sprinting/flying remains well below this threshold.
                _velocityDir = displacement.LengthSquared() > 256.0f
                        || vel.LengthSquared() > 400.0f
                        ? Vector3.Zero
                        : vel.LengthSquared() > 1.0f ? vel.Normalized() : Vector3.Zero;
            }

            _lastPlayerPos = pos;
        }

        Vector2I pcp = hasPlayer ? PlayerChunk() : _lastPlayerChunk;
        bool centerChanged = pcp != _lastPlayerChunk;
        bool priorityChanged = ChunkStreamingPolicy.DirectionChanged(
                _lastPriorityDirection, _velocityDir);
        if (centerChanged)
        {
            _lastPlayerChunk = pcp;
            RebuildQueue(pcp);
            UnloadFar(pcp);
            _farLod?.UpdateAround(pcp);
        }
        else if (priorityChanged)
        {
            RefreshFreshQueuePriorities(pcp);
            _lastPriorityDirection = _velocityDir;
        }

        if (_groupActive && WorkerThreadPool.IsGroupTaskCompleted(_group))
        {
            // Completion does not release the WorkerThreadPool group handle.
            // Wait once to reclaim it before dispatching the next batch or
            // quitting the scene; otherwise shutdown reports live Group objects.
            WorkerThreadPool.WaitForGroupTaskCompletion(_group);
            _groupActive = false;
        }

        Dispatch();

        // Keep player-edit rebuilds ahead of fresh terrain, but stop after a
        // small main-thread time slice. At least one payload is always applied
        // so progress cannot stall when a single collider is expensive.
        long applyStart = System.Diagnostics.Stopwatch.GetTimestamp();
        int applied = 0;
        while (!StreamingApplyBudget.ShouldStop(
                System.Diagnostics.Stopwatch.GetElapsedTime(applyStart).TotalMilliseconds,
                applied, ApplyBudgetMilliseconds))
        {
            (Vector2I Cp, MeshPayload Payload) item;
            if (!_applyRebuild.TryDequeue(out item) && !_applyFresh.TryDequeue(out item))
                break;

            Profiler.RecordPayloadDequeued(item.Payload.EstimatedBufferBytes);
            long itemStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                ApplyBuild(item.Cp, item.Payload);
            }
            finally
            {
                Profiler.RecordApply(System.Diagnostics.Stopwatch.GetElapsedTime(itemStart).TotalMilliseconds);
                _completedPayloadSlots.Release();
            }
            applied++;
        }

        lock (_queueLock)
        {
            Profiler.FreshQueued = _queue.Count;
            Profiler.RebuildQueued = _rebuildQueue.Count;
        }

        Profiler.WorkersActive = _groupActive;
        lock (_mutex)
        {
            Profiler.DataChunks = _dataCache.Count;
        }

        _memorySampleTimer += delta;
        if (_memorySampleTimer >= MemorySampleInterval)
        {
            _memorySampleTimer = 0.0;
            Profiler.RecordCacheMemory(GetCacheMemorySnapshot());
        }

        _autosaveTimer += delta;
        if (_autosaveTimer >= AutosaveInterval)
        {
            _autosaveTimer = 0.0;
            if (hasPlayer)
            {
                if (Player!.GlobalPosition.DistanceTo(_lastSavedPos) > 8.0f)
                {
                    _dirty = true;
                }

                if (_dirty || _unsavedPlaytimeSeconds >= AutosaveInterval)
                {
                    SaveNow();
                }
            }
        }
    }

    /// <summary>Queue every missing chunk inside the streamed square, nearest-first.</summary>
    private void RebuildQueue(Vector2I pcp)
    {
        int limit = ViewDistance + UnloadMargin;
        lock (_queueLock)
        {
            _streamCenter = pcp;
            _streamRetentionLimit = limit;
            _lastPriorityDirection = _velocityDir;

            // Rebuild requests are only useful while their coordinates remain
            // in the retained region. Reconstructing the bounded heap also
            // removes stale entries that were superseded by fresh requests.
            foreach (Vector2I stale in _rebuildQueued.ToArray())
            {
                if (!ChunkStreamingPolicy.IsWithinRetention(stale, pcp, limit))
                {
                    _rebuildQueued.Remove(stale);
                    _workTracker.ForgetFollowUp(stale);
                }
            }

            _rebuildQueue.Clear();
            foreach (Vector2I cp in _rebuildQueued)
            {
                _rebuildQueue.Enqueue(cp, Priority(cp, pcp));
            }

            _queue.Clear();
            _queued.Clear();
            int r = ViewDistance;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    Vector2I cp = pcp + new Vector2I(dx, dz);
                    if (!_chunks.ContainsKey(cp)
                            && !_rebuildQueued.Contains(cp)
                            && !_workTracker.IsInFlight(cp))
                    {
                        QueueFreshLocked(cp, pcp);
                    }
                }
            }
        }

        // Completed rebuilds are bounded by the payload semaphore. Release
        // obsolete results now so they cannot sit ahead of current-area work.
        PrunePendingRebuildPayloads(pcp, limit);
    }

    private void PrunePendingRebuildPayloads(Vector2I center, int limit)
    {
        int pending = _applyRebuild.Count;
        for (int i = 0; i < pending; i++)
        {
            if (!_applyRebuild.TryDequeue(out var item))
            {
                break;
            }

            if (ChunkStreamingPolicy.IsWithinRetention(item.Cp, center, limit))
            {
                _applyRebuild.Enqueue(item);
                continue;
            }

            Profiler.RecordPayloadDequeued(item.Payload.EstimatedBufferBytes);
            PayloadPool.Release(item.Payload);
            _completedPayloadSlots.Release();
        }
    }

    /// <summary>Nearest-first priority with a velocity prefetch bias (lower = sooner).</summary>
    private float Priority(Vector2I cp, Vector2I pcp)
    {
        Vector2I off = cp - pcp;
        float pri = off.LengthSquared();
        if (_velocityDir != Vector3.Zero)
        {
            pri -= (off.X * _velocityDir.X + off.Y * _velocityDir.Z) * 2.5f;
        }

        return pri;
    }

    /// <summary>Queue one fresh chunk nearest-first; the side set keeps entries unique.</summary>
    private void Enqueue(Vector2I cp, Vector2I pcp)
    {
        lock (_queueLock)
        {
            QueueFreshLocked(cp, pcp);
        }
    }

    private void QueueFreshLocked(Vector2I cp, Vector2I pcp)
    {
        if (!_workTracker.IsInFlight(cp)
                && !_rebuildQueued.Contains(cp)
                && _queued.Add(cp))
        {
            _queue.Enqueue(cp, Priority(cp, pcp));
        }
    }

    /// <summary>Queue a player-edit remesh; rebuilds always win over fresh streaming.</summary>
    private void EnqueueRebuild(Vector2I cp)
    {
        lock (_queueLock)
        {
            QueueRebuildLocked(cp, _streamCenter);
        }
    }

    private void QueueRebuildLocked(Vector2I cp, Vector2I center)
    {
        if (!ChunkStreamingPolicy.IsWithinRetention(cp, center, _streamRetentionLimit))
        {
            return;
        }

        if (_workTracker.IsInFlight(cp))
        {
            _workTracker.RequestRebuild(cp);
            return;
        }

        if (_rebuildQueued.Add(cp))
        {
            // The stale heap node in _queue is harmless: TryDequeueWorkLocked
            // verifies side-set membership before assigning work.
            _queued.Remove(cp);
            _rebuildQueue.Enqueue(cp, Priority(cp, center));
        }
    }

    private void RefreshFreshQueuePriorities(Vector2I pcp)
    {
        lock (_queueLock)
        {
            _queue.Clear();
            foreach (Vector2I cp in _queued)
            {
                _queue.Enqueue(cp, Priority(cp, pcp));
            }
        }
    }

    /// <summary>Re-mesh every built chunk (used when the meshing mode changes).</summary>
    public void RebuildAll()
    {
        foreach (Vector2I cp in _chunks.Keys.ToArray())
        {
            EnqueueRebuild(cp);
        }
    }

    private void Dispatch()
    {
        if (_groupActive)
        {
            return;
        }

        bool any;
        lock (_queueLock)
        {
            any = _rebuildQueue.Count > 0 || _queue.Count > 0;
        }

        if (!any)
        {
            return;
        }

        if (UseThreads)
        {
            // One persistent-style group per batch: each element is a worker
            // loop that keeps pulling until the queues drain.
            _group = WorkerThreadPool.AddGroupTask(
                    Callable.From<int>(WorkerLoop), MaxInFlight, MaxInFlight, true, "chunk_stream");
            _groupActive = true;
        }
        else
        {
            while (true)
            {
                Vector2I cp;
                lock (_queueLock)
                {
                    if (!TryDequeueWorkLocked(out cp, out _))
                    {
                        break;
                    }
                }

                try
                {
                    MeshPayload payload = Mesher.Build(this, cp);
                    ApplyBuild(cp, payload);
                }
                finally
                {
                    lock (_queueLock)
                    {
                        bool rebuildRequested = _workTracker.Complete(cp);
                        if (_alive && rebuildRequested
                                && ChunkStreamingPolicy.IsWithinRetention(
                                        cp, _streamCenter, _streamRetentionLimit))
                        {
                            QueueRebuildLocked(cp, _streamCenter);
                        }
                    }
                }
            }
        }
    }

    private void UnloadFar(Vector2I pcp)
    {
        int limit = ViewDistance + UnloadMargin;

        // ToArray snapshots the keys so the dictionary can be modified mid-loop.
        foreach (Vector2I cp in _chunks.Keys.ToArray())
        {
            if (Mathf.Abs(cp.X - pcp.X) > limit || Mathf.Abs(cp.Y - pcp.Y) > limit)
            {
                Chunk c = _chunks[cp];
                _chunks.Remove(cp);
                c.QueueFree();
            }
        }

        // Keep the one-chunk data/light halo required for neighbour snapshots.
        PruneWorldCaches(pcp, limit + 1);
    }

    /// <summary>
    /// Evict generated data and deterministic spatial lookup caches outside
    /// the retained region. Active snapshots/chunks retain their own array
    /// references, so removing a cache entry does not invalidate in-flight work.
    /// </summary>
    private void PruneWorldCaches(Vector2I center, int limit)
    {
        lock (_mutex)
        {
            bool Outside(Vector2I cp) =>
                    !ChunkStreamingPolicy.IsWithinRetention(cp, center, limit);
            foreach (Vector2I cp in _dataCache.Keys.Where(Outside).ToArray()) _dataCache.Remove(cp);
            foreach (Vector2I cp in _blockStateCache.Keys.Where(Outside).ToArray()) _blockStateCache.Remove(cp);
            foreach (Vector2I cp in _skyLightCache.Keys.Where(Outside).ToArray()) _skyLightCache.Remove(cp);
            foreach (Vector2I cp in _blockLightCache.Keys.Where(Outside).ToArray()) _blockLightCache.Remove(cp);
            foreach (Vector2I cp in _voxelLightCache.Keys.Where(Outside).ToArray()) _voxelLightCache.Remove(cp);
            foreach (Vector2I cp in _sourceScannedChunks.Where(Outside).ToArray()) _sourceScannedChunks.Remove(cp);
            foreach (Vector2I cp in _cropScannedChunks.Where(Outside).ToArray()) _cropScannedChunks.Remove(cp);
            foreach (Vector3I crop in _crops.Where(pos =>
                    Outside(new Vector2I(
                            Mathf.FloorToInt(pos.X / (float)ChunkX),
                            Mathf.FloorToInt(pos.Z / (float)ChunkZ)))).ToArray())
                _crops.Remove(crop);
            foreach (Vector3I pos in _lightSources.Keys.Where(pos =>
                    Outside(new Vector2I(
                            Mathf.FloorToInt(pos.X / (float)ChunkX),
                            Mathf.FloorToInt(pos.Z / (float)ChunkZ)))).ToArray())
                _lightSources.Remove(pos);

            // Procedural site lookups, including cached null results, are
            // deterministic and may be safely recomputed after spatial eviction.
            bool SiteOutside(long key, int cellSize) =>
                    ChunkStreamingPolicy.IsSiteCellOutsideRetention(
                            unchecked((int)(key >> 32)),
                            unchecked((int)(uint)key),
                            cellSize, center, limit, ChunkX);
            foreach (long key in _campfireCache.Keys
                    .Where(key => SiteOutside(key, Campfires.Cell)).ToArray())
                _campfireCache.Remove(key);
            foreach (long key in _structureCache.Keys
                    .Where(key => SiteOutside(key, StructureCell)).ToArray())
                _structureCache.Remove(key);
            foreach (long key in _villageCache.Keys
                    .Where(key => SiteOutside(key, VillageCell)).ToArray())
                _villageCache.Remove(key);
        }
    }

    // -----------------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------------

    public void SaveNow()
    {
        if (_worldReadOnlyReason != null)
        {
            if (!_readOnlyWarningShown)
            {
                GD.PushWarning($"minecraft: world is read-only: {_worldReadOnlyReason}");
                _readOnlyWarningShown = true;
            }
            return;
        }

        BeforeSave?.Invoke();
        Dictionary<string, Dictionary<string, BlockId>> editsOut = [];
        foreach ((Vector2I cp, Dictionary<int, BlockId> inner) in _edits)
        {
            editsOut[$"{cp.X},{cp.Y}"] = inner.ToDictionary(e => e.Key.ToString(), e => e.Value);
        }

        Dictionary<string, List<ItemStackSave>>? containers = null;
        if (Containers.Count > 0)
        {
            containers = [];
            foreach ((string key, Container container) in Containers)
            {
                List<ItemStackSave> items = container.ToSave();
                if (items.Count > 0)
                {
                    containers[key] = items;
                }
            }

            if (containers.Count == 0)
            {
                containers = null;
            }
        }

        Dictionary<string, FurnaceSave>? furnaces = null;
        if (Furnaces.Count > 0)
        {
            furnaces = [];
            foreach ((string key, FurnaceData furnace) in Furnaces)
            {
                if (!furnace.IsEmpty)
                {
                    furnaces[key] = new FurnaceSave(
                            furnace.ToSave(), furnace.BurnTime, furnace.BurnMax, furnace.SmeltTime);
                }
            }

            if (furnaces.Count == 0)
            {
                furnaces = null;
            }
        }

        Dictionary<string, Dictionary<string, byte>> blockStatesOut = [];
        foreach ((Vector2I cp, Dictionary<int, byte> states) in _blockStateEdits)
            if (states.Count > 0)
                blockStatesOut[$"{cp.X},{cp.Y}"] = states.ToDictionary(e => e.Key.ToString(), e => e.Value);

        WorldSave world = new(
                WorldStore.WorldFormatVersion,
                editsOut,
                TimeOfDay,
                containers,
                Flags.Count == 0 ? null : Flags,
                furnaces,
                blockStatesOut.Count == 0 ? null : blockStatesOut,
                Villagers.Count == 0 ? null : Villagers,
                Quests.Count == 0 ? null : Quests,
                BoatSystem != null ? BoatSystem.ToSave() : SavedBoats);
        bool worldSaved = WorldStore.WriteAtomic(
                WorldStore.WorldSavePath(_worldDir),
                JsonSerializer.Serialize(world, WorldStore.Json));

        float[] playerPos = Player is null
                ? []
                : [Player.GlobalPosition.X, Player.GlobalPosition.Y, Player.GlobalPosition.Z];
        float[]? respawn = Player?.RespawnPoint is { } rp ? [rp.X, rp.Y, rp.Z] : null;
        bool playerSaved = WorldStore.WriteAtomic(
                WorldStore.PlayerSavePath(_worldDir),
                JsonSerializer.Serialize(
                        new PlayerSave(
                                playerPos,
                                Player?.Selected ?? 0,
                                Player?.Inventory.ToSave(),
                                Player?.SaveMode,
                                Player?.Stats.Health ?? 0.0f,
                                Player?.Stats.Hunger ?? 0.0f,
                                respawn,
                                Player?.Stats.Experience ?? 0),
                        WorldStore.Json));

        long elapsedPlaytime = (long)_unsavedPlaytimeSeconds;
        WorldManifest updatedManifest = _manifest with
        {
            PlayedUtc = WorldStore.NowUtc(),
            PlaytimeSeconds = _manifest.PlaytimeSeconds + elapsedPlaytime,
        };
        bool manifestSaved = WorldStore.WriteManifest(_worldDir, updatedManifest);
        if (worldSaved && playerSaved && manifestSaved)
        {
            _manifest = updatedManifest;
            _unsavedPlaytimeSeconds = Math.Max(0.0, _unsavedPlaytimeSeconds - elapsedPlaytime);
            _dirty = false;
            if (Player != null)
            {
                _lastSavedPos = Player.GlobalPosition;
            }
        }
        else
        {
            _dirty = true;
            GD.PushWarning("minecraft: save incomplete; it will be retried on the next autosave");
        }
    }

    /// <summary>Focus-loss hook: write only when something actually changed.</summary>
    public void SaveIfDirty()
    {
        if (_dirty || _unsavedPlaytimeSeconds > 0.0)
        {
            SaveNow();
        }
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    private static bool HasSaveCandidate(string path) =>
            FileAccess.FileExists(path)
            || FileAccess.FileExists(path + ".bak")
            || FileAccess.FileExists(path + ".bak2");

    private void SetWorldReadOnly(string reason)
    {
        _worldReadOnlyReason ??= reason;
    }

    private void LoadSave()
    {
        string worldSavePath = WorldStore.WorldSavePath(_worldDir);
        WorldSave? world = WorldStore.ReadJson<WorldSave>(worldSavePath, WorldStore.Json);
        if (world == null && HasSaveCandidate(worldSavePath))
        {
            SetWorldReadOnly(
                    "world save is unreadable in the primary file and both backups; saving is disabled");
        }
        else if (world != null && !WorldCompatibilityPolicy.IsSupportedSaveVersion(
                world.Version, WorldStore.WorldFormatVersion))
        {
            SetWorldReadOnly(
                    $"world save format V{world.Version} is unsupported; " +
                    $"this build supports V1–V{WorldStore.WorldFormatVersion}");
        }

        if (world?.Edits != null)
        {
            foreach ((string key, Dictionary<string, BlockId> inner) in world.Edits)
            {
                string[] parts = key.Split(',');
                if (parts.Length != 2
                        || !int.TryParse(parts[0], out int cx)
                        || !int.TryParse(parts[1], out int cz))
                {
                    continue;
                }

                Dictionary<int, BlockId> edits = [];
                int volume = ChunkX * Height * ChunkZ;
                foreach ((string index, BlockId id) in inner)
                {
                    if (!int.TryParse(index, out int i))
                    {
                        continue;
                    }

                    if (!BlockDb.IsDefined(id))
                    {
                        SetWorldReadOnly(
                                $"saved block ID {(int)id} is not registered by this build; saving is disabled");
                        GD.PushWarning(
                                $"minecraft: unknown saved block ID {(int)id} in {_worldDir}; world opened read-only");
                        continue;
                    }

                    if (i < 0 || i >= volume)
                    {
                        GD.PushWarning($"minecraft: ignoring out-of-range edit index {i} in {_worldDir}");
                        continue;
                    }

                    edits[i] = id;
                    int stride = ChunkX * ChunkZ;
                    int y = i / stride;
                    int rem = i % stride;
                    int lz = rem / ChunkX;
                    int lx = rem % ChunkX;
                    byte emission = BlockDb.LightEmission[(int)id];
                    if (emission > 0)
                    {
                        _lightSources[new Vector3I(
                                cx * ChunkX + lx, y, cz * ChunkZ + lz)] = emission;
                    }

                    if (BlockDb.IsWheat(id))
                    {
                        _crops.Add(new Vector3I(cx * ChunkX + lx, y, cz * ChunkZ + lz));
                    }
                }

                _edits[new Vector2I(cx, cz)] = edits;
            }
        }

        // Optional, backward-compatible shape extension. Only valid non-zero
        // states on a persisted shape block are accepted; older saves omit it.
        if (world?.BlockStates != null)
        {
            foreach ((string key, Dictionary<string, byte> inner) in world.BlockStates)
            {
                string[] parts = key.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int cx)
                        || !int.TryParse(parts[1], out int cz)) continue;
                Vector2I cp = new(cx, cz);
                if (!_edits.TryGetValue(cp, out Dictionary<int, BlockId>? blocks)) continue;
                Dictionary<int, byte> states = [];
                foreach ((string index, byte state) in inner)
                {
                    if (!int.TryParse(index, out int i) || (uint)i >= (uint)(ChunkX * Height * ChunkZ)
                            || !blocks.TryGetValue(i, out BlockId id) || !BlockShapes.IsShape(id)
                            || state == 0 || !BlockShapes.IsValidState(id, state))
                        continue;
                    states[i] = state;
                }
                if (states.Count > 0) _blockStateEdits[cp] = states;
            }
        }

        // _editTops is normally maintained by SetBlock only; rebuild it from
        // the loaded edits so far LOD and the minimap show builds immediately
        // after a restart. Use the normal cache so the first later stream does
        // not generate the same terrain a second time.
        foreach ((Vector2I cp, Dictionary<int, BlockId> edits) in _edits)
        {
            if (edits.Count == 0)
            {
                continue;
            }

            byte[] data = ChunkData(cp);
            int stride = ChunkX * ChunkZ;
            foreach (int index in edits.Keys)
            {
                int rem = index % stride;
                UpdateEditTop(cp, rem % ChunkX, rem / ChunkX, data);
            }
        }

        if (world != null)
        {
            TimeOfDay = Mathf.Clamp(world.Time, 0.0, 1.0);
            SavedBoats = world.Boats;
            if (world.Containers != null)
            {
                foreach ((string key, List<ItemStackSave> items) in world.Containers)
                {
                    if (items is not null)
                    {
                        Container container = new();
                        container.LoadFrom(items);
                        Containers[key] = container;
                    }
                }
            }

            if (world.Flags != null)
            {
                foreach ((string key, bool value) in world.Flags)
                {
                    Flags[key] = value;
                }
            }

            if (world.Furnaces != null)
            {
                foreach ((string key, FurnaceSave saved) in world.Furnaces)
                {
                    FurnaceData furnace = new()
                    {
                        BurnTime = saved.BurnTime,
                        BurnMax = saved.BurnMax,
                        SmeltTime = saved.SmeltTime,
                    };
                    furnace.LoadFrom(saved.Slots);
                    Furnaces[key] = furnace;
                }
            }

            if (world.Villagers != null)
            {
                foreach ((string key, VillagerSave saved) in world.Villagers)
                {
                    if (saved != null && saved.Id == key
                            && saved.Position is { Length: 3 }
                            && saved.Home is { Length: 3 }
                            && saved.Work is { Length: 3 })
                    {
                        Villagers[key] = saved;
                    }
                }
            }

            if (world.Quests != null)
            {
                foreach ((string key, QuestProgressSave saved) in world.Quests)
                {
                    if (saved != null && saved.Stage >= 0 && saved.Progress >= 0)
                    {
                        Quests[key] = saved;
                    }
                }
            }
        }

        PlayerSave? player = WorldStore.ReadJson<PlayerSave>(
                WorldStore.PlayerSavePath(_worldDir), WorldStore.Json);
        if (player?.Pos is { Length: 3 } p)
        {
            SavedPlayerPos = new Vector3(p[0], p[1], p[2]);
        }

        if (player != null)
        {
            SavedInventory = player.Items;
            SavedSelected = player.Selected;
            SavedMode = player.Mode;
            SavedHealth = player.Health;
            SavedHunger = player.Hunger ?? -1.0f;
            SavedExperience = player.Experience;
            if (player.Respawn is { Length: 3 } r)
            {
                SavedRespawn = new Vector3(r[0], r[1], r[2]);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Deterministic hashes
    // -----------------------------------------------------------------------

    private static long Hash2(long x, long z)
    {
        long h = (x * 374761393 + z * 668265263) & 0xFFFFFFFFL;
        h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFFL;
        return (h ^ (h >> 16)) & 0xFFFFFFFFL;
    }

    private static long Hash3(long x, long y, long z)
    {
        long h = (x * 374761393 + y * 1013904223 + z * 668265263) & 0xFFFFFFFFL;
        h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFFL;
        return (h ^ (h >> 16)) & 0xFFFFFFFFL;
    }
}
