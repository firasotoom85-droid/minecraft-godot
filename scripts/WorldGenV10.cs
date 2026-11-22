namespace Minecraft;

/// <summary>
/// Version 10 keeps V9 terrain, structure placement and deterministic
/// weathering, and adds biome-specific canopy blocks to the existing tree
/// stamp. Tree positions, trunk heights and leaf volumes are unchanged; only
/// the leaf material follows the species already selected by V8. Keeping this
/// behind a new version leaves V1-V9 byte-identical and keeps saved worlds on
/// their original generator.
/// </summary>
public static class WorldGenV10
{
    public const int Version = 10;
}
