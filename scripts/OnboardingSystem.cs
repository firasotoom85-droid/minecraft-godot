using Godot;

namespace Minecraft;

/// <summary>
/// Contextual "First Night" onboarding: a short, non-modal chain that teaches
/// the survival loop through the HUD hint line. Progress is per-world.
/// </summary>
public partial class OnboardingSystem : Node
{
    private static readonly string[] StepIds =
    [
        "wood", "table", "pick", "stone", "torch", "night",
    ];

    private static readonly string[] Hints =
    [
        "Gather wood — punch a tree",
        "Craft a crafting table (E)",
        "Craft a wooden pickaxe",
        "Mine stone for cobblestone",
        "Place a torch to light the dark",
        "Survive the first night",
    ];

    private VoxelWorld? _world;
    private Player? _player;
    private Hud? _hud;
    private int _step;
    private bool _done;
    private bool _sawNight;
    private bool _slept;
    private bool _torchPlaced;
    private double _timer;

    public void Setup(VoxelWorld world, Player player, Hud hud)
    {
        _world = world;
        _player = player;
        _hud = hud;
        player.BlockPlaced += (_, blockId) =>
        {
            if (blockId == (int)BlockId.Torch)
            {
                _torchPlaced = true;
            }
        };
        player.Slept += () => _slept = true;

        _done = world.Flags.ContainsKey("onboard/done");
        while (_step < StepIds.Length && world.Flags.ContainsKey("onboard/" + StepIds[_step]))
        {
            _step += 1;
        }
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null)
        {
            return;
        }

        if (_done || !Settings.ShowHints || _player.Mode == GameMode.Creative || _player.Stats.Dead)
        {
            _hud?.HideHint();
            return;
        }

        if (DayNight.IsNight(_world.TimeOfDay))
        {
            _sawNight = true;
        }

        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = 0.3;
        while (_step < StepIds.Length && Complete(_step))
        {
            _world.Flags["onboard/" + StepIds[_step]] = true;
            _world.MarkDirty();
            _step += 1;
        }

        if (_step >= StepIds.Length)
        {
            _world.Flags["onboard/done"] = true;
            _world.MarkDirty();
            _done = true;
            _hud?.HideHint();
            _hud?.ShowToast("First Night complete — the world is yours");
            return;
        }

        _hud?.SetHint(Hints[_step]);
    }

    private bool Complete(int step) => step switch
    {
        0 => _player!.Inventory.CountOf(ItemId.Log) > 0
                || _player.Inventory.CountOf(ItemId.BirchLog) > 0,
        1 => _player!.Inventory.CountOf(ItemId.CraftingTable) > 0,
        2 => _player!.Inventory.CountOf(ItemId.WoodPick) > 0,
        3 => _player!.Inventory.CountOf(ItemId.Cobblestone) > 0,
        4 => _torchPlaced || (_world?.Flags.ContainsKey("onboard/torch") ?? false),
        5 => _slept || (_sawNight && !DayNight.IsNight(_world!.TimeOfDay)),
        _ => true,
    };
}
