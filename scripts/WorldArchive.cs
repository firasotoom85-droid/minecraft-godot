using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using IoFileAccess = System.IO.FileAccess;
using Godot;

namespace Minecraft;

public readonly record struct WorldArchiveProgress(string Stage, long Completed, long Total);
public sealed record WorldArchiveFile(string Path, long Length, string Sha256);
public sealed record WorldArchiveManifest(
    int ArchiveVersion,
    long ExportedUtc,
    WorldManifest World,
    List<WorldArchiveFile> Files);
public sealed record WorldImportResult(string WorldDirectory, string WorldName, bool RecoveredFromBackup);

/// <summary>
/// Portable, checksummed world archives. Imports are validated and extracted to
/// a private staging directory before a same-volume directory rename publishes them.
/// </summary>
public static class WorldArchive
{
    public const int ArchiveVersion = 1;
    public const string ArchiveManifestName = "archive-manifest.json";
    private const string WorldPrefix = "world/";
    private const int MaxEntries = 100_000;
    private const long MaxExpandedBytes = 16L * 1024 * 1024 * 1024;
    private const long MaxFileBytes = 8L * 1024 * 1024 * 1024;
    private const int BufferSize = 64 * 1024;

    public static long GetDirectorySize(string absoluteDirectory)
    {
        if (!Directory.Exists(absoluteDirectory))
            return 0;

        long total = 0;
        foreach (string file in EnumerateFilesWithoutLinks(absoluteDirectory))
        {
            try { total = checked(total + new FileInfo(file).Length); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return total;
    }

    public static void Export(
        string worldDirectory,
        string archivePath,
        IProgress<WorldArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(worldDirectory);
        string destination = Path.GetFullPath(archivePath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("The selected world folder does not exist.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("An export file already exists at the selected destination.");

        WorldManifest manifest = ReadBestManifest(source)
            ?? throw new InvalidDataException("The world manifest is missing or corrupt.");

        List<(string Absolute, string Relative, long Length)> files = [];
        long totalBytes = 0;
        foreach (string file in EnumerateFilesWithoutLinks(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;
            ValidateRelativePath(relative, allowWorldPrefix: false);
            long length = new FileInfo(file).Length;
            if (length > MaxFileBytes)
                throw new InvalidDataException($"World file is too large to export: {relative}");
            totalBytes = checked(totalBytes + length);
            if (totalBytes > MaxExpandedBytes || files.Count >= MaxEntries)
                throw new InvalidDataException("The world exceeds the supported archive size or file-count limit.");
            files.Add((file, WorldPrefix + relative, length));
        }

        if (!files.Any(file => file.Relative == WorldPrefix + "manifest.json"))
            throw new InvalidDataException("The world folder does not contain manifest.json.");

        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
            throw new IOException("The export destination has no parent directory.");
        Directory.CreateDirectory(parent);

        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        List<WorldArchiveFile> records = [];
        long completed = 0;
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, IoFileAccess.ReadWrite,
                    FileShare.None, BufferSize, FileOptions.SequentialScan))
            {
                using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach ((string absolute, string relative, long length) in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ZipArchiveEntry entry = zip.CreateEntry(relative, CompressionLevel.Fastest);
                        using Stream input = new FileStream(absolute, FileMode.Open, IoFileAccess.Read,
                                FileShare.Read, BufferSize, FileOptions.SequentialScan);
                        using Stream entryStream = entry.Open();
                        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        byte[] buffer = new byte[BufferSize];
                        long entryBytes = 0;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            entryStream.Write(buffer, 0, read);
                            hash.AppendData(buffer, 0, read);
                            entryBytes += read;
                            completed += read;
                            progress?.Report(new WorldArchiveProgress("Exporting world", completed, totalBytes));
                        }

                        if (entryBytes != length)
                            throw new IOException($"World file changed while exporting: {relative}");
                        records.Add(new WorldArchiveFile(relative, entryBytes,
                                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
                    }

                    WorldArchiveManifest archiveManifest = new(
                            ArchiveVersion,
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            manifest,
                            records);
                    ZipArchiveEntry metadata = zip.CreateEntry(ArchiveManifestName, CompressionLevel.Optimal);
                    using Stream metadataStream = metadata.Open();
                    JsonSerializer.Serialize(metadataStream, archiveManifest, WorldStore.Json);
                }

                output.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            progress?.Report(new WorldArchiveProgress("Export complete", totalBytes, totalBytes));
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static WorldImportResult Import(
        string archivePath,
        string worldsRoot,
        IProgress<WorldArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string archiveFile = Path.GetFullPath(archivePath);
        string root = Path.GetFullPath(worldsRoot);
        if (!File.Exists(archiveFile))
            throw new FileNotFoundException("The selected world archive does not exist.", archiveFile);
        Directory.CreateDirectory(root);

        string stagingRoot = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        string stagingWorld = Path.Combine(stagingRoot, "world");
        Directory.CreateDirectory(stagingWorld);

        try
        {
            using ZipArchive zip = ZipFile.OpenRead(archiveFile);
            if (zip.Entries.Count < 2 || zip.Entries.Count > MaxEntries + 1)
                throw new InvalidDataException("The archive has an invalid number of entries.");

            Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchiveEntry(entry);
                if (!entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException($"The archive contains a duplicate path: {entry.FullName}");
            }

            if (!entries.TryGetValue(ArchiveManifestName, out ZipArchiveEntry? metadataEntry)
                    || metadataEntry.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("The archive manifest is missing or too large.");

            WorldArchiveManifest? archiveManifest;
            using (Stream metadataStream = metadataEntry.Open())
                archiveManifest = JsonSerializer.Deserialize<WorldArchiveManifest>(metadataStream, WorldStore.Json);

            if (archiveManifest is null || archiveManifest.ArchiveVersion != ArchiveVersion
                    || archiveManifest.World is null || archiveManifest.Files is null)
                throw new InvalidDataException("The archive manifest is invalid or uses an unsupported archive version.");
            ValidateManifestMetadata(archiveManifest.World);

            if (archiveManifest.Files.Count == 0 || archiveManifest.Files.Count != entries.Count - 1)
                throw new InvalidDataException("The archive file index does not match the ZIP contents.");

            Dictionary<string, WorldArchiveFile> indexedFiles = new(StringComparer.Ordinal);
            long totalBytes = 0;
            foreach (WorldArchiveFile record in archiveManifest.Files)
            {
                if (record is null || record.Path is not { Length: > 0 }
                        || record.Sha256 is not { Length: 64 } sha256)
                    throw new InvalidDataException("The archive manifest contains a file record with missing path or SHA-256 metadata.");
                ValidateRelativePath(record.Path, allowWorldPrefix: true);
                if (!record.Path.StartsWith(WorldPrefix, StringComparison.Ordinal)
                        || record.Length < 0 || record.Length > MaxFileBytes
                        || sha256.Any(c => !Uri.IsHexDigit(c))
                        || !indexedFiles.TryAdd(record.Path, record))
                    throw new InvalidDataException($"The archive manifest contains an invalid file record: {record.Path}");
                totalBytes = checked(totalBytes + record.Length);
                if (totalBytes > MaxExpandedBytes)
                    throw new InvalidDataException("The archive expands beyond the supported size limit.");
                if (!entries.TryGetValue(record.Path, out ZipArchiveEntry? entry)
                        || entry.Length != record.Length)
                    throw new InvalidDataException($"An archive file is missing or has an unexpected size: {record.Path}");
            }

            foreach (string name in entries.Keys)
            {
                if (name != ArchiveManifestName && !indexedFiles.ContainsKey(name))
                    throw new InvalidDataException($"The archive contains an unlisted file: {name}");
            }
            if (!indexedFiles.ContainsKey(WorldPrefix + "manifest.json"))
                throw new InvalidDataException("The archive does not contain a world manifest.");

            long completed = 0;
            foreach (WorldArchiveFile record in archiveManifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ZipArchiveEntry entry = entries[record.Path];
                string relative = record.Path[WorldPrefix.Length..];
                string destination = Path.GetFullPath(Path.Combine(stagingWorld,
                        relative.Replace('/', Path.DirectorySeparatorChar)));
                EnsureContained(stagingWorld, destination);
                string? parent = Path.GetDirectoryName(destination);
                if (string.IsNullOrEmpty(parent))
                    throw new InvalidDataException($"Invalid archive path: {record.Path}");
                Directory.CreateDirectory(parent);

                using Stream input = entry.Open();
                using FileStream output = new(destination, FileMode.CreateNew, IoFileAccess.Write,
                        FileShare.None, BufferSize, FileOptions.SequentialScan);
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[BufferSize];
                long entryBytes = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entryBytes += read;
                    if (entryBytes > record.Length || entryBytes > MaxFileBytes)
                        throw new InvalidDataException($"An archive entry exceeded its declared size: {record.Path}");
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    completed += read;
                    progress?.Report(new WorldArchiveProgress("Importing world", completed, totalBytes));
                }
                output.Flush(true);

                string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (entryBytes != record.Length || !string.Equals(actualHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Checksum verification failed for {record.Path}.");
            }

            bool recovered = ValidateAndRecoverStagedWorld(stagingWorld, archiveManifest.World);
            string importedName = FindUniqueWorldName(root, archiveManifest.World.Name);
            if (!string.Equals(importedName, archiveManifest.World.Name, StringComparison.Ordinal))
            {
                WorldManifest renamed = archiveManifest.World with { Name = importedName };
                File.WriteAllText(Path.Combine(stagingWorld, "manifest.json"),
                        JsonSerializer.Serialize(renamed, WorldStore.Json));
            }

            string id;
            string target;
            do
            {
                id = "w" + Guid.NewGuid().ToString("N")[..12];
                target = Path.Combine(root, id);
            }
            while (Directory.Exists(target) || File.Exists(target));

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingWorld, target);
            progress?.Report(new WorldArchiveProgress("Import complete", totalBytes, totalBytes));
            return new WorldImportResult(WorldStore.WorldsRoot + "/" + id, importedName, recovered);
        }
        catch (InvalidDataException) { throw; }
        catch (InvalidOperationException e)
        {
            throw new InvalidDataException("The ZIP archive is malformed or incomplete.", e);
        }
        catch (IOException) { throw; }
        catch (JsonException e)
        {
            throw new InvalidDataException("The archive contains invalid JSON metadata or save data.", e);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(directory);
            }
            foreach (string file in Directory.EnumerateFiles(current))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                    yield return file;
            }
        }
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        ValidateRelativePath(entry.FullName, allowWorldPrefix: true);
        if (entry.Name.Length == 0)
            throw new InvalidDataException("Directory entries are not allowed in world archives.");
        int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType != 0 && unixType != 0x8000)
            throw new InvalidDataException($"Links and special files are not allowed: {entry.FullName}");
        if (entry.Length < 0 || entry.Length > MaxFileBytes)
            throw new InvalidDataException($"Archive entry exceeds the supported size: {entry.FullName}");
    }

    private static void ValidateRelativePath(string path, bool allowWorldPrefix)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains('\0')
                || path.StartsWith('/') || Path.IsPathRooted(path) || path.Contains(':'))
            throw new InvalidDataException($"Unsafe archive path: {path}");
        string[] parts = path.Split('/');
        if (parts.Any(part => part.Length == 0 || part == "." || part == ".."))
            throw new InvalidDataException($"Unsafe archive path: {path}");
        if (!allowWorldPrefix && parts.Length > 0 && parts[0] == "world")
            throw new InvalidDataException($"Unexpected reserved path: {path}");
    }

    private static void EnsureContained(string root, string candidate)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("The archive path resolves outside the staging directory.");
    }

