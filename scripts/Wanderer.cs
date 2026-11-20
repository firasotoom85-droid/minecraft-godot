using Godot;

namespace Minecraft;

/// <summary>Shared feeding, herd cohesion and relaxed wandering for passive animals.</summary>
public partial class Wanderer : Mob
{
    public override bool IsPassive => true;
    public override ItemId BreedingFood => this is Chicken ? ItemId.Seeds : ItemId.Wheat;

    public override bool Interact(Player player) => TryFeed(player);

    protected override Vector3 Think(double delta)
    {
        // A nearby player or predator creates a short, readable flight response.
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

        Vector3 herd = Manager?.HerdDirection(this, 8.0f) ?? Vector3.Zero;
        return Dir + herd * 0.35f;
    }
}
