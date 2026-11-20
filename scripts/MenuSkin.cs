using System;
using Godot;

namespace Minecraft;

/// <summary>Shared pixel-art presentation for every menu and inventory surface.</summary>
public static class MenuSkin
{
    private static readonly Color Ink = new(0.055f, 0.075f, 0.085f, 0.97f);
    private static readonly Color Edge = new(0.31f, 0.39f, 0.34f, 1.0f);
    private static readonly Color Highlight = new(0.82f, 0.76f, 0.53f, 1.0f);
    private static readonly Color Text = new(0.91f, 0.91f, 0.82f, 1.0f);

    public static PanelContainer Wrap(Control content, float width = 0)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        margin.AddChild(content);
        panel.AddChild(margin);
        if (width > 0) panel.CustomMinimumSize = new Vector2(width, 0);
        return panel;
    }

    public static StyleBoxFlat PanelStyle()
    {
        StyleBoxFlat style = new()
        {
            BgColor = Ink,
            BorderColor = Edge,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
            ShadowColor = new Color(0, 0, 0, 0.42f),
            ShadowSize = 8,
            ShadowOffset = new Vector2(0, 3),
        };
        style.SetCornerRadiusAll(0);
        style.SetBorderWidthAll(2);
        return style;
    }

    public static StyleBoxFlat SlotStyle(bool selected = false, bool hovered = false)
    {
        StyleBoxFlat style = new()
        {
            BgColor = selected ? new Color(0.29f, 0.31f, 0.22f, 1) : hovered ? new Color(0.22f, 0.25f, 0.20f, 1) : new Color(0.075f, 0.09f, 0.085f, 1),
            BorderColor = selected ? Highlight : hovered ? new Color(0.66f, 0.69f, 0.52f, 1) : new Color(0.30f, 0.36f, 0.31f, 1),
        };
        style.SetCornerRadiusAll(0);
        style.SetBorderWidthAll(selected ? 3 : 2);
        return style;
    }

    public static Button MakeButton(string text, Action pressed, float width = 300)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 42),
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Highlight);
        button.AddThemeColorOverride("font_focus_color", Highlight);
        button.AddThemeFontSizeOverride("font_size", 15);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.12f, 0.16f, 0.13f, 1), Edge));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.22f, 0.27f, 0.19f, 1), Highlight));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.07f, 0.10f, 0.08f, 1), Highlight));
        button.AddThemeStyleboxOverride("focus", ButtonStyle(new Color(0.12f, 0.16f, 0.13f, 1), Highlight));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(new Color(0.08f, 0.09f, 0.08f, 1), new Color(0.2f, 0.23f, 0.2f, 1)));
        button.Pressed += pressed;
        return button;
    }

    private static StyleBoxFlat ButtonStyle(Color background, Color border)
    {
        StyleBoxFlat style = new() { BgColor = background, BorderColor = border };
        style.SetCornerRadiusAll(0);
        style.SetBorderWidthAll(2);
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 7;
        style.ContentMarginBottom = 7;
        return style;
    }

    public static Label Title(string text, int size = 20)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            FocusMode = Control.FocusModeEnum.None,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Highlight);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 2);
        return label;
    }
}
