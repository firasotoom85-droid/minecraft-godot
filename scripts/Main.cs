using System;
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
    private VillageSystem _villageSystem = null!;
    private QuestSystem _questSystem = null!;
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
    private AcceptDialog? _saveFailureDialog;
    private CanvasLayer? _loadingLayer;
    private Control? _loadingRoot;
    private Label? _loadingStatus;
    private ProgressBar? _loadingProgress;
    private double _initialLoadingElapsed;
    private const double InitialLoadingTimeoutSeconds = 45.0;

    public override void _Ready()
    {
        // Close requests are accepted only after the current world snapshot commits.
        GetTree().AutoAcceptQuit = false;
        _world = GetNode<VoxelWorld>("World");
        _player = GetNode<Player>("Player");
        _hud = GetNode<Hud>("HUD");
        _worldEnv = GetNode<WorldEnvironment>("WorldEnvironment");
        _sun = GetNode<DirectionalLight3D>("Sun");

        SetupEnvironment();
        _player.SetWaterEnvironment(_worldEnv.Environment);
        _world.Player = _player;
        _player.World = _world;

        try
        {
            // A saved-dead player resumes from the saved bed/campfire anchor,
            // not from the last death position. For living saves, retain a
            // validated anchor as the alternate if the saved position became
            // obstructed after the last save.
            bool savedDead = _world.SavedPlayerDead;
            Vector3? savedBoatSeat = !savedDead && _world.SavedPlayerPos is Vector3 savedPosition
                    && _world.TryGetSavedOccupiedBoatSeat(savedPosition, out Vector3 seat)
                    ? seat : null;
            Vector3? startupPosition = savedDead
                    ? _world.SavedRespawn
                    : savedBoatSeat ?? _world.SavedPlayerPos;
            Vector3? alternatePosition = savedDead ? null : _world.SavedRespawn;
            if (!_player.TryTeleportToSafePosition(startupPosition,
                        allowWater: savedBoatSeat.HasValue,
                        alternatePosition: alternatePosition,
                        allowVehicleSupport: savedBoatSeat.HasValue))
            {
                ShowSpawnFailure("The saved position, respawn anchor and deterministic world spawn could not be prepared safely.");
                return;
            }
        }
        catch (InvalidOperationException error)
        {
            ShowSpawnFailure(error.Message);
            return;
        }
        _player.Inventory.LoadFrom(_world.SavedInventory);
        _player.Select(_world.SavedSelected, markDirty: false);
        _player.LoadFromSave(_world.SavedMode ?? _world.DefaultMode, _world.SavedHealth,
                _world.SavedHunger, _world.SavedRespawn, _world.SavedExperience, _world.SavedPlayerDead);
        _player.RestoreOrientation(_world.SavedPlayerYaw, _world.SavedPlayerPitch);

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
        _gfx.SetMoonShadowLight(_daynight.Moon);

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

        _questSystem = AddSystem(new QuestSystem());
        _questSystem.Setup(_world, _player, _mobs);
        _questSystem.StageCompleted += (quest, progress) =>
        {
            string message = progress.Completed
                    ? $"{quest.Title} complete — return to the village to claim your reward"
                    : $"{quest.Title}: {quest.Stages[progress.Stage].Description}";
            _hud.ShowToast(message);
        };
        _dungeonSystem.DungeonCleared += _questSystem.ReportDungeonClear;

        _villageSystem = AddSystem(new VillageSystem());
        _villageSystem.Setup(_world, _player, _mobs);

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
        _traderUi.Setup(_player, _hud, _questSystem);
        _player.TraderUsed += _traderUi.Open;
        _player.VillagerInteracted += _traderUi.Open;

        _photo = AddSystem(new PhotoMode());
        _photo.Setup(_player, _world, _hud, _daynight);

        _achievementSystem = AddSystem(new AchievementSystem());
        _achievementSystem.Setup(_world, _player, _hud);
        _achievementUi = AddSystem(new AchievementUi());
        _achievementUi.Setup(_player, _world, _achievementSystem);

        _onboarding = AddSystem(new OnboardingSystem());
        _onboarding.Setup(_world, _player, _hud, _mobs, _inventoryUi);

        _player.Notice += _hud.ShowToast;
        _player.CampfireUsed += _hud.ShowToast;
        _player.Respawned += () => _mobs.ClearHostilesNear(_player.GlobalPosition, 10.0f);
        _player.Slept += () => _mobs.ClearHostilesNear(_player.GlobalPosition, 32.0f);
        if (_player.Stats.Dead)
        {
            // A save captured during the death screen resumes through the same
            // softcore respawn path as a live death, after cleanup is wired.
            _player.Respawn();
            _world.MarkDirty();
        }

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
        BeginInitialLoading();
        _world.SetupInitial();
        _fluids = AddSystem(new FluidSystem());
        _fluids.Setup(_world, _player);
        _boats = AddSystem(new BoatSystem());
        _boats.Setup(_world, _player, _sound);
        _hud.Bind(_player);
        _hud.ShowToast($"{_gfx.CurrentPreset.Name} quality  ·  F6 change  ·  H help  ·  F3 info");
    }

    public override void _Process(double delta)
    {
        if (_loadingLayer == null)
        {
            return;
        }

        _initialLoadingElapsed += Math.Max(0.0, delta);
        InitialChunkLoadProgress progress = _world.GetInitialStreamingProgress();
        if (_loadingStatus != null)
        {
            _loadingStatus.Text = $"Preparing nearby terrain · {progress.ReadyChunks}/{progress.TotalChunks} detailed chunks ready";
        }

        if (_loadingProgress != null)
        {
            _loadingProgress.Value = progress.Fraction * 100.0;
        }

        bool timedOut = !progress.IsComplete
                && _initialLoadingElapsed >= InitialLoadingTimeoutSeconds;
        if (!progress.IsComplete && !timedOut)
        {
            return;
        }

        if (timedOut)
        {
            _hud.ShowToast("Detailed chunks are still streaming; coarse terrain remains visible.");
        }

        FinishInitialLoading();
    }

    private void BeginInitialLoading()
    {
        _player.InputLocked = true;
        _initialLoadingElapsed = 0.0;

        CanvasLayer layer = new()
        {
            Name = "WorldLoadingOverlay",
            Layer = 100,
        };
        Control root = new()
        {
            Name = "Root",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        ColorRect background = new()
        {
            Color = new Color(0.025f, 0.035f, 0.05f, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(background);

        CenterContainer center = new()
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(440.0f, 176.0f),
        };
        center.AddChild(panel);

        MarginContainer margins = new();
        margins.AddThemeConstantOverride("margin_left", 28);
        margins.AddThemeConstantOverride("margin_top", 22);
        margins.AddThemeConstantOverride("margin_right", 28);
        margins.AddThemeConstantOverride("margin_bottom", 22);
        panel.AddChild(margins);

        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 12);
        margins.AddChild(content);

        Label heading = new()
        {
            Text = "GENERATING WORLD",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeFontSizeOverride("font_size", 22);
        content.AddChild(heading);

        Label status = new()
        {
            Text = "Preparing nearby terrain · 0/9 chunks ready",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        content.AddChild(status);

        ProgressBar progress = new()
        {
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 0.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0.0f, 14.0f),
        };
        content.AddChild(progress);

        Label hint = new()
        {
            Text = "A coarse terrain layer stays visible while detailed chunks stream in.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        content.AddChild(hint);

        AddChild(layer);
        _loadingLayer = layer;
        _loadingRoot = root;
        _loadingStatus = status;
        _loadingProgress = progress;
    }

    private void FinishInitialLoading()
    {
        _world.CompleteInitialStreaming();
        _player.InputLocked = false;

        CanvasLayer? layer = _loadingLayer;
        Control? root = _loadingRoot;
        _loadingLayer = null;
        _loadingRoot = null;
        _loadingStatus = null;
        _loadingProgress = null;

        if (layer == null || root == null)
        {
            return;
        }

        Tween tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 0.0f, 0.25f);
        tween.TweenCallback(Callable.From(() => layer.QueueFree()));
    }

    private void ShowSpawnFailure(string reason)
    {
        // No gameplay workers or player movement should start while the user
        // chooses a different seed/preset from the world menu.
        _world.SetProcess(false);
        _world.SetPhysicsProcess(false);
        _player.InputLocked = true;
        _player.SetProcessUnhandledInput(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.SetProcess(false);
        _player.SetPhysicsProcess(false);
        if (_world.GetNodeOrNull<FarLod>("FarLod") is { } farLod)
        {
            farLod.SetProcess(false);
            farLod.SetPhysicsProcess(false);
        }

        GD.PushWarning($"minecraft: world startup stopped because no safe spawn was found ({reason})");
        AcceptDialog dialog = new()
        {
            Title = "No safe starting location",
            DialogText = "This world has no safe starting location within the search area. " +
                    "The world has not been modified. Return to the world list and choose " +
                    "another seed or terrain preset.\n\n" + reason,
        };
        AddChild(dialog);

        void ReturnToWorldList()
        {
            GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
        }

        dialog.Confirmed += ReturnToWorldList;
        dialog.CloseRequested += ReturnToWorldList;
        dialog.CallDeferred("popup_centered");
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
            if (_world.SaveNow())
            {
                GetTree().Quit();
            }
            else
            {
                ShowSaveFailureDialog();
            }
        }
        else if (what == (int)NotificationApplicationFocusOut)
        {
            _world.SaveIfDirty();
        }
    }

    private void ShowSaveFailureDialog()
    {
        if (_saveFailureDialog == null || !GodotObject.IsInstanceValid(_saveFailureDialog))
        {
            _saveFailureDialog = new AcceptDialog
            {
                Title = "Save failed",
                DialogText = "The latest world changes could not be committed safely. " +
                        "The game will remain open and the world is protected from further writes. " +
                        "Use World Management to restore a matching backup set before trying again.",
            };
            AddChild(_saveFailureDialog);
        }

        if (!_saveFailureDialog.Visible)
            _saveFailureDialog.CallDeferred("popup_centered");
    }

    private void SetupEnvironment()
    {
        _worldEnv.Environment = WorldEnvironmentFactory.Build(_world.WorldGenVersion, Settings.GfxPreset);
    }
}
