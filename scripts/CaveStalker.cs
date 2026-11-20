using Godot;

namespace Minecraft;

public partial class CaveStalker : Mob
{
    private double _pounceCooldown = 2.0;
    private float _windup;
    private float _pounceTime;
    private double _attackCooldown;

    public CaveStalker()
    {
        MaxHealth = 11.0f;
        Speed = 3.0f;
        DisplayName = "Cave Stalker";
        Loot = [(ItemId.RawPork, 1, 2, 1.0f), (ItemId.Feather, 1, 1, 0.45f)];
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.78f, 0.78f, 1.05f) },
            Position = new Vector3(0, 0.46f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color shell = new(0.20f, 0.25f, 0.23f);
        Color ridge = new(0.43f, 0.48f, 0.38f);
        Color eye = new(0.94f, 0.34f, 0.12f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.72f, 0.42f, 0.94f), new Vector3(0, 0.46f, 0), shell);
        AddBox(st, new Vector3(0.42f, 0.30f, 0.38f), new Vector3(0, 0.58f, -0.54f), ridge);
        AddBox(st, new Vector3(0.16f, 0.24f, 0.46f), new Vector3(-0.40f, 0.54f, -0.08f), shell);
        AddBox(st, new Vector3(0.16f, 0.24f, 0.46f), new Vector3(0.40f, 0.54f, -0.08f), shell);
        AddBox(st, new Vector3(0.08f, 0.07f, 0.04f), new Vector3(-0.15f, 0.68f, -0.74f), eye);
        AddBox(st, new Vector3(0.08f, 0.07f, 0.04f), new Vector3(0.15f, 0.68f, -0.74f), eye);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            foreach (float sz in new[] { -1.0f, 1.0f })
            {
                AddBox(st, new Vector3(0.13f, 0.20f, 0.15f),
                        new Vector3(sx * 0.30f, 0.18f, sz * 0.30f), ridge);
            }
        }

        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        _pounceCooldown -= delta;
        _attackCooldown -= delta;
        _pounceTime = Mathf.Max(0.0f, _pounceTime - (float)delta);
        MovementMultiplier = _pounceTime > 0.0f ? 2.65f : 1.0f;

        if (Manager?.PlayerRef is not { } player || player.Stats.Dead)
        {
            return Vector3.Zero;
        }

        Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
        toPlayer.Y = 0.0f;
        float distance = toPlayer.Length();
        if (distance > 22.0f)
        {
            _windup = 0.0f;
            return Vector3.Zero;
        }

        if (!HasLineOfSight(player))
        {
            _windup = 0.0f;
            if (TryGetRememberedTarget(out Vector3 lastSeen))
            {
                Vector3 remembered = lastSeen - GlobalPosition;
                remembered.Y = 0.0f;
                return remembered.LengthSquared() > 1.0f ? remembered : Vector3.Zero;
            }

            return Vector3.Zero;
        }

        RememberTarget(player.GlobalPosition);

        if (distance < 1.85f && _attackCooldown <= 0.0)
        {
            _attackCooldown = 1.15;
            if (player.ApplyDamage(new DamageInfo(3.0f, DamageKind.Melee,
                    GlobalPosition, 4.2f, 2.2f)))
            {
                Manager?.Puff(player.GlobalPosition + new Vector3(0, 0.8f, 0),
                        new Color(0.78f, 0.40f, 0.22f));
                Manager?.Lunge(this);
            }
        }

        if (_windup > 0.0f)
        {
            _windup -= (float)delta;
            if (_windup <= 0.0f)
            {
                _pounceTime = 0.58f;
                _pounceCooldown = 3.3;
                Manager?.Puff(GlobalPosition + new Vector3(0, 0.45f, 0),
                        new Color(0.78f, 0.48f, 0.24f));
                Manager?.SoundRef?.Play("mobchitter", GlobalPosition, 0.85f, -8.0f);
            }

            return Vector3.Zero;
        }

        if (distance < 8.5f && _pounceCooldown <= 0.0)
        {
            _windup = 0.52f;
            Manager?.Puff(GlobalPosition + new Vector3(0, 0.65f, 0),
                    new Color(0.92f, 0.46f, 0.18f));
            Manager?.SoundRef?.Play("mobchitter", GlobalPosition, 0.72f, -6.0f);
            return Vector3.Zero;
        }

        return toPlayer;
    }
}

/// <summary>
/// Briarback: a normally shy, armoured forest grazer. It only turns aggressive
/// after being struck, then retaliates with a short charge and headbutt.
/// </summary>
