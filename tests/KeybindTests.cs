using System;
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
        TestAssert.Equal(Error.Ok, GamepadBindings.SetButton("jump", JoyButton.X),
                "controller binding changes report successful persistence");
        TestAssert.Equal("X", GamepadBindings.LabelFor("jump"), "controller button can be rebound");
        TestAssert.Equal("jump", GamepadBindings.Conflict("sprint", JoyButton.X),
                "controller binding conflicts are detected");
        GamepadBindings.ResetDefaults();
        TestAssert.Equal("A", GamepadBindings.LabelFor("jump"), "controller reset restores defaults");

        // Keyboard rebinding must not erase the additive controller event.
        TestAssert.Check(Keybinds.Conflict("jump", Key.F6)?.Contains("cycle preset") == true,
                "F6 remains reserved for graphics preset cycling");
        TestAssert.Check(Keybinds.Conflict("jump", Key.Key1)?.Contains("hotbar 1") == true,
                "number keys remain reserved for direct hotbar selection");
        TestAssert.Equal(Error.InvalidParameter, Keybinds.Rebind("jump", Key.F6, out string? reservedConflict),
                "reserved gameplay keys cannot be rebound");
        TestAssert.Check(reservedConflict != null, "reserved-key rejection reports the owning action");
        TestAssert.Equal(Error.Ok, Keybinds.Rebind("jump", Key.F10),
                "an unreserved key can be rebound and persisted");
        TestAssert.Equal("A", GamepadBindings.LabelFor("jump"),
                "keyboard rebinding preserves the controller binding");
        TestAssert.Equal(Error.Ok, Keybinds.ResetDefaults(), "keyboard reset restores persisted defaults");

        ConfigFile settingsConfig = new();
        settingsConfig.SetValue("input", "mouse_sensitivity", 0.0033f);
        settingsConfig.SetValue("camera", "fov", 92.0f);
        settingsConfig.SetValue("audio", "master", 0.73f);
        settingsConfig.SetValue("video", "view_distance", 18);
        settingsConfig.SetValue("gameplay", "difficulty", 2);
        const string settingsRoundTripPath = "user://w09-settings-roundtrip.cfg";
        TestAssert.Equal(Error.Ok, Settings.SaveConfig(settingsConfig, settingsRoundTripPath),
                "settings ConfigFile writes report success");
        ConfigFile loadedSettings = new();
        TestAssert.Equal(Error.Ok, loadedSettings.Load(settingsRoundTripPath),
                "settings ConfigFile round trip can be loaded");
        TestAssert.Check(Settings.TryReadFloat(loadedSettings.GetValue("input", "mouse_sensitivity"),
                        0.0022f, 0.0005f, 0.006f, out float sensitivity) && Mathf.IsEqualApprox(sensitivity, 0.0033f),
                "mouse sensitivity survives a settings ConfigFile round trip");
        TestAssert.Check(Settings.TryReadFloat(loadedSettings.GetValue("camera", "fov"),
                        75.0f, 60.0f, 110.0f, out float fov) && Mathf.IsEqualApprox(fov, 92.0f),
                "camera FOV survives a settings ConfigFile round trip");
        TestAssert.Check(Settings.TryReadInt(loadedSettings.GetValue("video", "view_distance"),
                        5, 2, 24, out int viewDistance) && viewDistance == 18,
                "view distance survives a settings ConfigFile round trip");
        TestAssert.Check(Settings.TryReadInt(loadedSettings.GetValue("gameplay", "difficulty"),
                        0, 0, 2, out int difficulty) && difficulty == 2,
                "difficulty survives a settings ConfigFile round trip");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(settingsRoundTripPath));

        ConfigFile malformed = new();
        malformed.SetValue("input", "mouse_sensitivity", "not-a-number");
        malformed.SetValue("audio", "muted", 1);
        malformed.SetValue("video", "view_distance", 999);
        TestAssert.Check(!Settings.TryReadFloat(malformed.GetValue("input", "mouse_sensitivity"),
                        0.0022f, 0.0005f, 0.006f, out float safeSensitivity)
                        && Mathf.IsEqualApprox(safeSensitivity, 0.0022f),
                "wrong-typed settings retain the safe default");
        TestAssert.Check(!Settings.TryReadBool(malformed.GetValue("audio", "muted"), false, out bool safeMute)
                        && !safeMute,
                "wrong-typed Boolean settings are rejected");
        TestAssert.Check(!Settings.TryReadInt(malformed.GetValue("video", "view_distance"), 5, 2, 24, out _),
                "out-of-range integer settings are rejected");
        TestAssert.Check(Settings.SaveConfig(malformed, "user://w09-missing-settings-dir/settings.cfg") != Error.Ok,
                "settings persistence failures are observable");

        ConfigFile controllerConfig = new();
        controllerConfig.SetValue("buttons", "jump", (int)JoyButton.X);
        TestAssert.Check(GamepadBindings.TryReadButton(controllerConfig, "jump", JoyButton.A, out JoyButton validButton)
                        && validButton == JoyButton.X,
                "controller button bindings survive ConfigFile serialization");
        controllerConfig.SetValue("buttons", "jump", "invalid-button");
        TestAssert.Check(!GamepadBindings.TryReadButton(controllerConfig, "jump", JoyButton.A, out JoyButton safeButton)
                        && safeButton == JoyButton.A,
                "wrong-typed controller bindings preserve the default button");
        controllerConfig.SetValue("buttons", "jump", (int)JoyButton.Max);
        TestAssert.Check(!GamepadBindings.TryReadButton(controllerConfig, "jump", JoyButton.A, out _),
                "sentinel controller button values are rejected");
        TestAssert.Check(GamepadBindings.SaveConfig(controllerConfig,
                        "user://w09-missing-controller-dir/gamepad.cfg") != Error.Ok,
                "controller binding persistence failures are observable");

        TestAssert.Check(PhotoMode.CanTogglePhotoMode(true, true, true, true),
                "photo mode can always handle its own exit even while locked or dead");
        TestAssert.Check(!PhotoMode.CanTogglePhotoMode(false, true, false, false),
                "photo mode cannot be entered while another modal owns player input");
        TestAssert.Check(!PhotoMode.CanTogglePhotoMode(false, false, false, true),
                "photo mode cannot enter while the scene tree is paused");

        for (int i = 0; i <= (int)Biome.Meadow; i++)
            TestAssert.Check(Hud.BiomeName((Biome)i) != "?", $"F3 HUD resolves biome id {i}");
        TestAssert.Equal("?", Hud.BiomeName((Biome)255), "unknown biome ids keep a clear fallback");

        VBoxContainer focusRoot = new();
        Button disabledFocus = new() { Disabled = true };
        Button availableFocus = new();
        focusRoot.AddChild(disabledFocus);
        focusRoot.AddChild(availableFocus);
        TestAssert.Check(ReferenceEquals(UiFocus.FindFocusable(focusRoot), availableFocus),
                "focus selection skips disabled controls");
        TestAssert.FreeAll(focusRoot);

        Sound.EnsureBuses();
        int sfxBus = AudioServer.GetBusIndex("Sfx");
        int initialEffects = AudioServer.GetBusEffectCount(sfxBus);
        AudioEffectReverb unrelatedEffect = new();
        AudioEffectLowPassFilter ownedEffect = new();
        AudioServer.AddBusEffect(sfxBus, unrelatedEffect, initialEffects);
        AudioServer.AddBusEffect(sfxBus, ownedEffect, initialEffects + 1);
        TestAssert.Equal(initialEffects + 1, Sound.FindOwnedBusEffect(sfxBus, ownedEffect),
                "scene-owned underwater filter is found by identity, not fixed slot");
        Sound.RemoveOwnedBusEffect(sfxBus, ownedEffect);
        TestAssert.Equal(initialEffects, Sound.FindOwnedBusEffect(sfxBus, unrelatedEffect),
                "removing the underwater filter preserves and reindexes other bus effects");
        Sound.RemoveOwnedBusEffect(sfxBus, unrelatedEffect);
        TestAssert.Equal(initialEffects, AudioServer.GetBusEffectCount(sfxBus),
                "audio filter cleanup restores the original bus effect count");

        SceneTree testTree = (SceneTree)Engine.GetMainLoop();
        SceneTreeTimer pendingTimer = testTree.CreateTimer(60.0);
        bool delayedCallbackRan = false;
        Action delayedCallback = () => delayedCallbackRan = true;
        pendingTimer.Timeout += delayedCallback;
        Dictionary<SceneTreeTimer, Action> pendingThunder = new() { [pendingTimer] = delayedCallback };
        Sound.CancelPendingThunder(pendingThunder);
        TestAssert.Equal(0, pendingThunder.Count, "scene teardown disconnects and releases pending thunder callbacks");
        TestAssert.Check(!delayedCallbackRan, "cancelled delayed thunder has not run");

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
