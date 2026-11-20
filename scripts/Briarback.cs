using Godot;

namespace Minecraft;

public partial class Briarback : Wanderer
{
    private float _rageTime;
    private double _attackCooldown;

    public Briarback()
    {
        MaxHealth = 20.0f;
        Speed = 1.55f;
        DisplayName = "Briarback";
        Loot = [(ItemId.Leather, 1, 2, 1.0f), (ItemId.Stick, 1, 2, 0.75f)];
    }

    public override ItemId BreedingFood => ItemId.None;

    protected override void OnProvoked(in DamageInfo info)
    {
        _rageTime = 10.0f;
        Manager?.Puff(GlobalPosition + new Vector3(0, 1.0f, 0), new Color(0.55f, 0.68f, 0.33f));
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.0f, 1.05f, 1.25f) },
            Position = new Vector3(0, 0.72f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color hide = new(0.34f, 0.30f, 0.22f);
        Color moss = new(0.34f, 0.46f, 0.25f);
        Color thorn = new(0.76f, 0.73f, 0.56f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.92f, 0.70f, 1.12f), new Vector3(0, 0.78f, 0), hide);
        AddBox(st, new Vector3(0.58f, 0.52f, 0.54f), new Vector3(0, 0.94f, -0.67f), moss);
        AddBox(st, new Vector3(0.42f, 0.23f, 0.18f), new Vector3(0, 0.78f, -0.98f), thorn);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.10f, 0.35f, 0.12f),
                    new Vector3(sx * 0.20f, 1.30f, -0.18f), thorn);
            AddBox(st, new Vector3(0.10f, 0.28f, 0.12f),
                    new Vector3(sx * 0.42f, 1.12f, 0.18f), thorn);
            AddBox(st, new Vector3(0.17f, 0.48f, 0.17f),
                    new Vector3(sx * 0.30f, 0.29f, -0.34f), hide);
            AddBox(st, new Vector3(0.17f, 0.48f, 0.17f),
                    new Vector3(sx * 0.30f, 0.29f, 0.34f), hide);
        }

        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        _rageTime = Mathf.Max(0.0f, _rageTime - (float)delta);
        _attackCooldown -= delta;
        if (_rageTime > 0.0f && Manager?.PlayerRef is { } player && !player.Stats.Dead)
        {
            Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
            toPlayer.Y = 0.0f;
            float distance = toPlayer.Length();
            if (distance < 2.0f)
            {
                if (_attackCooldown <= 0.0
                        && GlobalPosition.DistanceSquaredTo(player.GlobalPosition) < 3.2f * 3.2f)
                {
                    _attackCooldown = 1.8;
                    if (player.ApplyDamage(new DamageInfo(2.5f, DamageKind.Melee,
                            GlobalPosition, 3.6f, 1.5f)))
                    {
                        Manager?.Lunge(this);
                    }
                }

                return Vector3.Zero;
            }

            return toPlayer;
        }

        return base.Think(delta);
    }
}

/// <summary>
/// Crystal Wisp: a hovering cave sentry. It maintains a middle range and
/// launches a visible, slow crystal bolt after a brief charge-up.
/// </summary>
