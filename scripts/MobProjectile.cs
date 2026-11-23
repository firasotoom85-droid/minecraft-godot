using Godot;

namespace Minecraft;

public static class MobProjectileRules
{
    public static bool ShouldStopOnTerrain(bool terrainHit) => terrainHit;
    public static bool ShouldDamagePlayer(bool playerHit, bool terrainHit, bool peaceful = false) =>
            playerHit && !terrainHit && !peaceful;
}

public partial class MobProjectile : Area3D
{
    private Vector3 _velocity;
    private float _damage;
    private Vector3 _source;
    private float _life = 4.0f;
    private Mobs? _manager;
    private RayCast3D? _terrainRay;
    private string _sound = "mobcrystal";
    private Color _color = Colors.White;

    internal RayCast3D? TerrainRayForTests => _terrainRay;

    internal void ConfigureTerrainRayForTests(Vector3 from, Vector3 to)
    {
        if (_terrainRay == null) return;
        _terrainRay.GlobalPosition = from;
        _terrainRay.TargetPosition = to - from;
        _terrainRay.ForceRaycastUpdate();
    }

    public void Setup(Mobs manager, Vector3 origin, Vector3 target, float damage,
            Color color, float speed, string sound)
    {
        _manager = manager;
        _terrainRay = new RayCast3D
        {
            CollisionMask = (uint)PhysicsLayer.Terrain,
            CollideWithAreas = false,
            TargetPosition = Vector3.Zero,
        };
        AddChild(_terrainRay);
        _damage = damage;
        _source = origin;
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
        if (Settings.Difficulty == Settings.DifficultyPeaceful)
        {
            QueueFree();
            return;
        }

        _life -= (float)delta;
        if (_life <= 0.0f)
        {
            QueueFree();
            return;
        }

        Vector3 nextPosition = GlobalPosition + _velocity * (float)delta;
        if (_terrainRay == null)
        {
            QueueFree();
            return;
        }
        _terrainRay.GlobalPosition = GlobalPosition;
        _terrainRay.TargetPosition = nextPosition - GlobalPosition;
        _terrainRay.ForceRaycastUpdate();
        bool terrainHit = _terrainRay.IsColliding();
        if (MobProjectileRules.ShouldStopOnTerrain(terrainHit))
        {
            _manager?.Puff(nextPosition, _color);
            QueueFree();
            return;
        }

        GlobalPosition = nextPosition;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is Player player && MobProjectileRules.ShouldDamagePlayer(true, false,
                Settings.Difficulty == Settings.DifficultyPeaceful))
        {
            if (player.ApplyDamage(new DamageInfo(_damage, DamageKind.Projectile,
                    _source, 1.8f, 0.8f)))
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
