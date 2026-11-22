using Godot;

namespace Minecraft;

/// <summary>
/// Shared pixel-art durability state for equipment icons and their inventory slots.
/// Four icon states are sufficient at 32px; the slot bar remains continuously scaled.
/// </summary>
public static class EquipmentDurabilityVisual
{
    public static bool TryGetFraction(ItemStack stack, out float fraction)
    {
        int maximum = EquipmentProgression.MaxDurability(stack.Id);
        if (stack.IsEmpty || maximum <= 0)
        {
            fraction = 0.0f;
            return false;
        }

        fraction = Mathf.Clamp((float)stack.Durability / maximum, 0.0f, 1.0f);
        return true;
    }

    public static int WearStage(ItemStack stack) =>
        TryGetFraction(stack, out float fraction) ? WearStage(fraction) : 0;

    public static int WearStage(int durability, int maximum)
    {
        if (maximum <= 0)
        {
            return 0;
        }

        float fraction = Mathf.Clamp((float)durability / maximum, 0.0f, 1.0f);
        return WearStage(fraction);
    }

    public static int WearStage(float fraction)
    {
        float remaining = Mathf.Clamp(fraction, 0.0f, 1.0f);
        return remaining > 0.75f ? 0 : remaining > 0.5f ? 1 : remaining > 0.25f ? 2 : 3;
    }

    public static Color BarColor(float fraction)
    {
        float remaining = Mathf.Clamp(fraction, 0.0f, 1.0f);
        return remaining > 0.5f
                ? new Color(0.28f, 0.84f, 0.36f)
                : remaining > 0.25f
                        ? new Color(1.0f, 0.72f, 0.18f)
                        : new Color(0.94f, 0.24f, 0.18f);
    }
}
