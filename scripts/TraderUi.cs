using Godot;

namespace Minecraft;

/// <summary>Atomic inventory-side barter rules shared by the trader UI and progression tests.</summary>
public static class TraderTradeRules
{
    public static bool CanExecute(Inventory inventory, ItemId give, int giveCount,
            ItemId get, int getCount) =>
            give != ItemId.None && get != ItemId.None && giveCount > 0 && getCount > 0
            && inventory.CountOf(give) >= giveCount && inventory.CanFit(get, getCount);

    public static bool TryExecute(Inventory inventory, ItemId give, int giveCount,
            ItemId get, int getCount)
    {
        if (!CanExecute(inventory, give, giveCount, get, getCount)
                || !inventory.Consume(give, giveCount))
        {
            return false;
        }

        // CanFit is checked before consuming, so this add must be all-or-nothing.
        // Keep a defensive rollback in case inventory rules change in the future.
        int leftover = inventory.Add(get, getCount);
        if (leftover > 0)
        {
            inventory.Add(give, giveCount);
            return false;
        }

        return true;
    }
}

/// <summary>Barter panel for the wandering trader and village residents.</summary>
public partial class TraderUi : CanvasLayer, IModalUi
{
    private sealed record Offer(string Label, ItemId Give, int GiveCount, ItemId Get, int GetCount);

