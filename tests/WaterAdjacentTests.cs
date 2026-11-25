using System;
using Godot;

namespace Minecraft;

/// <summary>Water-contact material tags, shoreline reeds and far-LOD continuity.</summary>
public static class WaterAdjacentTests
{
    public static void Run(Node host)
    {
        BlockDb.EnsureTables();
        CheckContentAndAtlas();
        CheckWetTagsAndShaders();
        CheckShorelineRules();
        CheckV11Delta(host);
        CheckWetMesherPaths(host);
    }

    private static void CheckContentAndAtlas()
    {
        TestAssert.Equal((byte)59, (byte)BlockId.RiverReeds,
                "river reeds append after the V10 block range");
        TestAssert.Equal(BlockId.RiverReeds, ItemDb.BlockFor(ItemId.RiverReeds)!.Value,
                "river reed item maps back to its new block ID");
        TestAssert.Equal(ItemId.RiverReeds, ItemDb.BlockItem(BlockId.RiverReeds),
                "river reed block has a placeable item mapping");
        TestAssert.Equal(59, BlockDb.IconTile(BlockId.RiverReeds),
                "river reeds use the appended atlas tile for their icon");
        for (int face = 0; face < 6; face++)
            TestAssert.Equal(59, BlockDb.FaceTile(BlockId.RiverReeds, face),
                    $"river reeds face {face} keeps the original cutout tile");
        TestAssert.Check(ItemDb.Icon(ItemId.RiverReeds) is AtlasTexture,
                "held reed icon reuses the existing atlas texture path");
        TestAssert.Check(BlockDb.IsCross(BlockId.RiverReeds),
                "river reeds reuse the existing double-sided cross-plant mesh");
        TestAssert.Check(!BlockDb.IsSolid(BlockId.RiverReeds)
                && BlockDb.IsTransparent(BlockId.RiverReeds),
                "river reeds remain non-colliding cutout vegetation");
        TestAssert.Equal(SurfaceFamily.Foliage, MaterialResponse.ForItem(ItemId.RiverReeds),
                "held river reeds use the shared foliage response");
        TestAssert.Check(BlockDb.IsWettable(BlockId.Dirt)
                && BlockDb.IsWettable(BlockId.Grass)
                && BlockDb.IsWettable(BlockId.Sand)
                && BlockDb.IsWettable(BlockId.Gravel)
                && BlockDb.IsWettable(BlockId.Farmland),
                "soil, sand, river gravel and farmland accept wet surface shading");
        TestAssert.Check(!BlockDb.IsWettable(BlockId.Stone)
                && !BlockDb.IsWettable(BlockId.Water)
                && !BlockDb.IsWettable(BlockId.Leaves),
                "wet shading does not leak onto unrelated material families");

        Image atlas = BlockDb.AtlasTexture().GetImage();
        TestAssert.Equal(512, atlas.GetWidth(), "shoreline art keeps the 512px atlas width");
        TestAssert.Equal(512, atlas.GetHeight(), "shoreline art keeps the 512px atlas height");
        int tileX = 59 % BlockDb.AtlasCols * BlockDb.TilePx;
        int tileY = 59 / BlockDb.AtlasCols * BlockDb.TilePx;
        int opaque = 0;
        int transparent = 0;
        for (int y = 0; y < BlockDb.TilePx; y++)
            for (int x = 0; x < BlockDb.TilePx; x++)
            {
                if (atlas.GetPixel(tileX + x, tileY + y).A > 0.5f) opaque++;
                else transparent++;
            }
        TestAssert.Check(opaque >= 90 && transparent >= 500,
                "original reed tile has a legible stalk silhouette with clean cutout gaps");
        TestAssert.Check(BlockDb.TileArrayTexture().GetWidth() == BlockDb.TilePx,
                "new tile remains within the existing 256-layer texture-array layout");
    }

    private static void CheckWetTagsAndShaders()
    {
        TestAssert.Equal((float)SurfaceFamily.Soil,
                MaterialResponse.VertexTag(SurfaceFamily.Soil, false),
                "dry material tags remain byte-compatible with existing vertex data");
        TestAssert.Equal((float)SurfaceFamily.Soil + 8.0f,
                MaterialResponse.VertexTag(SurfaceFamily.Soil, true),
                "wet soil uses the unused material-tag range");
        TestAssert.Equal((float)SurfaceFamily.Foliage + 8.0f,
                MaterialResponse.VertexTag(SurfaceFamily.Foliage, true),
                "wet shoreline foliage preserves its base response family");
        float bouncedWetTag = MaterialResponse.VertexTag(
                SurfaceFamily.Foliage, true, 0.75f);
        TestAssert.Equal((float)SurfaceFamily.Foliage + 8.0f,
                Mathf.Floor(bouncedWetTag + 0.001f),
                "fractional cave spill does not change a wet foliage material tag");
        TestAssert.Equal(0.75f, (bouncedWetTag
                - Mathf.Floor(bouncedWetTag + 0.001f)) * 16.0f,
                "wet foliage retains its four-bit cave-spill payload");

        string atlas = BlockDb.MakeOpaqueMaterial().Shader?.Code ?? string.Empty;
        string array = BlockDb.MakeArrayOpaqueMaterial().Shader?.Code ?? string.Empty;
        foreach ((string name, string shader) in new[] { ("atlas", atlas), ("greedy array", array) })
        {
            TestAssert.Check(shader.Contains("float encoded_family = floor(COLOR.a + 0.001)", StringComparison.Ordinal),
                    $"{name} shader decodes the compact wet material tag");
            TestAssert.Check(shader.Contains("mod(encoded_family, 8.0)", StringComparison.Ordinal)
                    && shader.Contains("step(7.5, encoded_family)", StringComparison.Ordinal),
                    $"{name} shader separates response family from wetness");
            TestAssert.Check(shader.Contains("vec3(0.80, 0.86, 0.88)", StringComparison.Ordinal),
                    $"{name} shader uses the same restrained cool wet tint");
        }
    }

