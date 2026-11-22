using Godot;

namespace Minecraft;

/// <summary>
/// Draws a crisp durability meter directly into an existing slot panel.
/// Reusing the slot's draw pass avoids one extra Control node per inventory cell.
/// </summary>
public partial class DurabilitySlotPanel : Panel
{
    private bool _showDurability;
    private float _durabilityFraction;

    public ItemRarity CurrentRarity { get; private set; }

    public void SetRarity(ItemRarity rarity)
    {
        if ((byte)rarity > (byte)ItemRarity.Legendary)
        {
            rarity = ItemRarity.Common;
        }

        if (CurrentRarity == rarity)
        {
            return;
        }

        CurrentRarity = rarity;
        QueueRedraw();
    }

    public void SetDurability(ItemStack stack)
    {
        bool showDurability = EquipmentDurabilityVisual.TryGetFraction(stack, out float fraction);
        if (_showDurability == showDurability
                && (!_showDurability || _durabilityFraction == fraction))
        {
            return;
        }

        _showDurability = showDurability;
        _durabilityFraction = fraction;
        QueueRedraw();
    }

    public override void _Draw()
    {
        base._Draw();
        if (CurrentRarity != ItemRarity.Common && Size.X >= 24.0f && Size.Y >= 18.0f)
        {
            Color rarity = ItemDb.RarityColor(CurrentRarity);
            DrawRect(new Rect2(2.0f, 2.0f, 6.0f, 6.0f),
                    new Color(0.025f, 0.03f, 0.035f, 0.98f));
            DrawRect(new Rect2(3.0f, 3.0f, 4.0f, 4.0f), rarity);
            DrawRect(new Rect2(3.0f, 3.0f, 2.0f, 2.0f), rarity.Lightened(0.24f));
            if (CurrentRarity == ItemRarity.Legendary)
            {
                DrawRect(new Rect2(5.0f, 5.0f, 2.0f, 2.0f), rarity.Darkened(0.18f));
            }
        }

        if (!_showDurability || Size.X < 24.0f || Size.Y < 18.0f)
        {
            return;
        }

        const float height = 3.0f;
        const float bottomInset = 2.0f;
        float width = Mathf.Min(Size.X - 12.0f, Size.X * 0.72f);
        float x = (Size.X - width) * 0.5f;
        float y = Size.Y - height - bottomInset;

        DrawRect(
                new Rect2(x - 1.0f, y - 1.0f, width + 2.0f, height + 2.0f),
                new Color(0.025f, 0.03f, 0.025f, 0.98f));
        DrawRect(
                new Rect2(x, y, width, height),
                new Color(0.16f, 0.18f, 0.15f));

        float fillWidth = width * _durabilityFraction;
        if (fillWidth > 0.0f)
        {
            Color fill = EquipmentDurabilityVisual.BarColor(_durabilityFraction);
            DrawRect(new Rect2(x, y, fillWidth, height), fill);
            if (fillWidth >= 2.0f)
            {
                DrawRect(new Rect2(x, y, fillWidth, 1.0f), fill.Lightened(0.18f));
            }
        }
    }
}
