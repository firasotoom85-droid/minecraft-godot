using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Minecraft;

/// <summary>Per-world metadata, stored next to the save files. Mode is the
/// default game mode for this world (null on pre-M2 worlds = creative).
/// Preset (gen v5+) selects the terrain shape: normal / flat / amplified / sky.</summary>
public sealed record WorldManifest(
    string Name,
    int Seed,
    int GenVersion,
    int[] NoiseSeeds,
    long CreatedUtc,
    long PlayedUtc,
    string? Mode = null,
    string? Preset = null,
    long PlaytimeSeconds = 0,
    bool HasSavedData = false,
    string? SnapshotId = null);

/// <summary>Legacy combined save (v1). Read only while migrating an old save.</summary>
public sealed record LegacySave(
    Dictionary<string, Dictionary<string, BlockId>>? Edits,
    float[]? Player,
    double Time);

/// <summary>World state (version 4): block edits, clock, containers, furnace
/// state, flags, optional vehicle state, an optional sparse block-state
/// extension, village residents and quest progress. Every field after Time is
/// optional so v1-v3 saves load without conversion.</summary>
public sealed record WorldSave(
    int Version,
    Dictionary<string, Dictionary<string, BlockId>>? Edits,
    double Time,
    Dictionary<string, List<ItemStackSave>>? Containers = null,
    Dictionary<string, bool>? Flags = null,
    Dictionary<string, FurnaceSave>? Furnaces = null,
    Dictionary<string, Dictionary<string, byte>>? BlockStates = null,
    Dictionary<string, VillagerSave>? Villagers = null,
    Dictionary<string, QuestProgressSave>? Quests = null,
    List<BoatSave>? Boats = null,
    string? SnapshotId = null);

/// <summary>Per-player state. Experience is optional and defaults to zero for legacy saves.</summary>
public sealed record PlayerSave(
    float[] Pos,
    int Selected = 0,
    List<ItemStackSave>? Items = null,
    string? Mode = null,
    float Health = 0.0f,
    float? Hunger = null,
    float[]? Respawn = null,
    int Experience = 0,
    float? Yaw = null,
    float? Pitch = null,
    bool Dead = false,
    string? SnapshotId = null);

public enum WorldReadStatus { Missing, Valid, Recovered, Corrupt, Unsupported, Inaccessible }

public sealed record WorldReadResult<T>(T? Value, WorldReadStatus Status, string? Error) where T : class;

public sealed record WorldOperationResult(bool Success, string Message);

/// <summary>One saved inventory slot (0-8 hotbar, 9-35 backpack, 36-39 armor).</summary>
public sealed record ItemStackSave(int Slot, int Id, int Count, int Durability = 0,
    int Sharpness = 0, int Protection = 0, int Unbreaking = 0);

/// <summary>Persisted furnace slots + burn/smelt timers.</summary>
public sealed record FurnaceSave(
    List<ItemStackSave> Slots,
    float BurnTime = 0.0f,
    float BurnMax = 0.0f,
    float SmeltTime = 0.0f);

/// <summary>Optional per-world boat state. Missing Boats in old saves means no boats.</summary>
public sealed record BoatSave(float[] Position, float Yaw, float[] Velocity, bool Occupied = false);

/// <summary>
/// World folders under user://worlds/&lt;id&gt;/ hold manifest.json (identity +
/// seeds), world.json (edits + time) and player.json (position). Saves are
/// temp-written, rotated through two .bak backups, then renamed into place.
/// </summary>
public static class WorldStore
{
    public const int CurrentGenVersion = WorldGenV12.Version;
    public const int WorldFormatVersion = 4;
    public const string WorldsRoot = "user://worlds";
    private const string LegacySavePath = "user://minecraft_save.json";

    // The five noise seeds the pre-seed build hardcoded, kept verbatim so the
    // migrated world regenerates byte-identical terrain.
    public static readonly int[] LegacyNoiseSeeds = [1337, 7331, 4242, 5150, 9109];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal static readonly JsonSerializerOptions ManifestJson = new(Json)
    {
        WriteIndented = true,
    };

    private static readonly HashSet<string> RecoveryRequiredDirectories = new(StringComparer.Ordinal);
    public static string? LastListError { get; private set; }
    public static List<string> UnreadableWorldDirectories { get; } = [];

    // Narrow failure-injection seams used by the serial headless persistence tests.
    internal static Func<string>? WorldIdFactoryForTests;
    internal static Func<string, string, Error>? RenameForTests;
    internal static Func<string, bool>? CopyFileAllowedForTests;
    internal static Func<string, bool>? CopyFileWriteAllowedForTests;
    internal static Func<string, string[]>? EnumerateWorldDirectoriesForTests;
    internal static Action<string, string>? MoveDirectoryForTests;

    public static long NowUtc() => (long)Time.GetUnixTimeFromSystem();

    public static string ManifestPath(string dir) => $"{dir}/manifest.json";

    public static string WorldSavePath(string dir) => $"{dir}/world.json";

    public static string PlayerSavePath(string dir) => $"{dir}/player.json";

    public static string DeletedWorldPath(string dir)
    {
        string prefix = WorldsRoot + "/";
        string id = dir.StartsWith(prefix, StringComparison.Ordinal) ? dir[prefix.Length..] : "";
        if (id.Length == 0 || id.Contains('/') || id.Contains('\\') || id is "." or "..")
            throw new System.ArgumentException("The world directory is not a direct child of the worlds root.", nameof(dir));
        return $"{WorldsRoot}/.quarantine/{id}";
    }

    /// <summary>One master seed maps to five noise seeds (splitmix-style mix, frozen forever).</summary>
    public static int[] DeriveNoiseSeeds(int seed)
    {
        int[] seeds = new int[5];
        for (int i = 0; i < seeds.Length; i++)
        {
            ulong x = unchecked((ulong)(uint)seed ^ ((ulong)(i + 1) * 0x9E3779B97F4A7C15UL));
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            seeds[i] = unchecked((int)(uint)(x >> 32));
        }

        return seeds;
    }

    public static WorldManifest? ReadManifest(string dir) =>
        ReadManifestResult(dir).Value;

    public static WorldReadResult<WorldManifest> ReadManifestResult(string dir) =>
        ReadJsonResult<WorldManifest>(ManifestPath(dir), ManifestJson);

    public static bool IsRecoveryRequired(string dir) =>
        RecoveryRequiredDirectories.Contains(dir.TrimEnd('/'));

    internal static void ClearRecoveryRequired(string dir) =>
        RecoveryRequiredDirectories.Remove(dir.TrimEnd('/'));

    public static void RequireRecovery(string dir) =>
        RecoveryRequiredDirectories.Add(dir.TrimEnd('/'));

    private static void MarkRecoveryRequired(string path)
    {
        string prefix = WorldsRoot + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return;
        int separator = path.LastIndexOf('/');
        if (separator > prefix.Length) RecoveryRequiredDirectories.Add(path[..separator]);
    }

