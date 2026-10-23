using Godot;

namespace Minecraft;

/// <summary>
/// Unlocks curated achievements and stores them in the world's persistent
/// flags. Event-driven for blocks/inventory/sleep, with a light per-frame check
/// for dawn and depth.
/// </summary>
public partial class AchievementSystem : Node
{
    private VoxelWorld? _world;
    private Player? _player;
    private Hud? _hud;

    private bool _sawNight;
    private bool _deep;

    public void Setup(VoxelWorld world, Player player, Hud hud)
    {
        _world = world;
        _player = player;
        _hud = hud;

        player.BlockBroken += (_, _) => Unlock("first_block");
        player.BlockPlaced += (_, blockId) =>
        {
            if (blockId == (int)BlockId.Torch)
            {
                Unlock("first_torch");
            }

            BuildFlag(blockId);
        };
        player.Slept += () => Unlock("first_night");
        player.Inventory.Changed += CheckInventory;
        CheckInventory();
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null)
        {
            return;
        }

        if (DayNight.IsNight(_world.TimeOfDay))
        {
            _sawNight = true;
        }
        else if (_sawNight)
        {
            Unlock("first_night");
        }

        if (!_deep && _player.GlobalPosition.Y < 20.0f)
        {
            _deep = true;
            Unlock("deep");
        }
    }

    private void CheckInventory()
    {
        if (_player != null && _player.Inventory.CountOf(ItemId.IronIngot) > 0)
        {
            Unlock("first_iron");
        }
    }

    private void BuildFlag(int blockId)
    {
        if (_world == null)
        {
            return;
        }

        string? flag = (BlockId)blockId switch
        {
            BlockId.Bed => "build/bed",
            BlockId.Chest => "build/chest",
            BlockId.Furnace => "build/furnace",
            _ => null,
        };
        if (flag is null)
        {
            return;
        }

        _world.Flags[flag] = true;
        _world.MarkDirty();
        if (_world.Flags.TryGetValue("build/bed", out bool bed)
                && _world.Flags.TryGetValue("build/chest", out bool chest)
                && _world.Flags.TryGetValue("build/furnace", out bool furnace)
                && bed && chest && furnace)
        {
            Unlock("homestead");
        }
    }

    private void Unlock(string id)
    {
        if (_world == null || _world.Flags.ContainsKey("ach/" + id))
        {
            return;
        }

        Achievement? def = null;
        foreach (Achievement candidate in AchievementDb.All)
        {
            if (candidate.Id == id)
            {
                def = candidate;
                break;
            }
        }

        if (def is null)
        {
            return;
        }

        _world.Flags["ach/" + id] = true;
        _world.MarkDirty();
        _hud?.ShowToast($"Achievement — {def.Name}");
    }
}
