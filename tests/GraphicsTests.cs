using Godot;

namespace Minecraft;

public static class GraphicsTests
{
    public static void Run()
    {
        TestAssert.Equal(6, Gfx.Presets.Count, "graphics preset list includes low through max fsr2");
        TestAssert.Equal(0, Gfx.DefaultPreset, "low is the hardware-friendly default");
        TestAssert.Equal("LOW", Gfx.Presets[Gfx.DefaultPreset].Name, "default preset name is low");
        TestAssert.Equal(5, Gfx.Presets[Gfx.DefaultPreset].View, "default view distance matches low preset");

        Gfx.Preset low = Gfx.Presets[0];
        TestAssert.Equal(45.0f, low.ShadowDistance, "low preset keeps near shadows");
        TestAssert.Equal(1024, low.ShadowSize, "low preset uses a 1k shadow atlas");
        TestAssert.Check(!low.Ssao && !low.Ssr && !low.Glow && !low.VolFog,
                "low preset disables expensive screen-space and volumetric effects");
        TestAssert.Equal(0.80f, low.Scale, "low preset renders below native resolution");

        Gfx.Preset balanced = Gfx.Presets[1];
        TestAssert.Equal("BALANCED", balanced.Name, "balanced preset is explicit");
        TestAssert.Equal(7, balanced.View, "balanced view distance");
        TestAssert.Equal(65.0f, balanced.ShadowDistance, "balanced shadow distance");
        TestAssert.Equal(0.90f, balanced.Scale, "balanced render scale");
        TestAssert.Check(!balanced.Ssao && !balanced.Ssr && !balanced.VolFog,
                "balanced avoids expensive screen-space and volumetric effects");

        // Version-1 user settings used LOW/HIGH/ULTRA/MAX/MAX FSR2 indexes.
        TestAssert.Equal(0, Settings.MigrateGraphicsPreset(0, 1), "legacy low index is stable");
        TestAssert.Equal(2, Settings.MigrateGraphicsPreset(1, 1), "legacy high index remains high");
        TestAssert.Equal(3, Settings.MigrateGraphicsPreset(2, 1), "legacy ultra index remains ultra");
        TestAssert.Equal(4, Settings.MigrateGraphicsPreset(3, 1), "legacy max index remains max");
        TestAssert.Equal(5, Settings.MigrateGraphicsPreset(4, 1), "legacy max fsr2 index remains max fsr2");
        TestAssert.Equal(Gfx.DefaultPreset, Settings.MigrateGraphicsPreset(99, 1),
                "invalid legacy preset falls back to the safe default");

        for (int i = 0; i < Gfx.Presets.Count; i++)
        {
            TestAssert.Equal(i, Settings.MigrateGraphicsPreset(i, Settings.GraphicsPresetVersion),
                    $"current graphics preset index {i} is unchanged");
        }

        TestAssert.Equal(0, Settings.MigrateGraphicsPreset(-1, Settings.GraphicsPresetVersion),
                "negative current preset clamps to low");
        TestAssert.Equal(Gfx.Presets.Count - 1,
                Settings.MigrateGraphicsPreset(99, Settings.GraphicsPresetVersion),
                "current preset clamps to the last known preset");

        TestAssert.Equal(9, Settings.MigrateGraphicsViewDistance(8, 1, 1),
                "legacy HIGH default view distance follows the expanded HIGH preset");
        TestAssert.Equal(5, Settings.MigrateGraphicsViewDistance(5, 0, 1),
                "legacy LOW view distance remains low");
        TestAssert.Equal(11, Settings.MigrateGraphicsViewDistance(11, 3, 1),
                "custom legacy view distance is preserved");
        TestAssert.Equal(8, Settings.MigrateGraphicsViewDistance(8, 1, 2),
                "versioned current view distance is not remapped");

        VoxelWorld renderOnlyWorld = new() { BuildCollision = false };
        Chunk renderOnlyChunk = new();
        VoxelWorld gameplayWorld = new();
        Chunk gameplayChunk = new();
        try
        {
            renderOnlyChunk.Setup(renderOnlyWorld, Vector2I.Zero);
            gameplayChunk.Setup(gameplayWorld, Vector2I.Zero);
            TestAssert.Check(renderOnlyChunk.GetNodeOrNull<CollisionShape3D>("Collision") == null,
                    "render-only preview chunk does not create a collision shape");
            TestAssert.Equal(0u, renderOnlyChunk.CollisionLayer,
                    "render-only preview chunk has no physics collision layer");
            TestAssert.Check(gameplayChunk.GetNodeOrNull<CollisionShape3D>("Collision") != null,
                    "gameplay chunks retain their collision shape");
            TestAssert.Equal((uint)PhysicsLayer.Terrain, gameplayChunk.CollisionLayer,
                    "gameplay chunks retain the terrain collision layer");
        }
        finally
        {
            TestAssert.FreeAll(renderOnlyChunk, renderOnlyWorld, gameplayChunk, gameplayWorld);
        }
    }
}
