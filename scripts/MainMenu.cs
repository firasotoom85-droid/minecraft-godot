using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Main menu built as a responsive navigation rail and a spacious content
/// stage over the live voxel-world backdrop.
/// </summary>
public partial class MainMenu : Control
{
    private VBoxContainer _buttons = null!;
    private PanelContainer _heroPanel = null!;
    private PanelContainer _worldsPanel = null!;
    private PanelContainer _newWorldPanel = null!;
    private PanelContainer _settingsPanel = null!;
    private PanelContainer _managePanel = null!;
    private VBoxContainer _worldList = null!;
    private LineEdit _nameEdit = null!;
    private LineEdit _seedEdit = null!;
    private LineEdit _renameEdit = null!;
    private Label _manageTitle = null!;
    private Label _manageStatus = null!;
    private Label _pageTitle = null!;
    private ConfirmationDialog _deleteDialog = null!;
    private string _manageDir = "";
    private OptionButton _typeOption = null!;

    public override void _Ready()
    {
        MenuBackdrop backdrop = new()
        {
            Name = "MenuBackdrop",
        };
        AddChild(backdrop);

        ColorRect shade = new()
        {
            Color = new Color(0.012f, 0.025f, 0.032f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);

        GradientTexture2D vignetteTexture = new()
        {
            Width = 2,
            Height = 256,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(0, 1),
            Gradient = new Gradient
            {
                Colors =
                [
                    new Color(0, 0, 0, 0.62f),
                    new Color(0, 0, 0, 0.12f),
                    new Color(0, 0, 0, 0.56f),
                ],
                Offsets = [0.0f, 0.52f, 1.0f],
            },
        };
        TextureRect vignette = new()
        {
            Texture = vignetteTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(vignette);

        MarginContainer shell = new();
        shell.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shell.AddThemeConstantOverride("margin_left", 42);
        shell.AddThemeConstantOverride("margin_right", 42);
        shell.AddThemeConstantOverride("margin_top", 34);
        shell.AddThemeConstantOverride("margin_bottom", 30);
        AddChild(shell);

        HBoxContainer layout = new();
        layout.AddThemeConstantOverride("separation", 34);
        shell.AddChild(layout);

        BuildNavigation(layout);
        BuildContent(layout);
        ShowMain();
    }

    private void BuildNavigation(HBoxContainer layout)
    {
        VBoxContainer rail = new()
        {
            CustomMinimumSize = new Vector2(286, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        rail.AddThemeConstantOverride("separation", 12);
        layout.AddChild(rail);

        PanelContainer brandMark = new();
        brandMark.AddThemeStyleboxOverride("panel", MenuSkin.PanelStyle(true));
        HBoxContainer brandRow = new();
        brandRow.AddThemeConstantOverride("separation", 12);
        Label mark = new()
        {
            Text = "M",
            CustomMinimumSize = new Vector2(48, 48),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.AddThemeFontSizeOverride("font_size", 30);
        mark.AddThemeColorOverride("font_color", MenuSkin.AccentBright);
        PanelContainer iconTile = new();
        iconTile.AddThemeStyleboxOverride("panel", MenuSkin.ButtonStyle(
                new Color(0.10f, 0.31f, 0.27f), MenuSkin.Accent.WithAlpha(0.70f)));
        iconTile.AddChild(mark);
        brandRow.AddChild(iconTile);

        VBoxContainer brandText = new();
        brandText.AddThemeConstantOverride("separation", 1);
        brandText.AddChild(MenuSkin.Title("MINECRAFT", 19, true));
        brandText.AddChild(MenuSkin.MutedLabel("GODOT EDITION", 10));
        brandRow.AddChild(brandText);
        brandMark.AddChild(brandRow);
        rail.AddChild(brandMark);

        rail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });
        rail.AddChild(MenuSkin.Eyebrow("Your next world starts here"));
        Label headline = MenuSkin.Title("BUILD YOUR\nOWN WORLD.", 34, true);
        headline.AddThemeConstantOverride("line_spacing", -4);
        rail.AddChild(headline);
        Label intro = MenuSkin.MutedLabel(
                "Explore wild terrain, shape the landscape, and make a place worth coming back to.",
                14);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rail.AddChild(intro);

        rail.AddChild(new HSeparator());
        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 9);
        _buttons.AddChild(MenuSkin.MakeButton("EXPLORE WORLDS", ShowWorlds, 0, primary: true));
        _buttons.AddChild(MenuSkin.MakeButton("CREATE NEW WORLD", ShowNewWorld, 0));
        _buttons.AddChild(MenuSkin.MakeButton("SETTINGS", ShowSettings, 0));
        _buttons.AddChild(MenuSkin.MakeButton("QUIT GAME", () => GetTree().Quit(), 0));
        rail.AddChild(_buttons);

        Control spacer = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        rail.AddChild(spacer);

        PanelContainer footer = new();
        footer.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
        VBoxContainer footerContent = new();
        footerContent.AddThemeConstantOverride("separation", 4);
        footerContent.AddChild(MenuSkin.Eyebrow("WORLD SYSTEMS"));
        footerContent.AddChild(MenuSkin.MutedLabel("Procedural terrain  ·  Survival  ·  Creative", 11));
        footerContent.AddChild(MenuSkin.MutedLabel("GODOT / VOXEL SANDBOX", 10));
        footer.AddChild(footerContent);
        rail.AddChild(footer);
    }

    private void BuildContent(HBoxContainer layout)
    {
        VBoxContainer stage = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        stage.AddThemeConstantOverride("separation", 14);
        layout.AddChild(stage);

        HBoxContainer topBar = new();
        topBar.AddThemeConstantOverride("separation", 12);
        _pageTitle = MenuSkin.Eyebrow("MAIN MENU");
        _pageTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        topBar.AddChild(_pageTitle);
        Label buildTag = new()
        {
            Text = "VOXEL SURVIVAL / 01",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buildTag.AddThemeFontSizeOverride("font_size", 10);
        buildTag.AddThemeColorOverride("font_color", MenuSkin.Muted);
        topBar.AddChild(buildTag);
        stage.AddChild(topBar);
        stage.AddChild(new HSeparator());

        _heroPanel = BuildHero();
        _worldsPanel = BuildWorlds();
        _newWorldPanel = BuildNewWorld();
        _settingsPanel = BuildSettings();
        _managePanel = BuildManage();

        stage.AddChild(_heroPanel);
        stage.AddChild(_worldsPanel);
        stage.AddChild(_newWorldPanel);
        stage.AddChild(_settingsPanel);
        stage.AddChild(_managePanel);
    }

    private PanelContainer BuildHero()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 18);
        box.AddChild(MenuSkin.Eyebrow("A LIVING, BREATHING SANDBOX"));

        Label title = MenuSkin.Title("Every block has\na story.", 46, true);
        title.AddThemeConstantOverride("line_spacing", -5);
        box.AddChild(title);

        Label description = MenuSkin.MutedLabel(
                "Start with a quiet valley. Follow the rivers, dig below the surface, build a shelter, and turn an untouched seed into your own world.",
                16);
        description.CustomMinimumSize = new Vector2(0, 62);
        box.AddChild(description);

        HBoxContainer features = new();
        features.AddThemeConstantOverride("separation", 10);
        features.AddChild(FeatureCard("01", "EXPLORE", "Biomes, caves and distant horizons"));
        features.AddChild(FeatureCard("02", "CREATE", "Build, craft and shape the land"));
        features.AddChild(FeatureCard("03", "SURVIVE", "Make it through the changing nights"));
        box.AddChild(features);

        Control spacer = new()
        {
            CustomMinimumSize = new Vector2(0, 10),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        box.AddChild(spacer);
        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(MenuSkin.MakeButton("CHOOSE A WORLD", ShowWorlds, 210, primary: true));
        actions.AddChild(MenuSkin.MakeButton("CREATE WORLD", ShowNewWorld, 170));
        box.AddChild(actions);

        PanelContainer panel = MenuSkin.Wrap(box);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return panel;
    }

    private static PanelContainer FeatureCard(string number, string title, string detail)
    {
        PanelContainer panel = new();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 5);
        Label index = new()
        {
            Text = number,
        };
        index.AddThemeFontSizeOverride("font_size", 11);
        index.AddThemeColorOverride("font_color", MenuSkin.AccentBright);
        content.AddChild(index);
        content.AddChild(MenuSkin.Title(title, 14, true));
        content.AddChild(MenuSkin.MutedLabel(detail, 11));
        panel.AddChild(content);
        return panel;
    }

    private PanelContainer BuildWorlds()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 14);
        box.AddChild(MenuSkin.Eyebrow("YOUR SAVED WORLDS"));
        box.AddChild(MenuSkin.Title("Choose where to continue.", 28, true));

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(0, 420),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _worldList = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _worldList.AddThemeConstantOverride("separation", 9);
        scroll.AddChild(_worldList);
        box.AddChild(scroll);

        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(MenuSkin.MakeButton("NEW WORLD", ShowNewWorld, 180, primary: true));
        actions.AddChild(MenuSkin.MakeButton("BACK", ShowMain, 120));
        box.AddChild(actions);