    private static void CheckShorelineRules()
    {
        TestAssert.Check(WorldGenV11.IsShallowWaterNeighbor(37, 40),
                "water neighbour accepts shallow floor within four blocks of sea level");
        TestAssert.Check(!WorldGenV11.IsShallowWaterNeighbor(35, 40)
                && !WorldGenV11.IsShallowWaterNeighbor(40, 40),
                "shallow-water check excludes deep and dry columns");
        TestAssert.Check(WorldGenV11.IsShorelineColumn(40, 40, Biome.Beach,
                        BlockId.Sand, true),
                "V11 accepts sandy waterline columns beside shallow water");
        TestAssert.Check(WorldGenV11.IsShorelineColumn(42, 40, Biome.Beach,
                        BlockId.Sand, true),
                "V11 accepts the upper beach band while it remains adjacent to shallow water");
        TestAssert.Check(!WorldGenV11.IsShorelineColumn(43, 40, Biome.Swamp,
                        BlockId.Grass, true)
                && !WorldGenV11.IsShorelineColumn(41, 40, Biome.Desert,
                        BlockId.Sand, true)
                && !WorldGenV11.IsShorelineColumn(41, 40, Biome.Plains,
                        BlockId.Grass, false),
                "V11 excludes high banks, deserts and locations without adjacent water");
        TestAssert.Check(WorldGenV11.ShouldPlaceReed(0u, Biome.Swamp)
                && !WorldGenV11.ShouldPlaceReed(1u, Biome.Swamp),
                "swamp reed frequency is deterministic");
        TestAssert.Check(WorldGenV11.ShouldPlaceReed(0u, Biome.Plains)
                && !WorldGenV11.ShouldPlaceReed(1u, Biome.Plains),
                "river-bank reed frequency is deterministic");

        TestAssert.Check(FarLod.IsWetShoreCell(false, 41, 40, 40, 42, 42, 42),
                "far LOD flags a low land cell touching a water corner");
        TestAssert.Check(!FarLod.IsWetShoreCell(true, 40, 40, 39, 40, 41, 40)
                && !FarLod.IsWetShoreCell(false, 48, 40, 39, 48, 48, 48)
                && !FarLod.IsWetShoreCell(false, 45, 40, 41, 42, 43, 44),
                "far LOD leaves water, elevated land and dry terrain unchanged");
    }

