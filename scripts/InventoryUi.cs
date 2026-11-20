using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

public enum SlotKind
{
    Craft,
    Result,
    Backpack,
    Hotbar,
    Container,
    Armor,
    FurnaceInput,
    FurnaceFuel,
    FurnaceOutput,
}

/// <summary>One inventory grid cell: icon + count, clickable with either mouse button.</summary>
public partial class InvSlot : Panel
{
    public SlotKind Kind { get; init; }
    public int Index { get; init; }

    private TextureRect _icon = null!;
    private Label _count = null!;
    private bool _hovered;
    private bool _hasStack;

    public event Action<InvSlot, MouseButton>? Clicked;
    public event Action<InvSlot, InvSlot>? Dropped;

    public override void _Ready()
    {
        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 4);
        AddChild(_icon);
        _count = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _count.AddThemeFontSizeOverride("font_size", 12);
        _count.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.95f));
        _count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _count.AddThemeConstantOverride("outline_size", 4);
        _count.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.Minsize, 2);
        AddChild(_count);
        AddThemeStyleboxOverride("panel", MenuSkin.SlotStyle());
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public void ShowStack(ItemStack stack, bool selected)
    {
        if (_icon == null)
        {
            return;
        }

        _hasStack = !stack.IsEmpty;
        TooltipText = stack.IsEmpty ? "" : $"{ItemDb.NameOf(stack.Id)}\\n{(ItemDb.ToolOf(stack.Id) is { } toolInfo ? $"Durability: {stack.Durability}/{toolInfo.Durability}" : $"Count: {stack.Count}")}";
        _icon.Texture = stack.IsEmpty ? null : ItemDb.Icon(stack.Id);
        _icon.Visible = !stack.IsEmpty;
        if (stack.IsEmpty)
        {
            _count.Text = "";
        }
        else if (ItemDb.ToolOf(stack.Id) is { } tool)
        {
            _count.Text = stack.Durability < tool.Durability ? stack.Durability.ToString() : "";
        }
        else
        {
            _count.Text = stack.Count > 1 ? stack.Count.ToString() : "";
        }

        AddThemeStyleboxOverride("panel", MenuSkin.SlotStyle(selected, _hovered));
    }

    private void OnMouseEntered()
    {
        _hovered = true;
        AddThemeStyleboxOverride("panel", MenuSkin.SlotStyle(false, true));
    }

    private void OnMouseExited()
    {
        _hovered = false;
        AddThemeStyleboxOverride("panel", MenuSkin.SlotStyle());
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!_hasStack) return default;
        Panel preview = new()
        {
            CustomMinimumSize = new Vector2(48, 48),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        preview.AddThemeStyleboxOverride("panel", MenuSkin.SlotStyle(true, false));
        SetDragPreview(preview);
        return Variant.From(this);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return data.AsGodotObject() is InvSlot source && source != this && source._hasStack;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.AsGodotObject() is InvSlot source && source != this)
            Dropped?.Invoke(source, this);
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Dispatch on release so Godot can recognize a drag gesture first.
        if (@event is InputEventMouseButton button && !button.Pressed)
        {
            Clicked?.Invoke(this, button.ButtonIndex);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            Clicked?.Invoke(this, MouseButton.Left);
            AcceptEvent();
        }
    }
}

/// <summary>
/// Inventory screen (E): hotbar + backpack, 2x2 or 3x3 crafting with recipe
/// matching, a one-click recipe book and a block palette. Click-to-move
/// stacks through a cursor-held stack; right-click splits or places one.
/// </summary>
public partial class InventoryUi : CanvasLayer, IModalUi
{
    private const int SlotPx = 44;

    private Player _player = null!;
    private Hud _hud = null!;

