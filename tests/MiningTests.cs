using System.Collections.Generic;
using Godot;

namespace Minecraft;

public static class MiningTests
{
    public static void Run()
    {
        TestAssert.Check(Player.ShouldEmitMiningTick(0.85f, 0.0),
                "periodic mining feedback plays while the block is still being mined");
        TestAssert.Check(!Player.ShouldEmitMiningTick(1.0f, 0.0),
                "the completion tick suppresses periodic feedback before BlockBroken");
        TestAssert.Check(!Player.ShouldEmitMiningTick(1.0f, 0.2),
                "a non-expired mining sound timer remains silent on completion");

        BlockDb.EnsureTables();

        BlockDb.BlockDef stone = BlockDb.DefOf(BlockId.Stone);
        ToolInfo woodPick = ItemDb.ToolOf(ItemId.WoodPick)!.Value;
        ToolInfo stonePick = ItemDb.ToolOf(ItemId.StonePick)!.Value;
        ToolInfo ironPick = ItemDb.ToolOf(ItemId.IronPick)!.Value;
        ToolInfo diamondPick = ItemDb.ToolOf(ItemId.DiamondPick)!.Value;

        float byHand = BlockDb.MiningSeconds(stone, null);
        float withWood = BlockDb.MiningSeconds(stone, woodPick);
        float withStone = BlockDb.MiningSeconds(stone, stonePick);
        float withIron = BlockDb.MiningSeconds(stone, ironPick);
        float withDiamond = BlockDb.MiningSeconds(stone, diamondPick);
        TestAssert.Check(withWood < byHand, "matching wood pick mines stone faster than hand");
        TestAssert.Check(withStone < withWood, "stone pick mines faster than wood pick");
        TestAssert.Check(withIron < withStone, "iron pick mines faster than stone pick");
        TestAssert.Check(withDiamond < withIron, "diamond pick mines faster than iron pick");
        TestAssert.Check(
            BlockDb.MiningSeconds(stone, ItemDb.ToolOf(ItemId.WoodAxe)) > withWood,
            "wrong tool class receives no pickaxe speed bonus");

        TestAssert.Check(
            BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.Dirt), ItemDb.ToolOf(ItemId.WoodShovel))
                    < BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.Dirt), null),
            "shovel is the preferred tool for dirt");
        TestAssert.Check(
            BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.Log), ItemDb.ToolOf(ItemId.WoodAxe))
                    < BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.Log), null),
            "axe is the preferred tool for logs");

        BlockDb.BlockDef glass = BlockDb.DefOf(BlockId.Glass);
        float glassNormalTime = glass.Hardness * 1.5f;
        TestAssert.Check(
            System.MathF.Abs(BlockDb.MiningSeconds(glass, null) - glassNormalTime) < 0.0001f,
            "glass uses normal hardness timing when no specialized tool is required");
        TestAssert.Check(
            System.MathF.Abs(BlockDb.MiningSeconds(glass, ItemDb.ToolOf(ItemId.WoodPick)) - glassNormalTime) < 0.0001f,
            "unrequired tool classes do not create an artificial speed penalty or bonus");

        BlockDb.BlockDef cactus = BlockDb.DefOf(BlockId.Cactus);
        TestAssert.Equal(ToolKind.None, cactus.Tool, "cactus does not require a preferred tool");
        TestAssert.Check(
            System.MathF.Abs(BlockDb.MiningSeconds(cactus, null) - cactus.Hardness * 1.5f) < 0.0001f,
            "cactus is not slowed by an arbitrary axe requirement");

        TestAssert.Check(BlockDb.CanHarvest(BlockDb.DefOf(BlockId.Dirt), null),
            "dirt drops when mined by hand");
        TestAssert.Check(BlockDb.CanHarvest(stone, ItemDb.ToolOf(ItemId.WoodPick)),
            "wood pick meets stone harvest tier");
        TestAssert.Check(!BlockDb.CanHarvest(stone, ItemDb.ToolOf(ItemId.WoodAxe)),
            "wrong tool class cannot harvest stone");
        TestAssert.Check(!BlockDb.CanHarvest(BlockDb.DefOf(BlockId.IronOre), ItemDb.ToolOf(ItemId.WoodPick)),
            "wood pick cannot harvest iron ore");
        TestAssert.Check(BlockDb.CanHarvest(BlockDb.DefOf(BlockId.IronOre), ItemDb.ToolOf(ItemId.StonePick)),
            "stone pick can harvest iron ore");
        TestAssert.Check(!BlockDb.CanHarvest(BlockDb.DefOf(BlockId.GoldOre), ItemDb.ToolOf(ItemId.StonePick)),
            "stone pick cannot harvest gold ore");
        TestAssert.Check(BlockDb.CanHarvest(BlockDb.DefOf(BlockId.GoldOre), ItemDb.ToolOf(ItemId.IronPick)),
            "iron pick can harvest gold ore");
        TestAssert.Check(!BlockDb.CanHarvest(BlockDb.DefOf(BlockId.DiamondOre), ItemDb.ToolOf(ItemId.StonePick)),
            "stone pick cannot harvest diamond ore");
        TestAssert.Check(BlockDb.CanHarvest(BlockDb.DefOf(BlockId.DiamondOre), ItemDb.ToolOf(ItemId.IronPick)),
            "iron pick can harvest diamond ore");

        TestAssert.Equal(ItemId.Cobblestone, stone.Drop, "stone drops cobblestone");
        TestAssert.Check(!BlockDb.DefOf(BlockId.Glass).Drops, "glass never drops itself");
        TestAssert.Check(!BlockDb.DefOf(BlockId.Leaves).Drops, "leaves do not drop themselves");
        TestAssert.Check(!BlockDb.DefOf(BlockId.Bedrock).Drops, "bedrock has no survival drop");
        TestAssert.Check(float.IsPositiveInfinity(
                BlockDb.MiningSeconds(BlockDb.DefOf(BlockId.Bedrock), diamondPick)),
            "bedrock is not mineable in survival");

        TestAssert.Equal(0, BlockDb.CrackStage(0.0f), "crack stage starts at zero");
        TestAssert.Equal(0, BlockDb.CrackStage(0.099f), "crack stage remains zero below ten percent");
        TestAssert.Equal(1, BlockDb.CrackStage(0.1f), "crack stage advances at ten percent");
        TestAssert.Equal(5, BlockDb.CrackStage(0.59f), "crack stage advances progressively");
        TestAssert.Equal(9, BlockDb.CrackStage(0.99f), "crack stage reaches final texture");
        TestAssert.Equal(9, BlockDb.CrackStage(1.0f), "completed mining clamps to final stage");

        TestAssert.Check(BlockDb.IsCross(BlockId.TallGrass), "tall grass uses cross geometry");
        TestAssert.Check(BlockDb.IsCross(BlockId.FlowerRed), "flowers use cross geometry");
        TestAssert.Check(BlockDb.IsCross(BlockId.Torch), "torches use cross geometry");
        TestAssert.Check(BlockDb.IsCross(BlockId.Wheat2), "wheat uses cross geometry");
        foreach (BlockId replaceable in new[]
        {
            BlockId.TallGrass, BlockId.FlowerRed, BlockId.FlowerYellow,
            BlockId.Wheat0, BlockId.Wheat1, BlockId.Wheat2, BlockId.RiverReeds,
        })
        {
            TestAssert.Check(BlockDb.IsReplaceable(replaceable), $"{replaceable} is explicitly replaceable during placement");
            TestAssert.Check(Player.CanReplacePlacementDestination(replaceable),
                    $"{replaceable} can be replaced by a placed block");
        }
        foreach (BlockId protectedBlock in new[]
        {
            BlockId.Torch, BlockId.Leaves, BlockId.Lava, BlockId.Stone, BlockId.Bedrock,
        })
        {
            TestAssert.Check(!BlockDb.IsReplaceable(protectedBlock),
                    $"{protectedBlock} is not implicitly replaceable");
            TestAssert.Check(!Player.CanReplacePlacementDestination(protectedBlock),
                    $"{protectedBlock} still blocks placement");
        }
        TestAssert.Check(Player.CanReplacePlacementDestination(BlockId.Air),
                "air remains a valid placement destination");
        TestAssert.Check(Player.CanReplacePlacementDestination(BlockId.Water),
                "water retains its existing placement replacement behavior");

        TestAssert.Check(
            BlockDb.RayIntersectsCross(
                    new Vector3(0.5f, 0.5f, -1.0f), new Vector3(0.0f, 0.0f, 1.0f),
                    Vector3I.Zero, 0.0f, 3.0f, out float crossDistance),
            "ray can hit a cross-shaped plant without a physics collider");
        TestAssert.Check(System.MathF.Abs(crossDistance - 1.5f) < 0.001f,
            "cross-plant ray reports the correct intersection distance");
        TestAssert.Check(!BlockDb.RayIntersectsCross(
                    new Vector3(0.5f, 1.5f, -1.0f), new Vector3(0.0f, 0.0f, 1.0f),
                    Vector3I.Zero, 0.0f, 3.0f, out _),
            "cross-plant ray rejects hits above the visible geometry");
        TestAssert.Check(!BlockDb.RayIntersectsCross(
                    new Vector3(0.5f, 0.5f, -1.0f), new Vector3(0.0f, 0.0f, 1.0f),
                    Vector3I.Zero, 0.0f, 1.0f, out _),
            "cross-plant ray respects its maximum reach");

        foreach (BlockId id in System.Enum.GetValues<BlockId>())
        {
            BlockDb.BlockDef def = BlockDb.DefOf(id);
            TestAssert.Check(float.IsFinite(def.Hardness) && def.Hardness >= 0.0f,
                $"{id} has valid mining hardness");
        }

        Inventory inventory = new();
        inventory.Set(0, ItemDb.New(ItemId.WoodPick, 1));
        TestAssert.Check(!inventory.DamageTool(0), "fresh tool survives one use");
        TestAssert.Equal(59, inventory.Get(0).Durability, "tool wear consumes one durability point");
        inventory.Set(0, new ItemStack(ItemId.WoodPick, 1, 1));
        TestAssert.Check(inventory.DamageTool(0), "last durability point breaks the tool");
        TestAssert.Check(inventory.Get(0).IsEmpty, "broken tool slot is cleared");
        TestAssert.Check(!inventory.DamageTool(-1), "invalid inventory slot cannot consume durability");
        CheckMinedRewardDelivery();
    }

    private static void CheckMinedRewardDelivery()
    {
        Player player = new() { Mode = GameMode.Survival };
        List<(ItemId Id, int Count)> gathered = [];
        player.ItemGathered += (id, count) => gathered.Add((id, count));

        player.GiveDrop(ItemId.RawIron, 3);
        TestAssert.Equal(3, player.Inventory.CountOf(ItemId.RawIron),
                "ordinary mined reward enters the existing inventory");
        TestAssert.Equal(1, gathered.Count,
                "one mined reward emits exactly one gather event");
        TestAssert.Equal(ItemId.RawIron, gathered[0].Id,
                "mined reward reports its actual gathered item");
        TestAssert.Equal(3, gathered[0].Count,
                "mined reward reports the accepted quantity");

        Inventory capacity = new();
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            capacity.Set(slot, ItemDb.New(ItemId.Dirt, 64));
        capacity.Set(0, ItemDb.New(ItemId.RawIron, 60));
        Player.MinedDrop[] fitPlan = [new(ItemId.RawIron, 4)];
        Player.MinedDrop[] overflowPlan =
        [
            new(ItemId.RawIron, 2),
            new(ItemId.RawIron, 3),
        ];
        TestAssert.Check(Player.CanFitMinedDrops(capacity, fitPlan),
                "mining preflight accepts a reward that fits the remaining stack space");
        TestAssert.Check(!Player.CanFitMinedDrops(capacity, overflowPlan),
                "mining preflight aggregates same-item rewards before removing the block");

        Inventory oneSlot = new();
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            oneSlot.Set(slot, ItemDb.New(ItemId.Dirt, 64));
        oneSlot.Set(0, ItemStack.Empty);
        Player.MinedDrop[] mixedPlan =
        [
            new(ItemId.Wheat, 1),
            new(ItemId.Seeds, 1),
        ];
        TestAssert.Check(!Player.CanFitMinedDrops(oneSlot, mixedPlan),
                "mining preflight reserves empty slots across different drop item types");

        Player partial = new() { Mode = GameMode.Survival };
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            partial.Inventory.Set(slot, ItemDb.New(ItemId.Dirt, 64));
        partial.Inventory.Set(0, ItemDb.New(ItemId.RawIron, 60));
        int partialGathered = 0;
        partial.ItemGathered += (_, count) => partialGathered += count;
        partial.GiveDrop(ItemId.RawIron, 8);
        TestAssert.Equal(64, partial.Inventory.CountOf(ItemId.RawIron),
                "partial reward delivery adds only the four remaining stack spaces");
        TestAssert.Equal(4, partialGathered,
                "gather events exclude the undelivered overflow quantity");

        player.Free();
        partial.Free();
    }
}
