using Godot;

namespace Minecraft;

/// <summary>Ballistic player arrow with gravity, swept collision and impact feedback.</summary>
public partial class ArrowProjectile : Area3D
{
    private Vector3 _velocity;
    private float _life = 5.0f;
    private Player? _owner;
    private bool _spent;
    private RayCast3D _sweep = null!;

    public void Launch(Player owner, Vector3 origin, Vector3 direction, float speed = 22.0f)
    {
        _owner = owner;
        _velocity = direction.Normalized() * speed;
        CollisionLayer = 0;
        CollisionMask = (uint)(PhysicsLayer.Mobs | PhysicsLayer.Terrain);
        Monitoring = true;
        Monitorable = false;
        SphereShape3D shape = new() { Radius = 0.10f };
        AddChild(new CollisionShape3D { Shape = shape });
        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(0.72f, 0.55f, 0.30f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        MeshInstance3D shaft = new()
        {
            Mesh = new BoxMesh { Size = new Vector3(0.045f, 0.045f, 0.52f) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(shaft);
        _sweep = new RayCast3D
        {
            TargetPosition = Vector3.Zero,
            Enabled = true,
            CollisionMask = CollisionMask,
        };
        AddChild(_sweep);
        BodyEntered += OnBodyEntered;
        GlobalPosition = origin;
        LookAt(origin + _velocity, Vector3.Up);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_spent) return;
        _life -= (float)delta;
        if (_life <= 0.0f) { Impact(GlobalPosition, false); return; }

        float dt = (float)delta;
        _velocity += Vector3.Down * (9.8f * dt);
        Vector3 step = _velocity * dt;
        _sweep.TargetPosition = ToLocal(GlobalPosition + step);
        _sweep.ForceRaycastUpdate();
        if (_sweep.IsColliding())
        {
            Node collider = _sweep.GetCollider() as Node ?? this;
            if (collider is IDamageable damageable)
                HitDamageable(damageable, _sweep.GetCollisionPoint());
            else
                Impact(_sweep.GetCollisionPoint(), false);
            return;
        }
        GlobalPosition += step;
        if (_velocity.LengthSquared() > 0.01f) LookAt(GlobalPosition + _velocity, Vector3.Up);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_spent || body == _owner) return;
        if (body is IDamageable damageable) HitDamageable(damageable, GlobalPosition);
        else Impact(GlobalPosition, false);
    }

    private void HitDamageable(IDamageable target, Vector3 point)
    {
        if (_spent) return;
        bool hit = target.ApplyDamage(new DamageInfo(5.0f, DamageKind.Projectile,
            _owner?.GlobalPosition ?? GlobalPosition, 2.0f, 0.55f));
        Impact(point, hit);
    }

    private void Impact(Vector3 point, bool hit)
    {
        if (_spent) return;
        _spent = true;
        if (hit && _owner != null)
            _owner.EmitSignal(Player.SignalName.Notice, "Arrow hit");
        else if (_owner != null)
            _owner.EmitSignal(Player.SignalName.Notice, "Arrow impact");
        QueueFree();
    }
}
