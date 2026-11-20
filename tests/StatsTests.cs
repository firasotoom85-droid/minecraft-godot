using Godot;

namespace Minecraft;

public static class StatsTests
{
    public static void Run()
    {
        PlayerStats stats = new();
        TestAssert.Equal(PlayerStats.MaxHealth, stats.Health, "full health at start");
        TestAssert.Check(stats.ApplyDamage(5.0f, GameMode.Survival), "survival damage lands");
        TestAssert.Equal(15.0f, stats.Health, "damage subtracts health");
        TestAssert.Check(!stats.ApplyDamage(5.0f, GameMode.Creative), "creative refuses damage");
        TestAssert.Equal(15.0f, stats.Health, "creative damage is a no-op");

        int deaths = 0;
        stats.Died += () => deaths++;
        TestAssert.Check(stats.ApplyDamage(20.0f, GameMode.Survival), "lethal damage lands");
        TestAssert.Equal(0.0f, stats.Health, "health floors at zero");
        TestAssert.Check(stats.Dead, "lethal damage marks the player dead");
        TestAssert.Equal(1, deaths, "death event fires once");

        stats.ResetForRespawn();
        TestAssert.Check(!stats.Dead, "respawn clears dead");
        TestAssert.Equal(PlayerStats.MaxHealth, stats.Health, "respawn restores health");
        stats.InvulnUntil = 0.0;
        TestAssert.Check(stats.ApplyDamage(2.0f, GameMode.Survival), "spawn protection can be cleared");

        PlayerStats loaded = new();
        loaded.Load(0.0f, 0.0f);
        TestAssert.Equal(PlayerStats.MaxHealth, loaded.Health, "unset saved health means full");
        loaded.SetHunger(10.0f);
        loaded.AddHunger(5.0f);
        TestAssert.Equal(15.0f, loaded.Hunger, "hunger adds and clamps");

        PlayerStats emptyHunger = new();
        emptyHunger.Load(PlayerStats.MaxHealth, 0.0f);
        TestAssert.Equal(0.0f, emptyHunger.Hunger, "a saved empty hunger bar stays empty");
        PlayerStats legacyHunger = new();
        legacyHunger.Load(PlayerStats.MaxHealth, -1.0f);
        TestAssert.Equal(PlayerStats.MaxHunger, legacyHunger.Hunger,
                "a missing legacy hunger field defaults to full");

        PlayerStats drowning = new();
        drowning.UpdateBreath(11.0, true, GameMode.Survival);
        TestAssert.Equal(0.0f, drowning.Breath, "breath drains underwater");
        TestAssert.Equal(PlayerStats.MaxHealth - 2.0f, drowning.Health, "drowning damages after breath runs out");
        drowning.UpdateBreath(2.0, false, GameMode.Survival);
        TestAssert.Check(drowning.Breath > 0.0f, "breath recovers above water");

        PlayerStats interruptedDive = new();
        for (int i = 0; i < 108; i++)
        {
            interruptedDive.UpdateBreath(0.1, true, GameMode.Survival);
        }

        TestAssert.Equal(PlayerStats.MaxHealth, interruptedDive.Health,
                "partial drowning time does not deal damage before a full second at zero breath");
        interruptedDive.UpdateBreath(0.1, false, GameMode.Survival);
        for (int i = 0; i < 6; i++)
        {
            interruptedDive.UpdateBreath(0.1, true, GameMode.Survival);
        }

        TestAssert.Equal(PlayerStats.MaxHealth, interruptedDive.Health,
                "surfacing resets the partial drowning timer before a later dive");
    }
}
