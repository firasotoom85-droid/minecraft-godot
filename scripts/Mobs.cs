using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Population manager: a handful of passive animals around the player, and
/// hostile night/storm spawns (shades, rot packs, hollow archers). Routes
/// deaths into loot, and exposes spawn/clear helpers for the dev console and
/// respawn safety.
/// </summary>
public partial class Mobs : Node
{
    private const int PassiveTarget = 9;
    private const float PassiveSpawnMin = 18.0f;
    private const float PassiveSpawnMax = 40.0f;
    private const float PassiveDespawn = 120.0f;
    private const float HostileSpawnMin = 24.0f;
    private const float HostileSpawnMax = 44.0f;
    private const float HostileDespawn = 80.0f;
    private const float FireSafety = 12.0f;

    public VoxelWorld? World;
    public Player? PlayerRef;
    public Fx? FxRef;
    public Sound? SoundRef;
    public Weather? WeatherRef;

    private readonly List<Mob> _passive = [];
    private readonly List<Mob> _hostiles = [];
    public int PassiveCount => _passive.Count;
    public int HostileCount => _hostiles.Count;

    /// <summary>Live boss hook for the dungeon HUD/dev spawns.</summary>
    public EmberWarden? ActiveWarden
    {
        get
        {
            foreach (Mob mob in _hostiles)
            {
                if (mob is EmberWarden warden && IsInstanceValid(warden) && !warden.Dead)
                {
                    return warden;
                }
            }

            return null;
        }
    }
    public bool IsStorm => WeatherRef?.Current == Weather.State.Storm;

    private double _timer = 1.5;
    private double _hostileTimer = 8.0;
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

        _passive.RemoveAll(mob => !IsInstanceValid(mob));
        _hostiles.RemoveAll(mob => !IsInstanceValid(mob));
        Vector3 pos = PlayerRef.GlobalPosition;
        _passive.RemoveAll(mob => DespawnFar(mob, pos, PassiveDespawn));
        _hostiles.RemoveAll(mob => DespawnFar(mob, pos, HostileDespawn));

        if (Settings.Difficulty == Settings.DifficultyPeaceful && _hostiles.Count > 0)
        {
            foreach (Mob mob in _hostiles)
            {
                Puff(mob.GlobalPosition + new Vector3(0, 1.0f, 0), new Color(0.30f, 0.26f, 0.38f));
                mob.QueueFree();
            }

            _hostiles.Clear();
        }

        _timer -= delta;
        if (_timer <= 0.0)
        {
            _timer = 2.0;
            if (_passive.Count < PassiveTarget)
            {
                SpawnPassive();
            }
        }

