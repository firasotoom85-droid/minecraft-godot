using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Escape overlay: pauses the tree, resumes it, saves on demand, shares the
/// settings form with the main menu and can quit back to the menu.
/// </summary>
public static class PauseInputPolicy
{
    /// <summary>
    /// A nested keybind modal owns Escape/back. The pause menu must not consume
    /// the same event and close the parent pause layer underneath it.
    /// </summary>
    public static bool CanHandlePause(bool pauseOpen, bool keybindOpen, bool playerInputLocked) =>
            !(pauseOpen && keybindOpen) && (pauseOpen || !playerInputLocked);
}

public partial class PauseMenu : CanvasLayer, IModalUi
{
    public const string MenuScene = "res://scenes/main_menu.tscn";

    private VoxelWorld _world = null!;
    private Hud _hud = null!;
    private Player _player = null!;
    private KeybindUi _keybinds = null!;
    private Control _root = null!;
    private CenterContainer _center = null!;
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

        _center = new CenterContainer();
        _center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_center);
        GetViewport().SizeChanged += ApplyResponsiveScale;

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
            if (TrySaveWorld(out string error))
                _status.Text = $"Saved · {Time.GetTimeStringFromSystem()}";
            else
                _status.Text = error;
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

        _center.AddChild(_mainPanel);
        _center.AddChild(_settingsPanel);
        ApplyResponsiveScale();
        _settingsPanel.Visible = false;
    }

    private void ApplyResponsiveScale()
    {
        if (_center == null || !IsInstanceValid(_center)) return;
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        float scale = Mathf.Clamp(Mathf.Min(viewport.X / 1450.0f, viewport.Y / 900.0f), 0.68f, 1.0f);
        _center.PivotOffset = viewport * 0.5f;
        _center.Scale = Vector2.One * scale;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: true }) return;
        // The keybind modal owns Escape/back while it is nested over pause.
        // Consuming the same pause action here would unpause beneath the modal.
        if (!PauseInputPolicy.CanHandlePause(_open, _keybinds.IsOpen, _player.InputLocked))
            return;
        if (@event.IsActionPressed("pause"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (_open && !_keybinds.IsOpen && @event.IsActionPressed("ui_cancel"))
        {
            Resume();
            GetViewport().SetInputAsHandled();
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
        GameplayClock.Pause();
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
        _hud.CloseHelp();
        UiFocus.FocusFirst(_mainPanel);
    }

    private void Resume()
    {
        _open = false;
        _root.Visible = false;
        GetTree().Paused = false;
        GameplayClock.Resume();
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
        UiFocus.FocusFirst(show ? _settingsPanel : _mainPanel);
    }

    private bool TrySaveWorld(out string error)
    {
        try
        {
            if (!_world.SaveNow())
            {
                error = "Save failed. The world remains open; retry or resume without quitting.";
                return false;
            }

            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = "Save failed: " + exception.Message;
            return false;
        }
    }

    private void QuitToMenu()
    {
        if (!TrySaveWorld(out string error))
        {
            _status.Text = error;
            return;
        }

        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(MenuScene);
    }
}
