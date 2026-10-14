using Godot;

namespace Minecraft;

/// <summary>
/// Small extension methods over Godot structs — something GDScript cannot
/// express at all: Godot's color/vector structs are immutable here, so
/// "modify one component" becomes a single readable expression.
/// </summary>
public static class Extensions
{
    public static Color WithAlpha(this Color c, float alpha) => new(c.R, c.G, c.B, alpha);

    public static Vector3 WithX(this Vector3 v, float x) => new(x, v.Y, v.Z);

    public static Vector2 XZ(this Vector3 v) => new(v.X, v.Z);
}
