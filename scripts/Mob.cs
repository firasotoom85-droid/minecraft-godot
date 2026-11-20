using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Shared creature runtime: bounded-rate AI, voxel-aware steering, locomotion,
/// hit feedback, breeding timers and the existing damage/loot event seam.
/// Subclasses only provide their steering and body.
/// </summary>
public partial class Mob : CharacterBody3D, IDamageable
{
    public const float MobGravity = 24.0f;
    public const float Leash = 24.0f;

    public VoxelWorld? World;
    public Mobs? Manager;
    public Vector3 Home = Vector3.Zero;
    public float Speed = 1.6f;
    public float MaxHealth = 10.0f;
    public float Health = 10.0f;
    public string DisplayName = "Mob";

    /// <summary>Loot granted to the killer's inventory: (item, min, max, chance).</summary>
    public (ItemId Id, int Min, int Max, float Chance)[] Loot = [];

    public event Action<Mob>? Died;
    public event Action<Mob, DamageInfo>? Hurt;
    public event Action<Mob, string>? Signalled;

    protected StandardMaterial3D BodyMat = null!;
    protected Vector3 Dir = Vector3.Zero;
    protected double Timer;
    protected bool Walking;
    protected double FlashTime;
    protected Vector3 Push;
    protected float MovementMultiplier = 1.0f;

    private MeshInstance3D? _bodyVisual;
    private Vector3 _bodyRestPosition;
    private double _thinkElapsed;
    private double _animationTime;
    private double _lovePuffTimer;
    private float _alertTime;
    private float _loveTime;
    private float _breedCooldown;
    private float _growthTime;

    public bool Dead => Health <= 0.0f;
    public bool IsAlerted => _alertTime > 0.0f;
    public bool InLove => _loveTime > 0.0f;
    public bool IsBaby { get; private set; }
    public virtual bool IsPassive => false;
    public virtual ItemId BreedingFood => ItemId.None;
    protected virtual bool UsesGravity => true;

    public void Setup(VoxelWorld world, Mobs manager, Vector3 home)
    {
        World = world;
        Manager = manager;
        Home = home;
        Health = MaxHealth;
        CollisionLayer = (uint)PhysicsLayer.Mobs;
        CollisionMask = (uint)PhysicsLayer.Terrain;
        FloorSnapLength = 0.4f;
        BuildBody();
        AddShape();
        Timer = GD.RandRange(0.5, 2.0);
    }

    protected virtual void BuildBody()
    {
    }

    protected virtual void AddShape()
    {
    }

    public bool ApplyDamage(in DamageInfo info)
    {
        if (Dead || info.Amount <= 0.0f)
        {
            return false;
        }

        Health -= info.Amount;
        FlashTime = 0.18;
        Hurt?.Invoke(this, info);
        OnProvoked(info);
        if (info.Knockback > 0.0f)
        {
            Vector3 away = GlobalPosition - info.Source;
            away.Y = 0.0f;
            if (away.LengthSquared() > 0.001f)
            {
                away = away.Normalized();
            }

            Push = away * info.Knockback + new Vector3(0, info.Lift, 0);
        }

        if (Health <= 0.0f)
        {
            Die();
        }

        return true;
    }

    /// <summary>Neutral creatures may become defensive after the first hit.</summary>
    protected virtual void OnProvoked(in DamageInfo info)
    {
    }

    protected virtual void Die()
    {
        Died?.Invoke(this);
        QueueFree();
    }

    /// <summary>Right-click use on the mob; true when it did something.</summary>
    public virtual bool Interact(Player player) => false;

    /// <summary>Consume the selected species food and enter the short breeding state.</summary>
    protected bool TryFeed(Player player)
    {
        ItemId food = BreedingFood;
        if (IsBaby || food == ItemId.None
                || player.Selected < 0 || player.Selected >= Inventory.HotbarSize
                || player.Inventory.Hotbar[player.Selected].Id != food
                || !player.Inventory.Consume(food, 1))
        {
            return false;
        }

        _loveTime = 14.0f;
        _lovePuffTimer = 0.0;
        RaiseSignalled("love");
        player.EmitSignal(Player.SignalName.Notice, $"{DisplayName} is ready to breed — find another nearby");
        return true;
    }

    public void SetBaby(float secondsToGrow = 90.0f)
    {
        IsBaby = true;
        _growthTime = Mathf.Max(secondsToGrow, 1.0f);
        Scale = new Vector3(0.65f, 0.65f, 0.65f);
        Speed *= 0.92f;
    }

