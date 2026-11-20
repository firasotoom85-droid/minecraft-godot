using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Shared visual language for menus and in-game panels: deep slate surfaces,
/// restrained teal accents, warm highlights and high-contrast readable text.
/// </summary>
public static class MenuSkin
{
    public static readonly Color Background = new(0.035f, 0.050f, 0.065f);
    public static readonly Color Surface = new(0.055f, 0.075f, 0.090f, 0.96f);
    public static readonly Color SurfaceRaised = new(0.085f, 0.115f, 0.125f, 0.98f);
    public static readonly Color SurfaceInset = new(0.025f, 0.040f, 0.050f, 0.92f);
    public static readonly Color Border = new(0.42f, 0.58f, 0.57f, 0.30f);
    public static readonly Color Text = new(0.91f, 0.95f, 0.93f);
    public static readonly Color Muted = new(0.59f, 0.69f, 0.69f);
    public static readonly Color Accent = new(0.22f, 0.78f, 0.66f);
    public static readonly Color AccentBright = new(0.38f, 0.94f, 0.78f);
    public static readonly Color Warm = new(0.94f, 0.72f, 0.38f);
    public static readonly Color Danger = new(0.90f, 0.34f, 0.31f);

    public static PanelContainer Wrap(Control content, float width = 0)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        margin.AddChild(content);
        panel.AddChild(margin);
        if (width > 0)
        {
            panel.CustomMinimumSize = new Vector2(width, 0);
        }

        return panel;
    }

    public static StyleBoxFlat PanelStyle(bool raised = false)
    {
        StyleBoxFlat sb = new()
        {
            BgColor = raised ? SurfaceRaised : Surface,
            BorderColor = Border,
            ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.30f),
            ShadowSize = 12,
            ShadowOffset = new Vector2(0, 5),
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        sb.SetCornerRadiusAll(12);
        sb.SetBorderWidthAll(1);
        return sb;
    }

    public static StyleBoxFlat InsetStyle()
    {
        StyleBoxFlat sb = new()
        {
            BgColor = SurfaceInset,
            BorderColor = new Color(Border.R, Border.G, Border.B, 0.22f),
        };
        sb.SetCornerRadiusAll(8);
        sb.SetBorderWidthAll(1);
        return sb;
    }

    public static StyleBoxFlat ButtonStyle(Color fill, Color stroke, int radius = 9)
    {
        StyleBoxFlat sb = new()
        {
            BgColor = fill,
            BorderColor = stroke,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        sb.SetCornerRadiusAll(radius);
        sb.SetBorderWidthAll(1);
        return sb;
    }

    public static Button MakeButton(
            string text, Action pressed, float width = 300, bool primary = false, bool danger = false)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 46),
            Alignment = HorizontalAlignment.Center,
            FocusMode = Control.FocusModeEnum.All,
        };
        if (width <= 0)
        {
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        Color fill = danger
                ? new Color(0.27f, 0.095f, 0.09f, 0.98f)
                : primary
                        ? new Color(0.10f, 0.34f, 0.30f, 0.98f)
                        : SurfaceRaised;
        Color hover = danger
                ? new Color(0.39f, 0.12f, 0.11f, 1.0f)
                : primary
                        ? new Color(0.14f, 0.46f, 0.39f, 1.0f)
                        : new Color(0.13f, 0.19f, 0.19f, 1.0f);
        Color stroke = danger ? Danger.WithAlpha(0.55f) : primary ? Accent.WithAlpha(0.72f) : Border;
        button.AddThemeStyleboxOverride("normal", ButtonStyle(fill, stroke));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(hover, primary ? AccentBright : stroke));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(
                primary ? new Color(0.07f, 0.25f, 0.22f) : new Color(0.045f, 0.09f, 0.09f),
                primary ? AccentBright : stroke));
        button.AddThemeStyleboxOverride("focus", ButtonStyle(fill, AccentBright));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(SurfaceInset, Border.WithAlpha(0.12f)));
        button.AddThemeColorOverride("font_color", danger ? new Color(1.0f, 0.84f, 0.82f) : Text);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", Muted.WithAlpha(0.4f));
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += pressed;
        return button;
    }

    public static Label Title(string text, int size = 20, bool leftAligned = false)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = leftAligned ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Text);
        return label;
    }

    public static Label Eyebrow(string text)
    {
        Label label = new()
        {
            Text = text.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", AccentBright);
        return label;
    }

    public static Label MutedLabel(string text, int size = 13)
    {
        Label label = new()
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Muted);
        return label;
    }

    public static void StyleInput(Control control)
    {
        StyleBoxFlat normal = InsetStyle();
        StyleBoxFlat focus = ButtonStyle(SurfaceInset, Accent.WithAlpha(0.85f));
        if (control is LineEdit line)
        {
            line.AddThemeStyleboxOverride("normal", normal);
            line.AddThemeStyleboxOverride("focus", focus);
            line.AddThemeStyleboxOverride("read_only", normal);
            line.AddThemeColorOverride("font_color", Text);
            line.AddThemeColorOverride("font_placeholder_color", Muted.WithAlpha(0.72f));
            line.AddThemeColorOverride("caret_color", AccentBright);
            line.AddThemeFontSizeOverride("font_size", 14);
            line.CustomMinimumSize = new Vector2(0, 44);
        }
        else if (control is OptionButton option)
        {
            option.AddThemeStyleboxOverride("normal", normal);
            option.AddThemeStyleboxOverride("hover", focus);
            option.AddThemeStyleboxOverride("pressed", focus);
            option.AddThemeStyleboxOverride("focus", focus);
            option.AddThemeColorOverride("font_color", Text);
            option.AddThemeColorOverride("font_hover_color", Colors.White);
            option.AddThemeFontSizeOverride("font_size", 14);
            option.CustomMinimumSize = new Vector2(0, 44);
        }
    }
}