    private Control _root = null!;
    private CenterContainer _center = null!;
    private Label _title = null!;
    private GridContainer _craftGrid = null!;
    private GridContainer _backpackGrid = null!;
    private HBoxContainer _hotbarRow = null!;
    private VBoxContainer _containerBox = null!;
    private PanelContainer _containerPanel = null!;
    private GridContainer _containerGrid = null!;
    private VBoxContainer _furnaceBox = null!;
    private PanelContainer _furnacePanel = null!;
    private ProgressBar _furnaceProgress = null!;
    private InvSlot _resultSlot = null!;
    private VBoxContainer _recipeList = null!;
    private VBoxContainer _paletteList = null!;
    private readonly List<(Button Button, Recipe Recipe)> _recipeRows = [];
    private TextureRect _cursorIcon = null!;
    private Label _cursorCount = null!;

    private readonly InvSlot[] _craftSlots = new InvSlot[9];
    private readonly InvSlot[] _backpackSlots = new InvSlot[27];
    private readonly InvSlot[] _hotbarSlots = new InvSlot[9];
    private readonly InvSlot[] _armorSlots = new InvSlot[Inventory.ArmorSlots];
    private readonly InvSlot[] _containerSlots = new InvSlot[Container.Size];
    private readonly InvSlot[] _furnaceSlots = new InvSlot[3];
    private readonly ItemStack[] _craft = new ItemStack[9];
    private Container? _container;
    private FurnaceData? _furnace;
    private double _furnaceRefreshTimer;
    private int _craftSize = 2;
    private ItemStack _cursor = ItemStack.Empty;
    private bool _open;

    public void Setup(Player player, Hud hud)
    {
        _player = player;
        _hud = hud;
        Layer = 9;
        AddToGroup(UiFocus.ModalGroup);
        Build();
        GetViewport().SizeChanged += ApplyResponsiveScale;
        ApplyResponsiveScale();
        _player.TableUsed += () => Open(3);
        _player.Inventory.Changed += () =>
        {
            if (_open)
            {
                Refresh();
            }
        };
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    private void Build()
    {
        _root = new Control
        {
            Name = "InventoryRoot",
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        ColorRect dim = new()
        {
            Color = new Color(0, 0, 0, 0.35f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        _center = new CenterContainer();
        _center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_center);

        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        _containerBox = BuildContainer();
        _containerPanel = MenuSkin.Wrap(_containerBox);
        _containerPanel.Visible = false;
        box.AddChild(_containerPanel);
        _furnaceBox = BuildFurnace();
        _furnacePanel = MenuSkin.Wrap(_furnaceBox);
        _furnacePanel.Visible = false;
        box.AddChild(_furnacePanel);
        _title = MenuSkin.Title("INVENTORY", 18);
        box.AddChild(_title);
        box.AddChild(BuildColumns());
        _center.AddChild(MenuSkin.Wrap(box));

        _cursorIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(32, 32),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _root.AddChild(_cursorIcon);
        _cursorCount = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _cursorCount.AddThemeFontSizeOverride("font_size", 13);
        _cursorCount.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.95f));
        _cursorCount.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _cursorCount.AddThemeConstantOverride("outline_size", 4);
        _root.AddChild(_cursorCount);
        BuildRecipeRows();
    }

    private void ApplyResponsiveScale()
    {
        if (_center == null || !IsInstanceValid(_center)) return;
        Vector2 viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        float scale = Mathf.Clamp(Mathf.Min(viewport.X / 1450.0f, viewport.Y / 900.0f), 0.68f, 1.0f);
        _center.PivotOffset = viewport * 0.5f;
        _center.Scale = Vector2.One * scale;
    }

    private VBoxContainer BuildContainer()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(MenuSkin.Title("CHEST", 14));
        _containerGrid = new GridContainer
        {
            Columns = 9,
        };
        _containerGrid.AddThemeConstantOverride("h_separation", 6);
        _containerGrid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < Container.Size; i++)
        {
            _containerSlots[i] = MakeSlot(SlotKind.Container, i);
            _containerGrid.AddChild(_containerSlots[i]);
        }