    internal void MarkBred()
    {
        _loveTime = 0.0f;
        _lovePuffTimer = 0.0;
        _breedCooldown = 90.0f;
    }

    internal bool CanBreedWith(Mob other) =>
            !Dead && !other.Dead && !IsBaby && !other.IsBaby
            && InLove && other.InLove && _breedCooldown <= 0.0f
            && other._breedCooldown <= 0.0f && GetType() == other.GetType();

    public void SetAlerted(float seconds)
    {
        _alertTime = Mathf.Max(_alertTime, seconds);
    }

    /// <summary>Subclass hook for the Signalled event (tame/stay/follow/love).</summary>
    protected void RaiseSignalled(string kind) => Signalled?.Invoke(this, kind);

    public override void _PhysicsProcess(double delta)
    {
        if (World == null || Dead)
        {
            return;
        }

        float dt = (float)delta;
        FlashStep(dt);
        UpdateLifeTimers(dt);
        Push = new Vector3(
                Mathf.MoveToward(Push.X, 0.0f, dt * 14.0f),
                Mathf.MoveToward(Push.Y, 0.0f, dt * 20.0f),
                Mathf.MoveToward(Push.Z, 0.0f, dt * 14.0f));

        // Distant creatures still move, but expensive perception and steering
        // are sampled at a lower rate. Nearby combat remains responsive.
        float thinkInterval = 0.14f;
        if (Manager?.PlayerRef is { } player
                && GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > 32.0f * 32.0f)
        {
            thinkInterval = 0.42f;
        }

        _thinkElapsed += delta;
        if (_thinkElapsed >= thinkInterval)
        {
            double thinkDelta = _thinkElapsed;
            _thinkElapsed = 0.0;
            _moveDirection = SteerAroundObstacles(Think(thinkDelta));
        }

        Vector3 dir = _moveDirection;
        Vector3 velocity = Velocity;
        if (UsesGravity)
        {
            if (!IsOnFloor())
            {
                velocity.Y -= MobGravity * dt;
            }
        }
        else
        {
            velocity.Y = Mathf.MoveToward(velocity.Y,
                    Mathf.Sin((float)_animationTime * 1.7f) * 0.12f, dt * 1.4f);
        }

        if (dir.LengthSquared() > 0.001f)
        {
            dir = dir.Normalized();
            velocity.X = dir.X * Speed * MovementMultiplier + Push.X;
            velocity.Z = dir.Z * Speed * MovementMultiplier + Push.Z;
            if (IsOnWall() && IsOnFloor() && UsesGravity)
            {
                velocity.Y = 5.2f;
            }

            Rotation = new Vector3(Rotation.X,
                    Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(-dir.X, -dir.Z), dt * 5.0f), Rotation.Z);
        }
        else
        {
            velocity.X = Push.X;
            velocity.Z = Push.Z;
        }

