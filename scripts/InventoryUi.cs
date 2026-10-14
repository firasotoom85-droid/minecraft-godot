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
}

/// <summary>One inventory grid cell: icon + count, clickable with either mouse button.</summary>
public partial class InvSlot : Panel
{
    public SlotKind Kind { get; init; }
    public int Index { get; init; }

    private TextureRect _icon = null!;
    private Label _count = null!;

    public event Action<InvSlot, MouseButton>? Clicked;

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
        AddThemeStyleboxOverride("panel", Style(false));
    }

    public void ShowStack(ItemStack stack, bool selected)
    {
        if (_icon == null)
        {
            return;
        }

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

        AddThemeStyleboxOverride("panel", Style(selected));
    }

    private static StyleBoxFlat Style(bool selected)
    {
        StyleBoxFlat sb = new()
        {
            BgColor = selected ? new Color(0.10f, 0.10f, 0.13f, 0.90f) : new Color(0.06f, 0.06f, 0.08f, 0.78f),
            BorderColor = selected ? new Color(1, 1, 1, 0.95f) : new Color(0.35f, 0.35f, 0.38f, 0.85f),
        };
        sb.SetBorderWidthAll(selected ? 3 : 2);
        sb.SetCornerRadiusAll(5);
        return sb;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.Pressed)
        {
            Clicked?.Invoke(this, button.ButtonIndex);
            AcceptEvent();
        }
    }
}

/// <summary>
/// Inventory screen (E): hotbar + backpack, 2x2 or 3x3 crafting with recipe
/// matching, a one-click recipe book and a block palette. Click-to-move
/// stacks through a cursor-held stack; right-click splits or places one.
/// </summary>
public partial class InventoryUi : CanvasLayer
{
    private const int SlotPx = 44;

    private Player _player = null!;
    private Hud _hud = null!;

    private Control _root = null!;
    private Label _title = null!;
    private GridContainer _craftGrid = null!;
    private GridContainer _backpackGrid = null!;
    private HBoxContainer _hotbarRow = null!;
    private InvSlot _resultSlot = null!;
    private VBoxContainer _recipeList = null!;
    private VBoxContainer _paletteList = null!;
    private TextureRect _cursorIcon = null!;
    private Label _cursorCount = null!;

    private readonly InvSlot[] _craftSlots = new InvSlot[9];
    private readonly InvSlot[] _backpackSlots = new InvSlot[27];
    private readonly InvSlot[] _hotbarSlots = new InvSlot[9];
    private readonly ItemStack[] _craft = new ItemStack[9];
    private int _craftSize = 2;
    private ItemStack _cursor = ItemStack.Empty;
    private bool _open;

    public void Setup(Player player, Hud hud)
    {
        _player = player;
        _hud = hud;
        Layer = 9;
        Build();
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

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(center);

        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 10);
        _title = MenuSkin.Title("INVENTORY", 18);
        box.AddChild(_title);
        box.AddChild(BuildColumns());
        center.AddChild(MenuSkin.Wrap(box));

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
        };
        slot.Clicked += OnSlotClicked;
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

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.IsActionPressed("inventory"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (_open && key.Keycode == Key.Escape)
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
    }

    private void Close()
    {
        _open = false;
        _root.Visible = false;
        ReturnCraftItems();
        GiveCursorBack();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _player.InputLocked = false;
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
        }

        _player.Inventory.RaiseChanged();
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

        _resultSlot.ShowStack(
                Recipes.Match(_craft, _craftSize) is { } match
                        ? ItemDb.New(match.Recipe.Result, match.Recipe.ResultCount)
                        : ItemStack.Empty,
                false);
        RefreshRecipes();
    }

    private void RefreshRecipes()
    {
        foreach (Node child in _recipeList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (Recipe recipe in Recipes.All)
        {
            bool can = Recipes.CanCraft(recipe, _player.Inventory);
            Button row = new()
            {
                Text = $"{recipe.Name} ×{recipe.ResultCount}\n{Recipes.IngredientText(recipe)}",
                Disabled = !can,
                CustomMinimumSize = new Vector2(0, 40),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            row.AddThemeFontSizeOverride("font_size", 13);
            row.Pressed += () => CraftFromBook(recipe);
            _recipeList.AddChild(row);
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
    }
}
