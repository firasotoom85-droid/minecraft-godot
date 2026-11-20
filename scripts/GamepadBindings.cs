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
        ("hotbar_prev", JoyButton.LeftShoulder),
        ("hotbar_next", JoyButton.RightShoulder),
        ("toggle_map", JoyButton.DpadUp),
        ("journal", JoyButton.DpadDown),
    ];

    private static bool _configured;

    public static void Configure()
    {
        if (_configured) return;
        _configured = true;

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
        foreach ((string action, JoyButton button) in DefaultButtons)
        {
            JoyButton selected = hasConfig && config.HasSectionKey("buttons", action)
                    ? (JoyButton)(int)config.GetValue("buttons", action, (int)button)
                    : button;
            ApplyButton(action, selected);
        }
    }

    public static void SetButton(string action, JoyButton button)
    {
        if (!IsBindable(action)) return;
        ApplyButton(action, button);
        Save();
    }

    public static void ResetDefaults()
    {
        foreach ((string action, JoyButton button) in DefaultButtons)
            ApplyButton(action, button);
        Save();
    }

    public static string LabelFor(string action)
    {
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
            if (inputEvent is InputEventJoypadButton button)
                return button.Button.ToString();
        return "—";
    }

    public static string? Conflict(string action, JoyButton button)
    {
        foreach ((string other, _) in DefaultButtons)
        {
            if (other == action) continue;
            foreach (InputEvent inputEvent in InputMap.ActionGetEvents(other))
                if (inputEvent is InputEventJoypadButton bound && bound.Button == button)
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

    private static void Save()
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
        config.Save(ConfigPath);
    }
}
