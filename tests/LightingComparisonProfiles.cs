using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Fixed scene composition and time-of-day settings for the repeatable
/// lighting inspections. These profiles change only test cameras and clock
/// values; production lights, environment resources and render paths are reused.
/// </summary>
public static class LightingComparisonProfiles
{
    public sealed record Profile(
            string Id,
            string DisplayName,
            string SceneFile,
            double TimeOfDay,
            Vector3 CameraOffset,
            Vector3 TargetOffset,
            float FieldOfView,
            string? ReferenceScreenshot);

    private static readonly Profile[] Entries =
    [
        new("forest-canopy", "Forest canopy", "ForestCanopyLighting.tscn",
                0.50, new Vector3(38.0f, 24.0f, 42.0f),
                new Vector3(0.0f, 8.0f, 0.0f), 62.0f,
                "res://docs/visual/screenshots/lighting-forest-canopy.png"),
        new("torch-cave", "Torch cave", "TorchCaveLighting.tscn",
                0.50, new Vector3(0.0f, 2.5f, 8.0f),
                new Vector3(0.0f, 1.8f, 0.0f), 68.0f,
                "res://docs/visual/screenshots/lighting-torch-cave.png"),
        new("river-valley", "River valley", "RiverValleyLighting.tscn",
                0.50, new Vector3(28.0f, 18.0f, 38.0f),
                new Vector3(0.0f, 4.0f, 0.0f), 64.0f,
                "res://docs/visual/screenshots/lighting-river-valley.png"),
        new("village-street", "Village street", "VillageStreetLighting.tscn",
                0.73, new Vector3(0.0f, 8.0f, -38.0f),
                new Vector3(0.0f, 3.0f, 14.0f), 60.0f,
                "res://docs/visual/screenshots/lighting-village-street.png"),
        new("dungeon-arena", "Dungeon arena", "DungeonArenaLighting.tscn",
                0.02, new Vector3(5.0f, 3.0f, 5.0f),
                new Vector3(0.0f, 2.5f, 0.0f), 62.0f,
                "res://docs/visual/screenshots/lighting-dungeon-arena.png"),
        // Captured for manual review until a GPU-backed reference is committed;
        // the audit refuses to invent one.
        new("pale-surfaces", "Snow, sand and pale stone", "PaleSurfaceLighting.tscn",
                0.50, new Vector3(10.0f, 7.0f, 18.0f),
                Vector3.Zero, 55.0f, null),
    ];

    public const int CaptureWidth = 1280;
    public const int CaptureHeight = 720;
    public const int ReferenceViewDistance = 3;
    public const int ChunkBuildRadius = 2;
    public const int WarmupFrames = 180;

    public static IReadOnlyList<Profile> All => Entries;

    public static bool TryGet(string id, out Profile profile)
    {
        foreach (Profile candidate in Entries)
        {
            if (candidate.Id == id)
            {
                profile = candidate;
                return true;
            }
        }

        profile = null!;
        return false;
    }
}