    private static void CheckV11Delta(Node host)
    {
        const int seed = 1337;
        string v10Dir = $"user://tests/water-adjacent-v10-{Time.GetTicksUsec()}";
        string v11Dir = $"user://tests/water-adjacent-v11-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(v10Dir);
        DirAccess.MakeDirRecursiveAbsolute(v11Dir);
        WorldStore.WriteManifest(v10Dir, new WorldManifest(
                "V10 shoreline baseline", seed, WorldGenV10.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        WorldStore.WriteManifest(v11Dir, new WorldManifest(
                "V11 shoreline delta", seed, WorldGenV11.Version,
                WorldStore.DeriveNoiseSeeds(seed), 0, 0, "survival", "normal"));
        VoxelWorld? v10 = null;
        VoxelWorld? v11 = null;
        try
        {
            GameSession.Instance.SelectWorld(v10Dir);
            v10 = new VoxelWorld();
            host.AddChild(v10);
            GameSession.Instance.SelectWorld(v11Dir);
            v11 = new VoxelWorld();
            host.AddChild(v11);

            bool foundReeds = false;
            int comparedChunks = 0;
            // Scan a fixed region around the existing seed-1337 river valley.
            for (int cz = -30; cz <= -18 && !foundReeds; cz++)
                for (int cx = 26; cx <= 38 && !foundReeds; cx++)
                {
                    Vector2I chunk = new(cx, cz);
                    byte[] oldData = v10.ChunkData(chunk);
                    byte[] newData = v11.ChunkData(chunk);
                    TestAssert.Equal(oldData.Length, newData.Length,
                            "V10 and V11 retain the same chunk storage shape");
                    int reedsInChunk = 0;
                    bool onlyShoreReedsChanged = true;
                    for (int i = 0; i < oldData.Length; i++)
                    {
                        if (oldData[i] == newData[i]) continue;
                        if ((BlockId)newData[i] == BlockId.RiverReeds
                                && (BlockId)oldData[i] == BlockId.Air)
                        {
                            reedsInChunk++;
                        }
                        else
                        {
                            onlyShoreReedsChanged = false;
                            break;
                        }
                    }

                    TestAssert.Check(onlyShoreReedsChanged,
                            $"V11 chunk {chunk} changes only V10 air cells into shoreline reeds");
                    comparedChunks++;
                    foundReeds = reedsInChunk > 0;
                }

            TestAssert.Check(foundReeds,
                    $"V11 adds deterministically placed shoreline reeds within {comparedChunks} river-valley chunks");
            TestAssert.Equal(WorldGenV10.Version, v10.WorldGenVersion,
                    "saved V10 world remains pinned to the original generator");
            TestAssert.Equal(WorldGenV11.Version, v11.WorldGenVersion,
                    "new V11 world enables the shoreline-only delta");
        }
        finally
        {
            if (v10 != null)
            {
                host.RemoveChild(v10);
                v10.QueueFree();
            }
            if (v11 != null)
            {
                host.RemoveChild(v11);
                v11.QueueFree();
            }
            RemoveWorldDirectory(v10Dir);
            RemoveWorldDirectory(v11Dir);
        }
    }

    private static void CheckWetMesherPaths(Node host)
    {
        string dir = $"user://tests/water-adjacent-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        WorldStore.WriteManifest(dir, new WorldManifest(
                "Water adjacent materials", 1337, WorldGenV11.Version,
                WorldStore.DeriveNoiseSeeds(1337), 0, 0, "survival", "normal"));
        VoxelWorld? world = null;
        bool oldGreedy = Mesher.Greedy;
        try
        {
            GameSession.Instance.SelectWorld(dir);
            world = new VoxelWorld { BuildCollision = false };
            host.AddChild(world);
            world.BuildPreview(Vector3.Zero, 1);
            TestAssert.Equal(WorldGenV11.Version, world.WorldGenVersion,
                    "shoreline mesh fixture remains on generator V11");
            TestAssert.Check(world.SetBlock(4, 120, 4, BlockId.Gravel),
                    "wet-gravel fixture uses the world mutation API");
            TestAssert.Check(world.SetBlock(4, 121, 4, BlockId.Water),
                    "water fixture uses the world mutation API");
            TestAssert.Check(world.SetBlock(8, 120, 8, BlockId.RiverReeds),
                    "reed fixture uses the world mutation API");
            TestAssert.Check(world.SetBlock(9, 119, 8, BlockId.Water),
                    "reed shore water at root level uses the world mutation API");
            TestAssert.Check(world.SetBlock(12, 120, 12, BlockId.FlowerRed),
                    "dry foliage control uses the world mutation API");
            TestAssert.Check(world.SetBlock(14, 120, 12, BlockId.Gravel),
                    "dry gravel control uses the world mutation API");

            foreach (bool greedy in new[] { false, true })
            {
                Mesher.Greedy = greedy;
                MeshPayload payload = Mesher.Build(world, Vector2I.Zero);
                try
                {
                    TestAssert.Check(HasTag(payload.Opaque, MaterialResponse.VertexTag(
                                    SurfaceFamily.Soil, true)),
                            $"{(greedy ? "greedy" : "per-face")} mesher marks water-facing gravel as wet");
                    TestAssert.Check(HasTag(payload.Opaque, MaterialResponse.VertexTag(
                                    SurfaceFamily.Foliage, true)),
                            $"{(greedy ? "greedy" : "per-face")} mesher marks water-adjacent reeds as wet");
                    TestAssert.Check(HasTag(payload.Opaque, (float)SurfaceFamily.Foliage),
                            $"{(greedy ? "greedy" : "per-face")} mesher retains dry foliage tags");
                    TestAssert.Check(HasTag(payload.Opaque, (float)SurfaceFamily.Soil),
                            $"{(greedy ? "greedy" : "per-face")} mesher retains dry gravel tags");
                }
                finally
                {
                    PayloadPool.Release(payload);
                }
            }
        }
        finally
        {
            Mesher.Greedy = oldGreedy;
            if (world != null)
            {
                host.RemoveChild(world);
                world.Free();
            }
            DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
            DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
            DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
            DirAccess.RemoveAbsolute(dir);
        }
    }

    private static void RemoveWorldDirectory(string dir)
    {
        DirAccess.RemoveAbsolute(WorldStore.ManifestPath(dir));
        DirAccess.RemoveAbsolute(WorldStore.WorldSavePath(dir));
        DirAccess.RemoveAbsolute(WorldStore.PlayerSavePath(dir));
        DirAccess.RemoveAbsolute(dir);
    }

    private static bool HasTag(SurfaceData surface, float tag)
    {
        foreach (Color color in surface.Color)
        {
            if (Mathf.IsEqualApprox(Mathf.Floor(color.A + 0.001f), tag)) return true;
        }
        return false;
    }
}
