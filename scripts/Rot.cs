using Godot;

namespace Minecraft;

/// <summary>
/// Rot: a slower, tougher pack hunter. It does not burn in daylight at this
/// stage; the population manager only rises it at night or in storms.
/// </summary>
public partial class Rot : Mob
{
    private const float ChaseRadius = 24.0f;
    private const float AttackRange = 1.7f;
    private const double AttackCooldown = 1.2;

    private double _attackCd;

    public Rot()
    {
        MaxHealth = 14.0f;
        Speed = 2.15f;
        DisplayName = "Rot";
        Loot = [(ItemId.Coal, 1, 2, 0.85f), (ItemId.Stick, 1, 2, 0.55f)];
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.66f, 1.7f, 0.5f),
            },
            Position = new Vector3(0, 0.85f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color flesh = new(0.26f, 0.40f, 0.20f);
        Color fleshLight = new(0.34f, 0.48f, 0.24f);
        Color eye = new(0.90f, 0.82f, 0.30f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.58f, 0.72f, 0.34f), new Vector3(0, 1.05f, 0), flesh);
        AddBox(st, new Vector3(0.42f, 0.42f, 0.42f), new Vector3(0, 1.62f, 0), fleshLight);
        AddBox(st, new Vector3(0.22f, 0.20f, 0.08f), new Vector3(0, 1.58f, -0.24f), flesh.Darkened(0.18f));
        AddBox(st, new Vector3(0.07f, 0.06f, 0.04f), new Vector3(-0.09f, 1.67f, -0.23f), eye);
        AddBox(st, new Vector3(0.07f, 0.06f, 0.04f), new Vector3(0.09f, 1.67f, -0.23f), eye);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.15f, 0.62f, 0.16f), new Vector3(sx * 0.39f, 1.08f, -0.16f), flesh);
            AddBox(st, new Vector3(0.17f, 0.62f, 0.19f), new Vector3(sx * 0.14f, 0.34f, 0), flesh);
        }

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
            bool visible = HasLineOfSight(player);
            if (visible) RememberTarget(player.GlobalPosition);
            if (d < ChaseRadius && (IsAlerted || visible))
            {
                if (!visible)
                {
                    if (TryGetRememberedTarget(out Vector3 lastSeen))
                    {
                        Vector3 remembered = lastSeen - GlobalPosition;
                        remembered.Y = 0.0f;
                        if (remembered.LengthSquared() > 1.0f) return remembered;
                    }

                    return TryInvestigateSound(out Vector3 soundDirection)
                            ? soundDirection : Vector3.Zero;
                }
                Manager?.AlertHostiles(GlobalPosition, 12.0f, 7.0f);
                _attackCd -= delta;
                if (d < AttackRange)
                {
                    if (MobCombatRules.CanAttack((float)_attackCd, visible))
                    {
                        _attackCd = AttackCooldown;
                        int packSize = Manager?.HostilesNear<Rot>(GlobalPosition, 9.0f) ?? 1;
                        float damage = (Settings.Difficulty == Settings.DifficultyHard ? 2.7f : 1.9f)
                                + Mathf.Min(packSize - 1, 3) * 0.35f;
                        if (player.ApplyDamage(new DamageInfo(
                                damage, DamageKind.Melee, GlobalPosition, 3.5f, 2.0f)))
                        {
                            Manager?.Lunge(this);
                        }
                    }

                    return toPlayer.Normalized() * 0.2f;
                }

                return toPlayer;
            }
        }

        if (TryInvestigateSound(out Vector3 noiseDirection))
        {
            return noiseDirection;
        }

        Timer -= delta;
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
