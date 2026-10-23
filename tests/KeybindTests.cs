using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class KeybindTests
{
    public static void Run()
    {
        HashSet<string> actions = [];
        TestAssert.Check(Keybinds.Bindable.Length > 8, "keybind list is populated");
        foreach ((string action, string label) in Keybinds.Bindable)
        {
            TestAssert.Check(actions.Add(action), $"keybind {action} is unique");
            TestAssert.Check(!string.IsNullOrWhiteSpace(label), $"keybind {action} has a label");
            TestAssert.Check(InputMap.HasAction(action), $"InputMap has {action}");
            TestAssert.Check(Keybinds.LabelFor(action) != "—", $"keybind {action} has a key");
        }

        TestAssert.Check(Settings.ShowHints, "hints default on");
        TestAssert.Check(Settings.HeadBob, "head bob default on");
    }
}
