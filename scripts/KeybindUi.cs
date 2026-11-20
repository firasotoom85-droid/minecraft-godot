using Godot;

namespace Minecraft;

/// <summary>Keyboard rebinding panel. Opened from the pause menu.</summary>
public partial class KeybindUi : CanvasLayer, IModalUi
{
    private Player _player = null!;
    private Control _root = null!;
    private VBoxContainer _rows = null!;
    private Label _notice = null!;
    private string? _waitingAction;
    private bool _open;

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
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(MenuSkin.Eyebrow("INPUT CONFIGURATION"));
        box.AddChild(MenuSkin.Title("Keyboard controls", 26, true));
        Label hint = new()
        {
            Text = "Choose a binding, then press the key you want to use. Escape cancels.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", MenuSkin.Muted);
        box.AddChild(hint);
        _notice = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _notice.AddThemeFontSizeOverride("font_size", 12);
        _notice.AddThemeColorOverride("font_color", MenuSkin.Warm);
        box.AddChild(_notice);
        _rows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _rows.AddThemeConstantOverride("separation", 4);
        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(0, 400),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_rows);
        box.AddChild(scroll);
        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(MenuSkin.MakeButton("Reset defaults", ResetAll, 0));
        actions.AddChild(MenuSkin.MakeButton("Back", Close, 0));
        box.AddChild(actions);
        PanelContainer panel = MenuSkin.Wrap(box, 560);
        center.AddChild(panel);
        BuildRows();
    }

    private void BuildRows()
    {
        foreach ((string action, string label) in Keybinds.Bindable)
        {
            PanelContainer card = new();
            card.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 12);
            Label name = new()
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(name);
            Button key = MenuSkin.MakeButton(Keybinds.LabelFor(action), () => WaitFor(action), 150);
            key.Name = "Key_" + action;
            row.AddChild(key);
            card.AddChild(row);
            _rows.AddChild(card);
        }
    }

    private void WaitFor(string action)
    {
        _waitingAction = action;
        _notice.Text = "";
        Button? target = _rows.GetNodeOrNull<Button>("Key_" + action);
        if (target != null)
        {
            target.Text = "Press a key…";
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_open)
        {
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (_waitingAction is { } action)
        {
            if (key.Keycode == Key.Escape)
            {
                RefreshButton(action);
            }
            else if (Keybinds.Conflict(action, key.Keycode) is { } conflict)
            {
                _notice.Text = $"{key.Keycode} is already bound to {conflict}";
                RefreshButton(action);
            }
            else
            {
                _notice.Text = "";
                Keybinds.Rebind(action, key.Keycode);
                RefreshButton(action);
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

    private void RefreshButton(string action)
    {
        Button? target = _rows.GetNodeOrNull<Button>("Key_" + action);
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
        RefreshAll();
    }

    public void Open()
    {
        _open = true;
        _root.Visible = true;
        _waitingAction = null;
        RefreshAll();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
    }

    private void Close()
    {
        _open = false;
        _root.Visible = false;
        _waitingAction = null;
    }

    public void ForceClose()
    {
        if (_open)
        {
            Close();
        }
    }
}
