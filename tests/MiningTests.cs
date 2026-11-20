using Godot;

namespace Minecraft;

public static class MiningTests
{
    public static void Run()
    {
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
    }
}
