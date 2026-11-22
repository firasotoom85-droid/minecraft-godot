using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Forward+ visual fixture for inspecting wear stages at native and reduced UI sizes.
/// Run: godot --path . res://tests/EquipmentDurabilityVisualCapture.tscn
/// </summary>
public partial class EquipmentDurabilityVisualCapture : Control
{
    private static readonly ItemId[] Samples =
    [
        ItemId.WoodPick,
        ItemId.IronAxe,
        ItemId.DiamondSword,
        ItemId.Bow,
        ItemId.Shield,
        ItemId.IronHelmet,
        ItemId.DiamondChestplate,
        ItemId.IronLegs,
        ItemId.DiamondBoots,
    ];

    private static readonly float[] Ratios = [1.0f, 0.74f, 0.49f, 0.24f];

    public override async void _Ready()
    {
        GetWindow().Title = "Equipment Durability Icon Validation";
        GetWindow().Size = new Vector2I(960, 760);

        ColorRect background = new()
        {
            Color = new Color(0.055f, 0.070f, 0.075f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        MarginContainer margin = new()
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 24.0f,
            OffsetTop = 18.0f,
            OffsetRight = -24.0f,
            OffsetBottom = -18.0f,
        };
        AddChild(margin);

        VBoxContainer layout = new();
        layout.AddThemeConstantOverride("separation", 10);
        margin.AddChild(layout);

        Label title = new()
        {
            Text = "EQUIPMENT CONDITION  ·  32PX PROCEDURAL ICONS",
            CustomMinimumSize = new Vector2(0, 30),
        };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", new Color(0.91f, 0.91f, 0.86f));
        layout.AddChild(title);

        Label legend = new()
        {
            Text = "FULL  ·  74%  ·  49%  ·  24%     /     icon scuffs + green / amber / red meter",
            CustomMinimumSize = new Vector2(0, 24),
        };
        legend.AddThemeFontSizeOverride("font_size", 13);
        legend.AddThemeColorOverride("font_color", new Color(0.67f, 0.72f, 0.68f));
        layout.AddChild(legend);

        GridContainer grid = new()
        {
            Columns = 5,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 9);
        layout.AddChild(grid);

        List<(InvSlot Slot, ItemStack Stack)> pending = [];
        for (int row = 0; row < Samples.Length; row++)
        {
            ItemId id = Samples[row];
            Label itemName = new()
            {
                Text = ItemDb.NameOf(id),
                CustomMinimumSize = new Vector2(142, 58),
                VerticalAlignment = VerticalAlignment.Center,
            };
            itemName.AddThemeFontSizeOverride("font_size", 13);
            itemName.AddThemeColorOverride("font_color", new Color(0.86f, 0.87f, 0.82f));
            grid.AddChild(itemName);

            int maximum = EquipmentProgression.MaxDurability(id);
            for (int column = 0; column < Ratios.Length; column++)
            {
                int durability = Mathf.RoundToInt(maximum * Ratios[column]);
                ItemStack stack = ItemDb.New(id, 1) with { Durability = durability };
                InvSlot slot = new()
                {
                    Kind = SlotKind.Backpack,
                    Index = row * Ratios.Length + column,
                    CustomMinimumSize = new Vector2(58, 58),
                };
                grid.AddChild(slot);
                pending.Add((slot, stack));
            }
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach ((InvSlot slot, ItemStack stack) in pending)
        {
            slot.ShowStack(stack, false);
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        string path = Path.Combine(OS.GetUserDataDir(), "equipment-durability-visual-validation.png");
        Error result = GetViewport().GetTexture().GetImage().SavePng(path);
        if (result != Error.Ok)
        {
            GD.PushError($"Equipment durability capture failed: {result}");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"EQUIPMENT_DURABILITY_CAPTURE {path}");
        GetTree().Quit();
    }
}
