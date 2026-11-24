using Godot;

namespace Minecraft;

/// <summary>
/// User settings, persisted to user://settings.cfg. Static because every
/// system reads it (player look, audio buses, streaming distance) and it
/// must behave identically from the main menu and the game scene.
/// </summary>
public static class Settings
{
    private const string Path = "user://settings.cfg";

    public static float MouseSensitivity = 0.0022f;
    public static bool InvertY;
    public static float Fov = 75.0f;
    public static float MasterVolume = 1.0f;
    public static float MusicVolume = 0.6f;
    public static float SfxVolume = 1.0f;
    public static float AmbientVolume = 1.0f;
    public static bool Muted;
    public static int GfxPreset = Gfx.DefaultPreset;
    public static bool AutoQuality = true;
    public static bool GreedyMeshing = true;
    public static int ViewDistance = Gfx.Presets[Gfx.DefaultPreset].View;

    // Graphics preset indexes are user settings, not world/save IDs. Version
    // the mapping so adding BALANCED never reinterprets an existing choice.
    public const int GraphicsPresetVersion = 2;

    public static int MigrateGraphicsPreset(int presetIndex, int storedVersion)
    {
        if (storedVersion >= GraphicsPresetVersion)
        {
            return Mathf.Clamp(presetIndex, 0, Gfx.Presets.Count - 1);
        }

        return presetIndex switch
        {
            0 => 0, // LOW
            1 => 2, // HIGH
            2 => 3, // ULTRA
            3 => 4, // MAX
            4 => 5, // MAX · FSR2
            _ => Gfx.DefaultPreset,
        };
    }

    public static int MigrateGraphicsViewDistance(
            int viewDistance, int legacyPresetIndex, int storedVersion)
    {
        int clamped = Mathf.Clamp(viewDistance, 2, 24);
        if (storedVersion >= GraphicsPresetVersion)
        {
            return clamped;
        }

        int legacyDefaultView = legacyPresetIndex switch
        {
            0 => 5,
            1 => 8,
            2 => 10,
            3 or 4 => 12,
            _ => -1,
        };
        if (clamped != legacyDefaultView)
        {
            return clamped; // Preserve a custom view-distance choice.
        }

        int migratedPreset = MigrateGraphicsPreset(legacyPresetIndex, storedVersion);
        return Gfx.Presets[migratedPreset].View;
    }
    public static bool HeadBob = true;
    public static bool ShowHints = true;

    // Gameplay: 0 peaceful, 1 normal, 2 hard. Peaceful-leaning default (D1).
    public const int DifficultyPeaceful = 0;
    public const int DifficultyNormal = 1;
    public const int DifficultyHard = 2;
    public static readonly string[] DifficultyNames = ["peaceful", "normal", "hard"];
    public static int Difficulty = DifficultyPeaceful;

    private static bool _loaded;

    internal static bool TryReadFloat(Variant stored, float fallback, float min, float max, out float value)
    {
        value = fallback;
        if (stored.VariantType is not (Variant.Type.Float or Variant.Type.Int))
            return false;
        float parsed = (float)stored;
        if (!float.IsFinite(parsed) || parsed < min || parsed > max)
            return false;
        value = parsed;
        return true;
    }

    internal static bool TryReadBool(Variant stored, bool fallback, out bool value)
    {
        value = fallback;
        if (stored.VariantType != Variant.Type.Bool)
            return false;
        value = (bool)stored;
        return true;
    }

    internal static bool TryReadInt(Variant stored, int fallback, int min, int max, out int value)
    {
        value = fallback;
        if (stored.VariantType != Variant.Type.Int)
            return false;
        int parsed = (int)stored;
        if (parsed < min || parsed > max)
            return false;
        value = parsed;
        return true;
    }

    private static float ReadFloat(ConfigFile cfg, string section, string key, float fallback, float min, float max)
    {
        Variant stored = cfg.GetValue(section, key, fallback);
        if (TryReadFloat(stored, fallback, min, max, out float value))
            return value;
        GD.PushWarning($"Ignoring invalid setting {section}/{key}; expected a number from {min} to {max}.");
        return fallback;
    }

