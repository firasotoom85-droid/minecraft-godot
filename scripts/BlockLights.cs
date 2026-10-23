using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Pooled point lights for placed emitters (torches). Keeps the scene's light
/// count bounded by only lighting the nearest emitters; the mesher still draws
/// the torch itself, so this is about local illumination, not the flame.
/// Version 1 does not shadow light through walls — a voxel light propagation
/// pass can replace this later without touching block or save data.
/// </summary>
public partial class BlockLights : Node3D
{
    private const int MaxLights = 16;
    private const float Range = 10.0f;
    private const float UpdateInterval = 0.25f;

    private VoxelWorld? _world;
    private Player? _player;
    private readonly List<OmniLight3D> _pool = [];
    private double _timer;

    public void Setup(VoxelWorld world, Player player)
    {
        _world = world;
        _player = player;
        for (int i = 0; i < MaxLights; i++)
        {
            OmniLight3D light = new()
            {
                Name = $"BlockLight{i}",
                OmniRange = Range,
                OmniAttenuation = 1.1f,
                LightColor = new Color(1.0f, 0.78f, 0.48f),
                LightEnergy = 1.35f,
                ShadowEnabled = false,
                Visible = false,
            };
            AddChild(light);
            _pool.Add(light);
        }
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null)
        {
            return;
        }

        _timer -= delta;
        if (_timer > 0.0)
        {
            return;
        }

        _timer = UpdateInterval;
        List<Vector3I> cells = _world.NearestLights(_player.GlobalPosition, MaxLights);
        double seconds = Time.GetTicksMsec() / 1000.0;
        for (int i = 0; i < _pool.Count; i++)
        {
            OmniLight3D light = _pool[i];
            if (i >= cells.Count)
            {
                light.Visible = false;
                continue;
            }

            Vector3I cell = cells[i];
            light.GlobalPosition = new Vector3(cell.X + 0.5f, cell.Y + 0.55f, cell.Z + 0.5f);
            // A slow, per-torch phase offset keeps the pool from pulsing in sync.
            double phase = (cell.X * 0.73) + (cell.Y * 0.31) + (cell.Z * 0.47);
            light.LightEnergy = 1.35f + (float)System.Math.Sin(seconds * 6.0 + phase) * 0.06f;
            light.Visible = true;
        }
    }
}