        _hostileTimer -= delta;
        if (_hostileTimer <= 0.0)
        {
            _hostileTimer = IsStorm ? 2.5 : 5.0;
            bool dark = DayNight.IsNight(World.TimeOfDay) || IsStorm;
            int cap = Settings.Difficulty == Settings.DifficultyHard ? 10 : 7;
            if (Settings.Difficulty != Settings.DifficultyPeaceful && dark && _hostiles.Count < cap)
            {
                SpawnHostile();
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

    private void SpawnMob(Mob mob, Vector3 point, float yOffset, bool hostile)
    {
        AddChild(mob);
        mob.Setup(World!, this, point);
        mob.GlobalPosition = point + new Vector3(0, yOffset, 0);
        mob.GlobalRotation = new Vector3(0, _rng.Randf() * Mathf.Tau, 0);
        mob.Died += OnMobDied;
        mob.Hurt += OnMobHurt;
        mob.Signalled += OnSignalled;
        (hostile ? _hostiles : _passive).Add(mob);
    }

    private void SpawnPassive()
    {
        if (!FindSpawnPoint(PassiveSpawnMin, PassiveSpawnMax, false, out Vector3 point))
        {
            return;
        }

        Mob mob = _rng.RandiRange(0, 9) switch
        {
            < 3 => new Sheep(),
            < 6 => new Cow(),
            < 8 => new Pig(),
            _ => new Chicken(),
        };
        SpawnMob(mob, point, 1.0f, false);
    }

    private void SpawnHostile()
    {
        int roll = _rng.RandiRange(0, 9);
        if (roll < 5)
        {
            if (FindSpawnPoint(HostileSpawnMin, HostileSpawnMax, true, out Vector3 point))
            {
                SpawnMob(new Shade(), point, 1.2f, true);
            }
        }
        else if (roll < 8)
        {
            int count = _rng.RandiRange(2, 3);
            for (int i = 0; i < count; i++)
            {
                if (!FindSpawnPoint(HostileSpawnMin, HostileSpawnMax, true, out Vector3 point))
                {
                    break;
                }

                SpawnMob(new Rot(), point, 1.2f, true);
            }
        }
        else if (FindSpawnPoint(HostileSpawnMin, HostileSpawnMax, true, out Vector3 point))
        {
            SpawnMob(new Hollow(), point, 1.2f, true);
        }
    }

    /// <summary>Dev-console helper: spawn n mobs close to the player.</summary>
    public int SpawnNear(bool shade, int count) => SpawnKind(shade ? "shade" : "sheep", count);

    /// <summary>Dev-console helper for every released mob type.</summary>
    public int SpawnKind(string kind, int count)
    {
        bool hostile = kind is "shade" or "rot" or "hollow" or "warden";
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (kind is not ("shade" or "rot" or "hollow" or "warden" or "sheep" or "cow" or "pig" or "chicken"))
            {
                break;
            }

            if (!FindSpawnPoint(6.0f, 14.0f, hostile, out Vector3 point))
            {
                break;
            }

            Mob mob = kind switch
            {
                "shade" => new Shade(),
                "rot" => new Rot(),
                "hollow" => new Hollow(),
                "warden" => new EmberWarden(),
                "cow" => new Cow(),
                "pig" => new Pig(),
                "chicken" => new Chicken(),
                _ => new Sheep(),
            };
            SpawnMob(mob, point, hostile ? 1.2f : 1.0f, hostile);
            spawned += 1;
        }

        return spawned;
    }

    /// <summary>Dungeon hook: place the boss at an exact arena position.</summary>
    public EmberWarden? SpawnWarden(Vector3 pos)
    {
        EmberWarden warden = new();
        SpawnMob(warden, pos, 0.2f, true);
        return warden;
    }

    /// <summary>Boss phase hook: summon adds around an arena position.</summary>
    public void SpawnAdds(Vector3 center, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float ang = i * Mathf.Tau / Mathf.Max(count, 1);
            Vector3 point = center + new Vector3(Mathf.Cos(ang) * 2.5f, 1.0f, Mathf.Sin(ang) * 2.5f);
            SpawnMob(new Shade(), point, 0.0f, true);
        }
    }

    /// <summary>Respawn/sleep safety: poof hostile mobs within radius of a point.</summary>
    public void ClearHostilesNear(Vector3 pos, float radius)
    {
        foreach (Mob mob in _hostiles)
        {
            if (mob.GlobalPosition.DistanceSquaredTo(pos) > radius * radius)
            {
                continue;
            }

            Puff(mob.GlobalPosition + new Vector3(0, 1.0f, 0), new Color(0.30f, 0.26f, 0.38f));
            mob.QueueFree();
        }

        _hostiles.RemoveAll(mob => !IsInstanceValid(mob));
    }

    // -----------------------------------------------------------------------
    // Feedback + loot
    // -----------------------------------------------------------------------

    private void OnMobDied(Mob mob)
    {
        Vector3 pos = mob.GlobalPosition + new Vector3(0, 0.7f, 0);
        bool passive = mob is Sheep or Cow or Pig or Chicken;
        Puff(pos, passive ? new Color(0.90f, 0.88f, 0.84f) : new Color(0.28f, 0.24f, 0.36f));
        SoundRef?.Play(passive ? "baa" : "mobdie", pos, passive ? 1.0f : 0.9f, -2.0f);

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
            bool sheep = mob is Sheep;
            SoundRef?.Play(sheep ? "baa" : "mobhurt", mob.GlobalPosition + new Vector3(0, 1.0f, 0), 1.0f, -4.0f);
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
