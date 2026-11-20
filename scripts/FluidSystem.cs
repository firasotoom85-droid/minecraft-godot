using Godot;

namespace Minecraft;

/// <summary>
/// Low-cost deterministic water propagation around the player. It reads only
/// streamed chunks and commits at most six edits per tick through SetBlock.
/// The fixed window, reused buffers and bounded interval avoid world scans,
/// chunk generation, worker contention and per-cell allocations.
/// </summary>
public partial class FluidSystem : Node
{
    private const int Radius = 7;
    private const int Width = Radius * 2 + 1;
    private const int Depth = Width;
    private const int Below = 8;
    private const int Above = 8;
    private const int Height = Below + Above + 1;
    private const int Plane = Width * Depth;
    private const int CellCount = Plane * Height;
    private const int MaxEditsPerTick = 6;
    private const double TickInterval = 0.28;

    private readonly BlockId[] _current = new BlockId[CellCount];
    private readonly BlockId[] _next = new BlockId[CellCount];
    private readonly bool[] _loaded = new bool[CellCount];

    private VoxelWorld? _world;
    private Player? _player;
    private double _accumulator;
    private int _originX;
    private int _originY;
    private int _originZ;

    public void Setup(VoxelWorld world, Player player)
    {
        _world = world;
        _player = player;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null) return;
        _accumulator += System.Math.Min(delta, 0.5);
        if (_accumulator < TickInterval) return;
        _accumulator = 0.0;
        Simulate();
    }

    private void Simulate()
    {
        _originX = Mathf.FloorToInt(_player!.GlobalPosition.X) - Radius;
        _originY = Mathf.FloorToInt(_player.GlobalPosition.Y) - Below;
        _originZ = Mathf.FloorToInt(_player.GlobalPosition.Z) - Radius;

        for (int y = 0; y < Height; y++)
        for (int z = 0; z < Depth; z++)
        for (int x = 0; x < Width; x++)
        {
            int i = Index(x, y, z);
            bool loaded = _world!.TryGetLoadedBlock(_originX + x, _originY + y,
                    _originZ + z, out BlockId id);
            _loaded[i] = loaded;
            _current[i] = loaded ? id : BlockId.Air;
            _next[i] = _current[i];
        }

        // Top-down, deterministic z/x ordering resolves competing flow proposals.
        for (int y = Height - 1; y >= 0; y--)
        for (int z = 0; z < Depth; z++)
        for (int x = 0; x < Width; x++)
        {
            int i = Index(x, y, z);
            int level = BlockDb.WaterLevel(_current[i]);
            if (level < 0) continue;

            if (level > 0)
            {
                int desired = FluidRules.ResolveFlowLevel(
                        WaterLevelAt(x, y + 1, z),
                        WaterLevelAt(x, y, z - 1),
                        WaterLevelAt(x, y, z + 1),
                        WaterLevelAt(x + 1, y, z),
                        WaterLevelAt(x - 1, y, z));
                if (desired < 0)
                {
                    _next[i] = BlockId.Air;
                    continue;
                }
                level = desired;
                _next[i] = FluidRules.BlockForLevel(level);
            }

            ReactWithLava(x, y, z);
            if (y > 0) OfferWater(x, y - 1, z, 1);
            int horizontalLevel = FluidRules.NextHorizontalLevel(level);
            if (horizontalLevel >= 0)
            {
                OfferWater(x - 1, y, z, horizontalLevel);
                OfferWater(x + 1, y, z, horizontalLevel);
                OfferWater(x, y, z - 1, horizontalLevel);
                OfferWater(x, y, z + 1, horizontalLevel);
            }
        }

        int edits = 0;
        for (int y = 0; y < Height && edits < MaxEditsPerTick; y++)
        {
            int worldY = _originY + y;
            if (worldY <= 0 || worldY >= _world!.Height) continue;
            for (int z = 0; z < Depth && edits < MaxEditsPerTick; z++)
            for (int x = 0; x < Width && edits < MaxEditsPerTick; x++)
            {
                int i = Index(x, y, z);
                if (!_loaded[i] || _next[i] == _current[i]) continue;
                if (_world.SetBlock(_originX + x, worldY, _originZ + z, _next[i])) edits++;
            }
        }
    }

    private int WaterLevelAt(int x, int y, int z)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth) return -1;
        int i = Index(x, y, z);
        return _loaded[i] ? BlockDb.WaterLevel(_current[i]) : -1;
    }

    private void OfferWater(int x, int y, int z, int level)
    {
        if (level is < 1 or > FluidRules.MaxFlowLevel
                || x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth) return;
        int i = Index(x, y, z);
        if (!_loaded[i]) return;

        BlockId existing = _current[i];
        if (existing == BlockId.Lava)
        {
            _next[i] = FluidRules.ReactWithWater(existing);
            return;
        }
        if (BlockDb.IsWater(existing))
        {
            int currentLevel = BlockDb.WaterLevel(existing);
            if (currentLevel == 0) return;
            int plannedLevel = BlockDb.WaterLevel(_next[i]);
            if (level < currentLevel && (plannedLevel < 0 || level < plannedLevel))
                _next[i] = FluidRules.BlockForLevel(level);
            return;
        }
        int nextLevel = BlockDb.WaterLevel(_next[i]);
        if (FluidRules.CanFlowInto(existing) && (nextLevel < 0 || level < nextLevel))
            _next[i] = FluidRules.BlockForLevel(level);
    }

    private void ReactWithLava(int x, int y, int z)
    {
        ReactAt(x - 1, y, z);
        ReactAt(x + 1, y, z);
        ReactAt(x, y - 1, z);
        ReactAt(x, y + 1, z);
        ReactAt(x, y, z - 1);
        ReactAt(x, y, z + 1);
    }

    private void ReactAt(int x, int y, int z)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth) return;
        int i = Index(x, y, z);
        if (_loaded[i] && _current[i] == BlockId.Lava)
            _next[i] = FluidRules.ReactWithWater(_current[i]);
    }

    private static int Index(int x, int y, int z) => x + z * Width + y * Plane;
}
