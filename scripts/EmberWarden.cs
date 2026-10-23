using Godot;

namespace Minecraft;

/// <summary>
/// Ember Warden: the Ember Deep boss. A slow, heavy bruiser with a slam,
/// a short-range ember burst, and two add-summon phases. It drops the unique
/// Ember Core trophy.
/// </summary>
public partial class EmberWarden : Mob
{
    private const float ChaseRadius = 26.0f;
    private const float SlamRange = 3.4f;
    private const float VolleyRange = 15.0f;
    private const double SlamCooldown = 2.4;
    private const double VolleyCooldown = 4.2;

    private double _slamCd = 1.0;
    private double _volleyCd = 2.0;
    private bool _phaseTwo;
    private bool _phaseThree;

    public EmberWarden()
    {
        MaxHealth = 120.0f;
        Speed = 1.7f;
        DisplayName = "Ember Warden";
        Loot = [(ItemId.EmberCore, 1, 1, 1.0f)];
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(1.15f, 2.35f, 0.9f),
            },
            Position = new Vector3(0, 1.2f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color stone = new(0.16f, 0.14f, 0.17f);
        Color stoneLight = new(0.24f, 0.20f, 0.24f);
        Color ember = new(1.0f, 0.42f, 0.08f);
        Color emberHot = new(1.0f, 0.82f, 0.30f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(1.05f, 1.05f, 0.72f), new Vector3(0, 1.35f, 0), stone);
        AddBox(st, new Vector3(0.62f, 0.58f, 0.58f), new Vector3(0, 2.15f, 0), stoneLight);
        AddBox(st, new Vector3(0.74f, 0.18f, 0.66f), new Vector3(0, 2.48f, 0), stone);
        AddBox(st, new Vector3(0.10f, 0.16f, 0.10f), new Vector3(-0.20f, 2.58f, 0), ember);
        AddBox(st, new Vector3(0.10f, 0.16f, 0.10f), new Vector3(0.20f, 2.58f, 0), ember);
        AddBox(st, new Vector3(0.10f, 0.08f, 0.05f), new Vector3(-0.15f, 2.22f, -0.30f), emberHot);
        AddBox(st, new Vector3(0.10f, 0.08f, 0.05f), new Vector3(0.15f, 2.22f, -0.30f), emberHot);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.28f, 1.05f, 0.30f), new Vector3(sx * 0.64f, 1.24f, -0.12f), stone);
            AddBox(st, new Vector3(0.30f, 1.02f, 0.34f), new Vector3(sx * 0.25f, 0.56f, 0), stone);
            AddBox(st, new Vector3(0.16f, 0.12f, 0.06f), new Vector3(sx * 0.25f, 1.52f, -0.38f), ember);
        }

        // Ember cracks across the chest.
        for (int i = 0; i < 6; i++)
        {
            AddBox(st, new Vector3(0.07f, 0.20f, 0.04f),
                    new Vector3(-0.35f + i * 0.14f, 1.22f + (i % 2) * 0.22f, -0.37f),
                    i % 2 == 0 ? ember : ember.Darkened(0.18f));
        }

        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        if (World == null)
        {
            return Vector3.Zero;
        }

        if (Manager?.PlayerRef is { } player && !player.Stats.Dead)
        {
            Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
            toPlayer.Y = 0.0f;
            float d = toPlayer.Length();
            if (d < ChaseRadius)
            {
                _slamCd -= delta;
                _volleyCd -= delta;
                if (d < SlamRange && _slamCd <= 0.0)
                {
                    _slamCd = SlamCooldown;
                    float damage = Settings.Difficulty == Settings.DifficultyHard ? 5.0f : 4.0f;
                    if (player.ApplyDamage(new DamageInfo(
                            damage, DamageKind.Melee, GlobalPosition, 5.0f, 3.0f)))
                    {
                        Manager?.Puff(player.GlobalPosition + new Vector3(0, 0.8f, 0),
                                new Color(1.0f, 0.45f, 0.12f));
                        Manager?.Lunge(this);
                    }
                }
                else if (d < VolleyRange && _volleyCd <= 0.0)
                {
                    _volleyCd = VolleyCooldown;
                    float damage = Settings.Difficulty == Settings.DifficultyHard ? 3.0f : 2.0f;
                    if (player.ApplyDamage(new DamageInfo(
                            damage, DamageKind.Fire, GlobalPosition, 1.0f, 0.5f)))
                    {
                        Manager?.Puff(player.GlobalPosition + new Vector3(0, 1.2f, 0),
                                new Color(1.0f, 0.62f, 0.16f));
                    }
                }

                if (!_phaseTwo && Health < MaxHealth * 0.66f)
                {
                    _phaseTwo = true;
                    Manager?.SpawnAdds(GlobalPosition, 2);
                    Manager?.Puff(GlobalPosition + new Vector3(0, 1.5f, 0), new Color(0.85f, 0.24f, 0.10f));
                }

                if (!_phaseThree && Health < MaxHealth * 0.33f)
                {
                    _phaseThree = true;
                    Manager?.SpawnAdds(GlobalPosition, 3);
                    Manager?.Puff(GlobalPosition + new Vector3(0, 1.5f, 0), new Color(1.0f, 0.42f, 0.10f));
                }

                if (d > SlamRange * 0.9f)
                {
                    return toPlayer;
                }

                return toPlayer.Normalized() * 0.15f;
            }
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
