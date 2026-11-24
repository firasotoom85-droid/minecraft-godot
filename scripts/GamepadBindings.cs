using System.Collections.Generic;
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

    public static readonly (string Action, string Label)[] ControllerOnlyActions =
    [
        ("break_block", "Mine blocks"),
        ("place_block", "Place / use"),
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
        if (config.Load(ConfigPath) != Error.Ok)
            config = new ConfigFile();
        ApplyStoredBindings(config);
        _configured = true;
    }

    internal static void ApplyStoredBindings(ConfigFile config)
    {
        HashSet<JoyButton> claimed = [];
        foreach ((string action, JoyButton fallback) in DefaultButtons)
        {
            JoyButton selected = fallback;
            if (config.HasSectionKey("buttons", action))
            {
                if (TryReadButton(config, action, fallback, out JoyButton saved)
                        && !claimed.Contains(saved))
                {
                    selected = saved;
                }
                else
                {
                    GD.PushWarning($"Ignoring invalid or duplicate controller binding in buttons/{action}; restoring a free default.");
                }
            }

            if (claimed.Contains(selected))
                selected = FindAvailableButton(claimed, fallback);
            if (selected == JoyButton.Max)
            {
                GD.PushWarning($"No unused controller button remains for {action}.");
                continue;
            }

            claimed.Add(selected);
            ApplyButton(action, selected);
        }
    }

    private static JoyButton FindAvailableButton(HashSet<JoyButton> claimed, JoyButton preferred)
    {
        if (!claimed.Contains(preferred))
            return preferred;
        for (int raw = 0; raw < (int)JoyButton.Max; raw++)
        {
            if (System.Enum.IsDefined(typeof(JoyButton), (long)raw)
                    && !claimed.Contains((JoyButton)raw))
                return (JoyButton)raw;
        }
        return JoyButton.Max;
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
                || !System.Enum.IsDefined(typeof(JoyButton), (long)button)
                || Conflict(action, button) != null)
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
            if (other == action || !InputMap.HasAction(other)) continue;
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
