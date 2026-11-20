using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Main menu: Play (world select / create), Settings, Quit. Built in code,
/// matching the in-game HUD's hand-rolled style.
/// </summary>
public partial class MainMenu : Control
{
    private VBoxContainer _buttons = null!;
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

        ColorRect bg = new()
        {
            Color = new Color(0.02f, 0.04f, 0.07f, 0.26f),
        };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        GradientTexture2D vignetteTex = new()
        {
            Width = 2,
            Height = 256,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(0, 1),
            Gradient = new Gradient
            {
                Colors =
                [
                    new Color(0, 0, 0, 0.58f),
                    new Color(0, 0, 0, 0.02f),
                    new Color(0, 0, 0, 0.42f),
                ],
                Offsets = [0.0f, 0.52f, 1.0f],
            },
        };
        TextureRect vignette = new()
        {
            Texture = vignetteTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(vignette);

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 14);
        center.AddChild(stack);

        Label title = new()
        {
            Text = "MINECRAFT",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1.0f));
        title.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        title.AddThemeConstantOverride("outline_size", 10);
        stack.AddChild(title);

        Label subtitle = new()
        {
            Text = "godot edition",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        subtitle.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.9f, 0.7f));
        stack.AddChild(subtitle);

        stack.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 10);
        _buttons.AddChild(MenuSkin.MakeButton("Play", ShowWorlds));
        _buttons.AddChild(MenuSkin.MakeButton("Settings", ShowSettings));
        _buttons.AddChild(MenuSkin.MakeButton("Quit", () => GetTree().Quit()));
        stack.AddChild(_buttons);

        _worldsPanel = BuildWorlds();
        _newWorldPanel = BuildNewWorld();
        _settingsPanel = BuildSettings();
        _managePanel = BuildManage();
        stack.AddChild(_worldsPanel);
        stack.AddChild(_newWorldPanel);
        stack.AddChild(_settingsPanel);
        stack.AddChild(_managePanel);
        ShowMain();
    }

    // -----------------------------------------------------------------------
    // Panels
    // -----------------------------------------------------------------------

    private PanelContainer BuildWorlds()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(MenuSkin.Title("SELECT WORLD"));

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(540, 250),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _worldList = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _worldList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_worldList);
        box.AddChild(scroll);

        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MenuSkin.MakeButton("New world", ShowNewWorld, 0));
        row.AddChild(MenuSkin.MakeButton("Back", ShowMain, 0));
        box.AddChild(row);
        return MenuSkin.Wrap(box);
    }

    private PanelContainer BuildNewWorld()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(MenuSkin.Title("NEW WORLD"));

        _nameEdit = new LineEdit
        {
            PlaceholderText = "world name",
            Text = "New World",
            CustomMinimumSize = new Vector2(540, 0),
        };
        box.AddChild(_nameEdit);
        _seedEdit = new LineEdit
        {
            PlaceholderText = "seed or share code  (e.g. 1337 or 1337·amplified)",
            CustomMinimumSize = new Vector2(540, 0),
        };
        box.AddChild(_seedEdit);

        _typeOption = new OptionButton
        {
            CustomMinimumSize = new Vector2(540, 0),
        };
        _typeOption.AddItem("Normal world", 0);
        _typeOption.AddItem("Flat world", 1);
        _typeOption.AddItem("Amplified — tall mountains", 2);
        _typeOption.AddItem("Sky islands", 3);
        box.AddChild(_typeOption);

        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MenuSkin.MakeButton("Create", CreateWorld, 0));
        row.AddChild(MenuSkin.MakeButton("Back", ShowMain, 0));
        box.AddChild(row);
        return MenuSkin.Wrap(box);
    }

    private PanelContainer BuildSettings()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(MenuSkin.Title("SETTINGS"));
        box.AddChild(SettingsPanel.Build(Sound.ApplyVolumes));
        box.AddChild(MenuSkin.MakeButton("Back", ShowMain, 0));
        return MenuSkin.Wrap(box);
    }

    private PanelContainer BuildManage()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        _manageTitle = MenuSkin.Title("MANAGE WORLD", 16);
        box.AddChild(_manageTitle);
        _renameEdit = new LineEdit
        {
            PlaceholderText = "world name",
            CustomMinimumSize = new Vector2(540, 0),
        };
        box.AddChild(_renameEdit);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MenuSkin.MakeButton("Rename", RenameWorld, 0));
        row.AddChild(MenuSkin.MakeButton("Duplicate", DuplicateWorld, 0));
        row.AddChild(MenuSkin.MakeButton("Delete", ConfirmDelete, 0));
        box.AddChild(row);
        _manageStatus = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _manageStatus.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_manageStatus);
        box.AddChild(MenuSkin.MakeButton("Back", () => { RefreshWorlds(); ShowWorlds(); }, 0));

        _deleteDialog = new ConfirmationDialog
        {
            Title = "Delete world?",
            DialogText = "This permanently deletes the world and its save files.",
        };
        _deleteDialog.Confirmed += DeleteWorld;
        AddChild(_deleteDialog);
        return MenuSkin.Wrap(box);
    }

    // -----------------------------------------------------------------------
    // Actions
    // -----------------------------------------------------------------------

    private void RefreshWorlds()
    {
        foreach (Node child in _worldList.GetChildren())
        {
            child.QueueFree();
        }

        var worlds = WorldStore.List();
        if (worlds.Count == 0)
        {
            Label none = new()
            {
                Text = "no worlds yet — create one",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            none.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.9f, 0.7f));
            _worldList.AddChild(none);
            return;
        }

        foreach ((string dir, WorldManifest manifest) in worlds)
        {
            string played = Time.GetDatetimeStringFromUnixTime(manifest.PlayedUtc, true);
            string presetTag = manifest.Preset is { Length: > 0 } pr ? $"   ·   {pr}" : "";
            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 6);
            Button entry = MenuSkin.MakeButton(
                    $"{manifest.Name}   ·   seed {manifest.Seed}{presetTag}   ·   gen v{manifest.GenVersion}   ·   {played}",
                    () => StartWorld(dir),
                    0);
            entry.Alignment = HorizontalAlignment.Left;
            entry.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(entry);
            row.AddChild(MenuSkin.MakeButton("Manage", () => ShowManage(dir, manifest), 92));
            _worldList.AddChild(row);
        }
    }

    private void ShowManage(string dir, WorldManifest manifest)
    {
        _manageDir = dir;
        _manageTitle.Text = $"MANAGE — {manifest.Name}";
        _renameEdit.Text = manifest.Name;
        _manageStatus.Text = "";
        _buttons.Visible = false;
        _worldsPanel.Visible = false;
        _newWorldPanel.Visible = false;
        _settingsPanel.Visible = false;
        _managePanel.Visible = true;
    }

    private void RenameWorld()
    {
        if (WorldStore.Rename(_manageDir, _renameEdit.Text))
        {
            _manageStatus.Text = "Renamed.";
            _manageTitle.Text = $"MANAGE — {_renameEdit.Text.Trim()}";
        }
        else
        {
            _manageStatus.Text = "Enter a name.";
        }
    }

    private void DuplicateWorld()
    {
        string? copy = WorldStore.Duplicate(_manageDir);
        _manageStatus.Text = copy is null ? "Could not duplicate world." : "Copy created.";
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
            _manageStatus.Text = "World deleted.";
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

    // -----------------------------------------------------------------------
    // Panel switching
    // -----------------------------------------------------------------------

    private void ShowMain()
    {
        _buttons.Visible = true;
        _worldsPanel.Visible = false;
        _newWorldPanel.Visible = false;
        _settingsPanel.Visible = false;
        _managePanel.Visible = false;
        UiFocus.FocusFirst(_buttons);
    }

    private void ShowWorlds()
    {
        RefreshWorlds();
        _buttons.Visible = false;
        _worldsPanel.Visible = true;
        _newWorldPanel.Visible = false;
        _settingsPanel.Visible = false;
        _managePanel.Visible = false;
        UiFocus.FocusFirst(_worldsPanel);
    }

    private void ShowNewWorld()
    {
        _seedEdit.Text = "";
        _buttons.Visible = false;
        _worldsPanel.Visible = false;
        _newWorldPanel.Visible = true;
        _settingsPanel.Visible = false;
        _managePanel.Visible = false;
        UiFocus.FocusFirst(_newWorldPanel);
    }

    private void ShowSettings()
    {
        _buttons.Visible = false;
        _worldsPanel.Visible = false;
        _newWorldPanel.Visible = false;
        _settingsPanel.Visible = true;
        _managePanel.Visible = false;
        UiFocus.FocusFirst(_settingsPanel);
    }
}
