using System;

namespace Minecraft;

/// <summary>Physics collision layers, typed instead of magic numbers.</summary>
[Flags]
public enum PhysicsLayer : uint
{
    None = 0,
    Terrain = 1,
    Player = 2,
    Mobs = 4,
}
