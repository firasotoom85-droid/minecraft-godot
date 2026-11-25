using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Deterministic, event-driven fluid updates over loaded world cells.
/// Unknown (unloaded) neighbours are never treated as air, and all edits go
/// through VoxelWorld.SetBlock so saves, lighting and meshing stay coherent.
/// </summary>
public partial class FluidSystem : Node
{
    private const int DiscoveryRadius = 7;
    private const int DiscoveryBelow = 8;
    private const int DiscoveryAbove = 8;
    private const int MaxActiveCellsPerTick = 96;
    private const int MaxEditsPerTick = 6;
    private const double TickInterval = 0.28;
    private const double DiscoveryRetryInterval = 2.0;

    private readonly Queue<Vector3I> _active = new();
    private readonly HashSet<Vector3I> _queued = [];
    private readonly HashSet<Vector3I> _sources = [];
    private readonly List<Vector3I> _sourceScratch = [];
    private VoxelWorld? _world;
    private Player? _player;
    private double _accumulator;
    private double _discoveryRetryTimer;
    private Vector2I _lastDiscoveryChunk = new(int.MinValue, int.MinValue);
    private int _lastDiscoveryYBand = int.MinValue;
    private int _sourcePulse;
    private int _editsRemaining;

    public int ActiveCellCount => _active.Count;
    public int ActiveSourceCount => _sources.Count;

    public void Setup(VoxelWorld world, Player player)
    {
        AttachWorld(world);
        _player = player;
    }

    private void AttachWorld(VoxelWorld world)
    {
        if (ReferenceEquals(_world, world)) return;
        if (_world != null) _world.BlockChanged -= OnWorldBlockChanged;
        _world = world;
        _world.BlockChanged += OnWorldBlockChanged;
    }

    private void OnWorldBlockChanged(Vector3I position) => EnqueueWithNeighbours(position);

    public override void _ExitTree()
    {
        if (_world != null) _world.BlockChanged -= OnWorldBlockChanged;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null) return;
        _discoveryRetryTimer -= System.Math.Min(delta, 0.5);
        _accumulator += System.Math.Min(delta, 0.5);
        if (_accumulator < TickInterval) return;
        _accumulator = 0.0;

        Vector3 position = _player.GlobalPosition;
        if (ShouldDiscoverLoadedWater(position))
            DiscoverLoadedWater(position);

