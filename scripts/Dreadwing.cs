using Godot;

namespace Minecraft;

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
        if (World == null)
        {
            return Vector3.Zero;
        }

        if (!DayNight.IsNight(World.TimeOfDay) && !(Manager?.IsStorm ?? false))
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
            Vector3 side = new Vector3(-flat.Z, 0.0f, flat.X).Normalized();
            float sideSign = GD.Randf() < 0.5f ? -1.0f : 1.0f;
            Vector3 candidate = player.GlobalPosition - flat.Normalized() * 8.0f
                    + side * sideSign * 5.0f + new Vector3(0, 4.0f, 0);
            int x = Mathf.FloorToInt(candidate.X);
            int z = Mathf.FloorToInt(candidate.Z);
            Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (World.HasChunk(cp))
            {
                int h = World.SurfaceHeight(x, z);
                candidate.Y = Mathf.Clamp(Mathf.Max(candidate.Y, h + 3.0f),
                        2.0f, World.Height - 2.0f);
                GlobalPosition = candidate;
            }

            Manager?.Puff(GlobalPosition, new Color(0.55f, 0.22f, 0.44f));
            Manager?.SoundRef?.Play("mobdread", GlobalPosition, 0.9f, -7.0f);
        }

        bool visible = CanDetectPlayer(9.0f) && HasLineOfSight(player);
        if (visible)
        {
            RememberTarget(player.GlobalPosition, 4.0f);
        }
        else
        {
            _windup = 0.0f;
            if (TryGetRememberedTarget(out Vector3 lastSeen))
            {
                Vector3 remembered = lastSeen - GlobalPosition;
                remembered.Y = 0.0f;
                if (remembered.LengthSquared() > 1.0f) return remembered;
            }

            return TryInvestigateSound(out Vector3 soundDirection)
                    ? soundDirection : Vector3.Zero;
        }

        if (_windup > 0.0f)
        {
            _windup -= (float)delta;
            if (_windup <= 0.0f)
            {
                _attackCooldown = 2.0;
                if (distance < 2.7f && player.ApplyDamage(new DamageInfo(MobCombatRules.DamageForDifficulty(3.0f, 3.6f),
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
