using System;
using System.Collections.Generic;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;
using System.Text.Json;
using Godot;

namespace Minecraft;

public static class SaveTests
{
    public static void Run()
    {
        string root = $"user://tests/save-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(root);

        Dictionary<string, Dictionary<string, BlockId>> edits = new()
        {
            ["0,0"] = new Dictionary<string, BlockId>
            {
                ["123"] = BlockId.Stone,
                ["124"] = BlockId.PlanksSlab,
            },
        };
        Dictionary<string, List<ItemStackSave>> containers = new()
        {
            ["1,2,3"] = [new ItemStackSave(0, (int)ItemId.Stone, 12)],
        };
        Dictionary<string, bool> flags = new() { ["dungeon/ember"] = true };
        Dictionary<string, Dictionary<string, byte>> blockStates = new()
        {
            ["0,0"] = new Dictionary<string, byte> { ["124"] = BlockShapes.SlabTop },
        };
        WorldSave save = new(WorldStore.WorldFormatVersion, edits, 0.625, containers, flags,
                BlockStates: blockStates);
        string path = root + "/world.json";

        PlayerSave emptyHungerSave = new([0.0f, 0.0f, 0.0f], 0, null, "survival", 20.0f, 0.0f);
        string emptyHungerJson = JsonSerializer.Serialize(emptyHungerSave, WorldStore.Json);
        PlayerSave? emptyHungerReload = JsonSerializer.Deserialize<PlayerSave>(emptyHungerJson, WorldStore.Json);
        TestAssert.Equal(0.0f, emptyHungerReload?.Hunger ?? -1.0f,
                "zero hunger survives player-save serialization");
        PlayerSave? legacyPlayer = JsonSerializer.Deserialize<PlayerSave>("{\"pos\":[0,0,0]}", WorldStore.Json);
        TestAssert.Check(legacyPlayer?.Hunger is null,
                "old player saves distinguish a missing hunger field");
        TestAssert.Check(legacyPlayer?.Yaw is null && legacyPlayer?.Pitch is null,
                "legacy player saves without orientation fields remain valid");
        TestAssert.Check(legacyPlayer is { Dead: false },
                "legacy player saves without a death field remain alive by default");
        PlayerStats legacyStats = new();
        legacyStats.Load(legacyPlayer?.Health ?? 0.0f, legacyPlayer?.Hunger ?? -1.0f,
                legacyPlayer?.Experience ?? 0, legacyPlayer?.Dead ?? false);
        TestAssert.Check(!legacyStats.Dead && legacyStats.Health == PlayerStats.MaxHealth,
                "legacy zero-health sentinel still restores as a healthy player");
        PlayerStats livingStats = new();
        livingStats.Load(13.5f, 6.0f, 24, dead: false);
        TestAssert.Check(!livingStats.Dead && livingStats.Health == 13.5f,
                "living player saves restore their persisted health without a death state");

        PlayerSave deadSave = new([3.0f, 20.0f, 4.0f], Mode: "survival", Health: 0.0f,
                Hunger: 7.0f, Respawn: [10.5f, 65.0f, -2.5f], Experience: 120, Dead: true);
        PlayerSave? deadReload = JsonSerializer.Deserialize<PlayerSave>(
                JsonSerializer.Serialize(deadSave, WorldStore.Json), WorldStore.Json);
        TestAssert.Check(deadReload is { Dead: true, Health: 0.0f },
                "explicit player death and zero health survive save round-trip");
        PlayerStats restoredDeadStats = new();
        restoredDeadStats.Load(deadReload?.Health ?? 0.0f, deadReload?.Hunger ?? -1.0f,
                deadReload?.Experience ?? 0, deadReload?.Dead ?? false);
        TestAssert.Check(restoredDeadStats.Dead && restoredDeadStats.Health == 0.0f,
                "explicitly dead player save restores zero health and dead state");

        Player deadPlayer = new();
        deadPlayer.Position = new Vector3(3.0f, 20.0f, 4.0f);
        deadPlayer.LoadFromSave(deadReload?.Mode, deadReload?.Health ?? 0.0f,
                deadReload?.Hunger ?? -1.0f,
                deadReload?.Respawn is { Length: 3 } savedAnchor
                        ? new Vector3(savedAnchor[0], savedAnchor[1], savedAnchor[2]) : null,
                deadReload?.Experience ?? 0, deadReload?.Dead ?? false);
        TestAssert.Check(deadPlayer.Stats.Dead,
                "saved death is pending until the respawn transition is invoked");
        deadPlayer.Respawn();
        TestAssert.Check(!deadPlayer.Stats.Dead && deadPlayer.Stats.Health == PlayerStats.MaxHealth,
                "restored death uses normal full-health respawn rules");
        TestAssert.Equal(new Vector3(10.5f, 65.0f, -2.5f), deadPlayer.Position,
                "restored death respawns at the saved anchor");
        TestAssert.Equal(120, deadPlayer.Stats.Experience,
                "softcore respawn preserves player experience");
        TestAssert.Equal(new Vector3(10.5f, 65.0f, -2.5f), deadPlayer.RespawnPoint ?? Vector3.Zero,
                "softcore respawn keeps the saved anchor");
        deadPlayer.Free();
        PlayerSave orientationSave = new([2.0f, 64.0f, -3.0f],
                Yaw: 1.25f, Pitch: -0.45f);
        PlayerSave? orientationReload = JsonSerializer.Deserialize<PlayerSave>(
                JsonSerializer.Serialize(orientationSave, WorldStore.Json), WorldStore.Json);
        TestAssert.Equal(1.25f, orientationReload?.Yaw ?? float.NaN,
                "player yaw survives optional save serialization");
        TestAssert.Equal(-0.45f, orientationReload?.Pitch ?? float.NaN,
                "camera pitch survives optional save serialization");

        Player orientedPlayer = new();
        Camera3D orientationCamera = new();
        orientedPlayer.Camera = orientationCamera;
        orientedPlayer.RestoreOrientation(orientationReload?.Yaw, orientationReload?.Pitch);
        TestAssert.Equal(1.25f, orientedPlayer.Rotation.Y,
                "saved yaw restores onto the player body");
        TestAssert.Equal(-0.45f, orientedPlayer.Camera.Rotation.X,
                "saved pitch restores onto the camera");
        orientedPlayer.RestoreOrientation(float.NaN, float.PositiveInfinity);
        TestAssert.Equal(1.25f, orientedPlayer.Rotation.Y,
                "non-finite saved yaw is ignored");
        TestAssert.Equal(-0.45f, orientedPlayer.Camera.Rotation.X,
                "non-finite saved pitch is ignored");
        orientedPlayer.Free();
        orientationCamera.Free();
        string previewPlayerPath = root + "/preview-player.json";
        TestAssert.Check(WorldStore.WriteAtomic(previewPlayerPath,
                JsonSerializer.Serialize(new PlayerSave([]), WorldStore.Json)),
                "preview fixture writes a player save without a live Player node");
        TestAssert.Equal(WorldReadStatus.Valid,
                WorldStore.ReadJsonResult<PlayerSave>(previewPlayerPath, WorldStore.Json).Status,
                "an empty position array remains a supported preview/headless save representation");

        WorldSave boatSave = new(WorldStore.WorldFormatVersion, [], 0.5,
                Boats: [new BoatSave([12.5f, 49.7f, -3.5f], 0.75f, [1.0f, 0.0f, -2.0f], true)]);
        WorldSave? boatReload = JsonSerializer.Deserialize<WorldSave>(
                JsonSerializer.Serialize(boatSave, WorldStore.Json), WorldStore.Json);
        TestAssert.Check(boatReload?.Boats is { Count: 1 } && boatReload.Boats[0].Occupied,
                "boat pose and occupied state survive optional world-save serialization");
        const string v3Legacy = """{"version":3,"edits":{},"time":0.5,"containers":{},"flags":{},"furnaces":{}}""";
        WorldSave? preBoatSave = JsonSerializer.Deserialize<WorldSave>(v3Legacy, WorldStore.Json);
        TestAssert.Check(preBoatSave?.Boats is null,
                "world saves written before boats load without a vehicle list");
        PlayerSave progressionSave = new([0.0f, 0.0f, 0.0f], Experience: 245);
        PlayerSave? progressionReload = JsonSerializer.Deserialize<PlayerSave>(JsonSerializer.Serialize(progressionSave, WorldStore.Json), WorldStore.Json);
        TestAssert.Equal(245, progressionReload?.Experience ?? -1, "experience survives player-save round-trip");
        TestAssert.Equal(0, legacyPlayer?.Experience ?? -1, "legacy player save defaults experience to zero");
        ItemStackSave enchantedSlot = new(0, (int)ItemId.IronSword, 1, 120, Sharpness: 2, Unbreaking: 3);
        string enchantedJson = JsonSerializer.Serialize(enchantedSlot, WorldStore.Json);
        ItemStackSave? enchantedReload = JsonSerializer.Deserialize<ItemStackSave>(enchantedJson, WorldStore.Json);
        TestAssert.Equal(2, enchantedReload?.Sharpness ?? 0, "saved sharpness level round-trips");
        TestAssert.Equal(3, enchantedReload?.Unbreaking ?? 0, "saved unbreaking level round-trips");
        TestAssert.Check(WorldStore.WriteAtomic(path, JsonSerializer.Serialize(save, WorldStore.Json)),
                "world save writes atomically");
        TestAssert.Check(!WorldStore.WriteAtomic(root + "/missing/save.json", "unwritable"),
                "atomic write reports a missing parent directory instead of claiming success");

        WorldSave? loaded = WorldStore.ReadJson<WorldSave>(path, WorldStore.Json);
        TestAssert.Check(loaded is not null, "world save reads back");
        if (loaded is not null)
        {
            TestAssert.Equal(WorldStore.WorldFormatVersion, loaded.Version, "save version round-trips");
            TestAssert.Equal(0.625, loaded.Time, "world time round-trips");
            TestAssert.Equal(BlockId.Stone, loaded.Edits?["0,0"]["123"], "edits round-trip");
            TestAssert.Equal(12, loaded.Containers?["1,2,3"][0].Count, "containers round-trip");
            TestAssert.Check(loaded.Flags?["dungeon/ember"] == true, "flags round-trip");
            TestAssert.Equal(BlockShapes.SlabTop, loaded.BlockStates?["0,0"]["124"] ?? 0,
                    "optional slab block state survives save round-trip");
        }

        string backupPath = root + "/backup.json";
        WorldStore.WriteAtomic(backupPath, "one");
        WorldStore.WriteAtomic(backupPath, "two");
        WorldStore.WriteAtomic(backupPath, "three");
        TestAssert.Equal("three", FileAccess.GetFileAsString(backupPath), "newest save is current");
        TestAssert.Equal("two", FileAccess.GetFileAsString(backupPath + ".bak"), "first backup is previous save");
        TestAssert.Equal("one", FileAccess.GetFileAsString(backupPath + ".bak2"), "second backup is oldest save");

        string rotationRoot = Path.Combine(Path.GetTempPath(),
                $"minecraft-save-rotation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rotationRoot);
        try
        {
            string workerRotationPath = Path.Combine(rotationRoot, "rotation.json");
            TestAssert.Check(WorldSaveWorker.WriteAtomic(workerRotationPath, "generation-1", out _),
                    "backup-rotation fixture writes its first generation");
            TestAssert.Check(WorldSaveWorker.WriteAtomic(workerRotationPath, "generation-2", out _),
                    "backup-rotation fixture writes its second generation");
            TestAssert.Check(WorldSaveWorker.WriteAtomic(workerRotationPath, "generation-3", out _),
                    "backup-rotation fixture establishes primary and two backups");

            string primaryBeforeFailure = Convert.ToHexString(File.ReadAllBytes(workerRotationPath));
            string backupBeforeFailure = Convert.ToHexString(File.ReadAllBytes(workerRotationPath + ".bak"));
            string oldestBeforeFailure = Convert.ToHexString(File.ReadAllBytes(workerRotationPath + ".bak2"));
            bool failedRotation = WorldSaveWorker.WriteAtomic(
                    workerRotationPath,
                    "generation-4",
                    out string? rotationError,
                    beforeCurrentToBackup: () => throw new System.IO.IOException(
                            "injected current-primary-to-backup rotation failure"));
            TestAssert.Check(!failedRotation && !string.IsNullOrEmpty(rotationError),
                    "atomic writer reports an injected failure before moving the current primary to .bak");
            TestAssert.Equal(primaryBeforeFailure,
                    Convert.ToHexString(File.ReadAllBytes(workerRotationPath)),
                    "rotation failure preserves the previous primary byte-for-byte");
            TestAssert.Equal(backupBeforeFailure,
                    Convert.ToHexString(File.ReadAllBytes(workerRotationPath + ".bak")),
                    "rotation failure preserves the previous backup byte-for-byte");
            TestAssert.Equal(oldestBeforeFailure,
                    Convert.ToHexString(File.ReadAllBytes(workerRotationPath + ".bak2")),
                    "rotation failure preserves the oldest backup byte-for-byte");
            TestAssert.Check(!File.Exists(workerRotationPath + ".tmp"),
                    "failed rotation removes its temporary file");
            TestAssert.Equal(0,
                    Directory.GetFiles(rotationRoot, "rotation.json.bak2.stage-*").Length,
                    "failed rotation restores and removes the staged oldest backup");

            bool failedPublish = WorldSaveWorker.WriteAtomic(
                    workerRotationPath,
                    "generation-4",
                    out string? publishError,
                    beforePublish: () => throw new System.IO.IOException(
                            "injected final temp-to-primary publish failure"));

            TestAssert.Check(!failedPublish && !string.IsNullOrEmpty(publishError),
                    "atomic writer reports an injected failure at the final publish boundary");
            TestAssert.Equal(primaryBeforeFailure,
                    Convert.ToHexString(File.ReadAllBytes(workerRotationPath)),
                    "final publish failure restores the previous primary byte-for-byte");
            TestAssert.Equal(backupBeforeFailure,
                    Convert.ToHexString(File.ReadAllBytes(workerRotationPath + ".bak")),
                    "final publish failure restores the previous backup byte-for-byte");
            TestAssert.Equal(oldestBeforeFailure,
                    Convert.ToHexString(File.ReadAllBytes(workerRotationPath + ".bak2")),
                    "final publish failure restores the oldest backup byte-for-byte");
            TestAssert.Check(!File.Exists(workerRotationPath + ".tmp"),
                    "failed final publish removes its temporary file");
            TestAssert.Equal(0,
                    Directory.GetFiles(rotationRoot, "rotation.json.bak2.stage-*").Length,
                    "successful rollback leaves no staged oldest-backup file");

            TestAssert.Check(WorldSaveWorker.WriteAtomic(workerRotationPath, "generation-4", out _),
                    "backup rotation succeeds after the injected publish failure");
            TestAssert.Equal("generation-4", File.ReadAllText(workerRotationPath),
                    "successful rotation publishes the new primary");
            TestAssert.Equal("generation-3", File.ReadAllText(workerRotationPath + ".bak"),
                    "successful rotation moves the previous primary to .bak");
            TestAssert.Equal("generation-2", File.ReadAllText(workerRotationPath + ".bak2"),
                    "successful rotation advances the previous .bak to .bak2");
            TestAssert.Equal(0,
                    Directory.GetFiles(rotationRoot, "rotation.json.bak2.stage-*").Length,
                    "successful commit removes the staged oldest-backup file");
        }
        finally
        {
            Directory.Delete(rotationRoot, recursive: true);
        }

        string workerRoot = Path.Combine(Path.GetTempPath(),
                $"minecraft-save-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workerRoot);
        try
        {
            string workerWorldPath = Path.Combine(workerRoot, "world.json");
            string workerPlayerPath = Path.Combine(workerRoot, "player.json");
            string workerManifestPath = Path.Combine(workerRoot, "manifest.json");
            WorldManifest workerManifest = new(
                    "Background Save Test", 24680, WorldStore.CurrentGenVersion,
                    WorldStore.DeriveNoiseSeeds(24680), 1, 1, HasSavedData: true);
            WorldSaveSnapshot workerSnapshot = new(
                    new WorldSave(WorldStore.WorldFormatVersion, [], 0.25),
                    new PlayerSave([1.0f, 2.0f, 3.0f], Health: 16.0f),
                    workerManifest,
                    workerWorldPath,
                    workerPlayerPath,
                    workerManifestPath,
                    DirtyRevision: 1,
                    ElapsedPlaytimeSeconds: 5.0);
            WorldSaveWorkerResult firstWorkerSave =
                    WorldSaveWorker.WriteAsync(workerSnapshot).GetAwaiter().GetResult();
            TestAssert.Check(firstWorkerSave.Success,
                    "background worker writes world, player and manifest snapshots");
            WorldSave? firstWorkerWorld =
                    JsonSerializer.Deserialize<WorldSave>(File.ReadAllText(workerWorldPath), WorldStore.Json);
            TestAssert.Equal(0.25, firstWorkerWorld?.Time ?? -1.0,
                    "background worker serializes world data before the atomic write");
            WorldManifest? firstWorkerManifest = JsonSerializer.Deserialize<WorldManifest>(
                    File.ReadAllText(workerManifestPath), WorldStore.ManifestJson);
            TestAssert.Check(firstWorkerManifest is { HasSavedData: true },
                    "background worker persists the save-history marker with its completed pair");

            WorldSaveSnapshot nextWorkerSnapshot = workerSnapshot with
            {
                World = workerSnapshot.World with { Time = 0.75 },
                Player = workerSnapshot.Player with { Health = 11.0f },
                Manifest = workerSnapshot.Manifest with { PlaytimeSeconds = 10 },
                DirtyRevision = 2,
                ElapsedPlaytimeSeconds = 5.0,
            };
            WorldSaveWorkerResult secondWorkerSave =
                    WorldSaveWorker.WriteAsync(nextWorkerSnapshot).GetAwaiter().GetResult();
            TestAssert.Check(secondWorkerSave.Success,
                    "ordered background worker accepts the next world snapshot");
            WorldSave? currentWorkerWorld =
                    JsonSerializer.Deserialize<WorldSave>(File.ReadAllText(workerWorldPath), WorldStore.Json);
            WorldSave? backupWorkerWorld =
                    JsonSerializer.Deserialize<WorldSave>(File.ReadAllText(workerWorldPath + ".bak"), WorldStore.Json);
            TestAssert.Equal(0.75, currentWorkerWorld?.Time ?? -1.0,
                    "newest background snapshot is committed as current");
            TestAssert.Equal(0.25, backupWorkerWorld?.Time ?? -1.0,
                    "background atomic writes retain the previous valid world snapshot");
            PlayerSave? currentWorkerPlayer =
                    JsonSerializer.Deserialize<PlayerSave>(File.ReadAllText(workerPlayerPath), WorldStore.Json);
            TestAssert.Equal(11.0f, currentWorkerPlayer?.Health ?? -1.0f,
                    "background worker commits the matching player snapshot");

            string protectedPath = Path.Combine(workerRoot, "protected.json");
            File.WriteAllText(protectedPath, "preserve-current");
            Directory.CreateDirectory(protectedPath + ".bak");
            bool protectedWrite = WorldSaveWorker.WriteAtomic(protectedPath, "must-not-replace", out string? protectedError);
            TestAssert.Check(!protectedWrite && !string.IsNullOrEmpty(protectedError),
                    "background atomic writer reports an occupied backup directory");
            TestAssert.Equal("preserve-current", File.ReadAllText(protectedPath),
                    "failed background atomic write preserves the current save");
            TestAssert.Check(!File.Exists(protectedPath + ".tmp"),
                    "failed background atomic write removes its temporary file");

            string incompleteWorldPath = Path.Combine(workerRoot, "incomplete-world.json");
            string incompletePlayerPath = Path.Combine(workerRoot, "incomplete-player.json");
            string incompleteManifestPath = Path.Combine(workerRoot, "incomplete-manifest.json");
            Directory.CreateDirectory(incompleteWorldPath);
            WorldManifest previousManifest = workerManifest with
            {
                HasSavedData = false,
                PlaytimeSeconds = 1,
            };
            File.WriteAllText(incompleteManifestPath,
                    JsonSerializer.Serialize(previousManifest, WorldStore.ManifestJson));
            WorldSaveSnapshot incompletePair = workerSnapshot with
            {
                WorldPath = incompleteWorldPath,
                PlayerPath = incompletePlayerPath,
                ManifestPath = incompleteManifestPath,
                Manifest = workerManifest with { HasSavedData = true },
            };
            WorldSaveWorkerResult incompleteResult =
                    WorldSaveWorker.WriteSynchronously(incompletePair);
            TestAssert.Check(!incompleteResult.Success,
                    "background worker reports a failed world/player save pair");
            WorldManifest? unchangedManifest = JsonSerializer.Deserialize<WorldManifest>(
                    File.ReadAllText(incompleteManifestPath), WorldStore.ManifestJson);
            TestAssert.Check(unchangedManifest is { HasSavedData: false, PlaytimeSeconds: 1 },
                    "failed save pair does not advance save-history metadata");
            TestAssert.Check(File.Exists(incompletePlayerPath),
                    "background worker still attempts the player half after world-write failure");

        }
        finally
        {
            Directory.Delete(workerRoot, recursive: true);
        }

        string corruptPath = root + "/corrupt.json";
        WorldStore.WriteAtomic(corruptPath, JsonSerializer.Serialize(save, WorldStore.Json));
        WorldStore.WriteAtomic(corruptPath, JsonSerializer.Serialize(save with { Time = 0.75 }, WorldStore.Json));
        using (FileAccess? f = FileAccess.Open(corruptPath, FileAccess.ModeFlags.Write))
        {
            f?.StoreString("{ definitely not json");
        }

        WorldSave? recovered = WorldStore.ReadJson<WorldSave>(corruptPath, WorldStore.Json);
        TestAssert.Check(recovered is not null, "corrupt save recovers from backup");
        TestAssert.Equal(0.625, recovered?.Time, "recovered save is the previous valid one");

        const string v2 = "{\"version\":2,\"edits\":{},\"time\":0.5}";
        WorldSave? old = JsonSerializer.Deserialize<WorldSave>(v2, WorldStore.Json);
        TestAssert.Check(old is not null, "v2 save still parses");
        TestAssert.Equal(2, old?.Version, "v2 version is preserved");
        TestAssert.Check(old?.Containers is null, "v2 save has no containers");
        TestAssert.Check(old?.Flags is null, "v2 save has no flags");
        TestAssert.Check(old?.BlockStates is null, "legacy save has no block-state extension");
        WorldSave? preShape = JsonSerializer.Deserialize<WorldSave>(v3Legacy, WorldStore.Json);
        TestAssert.Equal(3, preShape?.Version ?? -1, "v3 save remains readable after the state extension");
        TestAssert.Check(preShape?.BlockStates is null, "v3 save defaults to the empty shape-state extension");

        WorldSave upgraded = new(WorldStore.WorldFormatVersion, old?.Edits, old?.Time ?? 0.5);
        string upgradedJson = JsonSerializer.Serialize(upgraded, WorldStore.Json);
        WorldSave? upgradedReload = JsonSerializer.Deserialize<WorldSave>(upgradedJson, WorldStore.Json);
        TestAssert.Equal(WorldStore.WorldFormatVersion, upgradedReload?.Version ?? -1,
                "upgraded save writes the current world format version");
        TestAssert.Equal(4, WorldStore.WorldFormatVersion, "current world format is v4");
        TestAssert.Equal(WorldGenV13.Version, WorldStore.CurrentGenVersion,
                "new worlds use the latest versioned generator");

        string worldDir = WorldStore.Create("Management Test", 424242);
        TestAssert.Check(WorldStore.Rename(worldDir, "Renamed World"), "world rename succeeds");
        TestAssert.Equal("Renamed World", WorldStore.ReadManifest(worldDir)?.Name, "renamed manifest persists");

        string? copyDir = WorldStore.Duplicate(worldDir, "Copied World");
        TestAssert.Check(copyDir is not null, "world duplicate succeeds");
        if (copyDir is not null)
        {
            TestAssert.Equal("Copied World", WorldStore.ReadManifest(copyDir)?.Name, "copy has a new name");
            TestAssert.Equal(424242, WorldStore.ReadManifest(copyDir)?.Seed, "copy keeps the seed");
            TestAssert.Check(DirAccess.DirExistsAbsolute(copyDir), "copy folder exists");
            TestAssert.Check(WorldStore.Delete(copyDir), "copy can be deleted");
            TestAssert.Check(!DirAccess.DirExistsAbsolute(copyDir), "copy folder is gone");
        }

        TestAssert.Check(WorldStore.Delete(worldDir), "world can be deleted");
        TestAssert.Check(!DirAccess.DirExistsAbsolute(worldDir), "world folder is gone");
        TestAssert.Check(!WorldStore.Delete("user://settings.cfg"), "delete refuses paths outside worlds");

        string escapeDir = WorldStore.WorldsRoot + "/../delete-escape-world";
        DirAccess.MakeDirRecursiveAbsolute(escapeDir);
        string escapeMarker = escapeDir + "/marker.txt";
        using (FileAccess? marker = FileAccess.Open(escapeMarker, FileAccess.ModeFlags.Write))
            marker?.StoreString("keep");
        TestAssert.Check(!WorldStore.Delete(escapeDir),
                "delete refuses a traversal path that starts with the worlds prefix");
        TestAssert.Check(FileAccess.FileExists(escapeMarker),
                "a traversal-targeted delete leaves the outside file untouched");
        DirAccess.RemoveAbsolute(escapeMarker);
        DirAccess.RemoveAbsolute(escapeDir);

        // Reject malformed object graphs before they reach game-state loaders.
        string structuralPath = root + "/structural.json";
        TestAssert.Check(WorldStore.WriteAtomic(structuralPath,
                JsonSerializer.Serialize(save, WorldStore.Json)), "structural fixture primary writes");
        TestAssert.Check(WorldStore.WriteAtomic(structuralPath,
                JsonSerializer.Serialize(save with { Time = 0.875 }, WorldStore.Json)),
                "structural fixture newer primary rotates");
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(structuralPath),
                """{"version":4,"edits":{"0,0":null},"time":0.25,"furnaces":{"1,2,3":null}}""");
        WorldReadResult<WorldSave> structural = WorldStore.ReadJsonResult<WorldSave>(
                structuralPath, WorldStore.Json);
        TestAssert.Equal(WorldReadStatus.Recovered, structural.Status,
                "null nested edit dictionaries and furnace records reject the primary");
        TestAssert.Equal(0.625, structural.Value?.Time ?? -1.0,
                "structurally invalid primary falls back to a valid backup");

        string itemListPath = root + "/null-items.json";
        TestAssert.Check(WorldStore.WriteAtomic(itemListPath,
                JsonSerializer.Serialize(new PlayerSave([0.0f, 0.0f, 0.0f],
                        Items: [new ItemStackSave(0, (int)ItemId.Stone, 1)]), WorldStore.Json)),
                "valid player item fixture writes");
        TestAssert.Check(WorldStore.WriteAtomic(itemListPath,
                """{"pos":[0,0,0],"items":[null]}"""),
                "malformed player fixture replaces primary");
        WorldReadResult<PlayerSave> itemFallback = WorldStore.ReadJsonResult<PlayerSave>(
                itemListPath, WorldStore.Json);
        TestAssert.Equal(WorldReadStatus.Recovered, itemFallback.Status,
                "null item-stack entries reject a player-save candidate");

        // Preserve all three previous generations when a rename or final
        // temp-file commit fails during backup rotation.
        string rotationPath = root + "/rotation.json";
        WorldStore.WriteAtomic(rotationPath, "generation-one");
        WorldStore.WriteAtomic(rotationPath, "generation-two");
        WorldStore.WriteAtomic(rotationPath, "generation-three");
        WorldStore.RenameForTests = (from, to) =>
                from == rotationPath && to == rotationPath + ".bak"
                        ? Error.Failed : DirAccess.RenameAbsolute(from, to);
        bool rotationFailed;
        try { rotationFailed = !WorldStore.WriteAtomic(rotationPath, "generation-four"); }
        finally { WorldStore.RenameForTests = null; }
        TestAssert.Check(rotationFailed, "injected current-to-backup rename failure is reported");
        TestAssert.Equal("generation-three", FileAccess.GetFileAsString(rotationPath),
                "failed rotation preserves the previous primary");
        TestAssert.Equal("generation-two", FileAccess.GetFileAsString(rotationPath + ".bak"),
                "failed rotation restores the first backup");
        TestAssert.Equal("generation-one", FileAccess.GetFileAsString(rotationPath + ".bak2"),
                "failed rotation restores the oldest backup");

        WorldStore.RenameForTests = (from, to) =>
                from == rotationPath + ".tmp" && to == rotationPath
                        ? Error.Failed : DirAccess.RenameAbsolute(from, to);
        bool commitFailed;
        try { commitFailed = !WorldStore.WriteAtomic(rotationPath, "generation-four"); }
        finally { WorldStore.RenameForTests = null; }
        TestAssert.Check(commitFailed, "injected final temp commit failure is reported");
        TestAssert.Equal("generation-three", FileAccess.GetFileAsString(rotationPath),
                "failed final commit restores the previous primary");
        TestAssert.Equal("generation-two", FileAccess.GetFileAsString(rotationPath + ".bak"),
                "failed final commit restores the previous backup");
        TestAssert.Equal("generation-one", FileAccess.GetFileAsString(rotationPath + ".bak2"),
                "failed final commit retains the oldest backup");

        // IDs are collision-resistant, and a forced duplicate cannot overwrite
        // the first world's metadata.
        string testIdSuffix = Time.GetTicksUsec().ToString("X");
        string firstCollisionId = $"w-w05-{testIdSuffix}-collision";
        System.Collections.Generic.Queue<string> worldIds = new(
                [firstCollisionId, firstCollisionId, firstCollisionId + "-second"]);
        WorldStore.WorldIdFactoryForTests = () => worldIds.Dequeue();
        string collisionFirst;
        string collisionSecond;
        try
        {
            collisionFirst = WorldStore.Create("Collision First", 1001);
            string originalManifest = FileAccess.GetFileAsString(WorldStore.ManifestPath(collisionFirst));
            collisionSecond = WorldStore.Create("Collision Second", 1002);
            TestAssert.Check(collisionFirst != collisionSecond,
                    "repeated world IDs are rejected and a new candidate is reserved");
            TestAssert.Equal(originalManifest, FileAccess.GetFileAsString(WorldStore.ManifestPath(collisionFirst)),
                    "a directory-ID collision never changes the existing manifest");
        }
        finally { WorldStore.WorldIdFactoryForTests = null; }

        // Required manifest persistence is part of creation success.
        string failedCreateId = $"w-w05-{testIdSuffix}-create-failure";
        WorldStore.WorldIdFactoryForTests = () => failedCreateId;
        WorldStore.RenameForTests = (from, to) =>
                from.EndsWith(".tmp", StringComparison.Ordinal)
                        && to == WorldStore.ManifestPath(WorldStore.WorldsRoot + "/" + failedCreateId)
                        ? Error.Failed : DirAccess.RenameAbsolute(from, to);
        bool createFailed = false;
        try
        {
            try { WorldStore.Create("Must Not Open", 1003); }
            catch (System.IO.IOException) { createFailed = true; }
        }
        finally
        {
            WorldStore.WorldIdFactoryForTests = null;
            WorldStore.RenameForTests = null;
        }
        TestAssert.Check(createFailed, "failed manifest persistence makes world creation fail");
        TestAssert.Check(!DirAccess.DirExistsAbsolute(WorldStore.WorldsRoot + "/" + failedCreateId),
                "failed world creation removes its partial directory");
        TestAssert.Equal("Collision First", WorldStore.ReadManifest(collisionFirst)?.Name,
                "failed creation leaves an existing world's metadata untouched");

        TestAssert.Check(!WorldStore.RenameDetailed(collisionFirst, "   ").Success,
                "rename still rejects an empty name as input validation");
        WorldStore.RenameForTests = (from, to) =>
                from.EndsWith(".tmp", StringComparison.Ordinal)
                        && to == WorldStore.ManifestPath(collisionFirst)
                        ? Error.Failed : DirAccess.RenameAbsolute(from, to);
        WorldOperationResult renameFailure;
        try { renameFailure = WorldStore.RenameDetailed(collisionFirst, "Valid Name"); }
        finally { WorldStore.RenameForTests = null; }
        TestAssert.Check(!renameFailure.Success && renameFailure.Message.Contains("could not be saved",
                StringComparison.OrdinalIgnoreCase),
                "rename reports manifest persistence failures separately from invalid names");
        TestAssert.Equal("Collision First", WorldStore.ReadManifest(collisionFirst)?.Name,
                "failed rename preserves its previous manifest");

        // A simulated source-file read denial aborts duplication and leaves
        // the source world unchanged.
        string duplicateFailureSource = WorldStore.Create("Copy Failure Source", 1101);
        string sourceMarker = ProjectSettings.GlobalizePath(duplicateFailureSource) + "/source.bin";
        System.IO.File.WriteAllText(sourceMarker, "source bytes");
        string sourceManifestBefore = FileAccess.GetFileAsString(WorldStore.ManifestPath(duplicateFailureSource));
        string? successfulCopy = WorldStore.Duplicate(duplicateFailureSource);
        TestAssert.Check(successfulCopy is not null, "ordinary nested source content duplicates successfully");
        if (successfulCopy is not null)
        {
            string copiedMarker = ProjectSettings.GlobalizePath(successfulCopy) + "/source.bin";
            TestAssert.Equal("source bytes", System.IO.File.ReadAllText(copiedMarker),
                    "successful duplication preserves complete source file contents");
            WorldStore.Delete(successfulCopy);
        }

        WorldStore.CopyFileAllowedForTests = _ => false;
        string? failedCopy;
        try { failedCopy = WorldStore.Duplicate(duplicateFailureSource); }
        finally { WorldStore.CopyFileAllowedForTests = null; }
        TestAssert.Check(failedCopy is null, "injected source file read denial aborts duplication");
        TestAssert.Equal(sourceManifestBefore, FileAccess.GetFileAsString(WorldStore.ManifestPath(duplicateFailureSource)),
                "failed duplication leaves the source manifest unchanged");
        TestAssert.Check(System.IO.File.Exists(sourceMarker),
                "failed duplication leaves all source files in place");

        WorldStore.CopyFileWriteAllowedForTests = _ => false;
        string? failedWriteCopy;
        try { failedWriteCopy = WorldStore.Duplicate(duplicateFailureSource); }
        finally { WorldStore.CopyFileWriteAllowedForTests = null; }
        TestAssert.Check(failedWriteCopy is null,
                "injected destination write failure aborts duplication");
        TestAssert.Check(System.IO.File.Exists(sourceMarker),
                "destination write failure leaves the original source bytes intact");

        // Directory and file links are never traversed during duplicate or
        // delete; their external targets must remain byte-identical.
        string externalDirectory = ProjectSettings.GlobalizePath(root) + "/external-target";
        System.IO.Directory.CreateDirectory(externalDirectory);
        string externalMarker = System.IO.Path.Combine(externalDirectory, "keep.txt");
        System.IO.File.WriteAllText(externalMarker, "external data");
        string linkedWorld = WorldStore.Create("Linked World", 1102);
        string linkedWorldAbsolute = ProjectSettings.GlobalizePath(linkedWorld);
        bool directoryLinkCreated = false;
        bool nestedLinkCreated = false;
        bool fileLinkCreated = false;
        try
        {
            System.IO.Directory.CreateSymbolicLink(
                    System.IO.Path.Combine(linkedWorldAbsolute, "external-directory"), externalDirectory);
            directoryLinkCreated = true;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or PlatformNotSupportedException
                or System.Security.SecurityException)
        {
            // Some Windows test runners require elevated symbolic-link privileges.
        }

        string nestedDirectory = System.IO.Path.Combine(linkedWorldAbsolute, "nested");
        System.IO.Directory.CreateDirectory(nestedDirectory);
        try
        {
            System.IO.Directory.CreateSymbolicLink(
                    System.IO.Path.Combine(nestedDirectory, "external-nested"), externalDirectory);
            nestedLinkCreated = true;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or PlatformNotSupportedException
                or System.Security.SecurityException)
        {
            // The top-level link case still covers platforms with restricted nested links.
        }

        try
        {
            System.IO.File.CreateSymbolicLink(
                    System.IO.Path.Combine(linkedWorldAbsolute, "external-file"), externalMarker);
            fileLinkCreated = true;
        }
        catch (Exception e) when (e is System.IO.IOException
                or UnauthorizedAccessException or PlatformNotSupportedException
                or System.Security.SecurityException)
        {
            // File links can be restricted independently from directory links.
        }

        if (directoryLinkCreated || nestedLinkCreated || fileLinkCreated)
        {
            TestAssert.Check(WorldStore.Duplicate(linkedWorld) is null,
                    "world duplication rejects top-level, nested-directory, and file reparse points");
            TestAssert.Check(WorldStore.Delete(linkedWorld),
                    "world deletion removes links at every depth without descending into their targets");
            TestAssert.Equal("external data", System.IO.File.ReadAllText(externalMarker),
                    "external target contents survive linked-world duplication and deletion");
        }
        else
        {
            TestAssert.Check(true, "symbolic-link creation is unavailable on this test platform");
            WorldStore.Delete(linkedWorld);
        }

        // A world-directory access failure must not masquerade as an empty library.
        WorldStore.EnumerateWorldDirectoriesForTests = _ =>
                throw new UnauthorizedAccessException("simulated worlds-root denial");
        List<(string Dir, WorldManifest Manifest)> inaccessibleListing;
        try { inaccessibleListing = WorldStore.List(); }
        finally { WorldStore.EnumerateWorldDirectoriesForTests = null; }
        TestAssert.Check(inaccessibleListing.Count == 0 && WorldStore.LastListError is not null,
                "world-list permission errors are reported instead of showing an empty library");

        // A corrupt selected manifest remains the selected world even when
        // another healthy world is available in the same library.
        string corruptManifestWorld = WorldStore.Create("Unreadable Manifest", 1199);
        string otherValidWorld = WorldStore.Create("Other Valid World", 1198);
        foreach (string candidate in new[]
                {
                    WorldStore.ManifestPath(corruptManifestWorld),
                    WorldStore.ManifestPath(corruptManifestWorld) + ".bak",
                    WorldStore.ManifestPath(corruptManifestWorld) + ".bak2",
                })
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(candidate), "{ damaged manifest");
        List<(string Dir, WorldManifest Manifest)> visibleWorlds = WorldStore.List();
        TestAssert.Check(visibleWorlds.Exists(entry => entry.Dir == otherValidWorld)
                && WorldStore.UnreadableWorldDirectories.Contains(corruptManifestWorld),
                "world library displays healthy worlds and retains an unreadable world for recovery");
        if (GameSession.Instance is { } selectedSession)
        {
            string previousDir = selectedSession.WorldDir;
            WorldManifest? previousManifest = selectedSession.Manifest;
            selectedSession.SelectWorld(corruptManifestWorld);
            bool selectedWorldRejected = false;
            try { selectedSession.EnsureWorld(); }
            catch (System.IO.InvalidDataException) { selectedWorldRejected = true; }
            TestAssert.Check(selectedWorldRejected && selectedSession.WorldDir == corruptManifestWorld,
                    "an unreadable explicit selection never switches to another existing world");
            selectedSession.WorldDir = previousDir;
            selectedSession.Manifest = previousManifest;
        }

        // Damaged saves must not be treated as a first-run world or given a
        // starter kit. The manifest flag distinguishes prior saves from a
        // world that has never written its first save pair.
        string damagedWorld = WorldStore.Create("Damaged World", 1201);
        foreach (string savePath in new[]
                {
                    WorldStore.WorldSavePath(damagedWorld),
                    WorldStore.WorldSavePath(damagedWorld) + ".bak",
                    WorldStore.WorldSavePath(damagedWorld) + ".bak2",
                    WorldStore.PlayerSavePath(damagedWorld),
                    WorldStore.PlayerSavePath(damagedWorld) + ".bak",
                    WorldStore.PlayerSavePath(damagedWorld) + ".bak2",
                })
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(savePath), "{ malformed");
        WorldReadResult<WorldSave> damagedRead = WorldStore.ReadJsonResult<WorldSave>(
                WorldStore.WorldSavePath(damagedWorld), WorldStore.Json);
        TestAssert.Equal(WorldReadStatus.Corrupt, damagedRead.Status,
                "all malformed world-save candidates return an explicit corrupt result");
        TestAssert.Check(WorldStore.IsRecoveryRequired(damagedWorld),
                "unrecoverable save candidates mark the world recovery-required");
        List<(string Dir, WorldManifest Manifest)> playableWorlds = WorldStore.List();
        TestAssert.Check(!playableWorlds.Exists(entry => entry.Dir == damagedWorld)
                && WorldStore.UnreadableWorldDirectories.Contains(damagedWorld),
                "the world library lists a corrupt save pair under recovery instead of Play");
        TestAssert.Check(!WorldStore.WriteAtomic(WorldStore.WorldSavePath(damagedWorld),
                JsonSerializer.Serialize(save, WorldStore.Json)),
                "recovery-required worlds reject ordinary writes");

