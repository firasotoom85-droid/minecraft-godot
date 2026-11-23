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
            if (manifestRead.Value is null)
            {
                Manifest = null;
                throw new System.IO.InvalidDataException(
                        $"The selected world cannot be opened ({manifestRead.Status}). "
                        + "Its files have been retained; use world recovery before continuing. "
                        + manifestRead.Error);
            }

            Manifest = manifestRead.Value;
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
                        $"The selected world requires save recovery. World data: {worldRead.Status}; "
                        + $"player data: {playerRead.Status}. No files were replaced.");
            }

            if ((worldRead.Status == WorldReadStatus.Missing) != (playerRead.Status == WorldReadStatus.Missing))
            {
                WorldStore.RequireRecovery(WorldDir);
                throw new System.IO.InvalidDataException(
                        "The selected world contains an incomplete save set. Restore a matching world/player backup before playing.");
            }

            if (WorldStore.IsRecoveryRequired(WorldDir))
                throw new System.IO.InvalidDataException(
                        "The selected world is marked as recovery-required. Its data will not be overwritten.");

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
