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
    ];

    private static readonly Dictionary<string, Key> Defaults = [];
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        CaptureDefaults();
        ConfigFile cfg = new();
        if (cfg.Load(Path) != Error.Ok)
        {
            return;
        }

        foreach ((string action, _) in Bindable)
        {
            if (!cfg.HasSectionKey("keys", action))
            {
                continue;
            }

            int code = (int)cfg.GetValue("keys", action, (int)Key.None);
            if (code != (int)Key.None)
            {
                Apply(action, (Key)code);
            }
        }
    }

    public static void Save()
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

        cfg.Save(Path);
    }

    public static void Rebind(string action, Key key)
    {
        Apply(action, key);
        Save();
    }

    public static void ResetDefaults()
    {
        foreach ((string action, _) in Bindable)
        {
            if (Defaults.TryGetValue(action, out Key key))
            {
                Apply(action, key);
            }
        }

        Save();
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

        foreach ((string other, string label) in Bindable)
        {
            if (other != action && KeyOf(other) == key)
            {
                return label;
            }
        }

        return null;
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
