using System;

namespace Minecraft;

/// <summary>
/// Version 13 world-generation conflict rules. V1–V12 remain byte-identical;
/// new worlds reject campfire sites that would overlap a surface structure.
/// </summary>
public static class WorldGenV13
{
    public const int Version = 13;
    public const int StructureClearance = 15;

    public static bool CampfireConflictsWithStructure(int campfireX, int campfireZ,
            int structureX, int structureZ) =>
            Math.Abs((long)campfireX - structureX) <= StructureClearance
            && Math.Abs((long)campfireZ - structureZ) <= StructureClearance;
}
