using Godot;

namespace Minecraft;

/// <summary>Barter panel for the wandering trader. Trades use gold as currency.</summary>
public partial class TraderUi : CanvasLayer, IModalUi
{
    private sealed record Offer(string Label, ItemId Give, int GiveCount, ItemId Get, int GetCount);

    private static readonly Offer[] Offers =
    [
        new("Sell 8 Wheat → 1 Gold Ingot", ItemId.Wheat, 8, ItemId.GoldIngot, 1),
        new("Sell 16 Cobblestone → 1 Gold Ingot", ItemId.Cobblestone, 16, ItemId.GoldIngot, 1),
        new("Buy 4 Wheat Seeds ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Seeds, 4),
        new("Buy 4 Glass ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Glass, 4),
        new("Buy 8 Wool ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Wool, 8),
    ];

    private Player _player = null!;
    private Hud _hud = null!;
    private Control _root = null!;
    private VBoxContainer _offers = null!;
    private bool _open;

    public void Setup(Player player, Hud hud)
    {
        _player = player;
        _hud = hud;
        Layer = 13;
        AddToGroup(UiFocus.ModalGroup);
        Build();
    }

    private void Build()
    {
        _root = new Control
        {
            Name = "TraderRoot",
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
        box.AddThemeConstantOverride("separation", 9);
        box.AddChild(MenuSkin.Title("WANDERING TRADER", 17));
        Label hint = new()
        {
            Text = "Gold is the currency. Choose a trade:",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.70f, 0.78f, 0.88f));
        box.AddChild(hint);
        _offers = new VBoxContainer();
        _offers.AddThemeConstantOverride("separation", 6);
        box.AddChild(_offers);
        box.AddChild(MenuSkin.MakeButton("Leave", Close, 300));
        center.AddChild(MenuSkin.Wrap(box, 430));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetTree().Paused)
        {
            return;
        }

        if (_open && @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Open()
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
        foreach (Node child in _offers.GetChildren())
        {
            child.QueueFree();
        }

        foreach (Offer offer in Offers)
        {
            Button button = MenuSkin.MakeButton(offer.Label, () => TryTrade(offer), 380);
            _offers.AddChild(button);
        }
    }

    private void TryTrade(Offer offer)
    {
        if (_player.Inventory.CountOf(offer.Give) < offer.GiveCount)
        {
            _hud.ShowToast($"Need {offer.GiveCount - _player.Inventory.CountOf(offer.Give)} more {ItemDb.NameOf(offer.Give)}");
            return;
        }

        _player.Inventory.Consume(offer.Give, offer.GiveCount);
        int leftover = _player.Inventory.Add(offer.Get, offer.GetCount);
        if (leftover > 0)
        {
            _player.Inventory.Add(offer.Give, offer.GiveCount);
            _hud.ShowToast("Inventory full");
            return;
        }

        _player.Inventory.RaiseChanged();
        _hud.ShowToast($"+{offer.GetCount} {ItemDb.NameOf(offer.Get)}");
        Refresh();
    }
}
