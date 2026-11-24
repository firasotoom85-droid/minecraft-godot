namespace Minecraft;

/// <summary>
/// Shared vertical camera-look convention. Every input device and camera mode
/// applies the same user inversion preference at the input boundary.
/// </summary>
internal static class CameraLookInput
{
    internal static float VerticalDelta(float input, bool invertY) =>
            input * (invertY ? -1.0f : 1.0f);
}