    private static WorldManifest? ReadBestManifest(string directory)
    {
        foreach (string suffix in new[] { "", ".bak", ".bak2" })
        {
            string path = Path.Combine(directory, "manifest.json" + suffix);
            if (!File.Exists(path)) continue;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    continue;
                string json = File.ReadAllText(path);
                if (!WorldStore.IsValidBackupModel("manifest.json", json))
                    continue;
                WorldManifest? value = JsonSerializer.Deserialize<WorldManifest>(json, WorldStore.ManifestJson);
                if (value is not null) return value;
            }
            catch (JsonException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private static void ValidateManifestMetadata(WorldManifest manifest)
    {
        string json = JsonSerializer.Serialize(manifest, WorldStore.ManifestJson);
        if (!WorldStore.IsValidBackupModel("manifest.json", json))
            throw new InvalidDataException("The world manifest has invalid metadata or an unsupported generator version.");
    }

    private static bool ValidateAndRecoverStagedWorld(string worldDirectory, WorldManifest archivedManifest)
    {
        WorldManifest? stagedManifest = ReadBestManifest(worldDirectory);
        if (stagedManifest is null)
            throw new InvalidDataException("The imported world manifest is corrupt and has no valid backup.");
        ValidateManifestMetadata(stagedManifest);
        if (!ManifestsMatch(stagedManifest, archivedManifest))
            throw new InvalidDataException("The world manifest does not match the archive metadata.");

        bool recovered = !IsValidManifestFile(Path.Combine(worldDirectory, "manifest.json"));
        if (recovered)
            File.WriteAllText(Path.Combine(worldDirectory, "manifest.json"),
                    JsonSerializer.Serialize(archivedManifest, WorldStore.Json));

        recovered |= ValidateSaveCandidates(worldDirectory, "world.json", isWorldSave: true);
        recovered |= ValidateSaveCandidates(worldDirectory, "player.json", isWorldSave: false);
        return recovered;
    }

    private static bool IsValidManifestFile(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            WorldManifest? value = JsonSerializer.Deserialize<WorldManifest>(File.ReadAllText(path), WorldStore.Json);
            return value is not null;
        }
        catch (JsonException) { return false; }
    }

    private static bool ValidateSaveCandidates(string directory, string fileName, bool isWorldSave)
    {
        bool found = false;
        string[] candidates = [fileName, fileName + ".bak", fileName + ".bak2"];
        for (int i = 0; i < candidates.Length; i++)
        {
            string path = Path.Combine(directory, candidates[i]);
            if (!File.Exists(path)) continue;
            found = true;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    continue;
                string json = File.ReadAllText(path);
                string baseName = isWorldSave ? "world.json" : "player.json";
                if (!WorldStore.IsValidBackupModel(baseName, json))
                    continue;
                return i > 0;
            }
            catch (JsonException) { }
            catch (UnauthorizedAccessException e)
            {
                throw new InvalidDataException($"The imported {fileName} candidate cannot be read.", e);
            }
        }

        if (found)
            throw new InvalidDataException($"The imported {fileName} and its backups are corrupt or incompatible.");
        return false; // A never-played world may not have created its first save yet.
    }

