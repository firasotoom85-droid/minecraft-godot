using System;
using System.Collections.Generic;

namespace Minecraft;

/// <summary>
/// One furnace's slots and burn/smelt timers. Kept independent of the scene so
/// smelting continues while the chunk is unloaded and the state saves as plain
/// numbers.
/// </summary>
public sealed class FurnaceData
{
    public const float SmeltSeconds = 10.0f;

    public ItemStack Input;
    public ItemStack Fuel;
    public ItemStack Output;
    public float BurnTime;
    public float BurnMax;
    public float SmeltTime;

    private ItemId? _smeltResult;

    public bool IsBurning => BurnTime > 0.0f;

    public bool IsEmpty => Input.IsEmpty && Fuel.IsEmpty && Output.IsEmpty && BurnTime <= 0.0f && SmeltTime <= 0.0f;

    /// <summary>Advance one tick; returns true when visible state changed.</summary>
    public bool Tick(float delta)
    {
        bool changed = false;
        if (BurnTime > 0.0f)
        {
            BurnTime = Math.Max(BurnTime - delta, 0.0f);
            changed = true;
        }

        ItemId? result = Input.IsEmpty ? null : ItemDb.SmeltsTo(Input.Id);
        bool canAccept = result is { } r && CanAccept(r);
        if (result != _smeltResult)
        {
            // Keep the null sentinel: comparing null against a non-nullable
            // ItemId.None is always true and would dirty the world every tick.
            _smeltResult = result;
            SmeltTime = 0.0f;
            changed = true;
        }

        if (BurnTime <= 0.0f && canAccept && !Fuel.IsEmpty)
        {
            float seconds = ItemDb.FuelSeconds(Fuel.Id);
            if (seconds > 0.0f)
            {
                Fuel = Fuel.Count <= 1 ? ItemStack.Empty : Fuel with { Count = Fuel.Count - 1 };
                BurnTime = BurnMax = seconds;
                changed = true;
            }
        }

        if (canAccept && BurnTime > 0.0f)
        {
            SmeltTime += delta;
            if (SmeltTime >= SmeltSeconds)
            {
                Input = Input.Count <= 1 ? ItemStack.Empty : Input with { Count = Input.Count - 1 };
                AddOutput(result!.Value);
                SmeltTime = 0.0f;
                changed = true;
            }
        }
        else if (SmeltTime > 0.0f)
        {
            SmeltTime = 0.0f;
            changed = true;
        }

        return changed;
    }

    private bool CanAccept(ItemId id)
    {
        if (Output.IsEmpty)
        {
            return true;
        }

        return Output.Id == id && Output.Count < ItemDb.MaxStack(id);
    }

    private void AddOutput(ItemId id)
    {
        if (Output.IsEmpty)
        {
            Output = ItemDb.New(id, 1);
        }
        else
        {
            Output = Output with { Count = Output.Count + 1 };
        }
    }

    public List<ItemStackSave> ToSave()
    {
        List<ItemStackSave> data = [];
        if (!Input.IsEmpty)
        {
            data.Add(new ItemStackSave(0, (int)Input.Id, Input.Count, Input.Durability, Input.Sharpness, Input.Protection, Input.Unbreaking));
        }

        if (!Fuel.IsEmpty)
        {
            data.Add(new ItemStackSave(1, (int)Fuel.Id, Fuel.Count, Fuel.Durability, Fuel.Sharpness, Fuel.Protection, Fuel.Unbreaking));
        }

        if (!Output.IsEmpty)
        {
            data.Add(new ItemStackSave(2, (int)Output.Id, Output.Count, Output.Durability, Output.Sharpness, Output.Protection, Output.Unbreaking));
        }

        return data;
    }

    public void LoadFrom(List<ItemStackSave>? items)
    {
        Input = Fuel = Output = ItemStack.Empty;
        if (items != null)
        {
            foreach (ItemStackSave s in items)
            {
                ItemStack stack = new((ItemId)s.Id, s.Count, s.Durability);
                switch (s.Slot)
                {
                    case 0:
                        Input = stack;
                        break;
                    case 1:
                        Fuel = stack;
                        break;
                    case 2:
                        Output = stack;
                        break;
                }
            }
        }

        _smeltResult = Input.IsEmpty ? null : ItemDb.SmeltsTo(Input.Id);
    }
}
