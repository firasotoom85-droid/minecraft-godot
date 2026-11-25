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
            WorldReadResult<WorldSavePair> pairRead = WorldStore.ReadSavePairResult(WorldDir, manifest);
            if (pairRead.Value is null
                    || pairRead.Status is WorldReadStatus.Corrupt
                    or WorldReadStatus.Unsupported or WorldReadStatus.Inaccessible)
            {
                WorldStore.RequireRecovery(WorldDir);
                throw new System.IO.InvalidDataException(
                        $"The selected world requires matching world/player save recovery ({pairRead.Status}). "
                        + "No files were replaced. " + pairRead.Error);
            }

            if (pairRead.Status == WorldReadStatus.Recovered
                    && pairRead.Value.SaveGeneration > 0)
            {
                if (!WorldStore.PromoteRecoveredSavePair(WorldDir, manifest, pairRead.Value))
                    throw new System.IO.InvalidDataException(
                            "A matching backup pair was found but could not be promoted safely. "
                            + "The world remains recovery-protected.");
                pairRead = WorldStore.ReadSavePairResult(WorldDir, manifest);
                if (pairRead.Value is null || pairRead.Status != WorldReadStatus.Valid)
                {
                    WorldStore.RequireRecovery(WorldDir);
                    throw new System.IO.InvalidDataException(
                            "The recovered save pair could not be verified after promotion. "
                            + "The world remains recovery-protected. " + pairRead.Error);
                }
            }

            if (WorldStore.IsRecoveryRequired(WorldDir))
                throw new System.IO.InvalidDataException(
                        "The selected world is marked as recovery-required. Its data will not be overwritten.");

            // Older manifests may not have recorded save history. Upgrade only
            // after a matching world/player pair has been validated.
            if (!manifest.HasSavedData && pairRead.Value.World is not null
                    && pairRead.Value.Player is not null)
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
