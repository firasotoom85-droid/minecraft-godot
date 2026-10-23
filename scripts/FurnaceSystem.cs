using Godot;

namespace Minecraft;

/// <summary>
/// Ticks furnace state. Furnaces continue while their chunk is unloaded so a
/// stack of ore can finish smelting while the player builds; saving is left to
/// the world's normal autosave policy.
/// </summary>
public partial class FurnaceSystem : Node
{
    private VoxelWorld? _world;

    public void Setup(VoxelWorld world) => _world = world;

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }

        bool changed = false;
        foreach (FurnaceData furnace in _world.Furnaces.Values)
        {
            changed |= furnace.Tick((float)delta);
        }

        if (changed)
        {
            _world.MarkDirty();
        }
    }
}
