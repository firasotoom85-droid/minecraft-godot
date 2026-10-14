using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Population manager: keeps a handful of sheep around the player, and in
/// survival rises shades at night / during storms (never on peaceful).
/// Routes deaths into loot, and exposes spawn/clear helpers for the dev
/// console and respawn safety.
/// </summary>
public partial class Mobs : Node
{
    private const int SheepTarget = 6;
    private const float SheepSpawnMin = 18.0f;
    private const float SheepSpawnMax = 40.0f;
    private const float SheepDespawn = 120.0f;
    private const float ShadeSpawnMin = 24.0f;
    private const float ShadeSpawnMax = 44.0f;
    private const float ShadeDespawn = 80.0f;
    private const float FireSafety = 12.0f;

    public VoxelWorld? World;
    public Player? PlayerRef;
    public Fx? FxRef;
    public Sound? SoundRef;
    public Weather? WeatherRef;

    private readonly List<Sheep> _sheep = [];
    private readonly List<Shade> _shades = [];
    public int SheepCount => _sheep.Count;
    public int ShadeCount => _shades.Count;
    public bool IsStorm => WeatherRef?.Current == Weather.State.Storm;

    private double _timer = 1.5;
    private double _shadeTimer = 8.0;
    private readonly RandomNumberGenerator _rng = new();

    public void Setup(VoxelWorld world, Player player, Fx fx, Sound sound, Weather weather)
    {
        World = world;
        PlayerRef = player;
        FxRef = fx;
        SoundRef = sound;
        WeatherRef = weather;
        _rng.Randomize();
    }

    public override void _Process(double delta)
    {
        if (World == null || PlayerRef == null)
        {
            return;
        }

        _sheep.RemoveAll(mob => !IsInstanceValid(mob));
        _shades.RemoveAll(mob => !IsInstanceValid(mob));
        Vector3 pos = PlayerRef.GlobalPosition;
        _sheep.RemoveAll(mob => DespawnFar(mob, pos, SheepDespawn));
        _shades.RemoveAll(mob => DespawnFar(mob, pos, ShadeDespawn));

        if (Settings.Difficulty == Settings.DifficultyPeaceful && _shades.Count > 0)
        {
            foreach (Shade shade in _shades)
            {
                Puff(shade.GlobalPosition + new Vector3(0, 1.0f, 0), new Color(0.30f, 0.26f, 0.38f));
                shade.QueueFree();
            }

            _shades.Clear();
        }

        _timer -= delta;
        if (_timer <= 0.0)
        {
            _timer = 2.0;
            if (_sheep.Count < SheepTarget)
            {
                SpawnSheep();
            }
        }

        _shadeTimer -= delta;
        if (_shadeTimer <= 0.0)
        {
            _shadeTimer = IsStorm ? 2.5 : 5.0;
            bool dark = DayNight.IsNight(World.TimeOfDay) || IsStorm;
            int cap = Settings.Difficulty == Settings.DifficultyHard ? 9 : 6;
            if (Settings.Difficulty != Settings.DifficultyPeaceful && dark && _shades.Count < cap)
            {
                SpawnShade();
            }
        }
    }

    private bool DespawnFar(Mob mob, Vector3 playerPos, float radius)
    {
        if (mob.GlobalPosition.DistanceSquaredTo(playerPos) <= radius * radius)
        {
            return false;
        }

        mob.QueueFree();
        return true;
    }

    // -----------------------------------------------------------------------
    // Spawning
    // -----------------------------------------------------------------------

    private bool FindSpawnPoint(float min, float max, bool avoidFires, out Vector3 point)
    {
        point = Vector3.Zero;
        Vector3 basePos = PlayerRef!.GlobalPosition;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float ang = _rng.Randf() * Mathf.Tau;
            float dist = _rng.RandfRange(min, max);
            int x = Mathf.FloorToInt(basePos.X + Mathf.Cos(ang) * dist);
            int z = Mathf.FloorToInt(basePos.Z + Mathf.Sin(ang) * dist);
            int h = World!.SurfaceHeight(x, z);
            if (h <= World.SeaLevel + 1)
            {
                continue;
            }

            Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX), Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (!World.HasChunk(cp))
            {
                continue;
            }

            if (World.GetBlock(x, h, z) is not (BlockId.Grass or BlockId.SnowGrass or BlockId.Sand))
            {
                continue;
            }

