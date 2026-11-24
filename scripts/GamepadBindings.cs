using Godot;

namespace Minecraft;

/// <summary>
/// Adds a complete, additive controller map to the existing InputMap. Keyboard
/// bindings are never erased; controller button overrides are stored separately
/// from the legacy keyboard-only keybinds.cfg file.
/// </summary>
public static class GamepadBindings
{
    private const string ConfigPath = "user://gamepad.cfg";

    private static readonly (string Action, JoyButton Button)[] DefaultButtons =
    [
        ("jump", JoyButton.A),
        ("toggle_fly", JoyButton.B),
        ("inventory", JoyButton.Y),
        ("pause", JoyButton.Start),
        ("sprint", JoyButton.LeftStick),
        ("break_block", JoyButton.RightShoulder),
        ("place_block", JoyButton.LeftShoulder),
        ("pick_block", JoyButton.RightStick),
        ("hotbar_prev", JoyButton.DpadLeft),
        ("hotbar_next", JoyButton.DpadRight),
        ("toggle_map", JoyButton.DpadUp),
        ("journal", JoyButton.DpadDown),
    ];

    public static readonly (string Action, string Label)[] ControllerOnlyBindable =
    [
        ("break_block", "Mine / break block"),
        ("place_block", "Place / use block"),
        ("pick_block", "Pick block"),
    ];

    private static bool _configured;

    public static void Configure()
    {
        if (_configured) return;

        AddAction("hotbar_prev");
        AddAction("hotbar_next");
        AddAxis("forward", JoyAxis.LeftY, -1.0f);
        AddAxis("back", JoyAxis.LeftY, 1.0f);
        AddAxis("left", JoyAxis.LeftX, -1.0f);
        AddAxis("right", JoyAxis.LeftX, 1.0f);
        AddAxis("look_left", JoyAxis.RightX, -1.0f);
        AddAxis("look_right", JoyAxis.RightX, 1.0f);
        AddAxis("look_up", JoyAxis.RightY, -1.0f);
        AddAxis("look_down", JoyAxis.RightY, 1.0f);

        ConfigFile config = new();
        bool hasConfig = config.Load(ConfigPath) == Error.Ok;
        Dictionary<string, JoyButton> resolved = ResolveSavedButtons(config);
        foreach ((string action, JoyButton button) in DefaultButtons)
        {
            if (hasConfig && config.HasSectionKey("buttons", action))
            {
                if (!TryReadButton(config, action, button, out JoyButton requested))
                    GD.PushWarning($"Ignoring invalid controller binding in buttons/{action}; keeping {button}.");
                else if (resolved[action] != requested)
                    GD.PushWarning($"Ignoring conflicting controller binding in buttons/{action}; keeping {button}.");
            }
            ApplyButton(action, resolved[action]);
        }
        _configured = true;
    }

    /// <summary>Resolve all controller overrides together so valid button swaps survive loading.</summary>
    internal static Dictionary<string, JoyButton> ResolveSavedButtons(ConfigFile config)
    {
        Dictionary<string, JoyButton> resolved = [];
        HashSet<string> overrides = [];
        foreach ((string action, JoyButton fallback) in DefaultButtons)
        {
            resolved[action] = fallback;
            if (!config.HasSectionKey("buttons", action))
                continue;
            if (TryReadButton(config, action, fallback, out JoyButton selected))
            {
                resolved[action] = selected;
                overrides.Add(action);
            }
        }

        while (overrides.Count > 0)
        {
            HashSet<string> rejected = [];
            foreach (string action in overrides)
            {
                JoyButton button = resolved[action];
                foreach ((string other, _) in DefaultButtons)
                {
                    if (other != action && resolved[other] == button)
                    {
                        rejected.Add(action);
                        break;
                    }
                }
            }

            if (rejected.Count == 0)
                break;
            foreach (string action in rejected)
            {
                foreach ((string candidate, JoyButton fallback) in DefaultButtons)
                    if (candidate == action)
                    {
                        resolved[action] = fallback;
                        break;
                    }
                overrides.Remove(action);
            }
        }

        return resolved;
    }

    internal static bool TryReadButton(ConfigFile config, string action, JoyButton fallback, out JoyButton selected)
    {
        selected = fallback;
        Variant stored = config.GetValue("buttons", action, (int)fallback);
        if (stored.VariantType != Variant.Type.Int)
            return false;
        int raw = (int)stored;
        if (raw < 0 || raw >= (int)JoyButton.Max || !System.Enum.IsDefined(typeof(JoyButton), (long)raw))
            return false;
        selected = (JoyButton)raw;
        return true;
    }

    public static Error SetButton(string action, JoyButton button)
    {
        if (!IsBindable(action) || (int)button < 0 || button >= JoyButton.Max
                || !System.Enum.IsDefined(typeof(JoyButton), (long)button))
            return Error.InvalidParameter;
        ApplyButton(action, button);
        return Save();
    }

    public static Error ResetDefaults()
    {
        foreach ((string action, JoyButton button) in DefaultButtons)
            ApplyButton(action, button);
        return Save();
    }

    public static string LabelFor(string action)
    {
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
            if (inputEvent is InputEventJoypadButton button)
                return button.ButtonIndex.ToString();
        return "—";
    }

    public static string? Conflict(string action, JoyButton button)
    {
        foreach ((string other, _) in DefaultButtons)
        {
            if (other == action) continue;
            foreach (InputEvent inputEvent in InputMap.ActionGetEvents(other))
                if (inputEvent is InputEventJoypadButton bound && bound.ButtonIndex == button)
                    return other;
        }
        return null;
    }

    private static void AddAction(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }

    private static void AddAxis(string action, JoyAxis axis, float value)
    {
        AddAction(action);
        InputEventJoypadMotion motion = new() { Axis = axis, AxisValue = value };
        InputMap.ActionAddEvent(action, motion);
    }

    private static void ApplyButton(string action, JoyButton button)
    {
        AddAction(action);
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
            if (inputEvent is InputEventJoypadButton)
                InputMap.ActionEraseEvent(action, inputEvent);
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
    }

    private static bool IsBindable(string action)
    {
        foreach ((string candidate, _) in DefaultButtons)
            if (candidate == action) return true;
        return false;
    }

    internal static Error SaveConfig(ConfigFile config, string path) => config.Save(path);

    private static Error Save()
    {
        ConfigFile config = new();
        foreach ((string action, _) in DefaultButtons)
        {
            foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
            {
                if (inputEvent is InputEventJoypadButton button)
                {
                    config.SetValue("buttons", action, (int)button.ButtonIndex);
                    break;
                }
            }
        }
        Error result = SaveConfig(config, ConfigPath);
        if (result != Error.Ok)
            GD.PushError($"Could not persist controller bindings to {ConfigPath}: {result}.");
        return result;
    }
}
