using Godot;

namespace Minecraft;

public static class FirstPersonAnimationTests
{
    public static void Run()
    {
        CheckActionClock();
        CheckActionProfilesAreDistinctAndBounded();
        CheckMovementAndHeldStateLayers();
    }

    private static void CheckActionClock()
    {
        FirstPersonAnimationState state = new();
        TestAssert.Equal(FirstPersonAction.None, state.Action,
                "first-person action clock starts idle");
        state.Play(FirstPersonAction.Mine);
        TestAssert.Equal(FirstPersonAction.Mine, state.Action,
                "playing an action updates the active profile");
        TestAssert.Equal(0.0f, state.Progress,
                "new action starts at its first frame");

        state.Advance(0.08);
        float firstProgress = state.Progress;
        TestAssert.Check(firstProgress > 0.0f && firstProgress < 1.0f,
                "action clock advances through its profile");
        state.Advance(-1.0);
        TestAssert.Equal(firstProgress, state.Progress,
                "negative frame delta cannot rewind an action");
        state.Play(FirstPersonAction.Melee);
        TestAssert.Equal(FirstPersonAction.Melee, state.Action,
                "a new action can replace the current action");
        TestAssert.Equal(0.0f, state.Progress,
                "replaced action restarts from its anticipation pose");
        state.Advance(FirstPersonAnimation.DurationOf(FirstPersonAction.Melee) + 0.1);
        TestAssert.Equal(FirstPersonAction.None, state.Action,
                "action automatically returns to idle after its duration");
        TestAssert.Equal(1.0f, state.Progress,
                "idle action reports a completed progress value");
    }

    private static void CheckActionProfilesAreDistinctAndBounded()
    {
        FirstPersonPose idle = FirstPersonAnimation.Evaluate(
                FirstPersonAction.None, 1.0f, 0, 0, 0, 0, 0);
        FirstPersonPose mine = FirstPersonAnimation.Evaluate(
                FirstPersonAction.Mine, 0.43f, 0, 0, 0, 0, 0);
        FirstPersonPose melee = FirstPersonAnimation.Evaluate(
                FirstPersonAction.Melee, 0.42f, 0, 0, 0, 0, 0);
        FirstPersonPose place = FirstPersonAnimation.Evaluate(
                FirstPersonAction.Place, 0.32f, 0, 0, 0, 0, 0);
        FirstPersonPose eat = FirstPersonAnimation.Evaluate(
                FirstPersonAction.Eat, 0.5f, 0, 0, 0, 0, 0);
        FirstPersonPose bow = FirstPersonAnimation.Evaluate(
                FirstPersonAction.BowRelease, 0.42f, 0, 0, 0, 0, 0);
        FirstPersonPose shield = FirstPersonAnimation.Evaluate(
                FirstPersonAction.ShieldBlock, 0.25f, 0, 0, 0, 0, 0);
        FirstPersonPose landing = FirstPersonAnimation.Evaluate(
                FirstPersonAction.Landing, 0.5f, 0, 0, 0, 0, 0);

        TestAssert.Equal(Vector3.Zero, idle.ArmPosition,
                "idle arm position has no action offset");
        TestAssert.Equal(Vector3.Zero, idle.ArmRotation,
                "idle arm rotation has no action offset");
        TestAssert.Check(mine.ArmRotation.DistanceTo(melee.ArmRotation) > 0.2f,
                "mining and melee use visibly different arm rotations");
        TestAssert.Check(place.ItemPosition.DistanceTo(eat.ItemPosition) > 0.12f,
                "placing and eating use distinct held-item motion");
        TestAssert.Check(bow.ArmPosition.DistanceTo(mine.ArmPosition) > 0.02f,
                "bow release differs from a mining impact");
        TestAssert.Check(shield.ItemPosition.DistanceTo(place.ItemPosition) > 0.08f,
                "shield block differs from item placement");
        TestAssert.Check(landing.ArmPosition.DistanceTo(idle.ArmPosition) > 0.04f,
                "landing has a visible recovery dip");

        foreach (FirstPersonPose pose in new[] { mine, melee, place, eat, bow, shield, landing })
        {
            TestAssert.Check(pose.ArmPosition.Length() < 0.30f,
                    "arm positional offsets remain inside the camera-safe envelope");
            TestAssert.Check(pose.ItemPosition.Length() < 0.35f,
                    "held-item offsets remain inside the camera-safe envelope");
            TestAssert.Check(pose.ArmRotation.Length() < 1.2f,
                    "arm rotations remain inside the natural motion envelope");
            TestAssert.Check(pose.ItemScale >= 1.0f && pose.ItemScale <= 1.1f,
                    "held-item scale avoids distracting pop");
        }
    }

    private static void CheckMovementAndHeldStateLayers()
    {
        FirstPersonPose walking = FirstPersonAnimation.Evaluate(
                FirstPersonAction.None, 1.0f, 0.02f, 0, 0, 0, 0);
        FirstPersonPose idleBreathing = FirstPersonAnimation.Evaluate(
                FirstPersonAction.None, 1.0f, 0, 0.003f, 0, 0, 0);
        FirstPersonPose bowDraw = FirstPersonAnimation.Evaluate(
                FirstPersonAction.None, 1.0f, 0, 0, 1.0f, 0, 0);
        FirstPersonPose guard = FirstPersonAnimation.Evaluate(
                FirstPersonAction.None, 1.0f, 0, 0, 0, 1.0f, 0);
        FirstPersonPose climbing = FirstPersonAnimation.Evaluate(
                FirstPersonAction.None, 1.0f, 0, 0, 0, 0, 1.0f);
        FirstPersonPose combined = FirstPersonAnimation.Evaluate(
                FirstPersonAction.Melee, 0.4f, 0.02f, 0.002f, 0.8f, 0.7f, -0.5f);

        TestAssert.Check(walking.ArmPosition.LengthSquared() > 0.0f,
                "locomotion bob contributes to the viewmodel pose");
        TestAssert.Check(idleBreathing.ArmPosition.LengthSquared() > 0.0f,
                "idle breathing adds subtle arm movement");
        TestAssert.Check(bowDraw.ItemPosition.DistanceTo(Vector3.Zero) > 0.04f,
                "holding a bow adds a draw pose");
        TestAssert.Check(guard.ItemPosition.DistanceTo(Vector3.Zero) > 0.08f,
                "holding block with a shield raises the held item");
        TestAssert.Check(climbing.ArmRotation.LengthSquared() > 0.0f,
                "climbing contributes an alternating arm pose");
        TestAssert.Check(combined.ItemScale >= 1.0f && combined.ItemScale <= 1.1f,
                "action and held-state layers combine without unbounded scaling");
    }
}
