using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>Regression coverage for sky/block propagation and chunk seams.</summary>
public static class LightingTests
{
    public static void Run()
    {
        BlockDb.EnsureTables();
        TestCornerAverageChannels();
        TestSkyLightInterpolation();
        TestHostileSilhouetteResponse();
        TestWarmEmitterResponse();
        TestPaleSurfaceContrast();
        TestAssert.Check(BlockDb.LightEmission[(int)BlockId.Torch] >= 13, "torch has a strong nibble emission");
        TestAssert.Check(BlockDb.LightEmission[(int)BlockId.Lava] > 0
                && BlockDb.LightEmission[(int)BlockId.Lava] < BlockDb.LightEmission[(int)BlockId.Torch],
                "lava has a distinct lower emission value");
        TestAssert.Equal((byte)15, BlockDb.LightOpacity[(int)BlockId.Stone], "stone fully blocks voxel light");
        TestAssert.Equal((byte)0, BlockDb.LightOpacity[(int)BlockId.Glass], "glass transmits voxel light");
        TestAssert.Check(BlockDb.LightOpacity[(int)BlockId.Water] > 0
                && BlockDb.LightOpacity[(int)BlockId.Water] < 15, "water attenuates voxel light");

        TestBlockAttenuation();
        TestOverlappingWarmEmitters();
        TestOpaqueBlocking();
        TestTransparentTransmission();
        TestFoliageGapTransmission();
        TestShapeLightPassages();
        TestPartialShapeLighting();
        TestChunkBoundary();
        TestRemoval();
        TestIncrementalBlockUpdates();
        TestSunlightAndCaves();
        TestSkyBounceSeparation();
        TestGreedyMergePreservesBounce();
        TestMaterialTagChannels();
    }

    private static void TestCornerAverageChannels()
    {
        byte a = VoxelLighting.Pack(15, 0);
        byte b = VoxelLighting.Pack(7, 4);
        byte c = VoxelLighting.Pack(3, 8);
        byte d = VoxelLighting.Pack(1, 12);
        byte expected = VoxelLighting.Pack(7, 6);
        TestAssert.Equal(expected, VoxelLighting.AverageCorner(a, b, c, d),
                "explicit corner samples average sky and block nibbles independently");

        byte[] samples = [a, b, c, d];
        TestAssert.Equal(expected, VoxelLighting.AverageCorner(samples, 0, 1, 2),
                "array-offset and explicit-sample corner averaging stay equivalent");
    }

    private static void TestSkyLightInterpolation()
    {
        float midpoint = VoxelLighting.InterpolateSkyLight(
                0, 15, 0, 15, 0, 15, 0, 15, 0.5f, 0.25f, 0.75f);
        TestAssert.Equal(7.5f, midpoint,
                "camera sky exposure interpolates continuously between adjacent voxel centres");

        float verticalMidpoint = VoxelLighting.InterpolateSkyLight(
                0, 0, 15, 15, 0, 0, 15, 15, 0.2f, 0.5f, 0.8f);
        TestAssert.Equal(7.5f, verticalMidpoint,
                "trilinear skylight interpolation respects vertical gradients");

        float clamped = VoxelLighting.InterpolateSkyLight(
                15, 15, 15, 15, 15, 15, 15, 15, -2.0f, 3.0f, 0.5f);
        TestAssert.Equal(15.0f, clamped,
                "skylight interpolation clamps out-of-cell weights to a valid light level");

        VoxelWorld emptyWorld = new() { BuildCollision = false };
        Godot.Vector3 cameraPosition = new(0.5f, 4.5f, 0.5f);
        int initialChunks = emptyWorld.ChunkCount;
        bool hasCachedLight = emptyWorld.TrySampleCachedSkyLight(cameraPosition, out _);
        TestAssert.Check(!hasCachedLight,
                "camera exposure lookup reports a cache miss instead of generating world data");
        TestAssert.Equal(initialChunks, emptyWorld.ChunkCount,
                "camera exposure cache miss does not create or stream chunks");
        TestAssert.FreeAll(emptyWorld);
    }

    private static void TestHostileSilhouetteResponse()
    {
        StandardMaterial3D hostile = Mob.CreateBodyMaterial(hostile: true);
        StandardMaterial3D passive = Mob.CreateBodyMaterial(hostile: false);

        TestAssert.Check(hostile.RimEnabled,
                "hostile vertex-colour bodies enable a contour rim for silhouette separation");
        TestAssert.Equal(Mob.HostileRimStrength, hostile.Rim,
                "hostile rim strength stays at the deliberately restrained level");
        TestAssert.Equal(Mob.HostileRimTint, hostile.RimTint,
                "hostile rim blends moon/sun light with the creature's existing albedo");
        TestAssert.Check(!hostile.EmissionEnabled,
                "hostile silhouette readability does not use self-emission or create scene-wide fill");
        TestAssert.Check(!passive.RimEnabled,
                "passive creature materials do not receive the hostile-only contour treatment");
        TestAssert.Check(hostile.VertexColorUseAsAlbedo && passive.VertexColorUseAsAlbedo,
                "hostile and passive bodies retain the existing procedural vertex-colour pipeline");
    }

