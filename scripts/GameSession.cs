using Godot;

namespace Minecraft;

/// <summary>
/// Autoload: the selected world plus boot-time setup shared by the menu and
/// the game scene (settings load, audio buses, legacy save migration).
/// </summary>
public partial class GameSession : Node
{
    public static GameSession Instance = null!;

    public string WorldDir = "";
    public WorldManifest? Manifest;

    public override void _Ready()
    {
        Instance = this;
        Settings.Load();
        GamepadBindings.Configure();
        Keybinds.Load();
        Sound.EnsureBuses();
        Sound.ApplyVolumes();
        ContentValidation.ValidateOrWarn();
        WorldStore.MigrateLegacyIfNeeded();
    }

    public void SelectWorld(string dir)
    {
        WorldDir = dir;
        Manifest = WorldStore.ReadManifest(dir);
    }

    /// <summary>Resolve a usable world from any entry path (menu click or direct scene run).</summary>
    public string EnsureWorld()
    {
        WorldManifest? manifest = WorldDir.Length > 0 ? WorldStore.ReadManifest(WorldDir) : null;
        if (manifest != null)
        {
            Manifest = manifest;
            return WorldDir;
        }

        WorldStore.MigrateLegacyIfNeeded();
        var worlds = WorldStore.List();
        if (worlds.Count > 0)
        {
            SelectWorld(worlds[0].Dir);
        }
        else
        {
            SelectWorld(WorldStore.Create("My World", null));
        }

        return WorldDir;
    }
}
