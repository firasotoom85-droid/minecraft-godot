using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Manual reproduction fixture for transparent sorting within one chunk
/// compared with an equivalent pair split across a chunk boundary. Orbit the
/// camera through both depth axes to inspect both pane orders.
/// Run with: godot --path . res://tests/GlassSortingFixture.tscn
/// </summary>
public partial class GlassSortingFixture : Node3D
{
    private VoxelWorld? _world;
    private Camera3D? _camera;
    private string _fixtureDirectory = "";
    private Vector3 _target = new(11.5f, 100.5f, 9.0f);
    private double _angle;

    public override void _Ready()
    {
        _fixtureDirectory = $"user://tests/glass-sorting-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(_fixtureDirectory);
        WorldStore.WriteManifest(_fixtureDirectory, new WorldManifest(
                "Glass Sorting Reproduction", 420193, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(420193), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(_fixtureDirectory);

        AddChild(new WorldEnvironment
        {
            Name = "SortingEnvironment",
            Environment = WorldEnvironmentFactory.Build(),
        });

        _world = new VoxelWorld { BuildCollision = false };
        AddChild(_world);
        _world.BuildPreview(_target, 1);

        // Clear both comparison areas above the generated terrain. The second
        // pair intentionally straddles the X=16 chunk boundary.
        for (int y = 98; y <= 104; y++)
            for (int z = 6; z <= 12; z++)
                for (int x = 5; x <= 18; x++)
                    Place(x, y, z, BlockId.Air);

        // Pair A: both panes share chunk (0, 0) and overlap when viewed along Z.
        Place(8, 100, 8, BlockId.Glass);
        Place(8, 100, 10, BlockId.Glass);

        // Pair B: equivalent panes occupy chunks (0, 0) and (1, 0), respectively,
        // and overlap when viewed along X. This is the cross-chunk control case.
        Place(15, 100, 8, BlockId.Glass);
        Place(17, 100, 8, BlockId.Glass);

        _camera = new Camera3D
        {
            Name = "SortingCamera",
            Current = true,
            Fov = 60.0f,
        };
        AddChild(_camera);
        UpdateCamera();

        GD.Print("GLASS_SORTING_FIXTURE same_chunk=(8,100,8),(8,100,10) chunk=(0,0)");
        GD.Print("GLASS_SORTING_FIXTURE cross_chunk=(15,100,8) chunk=(0,0); (17,100,8) chunk=(1,0)");
        GD.Print("Inspect Pair A while facing +/-Z and Pair B while facing +/-X. The camera orbits continuously; no automatic visual result is asserted.");
    }

    public override void _Process(double delta)
    {
        _angle = (_angle + delta * 0.18) % (Math.PI * 2.0);
        UpdateCamera();
    }

    public override void _ExitTree()
    {
        if (_fixtureDirectory.Length == 0) return;
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(_fixtureDirectory));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(_fixtureDirectory));
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(_fixtureDirectory));
        DirAccess.RemoveAbsolute(_fixtureDirectory);
    }

    private void Place(int x, int y, int z, BlockId id)
    {
        if (_world == null || !_world.SetBlock(x, y, z, id))
        {
            if (_world != null && _world.GetBlock(x, y, z) == id) return;
            throw new InvalidOperationException(
                    $"Glass sorting fixture could not place {id} at ({x},{y},{z}).");
        }
    }

    private void UpdateCamera()
    {
        if (_camera == null) return;
        const float radius = 16.0f;
        _camera.GlobalPosition = _target + new Vector3(
                Mathf.Sin((float)_angle) * radius,
                2.5f,
                Mathf.Cos((float)_angle) * radius);
        _camera.LookAt(_target, Vector3.Up);
    }
}
