using System;
using System.Collections.Generic;
using System.IO;
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
        double ElapsedPlaytimeSeconds);

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
            long generation = snapshot.Manifest.SaveGeneration;
            if (generation <= 0
                    || snapshot.World.SaveGeneration != generation
                    || snapshot.Player.SaveGeneration != generation)
            {
                return new WorldSaveWorkerResult(false,
                        "world, player and manifest snapshots do not share a positive generation");
            }

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
    /// Filesystem-only counterpart to WorldStore.WriteAtomic. No Godot API is
    /// called here: the caller globalizes user:// paths before dispatching.
    /// </summary>
    internal static bool WriteAtomic(
            string path, string text, out string? error,
            Action? beforeCurrentToBackup = null, Action? beforePublish = null)
    {
        string temporary = path + ".tmp";
        string backup = path + ".bak";
        string secondBackup = path + ".bak2";
        string oldestBackup = path + ".bak3";
        string stagedOldestBackup = oldestBackup + ".stage-" + Guid.NewGuid().ToString("N");
        bool movedOldestBackup = false;
        bool movedSecondBackup = false;
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

            if (Directory.Exists(backup) || Directory.Exists(secondBackup)
                    || Directory.Exists(oldestBackup))
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

                if (File.Exists(secondBackup))
                {
                    File.Move(secondBackup, oldestBackup);
                    movedSecondBackup = true;
                }

                if (File.Exists(backup))
                {
                    File.Move(backup, secondBackup);
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

            // The previous .bak3 is disposable only after the new primary is
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

            if (!committed && movedBackup && !File.Exists(backup) && File.Exists(secondBackup))
            {
                try
                {
                    File.Move(secondBackup, backup);
                }
                catch (Exception recoveryFailure)
                {
                    recoveryErrors.Add($"could not restore previous backup: {recoveryFailure.Message}");
                }
            }

            if (!committed && movedSecondBackup
                    && !File.Exists(secondBackup) && File.Exists(oldestBackup))
            {
                try
                {
                    File.Move(oldestBackup, secondBackup);
                }
                catch (Exception recoveryFailure)
                {
                    recoveryErrors.Add($"could not restore second backup: {recoveryFailure.Message}");
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
