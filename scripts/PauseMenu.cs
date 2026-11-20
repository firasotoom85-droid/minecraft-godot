using Godot;

namespace Minecraft;

/// <summary>
/// Paused-game dashboard with quick actions and the shared settings interface.
/// </summary>
public partial class PauseMenu : CanvasLayer, IModalUi
{
    public const string MenuScene = "res://scenes/main_menu.tscn";

    private VoxelWorld _world = null!;
    private Hud _hud = null!;
    private Player _player = null!;
    private KeybindUi _keybinds = null!;
    private Control _root = null!;
    private PanelContainer _mainPanel = null!;
    private PanelContainer _settingsPanel = null!;
    private Label _status = null!;
    private bool _open;

    public void Setup(VoxelWorld world, Hud hud, Player player, KeybindUi keybinds)
    {
        _world = world;
        _hud = hud;
        _player = player;
        _keybinds = keybinds;
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;
        AddToGroup(UiFocus.ModalGroup);

        _root = new Control
        {
            Name = "PauseRoot",
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        ColorRect dim = new()
        {
            Color = new Color(0.008f, 0.020f, 0.025f, 0.76f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(center);

        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 13);
        box.AddChild(MenuSkin.Eyebrow("SESSION CONTROL"));
        box.AddChild(MenuSkin.Title("The world can wait.", 32, true));
        box.AddChild(MenuSkin.MutedLabel("Your game is paused. Take a moment, adjust your setup, or jump back in.", 13));

        PanelContainer session = new();
        session.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
        HBoxContainer sessionRow = new();
        sessionRow.AddThemeConstantOverride("separation", 14);
        VBoxContainer sessionInfo = new();
        sessionInfo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sessionInfo.AddThemeConstantOverride("separation", 3);
        sessionInfo.AddChild(MenuSkin.Eyebrow("CURRENT SESSION"));
        sessionInfo.AddChild(MenuSkin.Title("Voxel world", 16, true));
        sessionInfo.AddChild(MenuSkin.MutedLabel(
                $"{(_player.Mode == GameMode.Survival ? "SURVIVAL" : "CREATIVE")}  ·  {Settings.DifficultyNames[Settings.Difficulty].ToUpperInvariant()}",
                10));
        sessionRow.AddChild(sessionInfo);
        Label saveHint = new()
        {
            Text = "Autosave enabled",
            VerticalAlignment = VerticalAlignment.Center,
        };
        saveHint.AddThemeFontSizeOverride("font_size", 11);
        saveHint.AddThemeColorOverride("font_color", MenuSkin.AccentBright);
        sessionRow.AddChild(saveHint);
        session.AddChild(sessionRow);
        box.AddChild(session);

        _status = MenuSkin.MutedLabel("", 12);
        box.AddChild(_status);
        box.AddChild(MenuSkin.MakeButton("RESUME GAME", Resume, 0, primary: true));
        box.AddChild(MenuSkin.MakeButton("SAVE NOW", () =>
        {
            _world.SaveNow();
            _status.Text = $"Save requested  ·  {Time.GetTimeStringFromSystem()}";
        }, 0));
        box.AddChild(MenuSkin.MakeButton("SETTINGS", () => SwitchSettings(true), 0));
        box.AddChild(MenuSkin.MakeButton("KEYBOARD CONTROLS", () => _keybinds.Open(), 0));
        box.AddChild(MenuSkin.MakeButton("RETURN TO TITLE", QuitToMenu, 0, danger: true));
        _mainPanel = MenuSkin.Wrap(box, 430);

        VBoxContainer settingsBox = new();
        settingsBox.AddThemeConstantOverride("separation", 12);
        settingsBox.AddChild(MenuSkin.Eyebrow("PREFERENCES"));
        settingsBox.AddChild(MenuSkin.Title("Settings", 27, true));
        ScrollContainer settingsScroll = new()
        {
            CustomMinimumSize = new Vector2(0, 480),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        settingsScroll.AddChild(SettingsPanel.Build(() =>
        {
            Sound.ApplyVolumes();
            _world.SetViewDistance(Settings.ViewDistance);
        }));
        settingsBox.AddChild(settingsScroll);
        settingsBox.AddChild(MenuSkin.MakeButton("BACK TO PAUSE MENU", () => SwitchSettings(false), 210));
        _settingsPanel = MenuSkin.Wrap(settingsBox);
        _settingsPanel.Visible = false;

        center.AddChild(_mainPanel);
        center.AddChild(_settingsPanel);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.IsActionPressed("pause"))
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        if (_open)
        {
            Resume();
        }
        else
        {
            Open();
        }
    }

    private void Open()
    {
        _open = true;
        _root.Visible = true;
        _mainPanel.Visible = true;
        _settingsPanel.Visible = false;
        _status.Text = "";
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
        _hud.CloseHelp();
    }

    private void Resume()
    {
        _open = false;
        _root.Visible = false;
        GetTree().Paused = false;
        UiFocus.Restore(_player);
    }

    public void ForceClose()
    {
        if (_open)
        {
            Resume();
        }
    }

    private void SwitchSettings(bool show)
    {
        _mainPanel.Visible = !show;
        _settingsPanel.Visible = show;
    }

    private void QuitToMenu()
    {
        _world.SaveNow();
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(MenuScene);
    }
}
