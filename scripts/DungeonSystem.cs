using Godot;

namespace Minecraft;

/// <summary>
/// Wakes the Ember Warden when the player reaches its arena and records the
/// one-time clear. The boss itself lives in the normal hostile population.
/// </summary>
public partial class DungeonSystem : Node
{
    private const string ClearFlag = "dungeon/ember";
    private const float WakeRadius = 26.0f;

    private VoxelWorld? _world;
    private Player? _player;
    private Hud? _hud;
    private Mobs? _mobs;
    private EmberWarden? _warden;
    private double _timer;

    public event System.Action? DungeonCleared;

    public void Setup(VoxelWorld world, Player player, Hud hud, Mobs mobs)
    {
        _world = world;
        _player = player;
        _hud = hud;
        _mobs = mobs;
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null || _mobs == null)
        {
            return;
        }

        if (Settings.Difficulty == Settings.DifficultyPeaceful)
        {
            _hud?.HideBoss();
            return;
        }

        if (_warden is not null && IsInstanceValid(_warden) && !_warden.Dead)
        {
            _hud?.ShowBoss("EMBER WARDEN", _warden.Health, _warden.MaxHealth);
            return;
        }

        if (_warden is not null && (!IsInstanceValid(_warden) || _warden.Dead))
        {
            _warden = null;
        }

        if (_warden is null && _mobs.ActiveWarden is { } existing)
        {
            Track(existing);
            return;
        }

        _hud?.HideBoss();
        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = 0.75;
        if (_world.WorldGenVersion < 6 || _world.Flags.ContainsKey(ClearFlag))
        {
            return;
        }

        Vector3 pos = _player.GlobalPosition;
        int cx = Mathf.FloorToInt(pos.X / VoxelWorld.StructureCell);
        int cz = Mathf.FloorToInt(pos.Z / VoxelWorld.StructureCell);
        if (_world.FindNearestStructure(3, cx, cz, 4) is not { } site)
        {
            return;
        }

        int arenaY = Structures.DungeonArenaY(site);
        if (pos.Y > arenaY + 9.0f)
        {
            return;
        }

        float dx = pos.X - (site.X + 0.5f);
        float dz = pos.Z - (site.Z + 0.5f);
        if (dx * dx + dz * dz > WakeRadius * WakeRadius)
        {
            return;
        }

        EmberWarden? warden = _mobs.SpawnWarden(new Vector3(site.X + 0.5f, arenaY + 0.2f, site.Z + 0.5f));
        if (warden is null)
        {
            return;
        }

        _warden = warden;
        Track(warden);
        _hud?.ShowBoss("EMBER WARDEN", warden.Health, warden.MaxHealth);
    }

    private void Track(EmberWarden warden)
    {
        _warden = warden;
        warden.Died += _ => OnWardenDied();
        _hud?.ShowBoss("EMBER WARDEN", warden.Health, warden.MaxHealth);
    }

    private void OnWardenDied()
    {
        if (_world != null)
        {
            _world.Flags[ClearFlag] = true;
            _world.MarkDirty();
        }

        DungeonCleared?.Invoke();
        _hud?.HideBoss();
        _hud?.ShowToast("The Ember Warden falls — the Ember Deep is clear");
        _warden = null;
    }
}