    private static bool ManifestsMatch(WorldManifest a, WorldManifest b) =>
        a.Name == b.Name && a.Seed == b.Seed && a.GenVersion == b.GenVersion
        && a.CreatedUtc == b.CreatedUtc && a.PlayedUtc == b.PlayedUtc
        && a.Mode == b.Mode && a.Preset == b.Preset
        && a.PlaytimeSeconds == b.PlaytimeSeconds
        && a.HasSavedData == b.HasSavedData
        && a.NoiseSeeds.SequenceEqual(b.NoiseSeeds);

    private static string FindUniqueWorldName(string root, string requested)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path)) continue;
            try
            {
                WorldManifest? manifest = JsonSerializer.Deserialize<WorldManifest>(File.ReadAllText(path), WorldStore.Json);
                if (!string.IsNullOrWhiteSpace(manifest?.Name))
                    names.Add(manifest.Name);
            }
            catch (JsonException) { }
        }

        if (!names.Contains(requested)) return requested;
        string imported = requested + " (Imported)";
        if (!names.Contains(imported)) return imported;
        for (int suffix = 2; ; suffix++)
        {
            string candidate = requested + $" (Imported {suffix})";
            if (!names.Contains(candidate)) return candidate;
        }
    }
}

public sealed record WorldBackupInfo(
    string Path,
    string FileName,
    long ModifiedUtc,
    long SizeBytes,
    bool IsValid,
    string Status);