    private static void TestWarmEmitterResponse()
    {
        TestAssert.Equal(BlockDb.LightEmission[(int)BlockId.Torch], VoxelLighting.CampfireEmission,
                "campfires share the exact baked source strength used by torches");

        string waterShader = (ChunkMaterials.Water as ShaderMaterial)?.Shader?.Code ?? string.Empty;
        TestAssert.Check(!string.IsNullOrEmpty(waterShader),
                "water retains its dedicated production shader");
        foreach (string shader in BlockDb.WarmLightingShaderSources)
            CheckSharedWarmShader(shader, "opaque, array and glass");
        CheckSharedWarmShader(waterShader, "water");

        TestAssert.Check(!waterShader.Contains("shadowed_block", StringComparison.Ordinal)
                        && !waterShader.Contains("vec3(1.0, 0.68, 0.38)", StringComparison.Ordinal),
                "water no longer carries a separate, brighter block-light formula");

        TestAssert.Equal((byte)12, BlockDb.LightEmission[(int)BlockId.Lava],
                "lava's registered source remains twelve packed block-light levels");
        Color lavaSourceChannels = VoxelLighting.VertexColor(
                VoxelLighting.Pack(0, BlockDb.LightEmission[(int)BlockId.Lava]), 1.0f);
        float lavaSourceLevel = BlockDb.LightEmission[(int)BlockId.Lava]
                / (float)VoxelLighting.MaxLevel;
        TestAssert.Equal(lavaSourceLevel, lavaSourceChannels.G,
                "lava's packed low nibble is encoded unchanged in the shader block-light channel");
        float safeSourceLevel = Mathf.Max(lavaSourceLevel, 1.0f / VoxelLighting.MaxLevel);
        TestAssert.Equal(0.0f, Mathf.Clamp(0.0f / safeSourceLevel, 0.0f, 1.0f),
                "a zero packed block-light channel produces no lava emission");
        TestAssert.Equal(0.5f,
                Mathf.Clamp((lavaSourceLevel * 0.5f) / safeSourceLevel, 0.0f, 1.0f),
                "half of lava's source nibble produces half of its emissive output");
        TestAssert.Equal(1.0f,
                Mathf.Clamp(lavaSourceLevel / safeSourceLevel, 0.0f, 1.0f),
                "the registered lava source nibble reaches the preserved molten-core emission");

        ShaderMaterial lavaMaterial = ChunkMaterials.Lava;
        string lavaShader = lavaMaterial.Shader?.Code ?? string.Empty;
        TestAssert.Equal(lavaSourceLevel,
                lavaMaterial.GetShaderParameter("lava_source_level").AsSingle(),
                "lava shader normalization is derived from the registered block emission level");
        TestAssert.Check(lavaShader.Contains("float block = clamp(COLOR.g, 0.0, 1.0);",
                        StringComparison.Ordinal)
                        && lavaShader.Contains("block / max(lava_source_level", StringComparison.Ordinal)
                        && lavaShader.Contains("fract(COLOR.a) * 16.0", StringComparison.Ordinal)
                        && lavaShader.Contains("bounce * 0.22", StringComparison.Ordinal)
                        && lavaShader.Contains("EMISSION = tex.rgb * warm * lava_emission * 0.9;",
                                StringComparison.Ordinal)
                        && !lavaShader.Contains("warm * 0.9 +", StringComparison.Ordinal),
                "lava separates indirect sky fill while retaining packed block emission");

        Godot.Environment environment = WorldEnvironmentFactory.Build();
        TestAssert.Equal(Godot.Environment.ToneMapper.Filmic, environment.TonemapMode,
                "local warm emitter tuning retains the shared Filmic environment response");
        TestAssert.Equal(1.05f, environment.GlowHdrThreshold,
                "high-quality bloom is reserved for highlights instead of broadly lifting warm surfaces");
        TestAssert.Equal(1.0f, environment.AmbientLightEnergy,
                "warm block sources do not globally tint or dim neutral sky ambient");
    }

    private static void CheckSharedWarmShader(string shader, string label)
    {
        TestAssert.Check(shader.Contains(
                "uniform vec3 warm_block_light_color = vec3(1.0, 0.68, 0.42);",
                StringComparison.Ordinal),
                $"{label} uses the shared warm emitter hue");
        TestAssert.Check(shader.Contains("warm_block_response(sky, block)",
                StringComparison.Ordinal)
                && shader.Contains("warm_block_illumination(warm_response)", StringComparison.Ordinal)
                && shader.Contains("warm_block_emission(warm_response)", StringComparison.Ordinal),
                $"{label} shares one response for diffuse fill and residual emission");
        TestAssert.Check(shader.Split("exp(-block * 1.25)", StringSplitOptions.None).Length == 2
                && shader.Contains("response * 0.34;", StringComparison.Ordinal)
                && shader.Contains("response * 0.025;", StringComparison.Ordinal)
                && !shader.Contains("shadowed_block", StringComparison.Ordinal),
                $"{label} evaluates one bounded warm response per fragment");
        TestAssert.Check(shader.Contains(
                        "float bounce = clamp(fract(COLOR.a) * 16.0 * sky_light_energy, 0.0, 1.0);",
                        StringComparison.Ordinal)
                && shader.Contains("bounce * 0.22", StringComparison.Ordinal),
                $"{label} reads a separate bounded cool cave-bounce channel");
    }