        GameSession? session = GameSession.Instance;
        string previousWorldDirectory = session?.WorldDir ?? "";
        WorldManifest? previousSessionManifest = session?.Manifest;
        if (session is not null)
        {
            session.SelectWorld(damagedWorld);
            Inventory inventory = new();
            inventory.LoadFrom(null);
            TestAssert.Check(inventory.Get(0).IsEmpty,
                    "recovery-required worlds do not receive starter inventory");
            session.WorldDir = previousWorldDirectory;
            session.Manifest = previousSessionManifest;
        }

        string savedHistoryWorld = WorldStore.Create("Saved History", 1202);
        WorldStore.WriteAtomic(WorldStore.WorldSavePath(savedHistoryWorld),
                JsonSerializer.Serialize(save, WorldStore.Json));
        WorldStore.WriteAtomic(WorldStore.PlayerSavePath(savedHistoryWorld),
                JsonSerializer.Serialize(new PlayerSave([1.0f, 2.0f, 3.0f]), WorldStore.Json));
        WorldManifest savedHistoryManifest = WorldStore.ReadManifest(savedHistoryWorld)!;
        TestAssert.Check(WorldStore.WriteManifest(savedHistoryWorld, savedHistoryManifest),
                "complete save pair persists save-history metadata in the manifest");
        TestAssert.Check(WorldStore.ReadManifest(savedHistoryWorld)?.HasSavedData == true,
                "manifest records whether the world has saved progress");
        foreach (string candidate in new[]
                {
                    WorldStore.WorldSavePath(savedHistoryWorld),
                    WorldStore.WorldSavePath(savedHistoryWorld) + ".bak",
                    WorldStore.WorldSavePath(savedHistoryWorld) + ".bak2",
                    WorldStore.PlayerSavePath(savedHistoryWorld),
                    WorldStore.PlayerSavePath(savedHistoryWorld) + ".bak",
                    WorldStore.PlayerSavePath(savedHistoryWorld) + ".bak2",
                })
            if (System.IO.File.Exists(ProjectSettings.GlobalizePath(candidate)))
                System.IO.File.Delete(ProjectSettings.GlobalizePath(candidate));

