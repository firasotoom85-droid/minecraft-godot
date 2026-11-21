using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

public enum VillageTheme { Plains, Forest, Snow, Desert }
public enum VillagerProfession { Farmer, Merchant, Blacksmith, Librarian, Guard }

public readonly record struct VillageInfo(
        VillageTheme Theme, int X, int Y, int Z, int CellX, int CellZ, uint Seed)
{
    public string Id => $"village:{CellX}:{CellZ}";
}

public readonly record struct VillagerSpawnInfo(
        string Id, string Name, VillagerProfession Profession, Vector3 Home, Vector3 Work);

public sealed record VillagerSave(
        string Id, string Name, int Profession,
        float[] Position, float[] Home, float[] Work,
        float Health = 20.0f, int TradesCompleted = 0);

public sealed record QuestProgressSave(
        int Stage, int Progress, bool Completed = false, bool Claimed = false);

public static class VillageGenerator
{
    private static readonly string[] FirstNames =
    [
        "Alden", "Brina", "Corin", "Dara", "Eamon", "Fenna", "Galen", "Hana",
        "Ivo", "Junia", "Kael", "Lina", "Marek", "Nessa", "Orin", "Petra",
        "Quinn", "Rhea", "Soren", "Talia", "Ulric", "Vera", "Wren", "Yara",
    ];
    private static readonly string[] FamilyNames =
    [
        "Ashford", "Briar", "Cedar", "Dunewell", "Ember", "Fielding",
        "Greenvale", "Hearth", "Ironwood", "Juniper", "Kestrel", "Meadow",
    ];

    public static VillageTheme? ThemeForBiome(Biome biome) => biome switch
    {
        Biome.Plains or Biome.FlowerPlains or Biome.Meadow => VillageTheme.Plains,
        Biome.DenseForest or Biome.Birch => VillageTheme.Forest,
        Biome.Snow or Biome.Taiga => VillageTheme.Snow,
        Biome.Desert => VillageTheme.Desert,
        _ => null,
    };

    public static string ResidentName(uint seed, int index)
    {
        uint mixed = Mix(seed ^ unchecked((uint)index * 0x9E3779B9u));
        return $"{FirstNames[(int)(mixed % (uint)FirstNames.Length)]} "
                + FamilyNames[(int)((mixed >> 16) % (uint)FamilyNames.Length)];
    }

    public static VillagerSpawnInfo[] Roster(VillageInfo village)
    {
        (VillagerProfession Profession, int X, int Z, int WorkX, int WorkZ)[] residents =
        [
            (VillagerProfession.Farmer, 9, -9, 17, 1),
            (VillagerProfession.Blacksmith, -9, 9, -7, 7),
            (VillagerProfession.Librarian, 9, 9, 11, 7),
            (VillagerProfession.Merchant, -9, -9, 0, 0),
            (VillagerProfession.Guard, 0, -16, 0, -16),
        ];
        VillagerSpawnInfo[] roster = new VillagerSpawnInfo[residents.Length];
        for (int i = 0; i < residents.Length; i++)
        {
            var entry = residents[i];
            Vector3 home = new(village.X + entry.X + 0.5f, village.Y + 1.0f,
                    village.Z + entry.Z + 0.5f);
            Vector3 work = new(village.X + entry.WorkX + 0.5f, village.Y + 1.0f,
                    village.Z + entry.WorkZ + 0.5f);
            roster[i] = new VillagerSpawnInfo(village.Id + $"/resident-{i}",
                    ResidentName(village.Seed, i), entry.Profession, home, work);
        }
        return roster;
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}

public static class VillagerSchedule
{
    public enum Activity { Home, Work, Gathering }

    public static Activity ActivityAt(double timeOfDay)
    {
        double time = (timeOfDay % 1.0 + 1.0) % 1.0;
        if (time < 0.25 || time >= 0.73) return Activity.Home;
        return time < 0.56 ? Activity.Work : Activity.Gathering;
    }

    public static Vector3 DestinationAt(double timeOfDay, Vector3 home, Vector3 work, Vector3 plaza) =>
            ActivityAt(timeOfDay) switch
            {
                Activity.Home => home,
                Activity.Work => work,
                _ => plaza,
            };
}

public sealed record VillageTrade(string Label, ItemId Give, int GiveCount, ItemId Get, int GetCount);

public static class VillageTradeCatalog
{
    private static readonly IReadOnlyDictionary<VillagerProfession, VillageTrade[]> Trades =
            new Dictionary<VillagerProfession, VillageTrade[]>
            {
                [VillagerProfession.Farmer] =
                [
                    new("Sell 8 Wheat → 1 Gold Ingot", ItemId.Wheat, 8, ItemId.GoldIngot, 1),
                    new("Sell 4 Bread → 1 Gold Ingot", ItemId.Bread, 4, ItemId.GoldIngot, 1),
                    new("Buy 8 Seeds ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Seeds, 8),
                ],
                [VillagerProfession.Merchant] =
                [
                    new("Sell 16 Cobblestone → 1 Gold Ingot", ItemId.Cobblestone, 16, ItemId.GoldIngot, 1),
                    new("Buy 4 Glass ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Glass, 4),
                    new("Buy 8 Wool ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Wool, 8),
                ],
                [VillagerProfession.Blacksmith] =
                [
                    new("Sell 4 Raw Iron → 1 Gold Ingot", ItemId.RawIron, 4, ItemId.GoldIngot, 1),
                    new("Sell 2 Iron Ingots → 1 Gold Ingot", ItemId.IronIngot, 2, ItemId.GoldIngot, 1),
                    new("Buy an Iron Pickaxe ← 4 Gold Ingots", ItemId.GoldIngot, 4, ItemId.IronPick, 1),
                ],
                [VillagerProfession.Librarian] =
                [
                    new("Sell 2 Lapis Lazuli → 1 Gold Ingot", ItemId.LapisLazuli, 2, ItemId.GoldIngot, 1),
                    new("Buy 4 Lapis Lazuli ← 2 Gold Ingots", ItemId.GoldIngot, 2, ItemId.LapisLazuli, 4),
                    new("Buy an Enchanting Table ← 8 Gold Ingots", ItemId.GoldIngot, 8, ItemId.EnchantingTable, 1),
                ],
                [VillagerProfession.Guard] =
                [
                    new("Sell 16 Arrows → 1 Gold Ingot", ItemId.Arrow, 16, ItemId.GoldIngot, 1),
                    new("Buy 16 Arrows ← 1 Gold Ingot", ItemId.GoldIngot, 1, ItemId.Arrow, 16),
                    new("Sell 4 Iron Ingots → 2 Gold Ingots", ItemId.IronIngot, 4, ItemId.GoldIngot, 2),
                ],
            };

    public static IReadOnlyList<VillageTrade> For(VillagerProfession profession) =>
            Trades.TryGetValue(profession, out VillageTrade[]? offers)
                    ? offers : Array.Empty<VillageTrade>();
}