            point = new Vector3(x + 0.5f, h + 1.0f, z + 0.5f);
            if (avoidFires && World.NearestCampfire(point, FireSafety) != null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void SpawnSheep()
    {
        if (!FindSpawnPoint(SheepSpawnMin, SheepSpawnMax, false, out Vector3 point))
        {
            return;
        }

        Sheep sheep = new();
        AddChild(sheep);
        sheep.Setup(World!, this, point);
        sheep.GlobalPosition = point + new Vector3(0, 1.0f, 0);
        sheep.GlobalRotation = new Vector3(0, _rng.Randf() * Mathf.Tau, 0);
        sheep.Died += OnMobDied;
        sheep.Hurt += OnMobHurt;
        sheep.Signalled += OnSignalled;
        _sheep.Add(sheep);
    }

    private void SpawnShade()
    {
        if (!FindSpawnPoint(ShadeSpawnMin, ShadeSpawnMax, true, out Vector3 point))
        {
            return;
        }

        Shade shade = new();
        AddChild(shade);
        shade.Setup(World!, this, point);
        shade.GlobalPosition = point + new Vector3(0, 1.2f, 0);
        shade.GlobalRotation = new Vector3(0, _rng.Randf() * Mathf.Tau, 0);
        shade.Died += OnMobDied;
        shade.Hurt += OnMobHurt;
        shade.Signalled += OnSignalled;
        _shades.Add(shade);
    }

    /// <summary>Dev-console helper: spawn n mobs close to the player.</summary>
    public int SpawnNear(bool shade, int count)
    {
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (!FindSpawnPoint(6.0f, 14.0f, shade, out Vector3 point))
            {
                break;
            }

            if (shade)
            {
                Shade s = new();
                AddChild(s);
                s.Setup(World!, this, point);
                s.GlobalPosition = point + new Vector3(0, 1.2f, 0);
                s.Died += OnMobDied;
                s.Hurt += OnMobHurt;
                s.Signalled += OnSignalled;
                _shades.Add(s);
            }
            else
            {
                Sheep s = new();
                AddChild(s);
                s.Setup(World!, this, point);
                s.GlobalPosition = point + new Vector3(0, 1.0f, 0);
                s.Died += OnMobDied;
                s.Hurt += OnMobHurt;
                s.Signalled += OnSignalled;
                _sheep.Add(s);
            }

            spawned += 1;
        }

        return spawned;
    }

    /// <summary>Respawn safety: poof hostile mobs within radius of a point.</summary>
    public void ClearHostilesNear(Vector3 pos, float radius)
    {
        foreach (Shade shade in _shades)
        {
            if (shade.GlobalPosition.DistanceSquaredTo(pos) > radius * radius)
            {
                continue;
            }

            Puff(shade.GlobalPosition + new Vector3(0, 1.0f, 0), new Color(0.30f, 0.26f, 0.38f));
            shade.QueueFree();
        }

        _shades.RemoveAll(mob => !IsInstanceValid(mob));
    }

    // -----------------------------------------------------------------------
    // Feedback + loot
    // -----------------------------------------------------------------------

    private void OnMobDied(Mob mob)
    {
        Vector3 pos = mob.GlobalPosition + new Vector3(0, 0.7f, 0);
        bool sheep = mob is Sheep;
        Puff(pos, sheep ? new Color(0.90f, 0.88f, 0.84f) : new Color(0.28f, 0.24f, 0.36f));
        SoundRef?.Play(sheep ? "baa" : "mobdie", pos, sheep ? 1.0f : 0.9f, -2.0f);

        if (PlayerRef == null || mob.Loot.Length == 0)
        {
            return;
        }

        foreach ((ItemId id, int min, int max, float chance) in mob.Loot)
        {
            if (_rng.Randf() > chance)
            {
                continue;
            }

            int count = _rng.RandiRange(min, max);
            int leftover = PlayerRef.Inventory.Add(id, count);
            int gained = count - leftover;
            if (gained > 0)
            {
                PlayerRef.EmitSignal(Player.SignalName.Notice, $"+{gained} {ItemDb.NameOf(id)}");
            }

            if (leftover > 0)
            {
                PlayerRef.EmitSignal(Player.SignalName.Notice, "Inventory full");
            }
        }
    }

    private void OnMobHurt(Mob mob, DamageInfo info)
    {
        if (_rng.Randf() < 0.6f)
        {
            SoundRef?.Play(mob is Sheep ? "baa" : "mobhurt", mob.GlobalPosition + new Vector3(0, 1.0f, 0), 1.0f, -4.0f);
        }
    }

    private void OnSignalled(Mob mob, string kind)
    {
        Vector3 pos = mob.GlobalPosition + new Vector3(0, 1.2f, 0);
        switch (kind)
        {
            case "tame":
                Puff(pos, new Color(0.95f, 0.45f, 0.60f));
                SoundRef?.Play("baa", pos, 1.1f, -2.0f);
                break;
            case "stay":
                Puff(pos, new Color(0.55f, 0.75f, 0.95f));
                break;
            case "follow":
                Puff(pos, new Color(0.60f, 0.95f, 0.60f));
                break;
        }
    }

    /// <summary>Public particle shortcut for mobs (smoke, hearts, poofs).</summary>
    public void Puff(Vector3 pos, Color color) => FxRef?.Puff(pos, color);

    /// <summary>Small swing feedback when a mob connects a hit.</summary>
    public void Lunge(Mob mob) => SoundRef?.Play("dig", mob.GlobalPosition + new Vector3(0, 1.2f, 0), 0.55f, -14.0f);
}
