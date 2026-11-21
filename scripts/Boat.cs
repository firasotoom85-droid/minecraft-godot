using System;
using Godot;

namespace Minecraft;

/// <summary>
/// A small custom-physics wooden boat. The hull uses the existing streamed
/// terrain collider; water support is sampled from already-loaded voxel cells.
/// </summary>
public partial class Boat : CharacterBody3D
{
    private VoxelWorld? _world;
    private BoatSystem? _system;
    private Player? _rider;
    private double _rowSoundTimer;
    private double _oarPhase;
    private Node3D? _leftOar;
    private Node3D? _rightOar;
    private static Mesh? _hullMesh;
    private static StandardMaterial3D? _hullMaterial;
    private static StandardMaterial3D? _benchMaterial;

    public Player? Rider => _rider;
    public bool IsOccupied => _rider != null;

    public void Initialize(BoatSystem system, VoxelWorld world, Vector3 position,
            float yaw, Vector3 velocity)
    {
        _system = system;
        _world = world;
        Position = position;
        Rotation = new Vector3(0.0f, yaw, 0.0f);
        Velocity = velocity;
    }

    public override void _Ready()
    {
        CollisionLayer = (uint)PhysicsLayer.Mobs;
        CollisionMask = (uint)PhysicsLayer.Terrain;
        FloorSnapLength = 0.0f;
        BuildHull();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_world == null || !IsInsideTree()) return;
        if (!BoatPhysics.FootprintLoaded(_world, GlobalPosition))
        {
            Velocity = Vector3.Zero;
            return;
        }
        float dt = (float)delta;
        float throttle = 0.0f;
        float steering = 0.0f;
        bool brake = false;
        if (_rider != null && !_rider.InputLocked && !_rider.Dead)
        {
            throttle = Input.GetAxis("back", "forward");
            steering = Input.GetAxis("left", "right");
            brake = Input.IsActionPressed("sprint");
        }

        (Vector3 velocity, float yaw) = BoatPhysics.Step(
                Velocity, Rotation.Y, throttle, steering, brake, dt);
        Rotation = new Vector3(0.0f, yaw, 0.0f);

        Vector3 nextPosition = GlobalPosition + new Vector3(velocity.X, 0.0f, velocity.Z) * dt;
        if (!BoatPhysics.FootprintLoaded(_world, GlobalPosition)
                || !BoatPhysics.FootprintLoaded(_world, nextPosition))
        {
            velocity.X = 0.0f;
            velocity.Z = 0.0f;
        }

        if (TryGetWaterSurface(out float surface))
            velocity = BoatPhysics.ApplyBuoyancy(GlobalPosition, velocity, true, surface, dt);
        else
            velocity = BoatPhysics.ApplyBuoyancy(GlobalPosition, velocity, false, 0.0f, dt);