        if (++_sourcePulse >= 4)
        {
            _sourcePulse = 0;
            foreach (Vector3I source in _sources) Enqueue(source);
        }
        ProcessActiveCells(MaxActiveCellsPerTick, MaxEditsPerTick);
    }

    private bool ShouldDiscoverLoadedWater(Vector3 position)
    {
        Vector2I chunk = new(
                Mathf.FloorToInt(position.X / VoxelWorld.ChunkX),
                Mathf.FloorToInt(position.Z / VoxelWorld.ChunkZ));
        int yBand = Mathf.FloorToInt(position.Y / 8.0f);
        if (chunk == _lastDiscoveryChunk && yBand == _lastDiscoveryYBand
                && _discoveryRetryTimer > 0.0)
            return false;

        _lastDiscoveryChunk = chunk;
        _lastDiscoveryYBand = yBand;
        _discoveryRetryTimer = DiscoveryRetryInterval;
        return true;
    }

    internal void DiscoverForTests(VoxelWorld world, Vector3 center, double elapsed)
    {
        AttachWorld(world);
        _discoveryRetryTimer -= System.Math.Min(elapsed, 0.5);
        if (ShouldDiscoverLoadedWater(center)) DiscoverLoadedWater(center);
    }

    private void DiscoverLoadedWater(Vector3 center)
    {
        if (_world == null) return;

        // Do not keep pulsing sources from chunks the player has left behind.
        _sourceScratch.Clear();
        foreach (Vector3I source in _sources)
        {
            if (System.Math.Abs(source.X - center.X) > DiscoveryRadius + 1
                    || System.Math.Abs(source.Y - center.Y) > DiscoveryBelow + 1
                    || System.Math.Abs(source.Z - center.Z) > DiscoveryRadius + 1)
                _sourceScratch.Add(source);
        }
        foreach (Vector3I source in _sourceScratch) _sources.Remove(source);
        int ox = Mathf.FloorToInt(center.X) - DiscoveryRadius;
        int oy = Mathf.FloorToInt(center.Y) - DiscoveryBelow;
        int oz = Mathf.FloorToInt(center.Z) - DiscoveryRadius;
        for (int y = 0; y < DiscoveryBelow + DiscoveryAbove + 1; y++)
            for (int z = 0; z < DiscoveryRadius * 2 + 1; z++)
                for (int x = 0; x < DiscoveryRadius * 2 + 1; x++)
                {
                    int wx = ox + x;
                    int wy = oy + y;
                    int wz = oz + z;
                    if (!_world.TryGetLoadedBlock(wx, wy, wz, out BlockId id)
                            || !BlockDb.IsWater(id)) continue;
                    Vector3I cell = new(wx, wy, wz);
                    if (BlockDb.WaterLevel(id) == 0) _sources.Add(cell);
                    EnqueueWithNeighbours(cell);
                }
    }

    private void ProcessActiveCells(int budget, int editBudget)
    {
        if (_world == null) return;
        int processed = 0;
        int edits = 0;
        _editsRemaining = editBudget;
        while (processed < budget && edits < editBudget && _editsRemaining > 0 && _active.Count > 0)
        {
            Vector3I cell = _active.Dequeue();
            _queued.Remove(cell);
            processed++;
            if (!_world.TryGetLoadedBlock(cell.X, cell.Y, cell.Z, out BlockId id)
                    || !BlockDb.IsWater(id))
            {
                _sources.Remove(cell);
                continue;
            }

            int level = BlockDb.WaterLevel(id);
            if (level == 0)
            {
                _sources.Add(cell);
                edits += ReactAdjacentLava(cell);
                edits += SpreadFromSource(cell);
                continue;
            }

            _sources.Remove(cell);
            edits += ReactAdjacentLava(cell);
            edits += UpdateFlowingCell(cell, level);
        }
    }

    private int SpreadFromSource(Vector3I cell)
    {
        if (_world == null) return 0;
        Vector3I below = cell + Vector3I.Down;
        if (!TryGetLoaded(below, out BlockId belowId)) return 0;
        if (belowId == BlockId.Lava) return ReactAt(below);
        if (FluidRules.CanFlowInto(belowId)) return TryPlaceFlow(below, 1);

        int edits = 0;
        edits += OfferHorizontal(cell, Vector3I.Left, 1);
        edits += OfferHorizontal(cell, Vector3I.Right, 1);
        edits += OfferHorizontal(cell, new Vector3I(0, 0, -1), 1);
        edits += OfferHorizontal(cell, new Vector3I(0, 0, 1), 1);
        return edits;
    }

    private int UpdateFlowingCell(Vector3I cell, int level)
    {
        if (_world == null) return 0;
        Vector3I below = cell + Vector3I.Down;
        if (!TryGetLoaded(below, out BlockId belowId)) return 0;
        if (belowId == BlockId.Lava) return ReactAt(below);
        if (FluidRules.CanFlowInto(belowId)) return TryPlaceFlow(below, 1);

        int best = -1;
        bool unknownNeighbour = false;
        int aboveLevel = -1;
        if (!TryGetLoaded(cell + Vector3I.Up, out BlockId aboveId))
            unknownNeighbour = true;
        else
            aboveLevel = BlockDb.WaterLevel(aboveId);
        AccumulateSupport(cell + Vector3I.Left, ref best, ref unknownNeighbour);
        AccumulateSupport(cell + Vector3I.Right, ref best, ref unknownNeighbour);
        AccumulateSupport(cell + new Vector3I(0, 0, -1), ref best, ref unknownNeighbour);
        AccumulateSupport(cell + new Vector3I(0, 0, 1), ref best, ref unknownNeighbour);

        if (best < 0 && aboveLevel < 0)
        {
            if (unknownNeighbour) return 0;
            if (SetFluidBlock(cell, BlockId.Air))
            {
                EnqueueWithNeighbours(cell);
                return 1;
            }
            return 0;
        }

        int desired = aboveLevel >= 0 ? 1 : best + 1;
        if (desired > FluidRules.MaxFlowLevel)
        {
            if (unknownNeighbour) return 0;
            if (SetFluidBlock(cell, BlockId.Air))
            {
                EnqueueWithNeighbours(cell);
                return 1;
            }
            return 0;
        }

        if (unknownNeighbour && desired > level) return 0;

        int edits = 0;
        BlockId desiredBlock = FluidRules.BlockForLevel(desired);
        if (desiredBlock != FluidRules.BlockForLevel(level)
                && SetFluidBlock(cell, desiredBlock))
        {
            edits++;
            EnqueueWithNeighbours(cell);
        }

        edits += OfferHorizontal(cell, Vector3I.Left, desired + 1);
        edits += OfferHorizontal(cell, Vector3I.Right, desired + 1);
        edits += OfferHorizontal(cell, new Vector3I(0, 0, -1), desired + 1);
        edits += OfferHorizontal(cell, new Vector3I(0, 0, 1), desired + 1);
        return edits;
    }

    private void AccumulateSupport(Vector3I position, ref int best, ref bool unknown)
    {
        if (!TryGetLoaded(position, out BlockId id))
        {
            unknown = true;
            return;
        }
        int level = BlockDb.WaterLevel(id);
        if (level >= 0 && (best < 0 || level < best)) best = level;
    }

    private int OfferHorizontal(Vector3I source, Vector3I offset, int level)
    {
        if (level > FluidRules.MaxFlowLevel) return 0;
        Vector3I target = source + offset;
        if (!TryGetLoaded(target, out BlockId existing)) return 0;
        if (existing == BlockId.Lava) return ReactAt(target);
        if (BlockDb.IsWater(existing))
        {
            if (BlockDb.WaterLevel(existing) == 0) return 0;
            if (TryRegenerateSource(target)) return 1;
            return 0;
        }
        if (!FluidRules.CanFlowInto(existing)) return 0;
        if (TryRegenerateSource(target)) return 1;
        return TryPlaceFlow(target, level);
    }

    private bool TryRegenerateSource(Vector3I target)
    {
        if (_world == null || target.Y <= 1) return false;
        if (!TryGetLoaded(target + Vector3I.Down, out BlockId floor)
                || !BlockDb.IsSolid(floor)) return false;
        int sources = 0;
        if (IsSource(target + Vector3I.Left)) sources++;
        if (IsSource(target + Vector3I.Right)) sources++;
        if (IsSource(target + new Vector3I(0, 0, -1))) sources++;
        if (IsSource(target + new Vector3I(0, 0, 1))) sources++;
        bool replaceable = TryGetLoaded(target, out BlockId current)
                && (current == BlockId.Air || BlockDb.IsWater(current)
                        && BlockDb.WaterLevel(current) > 0);
        if (!FluidRules.CanRegenerateSource(sources, true, replaceable)) return false;
        if (!SetFluidBlock(target, BlockId.Water)) return false;
        _sources.Add(target);
        EnqueueWithNeighbours(target);
        return true;
    }

    private bool IsSource(Vector3I position) =>
            TryGetLoaded(position, out BlockId id) && id == BlockId.Water;

    private int ReactAdjacentLava(Vector3I cell)
    {
        int edits = 0;
        edits += ReactAt(cell + Vector3I.Up);
        edits += ReactAt(cell + Vector3I.Down);
        edits += ReactAt(cell + Vector3I.Left);
        edits += ReactAt(cell + Vector3I.Right);
        edits += ReactAt(cell + new Vector3I(0, 0, -1));
        edits += ReactAt(cell + new Vector3I(0, 0, 1));
        return edits;
    }

    private int ReactAt(Vector3I position)
    {
        if (_world == null || !TryGetLoaded(position, out BlockId id)
                || id != BlockId.Lava) return 0;

        BlockId reactedBlock = FluidRules.ReactWithWater(id);
        if (_world.Player is { } player
                && Player.BlockIntersectsPlayerCapsule(reactedBlock, 0,
                        new Vector3(position.X, position.Y, position.Z),
                        player.GlobalPosition, Player.CollisionRadius, Player.CollisionHeight))
        {
            // Keep the lava until the capsule has moved clear; the source pulse
            // will revisit it without creating collision inside the player.
            return 0;
        }

        if (!SetFluidBlock(position, reactedBlock)) return 0;
        EnqueueWithNeighbours(position);
        return 1;
    }

    private int TryPlaceFlow(Vector3I target, int level)
    {
        if (_world == null || level is < 1 or > FluidRules.MaxFlowLevel) return 0;
        if (!TryGetLoaded(target, out BlockId existing)) return 0;
        if (existing == BlockId.Lava) return ReactAt(target);
        if (!FluidRules.CanFlowInto(existing)) return 0;
        if (!SetFluidBlock(target, FluidRules.BlockForLevel(level))) return 0;
        EnqueueWithNeighbours(target);
        return 1;
    }

    private bool SetFluidBlock(Vector3I position, BlockId id)
    {
        if (_world == null || _editsRemaining <= 0) return false;
        if (!_world.SetBlock(position.X, position.Y, position.Z, id)) return false;
        _editsRemaining--;
        return true;
    }

    private bool TryGetLoaded(Vector3I position, out BlockId id)
    {
        if (_world == null)
        {
            id = BlockId.Air;
            return false;
        }
        if (position.Y < 0 || position.Y >= _world.Height)
        {
            id = BlockId.Bedrock;
            return true;
        }
        return _world.TryGetLoadedBlock(position.X, position.Y, position.Z, out id);
    }

    private void EnqueueWithNeighbours(Vector3I cell)
    {
        Enqueue(cell);
        Enqueue(cell + Vector3I.Up);
        Enqueue(cell + Vector3I.Down);
        Enqueue(cell + Vector3I.Left);
        Enqueue(cell + Vector3I.Right);
        Enqueue(cell + new Vector3I(0, 0, -1));
        Enqueue(cell + new Vector3I(0, 0, 1));
    }

    private void Enqueue(Vector3I cell)
    {
        if (_queued.Add(cell)) _active.Enqueue(cell);
    }

    internal void SimulateForTests(VoxelWorld world, Vector3 center, int budget = 256)
    {
        AttachWorld(world);
        DiscoverLoadedWater(center);
        ProcessActiveCells(budget, 64);
    }
}
