using Godot;

namespace Minecraft;

public partial class CrystalWisp : Mob
{
    private double _shotCooldown = 1.4;
    private float _charge;
    private float _strafeSign = 1.0f;
    private double _strafeTimer = 2.2;

    public CrystalWisp()
    {
        MaxHealth = 9.0f;
        Speed = 2.1f;
        DisplayName = "Crystal Wisp";
        Loot = [(ItemId.Coal, 1, 2, 1.0f), (ItemId.GoldIngot, 0, 1, 0.22f)];
    }

    protected override bool UsesGravity => false;

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = 0.48f },
            Position = new Vector3(0, 0.65f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color crystal = new(0.27f, 0.72f, 0.83f);
        Color glow = new(0.65f, 0.96f, 1.0f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.52f, 0.76f, 0.52f), new Vector3(0, 0.66f, 0), crystal);
        AddBox(st, new Vector3(0.20f, 0.94f, 0.20f), new Vector3(0, 0.70f, 0), glow);
        AddBox(st, new Vector3(0.66f, 0.12f, 0.18f), new Vector3(0, 0.64f, 0), glow);
        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        _shotCooldown -= delta;
        _strafeTimer -= delta;
        if (_strafeTimer <= 0.0)
        {
            _strafeTimer = GD.RandRange(1.5, 3.2);
            _strafeSign *= -1.0f;
        }

        if (Manager?.PlayerRef is not { } player || player.Stats.Dead)
        {
            _charge = 0.0f;
            return Vector3.Zero;
        }

        Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
        float distance = toPlayer.Length();
        if (distance > 25.0f)
        {
            _charge = 0.0f;
            return Vector3.Zero;
        }

        bool visible = HasLineOfSight(player);
        if (!visible)
        {
            _charge = 0.0f;
            return distance > 7.0f ? toPlayer : Vector3.Zero;
        }

        if (_shotCooldown <= 0.0 && _charge <= 0.0f && distance < 17.0f)
        {
            _charge = 0.62f;
            Manager?.Puff(GlobalPosition + new Vector3(0, 0.65f, 0), new Color(0.42f, 0.90f, 1.0f));
            Manager?.SoundRef?.Play("mobcrystal", GlobalPosition, 0.88f, -11.0f);
        }

        if (_charge > 0.0f)
        {
            _charge -= (float)delta;
            if (_charge <= 0.0f)
            {
                _shotCooldown = 2.8;
                Manager?.FireProjectile(GlobalPosition + new Vector3(0, 0.6f, 0),
                        player.GlobalPosition + new Vector3(0, 1.0f, 0), 2.5f,
                        new Color(0.42f, 0.88f, 1.0f), 8.5f, "mobcrystal");
            }
        }

        Vector3 flat = toPlayer;
        flat.Y = 0.0f;
        if (flat.LengthSquared() < 0.001f)
        {
            return Vector3.Zero;
        }

        Vector3 toward = flat.Normalized();
        Vector3 strafe = new Vector3(-toward.Z, 0.0f, toward.X) * _strafeSign;
        Vector3 movement = distance < 7.0f ? -toward : distance > 13.0f ? toward : Vector3.Zero;
        return movement + strafe * 0.35f;
    }
}

/// <summary>
/// Dreadwing: a rare nocturnal flier that uses gravity-free hovering and
/// repositioning to flank the player. It vanishes when dawn returns.
/// </summary>
