using Godot;

namespace Minecraft;

public static class FluidRules
{
    public const int MaxFlowLevel = 7;

    public static BlockId BlockForLevel(int level)
    {
        if (level == 0) return BlockId.Water;
        return level is >= 1 and <= MaxFlowLevel
                ? (BlockId)((int)BlockId.FlowingWater1 + level - 1)
                : BlockId.Air;
    }

    public static int NextHorizontalLevel(int level) =>
            level is >= 0 and < MaxFlowLevel ? level + 1 : -1;

    public static int ResolveFlowLevel(int above, int north, int south, int east, int west)
    {
        if (above >= 0) return 1;
        int best = -1;
        if (north >= 0) best = best < 0 ? north : Mathf.Min(best, north);
        if (south >= 0) best = best < 0 ? south : Mathf.Min(best, south);
        if (east >= 0) best = best < 0 ? east : Mathf.Min(best, east);
        if (west >= 0) best = best < 0 ? west : Mathf.Min(best, west);
        return best < 0 ? -1 : NextHorizontalLevel(best);
    }

    public static BlockId ReactWithWater(BlockId neighbor) =>
            neighbor == BlockId.Lava ? BlockId.Cobblestone : neighbor;

    public static bool CanFlowInto(BlockId id) =>
            id is BlockId.Air or BlockId.TallGrass or BlockId.FlowerRed
                    or BlockId.FlowerYellow or BlockId.Torch or BlockId.Ladder
                    or BlockId.Wheat0 or BlockId.Wheat1 or BlockId.Wheat2;
}

public static class WaterPhysics
{
    public const float HorizontalSpeed = 3.15f;
    public const float UnderwaterMiningMultiplier = 0.35f;
    public const float SwimUpSpeed = 4.1f;
    public const float DiveSpeed = -3.5f;

    public static float Immersion(bool feetWet, bool torsoWet, bool headWet)
    {
        int wet = (feetWet ? 1 : 0) + (torsoWet ? 1 : 0) + (headWet ? 1 : 0);
        return wet / 3.0f;
    }

    public static bool ShouldSwim(float immersion) => immersion >= 0.66f;
    public static float MiningSpeedMultiplier(bool underwater) =>
            underwater ? UnderwaterMiningMultiplier : 1.0f;

    public static Vector3 Step(Vector3 velocity, Vector3 direction, float immersion,
            bool swimUp, bool dive, float delta)
    {
        float dt = Mathf.Clamp(delta, 0.0f, 0.1f);
        float amount = Mathf.Clamp(immersion, 0.0f, 1.0f);
        direction.Y = 0.0f;
        if (direction.LengthSquared() > 1.0f) direction = direction.Normalized();

        float horizontalSpeed = Mathf.Lerp(4.0f, HorizontalSpeed, amount);
        velocity.X = Mathf.MoveToward(velocity.X, direction.X * horizontalSpeed, 10.0f * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, direction.Z * horizontalSpeed, 10.0f * dt);

        float targetY = Mathf.Lerp(-0.85f, 0.8f, amount);
        float acceleration = 5.5f;
        if (swimUp)
        {
            targetY = SwimUpSpeed;
            acceleration = 13.0f;
        }
        else if (dive)
        {
            targetY = DiveSpeed;
            acceleration = 13.0f;
        }
        velocity.Y = Mathf.MoveToward(velocity.Y, targetY, acceleration * dt);
        return velocity;
    }
}