    private static void TestPaleSurfaceContrast()
    {
        TestAssert.Equal(1.0f, MaterialResponse.PaleSurfaceWeight(Colors.White, SurfaceFamily.Soil),
                "snow-white soil receives full pale-surface response");
        TestAssert.Equal(1.0f, MaterialResponse.PaleSurfaceWeight(
                        new Color(0.86f, 0.81f, 0.61f), SurfaceFamily.Soil),
                "bright sand receives full pale-surface response");
        float paleStone = MaterialResponse.PaleSurfaceWeight(
                new Color(0.60f, 0.60f, 0.60f), SurfaceFamily.Stone);
        TestAssert.Check(paleStone > 0.5f && paleStone < 1.0f,
                "mid-value stone receives a soft partial response");
        TestAssert.Equal(0.0f, MaterialResponse.PaleSurfaceWeight(
                        new Color(0.24f, 0.26f, 0.28f), SurfaceFamily.Stone),
                "dark stone does not receive pale-surface contrast");
        TestAssert.Equal(0.0f, MaterialResponse.PaleSurfaceWeight(Colors.White, SurfaceFamily.Wood),
                "bright wood keeps its original directional response");
        TestAssert.Equal(0.0f, MaterialResponse.PaleSurfaceWeight(Colors.White, SurfaceFamily.Foliage),
                "bright foliage keeps its original directional response");

        TestAssert.Equal(0.70f, MaterialResponse.DirectionalSkyGain(0.0f, 1.0f, 1.0f),
                "turned-away pale surfaces receive stronger daylight shading");
        TestAssert.Equal(1.0f, MaterialResponse.DirectionalSkyGain(1.0f, 1.0f, 1.0f),
                "fully sun-facing pale surfaces keep their previous gain");
        TestAssert.Equal(0.88f, MaterialResponse.DirectionalSkyGain(0.0f, 0.0f, 1.0f),
                "zero-sun conditions retain existing sky fill");
        TestAssert.Equal(0.88f, MaterialResponse.DirectionalSkyGain(0.0f, 1.0f, 0.0f),
                "non-pale surfaces retain existing indirect sky gain");
        TestAssert.Equal(0.88f, MaterialResponse.DirectionalSkyGain(0.0f, 1.0f, 1.0f, 0.0f),
                "baseline mode restores the original directional sky gain");

        TestAssert.Equal(2.0f, MaterialResponse.VertexTag(SurfaceFamily.Soil, false),
                "dry soil retains its existing vertex tag");
        TestAssert.Equal(10.0f, MaterialResponse.VertexTag(SurfaceFamily.Soil, true),
                "wet soil retains its existing vertex tag");

        ShaderMaterial atlas = BlockDb.MakeOpaqueMaterial();
        ShaderMaterial array = BlockDb.MakeArrayOpaqueMaterial();
        foreach (ShaderMaterial material in new[] { atlas, array })
        {
            TestAssert.Equal(new Vector2(MaterialResponse.PaleSurfaceLuminanceMin,
                            MaterialResponse.PaleSurfaceLuminanceMax),
                    material.GetShaderParameter("pale_surface_luminance_range").AsVector2(),
                    "opaque renderers share the albedo threshold range");
            TestAssert.Equal(MaterialResponse.PaleSurfaceDirectionalContrast,
                    material.GetShaderParameter("pale_surface_contrast_strength").AsSingle(),
                    "opaque renderers share the default contrast strength");
            string shader = material.Shader?.Code ?? string.Empty;
            // The merged renderer feeds the pale weight into the directional
            // gain and passes the separated direct-sky channel, never the
            // combined sky that still carries lateral cave fill.
            TestAssert.Check(shader.Contains("pale_surface_luminance_range.x", StringComparison.Ordinal)
                            && shader.Contains("pale_surface_contrast_strength", StringComparison.Ordinal)
                            && shader.Contains("directional_sky_light(directional_response, direct_sky, COLOR.r, pale_surface)",
                                    StringComparison.Ordinal)
                            && shader.Contains("- pale_surface * pale_surface_contrast_strength",
                                    StringComparison.Ordinal),
                    "opaque shader applies pale weight only to directional sky gain");
        }
        string glassShader = BlockDb.MakeGlassMaterial().Shader?.Code ?? string.Empty;
        TestAssert.Check(!glassShader.Contains("pale_surface_contrast_strength", StringComparison.Ordinal),
                "glass remains outside opaque pale-surface response");
        string waterShader = (ChunkMaterials.Water as ShaderMaterial)?.Shader?.Code ?? string.Empty;
        TestAssert.Check(!waterShader.Contains("pale_surface_contrast_strength", StringComparison.Ordinal),
                "water remains outside opaque pale-surface response");

        ShaderMaterial sharedOpaque = ChunkMaterials.Opaque as ShaderMaterial
                ?? throw new InvalidOperationException("Active opaque material must remain shader-backed.");
        ChunkMaterials.SetPaleSurfaceContrast(0.0f);
        TestAssert.Equal(0.0f,
                sharedOpaque.GetShaderParameter("pale_surface_contrast_strength").AsSingle(),
                "baseline switch updates the shared opaque material");
        ChunkMaterials.SetPaleSurfaceContrast(MaterialResponse.PaleSurfaceDirectionalContrast);
        TestAssert.Equal(MaterialResponse.PaleSurfaceDirectionalContrast,
                sharedOpaque.GetShaderParameter("pale_surface_contrast_strength").AsSingle(),
                "contrast restore updates the shared opaque material");

        Color channels = VoxelLighting.VertexColor(VoxelLighting.Pack(11, 7), 0.75f);
        TestAssert.Equal(11.0f / VoxelLighting.MaxLevel, channels.R,
                "pale contrast leaves packed skylight vertex channel unchanged");
        TestAssert.Equal(7.0f / VoxelLighting.MaxLevel, channels.G,
                "pale contrast leaves packed block-light vertex channel unchanged");
        TestAssert.Equal(0.75f, channels.B,
                "pale contrast leaves AO/face-shade vertex channel unchanged");
    }