        if (session is not null)
        {
            session.SelectWorld(savedHistoryWorld);
            bool missingHistoryRejected = false;
            try { session.EnsureWorld(); }
            catch (System.IO.InvalidDataException) { missingHistoryRejected = true; }
            TestAssert.Check(missingHistoryRejected,
                    "worlds with a persisted save history cannot load after all save generations disappear");
            session.WorldDir = previousWorldDirectory;
            session.Manifest = previousSessionManifest;
        }

        string legacyPlayedWorld = WorldStore.Create("Legacy Played World", 1204);
        WorldManifest legacyPlayedManifest = WorldStore.ReadManifest(legacyPlayedWorld)! with
        {
            CreatedUtc = 1,
            PlayedUtc = 2,
            HasSavedData = false,
        };
        TestAssert.Check(WorldStore.WriteManifest(legacyPlayedWorld, legacyPlayedManifest),
                "legacy timestamp-only manifest remains writable");
        if (session is not null)
        {
            session.SelectWorld(legacyPlayedWorld);
            bool legacyMissingHistoryRejected = false;
            try { session.EnsureWorld(); }
            catch (System.IO.InvalidDataException) { legacyMissingHistoryRejected = true; }
            TestAssert.Check(legacyMissingHistoryRejected,
                    "legacy manifests with a played timestamp are not mistaken for never-saved worlds");
            session.WorldDir = previousWorldDirectory;
            session.Manifest = previousSessionManifest;
        }

