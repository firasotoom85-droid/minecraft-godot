using Godot;

namespace Minecraft;

/// <summary>
/// Escape overlay: pauses the tree, resumes it, saves on demand, shares the
/// settings form with the main menu and can quit back to the menu.
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
            Color = new Color(0, 0, 0, 0.45f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(center);

        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(MenuSkin.Title("PAUSED", 30));
        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.AddThemeFontSizeOverride("font_size", 13);
        _status.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 0.75f, 0.9f));
        box.AddChild(_status);
        box.AddChild(MenuSkin.MakeButton("Resume", Resume, 0));
        box.AddChild(MenuSkin.MakeButton("Save", () =>
        {
            _world.SaveNow();
            _status.Text = $"Saved · {Time.GetTimeStringFromSystem()}";
        }, 0));
        box.AddChild(MenuSkin.MakeButton("Settings", () => SwitchSettings(true), 0));
        box.AddChild(MenuSkin.MakeButton("Controls", () => _keybinds.Open(), 0));
        box.AddChild(MenuSkin.MakeButton("Quit to menu", QuitToMenu, 0));
        _mainPanel = MenuSkin.Wrap(box, 320);

        VBoxContainer settingsBox = new();
        settingsBox.AddThemeConstantOverride("separation", 10);
        settingsBox.AddChild(MenuSkin.Title("SETTINGS"));
        settingsBox.AddChild(SettingsPanel.Build(() =>
        {
            Sound.ApplyVolumes();
            _world.SetViewDistance(Settings.ViewDistance);
        }));
        settingsBox.AddChild(MenuSkin.MakeButton("Back", () => SwitchSettings(false), 0));
        _settingsPanel = MenuSkin.Wrap(settingsBox);

        center.AddChild(_mainPanel);
        center.AddChild(_settingsPanel);
        _settingsPanel.Visible = false;
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
