using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Owns placed boats and their optional save data. Restored boats wait for
/// their containing chunk to stream in; no chunk is force-generated for a
/// vehicle save or movement query.
/// </summary>
public partial class BoatSystem : Node
{
    private readonly List<Boat> _boats = [];
    private readonly List<BoatSave> _pendingRestore = [];
    private readonly Dictionary<Boat, Vector3> _lastDirtyPositions = [];
    private VoxelWorld? _world;
    private Player? _player;
    private Sound? _sound;

    public void Setup(VoxelWorld world, Player player, Sound sound)
    {
        _world = world;
        _player = player;
        _sound = sound;
        world.BoatSystem = this;
        player.Boats = this;
        if (world.SavedBoats != null)
            _pendingRestore.AddRange(world.SavedBoats);
    }

    public override void _Process(double delta)
    {
        if (_world == null) return;
        for (int i = _boats.Count - 1; i >= 0; i--)
        {
            Boat boat = _boats[i];
            if (!GodotObject.IsInstanceValid(boat))
            {
                _lastDirtyPositions.Remove(boat);
                _boats.RemoveAt(i);
                continue;
            }

            Vector3 position = boat.GlobalPosition;
            if (!_lastDirtyPositions.TryGetValue(boat, out Vector3 last)
                    || last.DistanceSquaredTo(position) >= 4.0f)
            {
                _lastDirtyPositions[boat] = position;
                _world.MarkDirty();
            }
        }
        if (_pendingRestore.Count == 0) return;
        for (int i = _pendingRestore.Count - 1; i >= 0; i--)
        {
            BoatSave save = _pendingRestore[i];
            if (save.Position is not { Length: 3 } p
                    || !float.IsFinite(p[0]) || !float.IsFinite(p[1]) || !float.IsFinite(p[2]))
            {
                _pendingRestore.RemoveAt(i);
                continue;
            }

            if (!float.IsFinite(save.Yaw))
            {
                _pendingRestore.RemoveAt(i);
                continue;
            }
            Vector3 position = new(p[0], p[1], p[2]);
            Vector2I chunk = new(
                    Mathf.FloorToInt(position.X / VoxelWorld.ChunkX),
                    Mathf.FloorToInt(position.Z / VoxelWorld.ChunkZ));
            if (!_world.HasChunk(chunk)) continue;

            Vector3 velocity = save.Velocity is { Length: 3 } v
                    && float.IsFinite(v[0]) && float.IsFinite(v[1]) && float.IsFinite(v[2])
                    ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;
            Boat boat = Spawn(position, save.Yaw, velocity);
            _pendingRestore.RemoveAt(i);
            if (save.Occupied && _player != null && _player.CurrentBoat == null)
                TryBoard(boat, _player);
        }
    }

    public bool TryPlaceBoat(Vector3I waterCell, BlockId water)
    {
        if (_world == null || !BlockDb.IsWater(water)
                || !_world.TryGetLoadedBlock(waterCell.X, waterCell.Y, waterCell.Z, out BlockId current)
                || !BlockDb.IsWater(current))
            return false;

        Vector3 position = new(
                waterCell.X + 0.5f,
                waterCell.Y + BlockDb.WaterHeight(current) - 0.28f,
                waterCell.Z + 0.5f);
        if (!BoatPhysics.FootprintLoaded(_world, position)) return false;
        foreach (Boat other in _boats)
            if (GodotObject.IsInstanceValid(other)
                    && other.GlobalPosition.DistanceSquaredTo(position) < 2.25f)
                return false;

        // Keep enough headroom for the boat hull and seated player.
        int x = waterCell.X;
        int z = waterCell.Z;
        if (!_world.TryGetLoadedBlock(x, waterCell.Y + 1, z, out BlockId above)
                || (BlockDb.IsSolid(above) && !BlockDb.IsWater(above)))
            return false;

        Spawn(position, _player?.GlobalRotation.Y ?? 0.0f, Vector3.Zero);
        _sound?.Play("place", position, 0.8f, -2.0f);
        return true;
    }

    public bool TryBoard(Boat boat, Player player)
    {
        if (boat.IsOccupied || player.CurrentBoat != null
                || boat.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > 16.0f)
            return false;

        boat.SetRider(player);
        player.EnterBoat(boat);
        _world?.MarkDirty();
        _sound?.Play("place", boat.GlobalPosition, 1.05f, -4.0f);
        return true;
    }

