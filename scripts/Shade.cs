using Godot;

namespace Minecraft;

/// <summary>
/// The first hostile: a slow, dark stalker that rises at night (and in
/// storms), chases the player within sight and swings at close range.
/// Campfires repel it; daylight burns it away. Tough but fair: it never
/// spawns on peaceful difficulty.
/// </summary>
public partial class Shade : Mob
{
    private const float ChaseRadius = 26.0f;
    private const float AttackRange = 1.7f;
    private const double AttackCooldown = 1.4;

    public Shade()
    {
        MaxHealth = 20.0f;
        Speed = 2.7f;
        DisplayName = "Shade";
        Loot = [(ItemId.Coal, 1, 2, 0.85f), (ItemId.Stick, 1, 1, 0.35f)];
    }

    private double _attackCd;
    private double _burn;
    private double _smokeTimer;

    protected override void AddShape()
    {
        CollisionShape3D shape = new()
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.7f, 1.8f, 0.5f),
            },
            Position = new Vector3(0, 0.9f, 0),
        };
        AddChild(shape);
    }

    protected override void BuildBody()
    {
        Color cloth = new(0.15f, 0.12f, 0.20f);
        Color clothLight = new(0.20f, 0.16f, 0.27f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.60f, 0.75f, 0.34f), new Vector3(0, 1.08f, 0), cloth);            // torso
        AddBox(st, new Vector3(0.42f, 0.42f, 0.42f), new Vector3(0, 1.66f, 0), clothLight);        // head
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.16f, 0.62f, 0.18f), new Vector3(sx * 0.40f, 1.08f, 0), cloth);    // arms
            AddBox(st, new Vector3(0.18f, 0.66f, 0.20f), new Vector3(sx * 0.15f, 0.36f, 0), cloth);    // legs
        }

        CommitBody(st);

        // Ember eyes: a small unshaded emissive mesh so it reads in the dark.
        SurfaceTool eyes = new();
        eyes.Begin(Mesh.PrimitiveType.Triangles);
        Color glow = new(1.0f, 0.62f, 0.18f);
        AddBox(eyes, new Vector3(0.09f, 0.07f, 0.04f), new Vector3(-0.10f, 1.70f, -0.235f), glow);
        AddBox(eyes, new Vector3(0.09f, 0.07f, 0.04f), new Vector3(0.10f, 1.70f, -0.235f), glow);
        StandardMaterial3D eyeMat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            EmissionEnabled = true,
            Emission = glow,
            EmissionEnergyMultiplier = 2.2f,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Eyes",
            Mesh = eyes.Commit(),
            MaterialOverride = eyeMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    protected override Vector3 Think(double delta)
    {
        if (World == null)
        {
            return Vector3.Zero;
        }

        // Daylight (outside storms) scorches the shade away.
        bool dark = DayNight.IsNight(World.TimeOfDay) || (Manager?.IsStorm ?? false);
        if (!dark)
        {
            _burn += delta;
            _smokeTimer -= delta;
            if (_smokeTimer <= 0.0)
            {
                _smokeTimer = 0.45;
                Manager?.Puff(GlobalPosition + new Vector3(0, 1.9f, 0), new Color(0.35f, 0.32f, 0.38f));
            }

            if (_burn > 3.0)
            {
                Die();
                return Vector3.Zero;
            }
        }
        else
        {
            _burn = 0.0;
        }

        // Campfires are home: never approach within the repel ring.
        if (World.NearestCampfire(GlobalPosition, 10.0f) is { } fire)
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
                Manager?.AlertHostiles(GlobalPosition, 10.0f, 6.0f);
                _attackCd -= delta;
                if (d < AttackRange)
                {
                    if (MobCombatRules.CanAttack((float)_attackCd, visible))
                    {
                        _attackCd = AttackCooldown;
                        float damage = Settings.Difficulty == Settings.DifficultyHard ? 3.0f : 2.0f;
                        bool hit = player.ApplyDamage(new DamageInfo(
                                damage, DamageKind.Melee, GlobalPosition, 4.0f, 2.5f));
                        if (hit)
                        {
                            Manager?.Lunge(this);
                        }
                    }

                    return toPlayer.Normalized() * 0.2f;
                }

                return toPlayer;
            }
        }

        // No prey: wander around the spawn point like everything else.
        if (TryInvestigateSound(out Vector3 noiseDirection))
        {
            return noiseDirection;
        }

        Timer -= delta;
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
