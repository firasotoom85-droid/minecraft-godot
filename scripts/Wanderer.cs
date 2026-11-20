using Godot;

namespace Minecraft;

/// <summary>Shared feeding, grazing, weather shelter, herd cohesion and wandering for passive animals.</summary>
public partial class Wanderer : Mob
{
    private static readonly Vector2I[] GrazingOffsets =
    [
        new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(2, 0), new(-2, 0), new(0, 2), new(0, -2),
        new(1, 1), new(-1, 1), new(1, -1), new(-1, -1),
    ];

    public override bool IsPassive => true;
    public override ItemId BreedingFood => this is Chicken ? ItemId.Seeds : ItemId.Wheat;
    public float Hunger { get; private set; } = 100.0f;
    public bool IsResting { get; private set; }

    public override bool Interact(Player player) => TryFeed(player);

    public static float DrainHunger(float hunger, float seconds) =>
            Mathf.Clamp(hunger - Mathf.Max(seconds, 0.0f) * 0.018f, 0.0f, 100.0f);

    public static float RestoreHunger(float hunger, float amount) =>
            Mathf.Clamp(hunger + amount, 0.0f, 100.0f);

    public static bool IsGrazeable(BlockId block) =>
            block is BlockId.Grass or BlockId.SnowGrass;

    protected override Vector3 Think(double delta)
    {
        Hunger = DrainHunger(Hunger, (float)delta);
        IsResting = false;

        // Threat response always wins over feeding and rest.
        Vector3 threat = Manager?.FleeDirection(this, 6.5f) ?? Vector3.Zero;
        if (threat.LengthSquared() > 0.01f)
        {
            Walking = true;
            Dir = threat;
            return threat;
        }

        if (IsAlerted)
        {
            Vector3 away = GlobalPosition - AlertSource;
            away.Y = 0.0f;
            if (away.LengthSquared() > 0.01f)
            {
                Walking = true;
                Dir = away.Normalized();
                return Dir;
            }
        }

        if (Hunger < 62.0f && TryFindGrazeTarget(out Vector3 grazeTarget))
        {
            Vector3 toGrass = grazeTarget - GlobalPosition;
            toGrass.Y = 0.0f;
            if (toGrass.LengthSquared() < 1.8f * 1.8f)
            {
                int gx = Mathf.FloorToInt(grazeTarget.X);
                int gz = Mathf.FloorToInt(grazeTarget.Z);
                int gy = Mathf.FloorToInt(grazeTarget.Y);
                if (World != null && IsGrazeable(World.GetBlock(gx, gy, gz))
                        && World.SetBlock(gx, gy, gz, BlockId.Dirt))
                {
                    Hunger = RestoreHunger(Hunger, 42.0f);
                    Manager?.Puff(GlobalPosition + new Vector3(0, 0.65f, 0),
                            new Color(0.48f, 0.72f, 0.34f));
                }
            }
            else
            {
                Walking = true;
                Dir = toGrass.Normalized();
                return Dir;
            }
        }

        if (ShouldSeekShelter() && TryFindShelter(out Vector3 shelter))
        {
            Vector3 toShelter = shelter - GlobalPosition;
            toShelter.Y = 0.0f;
            if (toShelter.LengthSquared() > 1.8f * 1.8f)
            {
                Walking = true;
                Dir = toShelter.Normalized();
                return Dir;
            }

            IsResting = true;
            Walking = false;
            return Vector3.Zero;
        }

        // Passive wildlife rests at night when no immediate threat is present.
        if (World != null && DayNight.IsNight(World.TimeOfDay))
        {
            IsResting = true;
            Walking = false;
            return Vector3.Zero;
        }

        Timer -= delta;
        if (Timer <= 0.0)
        {
            (Walking, Timer) = Walking
                    ? (false, GD.RandRange(1.2, 3.8))
                    : (true, GD.RandRange(1.4, 4.2));
            if (Walking)
            {
                float angle = GD.Randf() * Mathf.Tau;
                Dir = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            }
        }

        Vector3 home = GlobalPosition;
        if (home.DistanceTo(Home) > Leash)
        {
            Vector3 back = Home - home;
            back.Y = 0.0f;
            Dir = back.Normalized();
            Walking = true;
        }

        if (!Walking)
        {
            return Vector3.Zero;
        }

        Mobs.HerdProfile profile = Mobs.HerdProfile.For(this);
        Vector3 herd = Manager?.HerdDirection(this, 8.0f) ?? Vector3.Zero;
        return Dir + herd * profile.Cohesion;
    }

    private bool TryFindGrazeTarget(out Vector3 target)
    {
        target = Vector3.Zero;
        if (World == null) return false;
        int baseX = Mathf.FloorToInt(GlobalPosition.X);
        int baseZ = Mathf.FloorToInt(GlobalPosition.Z);
        int baseY = Mathf.FloorToInt(GlobalPosition.Y - 0.1f);
        foreach (Vector2I offset in GrazingOffsets)
        {
            int x = baseX + offset.X;
            int z = baseZ + offset.Y;
            Vector2I chunk = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (!World.HasChunk(chunk)) continue;
            int y = baseY - 1;
            if (!IsGrazeable(World.GetBlock(x, y, z))
                    || World.GetBlock(x, y + 1, z) != BlockId.Air) continue;
            target = new Vector3(x + 0.5f, y, z + 0.5f);
            return true;
        }

        return false;
    }

    private bool ShouldSeekShelter() => Manager?.WeatherRef?.Current
            is Weather.State.Rain or Weather.State.Storm;

    private bool TryFindShelter(out Vector3 shelter)
    {
        shelter = Vector3.Zero;
        if (World == null) return false;
        int bx = Mathf.FloorToInt(GlobalPosition.X);
        int bz = Mathf.FloorToInt(GlobalPosition.Z);
        int y = Mathf.FloorToInt(GlobalPosition.Y);
        float best = float.MaxValue;
        for (int dz = -6; dz <= 6; dz++)
        {
            for (int dx = -6; dx <= 6; dx++)
            {
                int x = bx + dx;
                int z = bz + dz;
                Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                        Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
                if (!World.HasChunk(cp)) continue;
                if (!BlockDb.DefOf(World.GetBlock(x, y + 2, z)).Solid
                        || !BlockDb.DefOf(World.GetBlock(x, y - 1, z)).Solid) continue;
                float distance = dx * dx + dz * dz;
                if (distance < best)
                {
                    best = distance;
                    shelter = new Vector3(x + 0.5f, GlobalPosition.Y, z + 0.5f);
                }
            }
        }

        return best < float.MaxValue;
    }
}
