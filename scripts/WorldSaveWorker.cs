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

    private static WorldSaveWorkerResult Write(WorldSaveSnapshot snapshot)
    {
        try
        {
            string worldJson = JsonSerializer.Serialize(snapshot.World, WorldStore.Json);
            string playerJson = JsonSerializer.Serialize(snapshot.Player, WorldStore.Json);
            string manifestJson = JsonSerializer.Serialize(snapshot.Manifest, WorldStore.ManifestJson);

            List<string> errors = [];
            bool worldSaved = WriteAtomic(snapshot.WorldPath, worldJson, out string? worldError);
            if (!worldSaved && worldError != null) errors.Add(worldError);

            bool playerSaved = WriteAtomic(snapshot.PlayerPath, playerJson, out string? playerError);
            if (!playerSaved && playerError != null) errors.Add(playerError);

            bool manifestSaved = WriteAtomic(snapshot.ManifestPath, manifestJson, out string? manifestError);
            if (!manifestSaved && manifestError != null) errors.Add(manifestError);

            return new WorldSaveWorkerResult(
                    worldSaved && playerSaved && manifestSaved,
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
    internal static bool WriteAtomic(string path, string text, out string? error)
    {
        string temporary = path + ".tmp";
        string backup = path + ".bak";
        string oldestBackup = path + ".bak2";
        bool movedBackup = false;
        bool movedCurrent = false;

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
                if (File.Exists(oldestBackup))
                {
                    File.Delete(oldestBackup);
                }

                if (File.Exists(backup))
                {
                    File.Move(backup, oldestBackup);
                    movedBackup = true;
                }

                File.Move(path, backup);
                movedCurrent = true;
            }

            File.Move(temporary, path);
            error = null;
            return true;
        }
        catch (Exception writeFailure)
        {
            List<string> recoveryErrors = [];

            if (movedCurrent && !File.Exists(path) && File.Exists(backup))
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

            if (movedBackup && !File.Exists(backup) && File.Exists(oldestBackup))
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

            error = recoveryErrors.Count == 0
                    ? writeFailure.Message
                    : $"{writeFailure.Message}; {string.Join("; ", recoveryErrors)}";
            return false;
        }
    }
}
