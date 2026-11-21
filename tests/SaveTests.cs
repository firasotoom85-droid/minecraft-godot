using System.Collections.Generic;
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
            ["0,0"] = new Dictionary<string, BlockId> { ["123"] = BlockId.Stone },
        };
        Dictionary<string, List<ItemStackSave>> containers = new()
        {
            ["1,2,3"] = [new ItemStackSave(0, (int)ItemId.Stone, 12)],
        };
        Dictionary<string, bool> flags = new() { ["dungeon/ember"] = true };
        WorldSave save = new(WorldStore.WorldFormatVersion, edits, 0.625, containers, flags);
        string path = root + "/world.json";

        PlayerSave emptyHungerSave = new([0.0f, 0.0f, 0.0f], 0, null, "survival", 20.0f, 0.0f);
        string emptyHungerJson = JsonSerializer.Serialize(emptyHungerSave, WorldStore.Json);
        PlayerSave? emptyHungerReload = JsonSerializer.Deserialize<PlayerSave>(emptyHungerJson, WorldStore.Json);
        TestAssert.Equal(0.0f, emptyHungerReload?.Hunger ?? -1.0f,
                "zero hunger survives player-save serialization");
        PlayerSave? legacyPlayer = JsonSerializer.Deserialize<PlayerSave>("{\"pos\":[0,0,0]}", WorldStore.Json);
        TestAssert.Check(legacyPlayer?.Hunger is null,
                "old player saves distinguish a missing hunger field");

        WorldSave boatSave = new(WorldStore.WorldFormatVersion, [], 0.5,
                Boats: [new BoatSave([12.5f, 49.7f, -3.5f], 0.75f, [1.0f, 0.0f, -2.0f], true)]);
        WorldSave? boatReload = JsonSerializer.Deserialize<WorldSave>(
                JsonSerializer.Serialize(boatSave, WorldStore.Json), WorldStore.Json);
        TestAssert.Check(boatReload?.Boats is { Count: 1 } && boatReload.Boats[0].Occupied,
                "boat pose and occupied state survive optional world-save serialization");
        const string v3 = """{"version":3,"edits":{},"time":0.5,"containers":{},"flags":{},"furnaces":{}}""";
        WorldSave? preBoatSave = JsonSerializer.Deserialize<WorldSave>(v3, WorldStore.Json);
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
        }

        string backupPath = root + "/backup.json";
        WorldStore.WriteAtomic(backupPath, "one");
        WorldStore.WriteAtomic(backupPath, "two");
        WorldStore.WriteAtomic(backupPath, "three");
        TestAssert.Equal("three", FileAccess.GetFileAsString(backupPath), "newest save is current");
        TestAssert.Equal("two", FileAccess.GetFileAsString(backupPath + ".bak"), "first backup is previous save");
        TestAssert.Equal("one", FileAccess.GetFileAsString(backupPath + ".bak2"), "second backup is oldest save");

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

        WorldSave upgraded = new(WorldStore.WorldFormatVersion, old?.Edits, old?.Time ?? 0.5);
        string upgradedJson = JsonSerializer.Serialize(upgraded, WorldStore.Json);
        WorldSave? upgradedReload = JsonSerializer.Deserialize<WorldSave>(upgradedJson, WorldStore.Json);
        TestAssert.Equal(WorldStore.WorldFormatVersion, upgradedReload?.Version ?? -1,
                "upgraded save writes the current world format version");
        TestAssert.Equal(7, WorldStore.CurrentGenVersion, "new worlds use generator version 7");

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

        DirAccess.RemoveAbsolute(backupPath + ".bak2");
        DirAccess.RemoveAbsolute(backupPath + ".bak");
        DirAccess.RemoveAbsolute(backupPath);
        DirAccess.RemoveAbsolute(corruptPath + ".bak");
        DirAccess.RemoveAbsolute(corruptPath);
        DirAccess.RemoveAbsolute(path + ".bak2");
        DirAccess.RemoveAbsolute(path + ".bak");
        DirAccess.RemoveAbsolute(path);
        DirAccess.RemoveAbsolute(root);
    }
}