        PanelContainer panel = MenuSkin.Wrap(box);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return panel;
    }

    private PanelContainer BuildNewWorld()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 14);
        box.AddChild(MenuSkin.Eyebrow("CREATE A NEW SAVE"));
        box.AddChild(MenuSkin.Title("Give your world a name.", 28, true));

        _nameEdit = new LineEdit
        {
            PlaceholderText = "World name",
            Text = "New World",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        MenuSkin.StyleInput(_nameEdit);
        box.AddChild(Field("WORLD NAME", _nameEdit));

        _seedEdit = new LineEdit
        {
            PlaceholderText = "Optional seed or share code (e.g. 1337 · amplified)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        MenuSkin.StyleInput(_seedEdit);
        box.AddChild(Field("WORLD SEED / SHARE CODE", _seedEdit));

        _typeOption = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _typeOption.AddItem("Normal world", 0);
        _typeOption.AddItem("Flat world", 1);
        _typeOption.AddItem("Amplified mountains", 2);
        _typeOption.AddItem("Sky islands", 3);
        MenuSkin.StyleInput(_typeOption);
        box.AddChild(Field("WORLD GENERATION", _typeOption));

        PanelContainer note = new();
        note.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
        note.AddChild(MenuSkin.MutedLabel(
                "A blank seed creates a random world. You can share a world by copying its seed and generation type.",
                12));
        box.AddChild(note);

        Control spacer = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(spacer);
        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(MenuSkin.MakeButton("CREATE WORLD", CreateWorld, 190, primary: true));
        actions.AddChild(MenuSkin.MakeButton("BACK", ShowMain, 120));
        box.AddChild(actions);

        PanelContainer panel = MenuSkin.Wrap(box);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return panel;
    }

    private static VBoxContainer Field(string label, Control input)
    {
        VBoxContainer field = new();
        field.AddThemeConstantOverride("separation", 6);
        field.AddChild(MenuSkin.Eyebrow(label));
        field.AddChild(input);
        return field;
    }

    private PanelContainer BuildSettings()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 12);
        box.AddChild(MenuSkin.Eyebrow("MAKE IT YOURS"));
        box.AddChild(MenuSkin.Title("Settings", 28, true));

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(0, 470),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(SettingsPanel.Build(Sound.ApplyVolumes));
        box.AddChild(scroll);
        box.AddChild(MenuSkin.MakeButton("BACK", ShowMain, 120));

        PanelContainer panel = MenuSkin.Wrap(box);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return panel;
    }

    private PanelContainer BuildManage()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 14);
        box.AddChild(MenuSkin.Eyebrow("WORLD MANAGEMENT"));
        _manageTitle = MenuSkin.Title("Manage world", 28, true);
        box.AddChild(_manageTitle);

        _renameEdit = new LineEdit
        {
            PlaceholderText = "World name",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        MenuSkin.StyleInput(_renameEdit);
        box.AddChild(Field("RENAME WORLD", _renameEdit));

        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 10);
        actions.AddChild(MenuSkin.MakeButton("RENAME", RenameWorld, 130, primary: true));
        actions.AddChild(MenuSkin.MakeButton("DUPLICATE", DuplicateWorld, 150));
        actions.AddChild(MenuSkin.MakeButton("DELETE", ConfirmDelete, 120, danger: true));
        box.AddChild(actions);

        _manageStatus = new Label();
        _manageStatus.AddThemeFontSizeOverride("font_size", 13);
        _manageStatus.AddThemeColorOverride("font_color", MenuSkin.AccentBright);
        box.AddChild(_manageStatus);
        Control spacer = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(spacer);
        box.AddChild(MenuSkin.MakeButton("BACK TO WORLDS", () => { RefreshWorlds(); ShowWorlds(); }, 190));

        _deleteDialog = new ConfirmationDialog
        {
            Title = "Delete world?",
            DialogText = "This permanently deletes the world and its save files.",
        };
        _deleteDialog.Confirmed += DeleteWorld;
        AddChild(_deleteDialog);
        _deleteDialog.AddThemeStyleboxOverride("panel", MenuSkin.PanelStyle(true));

        PanelContainer panel = MenuSkin.Wrap(box);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return panel;
    }

    private void RefreshWorlds()
    {
        foreach (Node child in _worldList.GetChildren())
        {
            child.QueueFree();
        }

        var worlds = WorldStore.List();
        if (worlds.Count == 0)
        {
            PanelContainer empty = new();
            empty.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());
            VBoxContainer content = new();
            content.AddThemeConstantOverride("separation", 6);
            content.AddChild(MenuSkin.Title("No worlds yet", 20, true));
            content.AddChild(MenuSkin.MutedLabel("Create a world to begin your first adventure.", 13));
            empty.AddChild(content);
            _worldList.AddChild(empty);
            return;
        }

        foreach ((string dir, WorldManifest manifest) in worlds)
        {
            string played = Time.GetDatetimeStringFromUnixTime(manifest.PlayedUtc, true);
            string preset = manifest.Preset is { Length: > 0 } pr ? pr : "normal";
            PanelContainer card = new();
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            card.AddThemeStyleboxOverride("panel", MenuSkin.InsetStyle());

            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 14);
            VBoxContainer details = new();
            details.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            details.AddThemeConstantOverride("separation", 4);
            details.AddChild(MenuSkin.Title(manifest.Name, 17, true));
            details.AddChild(MenuSkin.MutedLabel(
                    $"SEED {manifest.Seed}   ·   {preset.ToUpperInvariant()}   ·   GEN V{manifest.GenVersion}",
                    10));
            details.AddChild(MenuSkin.MutedLabel($"Last played  {played}", 11));
            row.AddChild(details);
            row.AddChild(MenuSkin.MakeButton("PLAY", () => StartWorld(dir), 104, primary: true));
            row.AddChild(MenuSkin.MakeButton("MANAGE", () => ShowManage(dir, manifest), 112));
            card.AddChild(row);
            _worldList.AddChild(card);
        }
    }

    private void ShowManage(string dir, WorldManifest manifest)
    {
        _manageDir = dir;
        _manageTitle.Text = manifest.Name;
        _renameEdit.Text = manifest.Name;
        _manageStatus.Text = "";
        SetPage("MANAGE WORLD", _managePanel);
    }

    private void RenameWorld()
    {
        if (WorldStore.Rename(_manageDir, _renameEdit.Text))
        {
            _manageStatus.Text = "World renamed.";
            _manageTitle.Text = _renameEdit.Text.Trim();
        }
        else
        {
            _manageStatus.Text = "Enter a valid world name.";
        }
    }

    private void DuplicateWorld()
    {
        string? copy = WorldStore.Duplicate(_manageDir);
        _manageStatus.Text = copy is null ? "Could not duplicate world." : "World copy created.";
    }

    private void ConfirmDelete()
    {
        _deleteDialog.DialogText = $"Delete “{_renameEdit.Text.Trim()}” and all of its saves?";
        _deleteDialog.PopupCentered();
    }

    private void DeleteWorld()
    {
        if (WorldStore.Delete(_manageDir))
        {
            RefreshWorlds();
            ShowWorlds();
        }
        else
        {
            _manageStatus.Text = "Could not delete world.";
        }
    }

    private static readonly string[] PresetKeys = ["normal", "flat", "amplified", "sky"];

    /// <summary>Seed field also accepts share codes: "1337·flat", "1337 amplified", "sky".</summary>
    private void CreateWorld()
    {
        string name = _nameEdit.Text.Trim();
        if (name.Length == 0)
        {
            name = "New World";
        }

        int? seed = null;
        string preset = PresetOf(_typeOption.Selected);
        foreach (string part in _seedEdit.Text.Trim().Split(['·', ' ', ',', ':', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int parsed))
            {
                seed = parsed;
            }
            else if (PresetFromName(part) is { } named)
            {
                preset = named;
            }
        }

        StartWorld(WorldStore.Create(name, seed, "survival", preset));
    }

    private static string PresetOf(int index) =>
        index >= 0 && index < PresetKeys.Length ? PresetKeys[index] : "normal";

    private static string? PresetFromName(string text) => text.ToLowerInvariant() switch
    {
        "normal" or "n" => "normal",
        "flat" or "f" => "flat",
        "amplified" or "amp" or "a" => "amplified",
        "sky" or "islands" or "skyislands" or "s" => "sky",
        _ => null,
    };

    private void StartWorld(string dir)
    {
        GameSession.Instance.SelectWorld(dir);
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    private void SetPage(string title, Control active)
    {
        _pageTitle.Text = title;
        _heroPanel.Visible = false;
        _worldsPanel.Visible = active == _worldsPanel;
        _newWorldPanel.Visible = active == _newWorldPanel;
        _settingsPanel.Visible = active == _settingsPanel;
        _managePanel.Visible = active == _managePanel;
    }

    private void ShowMain()
    {
        SetPage("MAIN MENU", _heroPanel);
    }

    private void ShowWorlds()
    {
        RefreshWorlds();
        SetPage("WORLD SELECT", _worldsPanel);
    }

    private void ShowNewWorld()
    {
        _seedEdit.Text = "";
        SetPage("CREATE WORLD", _newWorldPanel);
    }

    private void ShowSettings()
    {
        SetPage("SETTINGS", _settingsPanel);
    }
}
