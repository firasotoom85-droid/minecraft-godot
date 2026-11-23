using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Godot;

namespace Minecraft;

public static class WorldArchiveTests
{
    public static void Run()
    {
        string testRoot = $"user://tests/world-archive-{Time.GetTicksUsec()}";
        string absoluteRoot = ProjectSettings.GlobalizePath(testRoot);
        Directory.CreateDirectory(absoluteRoot);

        string? sourceWorld = null;
        string? importedOne = null;
        string? importedTwo = null;
        string? backupWorld = null;
        try
        {
            sourceWorld = WorldStore.Create("Portable Test", 8675309);
            WorldManifest sourceManifest = WorldStore.ReadManifest(sourceWorld)!;
            sourceManifest = sourceManifest with { PlaytimeSeconds = 3725 };
            TestAssert.Check(WorldStore.WriteManifest(sourceWorld, sourceManifest),
                    "playtime metadata writes atomically");

            WorldSave legacyV2 = new(2,
                    new Dictionary<string, Dictionary<string, BlockId>>
                    {
                        ["0,0"] = new Dictionary<string, BlockId> { ["7"] = BlockId.Stone },
                    },
                    0.375);
            TestAssert.Check(WorldStore.WriteAtomic(WorldStore.WorldSavePath(sourceWorld),
                    JsonSerializer.Serialize(legacyV2, WorldStore.Json)),
                    "legacy v2 test save writes");

            PlayerSave player = new([1.0f, 2.0f, 3.0f], Selected: 2, Experience: 14);
            TestAssert.Check(WorldStore.WriteAtomic(WorldStore.PlayerSavePath(sourceWorld),
                    JsonSerializer.Serialize(player, WorldStore.Json)),
                    "player data writes before export");

            byte[] largeData = new byte[4 * 1024 * 1024];
            for (int i = 0; i < largeData.Length; i++)
                largeData[i] = (byte)(i * 31);
            File.WriteAllBytes(Path.Combine(ProjectSettings.GlobalizePath(sourceWorld), "large-region.bin"), largeData);

            string archive = Path.Combine(absoluteRoot, "portable-world.zip");
            WorldArchive.Export(ProjectSettings.GlobalizePath(sourceWorld), archive);
            TestAssert.Check(File.Exists(archive) && new FileInfo(archive).Length > 0,
                    "large world archive exports to a completed ZIP");

            string worldsRoot = ProjectSettings.GlobalizePath(WorldStore.WorldsRoot);
            WorldImportResult first = WorldArchive.Import(archive, worldsRoot);
            importedOne = first.WorldDirectory;
            WorldImportResult second = WorldArchive.Import(archive, worldsRoot);
            importedTwo = second.WorldDirectory;

            TestAssert.Check(importedOne != importedTwo, "duplicate imports receive different world identities");
            TestAssert.Equal("Portable Test (Imported)", WorldStore.ReadManifest(importedOne)?.Name,
                    "first import receives a unique name while the source world exists");
            TestAssert.Equal("Portable Test (Imported 2)", WorldStore.ReadManifest(importedTwo)?.Name,
                    "repeated import receives a distinct name without overwriting prior worlds");
            TestAssert.Equal(8675309, WorldStore.ReadManifest(importedOne)?.Seed ?? 0,
                    "import preserves the world seed");
            TestAssert.Equal(3725L, WorldStore.ReadManifest(importedOne)?.PlaytimeSeconds ?? -1,
                    "import preserves cumulative playtime metadata");
            WorldSave? importedSave = WorldStore.ReadJson<WorldSave>(
                    WorldStore.WorldSavePath(importedOne), WorldStore.Json);
            TestAssert.Equal(2, importedSave?.Version ?? -1,
                    "import accepts and preserves a supported legacy v2 world save");
            TestAssert.Equal(0.375, importedSave?.Time ?? -1.0,
                    "legacy save data survives archive round-trip");
            TestAssert.Equal(largeData.Length,
                    new FileInfo(Path.Combine(ProjectSettings.GlobalizePath(importedOne), "large-region.bin")).Length,
                    "large archive entry is fully extracted");

            string corruptArchive = Path.Combine(absoluteRoot, "corrupt.zip");
            File.WriteAllText(corruptArchive, "not a ZIP archive");
            bool corruptRejected = false;
            try { WorldArchive.Import(corruptArchive, worldsRoot); }
            catch (InvalidDataException) { corruptRejected = true; }
            catch (IOException) { corruptRejected = true; }
            TestAssert.Check(corruptRejected, "corrupt ZIP archive is rejected without publishing a world");

            string traversalArchive = Path.Combine(absoluteRoot, "traversal.zip");
            using (FileStream stream = new(traversalArchive, FileMode.CreateNew))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry metadata = zip.CreateEntry(WorldArchive.ArchiveManifestName);
                using (StreamWriter writer = new(metadata.Open()))
                    writer.Write("{}");
                ZipArchiveEntry malicious = zip.CreateEntry("../outside.txt");
                using (StreamWriter writer = new(malicious.Open()))
                    writer.Write("must not escape staging");
            }

            bool traversalRejected = false;
            try { WorldArchive.Import(traversalArchive, worldsRoot); }
            catch (InvalidDataException) { traversalRejected = true; }
            TestAssert.Check(traversalRejected, "archive path traversal is rejected");
            TestAssert.Check(!File.Exists(Path.Combine(worldsRoot, "outside.txt")),
                    "path traversal does not write outside the staging folder");

            backupWorld = WorldStore.Create("Backup Restore Test", 1234);
            string backupSavePath = WorldStore.WorldSavePath(backupWorld);
            WorldSave before = new(WorldStore.WorldFormatVersion, null, 0.25);
            WorldSave after = new(WorldStore.WorldFormatVersion, null, 0.875);
            TestAssert.Check(WorldStore.WriteAtomic(backupSavePath,
                    JsonSerializer.Serialize(before, WorldStore.Json)), "initial restore point writes");
            TestAssert.Check(WorldStore.WriteAtomic(backupSavePath,
                    JsonSerializer.Serialize(after, WorldStore.Json)), "newer save rotates the previous version");
            List<WorldBackupInfo> backups = WorldBackupStore.List(backupWorld);
            WorldBackupInfo? selected = backups.Find(item => item.FileName == "world.json.bak" && item.IsValid);
            TestAssert.Check(selected is not null, "backup browser lists dated, validated restore points");
            if (selected is not null)
            {
                WorldBackupRestoreResult restored = WorldBackupStore.Restore(backupWorld, selected.Path);
                TestAssert.Check(restored.Success, "selected backup restores successfully");
                WorldSave? restoredSave = WorldStore.ReadJson<WorldSave>(backupSavePath, WorldStore.Json);
                TestAssert.Equal(0.25, restoredSave?.Time ?? -1.0,
                        "restore replaces the current save with the selected snapshot");
                WorldSave? safety = WorldStore.ReadJson<WorldSave>(backupSavePath + ".bak", WorldStore.Json);
                TestAssert.Equal(0.875, safety?.Time ?? -1.0,
                        "pre-restore current save is retained as a safety backup");
            }

            // Reject a null SHA-256 field through the importer's normal
            // invalid-data path before any destination is published.
            string nullHashArchive = Path.Combine(absoluteRoot, "null-hash.zip");
            using (FileStream stream = new(nullHashArchive, FileMode.CreateNew))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry metadata = zip.CreateEntry(WorldArchive.ArchiveManifestName);
                using (StreamWriter writer = new(metadata.Open()))
                    writer.Write("""{"archiveVersion":1,"exportedUtc":1,"world":{"name":"Bad","seed":1,"genVersion":1,"noiseSeeds":[1,2,3,4,5],"createdUtc":1,"playedUtc":1},"files":[{"path":"world/manifest.json","length":0,"sha256":null}]}""");
                zip.CreateEntry("world/manifest.json");
            }
            int worldsBeforeNullHash = Directory.GetDirectories(worldsRoot).Length;
            bool nullHashRejected = false;
            try { WorldArchive.Import(nullHashArchive, worldsRoot); }
            catch (InvalidDataException) { nullHashRejected = true; }
            TestAssert.Check(nullHashRejected,
                    "an archive record with sha256:null is rejected as invalid archive metadata");
            TestAssert.Equal(worldsBeforeNullHash, Directory.GetDirectories(worldsRoot).Length,
                    "invalid null-hash metadata does not publish an import destination");

