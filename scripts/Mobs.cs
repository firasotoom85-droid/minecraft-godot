using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Population and ecology director. It owns bounded populations, biome/cave
/// spawning, herd relationships, breeding, hostile alerts and transient shots.
/// AI is local to each mob; this manager performs only small nearby scans.
/// </summary>
public partial class Mobs : Node
{
    private const int PassiveTarget = 10;
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
    private double _breedingTimer = 2.0;
    private double _maintenanceTimer = 0.5;
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

        _maintenanceTimer -= delta;
        if (_maintenanceTimer <= 0.0)
        {
            _maintenanceTimer = 0.5;
            _passive.RemoveAll(static mob => !GodotObject.IsInstanceValid(mob));
            _hostiles.RemoveAll(static mob => !GodotObject.IsInstanceValid(mob));
            Vector3 playerPosition = PlayerRef.GlobalPosition;
            _passive.RemoveAll(mob => DespawnFar(mob, playerPosition, PassiveDespawn));
            _hostiles.RemoveAll(mob => DespawnFar(mob, playerPosition, HostileDespawn));
        }

        bool dark = DayNight.IsNight(World.TimeOfDay) || IsStorm;
        if (Settings.Difficulty == Settings.DifficultyPeaceful && _hostiles.Count > 0)
        {
            // Remove before QueueFree: deletion is deferred until the frame
            // boundary, so validity filtering here would retain the wrapper
            // and let the next frame read its disposed Node3D properties.
            for (int i = _hostiles.Count - 1; i >= 0; i--)
            {
                Mob mob = _hostiles[i];
                _hostiles.RemoveAt(i);
                if (!GodotObject.IsInstanceValid(mob))
                {
                    continue;
                }

                Vector3 position = mob.GlobalPosition;
                Puff(position + new Vector3(0, 1.0f, 0), new Color(0.30f, 0.26f, 0.38f));
                mob.QueueFree();
            }
        }
        else if (!dark)
        {
            // Surface-only hostiles retreat at dawn. Cave fauna and the
            // dungeon boss are not affected by the surface day/night cycle.
            // Remove each surface actor from the population before deferred
            // freeing so it cannot be revisited on the next population tick.
            for (int i = _hostiles.Count - 1; i >= 0; i--)
            {
                Mob mob = _hostiles[i];
                if (!GodotObject.IsInstanceValid(mob))
                {
                    _hostiles.RemoveAt(i);
                    continue;
                }

                if (mob is EmberWarden or CaveStalker or CrystalWisp or Dreadwing)
                {
                    continue;
                }

                Vector3 position = mob.GlobalPosition;
                if (IsUnderground(position))
                {
                    continue;
                }

                _hostiles.RemoveAt(i);
                Puff(position + new Vector3(0, 1.0f, 0), new Color(0.28f, 0.24f, 0.36f));
                mob.QueueFree();
            }
        }

        _timer -= delta;
        if (_timer <= 0.0)
        {
            _timer = 2.5;
            if (_passive.Count < PassiveTarget)
            {
                SpawnPassiveGroup();
            }
        }

        _breedingTimer -= delta;
        if (_breedingTimer <= 0.0)
        {
            _breedingTimer = 2.0;
            TryBreedPassives();
        }

