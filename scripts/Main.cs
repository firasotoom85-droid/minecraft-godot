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
    private DayNight _daynight = null!;
    private Fx _fx = null!;
    private Sound _sound = null!;
    private Mobs _mobs = null!;
    private Weather _weather = null!;
    private Ambient _ambient = null!;
    private Campfires _campfires = null!;
    private PauseMenu _pause = null!;
    private DevConsole _console = null!;
    private InventoryUi _inventoryUi = null!;
    private PhotoMode _photo = null!;

    public override void _Ready()
    {
        _world = GetNode<VoxelWorld>("World");
        _player = GetNode<Player>("Player");
        _hud = GetNode<Hud>("HUD");
        _worldEnv = GetNode<WorldEnvironment>("WorldEnvironment");
        _sun = GetNode<DirectionalLight3D>("Sun");

        SetupEnvironment();
        _world.Player = _player;
        _player.World = _world;
        _player.Position = _world.SavedPlayerPos ?? _world.FindSpawn();
        _player.Inventory.LoadFrom(_world.SavedInventory);
        _player.Select(_world.SavedSelected);
        _player.LoadFromSave(_world.SavedMode ?? _world.DefaultMode, _world.SavedHealth, _world.SavedHunger, _world.SavedRespawn);

        _gfx = AddSystem(new Gfx());
        _gfx.Setup(_world, _sun, _worldEnv);
        _gfx.PresetChanged += _hud.ShowToast;
        _gfx.ApplyPreset(Settings.GfxPreset, false);
        _world.SetViewDistance(Settings.ViewDistance);
        Sound.ApplyVolumes();

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

        _ambient = AddSystem(new Ambient());
        _ambient.Setup(_player, _world, _weather);

        _campfires = AddSystem(new Campfires());
        _campfires.Setup(_world, _player);

        _pause = AddSystem(new PauseMenu());
        _pause.Setup(_world, _hud, _player);

        _console = AddSystem(new DevConsole());
        _console.Setup(_player, _world, _hud, _mobs);

        _inventoryUi = AddSystem(new InventoryUi());
        _inventoryUi.Setup(_player, _hud);

        _photo = AddSystem(new PhotoMode());
        _photo.Setup(_player, _world, _hud, _daynight);

        _player.Notice += _hud.ShowToast;
        _player.CampfireUsed += _hud.ShowToast;
        _player.Respawned += () => _mobs.ClearHostilesNear(_player.GlobalPosition, 10.0f);

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
        ProceduralSkyMaterial skyMat = new()
        {
            SkyTopColor = new Color(0.22f, 0.44f, 0.80f),
            SkyHorizonColor = new Color(0.66f, 0.78f, 0.95f),
            GroundBottomColor = new Color(0.25f, 0.30f, 0.25f),
            GroundHorizonColor = new Color(0.66f, 0.78f, 0.95f),
            SunAngleMax = 18.0f,
            SunCurve = 0.12f,
            SkyCurve = 0.2f,
        };
        Sky sky = new()
        {
            SkyMaterial = skyMat,
            RadianceSize = Sky.RadianceSizeEnum.Size128,
        };

        Godot.Environment env = new()
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.66f, 0.78f, 0.95f),
            FogDensity = 0.0035f,
            FogSkyAffect = 0.15f,
            // Distance haze reads as atmosphere rather than a flat wall; the height
            // term keeps a soft mist in valleys while ridges stay clear.
            FogAerialPerspective = 0.5f,
            FogHeight = 30.0f,
            FogHeightDensity = 0.012f,
            // Heavy screen-space effects; Gfx toggles them per preset.
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.4f,
            SsrEnabled = true,
            SsrMaxSteps = 64,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 0.9f,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.0028f,
            VolumetricFogLength = 140.0f,
            VolumetricFogAlbedo = new Color(0.9f, 0.95f, 1.0f),
        };
        _worldEnv.Environment = env;
    }
}
