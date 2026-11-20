using Godot;

namespace Minecraft;

/// <summary>
/// Slash-command console: press '/', type a command, Enter runs it.
/// The fastest way to iterate on worldgen and test features.
/// </summary>
public partial class DevConsole : CanvasLayer
{
    private VoxelWorld _world = null!;
    private Player _player = null!;
    private Hud _hud = null!;
    private Mobs _mobs = null!;
    private TraderSystem _trader = null!;
    private PanelContainer _panel = null!;
    private LineEdit _entry = null!;
    private bool _open;

    public void Setup(Player player, VoxelWorld world, Hud hud, Mobs mobs, TraderSystem trader)
    {
        _player = player;
        _world = world;
        _hud = hud;
        _mobs = mobs;
        _trader = trader;
        Layer = 11;
        AddToGroup(UiFocus.ModalGroup);
        ProcessMode = ProcessModeEnum.Always;

        _panel = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(620, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        StyleBoxFlat sb = new()
        {
            BgColor = new Color(0.03f, 0.04f, 0.06f, 0.92f),
            BorderColor = new Color(1, 1, 1, 0.18f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        sb.SetCornerRadiusAll(8);
        sb.SetBorderWidthAll(1);
        _panel.AddThemeStyleboxOverride("panel", sb);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 84);

        _entry = new LineEdit
        {
            PlaceholderText = "command…  (/help)",
        };
        _entry.TextSubmitted += text =>
        {
            string result = Run(text);
            if (result.Length > 0)
            {
                _hud.ShowToast(result);
            }

            Close();
        };
        // A focused LineEdit swallows Escape before _UnhandledInput sees it,
        // so the close key is handled right on the field.
        _entry.GuiInput += @event =>
        {
            if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        };
        _panel.AddChild(_entry);
        AddChild(_panel);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetTree().Paused)
        {
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.IsActionPressed("console"))
        {
            if (_open)
            {
                Close();
            }
            else
            {
                Open();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (_open && key.Keycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Open()
    {
        _open = true;
        _panel.Visible = true;
        _entry.Text = "/";
        _entry.CaretColumn = 1;
        _entry.GrabFocus();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
    }

    private void Close()
    {
        _open = false;
        _panel.Visible = false;
        _entry.ReleaseFocus();
        UiFocus.Restore(_player);
    }

    public void ForceClose()
    {
        if (_open)
        {
            Close();
        }
    }

    /// <summary>Execute one command line; returns the status text for the HUD.</summary>
    public string Run(string line)
    {
        string text = line.Trim();
        if (text.StartsWith('/'))
        {
            text = text[1..];
        }

        string[] parts = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "";
        }

        switch (parts[0])
        {
            case "help":
                return "/seed · /tp x y z · /time <0..1|day|noon|night|midnight> · /vd n · /autoq on|off · /greedy on|off · /spawn · /save"
                        + " · /give <item> [n] · /hold <item> [n] · /armor iron|diamond|off · /gamemode · /difficulty · /heal · /hunger n"
                        + " · /kill · /mob <shade|rot|hollow|warden|sheep|cow|pig|chicken> [n] · /trader · /struct <tower|ruin|mine|ember>";
            case "seed":
                return $"seed {_world.WorldSeed}  ·  gen v{_world.WorldGenVersion}";
            case "tp":
                if (parts.Length == 4
                        && float.TryParse(parts[1], out float tx)
                        && float.TryParse(parts[2], out float ty)
                        && float.TryParse(parts[3], out float tz))
                {
                    _player.GlobalPosition = new Vector3(tx, ty, tz);
                    _player.Velocity = Vector3.Zero;
                    return $"tp {tx:0.0} {ty:0.0} {tz:0.0}";
                }

                return "usage: /tp x y z";
            case "time":
                if (parts.Length != 2)
                {
                    return "usage: /time <0..1|day|noon|night|midnight>";
                }

                double t;
                switch (parts[1])
                {
                    case "day":
                        t = 0.25;
                        break;
                    case "noon":
                        t = 0.5;
                        break;
                    case "night":
                        t = 0.75;
                        break;
                    case "midnight":
                        t = 0.0;
                        break;
                    default:
                        if (!double.TryParse(parts[1], out t))
                        {
                            return "usage: /time <0..1|day|noon|night|midnight>";
                        }

                        break;
                }

                _world.TimeOfDay = Mathf.Clamp(t, 0.0, 1.0);
                _world.MarkDirty();
                return $"time {_world.TimeOfDay:0.00}";
            case "vd":
                if (parts.Length == 2 && int.TryParse(parts[1], out int view))
                {
                    _world.SetViewDistance(view);
                    Settings.ViewDistance = _world.ViewDistance;
                    Settings.Save();
                    return $"view distance {_world.ViewDistance}";
                }

                return "usage: /vd 2..24";
            case "autoq":
                if (parts.Length == 2 && (parts[1] == "on" || parts[1] == "off"))
                {
                    Settings.AutoQuality = parts[1] == "on";
                    Settings.Save();
                    return $"auto quality {(Settings.AutoQuality ? "on" : "off")}";
                }

                return "usage: /autoq on|off";
            case "greedy":
                if (parts.Length == 2 && (parts[1] == "on" || parts[1] == "off"))
                {
                    bool on = parts[1] == "on";
                    Mesher.Greedy = on;
                    Settings.GreedyMeshing = on;
                    Settings.Save();
                    _world.RebuildAll();
                    return $"greedy meshing {(on ? "on" : "off")} — rebuilding chunks";
                }

                return "usage: /greedy on|off";
            case "spawn":
                _player.GlobalPosition = _world.FindSpawn();
                _player.Velocity = Vector3.Zero;
                return "teleported to spawn";
            case "save":
                _world.SaveNow();
                return "saved";
            case "give":
            {
                if (parts.Length < 2)
                {
                    return "usage: /give <item> [count]";
                }

                int count = 1;
                int nameEnd = parts.Length;
                if (parts.Length >= 3 && int.TryParse(parts[^1], out int parsed))
                {
                    count = Mathf.Clamp(parsed, 1, 6400);
                    nameEnd = parts.Length - 1;
                }

                string name = string.Join(" ", parts[1..nameEnd]);
                ItemId id = ItemDb.ByName(name);
                if (id == ItemId.None)
                {
                    return $"unknown item: {name}";
                }

                int leftover = _player.Inventory.Add(id, count);
                _player.Inventory.RaiseChanged();
                return leftover > 0
                        ? $"+{count - leftover} {ItemDb.NameOf(id)}  (inventory full)"
                        : $"+{count} {ItemDb.NameOf(id)}";
            }
            case "hold":
            {
                if (parts.Length < 2)
                {
                    return "usage: /hold <item> [count]";
                }

                int count = 1;
                int nameEnd = parts.Length;
                if (parts.Length >= 3 && int.TryParse(parts[^1], out int parsed))
                {
                    count = Mathf.Clamp(parsed, 1, 6400);
                    nameEnd = parts.Length - 1;
                }

                string name = string.Join(" ", parts[1..nameEnd]);
                ItemId id = ItemDb.ByName(name);
                if (id == ItemId.None)
                {
                    return $"unknown item: {name}";
                }

                _player.Inventory.Hotbar[_player.Selected] = ItemDb.New(id, count);
                _player.Inventory.RaiseChanged();
                return $"{ItemDb.NameOf(id)} x{count} in hand";
            }
            case "armor":
            {
                if (parts.Length != 2)
                {
                    return "usage: /armor iron|diamond|off";
                }

                ItemId[] set = parts[1] switch
                {
                    "iron" =>
                    [
                        ItemId.IronHelmet, ItemId.IronChestplate, ItemId.IronLegs, ItemId.IronBoots,
                    ],
                    "diamond" =>
                    [
                        ItemId.DiamondHelmet, ItemId.DiamondChestplate, ItemId.DiamondLegs, ItemId.DiamondBoots,
                    ],
                    "off" => [],
                    _ => [],
                };
                if (parts[1] is not ("iron" or "diamond" or "off"))
                {
                    return "usage: /armor iron|diamond|off";
                }

                for (int i = 0; i < _player.Inventory.Armor.Length; i++)
                {
                    _player.Inventory.Armor[i] = i < set.Length ? ItemDb.New(set[i], 1) : ItemStack.Empty;
                }

                _player.Inventory.RaiseChanged();
                return $"armor {parts[1]}";
            }
            case "gamemode":
                if (parts.Length == 2)
                {
                    switch (parts[1])
                    {
                        case "survival":
                            _player.SetMode(GameMode.Survival);
                            return "gamemode survival";
                        case "creative":
                            _player.SetMode(GameMode.Creative);
                            return "gamemode creative";
                    }
                }

                return "usage: /gamemode survival|creative";
            case "difficulty":
                if (parts.Length == 2)
                {
                    int d = System.Array.IndexOf(Settings.DifficultyNames, parts[1]);
                    if (d >= 0)
                    {
                        Settings.Difficulty = d;
                        Settings.Save();
                        return $"difficulty {Settings.DifficultyNames[d]}";
                    }
                }

                return "usage: /difficulty peaceful|normal|hard";
            case "heal":
                _player.Stats.Heal(PlayerStats.MaxHealth);
                _player.Stats.AddHunger(PlayerStats.MaxHunger);
                return "healed";
            case "hunger":
                if (parts.Length == 2 && float.TryParse(parts[1], out float hungerValue))
                {
                    _player.Stats.SetHunger(hungerValue);
                    return $"hunger {Mathf.Clamp(hungerValue, 0.0f, PlayerStats.MaxHunger):0}";
                }

                return "usage: /hunger 0..20";
            case "kill":
                return _player.ApplyDamage(new DamageInfo(1000.0f, DamageKind.Generic))
                        ? "you died (softcore: items kept)"
                        : "no effect (creative or invulnerable)";
            case "mob":
                if (parts.Length >= 2 && parts[1] is "shade" or "rot" or "hollow" or "warden" or "sheep" or "cow" or "pig" or "chicken")
                {
                    int many = parts.Length >= 3 && int.TryParse(parts[2], out int parsed)
                            ? Mathf.Clamp(parsed, 1, 20)
                            : 1;
                    int spawned = _mobs.SpawnKind(parts[1], many);
                    return spawned > 0 ? $"spawned {spawned} {parts[1]}" : "no room nearby";
                }

                return "usage: /mob shade|rot|hollow|warden|sheep|cow|pig|chicken [n]";
            case "trader":
                return _trader.SpawnNow()
                        ? "a trader has arrived"
                        : "could not find a safe trader spot";
            case "struct":
                if (parts.Length == 2 && parts[1] is "tower" or "ruin" or "mine" or "ember")
                {
                    int kind = parts[1] switch
                    {
                        "tower" => 0,
                        "ruin" => 1,
                        "ember" => 3,
                        _ => 2,
                    };
                    Vector3 pp = _player.GlobalPosition;
                    int pcx = Mathf.FloorToInt(pp.X / VoxelWorld.StructureCell);
                    int pcz = Mathf.FloorToInt(pp.Z / VoxelWorld.StructureCell);
                    if (_world.FindNearestStructure(kind, pcx, pcz, 6) is { } site)
                    {
                        float y = kind == 3
                                ? Structures.DungeonArenaY(site) + 2.0f
                                : site.Y + 3.0f;
                        _player.GlobalPosition = new Vector3(site.X + 0.5f, y, site.Z + 0.5f);
                        _player.Velocity = Vector3.Zero;
                        return $"{parts[1]} at {site.X} {site.Y} {site.Z}";
                    }

                    return $"no {parts[1]} within 6 cells";
                }

                return "usage: /struct <tower|ruin|mine|ember>";
            default:
                return $"unknown command: {parts[0]}  (try /help)";
        }
    }
}
