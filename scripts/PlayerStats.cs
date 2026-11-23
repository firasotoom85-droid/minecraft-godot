using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Player survival numbers: health, hunger and breath, with the rules that
/// drive them (hunger drain, regeneration, starvation, drowning). A plain
/// class with C# events — the HUD and audio subscribe, the Player routes
/// incoming damage in. All tuning constants live here.
/// </summary>
public sealed class PlayerStats
{
    public const float MaxHealth = 20.0f;   // 10 hearts
    public const float MaxHunger = 20.0f;   // 10 drumsticks
    public const float MaxBreath = 10.0f;   // 10 bubbles

    public float Health { get; private set; } = MaxHealth;
    public float Hunger { get; private set; } = MaxHunger;
    public float Breath { get; private set; } = MaxBreath;
    public bool Dead { get; private set; }
    public int Experience { get; private set; }
    public int ExperienceLevel => LevelForExperience(Experience);

    /// <summary>Seconds (engine clock) before damage is accepted again.</summary>
    public double InvulnUntil;

    public event Action? Changed;        // any bar value moved (HUD repaint)
    public event Action<float>? Damaged; // amount, after a hit lands
    public event Action? Died;
    public event Action? Respawned;

    private double _hungerAccum;
    private double _regenAccum;
    private double _starveAccum;
    private double _drownAccum;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    public bool Invulnerable => Dead || Now < InvulnUntil;

    /// <summary>Routes one damage unit; false when refused (creative/dead/invulnerable).</summary>
    public bool ApplyDamage(float amount, GameMode mode)
    {
        if (amount <= 0.0f || Dead || mode == GameMode.Creative || Now < InvulnUntil)
        {
            return false;
        }

        Health = Mathf.Max(Health - amount, 0.0f);
        Damaged?.Invoke(amount);
        Changed?.Invoke();
        if (Health <= 0.0f)
        {
            Dead = true;
            Died?.Invoke();
        }

        return true;
    }

    public static int ExperienceAtLevel(int level)
    {
        level = Math.Max(level, 0);
        if (level <= 16) return level * level + 6 * level;
        if (level <= 31) return (int)(2.5 * level * level - 40.5 * level + 360);
        return (int)(4.5 * level * level - 162.5 * level + 2220);
    }