        Velocity = velocity;
        MoveAndSlide();
        float horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        if (_rider != null && horizontalSpeed > 0.4f)
        {
            _oarPhase += delta * Mathf.Clamp(horizontalSpeed, 0.8f, 5.0f) * 2.2;
            if (_leftOar != null) _leftOar.Rotation = new Vector3(0.0f, Mathf.Sin((float)_oarPhase) * 0.62f, 0.0f);
            if (_rightOar != null) _rightOar.Rotation = new Vector3(0.0f, -Mathf.Sin((float)_oarPhase) * 0.62f, 0.0f);
        }
        if (_rider != null && horizontalSpeed > 1.2f)
        {
            _rowSoundTimer -= delta;
            if (_rowSoundTimer <= 0.0)
            {
                _rowSoundTimer = 0.9;
                _system?.PlayRow(GlobalPosition, horizontalSpeed);
            }
        }
        else _rowSoundTimer = 0.0;
        if (GetSlideCollisionCount() > 0)
        {
            for (int i = 0; i < GetSlideCollisionCount(); i++)
            {
                Vector3 normal = GetSlideCollision(i).GetNormal();
                if (Mathf.Abs(normal.Y) < 0.5f)
                {
                    Velocity = BoatPhysics.SlideCollision(Velocity, normal);
                }
            }
        }
    }

    public bool TryBoard(Player player) => _system?.TryBoard(this, player) ?? false;

    public bool Dismount() => _system?.Dismount(this) ?? false;

    public void Break(Player player) => _system?.RemoveBoat(this, player);

    internal void SetRider(Player? player) => _rider = player;

    private bool TryGetWaterSurface(out float surface)
    {
        surface = 0.0f;
        if (_world == null) return false;

        Span<Vector2> samples =
        [
            Vector2.Zero,
            new Vector2(-0.48f, -0.62f),
            new Vector2(0.48f, -0.62f),
            new Vector2(-0.48f, 0.62f),
            new Vector2(0.48f, 0.62f),
        ];
        float total = 0.0f;
        int count = 0;
        int baseY = Mathf.FloorToInt(GlobalPosition.Y);
        foreach (Vector2 sample in samples)
        {
            Vector3 offset = GlobalBasis * new Vector3(sample.X, 0.0f, sample.Y);
            int x = Mathf.FloorToInt(GlobalPosition.X + offset.X);
            int z = Mathf.FloorToInt(GlobalPosition.Z + offset.Z);
            for (int y = baseY - 1; y <= baseY + 1; y++)
            {
                if (!_world.TryGetLoadedBlock(x, y, z, out BlockId id) || !BlockDb.IsWater(id))
                    continue;
                float height = y + BlockDb.WaterHeight(id);
                if (height < GlobalPosition.Y - 0.75f || height > GlobalPosition.Y + 0.8f)
                    continue;
                total += height;
                count++;
                break;
            }
        }

        if (count == 0) return false;
        surface = total / count;
        return true;
    }

    private void BuildHull()
    {
        if (_hullMesh == null)
        {
            _hullMesh = CreateHullMesh();
            _hullMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.48f, 0.27f, 0.12f),
                Roughness = 0.92f,
            };
            _benchMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.65f, 0.39f, 0.18f),
                Roughness = 0.9f,
            };
        }

        AddChild(new MeshInstance3D
        {
            Name = "WoodenHull",
            Mesh = _hullMesh,
            MaterialOverride = _hullMaterial,
        });
        BoxMesh bench = new() { Size = new Vector3(1.05f, 0.12f, 0.18f) };
        AddChild(new MeshInstance3D
        {
            Name = "Seat",
            Mesh = bench,
            MaterialOverride = _benchMaterial,
            Position = new Vector3(0.0f, 0.19f, 0.0f),
        });
        BoxMesh oarMesh = new() { Size = new Vector3(0.045f, 0.045f, 1.15f) };
        StandardMaterial3D oarMaterial = new() { AlbedoColor = new Color(0.58f, 0.35f, 0.17f), Roughness = 0.95f };
        _leftOar = new Node3D { Name = "LeftOar", Position = new Vector3(-0.68f, 0.12f, 0.0f) };
        _leftOar.AddChild(new MeshInstance3D { Mesh = oarMesh, MaterialOverride = oarMaterial, Rotation = new Vector3(0, 0.55f, 0) });
        AddChild(_leftOar);
        _rightOar = new Node3D { Name = "RightOar", Position = new Vector3(0.68f, 0.12f, 0.0f) };
        _rightOar.AddChild(new MeshInstance3D { Mesh = oarMesh, MaterialOverride = oarMaterial, Rotation = new Vector3(0, -0.55f, 0) });
        AddChild(_rightOar);
        BoxShape3D collision = new() { Size = new Vector3(1.18f, 0.36f, 1.62f) };
        AddChild(new CollisionShape3D
        {
            Name = "HullCollision",
            Shape = collision,
            Position = new Vector3(0.0f, -0.01f, 0.0f),
        });
    }

    private static ArrayMesh CreateHullMesh()
    {
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Color wood = new(0.48f, 0.27f, 0.12f);
        Vector3 a = new(-0.42f, -0.18f, -0.72f);
        Vector3 b = new(0.42f, -0.18f, -0.72f);
        Vector3 c = new(0.42f, -0.18f, 0.72f);
        Vector3 d = new(-0.42f, -0.18f, 0.72f);
        Vector3 e = new(-0.68f, 0.18f, -0.92f);
        Vector3 f = new(0.68f, 0.18f, -0.92f);
        Vector3 g = new(0.68f, 0.18f, 0.92f);
        Vector3 h = new(-0.68f, 0.18f, 0.92f);

        AddQuad(tool, wood, a, b, c, d);
        AddQuad(tool, wood, a, e, h, d);
        AddQuad(tool, wood, b, c, g, f);
        AddQuad(tool, wood, a, b, f, e);
        AddQuad(tool, wood, d, h, g, c);
        ArrayMesh mesh = new();
        tool.GenerateNormals();
        tool.Commit(mesh);
        return mesh;
    }

    private static void AddQuad(SurfaceTool tool, Color color,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        tool.SetColor(color);
        tool.AddVertex(a);
        tool.AddVertex(b);
        tool.AddVertex(c);
        tool.AddVertex(a);
        tool.AddVertex(c);
        tool.AddVertex(d);
    }
}