    private static bool ReadBool(ConfigFile cfg, string section, string key, bool fallback)
    {
        Variant stored = cfg.GetValue(section, key, fallback);
        if (TryReadBool(stored, fallback, out bool value))
            return value;
        GD.PushWarning($"Ignoring invalid setting {section}/{key}; expected a Boolean.");
        return fallback;
    }

    private static int ReadInt(ConfigFile cfg, string section, string key, int fallback, int min, int max)
    {
        Variant stored = cfg.GetValue(section, key, fallback);
        if (TryReadInt(stored, fallback, min, max, out int value))
            return value;
        GD.PushWarning($"Ignoring invalid setting {section}/{key}; expected an integer from {min} to {max}.");
        return fallback;
    }

    internal static Error SaveConfig(ConfigFile config, string path) => config.Save(path);

    public static void Load()
    {
        if (_loaded)
            return;

        ConfigFile cfg = new();
        if (cfg.Load(Path) == Error.Ok)
        {
            MouseSensitivity = ReadFloat(cfg, "input", "mouse_sensitivity", MouseSensitivity, 0.0005f, 0.006f);
            InvertY = ReadBool(cfg, "input", "invert_y", InvertY);
            Fov = ReadFloat(cfg, "camera", "fov", Fov, 60.0f, 110.0f);
            MasterVolume = ReadFloat(cfg, "audio", "master", MasterVolume, 0.0f, 1.0f);
            MusicVolume = ReadFloat(cfg, "audio", "music", MusicVolume, 0.0f, 1.0f);
            SfxVolume = ReadFloat(cfg, "audio", "sfx", SfxVolume, 0.0f, 1.0f);
            AmbientVolume = ReadFloat(cfg, "audio", "ambient", AmbientVolume, 0.0f, 1.0f);
            Muted = ReadBool(cfg, "audio", "muted", Muted);
            int savedPreset = ReadInt(cfg, "video", "gfx_preset", GfxPreset, int.MinValue, int.MaxValue);
            int presetVersion = ReadInt(cfg, "video", "gfx_preset_version", 1, 1, 1000);
            GfxPreset = MigrateGraphicsPreset(savedPreset, presetVersion);
            int savedViewDistance = ReadInt(cfg, "video", "view_distance", ViewDistance, 2, 24);
            ViewDistance = MigrateGraphicsViewDistance(savedViewDistance, savedPreset, presetVersion);
            AutoQuality = ReadBool(cfg, "video", "auto_quality", AutoQuality);
            GreedyMeshing = ReadBool(cfg, "video", "greedy_meshing", GreedyMeshing);
            HeadBob = ReadBool(cfg, "video", "head_bob", HeadBob);
            ShowHints = ReadBool(cfg, "gameplay", "show_hints", ShowHints);
            Difficulty = ReadInt(cfg, "gameplay", "difficulty", Difficulty, 0, 2);
        }
        _loaded = true;
    }

    public static Error Save()
    {
        ConfigFile cfg = new();
        cfg.SetValue("input", "mouse_sensitivity", MouseSensitivity);
        cfg.SetValue("input", "invert_y", InvertY);
        cfg.SetValue("camera", "fov", Fov);
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ambient", AmbientVolume);
        cfg.SetValue("audio", "muted", Muted);
        cfg.SetValue("video", "gfx_preset", GfxPreset);
        cfg.SetValue("video", "gfx_preset_version", GraphicsPresetVersion);
        cfg.SetValue("video", "view_distance", ViewDistance);
        cfg.SetValue("video", "auto_quality", AutoQuality);
        cfg.SetValue("video", "greedy_meshing", GreedyMeshing);
        cfg.SetValue("video", "head_bob", HeadBob);
        cfg.SetValue("gameplay", "show_hints", ShowHints);
        cfg.SetValue("gameplay", "difficulty", Difficulty);
        Error result = SaveConfig(cfg, Path);
        if (result != Error.Ok)
            GD.PushError($"Could not persist settings to {Path}: {result}.");
        return result;
    }
}
