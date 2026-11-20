using Godot;

namespace Minecraft;

public partial class MobProjectile : Area3D
{
    private Vector3 _velocity;
    private float _damage;
    private float _life = 4.0f;
    private Mobs? _manager;
    private string _sound = "mobcrystal";
    private Color _color = Colors.White;

    public void Setup(Mobs manager, Vector3 origin, Vector3 target, float damage,
            Color color, float speed, string sound)
    {
        _manager = manager;
        _damage = damage;
        _sound = sound;
        _color = color;
        Vector3 direction = target - origin;
        _velocity = direction.LengthSquared() > 0.001f
                ? direction.Normalized() * speed : Vector3.Forward * speed;

        CollisionLayer = 0;
        CollisionMask = (uint)(PhysicsLayer.Player | PhysicsLayer.Terrain);
        Monitoring = true;
        Monitorable = false;

        SphereShape3D shape = new() { Radius = 0.20f };
        AddChild(new CollisionShape3D { Shape = shape });

        StandardMaterial3D material = new()
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 1.4f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        BodyEntered += OnBodyEntered;
        GlobalPosition = origin;
    }

    public override void _PhysicsProcess(double delta)
    {
        _life -= (float)delta;
        if (_life <= 0.0f)
        {
            QueueFree();
            return;
        }

        GlobalPosition += _velocity * (float)delta;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is Player player)
        {
            if (player.ApplyDamage(new DamageInfo(_damage, DamageKind.Projectile,
                    GlobalPosition, 1.8f, 0.8f)))
            {
                _manager?.Puff(player.GlobalPosition + new Vector3(0, 1.0f, 0), _color);
                _manager?.SoundRef?.Play(_sound, GlobalPosition, 1.0f, -6.0f);
            }
        }
        else
        {
            _manager?.Puff(GlobalPosition, _color);
        }

        QueueFree();
    }
}
