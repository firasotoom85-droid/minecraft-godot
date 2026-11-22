namespace Minecraft;

/// <summary>
/// Version 11 preserves V10 terrain, structures, biome canopies and ordinary
/// decorators, then adds deterministic emergent reeds on shallow sand and
/// grass shorelines.
/// The shoreline stamp writes only into air above the existing surface; V1-V10
/// generation remains frozen for saved worlds.
/// </summary>
public static class WorldGenV11
{
    public const int Version = 11;

    public static bool IsShallowWaterNeighbor(int surfaceHeight, int seaLevel) =>
            surfaceHeight < seaLevel && surfaceHeight >= seaLevel - 4;

    public static bool IsShorelineColumn(int surfaceHeight, int seaLevel, Biome biome,
            BlockId ground, bool adjacentToShallowWater) =>
            surfaceHeight >= seaLevel && surfaceHeight <= seaLevel + 2
            && biome is not (Biome.Desert or Biome.Snow or Biome.Ocean)
            && (ground is BlockId.Grass or BlockId.Sand) && adjacentToShallowWater;

    public static bool ShouldPlaceReed(uint coordinateHash, Biome biome) =>
            coordinateHash % (biome == Biome.Swamp ? 3u : 9u) == 0u;
}
