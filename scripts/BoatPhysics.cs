using Godot;

namespace Minecraft;

/// <summary>Small, deterministic boat controller shared by the live vehicle and tests.</summary>
public static class BoatPhysics
{
    public const float MaxForwardSpeed = 8.5f;
    public const float MaxReverseSpeed = 2.6f;
    public const float Acceleration = 3.4f;
    public const float Braking = 9.0f;
    public const float CoastDrag = 1.0f;
    public const float TurnRate = 1.75f;

    /// <summary>
    /// Heading zero points along -Z. Throttle is -1..1 and steer is -1..1.
    /// Reverse steering is inverted, as expected for a rudder behind the boat.
    /// </summary>
    public static (Vector3 Velocity, float Yaw) Step(Vector3 velocity, float yaw,
            float throttle, float steer, bool handbrake, float delta)
    {
        float dt = Mathf.Clamp(delta, 0.0f, 0.1f);
        throttle = Mathf.Clamp(throttle, -1.0f, 1.0f);
        steer = Mathf.Clamp(steer, -1.0f, 1.0f);

        Vector3 forward = new(-Mathf.Sin(yaw), 0.0f, -Mathf.Cos(yaw));
        float speed = velocity.Dot(forward);
        if (handbrake)
        {
            speed = Mathf.MoveToward(speed, 0.0f, Braking * dt);
        }
        else if (throttle > 0.0f)
        {
            speed = Mathf.MoveToward(speed, MaxForwardSpeed * throttle, Acceleration * dt);
        }
        else if (throttle < 0.0f)
        {
            float target = speed > 0.0f ? 0.0f : MaxReverseSpeed * throttle;
            float rate = speed > 0.0f ? Braking : Acceleration;
            speed = Mathf.MoveToward(speed, target, rate * dt);
        }
        else
        {
            speed = Mathf.MoveToward(speed, 0.0f, CoastDrag * dt);
        }

        float steeringAuthority = Mathf.Clamp(Mathf.Abs(speed) / 2.2f, 0.0f, 1.0f);
        yaw -= steer * TurnRate * steeringAuthority * (speed < -0.05f ? -1.0f : 1.0f) * dt;
        Vector3 newForward = new(-Mathf.Sin(yaw), 0.0f, -Mathf.Cos(yaw));
        Vector3 horizontal = newForward * speed;
        return (new Vector3(horizontal.X, velocity.Y, horizontal.Z), yaw);
    }

    /// <summary>Remove velocity into a horizontal obstacle while retaining tangential motion.</summary>
    public static Vector3 SlideCollision(Vector3 velocity, Vector3 normal)
    {
        if (Mathf.Abs(normal.Y) >= 0.5f) return velocity;
        Vector3 horizontal = new(velocity.X, 0.0f, velocity.Z);
        horizontal -= normal * horizontal.Dot(normal);
        return new Vector3(horizontal.X * 0.15f, velocity.Y, horizontal.Z * 0.15f);
    }

    /// <summary>Float toward the averaged surface height to damp bobbing on mixed flow levels.</summary>
    public static Vector3 ApplyBuoyancy(Vector3 position, Vector3 velocity,
            bool hasWater, float waterSurface, float delta)
    {
        float dt = Mathf.Clamp(delta, 0.0f, 0.1f);
        if (hasWater)
        {
            float targetHeight = waterSurface - 0.28f;
            float targetVelocity = Mathf.Clamp((targetHeight - position.Y) * 3.5f, -2.0f, 2.0f);
            velocity.Y = Mathf.MoveToward(velocity.Y, targetVelocity, 9.0f * dt);
        }
        else
        {
            velocity.Y = Mathf.Max(velocity.Y - 9.8f * dt, -18.0f);
        }
        return velocity;
    }

    /// <summary>All four hull corners must be in built chunks before horizontal motion.</summary>
    public static bool FootprintLoaded(VoxelWorld world, Vector3 position, float halfWidth = 0.68f,
            float halfLength = 0.88f)
    {
        return IsLoaded(position.X - halfWidth, position.Z - halfLength)
                && IsLoaded(position.X + halfWidth, position.Z - halfLength)
                && IsLoaded(position.X - halfWidth, position.Z + halfLength)
                && IsLoaded(position.X + halfWidth, position.Z + halfLength);

        bool IsLoaded(float x, float z)
        {
            Vector2I chunk = new(
                    Mathf.FloorToInt(x / VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / VoxelWorld.ChunkZ));
            return world.HasChunk(chunk);
        }
    }
}
