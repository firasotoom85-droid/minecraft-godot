using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Minecraft;

/// <summary>
/// Player UI: outlined crosshair, hotbar with slot numbers / selection pop /
/// held-block name, readable toast messages, underwater tint, fly badge,
/// an F3 info panel and an H help overlay.
/// </summary>
public partial class Hud : CanvasLayer
{
    private static readonly string[] Facings = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    private static readonly string[] BiomeNames = ["Plains", "Desert", "Snow", "Birch Forest"];
    private const int SlotSize = 56;

    // A raw string literal keeps the control sheet byte-for-byte readable
    // and indentation-managed, with no escapes and no continuation markers.
    private const string HelpText =
        """
        CONTROLS

        WASD          move            Space   jump  (tap twice: fly · creative)
        Shift         sprint          F       toggle fly (creative)
        Mouse         look            LMB     mine / attack (hold to keep)
        RMB           place / use · eat · cook · rest · tame
        MMB           pick block      1-9 / wheel   select slot
        E             inventory / crafting      ESC   pause menu
        M             minimap         P       photo mode (arrows scrub time)
        H             this help       F3      info panel
        F6            quality preset  F7/F8   view distance -+
        F9            mute sounds     /       console
        """;

    public Player? PlayerRef;
    public Gfx? Gfx;
    public Mobs? Mobs;
    public Weather? Weather;

    private readonly List<Panel> _slots = [];
    private readonly TextureRect[] _slotIcons = new TextureRect[Inventory.HotbarSize];
    private readonly Label[] _slotCounts = new Label[Inventory.HotbarSize];
    private Label _heldLabel = null!;
    private double _heldTime;
    private ColorRect _waterOverlay = null!;
    private ColorRect _vignette = null!;
    private PanelContainer _deathPanel = null!;
    private HBoxContainer _statsRow = null!;
    private HBoxContainer _breathRow = null!;
    private HBoxContainer _armorRow = null!;
    private readonly TextureRect[] _hearts = new TextureRect[10];
    private readonly TextureRect[] _hungers = new TextureRect[10];
    private readonly TextureRect[] _bubbles = new TextureRect[10];
    private readonly TextureRect[] _armorIcons = new TextureRect[4];
    private Texture2D[] _heartTex = [];
    private Texture2D[] _hungerTex = [];
    private Texture2D[] _armorTex = [];
    private float _vignetteStrength;
    private bool _statsBound;
    private PanelContainer _toast = null!;
    private Label _toastLabel = null!;
    private PanelContainer _hintPanel = null!;
    private Label _hintLabel = null!;
    private double _toastTime;
    private PanelContainer _infoPanel = null!;
    private Label _infoLabel = null!;
    private PanelContainer _helpPanel = null!;
    private PanelContainer _flyBadge = null!;
    private PanelContainer _mapPanel = null!;
    private PanelContainer _bossPanel = null!;
    private Label _bossName = null!;
    private ProgressBar _bossBar = null!;
    private Minimap _minimap = null!;
    private bool? _wasNight;
    private double _refresh;
    private bool _bound;

