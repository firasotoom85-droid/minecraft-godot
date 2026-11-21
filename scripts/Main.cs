using Godot;

namespace Minecraft;

/// <summary>
/// Entry point: builds the sky/lighting environment, spawns the player on
/// solid land (or the saved position), wires world &lt;-&gt; player &lt;-&gt; HUD &lt;-&gt; gfx,
/// kicks off chunk streaming.
/// </summary>
public partial class Main : Node3D
{
    private VoxelWorld _world = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private WorldEnvironment _worldEnv = null!;
    private DirectionalLight3D _sun = null!;

    private Gfx _gfx = null!;
    private FurnaceSystem _furnaceSystem = null!;
    private CropSystem _cropSystem = null!;
    private DayNight _daynight = null!;
    private Fx _fx = null!;
    private Sound _sound = null!;
    private Mobs _mobs = null!;
    private Weather _weather = null!;
    private DungeonSystem _dungeonSystem = null!;
    private TraderSystem _traderSystem = null!;
    private TraderUi _traderUi = null!;
    private Ambient _ambient = null!;
    private Campfires _campfires = null!;
    private PauseMenu _pause = null!;
    private KeybindUi _keybindUi = null!;
    private DevConsole _console = null!;
    private InventoryUi _inventoryUi = null!;
    private PhotoMode _photo = null!;
    private AchievementSystem _achievementSystem = null!;
    private AchievementUi _achievementUi = null!;
    private OnboardingSystem _onboarding = null!;
    private FluidSystem _fluids = null!;
    private BoatSystem _boats = null!;

    public override void _Ready()
    {
        _world = GetNode<VoxelWorld>("World");
        _player = GetNode<Player>("Player");
        _hud = GetNode<Hud>("HUD");
        _worldEnv = GetNode<WorldEnvironment>("WorldEnvironment");
        _sun = GetNode<DirectionalLight3D>("Sun");

        SetupEnvironment();
        _player.SetWaterEnvironment(_worldEnv.Environment);
        _world.Player = _player;
        _player.World = _world;
        _player.Position = _world.SavedPlayerPos ?? _world.FindSpawn();
        _player.Inventory.LoadFrom(_world.SavedInventory);
        _player.Select(_world.SavedSelected);
        _player.LoadFromSave(_world.SavedMode ?? _world.DefaultMode, _world.SavedHealth, _world.SavedHunger, _world.SavedRespawn, _world.SavedExperience);

        _gfx = AddSystem(new Gfx());
        _gfx.Setup(_world, _sun, _worldEnv);
        _gfx.PresetChanged += _hud.ShowToast;
        _gfx.ApplyPreset(Settings.GfxPreset, false);
        _world.SetViewDistance(Settings.ViewDistance);
        Sound.ApplyVolumes();

        _furnaceSystem = AddSystem(new FurnaceSystem());
        _furnaceSystem.Setup(_world);

        _cropSystem = AddSystem(new CropSystem());
        _cropSystem.Setup(_world);

        _daynight = AddSystem(new DayNight());
        _daynight.Setup(_world, _player, _sun, _worldEnv);

        _fx = AddSystem(new Fx());
        _fx.Setup(_player);

        _sound = AddSystem(new Sound());
        _sound.Setup(_player);
        _sound.MuteChanged += muted => _hud.ShowToast($"sound {(muted ? "off" : "on")}");
        _player.FlyChanged += flying =>
            _hud.ShowToast($"fly {(flying ? "on" : "off")}  ·  Space up/down");

        _weather = AddSystem(new Weather());
        _weather.Setup(_world, _player, _daynight, _sound);
        _sound.WorldRef = _world;
        _sound.WeatherRef = _weather;

        _mobs = AddSystem(new Mobs());
        _mobs.Setup(_world, _player, _fx, _sound, _weather);

        _dungeonSystem = AddSystem(new DungeonSystem());
        _dungeonSystem.Setup(_world, _player, _hud, _mobs);

        _traderSystem = AddSystem(new TraderSystem());
        _traderSystem.Setup(_world, _player);

        _ambient = AddSystem(new Ambient());
        _ambient.Setup(_player, _world, _weather);

        _campfires = AddSystem(new Campfires());
        _campfires.Setup(_world, _player);

        _pause = AddSystem(new PauseMenu());
        _keybindUi = AddSystem(new KeybindUi());
        _keybindUi.Setup(_player);
        _pause.Setup(_world, _hud, _player, _keybindUi);

        _console = AddSystem(new DevConsole());
        _console.Setup(_player, _world, _hud, _mobs, _traderSystem);

        _inventoryUi = AddSystem(new InventoryUi());
        _inventoryUi.Setup(_player, _hud);
        _player.ChestUsed += pos => _inventoryUi.OpenChest(pos);
        _player.FurnaceUsed += pos => _inventoryUi.OpenFurnace(pos);

        _traderUi = AddSystem(new TraderUi());
        _traderUi.Setup(_player, _hud);
        _player.TraderUsed += _traderUi.Open;

        _photo = AddSystem(new PhotoMode());
        _photo.Setup(_player, _world, _hud, _daynight);

        _achievementSystem = AddSystem(new AchievementSystem());
        _achievementSystem.Setup(_world, _player, _hud);
        _achievementUi = AddSystem(new AchievementUi());
        _achievementUi.Setup(_player, _world);

        _onboarding = AddSystem(new OnboardingSystem());
        _onboarding.Setup(_world, _player, _hud);

        _player.Notice += _hud.ShowToast;
        _player.CampfireUsed += _hud.ShowToast;
        _player.Respawned += () => _mobs.ClearHostilesNear(_player.GlobalPosition, 10.0f);
        _player.Slept += () => _mobs.ClearHostilesNear(_player.GlobalPosition, 32.0f);

        _player.Inventory.Changed += _world.MarkDirty;
        _player.ToolBroke += () =>
        {
            _hud.ShowToast("Your tool broke");
            _sound.Play("place", _player.GlobalPosition, 0.6f, -2.0f);
        };
        _player.InventoryFull += () => _hud.ShowToast("Inventory full");

        _hud.Gfx = _gfx;
        _hud.Mobs = _mobs;
        _hud.Weather = _weather;
        _world.SetupInitial();
        _fluids = AddSystem(new FluidSystem());
        _fluids.Setup(_world, _player);
        _boats = AddSystem(new BoatSystem());
        _boats.Setup(_world, _player, _sound);
        _hud.Bind(_player);
        _hud.ShowToast($"{_gfx.CurrentPreset.Name} quality  ·  F6 change  ·  H help  ·  F3 info");
    }

    /// <summary>
    /// Adds and names a system node, returning it already typed — a generic
    /// method with a Node constraint replaces eight copies of
    /// "new + name + AddChild + assign".
    /// </summary>
    private T AddSystem<T>(T system)
        where T : Node
    {
        system.Name = typeof(T).Name;
        AddChild(system);
        return system;
    }

    public override void _Notification(int what)
    {
        if (_world == null)
        {
            return;
        }

        if (what == (int)NotificationWMCloseRequest)
        {
            _world.SaveNow();
        }
        else if (what == (int)NotificationApplicationFocusOut)
        {
            _world.SaveIfDirty();
        }
    }

    private void SetupEnvironment()
    {
        _worldEnv.Environment = WorldEnvironmentFactory.Build();
    }
}
