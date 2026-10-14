using System;
using System.Collections.Generic;
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
    string? Preset = null);

/// <summary>Legacy combined save (v1). Read only while migrating an old save.</summary>
public sealed record LegacySave(
    Dictionary<string, Dictionary<string, BlockId>>? Edits,
    float[]? Player,
    double Time);

/// <summary>World state (version 2): block edits + world clock. No player data.</summary>
public sealed record WorldSave(
    int Version,
    Dictionary<string, Dictionary<string, BlockId>>? Edits,
    double Time);

/// <summary>Per-player state. Split from world state so multiplayer code
/// never has to untangle the two. M2 fields default to "unset" for old saves.</summary>
public sealed record PlayerSave(
    float[] Pos,
    int Selected = 0,
    List<ItemStackSave>? Items = null,
    string? Mode = null,
    float Health = 0.0f,
    float Hunger = 0.0f,
    float[]? Respawn = null);

/// <summary>One saved inventory slot (0-8 hotbar, 9-35 backpack).</summary>
public sealed record ItemStackSave(int Slot, int Id, int Count, int Durability = 0);

/// <summary>
/// World folders under user://worlds/&lt;id&gt;/ hold manifest.json (identity +
/// seeds), world.json (edits + time) and player.json (position). Saves are
/// temp-written, rotated through two .bak backups, then renamed into place.
/// </summary>
public static class WorldStore
{
    public const int CurrentGenVersion = 5;
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

    private static readonly JsonSerializerOptions ManifestJson = new(Json)
    {
        WriteIndented = true,
    };

    public static long NowUtc() => (long)Time.GetUnixTimeFromSystem();

    public static string ManifestPath(string dir) => $"{dir}/manifest.json";

    public static string WorldSavePath(string dir) => $"{dir}/world.json";

    public static string PlayerSavePath(string dir) => $"{dir}/player.json";

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
        ReadJson<WorldManifest>(ManifestPath(dir), ManifestJson);

    public static void WriteManifest(string dir, WorldManifest manifest)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        using FileAccess? f = FileAccess.Open(ManifestPath(dir), FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(manifest, ManifestJson));
    }

    /// <summary>Most recently played worlds first.</summary>
    public static List<(string Dir, WorldManifest Manifest)> List()
    {
        List<(string Dir, WorldManifest Manifest)> result = [];
        using DirAccess? da = DirAccess.Open(WorldsRoot);
        if (da == null)
        {
            return result;
        }

        foreach (string name in da.GetDirectories())
        {
            string dir = $"{WorldsRoot}/{name}";
            if (ReadManifest(dir) is { } manifest)
            {
                result.Add((dir, manifest));
            }
        }

        result.Sort((a, b) => b.Manifest.PlayedUtc.CompareTo(a.Manifest.PlayedUtc));
        return result;
    }

    public static string Create(string name, int? seed, string mode = "survival", string preset = "normal")
    {
        int resolved = seed ?? unchecked((int)(Time.GetTicksUsec() & 0x7FFFFFFF));
        string dir = $"{WorldsRoot}/w{Time.GetTicksUsec() & 0xFFFFFFFF:X8}";
        WriteManifest(dir, new WorldManifest(
                name, resolved, CurrentGenVersion, DeriveNoiseSeeds(resolved), NowUtc(), NowUtc(), mode,
                preset == "normal" ? null : preset));
        return dir;
    }

    /// <summary>Safety net for a missing manifest: an untouched v1-style world.</summary>
    public static WorldManifest DefaultFor(string dir)
    {
        WorldManifest manifest = new(
                "My World", LegacyNoiseSeeds[0], 1, LegacyNoiseSeeds, NowUtc(), NowUtc());
        WriteManifest(dir, manifest);
        return manifest;
    }

    /// <summary>One-time import of the pre-worlds save file into a world folder.</summary>
    public static void MigrateLegacyIfNeeded()
    {
        if (!FileAccess.FileExists(LegacySavePath) || List().Count > 0)
        {
            return;
        }

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

        if (save == null)
        {
            return;
        }

        string dir = $"{WorldsRoot}/default";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        bool worldWritten = WriteAtomic(
                WorldSavePath(dir), JsonSerializer.Serialize(new WorldSave(2, save.Edits, save.Time), Json));
        bool playerWritten = WriteAtomic(
                PlayerSavePath(dir), JsonSerializer.Serialize(new PlayerSave(save.Player ?? []), Json));
        if (!worldWritten || !playerWritten)
        {
            GD.PushWarning("minecraft: legacy migration failed to write; original save left untouched");
            return;
        }

        WriteManifest(dir, new WorldManifest(
                "My World", LegacyNoiseSeeds[0], 1, LegacyNoiseSeeds, NowUtc(), NowUtc()));
        DirAccess.RenameAbsolute(LegacySavePath, LegacySavePath + ".migrated");
        GD.Print($"minecraft: migrated legacy save into {dir}");
    }

    /// <summary>Temp-write, rotate two backups, then rename into place.</summary>
    public static bool WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        using (FileAccess? f = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
        {
            if (f == null)
            {
                GD.PushWarning($"minecraft: could not write {tmp}");
                return false;
            }

            f.StoreString(text);
        }

        if (FileAccess.FileExists(path))
        {
            string bak = path + ".bak";
            string bak2 = path + ".bak2";
            if (FileAccess.FileExists(bak))
            {
                if (FileAccess.FileExists(bak2))
                {
                    DirAccess.RemoveAbsolute(bak2);
                }

                DirAccess.RenameAbsolute(bak, bak2);
            }

            DirAccess.RenameAbsolute(path, bak);
        }

        return DirAccess.RenameAbsolute(tmp, path) == Error.Ok;
    }

    /// <summary>Parse a JSON file; on corruption fall back to .bak, then .bak2.</summary>
    public static T? ReadJson<T>(string path, JsonSerializerOptions options)
        where T : class
    {
        foreach (string candidate in new[] { path, path + ".bak", path + ".bak2" })
        {
            if (!FileAccess.FileExists(candidate))
            {
                continue;
            }

            T? value;
            try
            {
                value = JsonSerializer.Deserialize<T>(FileAccess.GetFileAsString(candidate), options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (value != null)
            {
                if (candidate != path)
                {
                    GD.PushWarning($"minecraft: recovered {path} from {candidate}");
                }

                return value;
            }
        }

        return null;
    }
}
