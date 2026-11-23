using Godot;

namespace Minecraft;

/// <summary>
/// Slow random growth for placed wheat. Daytime and an open block above are
/// required; growth is deliberately unhurried so a small field is a routine,
/// not a waiting screen.
/// </summary>
public partial class CropSystem : Node
{
    private const double Interval = 5.0;
    private const float GrowthChance = 0.10f;

    private VoxelWorld? _world;
    private double _timer = Interval;

    public void Setup(VoxelWorld world) => _world = world;

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }

        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = Interval;
        if (DayNight.IsNight(_world.TimeOfDay))
        {
            return;
        }

        foreach (Vector3I cell in _world.CropSnapshot())
        {
            ProcessCrop(cell, false);
        }
    }

    /// <summary>
    /// Processes only streamed crop cells. Missing support removes the crop through
    /// the normal mutation path; unavailable cells are left untouched until reloaded.
    /// </summary>
    private bool ProcessCrop(Vector3I cell, bool forceGrowth)
    {
        if (_world == null
                || !_world.TryGetLoadedBlock(cell.X, cell.Y, cell.Z, out BlockId id))
            return false;

        int stage = id switch
        {
            BlockId.Wheat0 => 0,
            BlockId.Wheat1 => 1,
            BlockId.Wheat2 => 2,
            _ => -1,
        };
        if (stage < 0 || stage >= 2) return false;

        if (!_world.TryGetLoadedBlock(cell.X, cell.Y - 1, cell.Z, out BlockId support))
            return false;
        if (support != BlockId.Farmland)
            return _world.SetBlock(cell.X, cell.Y, cell.Z, BlockId.Air);

        if (!_world.TryGetLoadedBlock(cell.X, cell.Y + 1, cell.Z, out BlockId above)
                || above != BlockId.Air)
            return false;

        if (!forceGrowth && GD.Randf() > GrowthChance) return false;
        return _world.SetBlock(cell.X, cell.Y, cell.Z,
                (BlockId)((int)BlockId.Wheat0 + stage + 1));
    }

    internal bool ProcessCropForTests(Vector3I cell) => ProcessCrop(cell, true);
}
