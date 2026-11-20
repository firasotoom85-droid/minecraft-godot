using Godot;

namespace Minecraft;

public static class FluidTests
{
    public static void Run()
    {
        TestAssert.Equal(0, BlockDb.WaterLevel(BlockId.Water), "source water is level zero");
        TestAssert.Check(BlockDb.IsWater(BlockId.Water), "source water is water");
        TestAssert.Check(!BlockDb.IsWater(BlockId.Lava), "lava remains distinct from water");
        TestAssert.Check(!BlockDb.IsWater(BlockId.Stone), "stone is not water");
        TestAssert.Equal(-1, BlockDb.WaterLevel(BlockId.Air), "air has no fluid level");
        TestAssert.Equal(BlockId.Water, FluidRules.BlockForLevel(0), "level zero maps to source");

        for (int level = 1; level <= FluidRules.MaxFlowLevel; level++)
        {
            BlockId id = FluidRules.BlockForLevel(level);
            TestAssert.Check(BlockDb.IsWater(id), $"flow level {level} is recognized");
            TestAssert.Equal(level, BlockDb.WaterLevel(id), $"flow level {level} round-trips");
            TestAssert.Check(!BlockDb.IsSolid(id), $"flow level {level} is non-solid");
            TestAssert.Check(BlockDb.WaterHeight(id) > 0.0f && BlockDb.WaterHeight(id) < 1.0f,
                    $"flow level {level} has partial voxel height");
        }

        TestAssert.Equal(BlockId.Air, FluidRules.BlockForLevel(-1), "negative level is rejected");
        TestAssert.Equal(BlockId.Air, FluidRules.BlockForLevel(8), "flow levels stop at seven");
        TestAssert.Equal(1, FluidRules.NextHorizontalLevel(0), "source spreads as level one");
        TestAssert.Equal(7, FluidRules.NextHorizontalLevel(6), "level six spreads to seven");
        TestAssert.Equal(-1, FluidRules.NextHorizontalLevel(7), "level seven stops spreading");
        TestAssert.Equal(-1, FluidRules.NextHorizontalLevel(-1), "non-water does not spread");
        TestAssert.Equal(1, FluidRules.ResolveFlowLevel(0, -1, -1, -1, -1), "water above feeds a fall");
        TestAssert.Equal(1, FluidRules.ResolveFlowLevel(-1, 0, -1, -1, -1), "side source feeds level one");
        TestAssert.Equal(4, FluidRules.ResolveFlowLevel(-1, 3, 5, -1, -1),
                "nearest horizontal supply wins deterministically");
        TestAssert.Equal(-1, FluidRules.ResolveFlowLevel(-1, -1, -1, -1, -1),
                "unsupported flow recedes");
        TestAssert.Equal(-1, FluidRules.ResolveFlowLevel(-1, 7, 7, 7, 7),
                "maximum flow distance is bounded");
        TestAssert.Check(FluidRules.CanRegenerateSource(2, true, true),
                "two horizontal sources above a solid floor can regenerate");
        TestAssert.Check(!FluidRules.CanRegenerateSource(1, true, true),
                "one source cannot regenerate an infinite source");
        TestAssert.Check(!FluidRules.CanRegenerateSource(2, false, true),
                "source regeneration requires a solid floor");

        TestAssert.Check(FluidRules.CanFlowInto(BlockId.Air), "water enters air");
        TestAssert.Check(FluidRules.CanFlowInto(BlockId.TallGrass), "water displaces plants");
        TestAssert.Check(FluidRules.CanFlowInto(BlockId.Torch), "water displaces torches");
        TestAssert.Check(!FluidRules.CanFlowInto(BlockId.Stone), "solid terrain blocks water");
        TestAssert.Check(!FluidRules.CanFlowInto(BlockId.Lava), "lava is handled by a reaction");
        TestAssert.Check(!FluidRules.CanFlowInto(BlockId.Water), "source water cannot be replaced");
        TestAssert.Equal(BlockId.Cobblestone, FluidRules.ReactWithWater(BlockId.Lava),
                "water converts lava to the available solid reaction block");
        TestAssert.Equal(BlockId.Stone, FluidRules.ReactWithWater(BlockId.Stone),
                "unrelated terrain remains unchanged");
        TestAssert.Check(!BlockDb.RenderFace(BlockId.Water, BlockId.FlowingWater3),
                "adjacent water interfaces are suppressed");
        TestAssert.Check(BlockDb.RenderFace(BlockId.Water, BlockId.Lava),
                "water and lava retain separate boundaries");

        float dry = WaterPhysics.Immersion(false, false, false);
        float shallow = WaterPhysics.Immersion(true, false, false);
        float body = WaterPhysics.Immersion(true, true, false);
        float submerged = WaterPhysics.Immersion(true, true, true);
        TestAssert.Equal(0.0f, dry, "dry player has no immersion");
        TestAssert.Check(shallow > 0.0f && !WaterPhysics.ShouldSwim(shallow),
                "ankle-deep water remains wading");
        TestAssert.Check(WaterPhysics.ShouldSwim(body), "body-deep water enables swimming");
        TestAssert.Equal(1.0f, submerged, "all three immersion samples are wet");

        Vector3 buoyant = WaterPhysics.Step(Vector3.Zero, Vector3.Zero, 1.0f, false, false, 0.1f);
        Vector3 rising = WaterPhysics.Step(Vector3.Zero, Vector3.Zero, 1.0f, true, false, 0.1f);
        Vector3 diving = WaterPhysics.Step(Vector3.Zero, Vector3.Zero, 1.0f, false, true, 0.1f);
        TestAssert.Check(buoyant.Y > 0.0f, "buoyancy provides upward movement");
        TestAssert.Check(rising.Y > buoyant.Y, "Space adds upward swim acceleration");
        TestAssert.Check(diving.Y < 0.0f, "Shift drives the player down");
        Vector3 ceiling = WaterPhysics.Step(Vector3.Zero, Vector3.Zero, 1.0f,
                true, false, 0.1f, true, false);
        Vector3 floor = WaterPhysics.Step(Vector3.Zero, Vector3.Zero, 1.0f,
                false, true, 0.1f, false, true);
        TestAssert.Equal(0.0f, ceiling.Y, "swim-up input is blocked by a ceiling");
        TestAssert.Equal(0.0f, floor.Y, "dive input is blocked by a floor");

        Vector3 horizontal = Vector3.Zero;
        for (int i = 0; i < 12; i++)
            horizontal = WaterPhysics.Step(horizontal, Vector3.Right, 1.0f, false, false, 0.1f);
        TestAssert.Check(horizontal.X > 0.0f && horizontal.X <= WaterPhysics.HorizontalSpeed + 0.001f,
                "underwater movement accelerates smoothly and stays capped");
        TestAssert.Equal(0.0f, horizontal.Z, "swimming adds no sideways drift");
        TestAssert.Check(WaterPhysics.MiningSpeedMultiplier(true) < 1.0f, "underwater mining is slower");
        TestAssert.Equal(1.0f, WaterPhysics.MiningSpeedMultiplier(false), "surface mining is unchanged");
    }
}