    public override void _Ready()
    {
        Control root = new()
        {
            Name = "Root",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _waterOverlay = new ColorRect
        {
            Name = "WaterOverlay",
            Color = new Color(0.10f, 0.30f, 0.65f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _waterOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_waterOverlay);

        _vignette = new ColorRect
        {
            Name = "Vignette",
            Color = new Color(0.62f, 0.04f, 0.04f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_vignette);

        BuildCrosshair(root);
        BuildBottom(root);
        BuildOverlays(root);
        BuildMinimap(root);
        BuildBossBar(root);
    }

    private void BuildBossBar(Control root)
    {
        _bossPanel = new PanelContainer
        {
            Name = "BossBar",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bossPanel.AddThemeStyleboxOverride("panel", MenuSkin.PanelStyle());
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 4);
        _bossName = new Label
        {
            Text = "BOSS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _bossName.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(_bossName);
        _bossBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(340, 12),
        };
        box.AddChild(_bossBar);
        _bossPanel.AddChild(box);
        _bossPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.Minsize, 16);
        root.AddChild(_bossPanel);
    }

    public void ShowBoss(string name, float health, float maxHealth)
    {
        if (_bossPanel == null)
        {
            return;
        }

        _bossPanel.Visible = true;
        _bossName.Text = name;
        _bossBar.MaxValue = maxHealth;
        _bossBar.Value = Mathf.Max(health, 0.0f);
    }

    public void HideBoss()
    {
        if (_bossPanel != null)
        {
            _bossPanel.Visible = false;
        }
    }

    // -----------------------------------------------------------------------
    // Crosshair (black-outlined plus, readable on any background)
    // -----------------------------------------------------------------------

    private static void BuildCrosshair(Control root)
    {
        CenterContainer cross = new()
        {
            Name = "Crosshair",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        cross.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(cross);
        Control wrap = new()
        {
            CustomMinimumSize = new Vector2(22, 22),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        cross.AddChild(wrap);
        Bar(wrap, new Vector2(0, 9), new Vector2(22, 4), new Color(0, 0, 0, 0.62f));
        Bar(wrap, new Vector2(9, 0), new Vector2(4, 22), new Color(0, 0, 0, 0.62f));
        Bar(wrap, new Vector2(1, 10), new Vector2(20, 2), new Color(0.96f, 0.96f, 0.83f, 0.98f));
        Bar(wrap, new Vector2(10, 1), new Vector2(2, 20), new Color(0.96f, 0.96f, 0.83f, 0.98f));
    }

    private static void Bar(Control parent, Vector2 pos, Vector2 size, Color color)
    {
        parent.AddChild(new ColorRect
        {
            Position = pos,
            Size = size,
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }

    // -----------------------------------------------------------------------
    // Hotbar + held-block name
    // -----------------------------------------------------------------------

    private void BuildBottom(Control root)
    {
        VBoxContainer stack = new()
        {
            Name = "BottomStack",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        stack.AddThemeConstantOverride("separation", 3);
        root.AddChild(stack);
        BuildSurvivalBars(stack);

        HBoxContainer hotbar = new()
        {
            Name = "Hotbar",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hotbar.AddThemeConstantOverride("separation", 5);
        stack.AddChild(hotbar);
        foreach (int i in Enumerable.Range(0, Inventory.HotbarSize))
        {
            Panel slot = new()
            {
                CustomMinimumSize = new Vector2(SlotSize, SlotSize),
                PivotOffset = new Vector2(SlotSize, SlotSize) * 0.5f,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            hotbar.AddChild(slot);
            _slots.Add(slot);
            TextureRect icon = new()
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 7);
            slot.AddChild(icon);
            _slotIcons[i] = icon;
            Label count = new()
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            count.AddThemeFontSizeOverride("font_size", 13);
            count.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.95f));
            count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            count.AddThemeConstantOverride("outline_size", 4);
            count.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 3);
            slot.AddChild(count);
            _slotCounts[i] = count;
            Label num = new()
            {
                Text = (i + 1).ToString(),
                Position = new Vector2(5, 2),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            num.AddThemeFontSizeOverride("font_size", 12);
            num.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
            num.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            num.AddThemeConstantOverride("outline_size", 3);
            slot.AddChild(num);
        }

        // Centre only after the slots exist, so the minimum size is known and the
        // row is truly centred (presetting an empty container offsets it).
        stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 14);

        _heldLabel = new Label
        {
            Name = "HeldBlock",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = Colors.White.WithAlpha(0.0f),
        };
        _heldLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 104);
        _heldLabel.AddThemeFontSizeOverride("font_size", 16);
        _heldLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.95f));
        _heldLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _heldLabel.AddThemeConstantOverride("outline_size", 5);
        root.AddChild(_heldLabel);
    }

    public void Bind(Player p)
    {
        PlayerRef = p;
        _minimap.World = p.World;
        _minimap.PlayerRef = p;
        if (!_bound)
        {
            p.HotbarChanged += OnHotbarChanged;
            p.Inventory.Changed += RefreshHotbar;
            p.Inventory.Changed += RefreshSurvival;
            _bound = true;
        }

        if (!_statsBound)
        {
            _statsBound = true;
            p.Stats.Damaged += amount => _vignetteStrength = Mathf.Min(_vignetteStrength + 0.25f + amount * 0.03f, 0.75f);
            p.Stats.Died += ShowDeath;
            p.Stats.Respawned += HideDeath;
        }

        RefreshHotbar();
        RefreshSurvival();
        OnHotbarChanged(p.Selected, (int)p.Inventory.Hotbar[p.Selected].Id);
    }

    private void OnHotbarChanged(int index, int itemId)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].AddThemeStyleboxOverride("panel", SlotStyle(i == index));
        }

