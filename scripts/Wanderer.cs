using Godot;

namespace Minecraft;

/// <summary>Shared relaxed wander steering for passive animals.</summary>
public partial class Wanderer : Mob
{
    protected override Vector3 Think(double delta)
    {
        Timer -= delta;
        if (Timer <= 0.0)
        {
            (Walking, Timer) = Walking
                    ? (false, GD.RandRange(1.0, 3.5))
                    : (true, GD.RandRange(1.5, 4.0));
            if (Walking)
            {
                float ang = GD.Randf() * Mathf.Tau;
                Dir = new Vector3(Mathf.Cos(ang), 0.0f, Mathf.Sin(ang));
            }
        }

        if (!Walking)
        {
            return Vector3.Zero;
        }

        Vector3 pos = GlobalPosition;
        if (pos.DistanceTo(Home) > Leash)
        {
            Vector3 back = Home - pos;
            back.Y = 0.0f;
            Dir = back.Normalized();
        }

        return Dir;
    }
}
