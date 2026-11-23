using System;
using Godot;

namespace Minecraft;

public static class LowCostShaderTests
{
    private const string LowQualityDefine = "#define MINECRAFT_LOW_QUALITY";

    public static void Run()
    {
        TestAssert.Check(Gfx.Presets[0].LowCostVoxelShaders,
                "LOW selects the compile-time low-cost voxel shader variant");
        for (int i = 1; i < Gfx.Presets.Count; i++)
            TestAssert.Check(!Gfx.Presets[i].LowCostVoxelShaders,
                    $"{Gfx.Presets[i].Name} retains the full voxel shader variant");

        bool previousGreedy = Mesher.Greedy;
        Gfx gfx = new();
        ShaderMaterial? atlas = null;
        ShaderMaterial? array = null;
        ShaderMaterial? glass = null;
        ShaderMaterial? lava = null;
        ShaderMaterial? water = null;
        try
        {
            // Apply LOW before touching the Lazy resources. This covers both
            // in-place updates and materials first created while LOW is active.
            gfx.ApplyPreset(0, persist: false);
            Mesher.Greedy = false;
            atlas = ChunkMaterials.Opaque as ShaderMaterial;
            Mesher.Greedy = true;
            array = ChunkMaterials.Opaque as ShaderMaterial;
            glass = ChunkMaterials.Glass;
            lava = ChunkMaterials.Lava;
            water = ChunkMaterials.Water as ShaderMaterial;

            AssertVariant(atlas, true, "classic atlas");
            AssertVariant(array, true, "greedy texture-array");
            AssertVariant(glass, true, "glass");
            AssertVariant(lava, true, "lava");
            AssertVariant(water, true, "water");

            gfx.ApplyPreset(1, persist: false);
            AssertVariant(atlas, false, "classic atlas after BALANCED");
            AssertVariant(array, false, "greedy texture-array after BALANCED");
            AssertVariant(glass, false, "glass after BALANCED");
            AssertVariant(lava, false, "lava after BALANCED");
            AssertVariant(water, false, "water after BALANCED");

            gfx.ApplyPreset(0, persist: false);
            AssertVariant(atlas, true, "classic atlas after returning to LOW");
            AssertVariant(array, true, "greedy texture-array after returning to LOW");
            AssertVariant(glass, true, "glass after returning to LOW");
            AssertVariant(lava, true, "lava after returning to LOW");
            AssertVariant(water, true, "water after returning to LOW");

            Mesher.Greedy = false;
            TestAssert.Check(ReferenceEquals(atlas, ChunkMaterials.Opaque),
                    "LOW/full switching preserves the classic atlas material instance");
            Mesher.Greedy = true;
            TestAssert.Check(ReferenceEquals(array, ChunkMaterials.Opaque),
                    "LOW/full switching preserves the greedy array material instance");
            TestAssert.Check(ReferenceEquals(glass, ChunkMaterials.Glass)
                    && ReferenceEquals(lava, ChunkMaterials.Lava)
                    && ReferenceEquals(water, ChunkMaterials.Water),
                    "LOW/full switching preserves shared glass, lava and water instances");
        }
        finally
        {
            gfx.ApplyPreset(1, persist: false);
            Mesher.Greedy = previousGreedy;
            gfx.Free();
        }
    }

    private static void AssertVariant(ShaderMaterial? material, bool enabled, string name)
    {
        TestAssert.Check(material != null && material.Shader != null,
                $"{name} shared shader material exists");
        if (material?.Shader == null)
            return;

        bool hasLowDefine = material.Shader.Code.Contains(LowQualityDefine, StringComparison.Ordinal);
        TestAssert.Equal(enabled, hasLowDefine,
                $"{name} uses {(enabled ? "LOW" : "full-quality")} shader source");
    }
}
