using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Minecraft;

/// <summary>
/// Immutable value snapshot captured on the main thread. The worker only
/// serializes these plain save models and touches absolute filesystem paths.
/// </summary>
internal sealed record WorldSaveSnapshot(
        WorldSave World,
        PlayerSave Player,
        WorldManifest Manifest,
        string WorldPath,
        string PlayerPath,
        string ManifestPath,
        long DirtyRevision,
        double ElapsedPlaytimeSeconds,
        WorldManifest? PreviousManifest = null);

internal readonly record struct WorldSaveWorkerResult(bool Success, string? Error);

/// <summary>
/// Serializes and atomically writes a world snapshot away from the frame loop.
/// One VoxelWorld task is active at a time, so snapshots cannot overtake.
/// </summary>
internal static class WorldSaveWorker
{
    public static Task<WorldSaveWorkerResult> WriteAsync(WorldSaveSnapshot snapshot) =>
            Task.Run(() => Write(snapshot));

    public static WorldSaveWorkerResult WriteSynchronously(WorldSaveSnapshot snapshot) =>
            Write(snapshot);

    private static WorldSaveWorkerResult Write(WorldSaveSnapshot snapshot)
    {
        try
        {
            string? snapshotId = snapshot.World.SnapshotId;
            if (snapshotId is null || !Guid.TryParseExact(snapshotId, "N", out _)
                    || snapshotId != snapshot.Player.SnapshotId
                    || snapshotId != snapshot.Manifest.SnapshotId)
                return new WorldSaveWorkerResult(false,
                        "world, player and manifest must share one valid snapshot identity");

            bool validWorld = WorldStore.ValidateModel(snapshot.World,
                    out _, out string worldValidationError);
            bool validPlayer = WorldStore.ValidateModel(snapshot.Player,
                    out _, out string playerValidationError);
            bool validManifest = WorldStore.ValidateModel(snapshot.Manifest,
                    out _, out string manifestValidationError);
            if (!validWorld || !validPlayer || !validManifest)
                return new WorldSaveWorkerResult(false,
                        $"save snapshot failed validation: {worldValidationError} {playerValidationError} {manifestValidationError}");

            if (!PreserveCommittedGeneration(snapshot, out string? preservationError))
                return new WorldSaveWorkerResult(false,
                        preservationError ?? "the previous committed generation could not be preserved");

            string worldJson = JsonSerializer.Serialize(snapshot.World, WorldStore.Json);
            string playerJson = JsonSerializer.Serialize(snapshot.Player, WorldStore.Json);
            string manifestJson = JsonSerializer.Serialize(snapshot.Manifest, WorldStore.ManifestJson);

            List<string> errors = [];
            bool worldSaved = WriteAtomic(snapshot.WorldPath, worldJson, out string? worldError);
            if (!worldSaved && worldError != null) errors.Add(worldError);

            bool playerSaved = WriteAtomic(snapshot.PlayerPath, playerJson, out string? playerError);
            if (!playerSaved && playerError != null) errors.Add(playerError);

            // The manifest's HasSavedData flag is the recovery sentinel for a
            // completed world/player pair. Never advance it when either half
            // failed, or a partial first save could look like valid history.
            if (!worldSaved || !playerSaved)
            {
                return new WorldSaveWorkerResult(false,
                        errors.Count == 0 ? "world/player save pair was incomplete" : string.Join("; ", errors));
            }

            bool manifestSaved = WriteAtomic(snapshot.ManifestPath, manifestJson, out string? manifestError);
            if (!manifestSaved && manifestError != null) errors.Add(manifestError);

            if (manifestSaved)
                PruneCommittedSnapshots(Path.GetDirectoryName(snapshot.WorldPath)!);

            return new WorldSaveWorkerResult(
                    manifestSaved,
                    errors.Count == 0 ? null : string.Join("; ", errors));
        }
        catch (Exception error)
        {
            return new WorldSaveWorkerResult(false, error.Message);
        }
    }