        // A valid world save without a player file remains a legacy/unplayed
        // world when the manifest has no evidence of completed save history.
        string singleFileWorld = WorldStore.Create("Legacy Single File", 1205);
        TestAssert.Check(WorldStore.WriteAtomic(WorldStore.WorldSavePath(singleFileWorld),
                JsonSerializer.Serialize(save, WorldStore.Json)),
                "legacy single-file world fixture writes");
        if (session is not null)
        {
            session.SelectWorld(singleFileWorld);
            TestAssert.Equal(singleFileWorld, session.EnsureWorld(),
                    "a valid single-file world without saved-history metadata remains loadable");
            session.WorldDir = previousWorldDirectory;
            session.Manifest = previousSessionManifest;
        }
        TestAssert.Check(WorldStore.List().Exists(entry => entry.Dir == singleFileWorld),
                "legacy single-file world remains visible in the world library");

        string unsupportedPath = root + "/future-save.json";
        WorldStore.WriteAtomic(unsupportedPath, v2);
        WorldStore.WriteAtomic(unsupportedPath, v2);
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(unsupportedPath),
                """{"version":999,"edits":{},"time":0.5}""");
        WorldReadResult<WorldSave> unsupportedRead = WorldStore.ReadJsonResult<WorldSave>(
                unsupportedPath, WorldStore.Json);
        TestAssert.Equal(WorldReadStatus.Unsupported, unsupportedRead.Status,
                "future save versions are reported as unsupported without falling back to older data");

        string emptyDefault = WorldStore.WorldsRoot + "/new-empty-default-" + testIdSuffix;
        TestAssert.Check(WorldStore.DefaultFor(emptyDefault) is not null,
                "an empty new directory still receives legacy default metadata");
        string populatedDefault = WorldStore.Create("Do Not Guess", 1203);
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(populatedDefault + "/unrecognized.save"), "keep");
        bool populatedDefaultRejected = false;
        try { WorldStore.DefaultFor(populatedDefault); }
        catch (System.IO.InvalidDataException) { populatedDefaultRejected = true; }
        TestAssert.Check(populatedDefaultRejected,
                "existing world directories never receive guessed generator metadata");

        foreach (string testWorld in new[]
                {
                    collisionFirst, collisionSecond, duplicateFailureSource,
                    corruptManifestWorld, otherValidWorld, damagedWorld, savedHistoryWorld,
                    legacyPlayedWorld, singleFileWorld, populatedDefault, emptyDefault,
                })
            WorldStore.Delete(testWorld);

        DirAccess.RemoveAbsolute(backupPath + ".bak2");
        DirAccess.RemoveAbsolute(backupPath + ".bak");
        DirAccess.RemoveAbsolute(backupPath);
        DirAccess.RemoveAbsolute(corruptPath + ".bak");
        DirAccess.RemoveAbsolute(corruptPath);
        DirAccess.RemoveAbsolute(unsupportedPath);
        DirAccess.RemoveAbsolute(previewPlayerPath);
        DirAccess.RemoveAbsolute(path + ".bak2");
        DirAccess.RemoveAbsolute(path + ".bak");
        DirAccess.RemoveAbsolute(path);
        if (System.IO.Directory.Exists(ProjectSettings.GlobalizePath(root)))
            System.IO.Directory.Delete(ProjectSettings.GlobalizePath(root), recursive: true);
    }
}
