using System;
using Godot;

namespace Minecraft;

/// <summary>Shared look for the menu surfaces, matching the in-game HUD style.</summary>
public static class MenuSkin
{
    public static PanelContainer Wrap(Control content, float width = 0)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        margin.AddChild(content);
        panel.AddChild(margin);
        if (width > 0)
        {
            panel.CustomMinimumSize = new Vector2(width, 0);
        }

        return panel;
    }

    public static StyleBoxFlat PanelStyle()
    {
        StyleBoxFlat sb = new()
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.94f),
            BorderColor = new Color(1, 1, 1, 0.14f),
        };
        sb.SetCornerRadiusAll(10);
        sb.SetBorderWidthAll(1);
        return sb;
    }

    public static Button MakeButton(string text, Action pressed, float width = 300)
    {
        Button b = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 42),
        };
        b.Pressed += pressed;
        return b;
    }

    public static Label Title(string text, int size = 20)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