public sealed record WorldBackupRestoreResult(bool Success, string Message);

/// <summary>Lists and restores the atomic snapshots produced by WorldStore.WriteAtomic.</summary>
public static class WorldBackupStore
{
    private static readonly string[] SaveFiles = ["manifest.json", "world.json", "player.json"];

    public static string? LastListError { get; private set; }
    internal static Func<string, string[]>? EnumerateBackupPathsForTests;
    internal static Func<string, string>? ReadBackupTextForTests;

    public static List<WorldBackupInfo> List(string worldDirectory)
    {
        List<WorldBackupInfo> result = [];
        LastListError = null;
        string absolute = ProjectSettings.GlobalizePath(worldDirectory);
        string[] paths;
        try
        {
            if (!Directory.Exists(absolute))
            {
                try { _ = File.GetAttributes(absolute); }
                catch (DirectoryNotFoundException) { return result; }
                catch (FileNotFoundException) { return result; }
            }
            paths = EnumerateBackupPathsForTests?.Invoke(absolute)
                    ?? Directory.EnumerateFiles(absolute, "*.bak*").ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            LastListError = $"Backups could not be listed: {e.Message}";
            return result;
        }

        foreach (string path in paths)
        {
            string name = System.IO.Path.GetFileName(path);
            string baseName = name.EndsWith(".bak2", StringComparison.Ordinal)
                    ? name[..^5] : name.EndsWith(".bak", StringComparison.Ordinal) ? name[..^4] : "";
            if (!SaveFiles.Contains(baseName, StringComparer.Ordinal)) continue;

            try
            {
                bool valid = ValidateBackup(path, baseName, out string status);
                FileInfo info = new(path);
                result.Add(new WorldBackupInfo(
                        worldDirectory.TrimEnd('/') + "/" + name,
                        name,
                        new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                        info.Length,
                        valid,
                        status));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                result.Add(new WorldBackupInfo(
                        worldDirectory.TrimEnd('/') + "/" + name,
                        name, 0, 0, false, $"Unavailable: {e.Message}"));
            }
        }

        result.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));
        return result;
    }

    public static WorldBackupRestoreResult Restore(string worldDirectory, string backupPath)
    {
        WorldBackupInfo? selected = List(worldDirectory).Find(item => item.Path == backupPath);
        if (selected is null)
            return new WorldBackupRestoreResult(false, LastListError
                    ?? "The selected backup no longer exists or could not be listed.");
        if (!selected.IsValid)
            return new WorldBackupRestoreResult(false, selected.Status);

        string baseName = selected.FileName.EndsWith(".bak2", StringComparison.Ordinal)
                ? selected.FileName[..^5] : selected.FileName[..^4];
        if (!SaveFiles.Contains(baseName, StringComparer.Ordinal))
            return new WorldBackupRestoreResult(false, "The selected file is not a recognized world backup.");

        string target = worldDirectory.TrimEnd('/') + "/" + baseName;
        string content;
        try { content = File.ReadAllText(ProjectSettings.GlobalizePath(selected.Path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            return new WorldBackupRestoreResult(false, $"The backup could not be read: {e.Message}");
        }

        if (!WorldStore.WriteAtomic(target, content, allowRecovery: true))
            return new WorldBackupRestoreResult(false,
                    "Restore failed. The current save was left in place; check available disk space and permissions.");

        WorldStore.TryClearRecoveryRequired(worldDirectory);
        return new WorldBackupRestoreResult(true,
                $"Restored {baseName}. The previous current file was retained as a safety backup.");
    }

    private static bool ValidateBackup(string path, string baseName, out string status)
    {
        try
        {
            string json = ReadBackupTextForTests?.Invoke(path)
                    ?? File.ReadAllText(path);
            bool valid = WorldStore.IsValidBackupModel(baseName, json);
            status = valid ? "Valid restore point" : "Corrupt or unsupported backup";
            return valid;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            status = $"Unavailable: {e.Message}";
            return false;
        }
    }
}
