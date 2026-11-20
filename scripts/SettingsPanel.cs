using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Shared settings for the main menu and pause menu. Controls are grouped into
/// clear cards, scale horizontally and apply changes immediately.
/// </summary>
public static class SettingsPanel
{
    public static Control Build(Action? onApply = null)
    {
        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 12);

        void Apply()
        {
            Settings.Save();
            onApply?.Invoke();
        }

        root.AddChild(Group("CONTROLS",
        [
            Slider("Mouse sensitivity", 0.0005f, 0.006f, 0.0001f,
                    Settings.MouseSensitivity, "0.0000",
                    v => { Settings.MouseSensitivity = v; Apply(); }),
            Check("Invert Y axis", Settings.InvertY,
                    v => { Settings.InvertY = v; Apply(); }),
            Slider("Field of view", 60, 110, 1,
                    Settings.Fov, "0",
                    v => { Settings.Fov = v; Apply(); }),
        ]));

        root.AddChild(Group("AUDIO",
        [
            Slider("Master volume", 0, 1, 0.05f,
                    Settings.MasterVolume, "0%",
                    v => { Settings.MasterVolume = v; Apply(); }, percent: true),
            Slider("Music volume", 0, 1, 0.05f,
                    Settings.MusicVolume, "0%",
                    v => { Settings.MusicVolume = v; Apply(); }, percent: true),
            Slider("Effects volume", 0, 1, 0.05f,
                    Settings.SfxVolume, "0%",
                    v => { Settings.SfxVolume = v; Apply(); }, percent: true),
            Slider("Ambience volume", 0, 1, 0.05f,
                    Settings.AmbientVolume, "0%",
                    v => { Settings.AmbientVolume = v; Apply(); }, percent: true),
        ]));

        root.AddChild(Group("VIDEO",
        [
            Slider("View distance", 2, 24, 1,
                    Settings.ViewDistance, "0",
                    v => { Settings.ViewDistance = (int)v; Apply(); }),
            Check("Auto quality (adapts to FPS)", Settings.AutoQuality,
                    v => { Settings.AutoQuality = v; Apply(); }),
            Check("Head bob", Settings.HeadBob,
                    v => { Settings.HeadBob = v; Apply(); }),
        ]));

        root.AddChild(Group("GAMEPLAY",
        [
            Check("Show contextual hints", Settings.ShowHints,
                    v => { Settings.ShowHints = v; Apply(); }),
            Option("Difficulty", ["Peaceful", "Normal", "Hard"], Settings.Difficulty,
                    i => { Settings.Difficulty = i; Apply(); }),
        ]));

        return root;
    }

    private static PanelContainer Group(string title, Control[] rows)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 8);
        Label heading = MenuSkin.Eyebrow(title);
        content.AddChild(heading);
        content.AddChild(new HSeparator());
        foreach (Control row in rows)
        {
            content.AddChild(row);
        }

        panel.AddChild(content);
        return panel;
    }

    private static HBoxContainer Slider(
            string text, float min, float max, float step, float value,
            string format, Action<float> setter, bool percent = false)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        row.CustomMinimumSize = new Vector2(0, 34);

        Label label = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(150, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 12);

        HSlider slider = new()
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(100, 22),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All,
        };
        slider.AddThemeColorOverride("font_color", MenuSkin.Text);

        Label readout = new()
        {
            Text = percent ? $"{Mathf.RoundToInt(value * 100.0f)}%" : value.ToString(format),
            CustomMinimumSize = new Vector2(56, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        readout.AddThemeFontSizeOverride("font_size", 12);
        readout.AddThemeColorOverride("font_color", MenuSkin.AccentBright);

        slider.ValueChanged += changed =>
        {
            float next = (float)changed;
            setter(next);
            readout.Text = percent ? $"{Mathf.RoundToInt(next * 100.0f)}%" : next.ToString(format);
        };
        row.AddChild(label);
        row.AddChild(slider);
        row.AddChild(readout);
        return row;
    }

    private static HBoxContainer Check(string text, bool value, Action<bool> setter)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        row.CustomMinimumSize = new Vector2(0, 34);

        Label label = new()
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 12);

        CheckButton check = new()
        {
            ButtonPressed = value,
            FocusMode = Control.FocusModeEnum.All,
        };
        check.Toggled += setter;
        row.AddChild(label);
        row.AddChild(check);
        return row;
    }

    private static HBoxContainer Option(string text, string[] items, int value, Action<int> setter)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        row.CustomMinimumSize = new Vector2(0, 42);

        Label label = new()
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 12);

        OptionButton option = new()
        {
            CustomMinimumSize = new Vector2(170, 42),
            FocusMode = Control.FocusModeEnum.All,
        };
        foreach (string item in items)
        {
            option.AddItem(item);
        }

        option.Selected = Mathf.Clamp(value, 0, items.Length - 1);
        MenuSkin.StyleInput(option);
        option.ItemSelected += index => setter((int)index);
        row.AddChild(label);
        row.AddChild(option);
        return row;
    }
}