        box.AddChild(_containerGrid);
        return box;
    }

    private VBoxContainer BuildFurnace()
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(MenuSkin.Title("FURNACE", 14));
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(FurnaceColumn(box, "INPUT", SlotKind.FurnaceInput, 0));
        row.AddChild(new Label { Text = "→", VerticalAlignment = VerticalAlignment.Center });
        row.AddChild(FurnaceColumn(box, "FUEL", SlotKind.FurnaceFuel, 1));
        row.AddChild(new Label { Text = "→", VerticalAlignment = VerticalAlignment.Center });
        row.AddChild(FurnaceColumn(box, "OUTPUT", SlotKind.FurnaceOutput, 2));
        box.AddChild(row);

        _furnaceProgress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = FurnaceData.SmeltSeconds,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(220, 10),
        };
        box.AddChild(_furnaceProgress);
        return box;
    }

    private VBoxContainer FurnaceColumn(VBoxContainer parent, string label, SlotKind kind, int index)
    {
        VBoxContainer col = new();
        col.AddThemeConstantOverride("separation", 4);
        Label text = new() { Text = label, HorizontalAlignment = HorizontalAlignment.Center };
        text.AddThemeFontSizeOverride("font_size", 11);
        text.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.92f, 0.8f));
        col.AddChild(text);
        _furnaceSlots[index] = MakeSlot(kind, index);
        col.AddChild(_furnaceSlots[index]);
        return col;
    }

    private Control BuildColumns()
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 18);

        VBoxContainer craftCol = new();
        craftCol.AddThemeConstantOverride("separation", 8);
        craftCol.AddChild(MenuSkin.Title("CRAFT", 14));
        _craftGrid = new GridContainer
        {
            Columns = _craftSize,
        };
        _craftGrid.AddThemeConstantOverride("h_separation", 6);
        _craftGrid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < 9; i++)
        {
            _craftSlots[i] = MakeSlot(SlotKind.Craft, i);
        }

        craftCol.AddChild(_craftGrid);
        Label arrow = new()
        {
            Text = "→",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        arrow.AddThemeFontSizeOverride("font_size", 22);
        craftCol.AddChild(arrow);
        _resultSlot = MakeSlot(SlotKind.Result, 0);
        HBoxContainer resultRow = new();
        resultRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        resultRow.AddChild(_resultSlot);
        resultRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        craftCol.AddChild(resultRow);
        row.AddChild(craftCol);

        VBoxContainer bagCol = new();
        bagCol.AddThemeConstantOverride("separation", 8);
        bagCol.AddChild(MenuSkin.Title("BACKPACK", 14));
        _backpackGrid = new GridContainer
        {
            Columns = 9,
        };
        _backpackGrid.AddThemeConstantOverride("h_separation", 6);
        _backpackGrid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < 27; i++)
        {
            _backpackSlots[i] = MakeSlot(SlotKind.Backpack, i);
            _backpackGrid.AddChild(_backpackSlots[i]);
        }

        bagCol.AddChild(_backpackGrid);
        bagCol.AddChild(MenuSkin.Title("ARMOR", 14));
        HBoxContainer armorRow = new();
        armorRow.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < Inventory.ArmorSlots; i++)
        {
            _armorSlots[i] = MakeSlot(SlotKind.Armor, i);
            armorRow.AddChild(_armorSlots[i]);
        }

        bagCol.AddChild(armorRow);
        bagCol.AddChild(MenuSkin.Title("HOTBAR", 14));
        _hotbarRow = new HBoxContainer();
        _hotbarRow.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < 9; i++)
        {
            _hotbarSlots[i] = MakeSlot(SlotKind.Hotbar, i);
            _hotbarRow.AddChild(_hotbarSlots[i]);
        }

        bagCol.AddChild(_hotbarRow);
        row.AddChild(bagCol);

        VBoxContainer sideCol = new();
        sideCol.AddThemeConstantOverride("separation", 8);
        HBoxContainer tabs = new();
        tabs.AddThemeConstantOverride("separation", 8);
        tabs.AddChild(MenuSkin.MakeButton("Recipes", () => ShowTab(true), 92));
        tabs.AddChild(MenuSkin.MakeButton("Palette", () => ShowTab(false), 92));
        sideCol.AddChild(tabs);
        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(320, 340),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        VBoxContainer scrollBox = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _recipeList = new VBoxContainer();
        _recipeList.AddThemeConstantOverride("separation", 4);
        _paletteList = new VBoxContainer
        {
            Visible = false,
        };
        _paletteList.AddThemeConstantOverride("separation", 4);
        scrollBox.AddChild(_recipeList);
        scrollBox.AddChild(_paletteList);
        scroll.AddChild(scrollBox);
        sideCol.AddChild(scroll);
        row.AddChild(sideCol);
        return row;
    }

    private InvSlot MakeSlot(SlotKind kind, int index)
    {
        InvSlot slot = new()
        {
            Kind = kind,
            Index = index,
            CustomMinimumSize = new Vector2(SlotPx, SlotPx),
            FocusMode = Control.FocusModeEnum.All,
        };
        slot.AddThemeStyleboxOverride("focus", MenuSkin.SlotStyle(true));
        slot.Clicked += OnSlotClicked;
        slot.Dropped += OnSlotDropped;
        return slot;
    }

    // -----------------------------------------------------------------------
    // Open / close
    // -----------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetTree().Paused)
        {
            return;
        }

        if (@event.IsActionPressed("inventory"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (_open && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle()
    {
        if (_open)
        {
            Close();
        }
        else
        {
            Open(_player.TableInReach() ? 3 : 2);
        }
    }

    /// <summary>Open the inventory screen backed by a chest at <paramref name="pos"/>.</summary>
    public void OpenChest(Vector3I pos)
    {
        if (_player.World == null)
        {
            return;
        }

        if (!_open)
        {
            Open(2);
        }

        _container = _player.World.GetContainer(pos);
        _containerPanel.Visible = true;
        _furnacePanel.Visible = false;
        _furnace = null;
        _title.Text = "INVENTORY";
        Refresh();
    }

    /// <summary>Open the inventory screen backed by a furnace at <paramref name="pos"/>.</summary>
    public void OpenFurnace(Vector3I pos)
    {
        if (_player.World == null)
        {
            return;
        }

        if (!_open)
        {
            Open(2);
        }

        _furnace = _player.World.GetFurnace(pos);
        _containerPanel.Visible = false;
        _container = null;
        _furnacePanel.Visible = true;
        _title.Text = "INVENTORY";
        _furnaceRefreshTimer = 0.0;
        Refresh();
    }

    private void Open(int size)
    {
        if (_open && size == _craftSize)
        {
            return;
        }

        if (_open)
        {
            ReturnCraftItems();
        }

        _open = true;
        _craftSize = size;
        _title.Text = size == 3 ? "CRAFTING TABLE" : "INVENTORY";
        _containerPanel.Visible = false;
        _container = null;
        _furnacePanel.Visible = false;
        _furnace = null;
        _craftGrid.Columns = size;
        foreach (Node child in _craftGrid.GetChildren())
        {
            _craftGrid.RemoveChild(child);
        }

        for (int i = 0; i < size * size; i++)
        {
            _craftGrid.AddChild(_craftSlots[i]);
        }

        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.InputLocked = true;
        Refresh();
        UiFocus.FocusFirst(_backpackSlots[0]);
    }

    private void Close()
    {
        _open = false;
        _root.Visible = false;
        _containerPanel.Visible = false;
        _container = null;
        _furnacePanel.Visible = false;
        _furnace = null;
        ReturnCraftItems();
        GiveCursorBack();
        UiFocus.Restore(_player);
    }

    public void ForceClose()
    {
        if (_open)
        {
            Close();
        }
    }

    private void ReturnCraftItems()
    {
        for (int i = 0; i < _craft.Length; i++)
        {
            if (_craft[i].IsEmpty)
            {
                continue;
            }

            int leftover = _player.Inventory.Add(_craft[i]);
            _craft[i] = leftover <= 0 ? ItemStack.Empty : _craft[i] with { Count = leftover };
        }
    }

    private void GiveCursorBack()
    {
        if (_cursor.IsEmpty)
        {
            return;
        }

        int leftover = _player.Inventory.Add(_cursor);
        _cursor = leftover <= 0 ? ItemStack.Empty : _cursor with { Count = leftover };
        if (leftover > 0)
        {
            _hud.ShowToast("Inventory full");
        }
    }

    // -----------------------------------------------------------------------
    // Slot interaction
    // -----------------------------------------------------------------------

    private void OnSlotClicked(InvSlot slot, MouseButton button)
    {
        if (button == MouseButton.Left && Input.IsKeyPressed(Key.Shift) && TransferSlotToInventory(slot))
        {
            _player.Inventory.RaiseChanged();
            Refresh();
            return;
        }

        switch (slot.Kind)
        {
            case SlotKind.Result:
                TakeResult();
                break;
            case SlotKind.Craft:
                ClickStack(ref _craft[slot.Index], button);
                break;
            case SlotKind.Backpack:
                ClickStack(ref _player.Inventory.Backpack[slot.Index], button);
                break;
            case SlotKind.Hotbar:
                _player.Select(slot.Index);
                ClickStack(ref _player.Inventory.Hotbar[slot.Index], button);
                break;
            case SlotKind.Container:
                if (_container != null)
                {
                    ClickStack(ref _container.Slots[slot.Index], button);
                }

                break;
            case SlotKind.Armor:
                ClickArmor(slot.Index, button);
                break;
            case SlotKind.FurnaceInput:
                if (_furnace != null)
                {
                    ClickStack(ref _furnace.Input, button);
                }

                break;
            case SlotKind.FurnaceFuel:
                if (_furnace != null)
                {
                    ClickStack(ref _furnace.Fuel, button);
                }

                break;
            case SlotKind.FurnaceOutput:
                TakeFurnaceOutput(button);
                break;
        }

        _player.Inventory.RaiseChanged();
    }

    private bool TransferSlotToInventory(InvSlot slot)
    {
        // Never add a stack back into the same player inventory it already occupies.
        if (slot.Kind is SlotKind.Backpack or SlotKind.Hotbar or SlotKind.Armor or SlotKind.Result)
        {
            return false;
        }

        ItemStack stack = slot.Kind switch
        {
            SlotKind.Craft => _craft[slot.Index],
            SlotKind.Backpack => _player.Inventory.Backpack[slot.Index],
            SlotKind.Hotbar => _player.Inventory.Hotbar[slot.Index],
            SlotKind.Container when _container != null => _container.Slots[slot.Index],
            SlotKind.FurnaceInput when _furnace != null => _furnace.Input,
            SlotKind.FurnaceFuel when _furnace != null => _furnace.Fuel,
            SlotKind.FurnaceOutput when _furnace != null => _furnace.Output,
            _ => ItemStack.Empty,
        };
        if (stack.IsEmpty) return false;
        int leftover = _player.Inventory.Add(stack);
        int moved = stack.Count - leftover;
        if (moved <= 0) return false;
        ItemStack remainder = leftover <= 0 ? ItemStack.Empty : stack with { Count = leftover };
        switch (slot.Kind)
        {
            case SlotKind.Craft: _craft[slot.Index] = remainder; break;
            case SlotKind.Backpack: _player.Inventory.Backpack[slot.Index] = remainder; break;
            case SlotKind.Hotbar: _player.Inventory.Hotbar[slot.Index] = remainder; break;
            case SlotKind.Container when _container != null: _container.Slots[slot.Index] = remainder; break;
            case SlotKind.FurnaceInput when _furnace != null: _furnace.Input = remainder; break;
            case SlotKind.FurnaceFuel when _furnace != null: _furnace.Fuel = remainder; break;
            case SlotKind.FurnaceOutput when _furnace != null: _furnace.Output = remainder; break;
        }
        return true;
    }

    private ItemStack ReadSlot(InvSlot slot) => slot.Kind switch
    {
        SlotKind.Craft => _craft[slot.Index],
        SlotKind.Backpack => _player.Inventory.Backpack[slot.Index],
        SlotKind.Hotbar => _player.Inventory.Hotbar[slot.Index],
        SlotKind.Armor => _player.Inventory.Armor[slot.Index],
        SlotKind.Container when _container != null => _container.Slots[slot.Index],
        SlotKind.FurnaceInput when _furnace != null => _furnace.Input,
        SlotKind.FurnaceFuel when _furnace != null => _furnace.Fuel,
        SlotKind.FurnaceOutput when _furnace != null => _furnace.Output,
        _ => ItemStack.Empty,
    };

    private void WriteSlot(InvSlot slot, ItemStack stack)
    {
        switch (slot.Kind)
        {
            case SlotKind.Craft: _craft[slot.Index] = stack; break;
            case SlotKind.Backpack: _player.Inventory.Backpack[slot.Index] = stack; break;
            case SlotKind.Hotbar: _player.Inventory.Hotbar[slot.Index] = stack; break;
            case SlotKind.Armor: _player.Inventory.Armor[slot.Index] = stack; break;
            case SlotKind.Container when _container != null: _container.Slots[slot.Index] = stack; break;
            case SlotKind.FurnaceInput when _furnace != null: _furnace.Input = stack; break;
            case SlotKind.FurnaceFuel when _furnace != null: _furnace.Fuel = stack; break;
            case SlotKind.FurnaceOutput when _furnace != null: _furnace.Output = stack; break;
        }
    }

    private void OnSlotDropped(InvSlot source, InvSlot target)
    {
        if (source.Kind == SlotKind.Result || target.Kind is SlotKind.Result or SlotKind.FurnaceOutput)
            return;
        if (source.Kind == target.Kind && source.Index == target.Index)
            return;

        ItemStack moving = ReadSlot(source);
        ItemStack destination = ReadSlot(target);
        if (moving.IsEmpty)
            return;

        if (target.Kind == SlotKind.Armor
                && (ItemDb.ArmorOf(moving.Id) is not { } armor || armor.Slot != target.Index || moving.Count != 1))
            return;
        if (source.Kind == SlotKind.Armor && !destination.IsEmpty
                && (ItemDb.ArmorOf(destination.Id) is not { } swappedArmor || swappedArmor.Slot != source.Index || destination.Count != 1))
            return;

        if (destination.IsEmpty)
        {
            WriteSlot(target, moving);
            WriteSlot(source, ItemStack.Empty);
        }
        else if (destination.Id == moving.Id && destination.Count < ItemDb.MaxStack(destination.Id))
        {
            int amount = Math.Min(ItemDb.MaxStack(destination.Id) - destination.Count, moving.Count);
            WriteSlot(target, destination with { Count = destination.Count + amount });
            WriteSlot(source, moving.Count == amount ? ItemStack.Empty : moving with { Count = moving.Count - amount });
        }
        else
        {
            if (target.Kind == SlotKind.Armor
                    && (ItemDb.ArmorOf(moving.Id) is not { } incoming || incoming.Slot != target.Index || moving.Count != 1))
                return;
            if (source.Kind == SlotKind.Armor
                    && (ItemDb.ArmorOf(destination.Id) is not { } outgoing || outgoing.Slot != source.Index || destination.Count != 1))
                return;
            WriteSlot(target, moving);
            WriteSlot(source, destination);
        }

        _player.Inventory.RaiseChanged();
        Refresh();
    }

    private void ClickStack(ref ItemStack slot, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            if (_cursor.IsEmpty)
            {
                _cursor = slot;
                slot = ItemStack.Empty;
            }
            else if (slot.IsEmpty)
            {
                slot = _cursor;
                _cursor = ItemStack.Empty;
            }
            else if (slot.Id == _cursor.Id && slot.Count < ItemDb.MaxStack(slot.Id))
            {
                int take = Math.Min(ItemDb.MaxStack(slot.Id) - slot.Count, _cursor.Count);
                slot = slot with { Count = slot.Count + take };
                _cursor = _cursor with { Count = _cursor.Count - take };
                if (_cursor.Count <= 0)
                {
                    _cursor = ItemStack.Empty;
                }
            }
            else
            {
                (_cursor, slot) = (slot, _cursor);
            }
        }
        else if (button == MouseButton.Right)
        {
            if (_cursor.IsEmpty)
            {
                int half = (slot.Count + 1) / 2;
                _cursor = slot with { Count = half };
                slot = slot.Count - half <= 0 ? ItemStack.Empty : slot with { Count = slot.Count - half };
            }
            else if (slot.IsEmpty)
            {
                slot = _cursor with { Count = 1 };
                _cursor = _cursor.Count <= 1 ? ItemStack.Empty : _cursor with { Count = _cursor.Count - 1 };
            }
            else if (slot.Id == _cursor.Id && slot.Count < ItemDb.MaxStack(slot.Id))
            {
                slot = slot with { Count = slot.Count + 1 };
                _cursor = _cursor.Count <= 1 ? ItemStack.Empty : _cursor with { Count = _cursor.Count - 1 };
            }
        }
    }

    private void ClickArmor(int index, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        ItemStack current = _player.Inventory.Armor[index];
        if (_cursor.IsEmpty)
        {
            _cursor = current;
            _player.Inventory.Armor[index] = ItemStack.Empty;
        }
        else if (ItemDb.ArmorOf(_cursor.Id) is { } armor && armor.Slot == index)
        {
            if (current.IsEmpty)
            {
                _player.Inventory.Armor[index] = _cursor;
                _cursor = ItemStack.Empty;
            }
            else
            {
                (_cursor, _player.Inventory.Armor[index]) = (current, _cursor);
            }
        }
    }

    private void TakeFurnaceOutput(MouseButton button)
    {
        if (_furnace == null || _furnace.Output.IsEmpty)
        {
            return;
        }

        ItemStack output = _furnace.Output;
        if (button == MouseButton.Left)
        {
            if (_cursor.IsEmpty)
            {
                _cursor = output;
                _furnace.Output = ItemStack.Empty;
            }
            else if (_cursor.Id == output.Id
                    && _cursor.Count + output.Count <= ItemDb.MaxStack(output.Id))
            {
                _cursor = _cursor with { Count = _cursor.Count + output.Count };
                _furnace.Output = ItemStack.Empty;
            }
        }
        else if (button == MouseButton.Right && _cursor.IsEmpty)
        {
            _cursor = output with { Count = 1 };
            _furnace.Output = output.Count <= 1 ? ItemStack.Empty : output with { Count = output.Count - 1 };
        }
    }

    private void TakeResult()
    {
        if (Recipes.Match(_craft, _craftSize) is not { } match)
        {
            return;
        }

        ItemStack result = ItemDb.New(match.Recipe.Result, match.Recipe.ResultCount);
        if (_cursor.IsEmpty)
        {
            _cursor = result;
        }
        else if (_cursor.Id == result.Id
                && _cursor.Count + result.Count <= ItemDb.MaxStack(result.Id))
        {
            _cursor = _cursor with { Count = _cursor.Count + result.Count };
        }
        else if (_player.Inventory.Add(result) > 0)
        {
            _hud.ShowToast("Inventory full");
            return;
        }

        Recipes.ConsumeFromGrid(_craft, _craftSize, match.Recipe, match.Row, match.Col);
        _player.Inventory.RaiseChanged();
    }

    private void CraftFromBook(Recipe recipe)
    {
        if (!Recipes.CanCraft(recipe, _player.Inventory))
        {
            return;
        }

        if (!_player.Inventory.CanFit(recipe.Result, recipe.ResultCount))
        {
            _hud.ShowToast("Inventory full");
            return;
        }

        if (!Recipes.Craft(recipe, _player.Inventory))
        {
            return;
        }

        _hud.ShowToast($"Crafted {ItemDb.NameOf(recipe.Result)}");
        _player.Inventory.RaiseChanged();
    }

    private void PaletteGive(ItemId item)
    {
        _player.Inventory.Hotbar[_player.Selected] = ItemDb.New(item, 64);
        _player.Inventory.RaiseChanged();
        _hud.ShowToast($"{ItemDb.NameOf(item)} ×64 → slot {_player.Selected + 1}");
    }

    // -----------------------------------------------------------------------
    // Refresh
    // -----------------------------------------------------------------------

    private void Refresh()
    {
        Inventory inv = _player.Inventory;
        for (int i = 0; i < 9; i++)
        {
            _hotbarSlots[i].ShowStack(inv.Hotbar[i], i == _player.Selected);
            _craftSlots[i].ShowStack(_craft[i], false);
        }

        for (int i = 0; i < 27; i++)
        {
            _backpackSlots[i].ShowStack(inv.Backpack[i], false);
        }

        for (int i = 0; i < Container.Size; i++)
        {
            _containerSlots[i].ShowStack(_container?.Slots[i] ?? ItemStack.Empty, false);
        }

        for (int i = 0; i < Inventory.ArmorSlots; i++)
        {
            _armorSlots[i].ShowStack(_player.Inventory.Armor[i], false);
        }

        RefreshFurnace();
        _resultSlot.ShowStack(
                Recipes.Match(_craft, _craftSize) is { } match
                        ? ItemDb.New(match.Recipe.Result, match.Recipe.ResultCount)
                        : ItemStack.Empty,
                false);
        RefreshRecipes();
    }

    private void RefreshFurnace()
    {
        if (_furnace == null)
        {
            return;
        }

        _furnaceSlots[0].ShowStack(_furnace.Input, false);
        _furnaceSlots[1].ShowStack(_furnace.Fuel, false);
        _furnaceSlots[2].ShowStack(_furnace.Output, false);
        _furnaceProgress.Value = _furnace.SmeltTime;
    }

    private void BuildRecipeRows()
    {
        foreach (Recipe recipe in Recipes.All)
        {
            Button row = new()
            {
                Text = $"{recipe.Name} ×{recipe.ResultCount}\n{Recipes.IngredientText(recipe)}",
                CustomMinimumSize = new Vector2(0, 40),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            row.AddThemeFontSizeOverride("font_size", 13);
            Recipe captured = recipe;
            row.Pressed += () => CraftFromBook(captured);
            _recipeList.AddChild(row);
            _recipeRows.Add((row, captured));
        }
    }

    private void RefreshRecipes()
    {
        // Update the existing rows; rebuilding ~35 buttons on every inventory
        // change was needless churn while the screen is open.
        foreach ((Button button, Recipe recipe) in _recipeRows)
        {
            button.Disabled = !Recipes.CanCraft(recipe, _player.Inventory)
                    || !_player.Inventory.CanFit(recipe.Result, recipe.ResultCount);
        }
    }

    private void BuildPalette()
    {
        foreach (Node child in _paletteList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (ItemId item in ItemDb.Placeable)
        {
            Button row = new()
            {
                Text = ItemDb.NameOf(item),
                Icon = ItemDb.Icon(item),
                CustomMinimumSize = new Vector2(0, 40),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            row.AddThemeFontSizeOverride("font_size", 13);
            ItemId captured = item;
            row.Pressed += () => PaletteGive(captured);
            _paletteList.AddChild(row);
        }
    }

    private void ShowTab(bool recipes)
    {
        _recipeList.Visible = recipes;
        _paletteList.Visible = !recipes;
        if (!recipes && _paletteList.GetChildCount() == 0)
        {
            BuildPalette();
        }
    }

    public override void _Process(double delta)
    {
        if (!_open)
        {
            return;
        }

        _cursorIcon.Visible = !_cursor.IsEmpty;
        _cursorIcon.Texture = _cursor.IsEmpty ? null : ItemDb.Icon(_cursor.Id);
        _cursorCount.Text = !_cursor.IsEmpty && _cursor.Count > 1 ? _cursor.Count.ToString() : "";
        Vector2 pos = GetViewport().GetMousePosition() + new Vector2(10, 10);
        _cursorIcon.GlobalPosition = pos;
        _cursorCount.GlobalPosition = pos + new Vector2(18, 16);

        if (_furnace != null)
        {
            _furnaceRefreshTimer -= delta;
            if (_furnaceRefreshTimer <= 0.0)
            {
                _furnaceRefreshTimer = 0.2;
                RefreshFurnace();
            }
        }
    }
}
