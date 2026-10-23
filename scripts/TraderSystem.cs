using Godot;

namespace Minecraft;

/// <summary>
/// Spawns a wandering trader near a campfire every few minutes and removes it
/// after a visit. Right-clicking the trader opens the barter panel.
/// </summary>
public partial class TraderSystem : Node
{
    private const double VisitInterval = 180.0;
    private const double VisitLength = 150.0;
    private const float CampfireSearch = 64.0f;

    private VoxelWorld? _world;
    private Player? _player;
    private Trader? _trader;
    private double _timer = 45.0;
    private double _age;

    public void Setup(VoxelWorld world, Player player)
    {
        _world = world;
        _player = player;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null)
        {
            return;
        }

        if (_trader is not null && IsInstanceValid(_trader))
        {
            _age += delta;
            if (_age > VisitLength
                    || _trader.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) > 96.0f * 96.0f)
            {
                _trader.QueueFree();
                _trader = null;
            }

            return;
        }

        _trader = null;
        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = VisitInterval;
        if (!DayNight.IsNight(_world.TimeOfDay))
        {
            return;
        }

        SpawnNow(false);
    }

    /// <summary>Dev/menu hook: spawn a trader near a campfire or the player.</summary>
    public bool SpawnNow(bool allowFallback = true)
    {
        if (_world == null || _player == null)
        {
            return false;
        }

        Vector3 point;
        Vector3 playerPos = _player.GlobalPosition;
        if (_world.NearbyCampfireSites(playerPos, CampfireSearch).Count > 0)
        {
            Vector3I site = _world.NearbyCampfireSites(playerPos, CampfireSearch)[0];
            point = new Vector3(site.X + 0.5f, site.Y + 1.5f, site.Z + 0.5f);
        }
        else if (allowFallback)
        {
            point = _world.FindSpawn() + new Vector3(0, 1.5f, 0);
        }
        else
        {
            return false;
        }

        Trader trader = new();
        AddChild(trader);
        trader.Setup(_world, null!, point);
        trader.GlobalPosition = point;
        trader.GlobalRotation = new Vector3(0, GD.Randf() * Mathf.Tau, 0);
        _trader = trader;
        _age = 0.0;
        return true;
    }
}