    public bool Dismount(Boat boat)
    {
        if (_world == null || boat.Rider is not { } player) return false;
        Vector3 right = boat.GlobalBasis.X;
        Vector3 back = boat.GlobalBasis.Z;
        Vector3[] candidates =
        [
            boat.GlobalPosition + right * 1.35f + Vector3.Up * 0.15f,
            boat.GlobalPosition - right * 1.35f + Vector3.Up * 0.15f,
            boat.GlobalPosition + back * 1.45f + Vector3.Up * 0.15f,
            boat.GlobalPosition - back * 1.45f + Vector3.Up * 0.15f,
        ];
        foreach (Vector3 candidate in candidates)
        {
            if (!CanDismountAt(candidate) || !_world.IsSafePlayerPosition(candidate))
                continue;
            if (!player.TryTeleportToSafePosition(candidate))
                continue;

            _world.MarkDirty();
            _sound?.Play("place", candidate, 0.9f, -5.0f);
            return true;
        }
        return false;
    }

    public void RemoveBoat(Boat boat, Player player)
    {
        if (_world == null || !_boats.Contains(boat)) return;
        if (player.Mode == GameMode.Survival && !player.Inventory.CanFit(ItemId.WoodenBoat, 1))
        {
            player.EmitSignal(Player.SignalName.InventoryFull);
            return;
        }
        if (boat.Rider is { } rider)
        {
            if (!Dismount(boat))
            {
                Vector3 fallback = boat.GlobalPosition + Vector3.Up * 1.1f;
                if (!CanDismountAt(fallback) || !_world.IsSafePlayerPosition(fallback)
                        || !rider.TryTeleportToSafePosition(fallback))
                    return;
            }
        }

        _boats.Remove(boat);
        _lastDirtyPositions.Remove(boat);
        if (player.Mode == GameMode.Survival)
            player.Inventory.Add(ItemId.WoodenBoat, 1);
        _sound?.Play("place", boat.GlobalPosition, 0.72f, -1.0f);
        boat.QueueFree();
        _world.MarkDirty();
    }

    internal void PlayRow(Vector3 position, float speed)
    {
        if (speed > 1.2f) _sound?.Play("splash", position, 1.18f, -11.0f);
    }

    public List<BoatSave>? ToSave()
    {
        List<BoatSave> saved = [];
        foreach (Boat boat in _boats)
        {
            if (!GodotObject.IsInstanceValid(boat) || !boat.IsInsideTree()) continue;
            Vector3 p = boat.GlobalPosition;
            Vector3 v = boat.Velocity;
            _lastDirtyPositions[boat] = p;
            saved.Add(new BoatSave(
                    [p.X, p.Y, p.Z], boat.GlobalRotation.Y,
                    [v.X, v.Y, v.Z], boat.IsOccupied));
        }

        // Boats outside the loaded radius remain queued for restoration.
        // Carry them forward on save so unloading far chunks cannot discard them.
        saved.AddRange(_pendingRestore);
        return saved.Count == 0 ? null : saved;
    }

    private Boat Spawn(Vector3 position, float yaw, Vector3 velocity)
    {
        Boat boat = new();
        boat.Name = $"WoodenBoat{_boats.Count + 1}";
        boat.Initialize(this, _world!, position, yaw, velocity);
        AddChild(boat);
        _boats.Add(boat);
        _world?.MarkDirty();
        return boat;
    }

    private bool CanDismountAt(Vector3 position)
    {
        if (_world == null) return false;
        int x = Mathf.FloorToInt(position.X);
        int y = Mathf.FloorToInt(position.Y);
        int z = Mathf.FloorToInt(position.Z);
        if (!_world.TryGetLoadedBlock(x, y, z, out BlockId feet)
                || !_world.TryGetLoadedBlock(x, y + 1, z, out BlockId head))
            return false;
        return !BlockDb.IsSolid(feet) && !BlockDb.IsSolid(head)
                && feet != BlockId.Lava && head != BlockId.Lava;
    }

    public override void _ExitTree()
    {
        if (_world?.BoatSystem == this) _world.BoatSystem = null;
        if (_player?.Boats == this) _player.Boats = null;
    }
}
