using System;
using Godot;

namespace Minecraft;

/// <summary>
/// The shared settings form (main menu + pause menu). Writes through to
/// Settings, persists on every change, and calls onApply so live systems
/// (audio buses, streaming distance) react immediately.
/// </summary>
public static class SettingsPanel
{
    public static Control Build(Action? onApply = null)
    {
        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 8);

        void Apply()
        {
            Settings.Save();
            onApply?.Invoke();
        }

        root.AddChild(Slider("Mouse sensitivity", 0.0005f, 0.006f, 0.0001f,
                Settings.MouseSensitivity, "0.0000",
                v =>
                {
                    Settings.MouseSensitivity = v;
                    Apply();
                }));
        root.AddChild(Check("Invert Y axis", Settings.InvertY,
                v =>
                {
                    Settings.InvertY = v;
                    Apply();
                }));
        root.AddChild(Slider("Field of view", 60, 110, 1,
                Settings.Fov, "0",
                v =>
                {
                    Settings.Fov = v;
                    Apply();
                }));

        root.AddChild(Header("AUDIO"));
        root.AddChild(Slider("Master volume", 0, 1, 0.05f,
                Settings.MasterVolume, "0.00",
                v =>
                {
                    Settings.MasterVolume = v;
                    Apply();
                }));
        root.AddChild(Slider("Music volume", 0, 1, 0.05f,
                Settings.MusicVolume, "0.00",
                v =>
                {
                    Settings.MusicVolume = v;
                    Apply();
                }));
        root.AddChild(Slider("Effects volume", 0, 1, 0.05f,
                Settings.SfxVolume, "0.00",
                v =>
                {
                    Settings.SfxVolume = v;
                    Apply();
                }));
        root.AddChild(Slider("Ambience volume", 0, 1, 0.05f,
                Settings.AmbientVolume, "0.00",
                v =>
                {
                    Settings.AmbientVolume = v;
                    Apply();
                }));

        root.AddChild(Header("VIDEO"));
        root.AddChild(Slider("View distance", 2, 24, 1,
                Settings.ViewDistance, "0",
                v =>
                {
                    Settings.ViewDistance = (int)v;
                    Apply();
                }));
        root.AddChild(Check("Auto quality (adapts to fps)", Settings.AutoQuality,
                v =>
                {
                    Settings.AutoQuality = v;
                    Apply();
                }));
        root.AddChild(Check("Head bob", Settings.HeadBob,
                v =>
                {
                    Settings.HeadBob = v;
                    Apply();
                }));

        root.AddChild(Header("GAMEPLAY"));
        root.AddChild(Check("Show hints", Settings.ShowHints,
                v =>
                {
                    Settings.ShowHints = v;
                    Apply();
                }));
        root.AddChild(Option("Difficulty", ["Peaceful", "Normal", "Hard"], Settings.Difficulty,
                i =>
                {
                    Settings.Difficulty = i;
                    Apply();
                }));
        return root;
    }

    private static HBoxContainer Slider(
            string text, float min, float max, float step, float value, string format, Action<float> setter)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 12);
        Label label = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(170, 0),
        };
        HSlider slider = new()
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(240, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Label readout = new()
        {
            Text = value.ToString(format),
            CustomMinimumSize = new Vector2(52, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        slider.ValueChanged += v =>
        {
            setter((float)v);
            readout.Text = ((float)v).ToString(format);
        };
        row.AddChild(label);
        row.AddChild(slider);
        row.AddChild(readout);
        return row;
    }

    private static HBoxContainer Check(string text, bool value, Action<bool> setter)
    {
        HBoxContainer row = new();
        Label label = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(170, 0),
        };
        CheckButton check = new()
        {
            ButtonPressed = value,
        };
        check.Toggled += v => setter(v);
        row.AddChild(label);
        row.AddChild(check);
        return row;
    }

    private static HBoxContainer Option(string text, string[] items, int value, Action<int> setter)
    {
        HBoxContainer row = new();
        Label label = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(170, 0),
        };
        OptionButton option = new()
        {
            CustomMinimumSize = new Vector2(160, 0),
        };
        foreach (string item in items)
        {
            option.AddItem(item);
        }

        option.Selected = Mathf.Clamp(value, 0, items.Length - 1);
        option.ItemSelected += i => setter((int)i);
        row.AddChild(label);
        row.AddChild(option);
        return row;
    }

    private static Label Header(string text)
    {
        Label label = new()
        {
            Text = text,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.9f, 0.8f));
        return label;
    }
}
