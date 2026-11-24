using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Rebindable keyboard actions. Defaults are captured from the project's
/// InputMap at boot, then an optional user://keybinds.cfg overrides them.
/// </summary>
public static class Keybinds
{
    private const string Path = "user://keybinds.cfg";

    public static readonly (string Action, string Label)[] Bindable =
    [
        ("forward", "Move forward"),
        ("back", "Move back"),
        ("left", "Strafe left"),
        ("right", "Strafe right"),
        ("jump", "Jump / swim / climb"),
        ("sprint", "Sprint / dive"),
        ("inventory", "Inventory"),
        ("pause", "Pause menu"),
        ("console", "Dev console"),
        ("toggle_fly", "Toggle fly"),
        ("photo_mode", "Photo mode"),
        ("toggle_map", "Minimap"),
        ("map_zoom_in", "Minimap zoom in"),
        ("map_zoom_out", "Minimap zoom out"),
        ("toggle_info", "Info panel"),
        ("toggle_help", "Help"),
        ("journal", "Achievements journal"),
        ("hotbar_prev", "Previous hotbar slot"),
        ("hotbar_next", "Next hotbar slot"),
    ];

    private static readonly Dictionary<string, Key> Defaults = [];
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        CaptureDefaults();
        ConfigFile cfg = new();
        if (cfg.Load(Path) != Error.Ok)
        {
            _loaded = true;
            return;
        }

        foreach ((string action, _) in Bindable)
        {
            if (!cfg.HasSectionKey("keys", action))
            {
                continue;
            }

            Variant stored = cfg.GetValue("keys", action, (int)Key.None);
            if (!TryReadKey(stored, out Key key))
            {
                GD.PushWarning($"Ignoring invalid keyboard binding in keys/{action}: expected a defined key code.");
                continue;
            }
            if (Conflict(action, key) == null)
                Apply(action, key);
            else
                GD.PushWarning($"Ignoring conflicting keyboard binding in keys/{action}: {key} is already reserved.");
        }
        _loaded = true;
    }

    internal static bool TryReadKey(Variant stored, out Key key)
    {
        key = Key.None;
        if (stored.VariantType != Variant.Type.Int)
            return false;
        int code = (int)stored;
        if (code == (int)Key.None || !Enum.IsDefined(typeof(Key), (long)code))
            return false;
        key = (Key)code;
        return true;
    }

    internal static Error SaveConfig(ConfigFile config, string path) => config.Save(path);

    public static Error Save()
    {
        ConfigFile cfg = new();
        foreach ((string action, _) in Bindable)
        {
            Key key = KeyOf(action);
            if (key != Key.None)
            {
                cfg.SetValue("keys", action, (int)key);
            }
        }

        Error result = SaveConfig(cfg, Path);
        if (result != Error.Ok)
            GD.PushError($"Could not persist keyboard bindings to {Path}: {result}.");
        return result;
    }

    public static Error Rebind(string action, Key key) => Rebind(action, key, out _);

    public static Error Rebind(string action, Key key, out string? conflict)
    {
        conflict = Conflict(action, key);
        if (key == Key.None || !IsBindable(action) || conflict != null)
            return Error.InvalidParameter;
        Apply(action, key);
        return Save();
    }

    public static Error ResetDefaults()
    {
        foreach ((string action, _) in Bindable)
        {
            if (Defaults.TryGetValue(action, out Key key) && key != Key.None)
            {
                Apply(action, key);
            }
            else if (InputMap.HasAction(action))
            {
                foreach (InputEvent existing in InputMap.ActionGetEvents(action))
                    if (existing is InputEventKey)
                        InputMap.ActionEraseEvent(action, existing);
            }
        }

        return Save();
    }

    public static string LabelFor(string action)
    {
        Key key = KeyOf(action);
        return key == Key.None ? "—" : key.ToString();
    }

    /// <summary>Label of another bindable action already using <paramref name="key"/>, or null.</summary>
    public static string? Conflict(string action, Key key)
    {
        if (key == Key.None)
        {
            return null;
        }

        foreach (StringName actionName in InputMap.GetActions())
        {
            string other = actionName.ToString();
            if (other == action || !InputMap.HasAction(other))
                continue;
            foreach (InputEvent inputEvent in InputMap.ActionGetEvents(other))
            {
                if (inputEvent is not InputEventKey bound)
                    continue;
                if (bound.Keycode == key || bound.PhysicalKeycode == key)
                {
                    foreach ((string bindableAction, string label) in Bindable)
                        if (bindableAction == other)
                            return label;
                    return other.Replace('_', ' ');
                }
            }
        }
        return null;
    }

    private static bool IsBindable(string action)
    {
        foreach ((string candidate, _) in Bindable)
            if (candidate == action)
                return true;
        return false;
    }

    private static void Apply(string action, Key key)
    {
        if (!InputMap.HasAction(action))
        {
            return;
        }

        foreach (InputEvent existing in InputMap.ActionGetEvents(action))
        {
            if (existing is InputEventKey)
                InputMap.ActionEraseEvent(action, existing);
        }
        InputEventKey ev = new()
        {
            Keycode = key,
            PhysicalKeycode = key,
        };
        InputMap.ActionAddEvent(action, ev);
    }

    private static Key KeyOf(string action)
    {
        if (!InputMap.HasAction(action))
        {
            return Key.None;
        }

        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey key)
            {
                return key.Keycode;
            }
        }

        return Key.None;
    }

    private static void CaptureDefaults()
    {
        if (Defaults.Count > 0)
        {
            return;
        }

        foreach ((string action, _) in Bindable)
        {
            Defaults[action] = KeyOf(action);
        }
    }
}