        Velocity = velocity;
        MoveAndSlide();
        AnimateBody(dt);
    }

    /// <summary>Subclass steering: desired horizontal movement direction (may be zero).</summary>
    protected virtual Vector3 Think(double delta) => Vector3.Zero;

    /// <summary>Shared terrain-only line of sight; callers invoke it on the AI tick, not every frame.</summary>
    protected bool HasLineOfSight(Node3D target)
    {
        Vector3 from = GlobalPosition + new Vector3(0, 1.15f, 0);
        Vector3 to = target.GlobalPosition + new Vector3(0, 1.1f, 0);
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
                from, to, (uint)PhysicsLayer.Terrain);
        query.CollideWithAreas = false;
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    private Vector3 SteerAroundObstacles(Vector3 desired)
    {
        if (desired.LengthSquared() < 0.001f || World == null)
        {
            return Vector3.Zero;
        }

        desired.Y = 0.0f;
        desired = desired.Normalized();
        if (!IsDirectionBlocked(desired, out bool water, out bool gap))
        {
            return desired;
        }

        if (water)
        {
            return -desired;
        }

        Vector3 left = new(-desired.Z, 0.0f, desired.X);
        Vector3 right = -left;
        bool leftBlocked = IsDirectionBlocked(left, out _, out _);
        bool rightBlocked = IsDirectionBlocked(right, out _, out _);
        if (!leftBlocked && !rightBlocked)
        {
            return (GetInstanceId() & 1UL) == 0UL ? left : right;
        }

        if (!leftBlocked)
        {
            return left;
        }

        if (!rightBlocked)
        {
            return right;
        }

        return gap ? -desired : Vector3.Zero;
    }

    private bool IsDirectionBlocked(Vector3 direction, out bool water, out bool gap)
    {
        water = false;
        gap = false;
        if (World == null)
        {
            return true;
        }

        Vector3 probe = GlobalPosition + direction * 1.05f;
        int x = Mathf.FloorToInt(probe.X);
        int z = Mathf.FloorToInt(probe.Z);
        int feetY = Mathf.FloorToInt(GlobalPosition.Y - 0.12f);
        BlockId floor = World.GetBlock(x, feetY, z);
        BlockId lower = World.GetBlock(x, feetY + 1, z);
        BlockId upper = World.GetBlock(x, feetY + 2, z);
        water = lower == BlockId.Water || floor == BlockId.Water;
        gap = floor == BlockId.Air || floor == BlockId.Water;
        bool wall = lower is not (BlockId.Air or BlockId.Water)
                || upper is not (BlockId.Air or BlockId.Water);
        return water || gap || wall;
    }

    private void UpdateLifeTimers(float dt)
    {
        _animationTime += dt;
        _alertTime = Mathf.Max(0.0f, _alertTime - dt);
        _loveTime = Mathf.Max(0.0f, _loveTime - dt);
        _breedCooldown = Mathf.Max(0.0f, _breedCooldown - dt);

        if (InLove)
        {
            _lovePuffTimer -= dt;
            if (_lovePuffTimer <= 0.0)
            {
                _lovePuffTimer = 1.25;
                Manager?.Puff(GlobalPosition + new Vector3(0, 1.4f, 0), new Color(1.0f, 0.45f, 0.68f));
            }
        }

        if (IsBaby)
        {
            _growthTime -= dt;
            if (_growthTime <= 0.0f)
            {
                IsBaby = false;
                Scale = Vector3.One;
                Speed /= 0.92f;
                Manager?.Puff(GlobalPosition + new Vector3(0, 1.0f, 0), new Color(0.70f, 0.92f, 0.58f));
                Manager?.SoundRef?.Play("mobgrowl", GlobalPosition, 1.2f, -8.0f);
            }
        }
    }

    private void AnimateBody(float dt)
    {
        if (_bodyVisual == null)
        {
            return;
        }

        bool moving = new Vector2(Velocity.X, Velocity.Z).LengthSquared() > 0.15f;
        float bob = moving
                ? Mathf.Abs(Mathf.Sin((float)_animationTime * Mathf.Max(Speed, 1.0f) * 2.4f)) * 0.045f
                : Mathf.Sin((float)_animationTime * 1.6f) * 0.012f;
        _bodyVisual.Position = _bodyRestPosition + new Vector3(0, bob, 0);
        Vector3 rotation = _bodyVisual.Rotation;
        rotation.X = moving ? Mathf.Sin((float)_animationTime * 3.2f) * 0.025f : 0.0f;
        rotation.Z = moving ? Mathf.Cos((float)_animationTime * 3.2f) * 0.018f : 0.0f;
        _bodyVisual.Rotation = rotation;
    }

    private void FlashStep(float dt)
    {
        if (FlashTime <= 0.0 || BodyMat == null)
        {
            return;
        }

        FlashTime -= dt;
        BodyMat.AlbedoColor = FlashTime > 0.0 ? new Color(1.0f, 0.35f, 0.30f) : Colors.White;
    }

    // Procedural block bodies: a single coloured mesh per creature.
    protected static void AddBox(SurfaceTool st, Vector3 size, Vector3 offset, Color col)
    {
        for (int f = 0; f < 6; f++)
        {
            st.SetNormal(BlockDb.FaceDirs[f]);
            st.SetColor(col);
            foreach (int t in Mesher.TriOrder)
            {
                Vector3 v = BlockDb.FaceVerts[f * 4 + t];
                st.SetUV(Vector2.Zero);
                st.AddVertex(offset + (v - new Vector3(0.5f, 0.5f, 0.5f)) * size);
            }
        }
    }

    protected void CommitBody(SurfaceTool st, string name = "Body")
    {
        BodyMat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1.0f,
        };
        MeshInstance3D mi = new()
        {
            Name = name,
            Mesh = st.Commit(),
            MaterialOverride = BodyMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        _bodyVisual = mi;
        _bodyRestPosition = mi.Position;
    }
}
