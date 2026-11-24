using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class KeybindTests
{
    public static void Run()
    {
        GamepadBindings.ResetDefaults();
        ConfigFile keySwap = new();
        keySwap.SetValue("keys", "forward", (int)Key.E);
        keySwap.SetValue("keys", "inventory", (int)Key.Z);
        Dictionary<string, Key> resolvedKeys = Keybinds.ResolveSavedBindings(keySwap);
        TestAssert.Equal(Key.E, resolvedKeys["forward"],
                "saved keyboard remaps can take a key another saved action vacated");
        TestAssert.Equal(Key.Z, resolvedKeys["inventory"],
                "keyboard remap sets restore independently of declaration order");

        ConfigFile duplicateKeys = new();
        duplicateKeys.SetValue("keys", "jump", (int)Key.F10);
        duplicateKeys.SetValue("keys", "sprint", (int)Key.F10);
        Dictionary<string, Key> safeKeys = Keybinds.ResolveSavedBindings(duplicateKeys);
        TestAssert.Check(safeKeys["jump"] != Key.F10 && safeKeys["sprint"] != Key.F10
                        && safeKeys["jump"] != safeKeys["sprint"],
                "duplicate saved keyboard overrides fall back to non-conflicting defaults");

        ConfigFile buttonSwap = new();
        buttonSwap.SetValue("buttons", "jump", (int)JoyButton.B);
        buttonSwap.SetValue("buttons", "toggle_fly", (int)JoyButton.A);
        Dictionary<string, JoyButton> resolvedButtons = GamepadBindings.ResolveSavedButtons(buttonSwap);
        TestAssert.Equal(JoyButton.B, resolvedButtons["jump"],
                "valid controller button swaps survive complete-set loading");
        TestAssert.Equal(JoyButton.A, resolvedButtons["toggle_fly"],
                "controller swaps are not rejected by old default assignments");

        ConfigFile duplicateButtons = new();
        duplicateButtons.SetValue("buttons", "jump", (int)JoyButton.X);
        duplicateButtons.SetValue("buttons", "sprint", (int)JoyButton.X);
        Dictionary<string, JoyButton> safeButtons = GamepadBindings.ResolveSavedButtons(duplicateButtons);
        TestAssert.Check(safeButtons["jump"] != JoyButton.X && safeButtons["sprint"] != JoyButton.X
                        && safeButtons["jump"] != safeButtons["sprint"],
                "duplicate persisted controller buttons restore safe defaults");

        foreach ((string action, string label) in GamepadBindings.ControllerOnlyBindable)
        {
            TestAssert.Check(!string.IsNullOrWhiteSpace(label),
                    $"controller-only action {action} has a readable label");
            TestAssert.Check(GamepadBindings.LabelFor(action) != "—",
                    $"controller-only action {action} has a configured binding");
        }

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
        ConfigFile malformedKeys = new();
        malformedKeys.SetValue("keys", "jump", "not-a-key");
        TestAssert.Check(!Keybinds.TryReadKey(malformedKeys.GetValue("keys", "jump"), out _),
                "wrong-typed keyboard binding variants are rejected");
        malformedKeys.SetValue("keys", "jump", (int)Key.F10);
        TestAssert.Check(Keybinds.TryReadKey(malformedKeys.GetValue("keys", "jump"), out Key validKey)
                        && validKey == Key.F10,
                "valid keyboard binding variants are round-tripped");
        TestAssert.Check(Keybinds.SaveConfig(new ConfigFile(),
                        "user://w09-missing-keyboard-dir/keybinds.cfg") != Error.Ok,
                "keyboard binding persistence failures are observable");
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
        TestAssert.Equal(Input.MouseModeEnum.Captured,
                PhotoMode.MouseModeAfterExit(sceneExit: false, Input.MouseModeEnum.Captured),
                "leaving photo mode restores captured gameplay mouse ownership");
        TestAssert.Equal(Input.MouseModeEnum.Visible,
                PhotoMode.MouseModeAfterExit(sceneExit: true, Input.MouseModeEnum.Captured),
                "scene teardown hands mouse ownership back to menu UI");

        for (int i = 0; i <= (int)Biome.Meadow; i++)
            TestAssert.Check(Hud.BiomeName((Biome)i) != "?", $"F3 HUD resolves biome id {i}");
        TestAssert.Equal("?", Hud.BiomeName((Biome)255), "unknown biome ids keep a clear fallback");

        BlockId[] appendedFoliage = [BlockId.SpruceLeaves, BlockId.JungleLeaves,
                BlockId.SwampLeaves, BlockId.RiverReeds, BlockId.Cactus, BlockId.Torch,
                BlockId.Wheat0, BlockId.Wheat1, BlockId.Wheat2];
        foreach (BlockId id in appendedFoliage)
        {
            TestAssert.Equal("dig_leaves", Sound.FamilyFor(id), $"{id} uses foliage break sounds");
            TestAssert.Equal("place_leaves", Sound.PlaceFamilyFor(id), $"{id} uses foliage placement sounds");
            TestAssert.Equal(1.45f, Sound.PitchFor(id), $"{id} uses the foliage pitch");
        }

        BlockId[] appendedWood = [BlockId.PlanksSlab, BlockId.PlanksStairs];
        foreach (BlockId id in appendedWood)
        {
            TestAssert.Equal("dig_wood", Sound.FamilyFor(id), $"{id} uses wood break sounds");
            TestAssert.Equal("place_wood", Sound.PlaceFamilyFor(id), $"{id} uses wood placement sounds");
            TestAssert.Equal(1.0f, Sound.PitchFor(id), $"{id} uses the wood pitch");
        }

        BlockId[] appendedStone = [BlockId.CobblestoneSlab, BlockId.BrickSlab,
                BlockId.CobblestoneStairs, BlockId.BrickStairs, BlockId.MossyCobblestone,
                BlockId.WeatheredCobblestone, BlockId.CrackedBrick, BlockId.MossyBrick,
                BlockId.EnchantingTable, BlockId.Furnace];
        foreach (BlockId id in appendedStone)
        {
            TestAssert.Equal("dig_stone", Sound.FamilyFor(id), $"{id} uses stone break sounds");
            TestAssert.Equal("place_stone", Sound.PlaceFamilyFor(id), $"{id} uses stone placement sounds");
            TestAssert.Equal(0.75f, Sound.PitchFor(id), $"{id} uses the stone pitch");
        }
        TestAssert.Equal("dig_sand", Sound.FamilyFor(BlockId.Ash), "ash uses the sand break sound family");
        TestAssert.Equal("place_sand", Sound.PlaceFamilyFor(BlockId.Ash), "ash uses the sand placement sound family");
        TestAssert.Equal(1.25f, Sound.PitchFor(BlockId.Ash), "ash uses the sand pitch");

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
        int ambientBus = AudioServer.GetBusIndex("Ambient");
        int initialSfxEffects = AudioServer.GetBusEffectCount(sfxBus);
        int initialAmbientEffects = AudioServer.GetBusEffectCount(ambientBus);
        AudioEffectReverb unrelatedSfxEffect = new();
        AudioEffectReverb unrelatedAmbientEffect = new();
        AudioEffectLowPassFilter ownedSfxEffect = new();
        AudioEffectLowPassFilter ownedAmbientEffect = new();
        AudioServer.AddBusEffect(sfxBus, unrelatedSfxEffect, initialSfxEffects);
        AudioServer.AddBusEffect(sfxBus, ownedSfxEffect, initialSfxEffects + 1);
        AudioServer.AddBusEffect(ambientBus, unrelatedAmbientEffect, initialAmbientEffects);
        AudioServer.AddBusEffect(ambientBus, ownedAmbientEffect, initialAmbientEffects + 1);
        TestAssert.Equal(initialSfxEffects + 1, Sound.FindOwnedBusEffect(sfxBus, ownedSfxEffect),
                "SFX underwater filter is found by identity, not fixed slot");
        TestAssert.Equal(initialAmbientEffects + 1, Sound.FindOwnedBusEffect(ambientBus, ownedAmbientEffect),
                "ambient underwater filter is found by identity, not fixed slot");
        Sound.RemoveOwnedBusEffect(sfxBus, ownedSfxEffect);
        Sound.RemoveOwnedBusEffect(ambientBus, ownedAmbientEffect);
        TestAssert.Equal(initialSfxEffects, Sound.FindOwnedBusEffect(sfxBus, unrelatedSfxEffect),
                "SFX cleanup preserves and reindexes unrelated bus effects");
        TestAssert.Equal(initialAmbientEffects, Sound.FindOwnedBusEffect(ambientBus, unrelatedAmbientEffect),
                "ambient cleanup preserves and reindexes unrelated bus effects");
        Sound.RemoveOwnedBusEffect(sfxBus, unrelatedSfxEffect);
        Sound.RemoveOwnedBusEffect(ambientBus, unrelatedAmbientEffect);
        TestAssert.Equal(initialSfxEffects, AudioServer.GetBusEffectCount(sfxBus),
                "SFX cleanup restores the original bus effect count");
        TestAssert.Equal(initialAmbientEffects, AudioServer.GetBusEffectCount(ambientBus),
                "ambient cleanup restores the original bus effect count");

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