        if (index >= 0 && index < _slots.Count)
        {
            Panel slot = _slots[index];
            slot.Scale = new Vector2(1.14f, 1.14f);
            CreateTween()
                    .TweenProperty(slot, "scale", Vector2.One, 0.16)
                    .SetTrans(Tween.TransitionType.Back)
                    .SetEase(Tween.EaseType.Out);
        }

        _heldLabel.Text = itemId == 0 ? "" : ItemDb.NameOf((ItemId)itemId);
        _heldLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 104);
        _heldTime = 1.6;
        RefreshHotbar();
    }

    /// <summary>Draw the hotbar from the real inventory: icons, counts, tool durability.</summary>
    private void RefreshHotbar()
    {
        if (PlayerRef == null)
        {
            return;
        }

        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            ItemStack stack = PlayerRef.Inventory.Hotbar[i];
            _slotIcons[i].Texture = stack.IsEmpty ? null : ItemDb.Icon(stack.Id);
            _slotIcons[i].Visible = !stack.IsEmpty;
            if (stack.IsEmpty)
            {
                _slotCounts[i].Text = "";
            }
            else if (ItemDb.ToolOf(stack.Id) is { } tool)
            {
                _slotCounts[i].Text = stack.Durability < tool.Durability ? stack.Durability.ToString() : "";
            }
            else
            {
                _slotCounts[i].Text = stack.Count > 1 ? stack.Count.ToString() : "";
            }
        }
    }

    private static StyleBoxFlat SlotStyle(bool selected)
    {
        StyleBoxFlat sb = new()
        {
            BgColor = selected ? new Color(0.10f, 0.10f, 0.13f, 0.80f) : new Color(0.06f, 0.06f, 0.08f, 0.62f),
            BorderColor = selected ? new Color(1, 1, 1, 0.95f) : new Color(0.35f, 0.35f, 0.38f, 0.85f),
        };
        sb.SetBorderWidthAll(selected ? 3 : 2);
        sb.SetCornerRadiusAll(6);
        return sb;
    }

    // -----------------------------------------------------------------------
    // Survival bars (hearts / hunger / breath) + damage vignette + death screen
    // -----------------------------------------------------------------------

    private enum IconKind
    {
        Heart,
        Hunger,
        Bubble,
        Armor,
    }

    private void BuildSurvivalBars(Control parent)
    {
        _statsRow = new HBoxContainer
        {
            Name = "StatsRow",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(544, 0),
        };
        parent.AddChild(_statsRow);

        _heartTex = [BarIcon(IconKind.Heart, 0), BarIcon(IconKind.Heart, 1), BarIcon(IconKind.Heart, 2)];
        _hungerTex = [BarIcon(IconKind.Hunger, 0), BarIcon(IconKind.Hunger, 1), BarIcon(IconKind.Hunger, 2)];

        HBoxContainer hearts = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _statsRow.AddChild(hearts);
        for (int i = 0; i < 10; i++)
        {
            _hearts[i] = IconRect(hearts, _heartTex[2], 20);
        }

        _armorTex = [BarIcon(IconKind.Armor, 0), BarIcon(IconKind.Armor, 2)];
        _armorRow = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _armorRow.AddThemeConstantOverride("separation", 1);
        _statsRow.AddChild(_armorRow);
        for (int i = 0; i < 4; i++)
        {
            _armorIcons[i] = IconRect(_armorRow, _armorTex[1], 15);
        }

        _statsRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _breathRow = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _statsRow.AddChild(_breathRow);
        Texture2D bubble = BarIcon(IconKind.Bubble, 0);
        for (int i = 0; i < 10; i++)
        {
            _bubbles[i] = IconRect(_breathRow, bubble, 16);
        }

        HBoxContainer hungers = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _statsRow.AddChild(hungers);
        for (int i = 0; i < 10; i++)
        {
            _hungers[i] = IconRect(hungers, _hungerTex[2], 20);
        }
    }

    private static TextureRect IconRect(Control parent, Texture2D tex, int size)
    {
        TextureRect rect = new()
        {
            Texture = tex,
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(rect);
        return rect;
    }

    /// <summary>Paints a 9x9 bar icon: state 0 full, 1 half, 2 empty.</summary>
    private static Texture2D BarIcon(IconKind kind, int state)
    {
        const int n = 9;
        Image img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        Color fill = kind switch
        {
            IconKind.Heart => new Color(0.83f, 0.14f, 0.14f),
            IconKind.Hunger => new Color(0.74f, 0.47f, 0.22f),
            IconKind.Armor => new Color(0.72f, 0.78f, 0.88f),
            _ => new Color(0.55f, 0.80f, 0.97f),
        };
        Color dim = new(0.22f, 0.22f, 0.25f, 0.85f);

        if (kind == IconKind.Armor)
        {
            string[] plate =
            [
                "..XXX..",
                ".XXXXX.",
                "XXXXXXX",
                "XX...XX",
                "XX...XX",
                ".X...X.",
            ];
            for (int y = 0; y < plate.Length; y++)
            {
                for (int x = 0; x < plate[y].Length; x++)
                {
                    if (plate[y][x] == 'X')
                    {
                        img.SetPixel(x + 1, y + 1, state == 2 ? dim : fill);
                    }
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        if (kind == IconKind.Bubble)
        {
            bool dimBubble = state == 2;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - 4.0f, y - 4.0f).Length();
                    if (d <= 3.7f)
                    {
                        Color c = d > 2.6f ? fill.WithAlpha(0.55f) : new Color(0.87f, 0.96f, 1.0f, 0.95f);
                        img.SetPixel(x, y, dimBubble ? dim : c);
                    }
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        string[] rows = kind == IconKind.Heart
                ?
                [
                    ".XX.XX.",
                    "XXXXXXX",
                    "XXXXXXX",
                    ".XXXXX.",
                    "..XXX..",
                    "...X...",
                ]
                :
                [
                    "..XXX..",
                    ".XXXXX.",
                    ".XXXXX.",
                    "..XXX..",
                    "....XX.",
                    ".....XX",
                    "....XX.",
                ];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] != 'X')
                {
                    continue;
                }

                bool empty = state == 2;
                bool halfDim = state == 1 && y > 3;
                bool bone = kind == IconKind.Hunger && y > 3;
                Color c = empty || halfDim ? dim : bone ? new Color(0.90f, 0.88f, 0.82f) : fill;
                if (!empty && !halfDim && kind == IconKind.Heart && x < 2 && y < 2)
                {
                    c = fill.Lightened(0.25f);   // heart highlight
                }

                img.SetPixel(x + 1, y + 1, c);
            }
        }

        return ImageTexture.CreateFromImage(img);
    }

    private void RefreshSurvival()
    {
        if (PlayerRef == null)
        {
            return;
        }

        PlayerStats stats = PlayerRef.Stats;
        bool survival = PlayerRef.Mode == GameMode.Survival;
        _statsRow.Visible = survival;
        if (!survival)
        {
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            float hp = stats.Health - i * 2.0f;
            _hearts[i].Texture = _heartTex[hp >= 2.0f ? 0 : hp >= 1.0f ? 1 : 2];
            float food = stats.Hunger - i * 2.0f;
            _hungers[i].Texture = _hungerTex[food >= 2.0f ? 0 : food >= 1.0f ? 1 : 2];
        }

        int armor = PlayerRef.ArmorPoints;
        _armorRow.Visible = armor > 0;
        for (int i = 0; i < _armorIcons.Length; i++)
        {
            _armorIcons[i].Texture = _armorTex[armor >= (i + 1) * 5 ? 0 : 1];
        }

        bool showBreath = stats.Breath < PlayerStats.MaxBreath;
        _breathRow.Visible = showBreath;
        if (showBreath)
        {
            float bubbles = Mathf.Ceil(stats.Breath);
            for (int i = 0; i < 10; i++)
            {
                _bubbles[i].Modulate = new Color(1, 1, 1, i < bubbles ? 1.0f : 0.15f);
            }
        }
    }

    private void ShowDeath()
    {
        // Death owns the screen: close inventory/console/trader/pause first so
        // their handlers cannot recapture the mouse and hide the Respawn button.
        UiFocus.CloseAll(this);
        _deathPanel.Visible = true;
        _vignetteStrength = 0.75f;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void HideDeath()
    {
        _deathPanel.Visible = false;
        if (!GetTree().Paused)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    // -----------------------------------------------------------------------
    // Overlays: toast, info panel (F3), help (H), fly badge
    // -----------------------------------------------------------------------

    private void BuildOverlays(Control root)
    {
        _toast = MakePanel(8.0f);
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 96);
        _toast.Modulate = Colors.White.WithAlpha(0.0f);
        root.AddChild(_toast);
        _toastLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _toastLabel.AddThemeFontSizeOverride("font_size", 16);
        _toastLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.97f));
        _toast.AddChild(_toastLabel);

        _hintPanel = MakePanel(8.0f);
        _hintPanel.Visible = false;
        root.AddChild(_hintPanel);
        _hintLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hintLabel.AddThemeFontSizeOverride("font_size", 14);
        _hintLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.92f, 0.68f, 0.98f));
        _hintLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _hintLabel.AddThemeConstantOverride("outline_size", 3);
        _hintPanel.AddChild(_hintLabel);
        _hintPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.Minsize, 14);

        _infoPanel = MakePanel(6.0f);
        _infoPanel.Position = new Vector2(10, 8);
        _infoPanel.Visible = false;
        root.AddChild(_infoPanel);
        _infoLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _infoLabel.AddThemeFontSizeOverride("font_size", 13);
        _infoLabel.AddThemeConstantOverride("line_spacing", 2);
        _infoLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1.0f, 0.95f));
        _infoPanel.AddChild(_infoLabel);

        _helpPanel = MakePanel(10.0f);
        _helpPanel.Visible = false;
        root.AddChild(_helpPanel);
        Label help = new()
        {
            Text = HelpText,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        help.AddThemeFontSizeOverride("font_size", 15);
        help.AddThemeConstantOverride("line_spacing", 4);
        help.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1.0f, 0.96f));
        _helpPanel.AddChild(help);
        _helpPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);

        _flyBadge = MakePanel(6.0f);
        _flyBadge.Visible = false;
        root.AddChild(_flyBadge);
        Label flyLabel = new()
        {
            Text = "FLYING  ·  F / tap Space to land",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        flyLabel.AddThemeFontSizeOverride("font_size", 13);
        flyLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 1.0f, 0.95f));
        _flyBadge.AddChild(flyLabel);
        _flyBadge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 12);

        // Death screen: softcore rules, so it is an invitation, not a setback.
        VBoxContainer deadBox = new();
        deadBox.AddThemeConstantOverride("separation", 12);
        Label deadTitle = new()
        {
            Text = "YOU DIED",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        deadTitle.AddThemeFontSizeOverride("font_size", 34);
        deadTitle.AddThemeColorOverride("font_color", new Color(0.95f, 0.32f, 0.30f));
        deadBox.AddChild(deadTitle);
        Label deadSub = new()
        {
            Text = "your items are safe — softcore rules",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        deadSub.AddThemeFontSizeOverride("font_size", 14);
        deadSub.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.92f, 0.85f));
        deadBox.AddChild(deadSub);
        deadBox.AddChild(MenuSkin.MakeButton("Respawn", () => PlayerRef?.Respawn(), 220));
        _deathPanel = MenuSkin.Wrap(deadBox, 340);
        _deathPanel.Visible = false;
        _deathPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        root.AddChild(_deathPanel);
        _deathPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
    }

    private void BuildMinimap(Control root)
    {
        _mapPanel = MakePanel(8.0f);
        _minimap = new Minimap
        {
            Name = "Minimap",
        };
        _mapPanel.AddChild(_minimap);
        root.AddChild(_mapPanel);
        // Anchor after the child exists: a top-right preset on a still-empty
        // panel would lock its width at zero and grow it off-screen.
        _mapPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize, 10);
    }

    private static PanelContainer MakePanel(float corner)
    {
        PanelContainer pc = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        StyleBoxFlat sb = new()
        {
            BgColor = new Color(0.04f, 0.05f, 0.07f, 0.72f),
            BorderColor = new Color(1, 1, 1, 0.12f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        sb.SetCornerRadiusAll((int)corner);
        sb.SetBorderWidthAll(1);
        pc.AddThemeStyleboxOverride("panel", sb);
        return pc;
    }

    public void ShowToast(string text)
    {
        _toastLabel.Text = text;
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 96);
        _toastTime = 3.0;
    }

    /// <summary>Contextual onboarding hint pinned near the bottom-left.</summary>
    public void SetHint(string text)
    {
        if (_hintPanel == null)
        {
            return;
        }

        _hintPanel.Visible = true;
        _hintLabel.Text = text;
    }

    public void HideHint()
    {
        if (_hintPanel != null)
        {
            _hintPanel.Visible = false;
        }
    }

    public void CloseHelp()
    {
        _helpPanel.Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (_deathPanel.Visible)
        {
            // Keyboard fallback: the death screen must never depend on a click.
            if (key.Keycode is Key.Enter or Key.R)
            {
                PlayerRef?.Respawn();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (PlayerRef?.InputLocked == true || PlayerRef?.Stats.Dead == true)
        {
            return;
        }

        if (key.IsActionPressed("toggle_info"))
        {
            _infoPanel.Visible = !_infoPanel.Visible;
        }
        else if (key.IsActionPressed("toggle_help"))
        {
            _helpPanel.Visible = !_helpPanel.Visible;
        }
        else if (key.IsActionPressed("toggle_map"))
        {
            _mapPanel.Visible = !_mapPanel.Visible;
        }
        else if (key.IsActionPressed("map_zoom_in"))
        {
            _minimap.ZoomIn();
        }
        else if (key.IsActionPressed("map_zoom_out"))
        {
            _minimap.ZoomOut();
        }
    }

    // -----------------------------------------------------------------------
    // Per-frame updates
    // -----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        _toastTime = System.Math.Max(_toastTime - delta, 0.0);
        _toast.Modulate = _toast.Modulate.WithAlpha(
                Mathf.MoveToward(_toast.Modulate.A, _toastTime > 0.0 ? 1.0f : 0.0f, (float)delta * 4.0f));
        _heldTime = System.Math.Max(_heldTime - delta, 0.0);
        _heldLabel.Modulate = _heldLabel.Modulate.WithAlpha(
                Mathf.MoveToward(_heldLabel.Modulate.A, _heldTime > 0.0 ? 1.0f : 0.0f, (float)delta * 2.5f));
        if (PlayerRef == null)
        {
            return;
        }

        float waterAlpha = PlayerRef.Underwater
                ? System.Math.Min(_waterOverlay.Color.A + (float)delta * 3.0f, 0.38f)
                : System.Math.Max(_waterOverlay.Color.A - (float)delta * 3.0f, 0.0f);
        _waterOverlay.Color = _waterOverlay.Color.WithAlpha(waterAlpha);

        // Damage vignette: fades out, pulses while near death, dims while dead.
        _vignetteStrength = System.Math.Max(_vignetteStrength - (float)delta * 1.3f, 0.0f);
        float pulse = 0.0f;
        bool dead = PlayerRef.Stats.Dead;
        if (!dead && PlayerRef.Mode == GameMode.Survival && PlayerRef.Stats.Health <= 6.0f)
        {
            pulse = 0.16f + 0.07f * Mathf.Sin((float)Time.GetTicksMsec() * 0.004f);
        }

        _vignette.Color = _vignette.Color.WithAlpha(dead ? 0.45f : Mathf.Max(_vignetteStrength, pulse) * 0.45f);

        _flyBadge.Visible = PlayerRef.Flying;

        // Dusk/dawn beats: one toast per transition, tone by difficulty.
        VoxelWorld? world = PlayerRef.World;
        if (world != null && !PlayerRef.Stats.Dead)
        {
            bool night = DayNight.IsNight(world.TimeOfDay);
            if (_wasNight is { } before && before != night)
            {
                bool survival = PlayerRef.Mode == GameMode.Survival;
                if (night)
                {
                    ShowToast(survival && Settings.Difficulty > Settings.DifficultyPeaceful
                            ? "Night falls — shades rise in the dark"
                            : "Night falls — the stars are out");
                }
                else if (survival)
                {
                    ShowToast("Dawn breaks — you survived the night");
                }
            }

            _wasNight = night;
        }

        _refresh -= delta;
        if (_refresh <= 0.0)
        {
            _refresh = 0.2;
            UpdateInfo();
            RefreshSurvival();
        }
    }

    private void UpdateInfo()
    {
        VoxelWorld? world = PlayerRef!.World;
        if (world == null)
        {
            return;
        }

        Vector3 p = PlayerRef.GlobalPosition;
        List<string> lines = [];
        string quality = (Gfx?.CurrentPreset.Name ?? "MAX") + (Settings.AutoQuality ? " (auto)" : "");
        string genTag = world.WorldGenVersion >= 5
                ? $"{world.PresetName} · H{world.Height}"
                : $"gen v{world.WorldGenVersion}";
        lines.Add($"minecraft-godot   ·   {quality} quality");
        lines.Add($"FPS {Engine.GetFramesPerSecond()}   {genTag}");
        string mode = PlayerRef.Mode == GameMode.Survival ? "survival" : "creative";
        string diff = Settings.DifficultyNames[Settings.Difficulty];
        lines.Add(PlayerRef.Mode == GameMode.Survival
                ? $"{mode} · {diff} · hp {PlayerRef.Stats.Health:0} · food {PlayerRef.Stats.Hunger:0}"
                : $"{mode} · {diff}");
        lines.Add($"XYZ {p.X:0.0} / {p.Y:0.0} / {p.Z:0.0}");
        int cx = Mathf.FloorToInt(p.X) >> 4;
        int cz = Mathf.FloorToInt(p.Z) >> 4;
        Biome biome = world.BiomeAt(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));
        lines.Add($"Chunk {cx},{cz}   Biome {((int)biome < BiomeNames.Length ? BiomeNames[(int)biome] : "?")}");
        double t = world.TimeOfDay;
        int hh = (int)(t * 24.0);
        int mm = (int)(t * 24.0 % 1.0 * 60.0);
        double yaw = PlayerRef.Rotation.Y;
        lines.Add($"Time {hh:00}:{mm:00}   Facing {FacingOf(yaw)} ({(int)(-yaw * 180.0 / System.Math.PI + 360.0) % 360}°)");
        lines.Add($"Looking at: {LookingAt()}");
        string weatherName = Weather is null ? "clear" : Minecraft.Weather.StateNames[(int)Weather.Current];
        lines.Add($"Mobs {Mobs?.PassiveCount ?? 0} animals · {Mobs?.HostileCount ?? 0} hostiles   Chunks {world.ChunkCount}   {weatherName}");
        (double meshAvg, double meshMax) = Profiler.BuildStats();
        lines.Add($"Stream q {Profiler.FreshQueued}+{Profiler.RebuildQueued} rebuild · workers {(Profiler.WorkersActive ? "on" : "idle")}"
                + $"   Mesh {meshAvg:0.0} ms avg / {meshMax:0.0} max");
        lines.Add($"RAM {(int)(Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0)} MB   VRAM {(int)(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0)} MB"
                + $"   Data {Profiler.DataChunks} chunks");
        _infoLabel.Text = string.Join("\n", lines);
    }

    private static string FacingOf(double yaw)
    {
        double ang = System.Math.Atan2(-System.Math.Sin(yaw), System.Math.Cos(yaw));  // 0 = north, +90° = east
        double deg = ((ang * 180.0 / System.Math.PI) + 360.0) % 360.0;
        return Facings[(int)System.Math.Round(deg / 45.0, System.MidpointRounding.AwayFromZero) % 8];
    }

    private string LookingAt()
    {
        if (PlayerRef!.Ray == null || !PlayerRef.Ray.IsColliding())
        {
            return "—";
        }

        if (PlayerRef.Ray.GetCollider() is Mob mob)
        {
            return mob.DisplayName;
        }

        if (PlayerRef.World == null)
        {
            return "—";
        }

        Vector3 pt = PlayerRef.Ray.GetCollisionPoint() - PlayerRef.Ray.GetCollisionNormal() * 0.5f;
        BlockId id = PlayerRef.World.GetBlock(Mathf.FloorToInt(pt.X), Mathf.FloorToInt(pt.Y), Mathf.FloorToInt(pt.Z));
        return BlockDb.IsDefined(id) ? BlockDb.NameOf(id) : "—";
    }
}
