using Godot;

namespace Minecraft;

/// <summary>Keyboard rebinding panel. Opened from the pause menu.</summary>
public partial class KeybindUi : CanvasLayer, IModalUi
{
    private Player _player = null!;
    private Control _root = null!;
    private CenterContainer _center = null!;
    private VBoxContainer _rows = null!;
    private Label _notice = null!;
    private string? _waitingAction;
    private bool _waitingForGamepad;
    private bool _ignoreNextGamepadEvent;
    private bool _open;
    private Control? _previousFocus;

    public bool IsOpen => _open;

    public void Setup(Player player)
    {
        _player = player;
        Layer = 15;
        ProcessMode = ProcessModeEnum.Always;
        AddToGroup(UiFocus.ModalGroup);
        Build();
    }

    private void Build()
    {
        _root = new Control
        {
            Name = "KeybindRoot",
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
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(MenuSkin.Title("CONTROLS", 18));
        Label hint = new()
        {
            Text = "Select a key or controller binding, then press the desired input. Escape cancels.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.70f, 0.78f, 0.88f));
        box.AddChild(hint);
        _notice = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _notice.AddThemeFontSizeOverride("font_size", 12);
        _notice.AddThemeColorOverride("font_color", new Color(1.0f, 0.66f, 0.55f));
        box.AddChild(_notice);
        _rows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _rows.AddThemeConstantOverride("separation", 4);
        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(500, 390),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_rows);
        box.AddChild(scroll);
        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(MenuSkin.MakeButton("Reset defaults", ResetAll, 0));
        actions.AddChild(MenuSkin.MakeButton("Back", Close, 0));
        box.AddChild(actions);
        _center.AddChild(MenuSkin.Wrap(box, 520));
        BuildRows();
        ApplyResponsiveScale();
    }

    private void ApplyResponsiveScale()
    {
        if (_center == null || !IsInstanceValid(_center)) return;
        Vector2 viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        float scale = Mathf.Clamp(Mathf.Min(viewport.X / 1100.0f, viewport.Y / 850.0f), 0.68f, 1.0f);
        _center.PivotOffset = viewport * 0.5f;
        _center.Scale = Vector2.One * scale;
    }

    private void BuildRows()
    {
        foreach ((string action, string label) in Keybinds.Bindable)
        {
            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 12);
            Label name = new()
            {
                Text = label,
                CustomMinimumSize = new Vector2(250, 0),
            };
            row.AddChild(name);
            Button key = MenuSkin.MakeButton(Keybinds.LabelFor(action), () => WaitFor(action), 120);
            key.Name = "Key_" + action;
            row.AddChild(key);
            if (GamepadBindings.LabelFor(action) != "—")
            {
                Button pad = MenuSkin.MakeButton(GamepadBindings.LabelFor(action), () => WaitForGamepad(action), 120);
                pad.Name = "Pad_" + action;
                row.AddChild(pad);
            }
            _rows.AddChild(row);
        }
    }

    private Button? FindBindingButton(string prefix, string action) =>
        _rows.FindChild(prefix + action, true, false) as Button;

    private void WaitFor(string action)
    {
        _waitingAction = action;
        _waitingForGamepad = false;
        _notice.Text = "";
        Button? target = FindBindingButton("Key_", action);
        if (target != null)
        {
            target.Text = "Press a key…";
        }
    }

    private void WaitForGamepad(string action)
    {
        _waitingAction = action;
        _waitingForGamepad = true;
        _ignoreNextGamepadEvent = true;
        CallDeferred(nameof(ClearGamepadActivationGuard));
        _notice.Text = "Press a controller button…";
        Button? target = FindBindingButton("Pad_", action);
        if (target != null) target.Text = "Press button…";
    }

    private void ClearGamepadActivationGuard()
    {
        _ignoreNextGamepadEvent = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_open) return;

        if (_waitingAction is { } action && _waitingForGamepad)
        {
            if (@event is InputEventKey cancel && cancel.Pressed && cancel.Keycode == Key.Escape)
            {
                RefreshPadButton(action);
                _waitingAction = null;
                _waitingForGamepad = false;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (@event is InputEventJoypadButton padEvent && padEvent.Pressed)
            {
                if (_ignoreNextGamepadEvent)
                {
                    _ignoreNextGamepadEvent = false;
                    return;
                }
                JoyButton button = padEvent.ButtonIndex;
                string? conflict = GamepadBindings.Conflict(action, button);
                if (conflict != null)
                {
                    _notice.Text = $"{button} is already used by {conflict}";
                    RefreshPadButton(action);
                }
                else
                {
                    GamepadBindings.SetButton(action, button);
                    _notice.Text = "";
                    RefreshPadButton(action);
                }
                _waitingAction = null;
                _waitingForGamepad = false;
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_waitingAction == null && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (_waitingAction is { } keyboardAction)
        {
            if (key.Keycode == Key.Escape)
            {
                RefreshButton(keyboardAction);
            }
            else if (Keybinds.Conflict(keyboardAction, key.Keycode) is { } conflict)
            {
                _notice.Text = $"{key.Keycode} is already bound to {conflict}";
                RefreshButton(keyboardAction);
            }
            else
            {
                _notice.Text = "";
                Keybinds.Rebind(keyboardAction, key.Keycode);
                RefreshButton(keyboardAction);
            }
            _waitingAction = null;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void RefreshPadButton(string action)
    {
        Button? target = FindBindingButton("Pad_", action);
        if (target != null) target.Text = GamepadBindings.LabelFor(action);
    }

    private void RefreshButton(string action)
    {
        Button? target = FindBindingButton("Key_", action);
        if (target != null)
        {
            target.Text = Keybinds.LabelFor(action);
        }
    }

    private void RefreshAll()
    {
        foreach ((string action, _) in Keybinds.Bindable)
        {
            RefreshButton(action);
        }
    }

    private void ResetAll()
    {
        Keybinds.ResetDefaults();
        GamepadBindings.ResetDefaults();
        RefreshAll();
        foreach ((string action, _) in Keybinds.Bindable) RefreshPadButton(action);
    }

    public void Open()
    {
        _previousFocus = GetViewport().GuiGetFocusOwner() as Control;
        _open = true;
        _root.Visible = true;
        _waitingAction = null;
        _waitingForGamepad = false;
        _ignoreNextGamepadEvent = false;
        RefreshAll();
        foreach ((string action, _) in Keybinds.Bindable) RefreshPadButton(action);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
        UiFocus.FocusFirst(_rows);
    }

    private void Close()
    {
        _open = false;
        _root.Visible = false;
        _waitingAction = null;
        if (_previousFocus != null && IsInstanceValid(_previousFocus))
            _previousFocus.GrabFocus();
        _previousFocus = null;
    }

    public void ForceClose()
    {
        if (_open)
        {
            Close();
        }
    }
}
