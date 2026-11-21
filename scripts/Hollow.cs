using Godot;

namespace Minecraft;

/// <summary>
/// Hollow: a patient ranged night stalker. It keeps a middle distance,
/// charges a visible shot, then fires a slow projectile that can be dodged.
/// </summary>
public partial class Hollow : Mob
{
    private const float SightRadius = 26.0f;
    private const float KeepMin = 6.0f;
    private const float KeepMax = 14.0f;
    private const double ShotCooldown = 2.4;

    private double _shotCd = 0.8;
    private float _charge;

    public Hollow()
    {
        MaxHealth = 12.0f;
        Speed = 1.9f;
        DisplayName = "Hollow";
        Loot =
        [
            (ItemId.Feather, 1, 2, 0.8f),
            (ItemId.Stick, 1, 1, 0.5f),
        ];
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.62f, 1.7f, 0.48f),
            },
            Position = new Vector3(0, 0.85f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color cloth = new(0.30f, 0.32f, 0.36f);
        Color clothLight = new(0.40f, 0.42f, 0.47f);
        Color face = new(0.10f, 0.10f, 0.12f);
        Color bow = new(0.52f, 0.34f, 0.16f);
        Color eye = new(0.55f, 0.88f, 0.98f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.58f, 0.74f, 0.32f), new Vector3(0, 1.05f, 0), cloth);
        AddBox(st, new Vector3(0.46f, 0.46f, 0.46f), new Vector3(0, 1.64f, 0), clothLight);
        AddBox(st, new Vector3(0.36f, 0.30f, 0.06f), new Vector3(0, 1.63f, -0.24f), face);
        AddBox(st, new Vector3(0.06f, 0.06f, 0.04f), new Vector3(-0.09f, 1.67f, -0.28f), eye);
        AddBox(st, new Vector3(0.06f, 0.06f, 0.04f), new Vector3(0.09f, 1.67f, -0.28f), eye);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.15f, 0.62f, 0.16f), new Vector3(sx * 0.39f, 1.08f, -0.12f), cloth);
            AddBox(st, new Vector3(0.17f, 0.66f, 0.19f), new Vector3(sx * 0.14f, 0.35f, 0), cloth);
        }

        // A simple bow held in front.
        AddBox(st, new Vector3(0.05f, 0.72f, 0.05f), new Vector3(0, 1.12f, -0.42f), bow);
        AddBox(st, new Vector3(0.05f, 0.12f, 0.32f), new Vector3(0, 1.12f, -0.30f), bow.Lightened(0.12f));
        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        if (World == null)
        {
            return Vector3.Zero;
        }

        if (World.NearestCampfire(GlobalPosition, 8.0f) is { } fire)
        {
            Vector3 away = GlobalPosition - fire;
            away.Y = 0.0f;
            if (away.LengthSquared() > 0.01f)
            {
                return away.Normalized();
            }
        }

        if (Manager?.PlayerRef is { } player && !player.Stats.Dead)
        {
            Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
            toPlayer.Y = 0.0f;
            float d = toPlayer.Length();
            bool visible = CanDetectPlayer(3.0f) && HasLineOfSight(player);
            if (visible) RememberTarget(player.GlobalPosition);
            if (d < SightRadius && (IsAlerted || visible || TryGetRememberedTarget(out _)))
            {
                Manager?.AlertHostiles(GlobalPosition, 12.0f, 7.0f);
                _shotCd -= delta;
                if (_charge > 0.0f)
                {
                    if (!visible)
                    {
                        _charge = 0.0f;
                    }
                    else
                    {
                        _charge -= (float)delta;
                        if (_charge <= 0.0f)
                        {
                            _shotCd = ShotCooldown;
                            float damage = MobCombatRules.DamageForDifficulty(2.0f, 3.0f);
                            Manager?.FireProjectile(GlobalPosition + new Vector3(0, 1.2f, 0),
                                    player.GlobalPosition + new Vector3(0, 1.2f, 0), damage,
                                    new Color(0.70f, 0.86f, 0.94f), 7.5f, "mobchitter");
                        }
                    }
                }
                else if (visible && _shotCd <= 0.0 && d <= KeepMax)
                {
                    _charge = 0.48f;
                    Manager?.Puff(GlobalPosition + new Vector3(0, 1.45f, 0),
                            new Color(0.55f, 0.82f, 0.94f));
                    Manager?.SoundRef?.Play("mobchitter", GlobalPosition, 0.9f, -10.0f);
                }

                if (!visible)
                {
                    if (TryGetRememberedTarget(out Vector3 lastSeen))
                    {
                        Vector3 remembered = lastSeen - GlobalPosition;
                        remembered.Y = 0.0f;
                        return remembered.LengthSquared() > 1.0f ? remembered : Vector3.Zero;
                    }

                    return TryInvestigateSound(out Vector3 soundDirection)
                            ? soundDirection : Vector3.Zero;
                }

                if (d < KeepMin)
                {
                    return -toPlayer.Normalized();
                }

                if (d > KeepMax)
                {
                    return toPlayer;
                }

                return Vector3.Zero;
            }
        }

        if (TryInvestigateSound(out Vector3 noiseDirection))
        {
            return noiseDirection;
        }

        Timer -= delta;
        if (Timer <= 0.0)
        {
            (Walking, Timer) = Walking
                    ? (false, GD.RandRange(1.0, 3.0))
                    : (true, GD.RandRange(1.2, 3.0));
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

        if (GlobalPosition.DistanceTo(Home) > Leash)
        {
            Vector3 back = Home - GlobalPosition;
            back.Y = 0.0f;
            Dir = back.Normalized();
        }

        return Dir;
    }

}
