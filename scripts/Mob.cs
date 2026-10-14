using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Base for wandering creatures: gravity, water avoidance, 1-block hops,
/// hit flash, knockback and health — all damage arrives through
/// <see cref="IDamageable"/>. Subclasses supply a body, collision shape and
/// a <see cref="Think"/> steering function.
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
    public event Action<Mob, string>? Signalled;  // "tame" / "stay" / "follow" — fx + audio hooks

    protected StandardMaterial3D BodyMat = null!;
    protected Vector3 Dir = Vector3.Zero;
    protected double Timer;
    protected bool Walking;
    protected double FlashTime;
    protected Vector3 Push;   // knockback impulse, decays

    public bool Dead => Health <= 0.0f;

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

    protected virtual void Die()
    {
        Died?.Invoke(this);
        QueueFree();
    }

    /// <summary>Right-click use on the mob; true when it did something.</summary>
    public virtual bool Interact(Player player) => false;

    /// <summary>Subclass hook for the Signalled event (tame/stay/follow feedback).</summary>
    protected void RaiseSignalled(string kind) => Signalled?.Invoke(this, kind);

    public override void _PhysicsProcess(double delta)
    {
        if (World == null)
        {
            return;
        }

        float dt = (float)delta;
        FlashStep(dt);
        Push = new Vector3(
                Mathf.MoveToward(Push.X, 0.0f, dt * 14.0f),
                Mathf.MoveToward(Push.Y, 0.0f, dt * 20.0f),
                Mathf.MoveToward(Push.Z, 0.0f, dt * 14.0f));

        Vector3 dir = Think(delta);
        Vector3 velocity = Velocity;
        if (!IsOnFloor())
        {
            velocity.Y -= MobGravity * dt;
        }

        if (dir.LengthSquared() > 0.001f)
        {
            dir = dir.Normalized();
            Vector3 ahead = GlobalPosition + dir * 1.3f;
            if (World.GetBlock(Mathf.FloorToInt(ahead.X), Mathf.FloorToInt(GlobalPosition.Y + 0.5f),
                    Mathf.FloorToInt(ahead.Z)) == BlockId.Water)
            {
                dir = -dir;
            }

            velocity.X = dir.X * Speed + Push.X;
            velocity.Z = dir.Z * Speed + Push.Z;
            if (IsOnWall() && IsOnFloor())
            {
                velocity.Y = 7.0f;
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
    }

    /// <summary>Subclass steering: desired horizontal movement direction (may be zero).</summary>
    protected virtual Vector3 Think(double delta) => Vector3.Zero;

    private void FlashStep(float dt)
    {
        if (FlashTime <= 0.0)
        {
            return;
        }

        FlashTime -= dt;
        BodyMat.AlbedoColor = FlashTime > 0.0 ? new Color(1.0f, 0.35f, 0.30f) : Colors.White;
    }

    // -----------------------------------------------------------------------
    // Procedural block bodies (one mesh, vertex colours = one draw call).
    // Replace with imported generated models to upgrade the look.
    // -----------------------------------------------------------------------

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
    }
}
