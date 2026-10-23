namespace Minecraft;

/// <summary>One persistent achievement. Ids are saved in world flags.</summary>
public sealed record Achievement(string Id, string Name, string Hint);

/// <summary>Curated goals shown in the journal and unlocked by gameplay events.</summary>
public static class AchievementDb
{
    public static readonly Achievement[] All =
    [
        new("first_block", "First Cut", "Break a block."),
        new("first_torch", "Light in the Dark", "Place a torch."),
        new("first_iron", "Iron Age", "Smelt an iron ingot."),
        new("homestead", "Homestead", "Place a bed, a chest and a furnace."),
        new("first_night", "Night Owl", "Survive until dawn."),
        new("deep", "Deep Down", "Reach y = 20 or lower."),
    ];
}