    private static int LevelForExperience(int experience)
    {
        int level = 0;
        while (level < 1000 && ExperienceAtLevel(level + 1) <= experience) level++;
        return level;
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0 || Dead) return;
        Experience = Math.Clamp(Experience + amount, 0, 1_000_000);
        Changed?.Invoke();
    }

    public bool TrySpendLevels(int levels)
    {
        if (levels <= 0) return true;
        int current = ExperienceLevel;
        if (current < levels) return false;
        Experience = Math.Max(Experience - (ExperienceAtLevel(current) - ExperienceAtLevel(current - levels)), 0);
        Changed?.Invoke();
        return true;
    }

    public void Heal(float amount)
    {
        if (Dead || amount <= 0.0f)
        {
            return;
        }

        float before = Health;
        Health = Mathf.Min(Health + amount, MaxHealth);
        if (Health != before)
        {
            Changed?.Invoke();
        }
    }

    public void AddHunger(float amount)
    {
        float before = Hunger;
        Hunger = Mathf.Clamp(Hunger + amount, 0.0f, MaxHunger);
        if (Hunger != before)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Direct set (dev console / tests).</summary>
    public void SetHunger(float value)
    {
        Hunger = Mathf.Clamp(value, 0.0f, MaxHunger);
        Changed?.Invoke();
    }

    /// <summary>Per-frame hunger, regeneration and starvation (survival only).</summary>
    public void Update(double delta, bool sprinting, bool moving, GameMode mode)
    {
        if (Dead || mode != GameMode.Survival)
        {
            return;
        }

        // Hunger drains with activity; peaceful keeps the larder full.
        if (Settings.Difficulty != Settings.DifficultyPeaceful)
        {
            double rate = sprinting ? 1.8 : moving ? 1.0 : 0.35;
            _hungerAccum += delta * rate;
            if (_hungerAccum >= 90.0)
            {
                _hungerAccum -= 90.0;
                Hunger = Mathf.Max(Hunger - 1.0f, 0.0f);
                Changed?.Invoke();
            }
        }

        // Well-fed players slowly mend, spending a little food per heart.
        if (Hunger >= 18.0f && Health < MaxHealth)
        {
            _regenAccum += delta;
            if (_regenAccum >= 3.5)
            {
                _regenAccum = 0.0;
                Health = Mathf.Min(Health + 1.0f, MaxHealth);
                Hunger = Mathf.Max(Hunger - 1.0f, 0.0f);
                Changed?.Invoke();
            }
        }
        else
        {
            _regenAccum = 0.0;
        }

        // Starvation: normal mode never kills, hard does.
        if (Hunger <= 0.0f && Settings.Difficulty != Settings.DifficultyPeaceful)
        {
            _starveAccum += delta;
            if (_starveAccum >= 4.0)
            {
                _starveAccum = 0.0;
                bool lethal = Settings.Difficulty == Settings.DifficultyHard;
                if (lethal || Health > 1.0f)
                {
                    ApplyDamage(1.0f, mode);
                }
            }
        }
        else
        {
            _starveAccum = 0.0;
        }
    }

    /// <summary>Per-frame breath / drowning (survival only).</summary>
    public void UpdateBreath(double delta, bool underwater, GameMode mode)
    {
        if (Dead || mode != GameMode.Survival)
        {
            Breath = MaxBreath;
            _drownAccum = 0.0;
            return;
        }

        if (underwater)
        {
            float before = Breath;
            double elapsed = System.Math.Max(delta, 0.0);
            Breath = Mathf.Max(Breath - (float)elapsed, 0.0f);
            if (Breath <= 0.0f)
            {
                _drownAccum += System.Math.Max(elapsed - before, 0.0);
                if (_drownAccum >= 1.0)
                {
                    _drownAccum = 0.0;
                    ApplyDamage(2.0f, mode);
                }
            }

            if (Mathf.CeilToInt(before) != Mathf.CeilToInt(Breath))
            {
                Changed?.Invoke();
            }
        }
        else
        {
            // Time spent above water must not carry into the next dive.
            _drownAccum = 0.0;
            if (Breath < MaxBreath)
            {
                float before = Breath;
                Breath = Mathf.Min(Breath + (float)delta * 4.0f, MaxBreath);
                if (Mathf.CeilToInt(before) != Mathf.CeilToInt(Breath))
                {
                    Changed?.Invoke();
                }
            }
        }
    }

    /// <summary>Fresh start: full bars, brief spawn protection. Inventory is untouched (D5).</summary>
    public void ResetForRespawn()
    {
        Health = MaxHealth;
        Hunger = MaxHunger;
        Breath = MaxBreath;
        Dead = false;
        InvulnUntil = Now + 2.5;
        _hungerAccum = _regenAccum = _starveAccum = _drownAccum = 0.0;
        Changed?.Invoke();
        Respawned?.Invoke();
    }

    /// <summary>Restore persisted values; a negative hunger sentinel means the field was absent in an old save.</summary>
    public void Load(float health, float hunger, int experience = 0, bool dead = false)
    {
        Health = dead ? 0.0f
                : health > 0.0f ? Mathf.Clamp(health, 1.0f, MaxHealth) : MaxHealth;
        Hunger = hunger >= 0.0f ? Mathf.Clamp(hunger, 0.0f, MaxHunger) : MaxHunger;
        Breath = MaxBreath;
        Dead = dead;
        Experience = Math.Clamp(experience, 0, 1_000_000);
        Changed?.Invoke();
    }
}

/// <summary>Per-player game mode; creative keeps the old sandbox feel.</summary>
public enum GameMode : byte
{
    Survival = 0,
    Creative = 1,
}
