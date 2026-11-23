using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Manual reproduction fixture for transparent panes that share one chunk
/// glass MeshInstance. Orbit the camera to view the two panes from both depth
/// directions; watch whether the rear pane blends over the nearer pane.
/// Run with: godot --path . res://tests/GlassSortingFixture.tscn
/// </summary>
public partial class GlassSortingFixture : Node3D
{
    private VoxelWorld? _world;
    private Camera3D? _camera;
    private string _fixtureDirectory = "";
    private Vector3 _target = new(8.5f, 100.5f, 9.0f);
    private double _angle;

    public override void _Ready()
    {
        _fixtureDirectory = $"user://tests/glass-sorting-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(_fixtureDirectory);
        WorldStore.WriteManifest(_fixtureDirectory, new WorldManifest(
                "Glass Sorting Reproduction", 420193, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(420193), 0, 0, "survival", "normal"));
        GameSession.Instance.SelectWorld(_fixtureDirectory);

        _world = new VoxelWorld { BuildCollision = false };
        AddChild(_world);
        _world.BuildPreview(_target, 1);

        for (int y = 98; y <= 104; y++)
        for (int z = 6; z <= 14; z++)
        for (int x = 6; x <= 11; x++)
            Place(x, y, z, BlockId.Air);

        // Two depth-separated panes in the same chunk. A contrasting opaque
        // wall behind them makes an incorrect blend order easy to see.
        Place(8, 100, 8, BlockId.Glass);
        Place(8, 100, 10, BlockId.Glass);
        for (int y = 99; y <= 102; y++)
        for (int x = 7; x <= 9; x++)
            Place(x, y, 13, BlockId.Stone);

        _camera = new Camera3D
        {
            Name = "SortingCamera",
            Current = true,
            Fov = 60.0f,
        };
        AddChild(_camera);
        UpdateCamera();

        GD.Print("GLASS_SORTING_FIXTURE panes=(8,100,8),(8,100,10) chunk=(0,0); camera orbits both depth orders");
        GD.Print("Inspect the same-chunk pane overlap from the +Z and -Z directions. No automatic visual result is asserted.");
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
        float radius = 12.0f;
        _camera.GlobalPosition = _target + new Vector3(
                Mathf.Sin((float)_angle) * radius,
                2.5f,
                Mathf.Cos((float)_angle) * radius);
        _camera.LookAt(_target, Vector3.Up);
    }
}