    private static void TestBlockAttenuation()
    {
        const int width = 8;
        const int height = 5;
        const int depth = 5;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] sources =
        [
            new(new Godot.Vector3I(1, 2, 2), BlockDb.LightEmission[(int)BlockId.Torch]),
        ];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Equal((byte)14, At(light, width, height, depth, 1, 2, 2), "torch source cell retains emission");
        TestAssert.Equal((byte)13, At(light, width, height, depth, 2, 2, 2), "block light attenuates one level per air voxel");
        TestAssert.Equal((byte)12, At(light, width, height, depth, 3, 2, 2), "block light continues attenuating with distance");
    }

    private static void TestOverlappingWarmEmitters()
    {
        const int width = 11;
        const int height = 3;
        const int depth = 3;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] first = [new(new Vector3I(2, 1, 1), VoxelLighting.CampfireEmission)];
        VoxelLightSource[] second = [new(new Vector3I(8, 1, 1), BlockDb.LightEmission[(int)BlockId.Torch])];
        byte[] firstLight = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, first);
        byte[] secondLight = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, second);
        byte[] combined = VoxelLighting.PropagateBlockLight(blocks, width, height, depth,
                [.. first, .. second]);
        byte firstContribution = At(firstLight, width, height, depth, 5, 1, 1);
        byte secondContribution = At(secondLight, width, height, depth, 5, 1, 1);
        TestAssert.Equal((byte)Math.Max(firstContribution, secondContribution),
                At(combined, width, height, depth, 5, 1, 1),
                "overlapping torch and campfire sources take the brighter propagated level instead of adding together");
        TestAssert.Equal(VoxelLighting.CampfireEmission,
                At(combined, width, height, depth, 2, 1, 1),
                "overlapping emitters preserve the shared peak source level");
    }

    private static void TestOpaqueBlocking()
    {
        const int width = 9;
        const int height = 7;
        const int depth = 7;
        byte[] blocks = new byte[width * height * depth];
        for (int z = 0; z < depth; z++)
            for (int y = 0; y < height; y++)
                blocks[Idx(width, height, depth, 4, y, z)] = (byte)BlockId.Stone;

        VoxelLightSource[] sources = [new(new Godot.Vector3I(2, 3, 3), 14)];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Equal((byte)0, At(light, width, height, depth, 5, 3, 3),
                "full opaque wall blocks block light without leaking around its edges");
    }

    private static void TestTransparentTransmission()
    {
        const int width = 8;
        const int height = 5;
        const int depth = 5;
        byte[] blocks = new byte[width * height * depth];
        blocks[Idx(width, height, depth, 3, 2, 2)] = (byte)BlockId.Glass;
        VoxelLightSource[] sources = [new(new Godot.Vector3I(1, 2, 2), 14)];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Check(At(light, width, height, depth, 4, 2, 2) > 0,
                "block light passes through glass");
        blocks[Idx(width, height, depth, 3, 2, 2)] = (byte)BlockId.Water;
        byte[] waterLight = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Check(At(waterLight, width, height, depth, 3, 2, 2)
                < At(light, width, height, depth, 3, 2, 2),
                "water attenuates more light than glass");
    }

    private static void TestFoliageGapTransmission()
    {
        const int width = 5;
        const int height = 5;
        const int depth = 3;
        byte[] blocks = new byte[width * height * depth];
        // Separate the foliage column and the open-gap column with stone so
        // the below-canopy value measures direct transmission, not side spill.
        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < depth; z++)
            {
                blocks[Idx(width, height, depth, 0, y, z)] = (byte)BlockId.Stone;
                blocks[Idx(width, height, depth, 2, y, z)] = (byte)BlockId.Stone;
                blocks[Idx(width, height, depth, 4, y, z)] = (byte)BlockId.Stone;
            }
            for (int x = 0; x < width; x++)
            {
                blocks[Idx(width, height, depth, x, y, 0)] = (byte)BlockId.Stone;
                blocks[Idx(width, height, depth, x, y, 2)] = (byte)BlockId.Stone;
            }
        }
        blocks[Idx(width, height, depth, 1, 3, 1)] = (byte)BlockId.Leaves;
        // x=3 is intentionally left air: it is the open canopy gap.

        byte[] sky = VoxelLighting.BuildSkyLight(blocks, width, height, depth);
        TestAssert.Equal((byte)13, At(sky, width, height, depth, 1, 3, 1),
                "leaf canopy voxels attenuate direct skylight without sealing the column");
        TestAssert.Equal((byte)15, At(sky, width, height, depth, 3, 3, 1),
                "an actual air gap in the canopy preserves full direct skylight");
        TestAssert.Equal((byte)13, At(sky, width, height, depth, 1, 2, 1),
                "direct skylight keeps the same attenuated level below the isolated leaf voxel");
        TestAssert.Equal((byte)15, At(sky, width, height, depth, 3, 2, 1),
                "the open foliage gap remains fully lit below the canopy");
        TestAssert.Check(BlockDb.LightOpacity[(int)BlockId.Leaves] < 15
                && BlockDb.Occl[(int)BlockId.Leaves] == 0,
                "alpha-cutout foliage remains a light-transmitting non-AO-occluder");

        byte[] isolated = new byte[3 * 4 * 3];
        isolated[Idx(3, 4, 3, 1, 2, 1)] = (byte)BlockId.Leaves;
        isolated[Idx(3, 4, 3, 0, 1, 1)] = (byte)BlockId.Stone;
        isolated[Idx(3, 4, 3, 2, 1, 1)] = (byte)BlockId.Stone;
        isolated[Idx(3, 4, 3, 1, 1, 0)] = (byte)BlockId.Stone;
        isolated[Idx(3, 4, 3, 1, 1, 2)] = (byte)BlockId.Stone;
        byte[] isolatedSky = VoxelLighting.BuildSkyLight(isolated, 3, 4, 3);
        TestAssert.Equal((byte)13, At(isolatedSky, 3, 4, 3, 1, 1, 1),
                "an isolated leaf transmits direct skylight with its full two-level opacity loss");
    }

    private static void TestShapeLightPassages()
    {
        BlockId slab = BlockId.PlanksSlab;
        BlockId stairs = BlockId.PlanksStairs;
        byte bottomTop = BlockShapes.LightPassageFaceMask(slab, BlockShapes.SlabBottom, 0);
        byte bottomBottom = BlockShapes.LightPassageFaceMask(slab, BlockShapes.SlabBottom, 1);
        byte topTop = BlockShapes.LightPassageFaceMask(slab, BlockShapes.SlabTop, 0);
        byte topBottom = BlockShapes.LightPassageFaceMask(slab, BlockShapes.SlabTop, 1);
        TestAssert.Check(bottomTop == 0x0F && bottomBottom == 0,
                "bottom slab cavity opens upward but its occupied underside blocks light");
        TestAssert.Check(topBottom == 0x0F && topTop == 0,
                "top slab cavity opens downward but its occupied top blocks light");
        TestAssert.Equal((byte)0, BlockShapes.LightPassageMask(slab, BlockShapes.SlabDouble),
                "a double slab has no light passage faces");

        byte uprightTop = BlockShapes.LightPassageFaceMask(stairs, 0, 0);
        byte uprightBottom = BlockShapes.LightPassageFaceMask(stairs, 0, 1);
        byte uprightFront = BlockShapes.LightPassageFaceMask(stairs, 0, 5);
        byte uprightBack = BlockShapes.LightPassageFaceMask(stairs, 0, 4);
        byte uprightRight = BlockShapes.LightPassageFaceMask(stairs, 0, 2);
        byte uprightLeft = BlockShapes.LightPassageFaceMask(stairs, 0, 3);
        byte invertedTop = BlockShapes.LightPassageFaceMask(stairs, BlockShapes.StairUpsideDown, 0);
        byte invertedBottom = BlockShapes.LightPassageFaceMask(stairs, BlockShapes.StairUpsideDown, 1);
        byte invertedFront = BlockShapes.LightPassageFaceMask(stairs, BlockShapes.StairUpsideDown, 5);
        byte invertedBack = BlockShapes.LightPassageFaceMask(stairs, BlockShapes.StairUpsideDown, 4);
        byte invertedRight = BlockShapes.LightPassageFaceMask(stairs, BlockShapes.StairUpsideDown, 2);
        byte invertedLeft = BlockShapes.LightPassageFaceMask(stairs, BlockShapes.StairUpsideDown, 3);
        TestAssert.Check(uprightTop == 0x03 && uprightBottom == 0
                && uprightFront == 0x0C && uprightBack == 0,
                "upright stair light follows its upper front cavity, not its solid back or base");
        TestAssert.Check(invertedBottom == 0x03 && invertedTop == 0
                && invertedFront == 0x03 && invertedBack == 0,
                "inverted stair light follows the lower front cavity and blocks its covered top");
        TestAssert.Equal((byte)0x04, uprightRight,
                "upright stair right-side opening occupies the matching upper-front micro-face");
        TestAssert.Equal((byte)0x04, uprightLeft,
                "upright stair left-side opening occupies the matching upper-front micro-face");
        TestAssert.Equal((byte)0x01, invertedRight,
                "inverted stair right-side opening occupies the matching lower-front micro-face");
        TestAssert.Equal((byte)0x01, invertedLeft,
                "inverted stair left-side opening occupies the matching lower-front micro-face");

        byte[] expectedTopApertures = [0x03, 0x0A, 0x0C, 0x05];
        for (byte facing = 0; facing < 4; facing++)
        {
            byte uprightTopMask = BlockShapes.LightPassageFaceMask(stairs, facing, 0);
            byte invertedBottomMask = BlockShapes.LightPassageFaceMask(stairs,
                    (byte)(facing | BlockShapes.StairUpsideDown), 1);
            TestAssert.Equal(expectedTopApertures[facing], uprightTopMask,
                    $"stair facing {facing} exposes only its upper-front sky aperture");
            TestAssert.Equal((byte)0, BlockShapes.LightPassageFaceMask(stairs, facing, 1),
                    $"upright stair facing {facing} blocks vertical sunlight below its solid base");
            TestAssert.Equal((byte)0, BlockShapes.LightPassageFaceMask(stairs,
                    (byte)(facing | BlockShapes.StairUpsideDown), 0),
                    $"inverted stair facing {facing} closes its upper face");
            TestAssert.Equal(expectedTopApertures[facing], invertedBottomMask,
                    $"inverted stair facing {facing} exposes the matching lower-front cavity");
        }

        TestAssert.Equal((byte)0, VoxelLighting.EffectiveOpacity(slab, BlockShapes.SlabBottom),
                "partial-shape cavity traversal does not inherit the full-cube opacity entry");
        TestAssert.Equal((byte)15, VoxelLighting.EffectiveOpacity(slab, BlockShapes.SlabDouble),
                "fully occupied shape state retains full opacity");

        byte[] blocks = new byte[3];
        byte[] states = new byte[3];
        blocks[0] = (byte)BlockId.PlanksStairs;
        blocks[1] = (byte)BlockId.PlanksStairs;
        blocks[2] = (byte)BlockId.Air;
        // A lower-front inverted stair faces an upper-front upright stair.
        // Their side openings do not line up in the shared four-bit face grid.
        states[0] = BlockShapes.StairUpsideDown;
        states[1] = 0;
        byte[] mismatched = VoxelLighting.PropagateBlockLight(blocks, 3, 1, 1,
                [new VoxelLightSource(new Vector3I(0, 0, 0), 14)], states);
        TestAssert.Equal((byte)0, At(mismatched, 3, 1, 1, 2, 0, 0),
                "light does not cross adjacent stair cavities whose half-face openings do not overlap");
    }

    private static void TestPartialShapeLighting()
    {
        const int width = 7;
        const int height = 5;
        const int depth = 5;

        static byte[] BuildHorizontalTunnel(BlockId shape, byte state,
                int width, int height, int depth, out byte[] states)
        {
            byte[] blocks = new byte[width * height * depth];
            Array.Fill(blocks, (byte)BlockId.Stone);
            states = new byte[blocks.Length];
            for (int x = 1; x <= 5; x++)
                blocks[Idx(width, height, depth, x, 2, 2)] = (byte)BlockId.Air;
            blocks[Idx(width, height, depth, 1, 2, 2)] = (byte)BlockId.Torch;
            int shapeIndex = Idx(width, height, depth, 3, 2, 2);
            blocks[shapeIndex] = (byte)shape;
            states[shapeIndex] = state;
            return blocks;
        }

        foreach ((BlockId shape, byte state, string label) in new[]
        {
            (BlockId.PlanksSlab, BlockShapes.SlabBottom, "bottom slab"),
            (BlockId.PlanksSlab, BlockShapes.SlabTop, "top slab"),
            (BlockId.PlanksStairs, (byte)0, "upright stairs"),
        })
        {
            byte[] blocks = BuildHorizontalTunnel(shape, state, width, height, depth, out byte[] states);
            byte[] blockLight = VoxelLighting.PropagateBlockLight(blocks, width, height, depth,
                    [new VoxelLightSource(new Vector3I(1, 2, 2), 14)], states);
            TestAssert.Check(At(blockLight, width, height, depth, 4, 2, 2) > 0,
                    $"block light remains continuous through the connected side cavity of a {label}");
            TestAssert.Equal((byte)11, At(blockLight, width, height, depth, 4, 2, 2),
                    $"{label} transmits light with one voxel-step attenuation through its opening");
        }

        // A roofed vertical shaft isolates top and bottom ports. The bottom
        // slab receives sun in its upper cavity but cannot pass it through its
        // occupied underside. Rotating it to a top slab closes the entrance.
        const int shaftWidth = 5;
        const int shaftHeight = 6;
        const int shaftDepth = 5;
        byte[] shaft = new byte[shaftWidth * shaftHeight * shaftDepth];
        Array.Fill(shaft, (byte)BlockId.Stone);
        byte[] shaftStates = new byte[shaft.Length];
        for (int y = 3; y < shaftHeight; y++)
            shaft[Idx(shaftWidth, shaftHeight, shaftDepth, 2, y, 2)] = (byte)BlockId.Air;
        shaft[Idx(shaftWidth, shaftHeight, shaftDepth, 2, 1, 2)] = (byte)BlockId.Air;
        int slabIndex = Idx(shaftWidth, shaftHeight, shaftDepth, 2, 2, 2);
        shaft[slabIndex] = (byte)BlockId.PlanksSlab;
        shaftStates[slabIndex] = BlockShapes.SlabBottom;
        byte[] bottomSky = VoxelLighting.BuildSkyLight(shaft, shaftWidth, shaftHeight, shaftDepth, shaftStates);
        TestAssert.Equal((byte)15, At(bottomSky, shaftWidth, shaftHeight, shaftDepth, 2, 2, 2),
                "direct sun reaches the open upper cavity of a bottom slab");
        TestAssert.Equal((byte)0, At(bottomSky, shaftWidth, shaftHeight, shaftDepth, 2, 1, 2),
                "the occupied bottom half of a slab stops direct skylight");

        shaftStates[slabIndex] = BlockShapes.SlabTop;
        byte[] topSky = VoxelLighting.BuildSkyLight(shaft, shaftWidth, shaftHeight, shaftDepth, shaftStates);
        TestAssert.Equal((byte)0, At(topSky, shaftWidth, shaftHeight, shaftDepth, 2, 2, 2),
                "a top slab blocks sunlight at its closed upper face");
        TestAssert.Equal((byte)0, At(topSky, shaftWidth, shaftHeight, shaftDepth, 2, 1, 2),
                "closed top-slab state does not leak skylight into the space below");

        shaftStates[slabIndex] = 0;
        shaft[slabIndex] = (byte)BlockId.PlanksStairs;
        byte[] stairSky = VoxelLighting.BuildSkyLight(shaft, shaftWidth, shaftHeight, shaftDepth, shaftStates);
        TestAssert.Equal((byte)15, At(stairSky, shaftWidth, shaftHeight, shaftDepth, 2, 2, 2),
                "direct sun enters the connected upper opening of an upright stair");
        TestAssert.Equal((byte)0, At(stairSky, shaftWidth, shaftHeight, shaftDepth, 2, 1, 2),
                "the solid stair base prevents skylight leaking into the lower voxel");
    }

    private static void TestChunkBoundary()
    {
        // Model two adjacent 16-wide chunks as one volume.
        const int width = 32;
        const int height = 8;
        const int depth = 8;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] sources = [new(new Godot.Vector3I(15, 4, 4), 14)];
        byte[] light = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, sources);
        TestAssert.Equal((byte)13, At(light, width, height, depth, 16, 4, 4),
                "light crosses the chunk boundary into the adjacent voxel");
        TestAssert.Equal((byte)12, At(light, width, height, depth, 17, 4, 4),
                "light continues attenuating inside the neighbouring chunk");
    }

    private static void TestRemoval()
    {
        const int width = 12;
        const int height = 8;
        const int depth = 8;
        byte[] blocks = new byte[width * height * depth];
        VoxelLightSource[] present = [new(new Godot.Vector3I(5, 4, 4), 14)];
        byte[] before = VoxelLighting.PropagateBlockLight(blocks, width, height, depth, present);
        byte[] afterRemoval = VoxelLighting.PropagateBlockLight(blocks, width, height, depth,
                System.Array.Empty<VoxelLightSource>());
        TestAssert.Check(At(before, width, height, depth, 6, 4, 4) > 0,
                "source initially illuminates adjacent cells");
        TestAssert.Equal((byte)0, At(afterRemoval, width, height, depth, 6, 4, 4),
                "re-solving after source removal clears stale block light");
    }

    private static void TestIncrementalBlockUpdates()
    {
        const int width = 32;
        const int height = 8;
        const int depth = 8;
        byte[] blocks = new byte[width * height * depth];
        byte[] incremental = new byte[blocks.Length];
        int first = Idx(width, height, depth, 15, 4, 4);
        int second = Idx(width, height, depth, 22, 4, 4);
        byte torch = BlockDb.LightEmission[(int)BlockId.Torch];

        blocks[first] = (byte)BlockId.Torch;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, first, torch);
        byte[] full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth,
                [new VoxelLightSource(new Godot.Vector3I(15, 4, 4), torch)]);
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental emitter addition matches a full block-light solve");

        blocks[second] = (byte)BlockId.Torch;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, second, torch);
        full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth,
                [
                    new VoxelLightSource(new Godot.Vector3I(15, 4, 4), torch),
                    new VoxelLightSource(new Godot.Vector3I(22, 4, 4), torch),
                ]);
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental second source addition matches full overlapping-source solve");

        blocks[first] = (byte)BlockId.Air;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, first, 0);
        full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth,
                [new VoxelLightSource(new Godot.Vector3I(22, 4, 4), torch)]);
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental source removal preserves the overlapping emitter");

        blocks[second] = (byte)BlockId.Air;
        VoxelLighting.ApplyIncrementalBlockLight(
                blocks, width, height, depth, incremental, second, 0);
        full = VoxelLighting.PropagateBlockLight(
                blocks, width, height, depth, System.Array.Empty<VoxelLightSource>());
        TestAssert.Check(EqualBytes(full, incremental),
                "incremental final source removal clears the complete light volume");
    }

    private static bool EqualBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void TestSunlightAndCaves()
    {
        const int width = 32;
        const int height = 12;
        const int depth = 32;
        byte[] blocks = new byte[width * height * depth];

        // A solid roof produces a genuinely enclosed dark volume.
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
                blocks[Idx(width, height, depth, x, 8, z)] = (byte)BlockId.Stone;

        byte[] sky = VoxelLighting.BuildSkyLight(blocks, width, height, depth);
        TestAssert.Equal((byte)15, At(sky, width, height, depth, 16, 9, 16),
                "open sky receives full direct sunlight");
        TestAssert.Equal((byte)0, At(sky, width, height, depth, 16, 7, 16),
                "opaque roof leaves the enclosed cave dark");

        // Open a vertical skylight shaft and verify light spills sideways below the roof.
        for (int y = 0; y <= 8; y++)
            blocks[Idx(width, height, depth, 0, y, 0)] = (byte)BlockId.Air;
        byte[] shaftSky = VoxelLighting.BuildSkyLight(blocks, width, height, depth);
        byte opening = At(shaftSky, width, height, depth, 0, 7, 0);
        byte caveEdge = At(shaftSky, width, height, depth, 4, 7, 0);
        TestAssert.Equal((byte)15, opening, "open skylight shaft keeps direct sun");
        TestAssert.Check(caveEdge > 0 && caveEdge < opening,
                "skylight diffuses laterally into cave openings");
    }

    private static void TestSkyBounceSeparation()
    {
        const int width = 5;
        const int height = 6;
        const int depth = 5;
        byte[] blocks = new byte[width * height * depth];

        // A stone ceiling with one open shaft feeds a horizontal cave tunnel.
        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
            blocks[Idx(width, height, depth, x, 4, z)] = (byte)BlockId.Stone;
        blocks[Idx(width, height, depth, 2, 4, 2)] = (byte)BlockId.Air;

        byte[] combined = VoxelLighting.BuildSkyLight(
                blocks, width, height, depth, null, out byte[] direct);
        byte directOpening = At(direct, width, height, depth, 2, 3, 2);
        byte directTunnel = At(direct, width, height, depth, 1, 3, 2);
        byte combinedTunnel = At(combined, width, height, depth, 1, 3, 2);

        TestAssert.Equal((byte)15, directOpening,
                "the unobstructed shaft retains full direct skylight");
        TestAssert.Equal((byte)0, directTunnel,
                "vertical direct skylight remains zero beneath the solid cave roof");
        TestAssert.Equal((byte)14, combinedTunnel,
                "the existing combined skylight still floods one step into the cave");
        TestAssert.Equal((byte)14, VoxelLighting.SkyBounce(combinedTunnel, directTunnel),
                "lateral cave fill is exposed as an independent indirect channel");
        TestAssert.Equal((byte)0, VoxelLighting.SkyBounce(directTunnel, directOpening),
                "the indirect channel cannot become negative when direct sky is brighter");

        Color channels = VoxelLighting.VertexColor(VoxelLighting.Pack(directTunnel, 9), 0.75f);
        TestAssert.Equal(0.0f, channels.R,
                "the mesh red channel carries direct sunlight only, not cave spill");
        TestAssert.Equal(9.0f / 15.0f, channels.G,
                "the mesh green channel remains reserved for warm block emission");

        float tag = MaterialResponse.VertexTag(
                SurfaceFamily.Wood, true, 14.0f / 15.0f);
        float integerTag = Mathf.Floor(tag + 0.001f);
        TestAssert.Equal(9.0f, integerTag,
                "fractional cave fill does not change the material family or wet bit");
        TestAssert.Check(Mathf.Abs(
                        14.0f / 15.0f - (tag - integerTag) * 16.0f) < 0.01f,
                "four-bit cave fill round-trips through the unused material-tag fraction");
    }

    /// <summary>
    /// Greedy merging must never fuse two faces whose cave spill differs: the
    /// merged quad re-uses one corner set for every covered cell. Combined sky
    /// and block light alone cannot prove that, because two cells can share the
    /// same combined sky while receiving different amounts of direct sun.
    /// </summary>
    private static void TestGreedyMergePreservesBounce()
    {
        const int height = 6;
        byte[] pad = new byte[Mesher.PLANE * height];
        byte[] statePad = new byte[Mesher.PLANE * height];
        int center = Mesher.PLANE * 2 + Mesher.PZ * 2 + 2;
        pad[center] = (byte)BlockId.Stone;

        // Same combined sky and block light, different vertical direct sun:
        // one sample is open to the sky, the other is lateral cave fill.
        byte[] sunlit = new byte[Mesher.PLANE * height];
        byte[] sunlitDirect = new byte[Mesher.PLANE * height];
        byte[] spilled = new byte[Mesher.PLANE * height];
        byte[] spilledDirect = new byte[Mesher.PLANE * height];
        Array.Fill(sunlit, VoxelLighting.Pack(15, 7));
        Array.Fill(sunlitDirect, (byte)15);
        Array.Fill(spilled, VoxelLighting.Pack(15, 7));
        Array.Fill(spilledDirect, (byte)5);

        TestAssert.Equal(
                Mesher.FaceKey(pad, statePad, sunlit, center, 0),
                Mesher.FaceKey(pad, statePad, spilled, center, 0),
                "the legacy surface key alone cannot tell cave spill from direct sun");

        Mesher.FaceMergeKey lit = Mesher.MergeKey(pad, statePad, sunlit, sunlitDirect, center, 0);
        Mesher.FaceMergeKey spill = Mesher.MergeKey(pad, statePad, spilled, spilledDirect, center, 0);
        TestAssert.Check(!lit.IsEmpty && !spill.IsEmpty,
                "both samples expose a visible opaque top face");
        TestAssert.Check(lit.Bounce == 0UL,
                "a fully sunlit corner contributes no cave spill to the merge key");
        TestAssert.Check(spill.Bounce == 0xAAAAUL,
                "every corner of the cave-lit sample records a ten-level spill nibble");
        TestAssert.Check(!lit.Equals(spill),
                "faces with different cave spill never share a greedy merge key");
        TestAssert.Check(Mesher.MergeKey(pad, statePad, spilled, spilledDirect, center, 0)
                        .Equals(spill),
                "identical lighting still merges, so greedy meshing is not over-split");
    }

    /// <summary>
    /// The fractional material-tag range must decode back to the same spill
    /// level for every family and wet combination without disturbing either.
    /// </summary>
    private static void TestMaterialTagChannels()
    {
        foreach (SurfaceFamily family in Enum.GetValues<SurfaceFamily>())
        {
            foreach (bool wet in new[] { false, true })
            {
                float expectedInteger = (byte)family + (wet ? 8.0f : 0.0f);
                foreach (byte bounce in new byte[] { 0, 1, 7, 15 })
                {
                    float tag = MaterialResponse.VertexTag(family, wet, bounce / 15.0f);
                    float decodedInteger = Mathf.Floor(tag + 0.001f);
                    TestAssert.Equal(expectedInteger, decodedInteger,
                            $"{family} wet={wet} spill={bounce} keeps its material family and wet flag");
                    // The tag lives around magnitude 8-15, where float32 steps
                    // are ~1e-6; 0.01 of a 15-level unit stays far inside one of
                    // the sixteen encoded steps and rejects a mis-scaled decode.
                    float decodedBounce = (tag - decodedInteger) * 16.0f * 15.0f;
                    TestAssert.Check(Mathf.Abs(decodedBounce - bounce) < 0.01f,
                            $"{family} wet={wet} spill={bounce} round-trips the cave-spill nibble");
                }
            }
        }
    }

    private static int Idx(int width, int height, int depth, int x, int y, int z) =>
        x + z * width + y * width * depth;

    private static byte At(byte[] values, int width, int height, int depth, int x, int y, int z) =>
        values[Idx(width, height, depth, x, y, z)];
}
