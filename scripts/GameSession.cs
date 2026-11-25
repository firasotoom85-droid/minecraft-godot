using Godot;

namespace Minecraft;

/// <summary>
/// Autoload: the selected world plus boot-time setup shared by the menu and
/// the game scene (settings load, audio buses, legacy save migration).
/// </summary>
public partial class GameSession : Node
{
    public static GameSession Instance = null!;

    public string WorldDir = "";
    public WorldManifest? Manifest;

    public override void _Ready()
    {
        Instance = this;
        Settings.Load();
        GamepadBindings.Configure();
        Keybinds.Load();
        Sound.EnsureBuses();
        Sound.ApplyVolumes();
        ContentValidation.ValidateOrWarn();
        WorldStore.MigrateLegacyIfNeeded();
    }

    public void SelectWorld(string dir)
    {
        WorldDir = dir;
        Manifest = WorldStore.ReadManifest(dir);
    }

    /// <summary>
    /// Resolve a usable world from any entry path. An explicit selection is
    /// authoritative: unreadable metadata or saves must never select another world.
    /// </summary>
    public string EnsureWorld()
    {
        if (WorldDir.Length > 0)
        {
            WorldReadResult<WorldManifest> manifestRead = WorldStore.ReadManifestResult(WorldDir);
            if (manifestRead.Value is not { } manifest)
            {
                Manifest = null;
                throw new System.IO.InvalidDataException(
                        $"The selected world cannot be opened ({manifestRead.Status}). "
                        + "Its files have been retained; use world recovery before continuing. "
                        + manifestRead.Error);
            }

            Manifest = manifest;
            WorldReadResult<WorldSave> worldRead = WorldStore.ReadJsonResult<WorldSave>(
                    WorldStore.WorldSavePath(WorldDir), WorldStore.Json);
            WorldReadResult<PlayerSave> playerRead = WorldStore.ReadJsonResult<PlayerSave>(
                    WorldStore.PlayerSavePath(WorldDir), WorldStore.Json);
            if (worldRead.Status is WorldReadStatus.Corrupt or WorldReadStatus.Unsupported
                    or WorldReadStatus.Inaccessible
                    || playerRead.Status is WorldReadStatus.Corrupt or WorldReadStatus.Unsupported
                    or WorldReadStatus.Inaccessible)
            {
                throw new System.IO.InvalidDataException(
                        $"The selected world requires save recovery. World data: {worldRead.Status} ({worldRead.Error}); "
                        + $"player data: {playerRead.Status} ({playerRead.Error}). No files were replaced.");
            }

            bool incompletePair = (worldRead.Status == WorldReadStatus.Missing)
                    != (playerRead.Status == WorldReadStatus.Missing);
            bool knownSavedHistory = manifest.HasSavedData || manifest.SnapshotId is not null
                    || manifest.PlaytimeSeconds > 0 || manifest.PlayedUtc > manifest.CreatedUtc;
            if (incompletePair && knownSavedHistory)
            {
                WorldStore.RequireRecovery(WorldDir);
                throw new System.IO.InvalidDataException(
                        "The selected world contains an incomplete save set. Restore a matching world/player backup before playing.");
            }

            if (worldRead.Status == WorldReadStatus.Missing && playerRead.Status == WorldReadStatus.Missing
                    && (manifest.HasSavedData || manifest.SnapshotId is not null
                    || manifest.PlaytimeSeconds > 0 || manifest.PlayedUtc > manifest.CreatedUtc))
            {
                WorldStore.RequireRecovery(WorldDir);
                throw new System.IO.InvalidDataException(
                        "This world previously contained saved progress, but all world/player save candidates are missing. Recovery is required.");
            }

            if (WorldStore.IsRecoveryRequired(WorldDir))
                throw new System.IO.InvalidDataException(
                        "The selected world is marked as recovery-required. Its data will not be overwritten.");

            // Upgrade older manifests in place after verifying both existing
            // save models. The optional field is ignored by pre-existing versions.
            if (!manifest.HasSavedData && worldRead.Value is not null && playerRead.Value is not null)
            {
                WorldManifest marked = manifest with { HasSavedData = true };
                if (!WorldStore.WriteManifest(WorldDir, marked))
                    throw new System.IO.IOException(
                            "The world is readable, but its save-history safety metadata could not be persisted.");
                Manifest = marked;
            }

            return WorldDir;
        }

        WorldStore.MigrateLegacyIfNeeded();
        var worlds = WorldStore.List();
        if (WorldStore.LastListError is not null)
            throw new System.IO.IOException(WorldStore.LastListError);
        if (worlds.Count > 0)
        {
            SelectWorld(worlds[0].Dir);
        }
        else if (WorldStore.UnreadableWorldDirectories.Count > 0)
        {
            WorldDir = WorldStore.UnreadableWorldDirectories[0];
            Manifest = null;
            throw new System.IO.InvalidDataException(
                    "Saved worlds exist but their metadata cannot be read. Open world management to recover them.");
        }
        else
        {
            SelectWorld(WorldStore.Create("My World", null));
        }

        return WorldDir;
    }
}