        _hostileTimer -= delta;
        if (_hostileTimer <= 0.0)
        {
            _hostileTimer = IsStorm ? 2.8 : 5.5;
            int cap = MobCombatRules.HostilePopulationCap;
            if (Settings.Difficulty != Settings.DifficultyPeaceful && _hostiles.Count < cap)
            {
                bool underground = IsUnderground(PlayerRef.GlobalPosition);
                if (dark || underground)
                {
                    SpawnHostile(dark);
                }
            }
        }
    }

    private bool IsUnderground(Vector3 position)
    {
        if (World == null)
        {
            return false;
        }

        int x = Mathf.FloorToInt(position.X);
        int z = Mathf.FloorToInt(position.Z);
        return position.Y < World.SurfaceHeight(x, z) - 6.0f;
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
    // Shared local perception: herd cohesion, flight response and alerts.
    // -----------------------------------------------------------------------

    public Vector3 FleeDirection(Mob source, float radius)
    {
        Vector3 flee = Vector3.Zero;
        float radiusSq = radius * radius;

        if (PlayerRef is { } player && !player.Stats.Dead)
        {
            Vector3 away = source.GlobalPosition - player.GlobalPosition;
            away.Y = 0.0f;
            float d2 = away.LengthSquared();
            bool offeringFood = source.BreedingFood != ItemId.None
                    && player.Selected >= 0 && player.Selected < Inventory.HotbarSize
                    && player.Inventory.Hotbar[player.Selected].Id == source.BreedingFood;
            float playerRadius = source.IsAlerted ? 16.0f : radius;
            if (!offeringFood && d2 > 0.001f && d2 < playerRadius * playerRadius)
            {
                flee += away.Normalized() * (1.0f - Mathf.Sqrt(d2) / playerRadius);
            }
        }

        foreach (Mob threat in _hostiles)
        {
            if (!IsInstanceValid(threat) || threat.Dead || threat == source)
            {
                continue;
            }

            Vector3 away = source.GlobalPosition - threat.GlobalPosition;
            away.Y = 0.0f;
            float d2 = away.LengthSquared();
            if (d2 > 0.001f && d2 < radiusSq)
            {
                flee += away.Normalized() * (1.0f - Mathf.Sqrt(d2) / radius) * 1.4f;
            }
        }

        return flee.LengthSquared() > 0.01f ? flee.Normalized() : Vector3.Zero;
    }

    public Vector3 HerdDirection(Mob source, float radius)
    {
        Vector3 centre = Vector3.Zero;
        Vector3 separation = Vector3.Zero;
        int neighbours = 0;
        float radiusSq = radius * radius;

        foreach (Mob other in _passive)
        {
            if (!IsInstanceValid(other) || other == source || other.Dead
                    || other.GetType() != source.GetType())
            {
                continue;
            }

            Vector3 offset = other.GlobalPosition - source.GlobalPosition;
            offset.Y = 0.0f;
            float d2 = offset.LengthSquared();
            if (d2 < 0.001f || d2 > radiusSq)
            {
                continue;
            }

            centre += other.GlobalPosition;
            neighbours++;
            if (d2 < 2.8f * 2.8f)
            {
                separation -= offset.Normalized() * (1.0f - Mathf.Sqrt(d2) / 2.8f);
            }
        }

        if (neighbours == 0)
        {
            return Vector3.Zero;
        }

        Vector3 cohesion = centre / neighbours - source.GlobalPosition;
        cohesion.Y = 0.0f;
        return cohesion.Normalized() * 0.55f + separation * 1.2f;
    }

    public void AlertHostiles(Vector3 position, float radius, float seconds = 8.0f)
    {
        float radiusSq = radius * radius;
        foreach (Mob mob in _hostiles)
        {
            if (IsInstanceValid(mob) && !mob.Dead
                    && mob.GlobalPosition.DistanceSquaredTo(position) <= radiusSq)
            {
                mob.SetAlerted(seconds);
            }
        }
    }

    public int HostilesNear<T>(Vector3 position, float radius) where T : Mob
    {
        int count = 0;
        float radiusSq = radius * radius;
        foreach (Mob mob in _hostiles)
        {
            if (mob is T && IsInstanceValid(mob) && !mob.Dead
                    && mob.GlobalPosition.DistanceSquaredTo(position) <= radiusSq)
            {
                count++;
            }
        }

        return count;
    }

    public void FireProjectile(Vector3 origin, Vector3 target, float damage,
            Color color, float speed, string sound)
    {
        MobProjectile projectile = new();
        AddChild(projectile);
        projectile.Setup(this, origin, target, damage, color, speed, sound);
    }

    // -----------------------------------------------------------------------
    // Spawning
    // -----------------------------------------------------------------------

    private bool FindSpawnPoint(float min, float max, bool avoidFires,
            out Vector3 point, Biome? requiredBiome = null)
    {
        point = Vector3.Zero;
        if (World == null || PlayerRef == null)
        {
            return false;
        }

        Vector3 basePos = PlayerRef.GlobalPosition;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float angle = _rng.Randf() * Mathf.Tau;
            float distance = _rng.RandfRange(min, max);
            int x = Mathf.FloorToInt(basePos.X + Mathf.Cos(angle) * distance);
            int z = Mathf.FloorToInt(basePos.Z + Mathf.Sin(angle) * distance);
            int h = World.SurfaceHeight(x, z);
            if (h <= World.SeaLevel + 1 || h >= World.Height - 4)
            {
                continue;
            }

            Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (!World.HasChunk(cp))
            {
                continue;
            }

            Biome biome = World.BiomeAt(x, z, h);
            if (requiredBiome.HasValue && biome != requiredBiome.Value)
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

    private bool FindCaveSpawnPoint(float min, float max, out Vector3 point)
    {
        point = Vector3.Zero;
        if (World == null || PlayerRef == null || !IsUnderground(PlayerRef.GlobalPosition))
        {
            return false;
        }

        Vector3 basePos = PlayerRef.GlobalPosition;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float angle = _rng.Randf() * Mathf.Tau;
            float distance = _rng.RandfRange(min, max);
            int x = Mathf.FloorToInt(basePos.X + Mathf.Cos(angle) * distance);
            int z = Mathf.FloorToInt(basePos.Z + Mathf.Sin(angle) * distance);
            Vector2I cp = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (!World.HasChunk(cp))
            {
                continue;
            }

            int surface = World.SurfaceHeight(x, z);
            int y = Mathf.Clamp(Mathf.FloorToInt(basePos.Y + _rng.RandfRange(-3.0f, 3.0f)),
                    5, World.Height - 4);
            if (y >= surface - 5 || !World.IsCave(x, y, z, surface))
            {
                continue;
            }

            BlockId here = World.GetBlock(x, y, z);
            BlockId above = World.GetBlock(x, y + 1, z);
            BlockId floor = World.GetBlock(x, y - 1, z);
            if (here != BlockId.Air || above != BlockId.Air
                    || floor == BlockId.Air || floor == BlockId.Lava || BlockDb.IsWater(floor))
            {
                continue;
            }

            Vector3 candidate = new(x + 0.5f, y + 0.05f, z + 0.5f);
            if (World.NearestCampfire(candidate, FireSafety) != null)
            {
                continue;
            }

            point = candidate;
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

    private void SpawnPassiveGroup()
    {
        if (World == null || PlayerRef == null)
        {
            return;
        }

        if (!FindSpawnPoint(PassiveSpawnMin, PassiveSpawnMax, false, out Vector3 point))
        {
            return;
        }

        int x = Mathf.FloorToInt(point.X);
        int z = Mathf.FloorToInt(point.Z);
        Biome biome = World.BiomeAt(x, z, World.SurfaceHeight(x, z));
        if (biome == Biome.Desert)
        {
            return; // dry sand flats do not support the ordinary grazing herds
        }

        if (biome == Biome.Birch && _rng.Randf() < 0.18f)
        {
            SpawnMob(new Briarback(), point, 0.0f, false);
            return;
        }

        Mob prototype = biome == Biome.Snow
                ? new Sheep()
                : _rng.RandiRange(0, 9) switch
                {
                    < 3 => new Sheep(),
                    < 6 => new Cow(),
                    < 8 => new Pig(),
                    _ => new Chicken(),
                };

        HerdProfile herd = HerdProfile.For(prototype);
        int groupSize = _rng.RandiRange(herd.Minimum, herd.Maximum);
        groupSize = Mathf.Min(groupSize, PassiveTarget - _passive.Count);
        for (int i = 0; i < groupSize && _passive.Count < PassiveTarget; i++)
        {
            Vector3 spawn = point;
            if (i > 0)
            {
                float angle = _rng.Randf() * Mathf.Tau;
                spawn += new Vector3(Mathf.Cos(angle) * herd.Spacing, 0, Mathf.Sin(angle) * herd.Spacing);
            }

            Mob animal = i == 0 ? prototype : CreatePassive(prototype);
            SpawnMob(animal, spawn, 0.0f, false);
        }
    }

    public readonly record struct HerdProfile(int Minimum, int Maximum, float Spacing, float Cohesion)
    {
        public static HerdProfile For(Mob species) => species switch
        {
            Chicken => new(3, 5, 1.15f, 0.50f),
            Sheep => new(2, 4, 1.65f, 0.38f),
            Cow => new(2, 3, 2.10f, 0.30f),
            Pig => new(2, 3, 1.80f, 0.34f),
            _ => new(1, 2, 1.70f, 0.30f),
        };
    }

    private static Mob CreatePassive(Mob prototype) => prototype switch
    {
        Sheep => new Sheep(),
        Cow => new Cow(),
        Pig => new Pig(),
        Chicken => new Chicken(),
        _ => new Sheep(),
    };

    private void TryBreedPassives()
    {
        if (_passive.Count < 2 || _passive.Count >= PassiveTarget)
        {
            return;
        }

        for (int i = 0; i < _passive.Count; i++)
        {
            Mob first = _passive[i];
            if (!IsInstanceValid(first) || first.Dead || !first.InLove || first.IsBaby)
            {
                continue;
            }

            for (int j = i + 1; j < _passive.Count; j++)
            {
                Mob second = _passive[j];
                if (!IsInstanceValid(second) || !first.CanBreedWith(second)
                        || first.GlobalPosition.DistanceSquaredTo(second.GlobalPosition) > 9.0f)
                {
                    continue;
                }

                Vector3 midpoint = (first.GlobalPosition + second.GlobalPosition) * 0.5f;
                midpoint.Y = Mathf.Max(first.GlobalPosition.Y, second.GlobalPosition.Y);
                if (!TryFindBabySpawnPosition(midpoint, out Vector3 childPosition))
                {
                    continue;
                }

                Mob baby = CreatePassive(first);
                first.MarkBred();
                second.MarkBred();
                SpawnMob(baby, childPosition, 0.0f, false);
                baby.SetBaby();
                Puff(childPosition + new Vector3(0, 1.0f, 0), new Color(1.0f, 0.55f, 0.72f));
                SoundRef?.Play("mobgrowl", childPosition, 1.35f, -8.0f);
                PlayerRef?.EmitSignal(Player.SignalName.Notice, $"A baby {baby.DisplayName} was born");
                return;
            }
        }
    }

    public static bool IsBabySpawnCellSafe(BlockId floor, BlockId feet, BlockId body, BlockId head) =>
            BlockDb.DefOf(floor).Solid && feet == BlockId.Air
            && body == BlockId.Air && head == BlockId.Air;

    private bool TryFindBabySpawnPosition(Vector3 midpoint, out Vector3 position)
    {
        position = Vector3.Zero;
        if (World == null) return false;

        // Require an intact floor and two clear voxels above it. Search a small
        // deterministic footprint around the parents before consuming love state.
        Vector2[] offsets =
        [
            Vector2.Zero, new(0.45f, 0), new(-0.45f, 0), new(0, 0.45f), new(0, -0.45f),
            new(0.65f, 0.65f), new(-0.65f, 0.65f), new(0.65f, -0.65f), new(-0.65f, -0.65f),
        ];
        foreach (Vector2 offset in offsets)
        {
            int x = Mathf.FloorToInt(midpoint.X + offset.X);
            int z = Mathf.FloorToInt(midpoint.Z + offset.Y);
            int feetY = Mathf.FloorToInt(midpoint.Y);
            Vector2I chunk = new(Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
            if (!World.HasChunk(chunk) || feetY < 1 || feetY >= World.Height - 2) continue;

            BlockId floor = World.GetBlock(x, feetY - 1, z);
            if (!IsBabySpawnCellSafe(floor, World.GetBlock(x, feetY, z),
                    World.GetBlock(x, feetY + 1, z), World.GetBlock(x, feetY + 2, z))) continue;

            Vector3 candidate = new(x + 0.5f, feetY, z + 0.5f);
            bool occupied = false;
            foreach (Mob other in _passive)
            {
                if (IsInstanceValid(other) && !other.Dead
                        && other.GlobalPosition.DistanceSquaredTo(candidate) < 1.0f)
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied)
            {
                position = candidate;
                return true;
            }
        }

        return false;
    }

    private void SpawnHostile(bool surfaceDark)
    {
        if (World == null || PlayerRef == null)
        {
            return;
        }

        bool underground = IsUnderground(PlayerRef.GlobalPosition);
        if (underground && FindCaveSpawnPoint(HostileSpawnMin * 0.45f,
                HostileSpawnMax * 0.45f, out Vector3 cavePoint))
        {
            int caveRoll = _rng.RandiRange(0, 9);
            Mob caveMob = caveRoll switch
            {
                < 4 => new CaveStalker(),
                < 7 => new CrystalWisp(),
                _ => new Hollow(),
            };
            SpawnMob(caveMob, cavePoint, 0.0f, true);
            return;
        }

        if (!surfaceDark || !FindSpawnPoint(HostileSpawnMin, HostileSpawnMax,
                true, out Vector3 point))
        {
            return;
        }

        int x = Mathf.FloorToInt(point.X);
        int z = Mathf.FloorToInt(point.Z);
        Biome biome = World.BiomeAt(x, z, World.SurfaceHeight(x, z));
        int cap = MobCombatRules.HostilePopulationCap;
        int roll = _rng.RandiRange(0, 99);

        // Keep the rare flier to one active encounter at a time.
        bool dreadwingActive = false;
        foreach (Mob existing in _hostiles)
        {
            if (existing is Dreadwing && IsInstanceValid(existing) && !existing.Dead)
            {
                dreadwingActive = true;
                break;
            }
        }

        if (!IsStorm && roll < 4 && !dreadwingActive && _hostiles.Count < cap)
        {
            SpawnMob(new Dreadwing(), point, 3.2f, true);
        }
        else if (biome == Biome.Birch && roll < 38)
        {
            int count = _rng.RandiRange(2, 3);
            for (int i = 0; i < count && _hostiles.Count < cap; i++)
            {
                if (!FindSpawnPoint(HostileSpawnMin, HostileSpawnMax,
                        true, out Vector3 packPoint, Biome.Birch))
                {
                    break;
                }

                SpawnMob(new Rot(), packPoint, 0.0f, true);
            }
        }
        else if (biome == Biome.Snow && roll < 72)
        {
            SpawnMob(new Hollow(), point, 0.0f, true);
        }
        else if (roll < 68 || IsStorm)
        {
            SpawnMob(new Shade(), point, 0.0f, true);
        }
        else
        {
            SpawnMob(new Hollow(), point, 0.0f, true);
        }
    }

    /// <summary>Dev-console helper: spawn n mobs close to the player.</summary>
    public int SpawnNear(bool shade, int count) => SpawnKind(shade ? "shade" : "sheep", count);

    /// <summary>Dev-console helper for every supported creature type.</summary>
    public int SpawnKind(string kind, int count)
    {
        bool hostile = kind is "shade" or "rot" or "hollow" or "warden"
                or "stalker" or "briarback" or "crystalwisp" or "dreadwing";
        if (count <= 0 || (hostile && Settings.Difficulty == Settings.DifficultyPeaceful))
        {
            return 0;
        }

        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (kind is not ("shade" or "rot" or "hollow" or "warden"
                    or "stalker" or "briarback" or "crystalwisp" or "dreadwing"
                    or "sheep" or "cow" or "pig" or "chicken"))
            {
                break;
            }

            Vector3 point = Vector3.Zero;
            bool caveKind = kind is "stalker" or "crystalwisp";
            bool found = caveKind
                    ? FindCaveSpawnPoint(4.0f, 10.0f, out point)
                            || FindSpawnPoint(6.0f, 14.0f, hostile, out point)
                    : FindSpawnPoint(6.0f, 14.0f, hostile, out point,
                            kind == "briarback" ? Biome.Birch : null);
            if (!found)
            {
                break;
            }

            Mob mob = kind switch
            {
                "shade" => new Shade(),
                "rot" => new Rot(),
                "hollow" => new Hollow(),
                "warden" => new EmberWarden(),
                "stalker" => new CaveStalker(),
                "briarback" => new Briarback(),
                "crystalwisp" => new CrystalWisp(),
                "dreadwing" => new Dreadwing(),
                "cow" => new Cow(),
                "pig" => new Pig(),
                "chicken" => new Chicken(),
                _ => new Sheep(),
            };
            SpawnMob(mob, point, kind == "dreadwing" ? 3.2f : 0.0f, hostile);
            spawned++;
        }

        return spawned;
    }

    /// <summary>Dungeon hook: place the boss at an exact arena position.</summary>
    public EmberWarden? SpawnWarden(Vector3 pos)
    {
        if (Settings.Difficulty == Settings.DifficultyPeaceful)
        {
            return null;
        }

        EmberWarden warden = new();
        SpawnMob(warden, pos, 0.2f, true);
        return warden;
    }

    /// <summary>Boss phase hook: summon adds around an arena position.</summary>
    public void SpawnAdds(Vector3 center, int count)
    {
        if (Settings.Difficulty == Settings.DifficultyPeaceful || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.Tau / Mathf.Max(count, 1);
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * 2.5f, 1.0f,
                    Mathf.Sin(angle) * 2.5f);
            SpawnMob(new Shade(), point, 0.0f, true);
        }
    }

    /// <summary>Respawn/sleep safety: poof hostile mobs within radius of a point.</summary>
    public void ClearHostilesNear(Vector3 pos, float radius)
    {
        float radiusSquared = radius * radius;
        for (int i = _hostiles.Count - 1; i >= 0; i--)
        {
            Mob mob = _hostiles[i];
            if (!GodotObject.IsInstanceValid(mob))
            {
                _hostiles.RemoveAt(i);
                continue;
            }

            Vector3 mobPosition = mob.GlobalPosition;
            if (mobPosition.DistanceSquaredTo(pos) > radiusSquared)
            {
                continue;
            }

            // QueueFree is deferred; remove from the live-population collection
            // before the node becomes a disposed wrapper on a later frame.
            _hostiles.RemoveAt(i);
            Puff(mobPosition + new Vector3(0, 1.0f, 0),
                    new Color(0.30f, 0.26f, 0.38f));
            mob.QueueFree();
        }
    }

    // -----------------------------------------------------------------------
    // Feedback and loot.
    // -----------------------------------------------------------------------

    private void OnMobDied(Mob mob)
    {
        // Remove synchronously while the dying actor is still valid. Mob.Die
        // queues deletion after raising this event, which is too late for
        // deferred validity-based pruning in the population manager.
        _passive.Remove(mob);
        _hostiles.Remove(mob);
        Vector3 pos = mob.GlobalPosition + new Vector3(0, 0.7f, 0);
        bool passive = mob.IsPassive;
        Puff(pos, passive ? new Color(0.90f, 0.88f, 0.84f)
                : new Color(0.28f, 0.24f, 0.36f));
        string sound = mob switch
        {
            Briarback => "mobgrowl",
            CaveStalker => "mobchitter",
            CrystalWisp => "mobcrystal",
            Dreadwing => "mobdread",
            Sheep => "baa",
            _ => "mobdie",
        };
        SoundRef?.Play(sound, pos, passive ? 1.0f : 0.9f, -2.0f);

        if (PlayerRef == null) return;
        int experience = MobCombatRules.ExperienceReward(mob);
        PlayerRef.Stats.AddExperience(experience);
        PlayerRef.EmitSignal(Player.SignalName.Notice, $"+{experience} XP");
        if (mob.Loot.Length == 0) return;

        foreach ((ItemId id, int min, int max, float chance) in mob.Loot)
        {
            if (_rng.Randf() > chance)
            {
                continue;
            }

            int count = _rng.RandiRange(min, max);
            if (count <= 0)
            {
                continue;
            }

            int leftover = PlayerRef.Inventory.Add(id, count);
            int gained = count - leftover;
            if (gained > 0)
            {
                PlayerRef.EmitSignal(Player.SignalName.Notice,
                        $"+{gained} {ItemDb.NameOf(id)}");
            }

            if (leftover > 0)
            {
                PlayerRef.EmitSignal(Player.SignalName.Notice, "Inventory full");
            }
        }
    }

    private void OnMobHurt(Mob mob, DamageInfo info)
    {
        if (_rng.Randf() > 0.65f)
        {
            return;
        }

        string sound = mob switch
        {
            Briarback => "mobgrowl",
            CaveStalker => "mobchitter",
            CrystalWisp => "mobcrystal",
            Dreadwing => "mobdread",
            Sheep => "baa",
            _ => "mobhurt",
        };
        SoundRef?.Play(sound, mob.GlobalPosition + new Vector3(0, 1.0f, 0),
                1.0f, -4.0f);
        if (mob.IsPassive)
        {
            foreach (Mob herdMate in _passive)
            {
                if (herdMate != mob && herdMate.GlobalPosition.DistanceSquaredTo(
                        mob.GlobalPosition) < 10.0f * 10.0f)
                {
                    herdMate.SetAlerted(5.0f, info.Source);
                }
            }
        }

        AlertHostiles(mob.GlobalPosition, 10.0f, 6.0f);
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
            case "love":
                Puff(pos, new Color(1.0f, 0.45f, 0.68f));
                SoundRef?.Play("mobgrowl", pos, 1.35f, -10.0f);
                break;
        }
    }

    public void Puff(Vector3 pos, Color color) => FxRef?.Puff(pos, color);

    public void Lunge(Mob mob) =>
            SoundRef?.Play("dig", mob.GlobalPosition + new Vector3(0, 1.2f, 0),
                    0.55f, -14.0f);
}
