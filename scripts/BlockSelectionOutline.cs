using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Builds the two complementary passes used by the first-person block target:
/// a world-space dark silhouette and fine, surface-offset light inset tracks.
/// The target mesh is deliberately independent of atlas materials and lighting.
/// </summary>
internal static class BlockSelectionOutline
{
    internal readonly record struct MeshSet(Mesh Silhouette, Mesh Contrast);

    internal static readonly Color SilhouetteColor = new(0.035f, 0.045f, 0.062f, 1.0f);
    internal static readonly Color ContrastColor = new(0.94f, 0.97f, 1.0f, 1.0f);

    private const float EdgeRadius = 0.012f;
    private const float ContrastInset = 0.028f;
    private const float ContrastSurfaceOffset = 0.004f;

    private readonly record struct FaceEdge(Vector3 A, Vector3 B, Vector3 FaceCenter, Vector3 FaceNormal);
    private readonly record struct ShapeOutlineEdge(int Face, Vector3I A, Vector3I B);

    internal static StandardMaterial3D CreateSilhouetteMaterial() => new()
    {
        AlbedoColor = SilhouetteColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        Roughness = 1.0f,
    };

    internal static StandardMaterial3D CreateContrastMaterial() => new()
    {
        AlbedoColor = ContrastColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        Roughness = 1.0f,
    };

    internal static MeshSet BuildCube()
    {
        List<FaceEdge> edges = new(24);
        Vector3 half = new(0.5f, 0.5f, 0.5f);

        for (int face = 0; face < 6; face++)
        {
            Vector3 faceCenter = Vector3.Zero;
            for (int vertex = 0; vertex < 4; vertex++)
                faceCenter += BlockDb.FaceVerts[face * 4 + vertex] - half;
            faceCenter *= 0.25f;

            for (int edge = 0; edge < 4; edge++)
            {
                Vector3 a = BlockDb.FaceVerts[face * 4 + edge] - half;
                Vector3 b = BlockDb.FaceVerts[face * 4 + ((edge + 1) & 3)] - half;
                edges.Add(new FaceEdge(a, b, faceCenter, BlockDb.FaceDirs[face]));
            }
        }

        return BuildFromEdges(edges);
    }

    internal static MeshSet BuildShape(BlockId id, byte state)
    {
        if (!BlockShapes.IsShape(id) || BlockShapes.IsFullCube(id, state))
            return BuildCube();

        // Cancel shared co-planar micro-face edges before making either pass.
        // This keeps a slab or stair silhouette free of half-cell grid seams.
        Dictionary<ShapeOutlineEdge, FaceEdge> exposedEdges = [];
        Span<Vector3I> corners = stackalloc Vector3I[4];

        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        for (int x = 0; x < 2; x++)
        {
            if (!BlockShapes.Occupied(id, state, x, y, z))
                continue;

            Vector3 cellCenter = new(
                    (x + 0.5f) * 0.5f - 0.5f,
                    (y + 0.5f) * 0.5f - 0.5f,
                    (z + 0.5f) * 0.5f - 0.5f);

            for (int face = 0; face < 6; face++)
            {
                if (!BlockShapes.FaceExposed(id, state, x, y, z, face, BlockId.Air, 0))
                    continue;

                Vector3 faceNormal = BlockDb.FaceDirs[face];
                Vector3 faceCenter = cellCenter + faceNormal * 0.25f;

                for (int vertex = 0; vertex < 4; vertex++)
                {
                    Vector3 unit = BlockDb.FaceVerts[face * 4 + vertex];
                    corners[vertex] = new Vector3I(
                            x + Mathf.RoundToInt(unit.X),
                            y + Mathf.RoundToInt(unit.Y),
                            z + Mathf.RoundToInt(unit.Z));
                }

                for (int edge = 0; edge < 4; edge++)
                {
                    Vector3I a = corners[edge];
                    Vector3I b = corners[(edge + 1) & 3];
                    bool ordered = Compare(a, b) <= 0;
                    ShapeOutlineEdge key = ordered
                            ? new ShapeOutlineEdge(face, a, b)
                            : new ShapeOutlineEdge(face, b, a);

                    Vector3 localA = GridToLocal(a);
                    Vector3 localB = GridToLocal(b);
                    if (!exposedEdges.Remove(key))
                        exposedEdges[key] = new FaceEdge(localA, localB, faceCenter, faceNormal);
                }
            }
        }

        return BuildFromEdges(new List<FaceEdge>(exposedEdges.Values));
    }

    private static MeshSet BuildFromEdges(IReadOnlyList<FaceEdge> faceEdges)
    {
        SurfaceTool silhouette = new();
        silhouette.Begin(Mesh.PrimitiveType.Triangles);
        HashSet<(Vector3I A, Vector3I B)> uniqueEdges = [];

        foreach (FaceEdge edge in faceEdges)
        {
            Vector3I a = Quantize(edge.A);
            Vector3I b = Quantize(edge.B);
            if (Compare(a, b) > 0)
                (a, b) = (b, a);
            if (!uniqueEdges.Add((a, b)))
                continue;

            Vector3 delta = edge.B - edge.A;
            int axis = Mathf.Abs(delta.X) > 0.0001f ? 0
                    : Mathf.Abs(delta.Y) > 0.0001f ? 1 : 2;
            Vector3 size = new(EdgeRadius * 2.0f, EdgeRadius * 2.0f, EdgeRadius * 2.0f);
            size[axis] = Mathf.Abs(delta[axis]) + EdgeRadius * 2.0f;
            AddBox(silhouette, (edge.A + edge.B) * 0.5f, size);
        }

        SurfaceTool contrast = new();
        contrast.Begin(Mesh.PrimitiveType.Lines);
        foreach (FaceEdge edge in faceEdges)
        {
            Vector3 midpoint = (edge.A + edge.B) * 0.5f;
            Vector3 inward = edge.FaceCenter - midpoint;
            if (inward.LengthSquared() < 0.000001f)
                continue;

            Vector3 offset = inward.Normalized() * ContrastInset
                    + edge.FaceNormal * ContrastSurfaceOffset;
            contrast.SetColor(Colors.White);
            contrast.AddVertex(edge.A + offset);
            contrast.AddVertex(edge.B + offset);
        }

        return new MeshSet(silhouette.Commit(), contrast.Commit());
    }

    private static void AddBox(SurfaceTool surface, Vector3 center, Vector3 size)
    {
        for (int face = 0; face < 6; face++)
        {
            surface.SetNormal(BlockDb.FaceDirs[face]);
            surface.SetColor(Colors.White);
            foreach (int triangleVertex in Mesher.TriOrder)
            {
                Vector3 vertex = BlockDb.FaceVerts[face * 4 + triangleVertex];
                surface.AddVertex(center + (vertex - new Vector3(0.5f, 0.5f, 0.5f)) * size);
            }
        }
    }

    private static Vector3 GridToLocal(Vector3I point) =>
            new(point.X * 0.5f - 0.5f, point.Y * 0.5f - 0.5f, point.Z * 0.5f - 0.5f);

    private static Vector3I Quantize(Vector3 point) =>
            new(Mathf.RoundToInt(point.X * 2.0f),
                Mathf.RoundToInt(point.Y * 2.0f),
                Mathf.RoundToInt(point.Z * 2.0f));

    private static int Compare(Vector3I left, Vector3I right)
    {
        if (left.X != right.X) return left.X.CompareTo(right.X);
        if (left.Y != right.Y) return left.Y.CompareTo(right.Y);
        return left.Z.CompareTo(right.Z);
    }
}
