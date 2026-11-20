using Godot;

namespace Minecraft;

/// <summary>Read-only journal of the curated achievements (J).</summary>
public partial class AchievementUi : CanvasLayer, IModalUi
{
    private Player _player = null!;
    private VoxelWorld _world = null!;
    private Control _root = null!;
    private VBoxContainer _list = null!;
    private bool _open;

    public void Setup(Player player, VoxelWorld world)
    {
        _player = player;
        _world = world;
        Layer = 12;
        AddToGroup(UiFocus.ModalGroup);
        Build();
    }

    private void Build()
    {
        _root = new Control
        {
            Name = "AchievementRoot",
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        ColorRect dim = new()
        {
            Color = new Color(0, 0, 0, 0.35f),
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
        box.AddChild(MenuSkin.Title("ACHIEVEMENTS", 18));
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 7);
        box.AddChild(_list);
        box.AddChild(MenuSkin.MakeButton("Close", Close, 240));
        center.AddChild(MenuSkin.Wrap(box, 460));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetTree().Paused)
        {
            return;
        }

        if (@event is InputEventKey key && (!key.Pressed || key.Echo))
            return;
        if (!_open && _player.InputLocked)
            return;

        if (@event.IsActionPressed("journal"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (_open && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
        else if (_open && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle()
    {
        if (_open)
        {
            Close();
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
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
        Refresh();
    }

    private void Close()
    {
        _open = false;
        _root.Visible = false;
        UiFocus.Restore(_player);
    }

    public void ForceClose()
    {
        if (_open)
        {
            Close();
        }
    }

    private void Refresh()
    {
        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }

        foreach (Achievement achievement in AchievementDb.All)
        {
            bool done = _world.Flags.ContainsKey("ach/" + achievement.Id);
            Label row = new()
            {
                Text = $"{(done ? "✓" : "•")}  {achievement.Name}",
            };
            row.AddThemeFontSizeOverride("font_size", 15);
            row.AddThemeColorOverride("font_color",
                    done ? new Color(0.72f, 0.95f, 0.72f) : new Color(0.86f, 0.90f, 0.98f));
            _list.AddChild(row);

            Label hint = new()
            {
                Text = "     " + achievement.Hint,
            };
            hint.AddThemeFontSizeOverride("font_size", 12);
            hint.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.78f, done ? 0.75f : 1.0f));
            _list.AddChild(hint);
        }
    }
}
