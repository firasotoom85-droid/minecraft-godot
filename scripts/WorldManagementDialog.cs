using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Minecraft;

/// <summary>Responsive pixel-art world tools for archive portability and restore-point management.</summary>
public partial class WorldManagementDialog : Control
{
    private CenterContainer _center = null!;
    private Label _title = null!;
    private Label _details = null!;
    private Label _status = null!;
    private OptionButton _backups = null!;
    private ProgressBar _progress = null!;
    private Button _exportButton = null!;
    private Button _restoreButton = null!;
    private Button _closeButton = null!;
    private FileDialog _exportDialog = null!;
    private FileDialog _importDialog = null!;
    private ConfirmationDialog _restoreDialog = null!;
    private string _worldDirectory = "";
    private string _pendingBackup = "";
    private bool _busy;
    private bool _importMode;

    public event Action? ImportCompleted;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
        BuildInterface();
        BuildDialogs();
        GetViewport().SizeChanged += ApplyResponsiveScale;
    }

    public void ShowFor(string worldDirectory)
    {
        _worldDirectory = worldDirectory;
        _importMode = false;
        Visible = true;
        RefreshWorldDetails();
        ApplyResponsiveScale();
        UiFocus.FocusFirst(_exportButton);
    }

    public void ShowImport()
    {
        _worldDirectory = "";
        _importMode = true;
        Visible = true;
        _title.Text = "IMPORT WORLD ARCHIVE";
        _details.Text = "Select a .zip world archive. The archive is validated and extracted to a staging folder before it is added. Existing worlds are never overwritten.";
        _status.Text = "Choose a world archive to import.";
        _backups.Clear();
        _exportButton.Disabled = true;
        _restoreButton.Disabled = true;
        _importDialog.PopupCenteredRatio(0.82f);
        ApplyResponsiveScale();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && !_busy && @event.IsActionPressed("ui_cancel"))
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildInterface()
    {
        ColorRect dim = new()
        {
            Color = new Color(0.005f, 0.012f, 0.018f, 0.88f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        _center = new CenterContainer();
        _center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_center);

        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 9);
        _title = MenuSkin.Title("WORLD MANAGEMENT", 19);
        box.AddChild(_title);

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(570, 260),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 8);
        _details = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(540, 0),
        };
        _details.AddThemeColorOverride("font_color", new Color(0.88f, 0.9f, 0.82f));
        _details.AddThemeFontSizeOverride("font_size", 14);
        content.AddChild(_details);

        content.AddChild(MenuSkin.Title("AVAILABLE BACKUPS", 14));
        _backups = new OptionButton
        {
            CustomMinimumSize = new Vector2(540, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _backups.AddThemeFontSizeOverride("font_size", 13);
        content.AddChild(_backups);
        scroll.AddChild(content);
        box.AddChild(scroll);

        _progress = new ProgressBar
        {
            CustomMinimumSize = new Vector2(540, 14),
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            Visible = false,
        };
        box.AddChild(_progress);

        _status = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(540, 32),
        };
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.AddThemeColorOverride("font_color", new Color(0.75f, 0.83f, 0.72f));
        box.AddChild(_status);

        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 8);
        _exportButton = MenuSkin.MakeButton("Export ZIP", OpenExportDialog, 0);
        _restoreButton = MenuSkin.MakeButton("Restore selected", ConfirmRestore, 0);
        actions.AddChild(_exportButton);
        actions.AddChild(_restoreButton);
        box.AddChild(actions);

        _closeButton = MenuSkin.MakeButton("Close", () => Visible = false, 0);
        box.AddChild(_closeButton);

        PanelContainer panel = MenuSkin.Wrap(box, 620);
        _center.AddChild(panel);
        ApplyResponsiveScale();
    }

    private void BuildDialogs()
    {
        _exportDialog = new FileDialog
        {
            Title = "Export world archive",
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.zip ; World archive"],
        };
        _exportDialog.FileSelected += ExportWorld;
        AddChild(_exportDialog);

        _importDialog = new FileDialog
        {
            Title = "Import world archive",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.zip ; World archive"],
        };
        _importDialog.FileSelected += ImportWorld;
        AddChild(_importDialog);

        _restoreDialog = new ConfirmationDialog
        {
            Title = "Restore backup?",
            DialogText = "The current save will first be retained as a safety backup. Restore the selected snapshot?",
        };
        _restoreDialog.Confirmed += RestoreSelectedBackup;
        AddChild(_restoreDialog);
    }

    private void ApplyResponsiveScale()
    {
        if (_center == null || !IsInstanceValid(_center))
            return;
        Vector2 viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0)
            return;
        float scale = Mathf.Clamp(Mathf.Min(viewport.X / 1200.0f, viewport.Y / 850.0f), 0.62f, 1.0f);
        _center.PivotOffset = viewport * 0.5f;
        _center.Scale = Vector2.One * scale;
    }

    private void RefreshWorldDetails()
    {
        WorldManifest? manifest = WorldStore.ReadManifest(_worldDirectory);
        if (manifest is null)
        {
            _title.Text = "WORLD MANAGEMENT";
            _details.Text = "World metadata could not be read. Check the manifest and its backups.";
            _status.Text = "The world manifest is missing or corrupt.";
            _exportButton.Disabled = true;
            _restoreButton.Disabled = true;
            RefreshBackups();
            return;
        }

        long size = WorldArchive.GetDirectorySize(ProjectSettings.GlobalizePath(_worldDirectory));
        string played = manifest.PlayedUtc > 0
                ? Time.GetDatetimeStringFromUnixTime(manifest.PlayedUtc, true) + " UTC"
                : "Never";
        string mode = manifest.Mode ?? "creative (legacy default)";
        string preset = string.IsNullOrWhiteSpace(manifest.Preset) ? "normal" : manifest.Preset;
        _title.Text = $"WORLD — {manifest.Name}";
        _details.Text =
                $"Name: {manifest.Name}\n" +
                $"Seed: {manifest.Seed}\n" +
                $"Generator: V{manifest.GenVersion} · {preset}\n" +
                $"Mode: {mode}\n" +
                $"Playtime: {FormatDuration(manifest.PlaytimeSeconds)}\n" +
                $"Last played: {played}\n" +
                $"World size: {FormatBytes(size)}";
        _exportButton.Disabled = false;
        RefreshBackups();
    }

    private void RefreshBackups()
    {
        _backups.Clear();
        if (string.IsNullOrEmpty(_worldDirectory))
            return;

        System.Collections.Generic.List<WorldBackupInfo> backups = WorldBackupStore.List(_worldDirectory);
        foreach (WorldBackupInfo backup in backups)
        {
            string date = Time.GetDatetimeStringFromUnixTime(backup.ModifiedUtc, true) + " UTC";
            string state = backup.IsValid ? "VALID" : "CORRUPT";
            string text = $"{backup.FileName} · {date} · {FormatBytes(backup.SizeBytes)} · {state}";
            int index = _backups.ItemCount;
            _backups.AddItem(text);
            _backups.SetItemMetadata(index, backup.Path);
        }
        if (_backups.ItemCount > 0)
            _backups.Select(0);
        _restoreButton.Disabled = _backups.ItemCount == 0;
    }

    private void OpenExportDialog()
    {
        if (_busy || string.IsNullOrEmpty(_worldDirectory))
            return;
        WorldManifest? manifest = WorldStore.ReadManifest(_worldDirectory);
        _exportDialog.CurrentFile = SafeFileName((manifest?.Name ?? "world") + ".zip");
        _exportDialog.PopupCenteredRatio(0.82f);
    }

    private void ExportWorld(string selectedPath)
    {
        _ = ExportWorldAsync(ToAbsolutePath(selectedPath));
    }

    private async Task ExportWorldAsync(string archivePath)
    {
        if (_busy)
            return;
        _busy = true;
        SetBusy(true, "Preparing world export…");
        string source = ProjectSettings.GlobalizePath(_worldDirectory);
        try
        {
            await Task.Run(() => WorldArchive.Export(source, archivePath, new DeferredProgress(this)));
            _status.Text = "World exported successfully. The source world was not modified.";
        }
        catch (Exception e)
        {
            _status.Text = "Export failed: " + e.Message;
        }
        finally
        {
            _busy = false;
            SetBusy(false, _status.Text);
        }
    }

    private void ImportWorld(string selectedPath)
    {
        _ = ImportWorldAsync(ToAbsolutePath(selectedPath));
    }

    private async Task ImportWorldAsync(string archivePath)
    {
        if (_busy)
            return;
        _busy = true;
        SetBusy(true, "Validating archive…");
        try
        {
            string root = ProjectSettings.GlobalizePath(WorldStore.WorldsRoot);
            WorldImportResult result = await Task.Run(
                    () => WorldArchive.Import(archivePath, root, new DeferredProgress(this)));
            _status.Text = result.RecoveredFromBackup
                    ? $"Imported “{result.WorldName}”. A damaged primary save was retained; a valid backup is available for recovery."
                    : $"Imported “{result.WorldName}” successfully.";
            _importMode = false;
            Visible = false;
            ImportCompleted?.Invoke();
        }
        catch (Exception e)
        {
            _status.Text = "Import failed: " + e.Message;
        }
        finally
        {
            _busy = false;
            SetBusy(false, _status.Text);
        }
    }

    private void ConfirmRestore()
    {
        if (_busy || _backups.Selected < 0)
            return;
        _pendingBackup = _backups.GetItemMetadata(_backups.Selected).AsString();
        _restoreDialog.DialogText = "The current file will first be retained as a safety backup. Restore the selected snapshot?";
        _restoreDialog.PopupCentered();
    }

    private void RestoreSelectedBackup()
    {
        WorldBackupRestoreResult result = WorldBackupStore.Restore(_worldDirectory, _pendingBackup);
        _status.Text = result.Message;
        RefreshWorldDetails();
    }

    private void SetBusy(bool busy, string message)
    {
        _exportButton.Disabled = busy || _importMode;
        _restoreButton.Disabled = busy || _importMode || _backups.ItemCount == 0;
        _closeButton.Disabled = busy;
        _progress.Visible = busy;
        if (!busy)
            _progress.Value = 0;
        _status.Text = message;
    }

    private void ApplyProgress(WorldArchiveProgress value)
    {
        if (!IsInsideTree())
            return;
        _status.Text = value.Stage;
        _progress.Value = value.Total <= 0 ? 0 : Math.Clamp(value.Completed * 100.0 / value.Total, 0, 100);
    }

    private static string ToAbsolutePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal) || path.StartsWith("res://", StringComparison.Ordinal)
                ? ProjectSettings.GlobalizePath(path) : Path.GetFullPath(path);

    private static string SafeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.0} GiB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):0.0} MiB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KiB";
        return $"{bytes} B";
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0) return "0 min";
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} d {span.Hours} h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes} min";
        return $"{Math.Max(1, span.Minutes)} min";
    }

    private sealed class DeferredProgress(WorldManagementDialog dialog) : IProgress<WorldArchiveProgress>
    {
        public void Report(WorldArchiveProgress value)
        {
            Callable.From(() => dialog.ApplyProgress(value)).CallDeferred();
        }
    }
}
