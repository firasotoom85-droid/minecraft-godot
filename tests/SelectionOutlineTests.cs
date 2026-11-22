using Godot;

namespace Minecraft;

/// <summary>
/// Guards the paired, lighting-independent selection passes and the geometry
/// used by cube, slab and stair targets.
/// </summary>
public static class SelectionOutlineTests
{
    public static void Run()
    {
        CheckHighContrastMaterials();

        BlockSelectionOutline.MeshSet cube = BlockSelectionOutline.BuildCube();
        CheckMeshSet(cube, "full cube selection");
        Aabb cubeSilhouette = cube.Silhouette.GetAabb();
        Aabb cubeContrast = cube.Contrast.GetAabb();
        TestAssert.Check(cubeSilhouette.Size.X > 1.02f
                && cubeSilhouette.Size.Y > 1.02f
                && cubeSilhouette.Size.Z > 1.02f,
                "cube silhouette expands slightly beyond block faces to avoid z-fighting");
        TestAssert.Check(cubeContrast.Size.X < cubeSilhouette.Size.X - 0.01f
                && cubeContrast.Size.Y < cubeSilhouette.Size.Y - 0.01f
                && cubeContrast.Size.Z < cubeSilhouette.Size.Z - 0.01f,
                "bright inset tracks remain separated from the dark silhouette");

        foreach (BlockId id in new[]
        {
            BlockId.PlanksSlab, BlockId.CobblestoneSlab, BlockId.BrickSlab,
            BlockId.PlanksStairs, BlockId.CobblestoneStairs, BlockId.BrickStairs,
        })
        {
            int stateCount = BlockShapes.Kind(id) == BlockShapeKind.Slab ? 3 : 8;
            for (byte state = 0; state < stateCount; state++)
            {
                BlockSelectionOutline.MeshSet shape = BlockSelectionOutline.BuildShape(id, state);
                string label = $"{id} state {state}";
                CheckMeshSet(shape, label);

                bool spansFullHeight = BlockShapes.Kind(id) == BlockShapeKind.Stairs
                        || BlockShapes.IsFullCube(id, state);
                float expectedHeight = spansFullHeight ? 1.024f : 0.524f;
                TestAssert.Check(Mathf.Abs(shape.Silhouette.GetAabb().Size.Y - expectedHeight) < 0.002f,
                        $"{label} silhouette follows the occupied block height");
                TestAssert.Check(shape.Contrast.GetAabb().Size.Y > 0.45f,
                        $"{label} keeps its inset contrast lines on exposed surfaces");
            }
        }
    }

    private static void CheckHighContrastMaterials()
    {
        StandardMaterial3D dark = BlockSelectionOutline.CreateSilhouetteMaterial();
        StandardMaterial3D light = BlockSelectionOutline.CreateContrastMaterial();
        float darkLuminance = Luminance(dark.AlbedoColor);
        float lightLuminance = Luminance(light.AlbedoColor);

        TestAssert.Check(dark.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded
                && light.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded,
                "both selection passes ignore world lighting and cave exposure");
        TestAssert.Check(dark.AlbedoColor.A >= 0.99f && light.AlbedoColor.A >= 0.99f,
                "both selection passes stay opaque over snow and transparent water");
        TestAssert.Check(darkLuminance < 0.08f && lightLuminance > 0.88f
                && lightLuminance - darkLuminance > 0.80f,
                "dark silhouette and light inset retain strong luminance separation");
    }

    private static void CheckMeshSet(BlockSelectionOutline.MeshSet meshes, string label)
    {
        TestAssert.Check(meshes.Silhouette.GetSurfaceCount() > 0,
                $"{label} has a silhouette surface");
        TestAssert.Check(meshes.Contrast.GetSurfaceCount() > 0,
                $"{label} has an inset contrast surface");
        ArrayMesh silhouetteMesh = (ArrayMesh)meshes.Silhouette;
        ArrayMesh contrastMesh = (ArrayMesh)meshes.Contrast;
        TestAssert.Equal(Mesh.PrimitiveType.Triangles,
                silhouetteMesh.SurfaceGetPrimitiveType(0),
                $"{label} silhouette uses stable world-space geometry");
        TestAssert.Equal(Mesh.PrimitiveType.Lines,
                contrastMesh.SurfaceGetPrimitiveType(0),
                $"{label} inset uses a thin line pass at every viewing distance");

        Aabb silhouette = meshes.Silhouette.GetAabb();
        Aabb contrast = meshes.Contrast.GetAabb();
        TestAssert.Check(IsFinite(silhouette.Position) && IsFinite(silhouette.Size)
                && IsFinite(contrast.Position) && IsFinite(contrast.Size),
                $"{label} meshes contain only finite bounds");
        TestAssert.Check(silhouette.Size.X > 0.01f && silhouette.Size.Y > 0.01f
                && silhouette.Size.Z > 0.01f && contrast.Size.X > 0.01f
                && contrast.Size.Y > 0.01f && contrast.Size.Z > 0.01f,
                $"{label} renders contrast edges on all three axes");
    }

    private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static float Luminance(Color color) =>
            0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
}