    private static string? ParentWorldDirectory(string path)
    {
        string prefix = WorldsRoot + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string[] segments = path[prefix.Length..].Split('/');
        if (segments.Length < 2 || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            return null;
        return $"{prefix}{segments[0]}";
    }

    public static bool WriteManifest(string dir, WorldManifest manifest)
    {
        if (!ValidateModel(manifest, out _, out string validationError))
        {
            GD.PushWarning($"minecraft: refusing invalid world manifest for {dir}: {validationError}");
            return false;
        }

        if (!DirAccess.DirExistsAbsolute(dir))
        {
            Error mkdir = DirAccess.MakeDirRecursiveAbsolute(dir);
            if (mkdir != Error.Ok && !DirAccess.DirExistsAbsolute(dir))
            {
                GD.PushWarning($"minecraft: could not create world directory {dir} ({mkdir})");
                return false;
            }
        }

        WorldReadResult<WorldSave> worldSave = ReadJsonResult<WorldSave>(WorldSavePath(dir), Json);
        WorldReadResult<PlayerSave> playerSave = ReadJsonResult<PlayerSave>(PlayerSavePath(dir), Json);
        if (manifest.HasSavedData || (worldSave.Value is not null && playerSave.Value is not null))
            manifest = manifest with { HasSavedData = true };

        return WriteAtomic(ManifestPath(dir), JsonSerializer.Serialize(manifest, ManifestJson));
    }

    /// <summary>Most recently played worlds first. Corrupt world folders are retained for recovery.</summary>
    public static List<(string Dir, WorldManifest Manifest)> List()
    {
        List<(string Dir, WorldManifest Manifest)> result = [];
        LastListError = null;
        UnreadableWorldDirectories.Clear();
        string absoluteRoot = ProjectSettings.GlobalizePath(WorldsRoot);
        string[] directories;
        try
        {
            if (!System.IO.Directory.Exists(absoluteRoot))
                System.IO.Directory.CreateDirectory(absoluteRoot);
            if ((System.IO.File.GetAttributes(absoluteRoot) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new System.IO.IOException("The worlds library directory is a filesystem link; refusing to enumerate through it.");
            directories = EnumerateWorldDirectoriesForTests?.Invoke(absoluteRoot)
                    ?? System.IO.Directory.GetDirectories(absoluteRoot);
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LastListError = $"Saved worlds could not be read: {e.Message}";
            return result;
        }

        foreach (string absolute in directories)
        {
            string name = System.IO.Path.GetFileName(absolute.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name) || name == ".quarantine") continue;
            string dir = $"{WorldsRoot}/{name}";
            try
            {
                if ((System.IO.File.GetAttributes(absolute) & System.IO.FileAttributes.ReparsePoint) != 0)
                {
                    UnreadableWorldDirectories.Add(dir);
                    MarkRecoveryRequired(ManifestPath(dir));
                    continue;
                }

                WorldReadResult<WorldManifest> manifest = ReadManifestResult(dir);
                if (manifest.Value is { } value)
                {
                    WorldReadResult<WorldSave> worldSave = ReadJsonResult<WorldSave>(WorldSavePath(dir), Json);
                    WorldReadResult<PlayerSave> playerSave = ReadJsonResult<PlayerSave>(PlayerSavePath(dir), Json);
                    bool worldUnavailable = worldSave.Status is WorldReadStatus.Corrupt
                            or WorldReadStatus.Unsupported or WorldReadStatus.Inaccessible;
                    bool playerUnavailable = playerSave.Status is WorldReadStatus.Corrupt
                            or WorldReadStatus.Unsupported or WorldReadStatus.Inaccessible;
                    bool incompletePair = (worldSave.Status == WorldReadStatus.Missing)
                            != (playerSave.Status == WorldReadStatus.Missing);
                    bool knownSavedHistory = value.HasSavedData || value.SnapshotId is not null
                            || value.PlaytimeSeconds > 0 || value.PlayedUtc > value.CreatedUtc;
                    bool missingHistory = worldSave.Status == WorldReadStatus.Missing
                            && playerSave.Status == WorldReadStatus.Missing && knownSavedHistory;
                    bool unexplainedIncompletePair = incompletePair && knownSavedHistory;
                    if (worldUnavailable || playerUnavailable || unexplainedIncompletePair || missingHistory)
                    {
                        if (unexplainedIncompletePair || missingHistory)
                            RequireRecovery(dir);
                        UnreadableWorldDirectories.Add(dir);
                        continue;
                    }

                    result.Add((dir, value));
                    continue;
                }

                bool hasData = HasSaveArtifacts(dir) || System.IO.Directory.GetFileSystemEntries(absolute).Length > 0;
                if (manifest.Status != WorldReadStatus.Missing || hasData)
                {
                    UnreadableWorldDirectories.Add(dir);
                    MarkRecoveryRequired(ManifestPath(dir));
                }
            }
            catch (Exception e) when (e is System.IO.IOException
                    or UnauthorizedAccessException or System.Security.SecurityException)
            {
                UnreadableWorldDirectories.Add(dir);
                MarkRecoveryRequired(ManifestPath(dir));
            }
        }

        result.Sort((a, b) => b.Manifest.PlayedUtc.CompareTo(a.Manifest.PlayedUtc));
        UnreadableWorldDirectories.Sort(StringComparer.Ordinal);
        return result;
    }

    private static bool HasSaveArtifacts(string dir) =>
        FileAccess.FileExists(ManifestPath(dir))
        || FileAccess.FileExists(ManifestPath(dir) + ".bak")
        || FileAccess.FileExists(ManifestPath(dir) + ".bak2")
        || FileAccess.FileExists(WorldSavePath(dir))
        || FileAccess.FileExists(WorldSavePath(dir) + ".bak")
        || FileAccess.FileExists(WorldSavePath(dir) + ".bak2")
        || FileAccess.FileExists(PlayerSavePath(dir))
        || FileAccess.FileExists(PlayerSavePath(dir) + ".bak")
        || FileAccess.FileExists(PlayerSavePath(dir) + ".bak2");

    public static string Create(string name, int? seed, string mode = "survival", string preset = "normal")
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 128
                || mode is not ("survival" or "creative")
                || preset is not ("normal" or "flat" or "amplified" or "sky"))
            throw new System.IO.InvalidDataException("The world name, game mode, or generator preset is invalid.");

        int resolved = seed ?? unchecked((int)(Time.GetTicksUsec() & 0x7FFFFFFF));
        string dir = ReserveWorldDirectory();
        long createdUtc = NowUtc();
        WorldManifest manifest = new(
                name, resolved, CurrentGenVersion, DeriveNoiseSeeds(resolved), createdUtc, createdUtc, mode,
                preset == "normal" ? null : preset);
        if (!WriteManifest(dir, manifest))
        {
            Delete(dir);
            throw new System.IO.IOException("The new world could not be saved. Check storage permissions and available space.");
        }

        return dir;
    }