    private static readonly Offer[] WanderingOffers =
    [
        new("Sell 8 Wheat → 1 Gold Ingot", ItemId.Wheat, 8, ItemId.GoldIngot, 1),
        new("Sell 16 Cobblestone → 1 Gold Ingot", ItemId.Cobblestone, 16, ItemId.GoldIngot, 1),
        new("Buy 4 Wheat Seeds ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Seeds, 4),
        new("Buy 4 Glass ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Glass, 4),
        new("Buy 8 Wool ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Wool, 8),
        new("Buy 1 Lapis Lazuli ← 2 Gold Ingots", ItemId.GoldIngot, 2, ItemId.LapisLazuli, 1),
    ];

    private Player _player = null!;
    private Hud _hud = null!;
    private QuestSystem _quests = null!;
    private Control _root = null!;
    private VBoxContainer _offers = null!;
    private VBoxContainer _questRows = null!;
    private Label _title = null!;
    private Label _dialogue = null!;
    private Label _hint = null!;
    private Villager? _villager;
    private bool _open;

    public void Setup(Player player, Hud hud, QuestSystem quests)
    {
        _player = player;
        _hud = hud;
        _quests = quests;
        Layer = 13;
        AddToGroup(UiFocus.ModalGroup);
        Build();
        _quests.ProgressChanged += () => { if (_open) Refresh(); };
    }

    private void Build()
    {
        _root = new Control { Name = "TraderRoot", Visible = false };
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
        box.AddThemeConstantOverride("separation", 8);
        _title = MenuSkin.Title("WANDERING TRADER", 17);
        box.AddChild(_title);
        _dialogue = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _dialogue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialogue.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_dialogue);
        _hint = new Label { Text = "Gold ingots are the village currency.", HorizontalAlignment = HorizontalAlignment.Center };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        _hint.AddThemeColorOverride("font_color", new Color(0.70f, 0.78f, 0.88f));
        box.AddChild(_hint);
        _offers = new VBoxContainer();
        _offers.AddThemeConstantOverride("separation", 5);
        box.AddChild(_offers);
        box.AddChild(MenuSkin.Title("VILLAGE REQUESTS", 13));
        _questRows = new VBoxContainer();
        _questRows.AddThemeConstantOverride("separation", 5);
        box.AddChild(_questRows);
        box.AddChild(MenuSkin.MakeButton("Leave", Close, 300));
        center.AddChild(MenuSkin.Wrap(box, 450));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetTree().Paused) return;
        if (_open && @event is InputEventKey key && (!key.Pressed || key.Echo)) return;
        if (_open && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Open() => OpenFor(null);
    public void Open(Villager villager) => OpenFor(villager);

    private void OpenFor(Villager? villager)
    {
        if (villager != null && (!GodotObject.IsInstanceValid(villager)
                || villager.Dead || villager.IsQueuedForDeletion()))
        {
            _hud.ShowToast("This villager is no longer available");
            if (_open) Close();
            return;
        }

        DetachVillager();
        _villager = villager;
        if (_villager != null)
        {
            _villager.Died += OnVillagerDied;
            _villager.TreeExiting += OnVillagerTreeExiting;
        }
        _open = true;
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
        Refresh();
        UiFocus.FocusFirst(_root);
    }

    private void OnVillagerDied(Mob mob)
    {
        if (ReferenceEquals(_villager, mob)) Close();
    }

    private void OnVillagerTreeExiting()
    {
        Close();
    }

    private void DetachVillager()
    {
        if (_villager == null || !GodotObject.IsInstanceValid(_villager)) return;
        _villager.Died -= OnVillagerDied;
        _villager.TreeExiting -= OnVillagerTreeExiting;
    }

    private bool EnsureVillagerUsable()
    {
        if (!_open) return false;
        if (_villager == null) return true;
        if (GodotObject.IsInstanceValid(_villager)
                && !_villager.Dead && !_villager.IsQueuedForDeletion())
            return true;
        Close();
        return false;
    }

    private void Close()
    {
        DetachVillager();
        _open = false;
        _root.Visible = false;
        _villager = null;
        UiFocus.Restore(_player);
    }

    public void ForceClose()
    {
        if (_open) Close();
    }

    private void Refresh()
    {
        if (!EnsureVillagerUsable()) return;
        foreach (Node child in _offers.GetChildren()) child.QueueFree();
        foreach (Node child in _questRows.GetChildren()) child.QueueFree();
        if (_villager == null)
        {
            _title.Text = "WANDERING TRADER";
            _dialogue.Text = "A travelling merchant offers supplies.";
            _hint.Text = "Gold ingots are the currency. Choose a trade:";
            _questRows.Visible = false;
            foreach (Offer offer in WanderingOffers)
                _offers.AddChild(MenuSkin.MakeButton(offer.Label, () => TryTrade(offer), 390));
            return;
        }

        _title.Text = $"{_villager.DisplayName.ToUpperInvariant()} — {_villager.Profession.ToString().ToUpperInvariant()}";
        _dialogue.Text = _villager.DialogueFor(_player.World?.TimeOfDay ?? 0.5);
        _hint.Text = $"Gold ingots are the currency · Trades completed: {_villager.TradesCompleted}";
        _questRows.Visible = true;
        foreach (VillageTrade offer in VillageTradeCatalog.For(_villager.Profession))
        {
            Offer row = new(offer.Label, offer.Give, offer.GiveCount, offer.Get, offer.GetCount);
            _offers.AddChild(MenuSkin.MakeButton(row.Label, () => TryTrade(row), 390));
        }

        foreach (QuestDefinition quest in _quests.AvailableFor(_villager.Profession))
        {
            QuestProgressSave? progress = _quests.ProgressOf(quest.Id);
            VBoxContainer questRow = new();
            Label description = new()
            {
                Text = $"{quest.Title}\n{_quests.StatusOf(quest)}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(390, 0),
            };
            description.AddThemeFontSizeOverride("font_size", 12);
            questRow.AddChild(description);
            if (progress == null)
                questRow.AddChild(MenuSkin.MakeButton("Accept Quest", () => AcceptQuest(quest.Id), 390));
            else if (progress.Completed && !progress.Claimed)
                questRow.AddChild(MenuSkin.MakeButton(
                        $"Claim {quest.RewardCount} {ItemDb.NameOf(quest.Reward)} + {quest.Experience} XP",
                        () => ClaimQuest(quest.Id), 390));
            _questRows.AddChild(questRow);
        }
    }

    private void AcceptQuest(string id)
    {
        if (!EnsureVillagerUsable()) return;
        if (!_quests.Accept(id)) _hud.ShowToast("Quest could not be accepted");
        Refresh();
    }

    private void ClaimQuest(string id)
    {
        if (!EnsureVillagerUsable()) return;
        if (!_quests.Claim(id)) _hud.ShowToast("Make room in your inventory before claiming the reward");
        Refresh();
    }

    private void TryTrade(Offer offer)
    {
        if (!EnsureVillagerUsable()) return;
        int available = _player.Inventory.CountOf(offer.Give);
        if (available < offer.GiveCount)
        {
            _hud.ShowToast($"Need {offer.GiveCount - available} more {ItemDb.NameOf(offer.Give)}");
            return;
        }

        if (!_player.Inventory.CanFit(offer.Get, offer.GetCount))
        {
            _hud.ShowToast("Make room for the trade output");
            return;
        }

        if (!TraderTradeRules.TryExecute(_player.Inventory, offer.Give, offer.GiveCount,
                offer.Get, offer.GetCount))
        {
            _hud.ShowToast("Trade could not be completed");
            return;
        }

        // The route quest specifically asks for trades with village residents;
        // wandering-trader barter remains separate from village reputation.
        if (_villager != null)
        {
            _player.RecordTrade();
            _villager.RecordTrade();
        }
        _player.Inventory.RaiseChanged();
        _hud.ShowToast($"+{offer.GetCount} {ItemDb.NameOf(offer.Get)}");
        Refresh();
    }
}