            // One unreadable restore point must not hide valid neighboring files.
            WorldBackupStore.ReadBackupTextForTests = path =>
            {
                if (path.EndsWith("world.json.bak2", StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("simulated backup read denial");
                return File.ReadAllText(path);
            };
            List<WorldBackupInfo> partialBackups;
            try { partialBackups = WorldBackupStore.List(backupWorld); }
            finally { WorldBackupStore.ReadBackupTextForTests = null; }
            TestAssert.Check(partialBackups.Exists(item => item.FileName == "world.json.bak" && item.IsValid),
                    "an inaccessible backup does not hide other valid restore points");
            TestAssert.Check(partialBackups.Exists(item => item.FileName == "world.json.bak2"
                    && !item.IsValid && item.Status.StartsWith("Unavailable:", StringComparison.Ordinal)),
                    "an unreadable backup is displayed as unavailable instead of aborting the list");

            WorldBackupStore.EnumerateBackupPathsForTests = _ =>
                    throw new UnauthorizedAccessException("simulated backup directory denial");
            List<WorldBackupInfo> inaccessibleBackups;
            try { inaccessibleBackups = WorldBackupStore.List(backupWorld); }
            finally { WorldBackupStore.EnumerateBackupPathsForTests = null; }
            TestAssert.Check(inaccessibleBackups.Count == 0 && WorldBackupStore.LastListError is not null,
                    "backup-directory access errors are surfaced without escaping the list operation");

            WorldStore.RequireRecovery(backupWorld);
            TestAssert.Check(!WorldStore.WriteAtomic(backupSavePath,
                    JsonSerializer.Serialize(after, WorldStore.Json)),
                    "ordinary writes are blocked while a world is awaiting recovery");
            WorldBackupInfo? recoveryPoint = WorldBackupStore.List(backupWorld)
                    .Find(item => item.FileName == "world.json.bak" && item.IsValid);
            TestAssert.Check(recoveryPoint is not null, "a valid backup remains selectable in recovery mode");
            if (recoveryPoint is not null)
            {
                WorldBackupRestoreResult recoveryRestore = WorldBackupStore.Restore(
                        backupWorld, recoveryPoint.Path);
                TestAssert.Check(recoveryRestore.Success,
                        "explicit backup restore is allowed for a recovery-required world");
                TestAssert.Check(!WorldStore.IsRecoveryRequired(backupWorld),
                        "recovery clears only after all required save models validate");
            }

            WorldManifest? oldManifest = JsonSerializer.Deserialize<WorldManifest>(
                    "{\"name\":\"Old\",\"seed\":1,\"genVersion\":1,\"noiseSeeds\":[1,2,3,4,5],\"createdUtc\":1,\"playedUtc\":1}",
                    WorldStore.Json);
            TestAssert.Equal(0L, oldManifest?.PlaytimeSeconds ?? -1,
                    "legacy manifests default missing playtime metadata to zero");
        }
        finally
        {
            if (backupWorld is not null) WorldStore.Delete(backupWorld);
            if (importedOne is not null) WorldStore.Delete(importedOne);
            if (importedTwo is not null) WorldStore.Delete(importedTwo);
            if (sourceWorld is not null) WorldStore.Delete(sourceWorld);
            if (Directory.Exists(absoluteRoot))
                Directory.Delete(absoluteRoot, recursive: true);
        }
    }
}