    private static string ReserveWorldDirectory()
    {
        string absoluteRoot = ProjectSettings.GlobalizePath(WorldsRoot);
        try
        {
            if (!System.IO.Directory.Exists(absoluteRoot))
                System.IO.Directory.CreateDirectory(absoluteRoot);
            System.IO.FileAttributes rootAttributes = System.IO.File.GetAttributes(absoluteRoot);
            if ((rootAttributes & System.IO.FileAttributes.Directory) == 0
                    || (rootAttributes & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new System.IO.IOException("The worlds root must be a regular directory, not a link or file.");
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new System.IO.IOException($"Could not safely access the worlds directory: {e.Message}", e);
        }

        for (int attempt = 0; attempt < 64; attempt++)
        {
            string id = WorldIdFactoryForTests?.Invoke()
                    ?? "w" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(id) || id.Contains('/') || id.Contains('\\')
                    || id is "." or "..")
                throw new System.IO.InvalidDataException("The world ID provider returned an invalid identifier.");
            string dir = $"{WorldsRoot}/{id}";
            string quarantined = ProjectSettings.GlobalizePath(DeletedWorldPath(dir));
            if (System.IO.Directory.Exists(quarantined) || System.IO.File.Exists(quarantined))
                continue;
            Error created = DirAccess.MakeDirAbsolute(dir);
            if (created == Error.Ok) return dir;
            string absolute = ProjectSettings.GlobalizePath(dir);
            if (DirAccess.DirExistsAbsolute(dir) || System.IO.File.Exists(absolute))
                continue;
            throw new System.IO.IOException($"Could not reserve the new world directory ({created}).");
        }

        throw new System.IO.IOException("A unique world directory could not be allocated.");
    }

    /// <summary>Rename a world without touching its seed or save files.</summary>
    public static bool Rename(string dir, string name) => RenameDetailed(dir, name).Success;

    public static WorldOperationResult RenameDetailed(string dir, string name)
    {
        string cleanName = name?.Trim() ?? "";
        if (cleanName.Length == 0 || cleanName.Length > 128)
            return new WorldOperationResult(false, "Enter a valid world name (1–128 characters).");

        WorldReadResult<WorldManifest> read = ReadManifestResult(dir);
        if (read.Value is null)
            return new WorldOperationResult(false, read.Status switch
            {
                WorldReadStatus.Missing => "The world manifest is missing.",
                WorldReadStatus.Inaccessible => "The world manifest cannot be read because storage access was denied.",
                WorldReadStatus.Unsupported => "The world manifest uses an unsupported generator version.",
                _ => "The world manifest is corrupt. Restore a valid backup before renaming."
            });

        if (!WriteManifest(dir, read.Value with { Name = cleanName }))
            return new WorldOperationResult(false,
                    "The name is valid, but the manifest could not be saved. Check storage permissions and available space.");
        return new WorldOperationResult(true, "Renamed.");
    }

    /// <summary>Copy a world folder and give the copy a fresh identity.</summary>
    public static string? Duplicate(string dir, string? name = null)
    {
        WorldManifest? manifest = ReadManifest(dir);
        if (manifest is null) return null;

        string target;
        try { target = ReserveWorldDirectory(); }
        catch (System.IO.IOException) { return null; }

        if (!CopyDirectory(dir, target))
        {
            Delete(target);
            return null;
        }

        long duplicatedUtc = NowUtc();
        string copyName = string.IsNullOrWhiteSpace(name) ? $"{manifest.Name} copy" : name.Trim();
        if (copyName.Length > 128) copyName = copyName[..128];
        bool manifestWritten = WriteManifest(target, manifest with
        {
            Name = copyName,
            CreatedUtc = duplicatedUtc,
            PlayedUtc = duplicatedUtc,
        });
        if (!manifestWritten)
        {
            Delete(target);
            return null;
        }

        return target;
    }

    /// <summary>Delete a world without following links or leaving the configured worlds root.</summary>
    public static bool Delete(string dir)
    {
        string prefix = WorldsRoot + "/";
        if (!dir.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string relative = dir[prefix.Length..];
        if (relative.Length == 0 || relative.Contains('/') || relative.Contains('\\')
                || relative is "." or "..")
            return false;

        string root = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(WorldsRoot));
        string target = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        string rootPrefix = root.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootPrefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return false;

        try
        {
            if ((System.IO.File.GetAttributes(root) & System.IO.FileAttributes.ReparsePoint) != 0)
                return false;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }

        try
        {
            System.IO.FileAttributes targetAttributes = System.IO.File.GetAttributes(target);
            if ((targetAttributes & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                    != System.IO.FileAttributes.Directory)
                return false;

            string quarantineRoot = System.IO.Path.Combine(root, ".quarantine");
            if (System.IO.Directory.Exists(quarantineRoot))
            {
                System.IO.FileAttributes quarantineAttributes = System.IO.File.GetAttributes(quarantineRoot);
                if ((quarantineAttributes & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory)
                    return false;
            }
            else
            {
                System.IO.Directory.CreateDirectory(quarantineRoot);
            }

            string quarantineTarget = System.IO.Path.Combine(quarantineRoot, relative);
            if (System.IO.Directory.Exists(quarantineTarget) || System.IO.File.Exists(quarantineTarget))
                return false;

            // One same-volume directory rename is the deletion transaction. Keep
            // the complete save tree intact if the filesystem operation fails.
            if (MoveDirectoryForTests is { } move)
                move(target, quarantineTarget);
            else
                System.IO.Directory.Move(target, quarantineTarget);

            RecoveryRequiredDirectories.Remove(dir.TrimEnd('/'));
            return true;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            GD.PushWarning($"minecraft: could not quarantine world directory {dir}: {e.Message}");
            return false;
        }
    }

    /// <summary>Restore a world previously moved into the deletion quarantine.</summary>
    public static bool RestoreDeletedWorld(string dir)
    {
        string prefix = WorldsRoot + "/";
        if (!dir.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string relative = dir[prefix.Length..];
        if (relative.Length == 0 || relative.Contains('/') || relative.Contains('\\')
                || relative is "." or "..") return false;

        string root = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(WorldsRoot));
        string target = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        string quarantineRoot = System.IO.Path.GetFullPath(
                ProjectSettings.GlobalizePath(WorldsRoot + "/.quarantine"));
        string quarantine = System.IO.Path.Combine(quarantineRoot, relative);
        string rootPrefix = root.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootPrefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return false;

        try
        {
            if (System.IO.Directory.Exists(target) || System.IO.File.Exists(target)) return false;
            System.IO.FileAttributes rootAttributes = System.IO.File.GetAttributes(root);
            System.IO.FileAttributes quarantineAttributes = System.IO.File.GetAttributes(quarantineRoot);
            System.IO.FileAttributes sourceAttributes = System.IO.File.GetAttributes(quarantine);
            if ((rootAttributes & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory
                    || (quarantineAttributes & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory
                    || (sourceAttributes & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory)
                return false;

            System.IO.Directory.Move(quarantine, target);
            RecoveryRequiredDirectories.Remove(dir.TrimEnd('/'));
            return true;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            GD.PushWarning($"minecraft: could not restore quarantined world {dir}: {e.Message}");
            return false;
        }
    }

    private static bool DeleteDirectoryAbsolute(string directory)
    {
        try
        {
            System.IO.FileAttributes attributes = System.IO.File.GetAttributes(directory);
            if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
            {
                // Delete the link itself. Never enumerate or open its target.
                if (System.IO.Directory.Exists(directory))
                    System.IO.Directory.Delete(directory, recursive: false);
                else
                    System.IO.File.Delete(directory);
                return true;
            }

            if ((attributes & System.IO.FileAttributes.Directory) == 0)
                return false;

            foreach (string entry in System.IO.Directory.GetFileSystemEntries(directory))
            {
                System.IO.FileAttributes childAttributes = System.IO.File.GetAttributes(entry);
                bool isDirectory = (childAttributes & System.IO.FileAttributes.Directory) != 0;
                if ((childAttributes & System.IO.FileAttributes.ReparsePoint) != 0)
                {
                    if (isDirectory) System.IO.Directory.Delete(entry, recursive: false);
                    else System.IO.File.Delete(entry);
                }
                else if (isDirectory)
                {
                    if (!DeleteDirectoryAbsolute(entry)) return false;
                }
                else
                {
                    System.IO.File.Delete(entry);
                }
            }

            System.IO.Directory.Delete(directory, recursive: false);
            return true;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            GD.PushWarning($"minecraft: could not safely delete world directory {directory}: {e.Message}");
            return false;
        }
    }

    private static bool CopyDirectory(string source, string target)
    {
        string prefix = WorldsRoot + "/";
        if (!source.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string relative = source[prefix.Length..];
        if (relative.Length == 0 || relative.Contains('/') || relative.Contains('\\')
                || relative is "." or "..")
            return false;

        try
        {
            string absoluteSource = ProjectSettings.GlobalizePath(source);
            string absoluteTarget = ProjectSettings.GlobalizePath(target);
            System.IO.FileAttributes sourceAttributes = System.IO.File.GetAttributes(absoluteSource);
            if ((sourceAttributes & System.IO.FileAttributes.Directory) == 0
                    || (sourceAttributes & System.IO.FileAttributes.ReparsePoint) != 0)
                return false;
            System.IO.Directory.CreateDirectory(absoluteTarget);
            return CopyDirectoryAbsolute(absoluteSource, absoluteTarget);
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            GD.PushWarning($"minecraft: world copy failed: {e.Message}");
            return false;
        }
    }

    private static bool CopyDirectoryAbsolute(string source, string target)
    {
        foreach (string entry in System.IO.Directory.GetFileSystemEntries(source))
        {
            System.IO.FileAttributes attributes = System.IO.File.GetAttributes(entry);
            if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                return false;

            string destination = System.IO.Path.Combine(target, System.IO.Path.GetFileName(entry));
            if ((attributes & System.IO.FileAttributes.Directory) != 0)
            {
                System.IO.Directory.CreateDirectory(destination);
                if (!CopyDirectoryAbsolute(entry, destination)) return false;
                continue;
            }

            if ((attributes & System.IO.FileAttributes.Device) != 0
                    || !System.IO.File.Exists(entry)
                    || CopyFileAllowedForTests?.Invoke(entry) == false)
                return false;

            using System.IO.FileStream input = new(entry, System.IO.FileMode.Open,
                    System.IO.FileAccess.Read, System.IO.FileShare.Read);
            using System.IO.FileStream output = new(destination, System.IO.FileMode.CreateNew,
                    System.IO.FileAccess.Write, System.IO.FileShare.None);
            if (CopyFileWriteAllowedForTests?.Invoke(entry) == false)
                return false;
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            if (output.Length != input.Length) return false;
        }

        return true;
    }

    /// <summary>Safety net for a missing manifest: only an empty, new directory may receive legacy defaults.</summary>
    public static WorldManifest DefaultFor(string dir)
    {
        string absolute = ProjectSettings.GlobalizePath(dir);
        if (System.IO.Directory.Exists(absolute)
                && System.IO.Directory.GetFileSystemEntries(absolute).Length > 0)
            throw new System.IO.InvalidDataException(
                    $"World metadata is unavailable for {dir}; refusing to invent generator settings for an existing world.");

        if (!System.IO.Directory.Exists(absolute))
            System.IO.Directory.CreateDirectory(absolute);
        long createdUtc = NowUtc();
        WorldManifest manifest = new(
                "My World", LegacyNoiseSeeds[0], 1, LegacyNoiseSeeds, createdUtc, createdUtc);
        if (!WriteManifest(dir, manifest))
            throw new System.IO.IOException($"Could not write the default manifest for {dir}.");
        return manifest;
    }

    /// <summary>One-time import of the pre-worlds save file into a world folder.</summary>
    public static void MigrateLegacyIfNeeded()
    {
        if (!FileAccess.FileExists(LegacySavePath))
            return;

        List<(string Dir, WorldManifest Manifest)> worlds = List();
        if (LastListError is not null || UnreadableWorldDirectories.Count > 0)
            return;

        LegacySave? save;
        try
        {
            save = JsonSerializer.Deserialize<LegacySave>(FileAccess.GetFileAsString(LegacySavePath), Json);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"minecraft: legacy save unreadable ({e.Message}), leaving it untouched");
            return;
        }

        if (save is null)
            return;

        string dir;
        try
        {
            dir = ReserveWorldDirectory();
        }
        catch (System.IO.IOException e)
        {
            GD.PushWarning($"minecraft: legacy migration could not reserve a world directory ({e.Message}); original save left untouched");
            return;
        }

        long migratedUtc = NowUtc();
        HashSet<string> existingNames = worlds.Select(world => world.Manifest.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = "My World";
        for (int suffix = 1; existingNames.Contains(name); suffix++)
        {
            string tail = suffix == 1 ? " (Legacy)" : $" (Legacy {suffix})";
            name = "My World"[..Math.Min(8, 128 - tail.Length)] + tail;
        }

        string migrationSnapshotId = Guid.NewGuid().ToString("N");
        WorldSave legacyWorld = new(WorldFormatVersion, save.Edits, save.Time,
                SnapshotId: migrationSnapshotId);
        PlayerSave legacyPlayer = new(save.Player ?? [], SnapshotId: migrationSnapshotId);
        WorldManifest legacyManifest = new(name, LegacyNoiseSeeds[0], 1,
                LegacyNoiseSeeds, migratedUtc, migratedUtc, HasSavedData: true,
                SnapshotId: migrationSnapshotId);
        WorldSaveSnapshot migration = new(
                legacyWorld,
                legacyPlayer,
                legacyManifest,
                ProjectSettings.GlobalizePath(WorldSavePath(dir)),
                ProjectSettings.GlobalizePath(PlayerSavePath(dir)),
                ProjectSettings.GlobalizePath(ManifestPath(dir)),
                DirtyRevision: 0,
                ElapsedPlaytimeSeconds: 0.0);
        WorldSaveWorkerResult migrationResult = WorldSaveWorker.WriteSynchronously(migration);
        if (!migrationResult.Success)
        {
            GD.PushWarning($"minecraft: legacy migration failed to commit a complete save pair ({migrationResult.Error}); original save left untouched");
            Delete(dir);
            return;
        }

        string migratedPath = LegacySavePath + ".migrated";
        if (FileAccess.FileExists(migratedPath))
            migratedPath += "-" + migratedUtc.ToString(CultureInfo.InvariantCulture);
        if (DirAccess.RenameAbsolute(LegacySavePath, migratedPath) != Error.Ok)
        {
            GD.PushWarning("minecraft: legacy save was imported, but the original file could not be archived; it was left untouched");
            return;
        }

        GD.Print($"minecraft: migrated legacy save into {dir}");
    }

    /// <summary>
    /// Write and flush a sibling temp file before rotating backups. Every
    /// rename is checked; if the final commit fails, restore the previous file.
    /// </summary>
    public static bool WriteAtomic(string path, string text, bool allowRecovery = false)
    {
        bool isWorldPath = path.StartsWith(WorldsRoot + "/", StringComparison.Ordinal);
        string? worldDirectory = ParentWorldDirectory(path);
        if (isWorldPath && worldDirectory is null)
        {
            GD.PushWarning($"minecraft: refusing an invalid world save path: {path}");
            return false;
        }
        if (worldDirectory is not null)
        {
            try
            {
                string absoluteRoot = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(WorldsRoot));
                string absoluteWorld = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(worldDirectory));
                string rootPrefix = absoluteRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                if (!absoluteWorld.StartsWith(rootPrefix, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                        || (System.IO.File.GetAttributes(absoluteWorld)
                        & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory)
                    return false;
            }
            catch (Exception e) when (e is System.IO.IOException
                    or UnauthorizedAccessException or System.Security.SecurityException)
            {
                GD.PushWarning($"minecraft: refusing to write through an unavailable world directory {worldDirectory}: {e.Message}");
                return false;
            }
        }
        if (!allowRecovery && worldDirectory is not null && IsRecoveryRequired(worldDirectory))
        {
            GD.PushWarning($"minecraft: refusing to overwrite recovery-required world data at {path}");
            return false;
        }

        string tmp = path + ".tmp";
        bool Fail(string reason)
        {
            if (FileAccess.FileExists(tmp)) DirAccess.RemoveAbsolute(tmp);
            GD.PushWarning($"minecraft: atomic write failed for {path}: {reason}");
            return false;
        }

        if (!TryGetCandidateAttributes(tmp, out System.IO.FileAttributes temporaryAttributes,
                out bool temporaryExists, out string? temporaryError))
            return Fail($"could not inspect temporary file path ({temporaryError})");
        if (temporaryExists && (temporaryAttributes & (System.IO.FileAttributes.Directory
                | System.IO.FileAttributes.ReparsePoint)) != 0)
            return Fail("the temporary file path is a directory or filesystem link");
        if (temporaryExists && DirAccess.RemoveAbsolute(tmp) != Error.Ok)
            return Fail("could not remove a stale temporary file");

        foreach (string candidate in new[] { path, path + ".bak", path + ".bak2" })
        {
            if (!TryGetCandidateAttributes(candidate, out System.IO.FileAttributes attributes,
                    out bool exists, out string? attributeError))
                return Fail($"could not inspect save generation {candidate} ({attributeError})");
            if (exists && (attributes & (System.IO.FileAttributes.Directory
                    | System.IO.FileAttributes.ReparsePoint)) != 0)
                return Fail($"save generation {candidate} is a directory or filesystem link");
        }

        Error writeError;
        using (FileAccess? file = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
        {
            if (file == null) return Fail($"could not open {tmp}");
            file.StoreString(text);
            file.Flush();
            writeError = file.GetError();
        }
        if (writeError != Error.Ok) return Fail($"temporary file write returned {writeError}");

        bool movedCurrent = false;
        bool movedBackup = false;
        bool stagedOldest = false;
        string bak = path + ".bak";
        string bak2 = path + ".bak2";
        string stagedOldestPath = bak2 + ".staged-" + Guid.NewGuid().ToString("N");

        Error RenamePath(string from, string to) =>
                RenameForTests?.Invoke(from, to) ?? DirAccess.RenameAbsolute(from, to);

        void RestoreRotation()
        {
            if (movedCurrent && !FileAccess.FileExists(path) && FileAccess.FileExists(bak))
            {
                Error restore = RenamePath(bak, path);
                if (restore != Error.Ok)
                    GD.PushError($"minecraft: could not restore {path} from its previous primary ({restore})");
            }
            if (movedBackup && FileAccess.FileExists(bak2) && !FileAccess.FileExists(bak))
            {
                Error restore = RenamePath(bak2, bak);
                if (restore != Error.Ok)
                    GD.PushError($"minecraft: could not restore previous backup {bak} ({restore})");
            }
            if (stagedOldest && FileAccess.FileExists(stagedOldestPath) && !FileAccess.FileExists(bak2))
            {
                Error restore = RenamePath(stagedOldestPath, bak2);
                if (restore != Error.Ok)
                    GD.PushError($"minecraft: oldest backup remains preserved at {stagedOldestPath} ({restore})");
            }
        }

        if (FileAccess.FileExists(path))
        {
            if (DirAccess.DirExistsAbsolute(path) || DirAccess.DirExistsAbsolute(bak)
                    || DirAccess.DirExistsAbsolute(bak2))
                return Fail("a save or backup path is occupied by a directory");

            if (FileAccess.FileExists(bak2))
            {
                Error stage = RenamePath(bak2, stagedOldestPath);
                if (stage != Error.Ok)
                    return Fail($"could not stage oldest backup ({stage})");
                stagedOldest = true;
            }

            if (FileAccess.FileExists(bak))
            {
                Error rotate = RenamePath(bak, bak2);
                if (rotate != Error.Ok)
                {
                    RestoreRotation();
                    return Fail($"could not rotate first backup ({rotate})");
                }
                movedBackup = true;
            }

            Error moveCurrent = RenamePath(path, bak);
            if (moveCurrent != Error.Ok)
            {
                RestoreRotation();
                return Fail($"could not preserve current file as backup ({moveCurrent})");
            }
            movedCurrent = true;
        }

        Error commit = RenamePath(tmp, path);
        if (commit != Error.Ok)
        {
            RestoreRotation();
            return Fail($"could not commit temporary file ({commit})");
        }

        if (stagedOldest && FileAccess.FileExists(stagedOldestPath))
        {
            Error retire = DirAccess.RemoveAbsolute(stagedOldestPath);
            if (retire != Error.Ok)
                GD.PushWarning($"minecraft: committed {path}, but the retired oldest backup remains at {stagedOldestPath} ({retire})");
        }
        return true;
    }

    private static bool TryGetCandidateAttributes(
            string candidate, out System.IO.FileAttributes attributes, out bool exists, out string? error)
    {
        attributes = 0;
        exists = false;
        error = null;
        string absolute = ProjectSettings.GlobalizePath(candidate);
        try
        {
            attributes = System.IO.File.GetAttributes(absolute);
            exists = true;
            return true;
        }
        catch (System.IO.FileNotFoundException) { return true; }
        catch (System.IO.DirectoryNotFoundException) { return true; }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            exists = System.IO.File.Exists(absolute) || System.IO.Directory.Exists(absolute);
            error = e.Message;
            return false;
        }
    }

    private static string? ValidateWorldReadDirectory(string path)
    {
        string? worldDirectory = ParentWorldDirectory(path);
        if (worldDirectory is null) return null;

        foreach (string directory in new[]
                {
                    ProjectSettings.GlobalizePath(WorldsRoot),
                    ProjectSettings.GlobalizePath(worldDirectory),
                })
        {
            try
            {
                System.IO.FileAttributes attributes = System.IO.File.GetAttributes(directory);
                if ((attributes & System.IO.FileAttributes.Directory) == 0
                        || (attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                    return $"world storage path is not a regular directory: {directory}";
            }
            catch (System.IO.DirectoryNotFoundException) { return null; }
            catch (System.IO.FileNotFoundException) { return null; }
            catch (Exception e) when (e is System.IO.IOException
                    or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return $"world storage path cannot be inspected: {e.Message}";
            }
        }

        string snapshotRoot = System.IO.Path.Combine(
                ProjectSettings.GlobalizePath(worldDirectory), ".snapshots");
        try
        {
            if (System.IO.Directory.Exists(snapshotRoot))
            {
                if ((System.IO.File.GetAttributes(snapshotRoot)
                        & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory)
                    return $"world snapshot storage is not a regular directory: {snapshotRoot}";
                foreach (string generationDirectory in System.IO.Directory.GetDirectories(snapshotRoot))
                    if ((System.IO.File.GetAttributes(generationDirectory)
                            & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                            != System.IO.FileAttributes.Directory)
                        return $"world snapshot generation is not a regular directory: {generationDirectory}";
            }
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"world snapshot storage cannot be inspected: {e.Message}";
        }

        return null;
    }

    /// <summary>Parse and structurally validate a JSON file; fall back to the next recovery candidate.</summary>
    public static T? ReadJson<T>(string path, JsonSerializerOptions options)
        where T : class => ReadJsonResult<T>(path, options).Value;

    private static bool HasSaveCandidate(string path)
    {
        if (FileAccess.FileExists(path) || FileAccess.FileExists(path + ".bak")
                || FileAccess.FileExists(path + ".bak2")) return true;
        string? worldDirectory = ParentWorldDirectory(path);
        if (worldDirectory is null) return false;

        string snapshotRoot = System.IO.Path.Combine(
                ProjectSettings.GlobalizePath(worldDirectory), ".snapshots");
        try
        {
            if (!System.IO.Directory.Exists(snapshotRoot)
                    || (System.IO.File.GetAttributes(snapshotRoot)
                    & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                    != System.IO.FileAttributes.Directory) return false;
            string fileName = System.IO.Path.GetFileName(path);
            foreach (string generationDirectory in System.IO.Directory.GetDirectories(snapshotRoot))
            {
                string generation = System.IO.Path.GetFileName(generationDirectory);
                if (generation != "legacy" && !Guid.TryParseExact(generation, "N", out _)) continue;
                if ((System.IO.File.GetAttributes(generationDirectory)
                        & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        != System.IO.FileAttributes.Directory) continue;
                if (System.IO.File.Exists(System.IO.Path.Combine(generationDirectory, fileName)))
                    return true;
            }
            return false;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or System.Security.SecurityException) { return true; }
    }

    public static WorldReadResult<T> ReadJsonResult<T>(string path, JsonSerializerOptions options)
        where T : class
    {
        bool found = false;
        bool inaccessible = false;
        string? lastError = null;
        string? expectedSnapshotId = null;
        string? stableSnapshotPath = null;
        bool validateSnapshotPair = typeof(T) == typeof(WorldSave) || typeof(T) == typeof(PlayerSave);
        if (validateSnapshotPair && ParentWorldDirectory(path) is { } worldDirectory)
        {
            WorldReadResult<WorldManifest> manifestRead = ReadManifestResult(worldDirectory);
            if (manifestRead.Value is not { } committedManifest)
            {
                bool saveExists = HasSaveCandidate(path);
                if (!saveExists && manifestRead.Status == WorldReadStatus.Missing)
                    return new WorldReadResult<T>(null, WorldReadStatus.Missing, null);
                MarkRecoveryRequired(path);
                WorldReadStatus pairStatus = manifestRead.Status switch
                {
                    WorldReadStatus.Unsupported => WorldReadStatus.Unsupported,
                    WorldReadStatus.Inaccessible => WorldReadStatus.Inaccessible,
                    _ => WorldReadStatus.Corrupt,
                };
                return new WorldReadResult<T>(null, pairStatus,
                        $"The save cannot be matched to a readable commit manifest ({manifestRead.Error}).");
            }

            expectedSnapshotId = committedManifest.SnapshotId;
            string snapshotKey = expectedSnapshotId ?? "legacy";
            stableSnapshotPath = $"{worldDirectory}/.snapshots/{snapshotKey}/{System.IO.Path.GetFileName(path)}";
        }
        if (ValidateWorldReadDirectory(path) is { } directoryError)
        {
            MarkRecoveryRequired(path);
            return new WorldReadResult<T>(null, WorldReadStatus.Inaccessible, directoryError);
        }

        List<string> candidates = [path, path + ".bak", path + ".bak2"];
        if (stableSnapshotPath is not null) candidates.Add(stableSnapshotPath);
        if (typeof(T) == typeof(WorldManifest) && ParentWorldDirectory(path) is { } manifestDirectory)
        {
            string snapshotRoot = System.IO.Path.Combine(
                    ProjectSettings.GlobalizePath(manifestDirectory), ".snapshots");
            try
            {
                if (System.IO.Directory.Exists(snapshotRoot)
                        && (System.IO.File.GetAttributes(snapshotRoot)
                        & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                        == System.IO.FileAttributes.Directory)
                {
                    foreach (string generationDirectory in System.IO.Directory.GetDirectories(snapshotRoot)
                            .Where(directory =>
                            {
                                string generation = System.IO.Path.GetFileName(directory);
                                return generation == "legacy" || Guid.TryParseExact(generation, "N", out _);
                            })
                            .OrderByDescending(System.IO.Directory.GetCreationTimeUtc))
                    {
                        if ((System.IO.File.GetAttributes(generationDirectory)
                                & (System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReparsePoint))
                                != System.IO.FileAttributes.Directory) continue;
                        candidates.Add($"{manifestDirectory}/.snapshots/{System.IO.Path.GetFileName(generationDirectory)}/manifest.json");
                    }
                }
            }
            catch (Exception e) when (e is System.IO.IOException
                    or UnauthorizedAccessException or System.Security.SecurityException)
            {
                inaccessible = true;
                lastError = $"could not enumerate stable save snapshots ({e.Message})";
            }
        }
        foreach (string candidate in candidates)
        {
            if (!TryGetCandidateAttributes(candidate, out System.IO.FileAttributes attributes,
                    out bool exists, out string? attributeError))
            {
                found = true;
                inaccessible = true;
                lastError = $"{candidate}: {attributeError}";
                continue;
            }
            if (!exists) continue;
            found = true;
            if ((attributes & (System.IO.FileAttributes.Directory
                    | System.IO.FileAttributes.ReparsePoint)) != 0)
            {
                inaccessible = true;
                lastError = $"{candidate}: directories and filesystem links are not save files";
                continue;
            }

            try
            {
                using FileAccess? file = FileAccess.Open(candidate, FileAccess.ModeFlags.Read);
                if (file is null)
                {
                    inaccessible = true;
                    lastError = $"could not open {candidate}";
                    continue;
                }

                string json = file.GetAsText();
                Error readError = file.GetError();
                if (readError != Error.Ok)
                {
                    inaccessible = true;
                    lastError = $"could not read {candidate} ({readError})";
                    continue;
                }

                T? value = JsonSerializer.Deserialize<T>(json, options);
                if (value is null) { lastError = $"{candidate} contains a null JSON value"; continue; }
                if (!ValidateModel(value, out WorldReadStatus validationStatus, out string validationError))
                {
                    lastError = $"{candidate}: {validationError}";
                    if (validationStatus == WorldReadStatus.Unsupported)
                    {
                        // Never downgrade a newer primary save to an older backup.
                        MarkRecoveryRequired(path);
                        return new WorldReadResult<T>(null, WorldReadStatus.Unsupported, lastError);
                    }
                    continue;
                }

                if (validateSnapshotPair)
                {
                    string candidateSnapshotId = value switch
                    {
                        WorldSave worldSave => worldSave.SnapshotId ?? "",
                        PlayerSave playerSave => playerSave.SnapshotId ?? "",
                        _ => "",
                    };
                    if (!string.Equals(candidateSnapshotId, expectedSnapshotId ?? "", StringComparison.Ordinal))
                    {
                        lastError = $"{candidate}: save generation does not match the committed manifest";
                        continue;
                    }
                }

                if (candidate != path)
                    GD.PushWarning($"minecraft: recovered {path} from {candidate}");
                return new WorldReadResult<T>(value,
                        candidate == path ? WorldReadStatus.Valid : WorldReadStatus.Recovered, null);
            }
            catch (JsonException e) { lastError = $"{candidate}: invalid JSON ({e.Message})"; }
            catch (Exception e) when (e is System.IO.IOException
                    or UnauthorizedAccessException or System.Security.SecurityException)
            {
                inaccessible = true;
                lastError = $"{candidate}: {e.Message}";
            }
        }

        if (!found) return new WorldReadResult<T>(null, WorldReadStatus.Missing, null);
        MarkRecoveryRequired(path);
        WorldReadStatus status = inaccessible ? WorldReadStatus.Inaccessible : WorldReadStatus.Corrupt;
        return new WorldReadResult<T>(null, status,
                lastError ?? "No readable and structurally valid save candidate was found.");
    }

    internal static bool ValidateModel<T>(T value, out WorldReadStatus status, out string error)
        where T : class
    {
        status = WorldReadStatus.Corrupt;
        error = "The save model is structurally invalid.";
        switch (value)
        {
            case WorldManifest manifest:
                if (manifest.Name is null || string.IsNullOrWhiteSpace(manifest.Name)
                        || manifest.Name.Length > 128 || manifest.NoiseSeeds is not { Length: 5 }
                        || !ValidSnapshotId(manifest.SnapshotId))
                    return false;
                if (manifest.GenVersion > CurrentGenVersion)
                {
                    status = WorldReadStatus.Unsupported;
                    error = $"generator version {manifest.GenVersion} is newer than {CurrentGenVersion}";
                    return false;
                }
                if (manifest.GenVersion < 1 || (manifest.Mode is not null
                        && manifest.Mode is not ("survival" or "creative"))
                        || (manifest.Preset is not null
                        && manifest.Preset is not ("normal" or "flat" or "amplified" or "sky")))
                    return false;
                return true;

            case WorldSave save:
                if (save.Version > WorldFormatVersion)
                {
                    status = WorldReadStatus.Unsupported;
                    error = $"world save version {save.Version} is newer than {WorldFormatVersion}";
                    return false;
                }
                if (save.Version < 1 || !double.IsFinite(save.Time)
                        || !ValidSnapshotId(save.SnapshotId)) return false;
                if (save.Edits is not null)
                {
                    HashSet<Vector2I> coordinatesSeen = [];
                    foreach ((string chunk, Dictionary<string, BlockId> inner) in save.Edits)
                    {
                        if (!TryParseCoordinateKey(chunk, 2, out int[] coordinates)
                                || !coordinatesSeen.Add(new Vector2I(coordinates[0], coordinates[1]))
                                || inner is null) return false;
                        HashSet<int> indicesSeen = [];
                        foreach ((string index, BlockId id) in inner)
                        {
                            if (!int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                                    || parsed < 0 || parsed >= 16 * 128 * 16
                                    || index != parsed.ToString(CultureInfo.InvariantCulture)
                                    || !indicesSeen.Add(parsed) || !BlockDb.IsDefined(id))
                                return false;
                        }
                    }
                }
                if (save.Containers is not null)
                {
                    HashSet<Vector3I> coordinatesSeen = [];
                    foreach ((string key, List<ItemStackSave> items) in save.Containers)
                        if (!TryParseCoordinateKey(key, 3, out int[] coordinates)
                                || !coordinatesSeen.Add(new Vector3I(coordinates[0], coordinates[1], coordinates[2]))
                                || !ValidStacks(items, Container.Size)) return false;
                }
                if (save.Flags is not null && save.Flags.Keys.Any(string.IsNullOrWhiteSpace)) return false;
                if (save.Furnaces is not null)
                {
                    HashSet<Vector3I> coordinatesSeen = [];
                    foreach ((string key, FurnaceSave furnace) in save.Furnaces)
                        if (!TryParseCoordinateKey(key, 3, out int[] coordinates)
                                || !coordinatesSeen.Add(new Vector3I(coordinates[0], coordinates[1], coordinates[2]))
                                || furnace is null
                                || !ValidStacks(furnace.Slots, 3, validateFurnaceSlots: true)
                                || !float.IsFinite(furnace.BurnTime) || furnace.BurnTime < 0
                                || !float.IsFinite(furnace.BurnMax) || furnace.BurnMax < 0
                                || !float.IsFinite(furnace.SmeltTime) || furnace.SmeltTime < 0)
                            return false;
                }
                if (save.BlockStates is not null)
                {
                    HashSet<Vector2I> coordinatesSeen = [];
                    foreach ((string chunk, Dictionary<string, byte> inner) in save.BlockStates)
                    {
                        if (!TryParseCoordinateKey(chunk, 2, out int[] coordinates)
                                || !coordinatesSeen.Add(new Vector2I(coordinates[0], coordinates[1]))
                                || inner is null) return false;
                        if (inner.Keys.Any(key => !int.TryParse(key, NumberStyles.Integer,
                                        CultureInfo.InvariantCulture, out int index)
                                || index < 0 || index >= 16 * 128 * 16
                                || key != index.ToString(CultureInfo.InvariantCulture))) return false;
                    }
                }
                if (save.Villagers is not null)
                    foreach ((string key, VillagerSave villager) in save.Villagers)
                        if (string.IsNullOrWhiteSpace(key) || villager is null || villager.Id != key
                                || string.IsNullOrWhiteSpace(villager.Name)
                                || !Enum.IsDefined(typeof(VillagerProfession), villager.Profession)
                                || !ValidVector(villager.Position) || !ValidVector(villager.Home)
                                || !ValidVector(villager.Work) || !float.IsFinite(villager.Health)
                                || villager.Health < 0 || villager.TradesCompleted < 0)
                            return false;
                if (save.Quests is not null)
                    foreach ((string key, QuestProgressSave progress) in save.Quests)
                        if (string.IsNullOrWhiteSpace(key) || progress is null
                                || progress.Stage < 0 || progress.Progress < 0
                                || (QuestCatalog.Find(key) is { } quest
                                && (progress.Stage > quest.Stages.Length
                                || (progress.Stage < quest.Stages.Length
                                && progress.Progress > quest.Stages[progress.Stage].Objective.Count))))
                            return false;
                if (save.Boats is not null)
                    foreach (BoatSave boat in save.Boats)
                        if (boat is null || !ValidVector(boat.Position) || !ValidVector(boat.Velocity)
                                || !float.IsFinite(boat.Yaw))
                            return false;
                return true;

            case PlayerSave player:
                if ((player.Pos is not { Length: 0 } && !ValidVector(player.Pos))
                        || player.Selected < 0
                        || player.Selected >= Inventory.HotbarSize
                        || !ValidSnapshotId(player.SnapshotId)
                        || (player.Items is not null && !ValidStacks(player.Items,
                                Inventory.PersistentSlotCount, validateInventorySlots: true))
                        || (player.Mode is not null && player.Mode is not ("survival" or "creative"))
                        || !float.IsFinite(player.Health) || player.Health < 0
                        || (player.Hunger is { } hunger && (!float.IsFinite(hunger) || hunger < 0 || hunger > 20))
                        || (player.Respawn is not null && !ValidVector(player.Respawn))
                        || player.Experience < 0)
                    return false;
                return true;

            default:
                return true;
        }
    }

    private static bool ValidStacks(
            List<ItemStackSave>? items, int slotCount,
            bool validateInventorySlots = false, bool validateFurnaceSlots = false)
    {
        if (items is null) return false;
        HashSet<int> occupied = [];
        foreach (ItemStackSave stack in items)
        {
            if (stack is null || stack.Slot < 0 || stack.Slot >= slotCount || !occupied.Add(stack.Slot))
                return false;
            if (ItemStackSaveValidator.IsUnrecognizedItemId(stack.Id))
            {
                if (stack.Count <= 0 || stack.Durability < 0 || stack.Sharpness < 0
                        || stack.Protection < 0 || stack.Unbreaking < 0) return false;
                continue;
            }
            if (!ItemStackSaveValidator.TryCreate(stack, out ItemStack item, out _))
                return false;
            if (validateInventorySlots && !ItemStackSaveValidator.FitsInventorySlot(stack.Slot, item))
                return false;
            if (validateFurnaceSlots && !ItemStackSaveValidator.FitsFurnaceSlot(stack.Slot, item))
                return false;
        }
        return true;
    }

    private static bool ValidSnapshotId(string? snapshotId) =>
            snapshotId is null || Guid.TryParseExact(snapshotId, "N", out _);

    private static bool TryParseCoordinateKey(string? key, int dimensions, out int[] coordinates)
    {
        coordinates = [];
        if (string.IsNullOrEmpty(key)) return false;
        string[] parts = key.Split(',');
        if (parts.Length != dimensions) return false;
        int[] parsed = new int[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i])
                    || parts[i] != parsed[i].ToString(CultureInfo.InvariantCulture)) return false;
        }
        coordinates = parsed;
        return true;
    }

    internal static string? SnapshotIdOf(string baseName, string json) => baseName switch
    {
        "manifest.json" => JsonSerializer.Deserialize<WorldManifest>(json, ManifestJson)?.SnapshotId,
        "world.json" => JsonSerializer.Deserialize<WorldSave>(json, Json)?.SnapshotId,
        "player.json" => JsonSerializer.Deserialize<PlayerSave>(json, Json)?.SnapshotId,
        _ => null,
    };

    private static bool ValidVector(float[]? vector) =>
        vector is { Length: 3 } && float.IsFinite(vector[0])
        && float.IsFinite(vector[1]) && float.IsFinite(vector[2]);

    internal static bool IsValidBackupModel(string baseName, string json)
    {
        try
        {
            if (baseName == "manifest.json")
            {
                WorldManifest? value = JsonSerializer.Deserialize<WorldManifest>(json, ManifestJson);
                return value is not null && ValidateModel(value, out _, out _);
            }
            if (baseName == "world.json")
            {
                WorldSave? value = JsonSerializer.Deserialize<WorldSave>(json, Json);
                return value is not null && ValidateModel(value, out _, out _);
            }
            PlayerSave? player = JsonSerializer.Deserialize<PlayerSave>(json, Json);
            return player is not null && ValidateModel(player, out _, out _);
        }
        catch (JsonException) { return false; }
    }

    internal static void TryClearRecoveryRequired(string dir)
    {
        if (!IsRecoveryRequired(dir)) return;
        WorldReadResult<WorldManifest> manifest = ReadManifestResult(dir);
        WorldReadResult<WorldSave> world = ReadJsonResult<WorldSave>(WorldSavePath(dir), Json);
        WorldReadResult<PlayerSave> player = ReadJsonResult<PlayerSave>(PlayerSavePath(dir), Json);
        if (manifest.Value is null
                || (world.Status is not (WorldReadStatus.Valid or WorldReadStatus.Recovered or WorldReadStatus.Missing))
                || (player.Status is not (WorldReadStatus.Valid or WorldReadStatus.Recovered or WorldReadStatus.Missing)))
            return;

        bool bothMissing = world.Status == WorldReadStatus.Missing
                && player.Status == WorldReadStatus.Missing;
        bool bothPresent = world.Value is not null && player.Value is not null;
        bool knownPreviouslySaved = manifest.Value.HasSavedData || manifest.Value.SnapshotId is not null
                || manifest.Value.PlaytimeSeconds > 0 || manifest.Value.PlayedUtc > manifest.Value.CreatedUtc;
        if ((bothMissing && !knownPreviouslySaved) || bothPresent)
            ClearRecoveryRequired(dir);
    }


}
