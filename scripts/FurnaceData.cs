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
    private Dictionary<int, ItemStackSave>? _unrecognizedSavedSlots;

    public bool IsBurning => BurnTime > 0.0f;

    public bool HasUnrecognizedSaveSlot(int slot) =>
            _unrecognizedSavedSlots?.ContainsKey(slot) ?? false;

    public void ClearUnrecognizedSaveSlot(int slot)
    {
        if (_unrecognizedSavedSlots == null)
        {
            return;
        }

        _unrecognizedSavedSlots.Remove(slot);
        if (_unrecognizedSavedSlots.Count == 0)
        {
            _unrecognizedSavedSlots = null;
        }
    }

    public bool IsEmpty => Input.IsEmpty && Fuel.IsEmpty && Output.IsEmpty
            && BurnTime <= 0.0f && SmeltTime <= 0.0f
            && _unrecognizedSavedSlots is not { Count: > 0 };

    public bool CanSetSlot(int slot, ItemStack stack)
    {
        if (slot is not (0 or 1) || HasUnrecognizedSaveSlot(slot))
            return false;
        return stack.IsEmpty || ItemStackSaveValidator.FitsFurnaceSlot(slot, stack);
    }

    public bool TrySetSlot(int slot, ItemStack stack)
    {
        if (!CanSetSlot(slot, stack)) return false;
        ClearUnrecognizedSaveSlot(slot);
        if (slot == 0) Input = stack;
        else Fuel = stack;
        return true;
    }

    /// <summary>Advance one tick; carries elapsed time across fuel boundaries.</summary>
    public bool Tick(float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0.0f
                || (!Input.IsEmpty && !ItemStackSaveValidator.FitsFurnaceSlot(0, Input))
                || (!Output.IsEmpty && !ItemStackSaveValidator.FitsFurnaceSlot(2, Output)))
        {
            return false;
        }

        bool changed = false;
        ItemId? result = Input.IsEmpty ? null : ItemDb.SmeltsTo(Input.Id);
        if (result != _smeltResult)
        {
            _smeltResult = result;
            SmeltTime = 0.0f;
            changed = true;
        }

        bool canAccept = result is { } output && CanAccept(output);
        float remaining = delta;
        while (remaining > 0.000001f)
        {
            if (BurnTime <= 0.0f)
            {
                if (!canAccept || Fuel.IsEmpty
                        || !ItemStackSaveValidator.FitsFurnaceSlot(1, Fuel))
                {
                    break;
                }

                float seconds = ItemDb.FuelSeconds(Fuel.Id);
                if (seconds <= 0.0f)
                {
                    break;
                }

                Fuel = Fuel.Count <= 1 ? ItemStack.Empty : Fuel with { Count = Fuel.Count - 1 };
                BurnTime = BurnMax = seconds;
                changed = true;
            }

            float step = Math.Min(remaining, BurnTime);
            BurnTime = Math.Max(BurnTime - step, 0.0f);
            remaining -= step;
            changed = true;

            if (!canAccept)
            {
                if (SmeltTime > 0.0f)
                {
                    SmeltTime = 0.0f;
                    changed = true;
                }

                continue;
            }

            SmeltTime += step;
            while (SmeltTime >= SmeltSeconds && canAccept)
            {
                Input = Input.Count <= 1 ? ItemStack.Empty : Input with { Count = Input.Count - 1 };
                AddOutput(result!.Value);
                SmeltTime -= SmeltSeconds;
                changed = true;

                // Input is a homogeneous stack, but output capacity can become
                // exhausted while processing a large elapsed-time step.
                canAccept = !Input.IsEmpty && ItemDb.SmeltsTo(Input.Id) is { } next
                        && next == result.Value && CanAccept(next);
            }

            if (!canAccept && SmeltTime > 0.0f)
            {
                SmeltTime = 0.0f;
                changed = true;
            }
        }

        if (SmeltTime > 0.0f && (!canAccept || (BurnTime <= 0.0f
                && (Fuel.IsEmpty || !ItemStackSaveValidator.FitsFurnaceSlot(1, Fuel)))))
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
            return !HasUnrecognizedSaveSlot(2);
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

        if (_unrecognizedSavedSlots != null)
        {
            foreach (KeyValuePair<int, ItemStackSave> entry in _unrecognizedSavedSlots)
            {
                ItemStack runtime = entry.Key switch
                {
                    0 => Input,
                    1 => Fuel,
                    _ => Output,
                };
                if (runtime.IsEmpty)
                {
                    data.Add(entry.Value);
                }
            }
        }

        return data;
    }

    public void LoadFrom(List<ItemStackSave>? items)
    {
        Input = Fuel = Output = ItemStack.Empty;
        _smeltResult = null;
        _unrecognizedSavedSlots = null;
        if (items != null)
        {
            bool[] loadedSlots = new bool[3];
            foreach (ItemStackSave saved in items)
            {
                if (saved.Slot < 0 || saved.Slot >= loadedSlots.Length)
                {
                    ItemStackSaveValidator.ReportInvalid("furnace", saved, "slot is out of range");
                    continue;
                }

                if (ItemStackSaveValidator.IsUnrecognizedItemId(saved.Id))
                {
                    if (loadedSlots[saved.Slot])
                    {
                        ItemStackSaveValidator.ReportInvalid("furnace", saved, "duplicate slot record");
                        continue;
                    }

                    loadedSlots[saved.Slot] = true;
                    _unrecognizedSavedSlots ??= new Dictionary<int, ItemStackSave>();
                    _unrecognizedSavedSlots.Add(saved.Slot, saved);
                    ItemStackSaveValidator.ReportUnrecognized("furnace", saved);
                    continue;
                }

                if (!ItemStackSaveValidator.TryCreate(saved, out ItemStack stack, out string reason))
                {
                    ItemStackSaveValidator.ReportInvalid("furnace", saved, reason);
                    continue;
                }

                if (!ItemStackSaveValidator.FitsFurnaceSlot(saved.Slot, stack))
                {
                    ItemStackSaveValidator.ReportInvalid("furnace", saved,
                            "item is not accepted in this furnace slot");
                    continue;
                }

                if (loadedSlots[saved.Slot])
                {
                    ItemStackSaveValidator.ReportInvalid("furnace", saved, "duplicate slot record");
                    continue;
                }

                loadedSlots[saved.Slot] = true;
                switch (saved.Slot)
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
