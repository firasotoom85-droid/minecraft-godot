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
    private Vector3 _moveDirection;
    protected float MovementMultiplier = 1.0f;

    private MeshInstance3D? _bodyVisual;
    private Vector3 _bodyRestPosition;
    private double _thinkElapsed;
    private double _animationTime;
    private double _lovePuffTimer;
    private float _alertTime;
    private Vector3 _alertSource;
    private float _loveTime;
    private float _breedCooldown;
    private float _growthTime;
    private Vector3 _rememberedTarget;
    private float _rememberedTargetTime;
    private Node3D[] _animatedLegs = [];

    public bool Dead => Health <= 0.0f;
    public bool IsAlerted => _alertTime > 0.0f;
    public Vector3 AlertSource => _alertSource;
    public bool InLove => _loveTime > 0.0f;
    public bool IsBaby { get; private set; }
    public virtual bool IsPassive => false;
    public virtual ItemId BreedingFood => ItemId.None;
    protected virtual bool UsesGravity => true;
    protected virtual bool UsesVoxelSteering => true;

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

    public void SetAlerted(float seconds, Vector3 sourcePosition)
    {
        SetAlerted(seconds);
        _alertSource = sourcePosition;
    }

    /// <summary>Remember a last-seen target briefly so hostiles can navigate around corners.</summary>
    protected void RememberTarget(Vector3 position, float seconds = 3.5f)
    {
        _rememberedTarget = position;
        _rememberedTargetTime = Mathf.Max(_rememberedTargetTime, seconds);
    }

    protected bool TryGetRememberedTarget(out Vector3 position)
    {
        position = _rememberedTarget;
        return _rememberedTargetTime > 0.0f;
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
            Vector3 desired = Think(thinkDelta);
            _moveDirection = !UsesVoxelSteering
                    ? desired
                    : UsesGravity ? SteerAroundObstacles(desired) : SteerAroundWalls(desired);
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

    public static bool CanDetectAtLight(byte skyLight, byte blockLight, byte threshold) =>
            Mathf.Max(skyLight, blockLight) <= threshold;

    /// <summary>Sample cached voxel light at the creature's feet; called only on AI ticks.</summary>
    protected bool CanDetectPlayer(float maximumLight)
    {
        if (World == null) return false;
        int x = Mathf.FloorToInt(GlobalPosition.X);
        int y = Mathf.Clamp(Mathf.FloorToInt(GlobalPosition.Y + 0.8f), 0, World.Height - 1);
        int z = Mathf.FloorToInt(GlobalPosition.Z);
        Vector2I chunk = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
        if (!World.HasChunk(chunk)) return false;
        VoxelLightData light = World.LightData(chunk);
        int localX = ((x % VoxelWorld.ChunkX) + VoxelWorld.ChunkX) % VoxelWorld.ChunkX;
        int localZ = ((z % VoxelWorld.ChunkZ) + VoxelWorld.ChunkZ) % VoxelWorld.ChunkZ;
        byte sky = light.SkyAt(localX, y, localZ, World.Height);
        byte block = light.BlockAt(localX, y, localZ, World.Height);
        return CanDetectAtLight(sky, block, (byte)Mathf.Clamp(Mathf.RoundToInt(maximumLight), 0, 15));
    }

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

    /// <summary>
    /// Hovering mobs steer around solid voxel walls without interpreting open
    /// air below them as a fall. This keeps cave sentries mobile in chambers.
    /// </summary>
    private Vector3 SteerAroundWalls(Vector3 desired)
    {
        if (desired.LengthSquared() < 0.001f || World == null)
        {
            return Vector3.Zero;
        }

        desired.Y = 0.0f;
        desired = desired.Normalized();
        if (!IsWallBlocked(desired))
        {
            return desired;
        }

        Vector3 left = new(-desired.Z, 0.0f, desired.X);
        Vector3 right = -left;
        bool leftBlocked = IsWallBlocked(left);
        bool rightBlocked = IsWallBlocked(right);
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

        return Vector3.Zero;
    }

    private bool IsWallBlocked(Vector3 direction)
    {
        if (World == null)
        {
            return true;
        }

        Vector3 probe = GlobalPosition + direction * 1.05f;
        int x = Mathf.FloorToInt(probe.X);
        int z = Mathf.FloorToInt(probe.Z);
        int bodyY = Mathf.FloorToInt(GlobalPosition.Y + 0.35f);
        int headY = Mathf.FloorToInt(GlobalPosition.Y + 0.95f);
        return BlockDb.DefOf(World.GetBlock(x, bodyY, z)).Solid
                || BlockDb.DefOf(World.GetBlock(x, headY, z)).Solid;
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
        water = BlockDb.IsWater(lower) || BlockDb.IsWater(floor);
        gap = floor == BlockId.Air || BlockDb.IsWater(floor);
        bool wall = (lower != BlockId.Air && !BlockDb.IsWater(lower))
                || (upper != BlockId.Air && !BlockDb.IsWater(upper));
        return water || gap || wall;
    }

    private void UpdateLifeTimers(float dt)
    {
        _animationTime += dt;
        _alertTime = Mathf.Max(0.0f, _alertTime - dt);
        _rememberedTargetTime = Mathf.Max(0.0f, _rememberedTargetTime - dt);
        if (_alertTime <= 0.0f)
        {
            _alertSource = Vector3.Zero;
        }
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

    /// <summary>Add four independently pivoted legs to procedural quadruped/bird bodies.</summary>
    protected void AddAnimatedLegs(Color color, float length, float thickness,
            float xSpacing, float zSpacing, float topY)
    {
        StandardMaterial3D material = new()
        {
            AlbedoColor = color,
            Roughness = 1.0f,
        };
        _animatedLegs = new Node3D[4];
        int index = 0;
        foreach (float x in new[] { -xSpacing, xSpacing })
        {
            foreach (float z in new[] { -zSpacing, zSpacing })
            {
                Node3D pivot = new()
                {
                    Name = $"LegPivot{index}",
                    Position = new Vector3(x, topY, z),
                };
                MeshInstance3D leg = new()
                {
                    Name = $"Leg{index}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3(thickness, length, thickness),
                    },
                    Position = new Vector3(0, -length * 0.5f, 0),
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                pivot.AddChild(leg);
                AddChild(pivot);
                _animatedLegs[index++] = pivot;
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
        bool resting = this is Wanderer { IsResting: true };
        _bodyVisual.Position = _bodyRestPosition + new Vector3(0, bob - (resting ? 0.055f : 0.0f), 0);
        float gait = moving ? Mathf.Sin((float)_animationTime * Mathf.Max(Speed, 1.0f) * 4.0f) * 0.48f : 0.0f;
        for (int i = 0; i < _animatedLegs.Length; i++)
        {
            Vector3 legRotation = _animatedLegs[i].Rotation;
            float phase = (i == 0 || i == 3) ? gait : -gait;
            legRotation.X = phase;
            _animatedLegs[i].Rotation = legRotation;
        }
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

/// <summary>Shared deterministic gates for hostile attacks and visible telegraphs.</summary>
public static class MobCombatRules
{
    /// <summary>Centralizes normal/hard attack scaling so every hostile archetype has a deliberate hard-mode value.</summary>
    public static float DamageForDifficulty(float normalDamage, float hardDamage) =>
            Settings.Difficulty == Settings.DifficultyPeaceful ? 0.0f
            : Settings.Difficulty == Settings.DifficultyHard ? hardDamage : normalDamage;

    public static int HostilePopulationCap => Settings.Difficulty switch
    {
        Settings.DifficultyPeaceful => 0,
        Settings.DifficultyHard => 10,
        _ => 7,
    };

    public static int ExperienceReward(Mob mob) =>
            mob is EmberWarden ? 25 : mob.IsPassive ? 1 : 5;

    public static bool CanAttack(float cooldownRemaining, bool hasLineOfSight) =>
            cooldownRemaining <= 0.0f && hasLineOfSight;

    public static bool CanBeginTelegraph(float cooldownRemaining, float distance,
            float range, bool hasLineOfSight) =>
            cooldownRemaining <= 0.0f && hasLineOfSight && distance > 0.0f && distance <= range;
}
