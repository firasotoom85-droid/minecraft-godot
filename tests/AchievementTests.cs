using System.Collections.Generic;

namespace Minecraft;

public static class AchievementTests
{
    public static void Run()
    {
        HashSet<string> ids = [];
        TestAssert.Check(AchievementDb.All.Length >= 6, "achievement list is populated");
        foreach (Achievement achievement in AchievementDb.All)
        {
            TestAssert.Check(!string.IsNullOrWhiteSpace(achievement.Id), "achievement id is non-empty");
            TestAssert.Check(ids.Add(achievement.Id), $"achievement id {achievement.Id} is unique");
            TestAssert.Check(!string.IsNullOrWhiteSpace(achievement.Name), $"achievement {achievement.Id} has a name");
            TestAssert.Check(!string.IsNullOrWhiteSpace(achievement.Hint), $"achievement {achievement.Id} has a hint");
        }
    }
}
