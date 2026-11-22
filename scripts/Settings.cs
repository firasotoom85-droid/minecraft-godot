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

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        ConfigFile cfg = new();
        if (cfg.Load(Path) != Error.Ok)
        {
            return;
        }

        MouseSensitivity = (float)cfg.GetValue("input", "mouse_sensitivity", MouseSensitivity);
        InvertY = (bool)cfg.GetValue("input", "invert_y", InvertY);
        Fov = (float)cfg.GetValue("camera", "fov", Fov);
        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
        MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume);
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        AmbientVolume = (float)cfg.GetValue("audio", "ambient", AmbientVolume);
        Muted = (bool)cfg.GetValue("audio", "muted", Muted);
        int savedPreset = (int)cfg.GetValue("video", "gfx_preset", GfxPreset);
        int presetVersion = (int)cfg.GetValue("video", "gfx_preset_version", 1);
        GfxPreset = MigrateGraphicsPreset(savedPreset, presetVersion);
        int savedViewDistance = (int)cfg.GetValue("video", "view_distance", ViewDistance);
        ViewDistance = MigrateGraphicsViewDistance(savedViewDistance, savedPreset, presetVersion);
        AutoQuality = (bool)cfg.GetValue("video", "auto_quality", AutoQuality);
        GreedyMeshing = (bool)cfg.GetValue("video", "greedy_meshing", GreedyMeshing);
        HeadBob = (bool)cfg.GetValue("video", "head_bob", HeadBob);
        ShowHints = (bool)cfg.GetValue("gameplay", "show_hints", ShowHints);
        Difficulty = Mathf.Clamp((int)cfg.GetValue("gameplay", "difficulty", Difficulty), 0, 2);
    }

    public static void Save()
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
        cfg.Save(Path);
    }
}
