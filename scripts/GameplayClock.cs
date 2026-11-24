using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Monotonic gameplay time that excludes intervals spent in the pause menu.
/// Real-time UI and input gestures should continue using the engine clock.
/// </summary>
public static class GameplayClock
{
    private static bool _paused;
    private static double _pauseStartedAt;
    private static double _pausedDuration;

    private static double RealNow => Time.GetTicksMsec() / 1000.0;

    public static double Now => CalculateNow(RealNow, _pausedDuration, _paused, _pauseStartedAt);

    public static void Pause()
    {
        if (_paused)
            return;

        _pauseStartedAt = RealNow;
        _paused = true;
    }

    public static void Resume()
    {
        if (!_paused)
            return;

        _pausedDuration += Math.Max(0.0, RealNow - _pauseStartedAt);
        _paused = false;
    }

    internal static double CalculateNow(
            double realNow, double pausedDuration, bool paused, double pauseStartedAt)
    {
        double elapsedPause = paused ? Math.Max(0.0, realNow - pauseStartedAt) : 0.0;
        return realNow - Math.Max(0.0, pausedDuration) - elapsedPause;
    }
}
