using Godot;

namespace Minecraft;

/// <summary>
/// A fast cave ambusher that pauses for a readable pounce tell before
/// accelerating into melee range. Its chitin-like remains are represented by
/// existing survival materials, so no saved item ids need to change.
/// </summary>
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
        if (distance > 22.0f || !HasLineOfSight(player))
        {
            return Vector3.Zero;
        }

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
            }

            return toPlayer;
        }

        if (distance < 8.5f && _pounceCooldown <= 0.0)
        {
            _windup = 0.52f;
            Manager?.Puff(GlobalPosition + new Vector3(0, 0.65f, 0),
                    new Color(0.92f, 0.46f, 0.18f));
            return Vector3.Zero;
        }

        return toPlayer;
    }
}

/// <summary>
/// Briarback: a normally shy, armoured forest grazer. It only turns aggressive
/// after being struck, then retaliates with a short charge and headbutt.
/// </summary>
public partial class Briarback : Wanderer
{
    private Vector3 _provokedFrom;
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
        _provokedFrom = info.Source;
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
            Vector3 toPlayer = _provokedFrom - GlobalPosition;
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
        if (distance > 25.0f || !HasLineOfSight(player))
        {
            _charge = 0.0f;
            return Vector3.Zero;
        }

        if (_shotCooldown <= 0.0 && _charge <= 0.0f && distance < 17.0f)
        {
            _charge = 0.62f;
            Manager?.Puff(GlobalPosition + new Vector3(0, 0.65f, 0), new Color(0.42f, 0.90f, 1.0f));
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
        Vector3 strafe = new(-toward.Z, 0.0f, toward.X) * _strafeSign;
        Vector3 movement = distance < 7.0f ? -toward : distance > 13.0f ? toward : Vector3.Zero;
        return movement + strafe * 0.35f;
    }
}

/// <summary>
/// Dreadwing: a rare nocturnal flier that uses gravity-free hovering and
/// repositioning to flank the player. It vanishes when dawn returns.
/// </summary>
public partial class Dreadwing : Mob
{
    private double _repositionCooldown = 5.0;
    private double _attackCooldown = 1.0;
    private float _windup;

    public Dreadwing()
    {
        MaxHealth = 16.0f;
        Speed = 3.3f;
        DisplayName = "Dreadwing";
        Loot = [(ItemId.Feather, 2, 4, 1.0f), (ItemId.Leather, 1, 1, 0.65f)];
    }

    protected override bool UsesGravity => false;
    protected override bool UsesVoxelSteering => false;

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = 0.55f },
            Position = new Vector3(0, 0.9f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color membrane = new(0.16f, 0.14f, 0.22f);
        Color edge = new(0.40f, 0.24f, 0.35f);
        Color eye = new(0.92f, 0.18f, 0.28f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.48f, 0.55f, 0.66f), new Vector3(0, 0.95f, 0), membrane);
        AddBox(st, new Vector3(0.30f, 0.28f, 0.32f), new Vector3(0, 1.30f, -0.36f), membrane);
        AddBox(st, new Vector3(0.90f, 0.10f, 0.52f), new Vector3(-0.65f, 0.92f, 0.05f), edge);
        AddBox(st, new Vector3(0.90f, 0.10f, 0.52f), new Vector3(0.65f, 0.92f, 0.05f), edge);
        AddBox(st, new Vector3(0.08f, 0.06f, 0.04f), new Vector3(-0.10f, 1.34f, -0.52f), eye);
        AddBox(st, new Vector3(0.08f, 0.06f, 0.04f), new Vector3(0.10f, 1.34f, -0.52f), eye);
        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        if (World != null && !DayNight.IsNight(World.TimeOfDay) && !(Manager?.IsStorm ?? false))
        {
            Die();
            return Vector3.Zero;
        }

        _repositionCooldown -= delta;
        _attackCooldown -= delta;
        if (Manager?.PlayerRef is not { } player || player.Stats.Dead)
        {
            return Vector3.Zero;
        }

        Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
        float distance = toPlayer.Length();
        if (distance > 34.0f)
        {
            return Vector3.Zero;
        }

        if (_repositionCooldown <= 0.0 && distance > 5.0f)
        {
            _repositionCooldown = 6.0;
            Vector3 flat = toPlayer;
            flat.Y = 0.0f;
            Vector3 side = new(-flat.Z, 0.0f, flat.X).Normalized();
            float sideSign = GD.Randf() < 0.5f ? -1.0f : 1.0f;
            Vector3 candidate = player.GlobalPosition - flat.Normalized() * 8.0f
                    + side * sideSign * 5.0f + new Vector3(0, 4.0f, 0);
            int x = Mathf.FloorToInt(candidate.X);
            int z = Mathf.FloorToInt(candidate.Z);
            int h = World!.SurfaceHeight(x, z);
            if (candidate.Y < h + 2.0f)
            {
                candidate.Y = h + 3.0f;
            }

            Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (World.HasChunk(cp))
            {
                GlobalPosition = candidate;
            }

            Manager?.Puff(GlobalPosition, new Color(0.55f, 0.22f, 0.44f));
        }

        if (_windup > 0.0f)
        {
            _windup -= (float)delta;
            if (_windup <= 0.0f)
            {
                _attackCooldown = 2.0;
                if (distance < 2.7f && player.ApplyDamage(new DamageInfo(3.0f,
                        DamageKind.Melee, GlobalPosition, 3.0f, 2.0f)))
                {
                    Manager?.Lunge(this);
                }
            }

            return Vector3.Zero;
        }

        if (distance < 2.7f && _attackCooldown <= 0.0)
        {
            _windup = 0.55f;
            Manager?.Puff(GlobalPosition + new Vector3(0, 1.2f, 0), new Color(0.82f, 0.20f, 0.36f));
            return Vector3.Zero;
        }

        Vector3 desired = toPlayer;
        desired.Y = Mathf.Clamp(desired.Y, -3.0f, 3.0f);
        return desired;
    }
}

/// <summary>A lightweight visual projectile using Area3D overlap detection.</summary>
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
            if (player.ApplyDamage(new DamageInfo(_damage, DamageKind.Melee,
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
