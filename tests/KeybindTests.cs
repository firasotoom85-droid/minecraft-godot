using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class KeybindTests
{
    public static void Run()
    {
        GamepadBindings.ResetDefaults();
        HashSet<string> actions = [];
        TestAssert.Check(Keybinds.Bindable.Length > 8, "keybind list is populated");
        foreach ((string action, string label) in Keybinds.Bindable)
        {
            TestAssert.Check(actions.Add(action), $"keybind {action} is unique");
            TestAssert.Check(!string.IsNullOrWhiteSpace(label), $"keybind {action} has a label");
            TestAssert.Check(InputMap.HasAction(action), $"InputMap has {action}");
            if (action is not ("hotbar_prev" or "hotbar_next"))
                TestAssert.Check(Keybinds.LabelFor(action) != "—", $"keybind {action} has a key");
        }

        TestAssert.Check(InputMap.HasAction("look_left") && InputMap.HasAction("look_right"),
                "controller look actions are registered");
        TestAssert.Check(InputMap.HasAction("hotbar_prev") && InputMap.HasAction("hotbar_next"),
                "controller hotbar cycling actions are registered");
        TestAssert.Equal("A", GamepadBindings.LabelFor("jump"), "jump has the default controller button");
        TestAssert.Equal("RightShoulder", GamepadBindings.LabelFor("break_block"),
                "mining has the default controller button");
        TestAssert.Equal("DpadLeft", GamepadBindings.LabelFor("hotbar_prev"),
                "hotbar previous is mapped to the D-pad");
        TestAssert.Check(GamepadBindings.Conflict("jump", JoyButton.A) == null,
                "default controller bindings do not conflict");
        GamepadBindings.SetButton("jump", JoyButton.X);
        TestAssert.Equal("X", GamepadBindings.LabelFor("jump"), "controller button can be rebound");
        TestAssert.Equal("jump", GamepadBindings.Conflict("sprint", JoyButton.X),
                "controller binding conflicts are detected");
        GamepadBindings.ResetDefaults();
        TestAssert.Equal("A", GamepadBindings.LabelFor("jump"), "controller reset restores defaults");

        // Keyboard rebinding must not erase the additive controller event.
        Keybinds.Rebind("jump", Key.Space);
        TestAssert.Equal("A", GamepadBindings.LabelFor("jump"),
                "keyboard rebinding preserves the controller binding");
        Keybinds.ResetDefaults();

        TestAssert.Check(Settings.ShowHints, "hints default on");
        TestAssert.Check(Settings.HeadBob, "head bob default on");

        TestAssert.Check(PauseInputPolicy.CanHandlePause(false, false, false),
                "pause opens during normal gameplay");
        TestAssert.Check(!PauseInputPolicy.CanHandlePause(false, false, true),
                "pause does not stack over another modal");
        TestAssert.Check(PauseInputPolicy.CanHandlePause(true, false, true),
                "pause can resume when no nested modal is open");
        TestAssert.Check(!PauseInputPolicy.CanHandlePause(true, true, true),
                "keybind modal owns Escape and prevents its parent pause menu from resuming");
    }
}
