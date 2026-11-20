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
        box.AddThemeConstantOverride("separation", 12);
        box.AddChild(MenuSkin.Eyebrow("TRADING POST"));
        box.AddChild(MenuSkin.Title("Wandering trader", 26, true));
        Label hint = new()
        {
            Text = "Gold is the currency. Choose a trade:",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", MenuSkin.Muted);
        box.AddChild(hint);
        _offers = new VBoxContainer();
        _offers.AddThemeConstantOverride("separation", 8);
        box.AddChild(_offers);
        box.AddChild(MenuSkin.MakeButton("LEAVE", Close, 160));
        center.AddChild(MenuSkin.Wrap(box, 500));
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
            PanelContainer card = new();
            card.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 12);
            VBoxContainer details = new();
            details.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            details.AddThemeConstantOverride("separation", 3);
            details.AddChild(MenuSkin.Title(offer.Label, 13, true));
            int have = _player.Inventory.CountOf(offer.Give);
            details.AddChild(MenuSkin.MutedLabel(
                    $"REQUIRES  {have}/{offer.GiveCount}  {ItemDb.NameOf(offer.Give)}",
                    10));
            row.AddChild(details);
            Button button = MenuSkin.MakeButton("TRADE", () => TryTrade(offer), 94, primary: true);
            button.Disabled = have < offer.GiveCount;
            row.AddChild(button);
            card.AddChild(row);
            _offers.AddChild(card);
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