    /// <summary>
    /// Preserve the generation currently selected by the manifest before rotating
    /// individual primary/backup files. The directory rename publishes the pair as
    /// one stable recovery unit, so repeated failed retries cannot age it out.
    /// </summary>
    private static bool PreserveCommittedGeneration(WorldSaveSnapshot snapshot, out string? error)
    {
        error = null;
        WorldManifest? previous = snapshot.PreviousManifest;
        if (previous is null) return true;

        string worldDirectory = Path.GetDirectoryName(snapshot.WorldPath)!;
        string snapshotRoot = Path.Combine(worldDirectory, ".snapshots");
        string generationKey = previous.SnapshotId ?? "legacy";
        string generationDirectory = Path.Combine(snapshotRoot, generationKey);
        bool knownHistory = previous.HasSavedData || previous.SnapshotId is not null
                || previous.PlaytimeSeconds > 0 || previous.PlayedUtc > previous.CreatedUtc;

        if (Directory.Exists(generationDirectory))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(generationDirectory);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                        != FileAttributes.Directory)
                {
                    error = "the committed snapshot directory is not a regular directory";
                    return false;
                }

                bool hasWorld = IsMatchingSnapshotFile(
                        Path.Combine(generationDirectory, "world.json"), "world.json", previous.SnapshotId);
                bool hasPlayer = IsMatchingSnapshotFile(
                        Path.Combine(generationDirectory, "player.json"), "player.json", previous.SnapshotId);
                if ((knownHistory && (!hasWorld || !hasPlayer)) || (!hasWorld && !hasPlayer))
                {
                    error = "the existing committed snapshot directory is incomplete or corrupt";
                    return false;
                }
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                error = $"could not inspect the committed snapshot directory ({e.Message})";
                return false;
            }
        }

        if (Directory.Exists(snapshotRoot))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(snapshotRoot);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                        != FileAttributes.Directory)
                {
                    error = "the snapshot root is not a regular directory";
                    return false;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                error = $"could not inspect the snapshot root ({e.Message})";
                return false;
            }
        }

        bool hasWorldCandidate = TryReadMatchingCandidate(
                snapshot.WorldPath, "world.json", previous.SnapshotId,
                out string worldJson, out string? worldError);
        bool hasPlayerCandidate = TryReadMatchingCandidate(
                snapshot.PlayerPath, "player.json", previous.SnapshotId,
                out string playerJson, out string? playerError);

        if (worldError is not null || playerError is not null)
        {
            error = worldError ?? playerError;
            return false;
        }

        if (!hasWorldCandidate && !hasPlayerCandidate)
        {
            // A never-saved world has no previous pair to preserve. Orphaned
            // files from an interrupted first save have a different ID and are
            // deliberately ignored; the unchanged manifest still rejects them.
            return !knownHistory;
        }

        if (knownHistory && (!hasWorldCandidate || !hasPlayerCandidate))
        {
            error = "the manifest identifies saved history but a matching world/player pair is incomplete";
            return false;
        }

        try
        {
            Directory.CreateDirectory(snapshotRoot);
            string stagingDirectory = Path.Combine(snapshotRoot, ".stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                if (hasWorldCandidate && !WriteAtomic(
                        Path.Combine(stagingDirectory, "world.json"), worldJson, out error))
                    return false;
                if (hasPlayerCandidate && !WriteAtomic(
                        Path.Combine(stagingDirectory, "player.json"), playerJson, out error))
                    return false;
                string manifestJson = JsonSerializer.Serialize(previous, WorldStore.ManifestJson);
                if (!WriteAtomic(Path.Combine(stagingDirectory, "manifest.json"), manifestJson, out error))
                    return false;

                Directory.Move(stagingDirectory, generationDirectory);
                return true;
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    try { Directory.Delete(stagingDirectory, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            error = $"could not publish a stable snapshot of the previous generation ({e.Message})";
            return false;
        }
    }

    private static bool TryReadMatchingCandidate(
            string path, string fileName, string? expectedSnapshotId,
            out string content, out string? error)
    {
        content = "";
        error = null;
        foreach (string candidate in new[] { path, path + ".bak", path + ".bak2" })
        {
            try
            {
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) continue;
                FileAttributes attributes = File.GetAttributes(candidate);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    continue;

                string json = File.ReadAllText(candidate);
                if (!WorldStore.IsValidBackupModel(fileName, json)) continue;
                string? generation;
                try { generation = WorldStore.SnapshotIdOf(fileName, json); }
                catch (JsonException) { continue; }
                if (!string.Equals(generation, expectedSnapshotId, StringComparison.Ordinal))
                    continue;
                content = json;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                error = $"could not read the previous {fileName} candidate ({e.Message})";
                return false;
            }
        }
        return false;
    }

    private static bool IsMatchingSnapshotFile(string path, string fileName, string? expectedSnapshotId)
    {
        try
        {
            if (!File.Exists(path)) return false;
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return false;
            string json = File.ReadAllText(path);
            return WorldStore.IsValidBackupModel(fileName, json)
                    && string.Equals(WorldStore.SnapshotIdOf(fileName, json),
                            expectedSnapshotId, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.SecurityException or JsonException)
        {
            return false;
        }
    }

    private static void PruneCommittedSnapshots(string worldDirectory)
    {
        string snapshotRoot = Path.Combine(worldDirectory, ".snapshots");
        if (!Directory.Exists(snapshotRoot)) return;
        try
        {
            DirectoryInfo root = new(snapshotRoot);
            if ((root.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                    != FileAttributes.Directory) return;

            DirectoryInfo[] snapshots = root.GetDirectories()
                    .Where(directory => !directory.Name.StartsWith(".stage-", StringComparison.Ordinal)
                            && (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(directory => directory.CreationTimeUtc)
                    .ToArray();
            foreach (DirectoryInfo old in snapshots.Skip(2))
            {
                try { old.Delete(recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // Pruning is optional; the committed primary and pair snapshots are safe.
        }
    }

    /// <summary>
    /// Filesystem-only counterpart to WorldStore.WriteAtomic. No Godot API is
    /// called here: the caller globalizes user:// paths before dispatching.
    /// </summary>
    internal static bool WriteAtomic(
            string path, string text, out string? error,
            Action? beforeCurrentToBackup = null, Action? beforePublish = null)
    {
        string temporary = path + ".tmp";
        string backup = path + ".bak";
        string oldestBackup = path + ".bak2";
        string stagedOldestBackup = oldestBackup + ".stage-" + Guid.NewGuid().ToString("N");
        bool movedOldestBackup = false;
        bool movedBackup = false;
        bool movedCurrent = false;
        bool committed = false;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (Directory.Exists(backup) || Directory.Exists(oldestBackup))
            {
                throw new IOException("a backup path is occupied by a directory");
            }

            if (File.Exists(path))
            {
                // Preserve the oldest known-good generation until the new
                // primary is committed. All staged paths are unique siblings
                // so the rename remains on the same filesystem.
                if (File.Exists(oldestBackup))
                {
                    File.Move(oldestBackup, stagedOldestBackup);
                    movedOldestBackup = true;
                }

                if (File.Exists(backup))
                {
                    File.Move(backup, oldestBackup);
                    movedBackup = true;
                }

                beforeCurrentToBackup?.Invoke();
                File.Move(path, backup);
                movedCurrent = true;
            }

            // Failure injection is internal and used only by the regression
            // suite to exercise the rotation and final publish rollback boundaries.
            beforePublish?.Invoke();
            File.Move(temporary, path);
            committed = true;

            // The previous .bak2 is disposable only after the new primary is
            // visible. A cleanup failure does not invalidate that committed save.
            if (movedOldestBackup)
            {
                try
                {
                    File.Delete(stagedOldestBackup);
                }
                catch (IOException)
                {
                    // Leave the uniquely named staged backup for recovery.
                }
                catch (Exception)
                {
                    // Leave the uniquely named staged backup for recovery.
                }
            }

            error = null;
            return true;
        }
        catch (Exception writeFailure)
        {
            List<string> recoveryErrors = [];

            if (!committed && movedCurrent && !File.Exists(path) && File.Exists(backup))
            {
                try
                {
                    File.Move(backup, path);
                }
                catch (Exception recoveryFailure)
                {
                    recoveryErrors.Add($"could not restore current file: {recoveryFailure.Message}");
                }
            }

            if (!committed && movedBackup && !File.Exists(backup) && File.Exists(oldestBackup))
            {
                try
                {
                    File.Move(oldestBackup, backup);
                }
                catch (Exception recoveryFailure)
                {
                    recoveryErrors.Add($"could not restore previous backup: {recoveryFailure.Message}");
                }
            }

            if (!committed && movedOldestBackup
                    && !File.Exists(oldestBackup) && File.Exists(stagedOldestBackup))
            {
                try
                {
                    File.Move(stagedOldestBackup, oldestBackup);
                }
                catch (Exception recoveryFailure)
                {
                    recoveryErrors.Add($"could not restore oldest backup: {recoveryFailure.Message}");
                }
            }

            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanupFailure)
                {
                    recoveryErrors.Add($"could not remove temporary file: {cleanupFailure.Message}");
                }
            }

            // If restoration itself failed, keep the uniquely named staged
            // generation on disk; it is the only remaining copy of that backup.

            error = recoveryErrors.Count == 0
                    ? writeFailure.Message
                    : $"{writeFailure.Message}; {string.Join("; ", recoveryErrors)}";
            return false;
        }
    }
}
