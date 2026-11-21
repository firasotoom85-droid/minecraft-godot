using Godot;

namespace Minecraft;

/// <summary>
/// Bucket state transitions and world interactions. A bucket can only remove
/// an actual source cell; flowing water can never be promoted into a bucket.
/// World edits always use VoxelWorld.SetBlock and are committed before the
/// caller replaces the held inventory item.
/// </summary>
public static class BucketMechanics
{
    public static bool IsBucket(ItemId id) =>
            id is ItemId.EmptyBucket or ItemId.WaterBucket or ItemId.LavaBucket;

    public static bool TryCollect(VoxelWorld world, Vector3I position, ItemId held,
            out ItemId result)
    {
        result = held;
        if (held != ItemId.EmptyBucket
                || !world.TryGetLoadedBlock(position.X, position.Y, position.Z, out BlockId current))
            return false;

        ItemId filled = current switch
        {
            BlockId.Water => ItemId.WaterBucket,
            BlockId.Lava => ItemId.LavaBucket,
            _ => ItemId.None,
        };
        if (filled == ItemId.None || !world.SetBlock(position.X, position.Y, position.Z, BlockId.Air))
            return false;

        result = filled;
        return true;
    }

    public static bool TryCollect(VoxelWorld world, Vector3I position,
            Inventory inventory, int slot, out BlockId collectedFluid)
    {
        collectedFluid = BlockId.Air;
        if ((uint)slot >= Inventory.SlotCount
                || inventory.Get(slot) is not { Id: ItemId.EmptyBucket, Count: 1 } held
                || !TryCollect(world, position, held.Id, out ItemId replacement))
            return false;

        collectedFluid = replacement == ItemId.WaterBucket ? BlockId.Water : BlockId.Lava;
        inventory.Set(slot, ItemDb.New(replacement, 1));
        inventory.RaiseChanged();
        return true;
    }

    public static bool TryPlace(VoxelWorld world, Vector3I position, ItemId held,
            out ItemId result, out BlockId placedFluid)
    {
        result = held;
        placedFluid = BlockId.Air;
        BlockId fluid = held switch
        {
            ItemId.WaterBucket => BlockId.Water,
            ItemId.LavaBucket => BlockId.Lava,
            _ => BlockId.Air,
        };
        if (fluid == BlockId.Air
                || !world.TryGetLoadedBlock(position.X, position.Y, position.Z, out BlockId target)
                || !FluidRules.CanFlowInto(target)
                || !world.SetBlock(position.X, position.Y, position.Z, fluid))
            return false;

        result = ItemId.EmptyBucket;
        placedFluid = fluid;
        return true;
    }

    public static bool TryPlace(VoxelWorld world, Vector3I position,
            Inventory inventory, int slot, out BlockId placedFluid)
    {
        placedFluid = BlockId.Air;
        if ((uint)slot >= Inventory.SlotCount) return false;
        ItemStack held = inventory.Get(slot);
        if (held.Count != 1 || !TryPlace(world, position, held.Id,
                    out ItemId replacement, out placedFluid))
            return false;

        inventory.Set(slot, ItemDb.New(replacement, 1));
        inventory.RaiseChanged();
        return true;
    }

    /// <summary>Eight bookkeeping units represent one full source bucket.</summary>
    public static int FluidUnits(ItemId bucket) => bucket switch
    {
        ItemId.WaterBucket or ItemId.LavaBucket => 8,
        _ => 0,
    };

    /// <summary>Flowing water carries less than a source; lava is source-only.</summary>
    public static int FluidUnits(BlockId block) => block switch
    {
        BlockId.Water => 8,
        BlockId.Lava => 8,
        _ when BlockDb.IsWater(block) => 8 - BlockDb.WaterLevel(block),
        _ => 0,
    };
}
