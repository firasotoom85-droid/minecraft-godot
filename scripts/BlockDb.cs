using System;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace Minecraft;

/// <summary>
/// Block database + procedurally generated pixel-art texture atlas.
/// No external assets: every block texture is painted here at startup.
///
/// The public API speaks in <see cref="BlockId"/> — the enum makes every
/// block comparison compile-time checked. The flattened lookup tables used by
/// the mesher hot loop stay as raw arrays internally, where ids are indexes.
/// </summary>
public static class BlockDb
{
    // Table stride, not the number of defined blocks. Ids are append-only and
    // persist in saves, so leave headroom and never renumber an existing id.
    public const int BlockCount = 128;

    public static readonly BlockId[] Hotbar =
    [
        BlockId.Grass, BlockId.Dirt, BlockId.Stone, BlockId.Sand, BlockId.Log,
        BlockId.Planks, BlockId.Cobblestone, BlockId.Glass, BlockId.Leaves,
    ];

    // 16x16 tiles keeps the atlas at 512x512 and gives 256 texture-array
    // layers, enough for the M6 content pass without reworking UV math again.
    public const int AtlasCols = 16;
    public const int TileCount = AtlasCols * AtlasCols;
    public const int TilePx = 32;
    // Shared smallest authored noise/speckle unit across terrain materials.
    // Structural accents (grain, seams, foliage silhouettes) may still use
    // individual pixels where their shape requires it.
    public const int TextureDetailPx = 2;

    private const int TGrassTop = 0;
    private const int TGrassSide = 1;
    private const int TDirt = 2;
    private const int TStone = 3;
    private const int TSand = 4;
    private const int TLogSide = 5;
    private const int TLogTop = 6;
    private const int TLeaves = 7;
    private const int TWater = 8;
    private const int TPlanks = 9;
    private const int TCobblestone = 10;
    private const int TBrick = 11;
    private const int TCoalOre = 12;
    private const int TIronOre = 13;
    private const int TGlass = 14;
    private const int TBedrock = 15;
    private const int TSnow = 16;
    private const int TSnowSide = 17;
    private const int TGravel = 18;
    private const int TGoldOre = 19;
    private const int TDiamondOre = 20;
    private const int TBirchSide = 21;
    private const int TBirchTop = 22;
    private const int TBirchLeaves = 23;
    private const int TCactusSide = 24;
    private const int TCactusTop = 25;
    private const int TCraftingTop = 26;
    private const int TCraftingSide = 27;
    private const int TWool = 28;
    private const int TLava = 29;
    private const int TTallGrass = 30;
    private const int TFlowerRed = 31;
    private const int TFlowerYellow = 32;
    private const int TTorch = 33;
    private const int TChest = 34;
    private const int TFurnace = 35;
    private const int TFarmlandTop = 36;
    private const int TFarmlandSide = 37;
    private const int TWheat0 = 38;
    private const int TWheat1 = 39;
    private const int TWheat2 = 40;
    private const int TBedTop = 41;
    private const int TBedSide = 42;
    private const int TLadder = 43;
    private const int TEnchantTop = 44;
    private const int TEnchantSide = 45;
    private const int TPlanksBuilding = 46;
    private const int TCobbleBuilding = 47;
    private const int TBrickBuilding = 48;
    // Append new art tiles without shifting any existing atlas coordinates.
    private const int TPlanksTop = 49;
    private const int TPlanksSide = 50;
    private const int TPlanksEnd = 51;
    // Weathered structure materials follow the wood-grain range so every
    // material keeps a unique atlas coordinate (49-51 are taken by #45).
    private const int TMossyCobblestone = 52;
    private const int TWeatheredCobblestone = 53;
    private const int TCrackedBrick = 54;
    private const int TMossyBrick = 55;
    // Biome foliage follows the weathering range; every tile stays unique.
    private const int TSpruceLeaves = 56;
    private const int TJungleLeaves = 57;
    private const int TSwampLeaves = 58;
    private const int TRiverReeds = 59;

    /// <summary>
    /// One block's definition; the tiles array is indexed by face (FACE order).
    /// Hardness is the mining resistance used by MiningSeconds. Tool selects the
    /// preferred mining class; Tier gates drops; Drops disables the own-block
    /// drop for blocks such as glass and leaves. Light is 0..1 for emitters.
    /// </summary>
    public readonly record struct BlockDef(
        string Name, bool Solid, bool Transparent, int[] Tiles,
        float Hardness = 1.0f, ToolKind Tool = ToolKind.None, int Tier = 0,
        ItemId Drop = ItemId.None, int DropCount = 1, float Light = 0.0f, bool Drops = true);

    // Face order: +Y, -Y, +X, -X, +Z, -Z  (-1 = no face)
    private static readonly BlockDef[] Defs =
    [
        new("Air", false, true, [-1, -1, -1, -1, -1, -1], 0.0f, Drops: false),
        new("Grass Block", true, false, [TGrassTop, TDirt, TGrassSide, TGrassSide, TGrassSide, TGrassSide],
                0.6f, ToolKind.Shovel),
        new("Dirt", true, false, [TDirt, TDirt, TDirt, TDirt, TDirt, TDirt], 0.5f, ToolKind.Shovel),
        new("Stone", true, false, [TStone, TStone, TStone, TStone, TStone, TStone],
                1.5f, ToolKind.Pick, 1, ItemId.Cobblestone),
        new("Sand", true, false, [TSand, TSand, TSand, TSand, TSand, TSand], 0.5f, ToolKind.Shovel),
        new("Oak Log", true, false, [TLogTop, TLogTop, TLogSide, TLogSide, TLogSide, TLogSide],
                2.0f, ToolKind.Axe),
        new("Oak Leaves", true, false, [TLeaves, TLeaves, TLeaves, TLeaves, TLeaves, TLeaves],
                0.2f, ToolKind.Hoe, Drops: false),
        new("Water", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Planks", true, false,
                [TPlanksTop, TPlanksTop, TPlanksEnd, TPlanksEnd, TPlanksSide, TPlanksSide],
                2.0f, ToolKind.Axe),
        new("Cobblestone", true, false, [TCobblestone, TCobblestone, TCobblestone, TCobblestone, TCobblestone, TCobblestone],
                2.0f, ToolKind.Pick, 1),
        new("Bricks", true, false, [TBrick, TBrick, TBrick, TBrick, TBrick, TBrick],
                1.5f, ToolKind.Pick, 1),
        new("Coal Ore", true, false, [TCoalOre, TCoalOre, TCoalOre, TCoalOre, TCoalOre, TCoalOre],
                3.0f, ToolKind.Pick, 1, ItemId.Coal),
        new("Iron Ore", true, false, [TIronOre, TIronOre, TIronOre, TIronOre, TIronOre, TIronOre],
                3.0f, ToolKind.Pick, 2, ItemId.RawIron),
        new("Glass", true, true, [TGlass, TGlass, TGlass, TGlass, TGlass, TGlass],
                0.3f, Drops: false),
        new("Bedrock", true, false, [TBedrock, TBedrock, TBedrock, TBedrock, TBedrock, TBedrock],
                100000.0f, Drops: false),
        new("Snowy Grass", true, false, [TSnow, TDirt, TSnowSide, TSnowSide, TSnowSide, TSnowSide],
                0.6f, ToolKind.Shovel),
        new("Snow Block", true, false, [TSnow, TSnow, TSnow, TSnow, TSnow, TSnow], 0.2f, ToolKind.Shovel),
        new("Gravel", true, false, [TGravel, TGravel, TGravel, TGravel, TGravel, TGravel],
                0.6f, ToolKind.Shovel),
        new("Gold Ore", true, false, [TGoldOre, TGoldOre, TGoldOre, TGoldOre, TGoldOre, TGoldOre],
                3.0f, ToolKind.Pick, 3, ItemId.RawGold),
        new("Diamond Ore", true, false, [TDiamondOre, TDiamondOre, TDiamondOre, TDiamondOre, TDiamondOre, TDiamondOre],
                3.0f, ToolKind.Pick, 3, ItemId.Diamond),
        new("Birch Log", true, false, [TBirchTop, TBirchTop, TBirchSide, TBirchSide, TBirchSide, TBirchSide],
                2.0f, ToolKind.Axe),
        new("Birch Leaves", true, false, [TBirchLeaves, TBirchLeaves, TBirchLeaves, TBirchLeaves, TBirchLeaves, TBirchLeaves],
                0.2f, ToolKind.Hoe, Drops: false),
        new("Cactus", true, false, [TCactusTop, TCactusTop, TCactusSide, TCactusSide, TCactusSide, TCactusSide],
                0.4f),
        new("Crafting Table", true, false,
                [TCraftingTop, TPlanks, TCraftingSide, TCraftingSide, TCraftingSide, TCraftingSide],
                2.5f, ToolKind.Axe),
        new("Wool", true, false, [TWool, TWool, TWool, TWool, TWool, TWool], 0.8f),
        new("Lava", false, true, [TLava, TLava, TLava, TLava, TLava, TLava], 0.0f, Drops: false, Light: 0.8f),
        new("Tall Grass", false, true,
                [TTallGrass, TTallGrass, TTallGrass, TTallGrass, TTallGrass, TTallGrass],
                0.05f, Drops: false),
        new("Red Flower", false, true,
                [TFlowerRed, TFlowerRed, TFlowerRed, TFlowerRed, TFlowerRed, TFlowerRed], 0.05f),
        new("Yellow Flower", false, true,
                [TFlowerYellow, TFlowerYellow, TFlowerYellow, TFlowerYellow, TFlowerYellow, TFlowerYellow], 0.05f),
        new("Torch", false, true,
                [TTorch, TTorch, TTorch, TTorch, TTorch, TTorch], 0.05f, Light: 0.9f),
        new("Chest", true, false,
                [TChest, TChest, TChest, TChest, TChest, TChest], 2.5f, ToolKind.Axe),
        new("Furnace", true, false,
                [TFurnace, TFurnace, TFurnace, TFurnace, TFurnace, TFurnace], 3.5f, ToolKind.Pick, 1),
        new("Farmland", true, false,
                [TFarmlandTop, TDirt, TFarmlandSide, TFarmlandSide, TFarmlandSide, TFarmlandSide],
                0.6f, ToolKind.Shovel, Drop: ItemId.Dirt),
        new("Wheat Sprout", false, true,
                [TWheat0, TWheat0, TWheat0, TWheat0, TWheat0, TWheat0], 0.05f, Drops: false),
        new("Wheat", false, true,
                [TWheat1, TWheat1, TWheat1, TWheat1, TWheat1, TWheat1], 0.05f, Drops: false),
        new("Ripe Wheat", false, true,
                [TWheat2, TWheat2, TWheat2, TWheat2, TWheat2, TWheat2], 0.05f, Drops: false),
        new("Bed", true, false,
                [TBedTop, TBedSide, TBedSide, TBedSide, TBedSide, TBedSide], 0.2f, ToolKind.Axe),
        new("Ladder", false, true,
                [TLadder, TLadder, TLadder, TLadder, TLadder, TLadder], 0.4f, ToolKind.Axe),
        new("Flowing Water 1", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Flowing Water 2", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Flowing Water 3", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Flowing Water 4", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Flowing Water 5", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Flowing Water 6", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Flowing Water 7", false, true, [TWater, TWater, TWater, TWater, TWater, TWater], 0.0f, Drops: false),
        new("Enchanting Table", true, false,
                [TEnchantTop, TEnchantSide, TEnchantSide, TEnchantSide, TEnchantSide, TEnchantSide],
                3.0f, ToolKind.Pick, 2),
        new("Oak Planks Slab", true, false,
                [TPlanksTop, TPlanksTop, TPlanksEnd, TPlanksEnd, TPlanksSide, TPlanksSide],
                2.0f, ToolKind.Axe),
        new("Cobblestone Slab", true, false,
                [TCobbleBuilding, TCobbleBuilding, TCobbleBuilding, TCobbleBuilding, TCobbleBuilding, TCobbleBuilding],
                2.0f, ToolKind.Pick, 1),
        new("Brick Slab", true, false,
                [TBrickBuilding, TBrickBuilding, TBrickBuilding, TBrickBuilding, TBrickBuilding, TBrickBuilding],
                1.5f, ToolKind.Pick, 1),
        new("Oak Planks Stairs", true, false,
                [TPlanksTop, TPlanksTop, TPlanksEnd, TPlanksEnd, TPlanksSide, TPlanksSide],
                2.0f, ToolKind.Axe),
        new("Cobblestone Stairs", true, false,
                [TCobbleBuilding, TCobbleBuilding, TCobbleBuilding, TCobbleBuilding, TCobbleBuilding, TCobbleBuilding],
                2.0f, ToolKind.Pick, 1),
        new("Brick Stairs", true, false,
                [TBrickBuilding, TBrickBuilding, TBrickBuilding, TBrickBuilding, TBrickBuilding, TBrickBuilding],
                1.5f, ToolKind.Pick, 1),
        new("Mossy Cobblestone", true, false,
                [TMossyCobblestone, TMossyCobblestone, TMossyCobblestone, TMossyCobblestone, TMossyCobblestone, TMossyCobblestone],
                2.0f, ToolKind.Pick, 1),
        new("Weathered Cobblestone", true, false,
                [TWeatheredCobblestone, TWeatheredCobblestone, TWeatheredCobblestone, TWeatheredCobblestone, TWeatheredCobblestone, TWeatheredCobblestone],
                2.0f, ToolKind.Pick, 1),
        new("Cracked Brick", true, false,
                [TCrackedBrick, TCrackedBrick, TCrackedBrick, TCrackedBrick, TCrackedBrick, TCrackedBrick],
                1.5f, ToolKind.Pick, 1),
        new("Mossy Brick", true, false,
                [TMossyBrick, TMossyBrick, TMossyBrick, TMossyBrick, TMossyBrick, TMossyBrick],
                1.5f, ToolKind.Pick, 1),
        new("Spruce Leaves", true, false,
                [TSpruceLeaves, TSpruceLeaves, TSpruceLeaves, TSpruceLeaves, TSpruceLeaves, TSpruceLeaves],
                0.2f, ToolKind.Hoe, Drops: false),
        new("Jungle Leaves", true, false,
                [TJungleLeaves, TJungleLeaves, TJungleLeaves, TJungleLeaves, TJungleLeaves, TJungleLeaves],
                0.2f, ToolKind.Hoe, Drops: false),
        new("Swamp Leaves", true, false,
                [TSwampLeaves, TSwampLeaves, TSwampLeaves, TSwampLeaves, TSwampLeaves, TSwampLeaves],
                0.2f, ToolKind.Hoe, Drops: false),
        new("River Reeds", false, true, [TRiverReeds, TRiverReeds, TRiverReeds, TRiverReeds, TRiverReeds, TRiverReeds],
                0.05f),
    ];

    // Cube face geometry shared by the mesher. Order: +Y, -Y, +X, -X, +Z, -Z.
    // Winding is clockwise as seen from outside (Godot front-face convention).
    // Flat arrays: face f occupies FaceVerts[f * 4 .. f * 4 + 3].
    public static readonly Vector3[] FaceDirs =
    [
        new(0, 1, 0), new(0, -1, 0), new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
    ];

    private static readonly float[] FaceShades = [1.0f, 0.55f, 0.8f, 0.8f, 0.65f, 0.65f];

    public static readonly Vector3[] FaceVerts =
    [
        new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1),
        new(0, 0, 0), new(0, 0, 1), new(1, 0, 1), new(1, 0, 0),
        new(1, 0, 0), new(1, 0, 1), new(1, 1, 1), new(1, 1, 0),
        new(0, 0, 0), new(0, 1, 0), new(0, 1, 1), new(0, 0, 1),
        new(0, 0, 1), new(0, 1, 1), new(1, 1, 1), new(1, 0, 1),
        new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 0, 0),
    ];

    public static readonly Vector2[] FaceUvs =
    [
        new(0, 0), new(1, 0), new(1, 1), new(0, 1),
        new(0, 0), new(0, 1), new(1, 1), new(1, 0),
        new(1, 1), new(0, 1), new(0, 0), new(1, 0),
        new(0, 1), new(0, 0), new(1, 0), new(1, 1),
        new(0, 1), new(0, 0), new(1, 0), new(1, 1),
        new(0, 1), new(0, 0), new(1, 0), new(1, 1),
    ];

    // -----------------------------------------------------------------------
    // Flattened lookup tables for the hot meshing loop (built once, lazily).
    // -----------------------------------------------------------------------

    private static readonly Lock TableLock = new();
    private static bool _tablesReady;
    internal static int[] Tiles = [];       // id * 6 + f -> tile (-1 = none)
    internal static byte[] Render = [];     // self * BlockCount + neighbor -> 1/0
    internal static byte[] SolidMask = [];  // id -> 1/0
    internal static byte[] TransMask = [];  // id -> 1/0
    internal static byte[] Occl = [];       // id -> 1/0 (opaque solid, not foliage)
    internal static byte[] LightEmission = []; // id -> 0..15 block-light source level
    internal static byte[] LightOpacity = [];  // id -> 0..15 propagation loss
    private static byte[] _foliage = [];    // id -> 1/0 (cutout leaf types)
    internal static Vector3[] FaceNormals = [];  // f
    internal static Color[] FaceColors = [];     // f
    internal static Vector2[] UvLut = [];        // (tile * 6 + f) * 4 + i

    public static bool IsDefined(BlockId id) => (int)id >= 0 && (int)id < Defs.Length;

    public static BlockDef DefOf(BlockId id) => Defs[(int)id];

    /// <summary>Whether the held tool satisfies this block's drop requirement.</summary>
    public static bool CanHarvest(BlockDef def, ToolInfo? tool) =>
        def.Tier <= 0
        || (tool is { } held && held.Kind == def.Tool && held.Tier >= def.Tier);

    /// <summary>
    /// Mining duration: the matching tool uses hardness × 1.5 / speed;
    /// a hand or a non-matching tool uses hardness × 5. Blocks without a
    /// preferred tool class use the normal 1.5 hardness multiplier.
    /// </summary>
    public static float MiningSeconds(BlockDef def, ToolInfo? tool)
    {
        if (def.Hardness <= 0.0f || def.Hardness >= 1000.0f)
        {
            return float.PositiveInfinity;
        }

        if (def.Tool == ToolKind.None)
        {
            return def.Hardness * 1.5f;
        }

        if (tool is { } held && held.Kind == def.Tool && held.Speed > 0.0f)
        {
            return def.Hardness * 1.5f / held.Speed;
        }

        return def.Hardness * 5.0f;
    }

    /// <summary>
    /// Intersects the two diagonal, full-height planes used by cross-plant
    /// geometry. Direction must be normalized; distances are from origin.
    /// </summary>
    public static bool RayIntersectsCross(
            Vector3 origin, Vector3 direction, Vector3I block, float minDistance,
            float maxDistance, out float hitDistance)
    {
        hitDistance = float.PositiveInfinity;
        Vector3 localOrigin = origin - new Vector3(block.X, block.Y, block.Z);
        const float epsilon = 0.0001f;

        // Plane A: local X == local Z.
        float denominatorA = direction.X - direction.Z;
        if (Mathf.Abs(denominatorA) > epsilon)
        {
            float distance = (localOrigin.Z - localOrigin.X) / denominatorA;
            if (distance >= minDistance - epsilon && distance <= maxDistance + epsilon)
            {
                Vector3 point = localOrigin + direction * distance;
                if (point.X >= -epsilon && point.X <= 1.0f + epsilon
                        && point.Y >= -epsilon && point.Y <= 1.0f + epsilon
                        && point.Z >= -epsilon && point.Z <= 1.0f + epsilon)
                {
                    hitDistance = distance;
                }
            }
        }

        // Plane B: local X + local Z == 1.
        float denominatorB = direction.X + direction.Z;
        if (Mathf.Abs(denominatorB) > epsilon)
        {
            float distance = (1.0f - localOrigin.X - localOrigin.Z) / denominatorB;
            if (distance >= minDistance - epsilon && distance <= maxDistance + epsilon)
            {
                Vector3 point = localOrigin + direction * distance;
                if (point.X >= -epsilon && point.X <= 1.0f + epsilon
                        && point.Y >= -epsilon && point.Y <= 1.0f + epsilon
                        && point.Z >= -epsilon && point.Z <= 1.0f + epsilon)
                {
                    hitDistance = Mathf.Min(hitDistance, distance);
                }
            }
        }

        return float.IsFinite(hitDistance);
    }

    /// <summary>Crack texture stage in the ten-stage breaking animation.</summary>
    public static int CrackStage(float progress) =>
        Math.Clamp((int)(Mathf.Clamp(progress, 0.0f, 1.0f) * 10.0f), 0, 9);

    public static void EnsureTables()
    {
        lock (TableLock)
        {
            if (_tablesReady)
            {
                return;
            }

            _tablesReady = true;
            Tiles = new int[BlockCount * 6];
            System.Array.Fill(Tiles, -1);
            Render = new byte[BlockCount * BlockCount];
            SolidMask = new byte[BlockCount];
            TransMask = new byte[BlockCount];
            Occl = new byte[BlockCount];
            LightEmission = new byte[BlockCount];
            LightOpacity = new byte[BlockCount];
            _foliage = new byte[BlockCount];
            _foliage[(int)BlockId.Leaves] = 1;
            _foliage[(int)BlockId.BirchLeaves] = 1;
            _foliage[(int)BlockId.SpruceLeaves] = 1;
            _foliage[(int)BlockId.JungleLeaves] = 1;
            _foliage[(int)BlockId.SwampLeaves] = 1;
            foreach (BlockId id in DefinedIds())
            {
                int[] tiles = Defs[(int)id].Tiles;
                for (int f = 0; f < 6; f++)
                {
                    Tiles[(int)id * 6 + f] = tiles[f];
                }

                SolidMask[(int)id] = Defs[(int)id].Solid ? (byte)1 : (byte)0;
                TransMask[(int)id] = Defs[(int)id].Transparent ? (byte)1 : (byte)0;
                // Foliage is not an AO occluder (light passes through leaves) and
                // is never skipped as "fully enclosed" because leaf/leaf faces
                // are drawn.
                Occl[(int)id] = IsSolid(id) && !IsTransparent(id) && !IsFoliage(id) ? (byte)1 : (byte)0;
                LightEmission[(int)id] = (byte)Mathf.Clamp(Mathf.RoundToInt(Defs[(int)id].Light * 15.0f), 0, 15);
                LightOpacity[(int)id] = id switch
                {
                    BlockId.Water or BlockId.Leaves or BlockId.BirchLeaves
                            or BlockId.SpruceLeaves or BlockId.JungleLeaves
                            or BlockId.SwampLeaves => 2,
                    _ when IsSolid(id) && !IsTransparent(id) && !IsFoliage(id) => 15,
                    _ => 0,
                };
            }

            foreach (BlockId self in DefinedIds())
            {
                foreach (BlockId neighbor in DefinedIds())
                {
                    Render[(int)self * BlockCount + (int)neighbor] = RenderFace(self, neighbor) ? (byte)1 : (byte)0;
                }
            }

            FaceNormals = new Vector3[6];
            FaceColors = new Color[6];
            for (int f = 0; f < 6; f++)
            {
                FaceNormals[f] = FaceDirs[f];
                float shade = FaceShades[f];
                FaceColors[f] = new Color(shade, shade, shade, 1.0f);
            }

            UvLut = new Vector2[TileCount * 6 * 4];
            for (int tile = 0; tile < TileCount; tile++)
            {
                Rect2 rect = TileUvRect(tile);
                for (int f = 0; f < 6; f++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        UvLut[(tile * 6 + f) * 4 + i] = MapUv(rect, FaceUvs[f * 4 + i]);
                    }
                }
            }
        }
    }

    /// <summary>Every defined block id, generated without boxing.</summary>
    private static IEnumerable<BlockId> DefinedIds()
    {
        for (int i = 0; i < Defs.Length; i++)
        {
            yield return (BlockId)i;
        }
    }

    private static readonly Lazy<ImageTexture> AtlasLazy = new(() => ImageTexture.CreateFromImage(BuildAtlasImage()));
    private static readonly Lazy<Shader?> WaterShaderLazy = new(() => ResourceLoader.Load<Shader>("res://shaders/water.gdshader"));

    public static string NameOf(BlockId id) => Defs[(int)id].Name;

    public static bool IsSolid(BlockId id) => Defs[(int)id].Solid;

    public static bool IsTransparent(BlockId id) => Defs[(int)id].Transparent;

    public static int WaterLevel(BlockId id)
    {
        if (id == BlockId.Water) return 0;
        int level = (int)id - (int)BlockId.FlowingWater1 + 1;
        return level is >= 1 and <= 7 ? level : -1;
    }

    public static bool IsWater(BlockId id) => WaterLevel(id) >= 0;

    public static float WaterHeight(BlockId id)
    {
        int level = WaterLevel(id);
        return level < 0 ? 0.0f : 1.0f - level / 8.0f;
    }

    /// <summary>
    /// Whether the face of <paramref name="selfId"/> towards <paramref name="neighborId"/> must be drawn.
    /// Foliage/foliage faces are kept so alpha-cutout canopies look full.
    /// </summary>
    public static bool RenderFace(BlockId selfId, BlockId neighborId)
    {
        if (IsWater(selfId) && IsWater(neighborId)) return false;

        if (neighborId == BlockId.Air)
        {
            return true;
        }

        if (IsFoliage(selfId) && IsFoliage(neighborId))
        {
            return true;
        }

        if (IsTransparent(neighborId))
        {
            return selfId != neighborId;
        }

        return false;
    }

    public static bool IsFoliage(BlockId id) => id is BlockId.Leaves or BlockId.BirchLeaves
            or BlockId.SpruceLeaves or BlockId.JungleLeaves or BlockId.SwampLeaves;

    /// <summary>Soil and loose mineral surfaces receive a damp response against water.</summary>
    public static bool IsWettable(BlockId id) => id is BlockId.Grass or BlockId.Dirt
            or BlockId.Sand or BlockId.Gravel or BlockId.Farmland;

    /// <summary>Fine shoreline plants use the wet foliage response beside water.</summary>
    public static bool IsShorelineVegetation(BlockId id) =>
            id is BlockId.TallGrass or BlockId.RiverReeds;

    /// <summary>Cross-shaped plants: two crossed double-sided quads, no cube faces.</summary>
    public static bool IsCross(BlockId id) =>
        id is BlockId.TallGrass or BlockId.FlowerRed or BlockId.FlowerYellow or BlockId.Torch
                or BlockId.Wheat0 or BlockId.Wheat1 or BlockId.Wheat2 or BlockId.RiverReeds;

    public static bool IsWheat(BlockId id) =>
        id is BlockId.Wheat0 or BlockId.Wheat1 or BlockId.Wheat2;

    /// <summary>Tile used for the hotbar icon of a block.</summary>
    public static int IconTile(BlockId id)
    {
        int[] tiles = Defs[(int)id].Tiles;
        return tiles[4] >= 0 ? tiles[4] : tiles[0];
    }

    private static readonly Color[] TileAverageColors = new Color[TileCount];
    private static readonly bool[] TileAverageReady = new bool[TileCount];
    private static readonly Lock TileAverageLock = new();

    /// <summary>Returns a block's texture tile for a face in +Y, -Y, +X, -X, +Z, -Z order.</summary>
    public static int FaceTile(BlockId id, int face)
    {
        if (!IsDefined(id) || (uint)face >= 6u)
        {
            return -1;
        }

        return Defs[(int)id].Tiles[face];
    }

    /// <summary>Average opaque color for one specific block face's atlas tile.</summary>
    public static Color AverageFaceColor(BlockId id, int face)
    {
        int tile = FaceTile(id, face);
        return tile >= 0 ? AverageAtlasTileColor(tile) : Colors.White;
    }

    /// <summary>Average (opaque) color of a block's icon tile — used to tint particles.</summary>
    public static Color AverageTileColor(BlockId id) => AverageAtlasTileColor(IconTile(id));

    private static Color AverageAtlasTileColor(int tile)
    {
        if ((uint)tile >= TileCount)
        {
            return Colors.White;
        }

        lock (TileAverageLock)
        {
            if (TileAverageReady[tile])
            {
                return TileAverageColors[tile];
            }

            Image img = AtlasTexture().GetImage();
            int ox = (tile % AtlasCols) * TilePx;
            int oy = (tile / AtlasCols) * TilePx;
            Vector3 acc = Vector3.Zero;
            int n = 0;
            for (int y = 0; y < TilePx; y++)
            {
                for (int x = 0; x < TilePx; x++)
                {
                    Color c = img.GetPixel(ox + x, oy + y);
                    if (c.A > 0.5f)
                    {
                        acc += new Vector3(c.R, c.G, c.B);
                        n++;
                    }
                }
            }

            Color average = Colors.White;
            if (n > 0)
            {
                acc /= n;
                average = new Color(acc.X, acc.Y, acc.Z);
            }

            TileAverageColors[tile] = average;
            TileAverageReady[tile] = true;
            return average;
        }
    }

    // -----------------------------------------------------------------------
    // Atlas / UV helpers
    // -----------------------------------------------------------------------

    public static ImageTexture AtlasTexture() => AtlasLazy.Value;

    public static Rect2 TileUvRect(int tile)
    {
        int col = tile % AtlasCols;
        int row = tile / AtlasCols;
        float step = 1.0f / AtlasCols;
        return new Rect2(col * step, row * step, step, step);
    }

    /// <summary>
    /// Map a tile-local uv (0..1) into the atlas, with a tiny inset so that
    /// nearest-neighbour filtering can never sample a neighbouring tile.
    /// </summary>
    public static Vector2 MapUv(Rect2 rect, Vector2 uv)
    {
        float inset = 0.25f / (AtlasCols * TilePx);
        return new Vector2(
            rect.Position.X + inset + uv.X * (rect.Size.X - inset * 2.0f),
            rect.Position.Y + inset + uv.Y * (rect.Size.Y - inset * 2.0f));
    }

    /// <summary>
    /// Convert a block-local point on one of the canonical cube faces to the
    /// same oriented 0..1 UV used by FaceUvs. Unlike a face-corner lookup, this
    /// also preserves texture density on half-block micro-faces.
    /// </summary>
    public static Vector2 FaceUvAt(int face, Vector3 localPosition) => face switch
    {
        0 or 1 => new Vector2(localPosition.X, localPosition.Z),
        2 => new Vector2(1.0f - localPosition.Z, 1.0f - localPosition.Y),
        3 => new Vector2(localPosition.Z, 1.0f - localPosition.Y),
        4 => new Vector2(localPosition.X, 1.0f - localPosition.Y),
        5 => new Vector2(1.0f - localPosition.X, 1.0f - localPosition.Y),
        _ => Vector2.Zero,
    };

    /// <summary>
    /// Shape-face tile selection. Stairs turn their board run with their facing:
    /// odd facings expose the long-grain faces on X and cut ends on Z.
    /// </summary>
    public static int ShapeFaceTile(BlockId id, byte state, int face)
    {
        if ((uint)face >= 6 || !IsDefined(id)) return -1;
        int[] tiles = Defs[(int)id].Tiles;
        if (id == BlockId.PlanksStairs && (state & BlockShapes.StairFacingMask) % 2 != 0)
        {
            if (face is 2 or 3) return tiles[4];
            if (face is 4 or 5) return tiles[2];
        }

        return tiles[face];
    }

    /// <summary>
    /// Block-specific UV orientation in the full unit-cube coordinate frame.
    /// East/west oak stairs rotate their tread grain to follow the stair width.
    /// </summary>
    public static Vector2 BlockFaceUvAt(BlockId id, byte state, int face, Vector3 localPosition)
    {
        Vector2 uv = FaceUvAt(face, localPosition);
        if (id == BlockId.PlanksStairs && face is 0 or 1
                && (state & BlockShapes.StairFacingMask) % 2 != 0)
        {
            uv = new Vector2(uv.Y, 1.0f - uv.X);
        }

        return uv;
    }

    private const string LitAtlasShader = @"
shader_type spatial;
render_mode cull_back, depth_draw_opaque, alpha_to_coverage;

uniform sampler2D atlas : source_color, filter_nearest, repeat_disable;
uniform vec4 tint = vec4(1.0);
uniform float sky_light_energy = 1.0;

uniform vec3 response_stone = vec3(0.8, 0.0, 0.2);
uniform vec3 response_wood = vec3(0.8, 0.0, 0.2);
uniform vec3 response_soil = vec3(0.8, 0.0, 0.2);
uniform vec3 response_ore = vec3(0.8, 0.0, 0.2);
uniform vec3 response_foliage = vec3(0.8, 0.0, 0.2);
uniform vec3 response_glass = vec3(0.8, 0.0, 0.2);
uniform vec3 response_metal = vec3(0.8, 0.0, 0.2);

vec3 surface_response(float family) {
    if (family > 5.5) return response_metal;
    if (family > 4.5) return response_glass;
    if (family > 3.5) return response_foliage;
    if (family > 2.5) return response_ore;
    if (family > 1.5) return response_soil;
    if (family > 0.5) return response_wood;
    return response_stone;
}

varying vec3 surface_world_position;
varying vec3 surface_world_normal;

uint surface_cell_hash(ivec3 cell) {
    uint hash = uint(cell.x) * 374761393u;
    hash ^= uint(cell.y) * 668265263u;
    hash ^= uint(cell.z) * 2147483647u;
    hash = (hash ^ (hash >> 13u)) * 1274126177u;
    return hash ^ (hash >> 16u);
}

void vertex() {
    surface_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    surface_world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
}

void fragment() {
    vec4 tex = texture(atlas, UV);
    if (COLOR.a < 0.5) {
        ivec3 cell = ivec3(floor(surface_world_position - surface_world_normal * 0.01));
        float tone = float(surface_cell_hash(cell) & 65535u) / 65535.0 * 2.0 - 1.0;
        tex.rgb *= 1.0 + tone * 0.035;
    }
    float sky = clamp(COLOR.r * sky_light_energy, 0.0, 1.0);
    float block = clamp(COLOR.g, 0.0, 1.0);
    float shadowed_block = block * (1.0 - sky);
    float shade = COLOR.b;
    vec3 warm = vec3(1.0, 0.68, 0.38);
    vec3 illumination = vec3(0.035) + vec3(1.0, 0.98, 0.92) * sky + warm * shadowed_block * 0.42;
    ALBEDO = tex.rgb * illumination * shade * tint.rgb;
    EMISSION = tex.rgb * warm * shadowed_block * 0.08;
    ALPHA = tex.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
    float encoded_family = floor(COLOR.a + 0.5);
    float wet = step(7.5, encoded_family);
    float family = mod(encoded_family, 8.0);
    vec3 response = surface_response(family);
    ALBEDO *= mix(vec3(1.0), vec3(0.80, 0.86, 0.88), wet);
    ROUGHNESS = mix(response.x, 0.64, wet * 0.70);
    METALLIC = response.y;
    SPECULAR = mix(response.z, 0.34, wet * 0.70);
}
";

    private const string LitGlassShader = @"
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque;

uniform sampler2D atlas : source_color, filter_nearest, repeat_disable;
uniform float sky_light_energy = 1.0;

uniform vec3 response_stone = vec3(0.8, 0.0, 0.2);
uniform vec3 response_wood = vec3(0.8, 0.0, 0.2);
uniform vec3 response_soil = vec3(0.8, 0.0, 0.2);
uniform vec3 response_ore = vec3(0.8, 0.0, 0.2);
uniform vec3 response_foliage = vec3(0.8, 0.0, 0.2);
uniform vec3 response_glass = vec3(0.8, 0.0, 0.2);
uniform vec3 response_metal = vec3(0.8, 0.0, 0.2);

vec3 surface_response(float family) {
    if (family > 5.5) return response_metal;
    if (family > 4.5) return response_glass;
    if (family > 3.5) return response_foliage;
    if (family > 2.5) return response_ore;
    if (family > 1.5) return response_soil;
    if (family > 0.5) return response_wood;
    return response_stone;
}

void fragment() {
    vec4 tex = texture(atlas, UV);
    float sky = clamp(COLOR.r * sky_light_energy, 0.0, 1.0);
    float block = clamp(COLOR.g, 0.0, 1.0);
    float shadowed_block = block * (1.0 - sky);
    float shade = COLOR.b;
    vec3 warm = vec3(1.0, 0.72, 0.46);
    vec3 illumination = vec3(0.05) + vec3(1.0, 0.98, 0.92) * sky + warm * shadowed_block * 0.4;
    ALBEDO = tex.rgb * illumination * shade;
    EMISSION = tex.rgb * warm * shadowed_block * 0.08;
    ALPHA = tex.a * 0.58;
    vec3 response = surface_response(COLOR.a);
    ROUGHNESS = response.x;
    METALLIC = response.y;
    SPECULAR = response.z;
}
";

    /// <summary>Opaque atlas material; vertex channels carry sky, block light and AO.</summary>
    public static ShaderMaterial MakeOpaqueMaterial()
    {
        ShaderMaterial material = new() { Shader = new Shader { Code = LitAtlasShader } };
        material.SetShaderParameter("atlas", AtlasTexture());
        material.SetShaderParameter("sky_light_energy", 1.0f);
        material.SetShaderParameter("tint", Colors.White);
        MaterialResponse.ApplyTo(material);
        return material;
    }

    /// <summary>Alpha-blended glass with baked voxel-light channels.</summary>
    public static ShaderMaterial MakeGlassMaterial()
    {
        ShaderMaterial material = new() { Shader = new Shader { Code = LitGlassShader } };
        material.SetShaderParameter("atlas", AtlasTexture());
        material.SetShaderParameter("sky_light_energy", 1.0f);
        MaterialResponse.ApplyTo(material);
        return material;
    }

    /// <summary>
    /// Greedy-mesh material: one texture-array layer per tile, so merged
    /// multi-block quads can tile their texture with UV fract() — impossible
    /// with a classic atlas (a merged quad would sample neighbouring tiles).
    /// Uses the same C#-authored response palette and lighting channels as the
    /// atlas shader; only the texture lookup differs for merged quads.
    /// </summary>
    private const string OpaqueArrayShader = @"
shader_type spatial;
render_mode cull_back, depth_draw_opaque, alpha_to_coverage;

uniform sampler2DArray tiles : source_color, filter_nearest_mipmap;
uniform vec4 tint = vec4(1.0);   // wet-ground darkening (Weather)
uniform float sky_light_energy = 1.0;

uniform vec3 response_stone = vec3(0.8, 0.0, 0.2);
uniform vec3 response_wood = vec3(0.8, 0.0, 0.2);
uniform vec3 response_soil = vec3(0.8, 0.0, 0.2);
uniform vec3 response_ore = vec3(0.8, 0.0, 0.2);
uniform vec3 response_foliage = vec3(0.8, 0.0, 0.2);
uniform vec3 response_glass = vec3(0.8, 0.0, 0.2);
uniform vec3 response_metal = vec3(0.8, 0.0, 0.2);

vec3 surface_response(float family) {
    if (family > 5.5) return response_metal;
    if (family > 4.5) return response_glass;
    if (family > 3.5) return response_foliage;
    if (family > 2.5) return response_ore;
    if (family > 1.5) return response_soil;
    if (family > 0.5) return response_wood;
    return response_stone;
}

varying vec2 tile_uv;
varying float tile_layer;
varying vec3 surface_world_position;
varying vec3 surface_world_normal;

uint surface_cell_hash(ivec3 cell) {
    uint hash = uint(cell.x) * 374761393u;
    hash ^= uint(cell.y) * 668265263u;
    hash ^= uint(cell.z) * 2147483647u;
    hash = (hash ^ (hash >> 13u)) * 1274126177u;
    return hash ^ (hash >> 16u);
}

void vertex() {
    tile_uv = UV;
    tile_layer = UV2.x;
    surface_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    surface_world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
}

void fragment() {
    vec4 tex = texture(tiles, vec3(fract(tile_uv), tile_layer));
    if (COLOR.a < 0.5) {
        ivec3 cell = ivec3(floor(surface_world_position - surface_world_normal * 0.01));
        float tone = float(surface_cell_hash(cell) & 65535u) / 65535.0 * 2.0 - 1.0;
        tex.rgb *= 1.0 + tone * 0.035;
    }
    float sky = clamp(COLOR.r * sky_light_energy, 0.0, 1.0);
    float block = clamp(COLOR.g, 0.0, 1.0);
    float shadowed_block = block * (1.0 - sky);
    float shade = COLOR.b;
    vec3 warm = vec3(1.0, 0.68, 0.38);
    vec3 illumination = vec3(0.035) + vec3(1.0, 0.98, 0.92) * sky + warm * shadowed_block * 0.42;
    ALBEDO = tex.rgb * illumination * shade * tint.rgb;
    EMISSION = tex.rgb * warm * shadowed_block * 0.08;
    ALPHA = tex.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
    float encoded_family = floor(COLOR.a + 0.5);
    float wet = step(7.5, encoded_family);
    float family = mod(encoded_family, 8.0);
    vec3 response = surface_response(family);
    ALBEDO *= mix(vec3(1.0), vec3(0.80, 0.86, 0.88), wet);
    ROUGHNESS = mix(response.x, 0.64, wet * 0.70);
    METALLIC = response.y;
    SPECULAR = mix(response.z, 0.34, wet * 0.70);
}
";

    public static ShaderMaterial MakeArrayOpaqueMaterial()
    {
        Shader shader = new()
        {
            Code = OpaqueArrayShader,
        };
        ShaderMaterial m = new()
        {
            Shader = shader,
        };
        m.SetShaderParameter("tiles", TileArrayTexture());
        MaterialResponse.ApplyTo(m);
        return m;
    }

    private static readonly Lazy<Texture2DArray> TileArrayLazy = new(MakeTileArray);

    /// <summary>One array layer per atlas tile (built from the atlas image once).</summary>
    public static Texture2DArray TileArrayTexture() => TileArrayLazy.Value;

    private static Texture2DArray MakeTileArray()
    {
        Image atlas = AtlasTexture().GetImage();
        Godot.Collections.Array<Image> layers = [];
        for (int tile = 0; tile < AtlasCols * AtlasCols; tile++)
        {
            Image slice = Image.CreateEmpty(TilePx, TilePx, false, Image.Format.Rgba8);
            slice.BlitRect(
                    atlas,
                    new Rect2I((tile % AtlasCols) * TilePx, (tile / AtlasCols) * TilePx, TilePx, TilePx),
                    Vector2I.Zero);
            // Generate each tile's mip chain independently; atlas mipmaps would bleed across tile borders.
            Error mipmapError = slice.GenerateMipmaps();
            if (mipmapError != Error.Ok)
                GD.PushWarning($"minecraft: mipmap generation failed for tile {tile} ({mipmapError})");
            layers.Add(slice);
        }

        Texture2DArray arr = new();
        Error err = arr.CreateFromImages(layers);
        if (err != Error.Ok)
        {
            GD.PushWarning($"minecraft: tile texture-array build failed ({err})");
        }

        return arr;
    }

    private const string LitLavaShader = @"
shader_type spatial;
render_mode cull_back, depth_draw_opaque, specular_disabled;

uniform sampler2D atlas : source_color, filter_nearest, repeat_disable;
uniform float sky_light_energy = 1.0;

void fragment() {
    vec4 tex = texture(atlas, UV);
    float sky = COLOR.r * sky_light_energy;
    float block = COLOR.g;
    float shade = COLOR.b;
    vec3 warm = vec3(1.0, 0.48, 0.12);
    vec3 illumination = vec3(0.03) + vec3(1.0, 0.94, 0.82) * sky + warm * block * 0.35;
    ALBEDO = tex.rgb * illumination * shade;
    EMISSION = tex.rgb * (warm * 0.9 + warm * block * 0.7);
    ALPHA = tex.a;
    ROUGHNESS = 1.0;
}
";

    public static ShaderMaterial MakeLavaMaterial()
    {
        ShaderMaterial material = new() { Shader = new Shader { Code = LitLavaShader } };
        material.SetShaderParameter("atlas", AtlasTexture());
        material.SetShaderParameter("sky_light_energy", 1.0f);
        return material;
    }

    /// <summary>Animated water (custom shader); falls back to glass if the shader is gone.</summary>
    public static Material MakeWaterMaterial()
    {
        Shader? shader = WaterShaderLazy.Value;
        if (shader == null)
        {
            return MakeGlassMaterial();
        }

        ShaderMaterial m = new()
        {
            Shader = shader,
        };
        m.SetShaderParameter("atlas_texture", AtlasTexture());
        m.SetShaderParameter("sky_light_energy", 1.0f);
        return m;
    }

    // -----------------------------------------------------------------------
    // Atlas painting (all patterns scale with TilePx)
    // -----------------------------------------------------------------------

    private static Image BuildAtlasImage()
    {
        int size = AtlasCols * TilePx;
        Image img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        RandomNumberGenerator rng = new()
        {
            Seed = 90210,
        };
        PaintGrassTop(img, rng);
        PaintGrassSide(img, rng);
        PaintDirt(img, TDirt, rng);
        PaintStone(img, TStone, rng);
        PaintSand(img, rng);
        PaintLogSide(img, rng);
        PaintLogTop(img, rng);
        PaintLeaves(img, TLeaves, new Color(0.20f, 0.42f, 0.15f), new Color(0.13f, 0.29f, 0.10f),
                new Color(0.28f, 0.52f, 0.21f), rng);
        PaintWater(img, rng);
        PaintPlanks(img, TPlanks, rng);
        PaintCobblestone(img, rng);
        PaintBrick(img, rng);
        PaintOre(img, TCoalOre, new Color(0.07f, 0.07f, 0.08f), rng);
        PaintOre(img, TIronOre, new Color(0.86f, 0.60f, 0.42f), rng);
        PaintGlass(img);
        PaintBedrock(img, rng);
        PaintSnow(img, rng);
        PaintSnowSide(img, rng);
        PaintGravel(img, rng);
        PaintOre(img, TGoldOre, new Color(0.95f, 0.78f, 0.25f), rng);
        PaintOre(img, TDiamondOre, new Color(0.35f, 0.90f, 0.90f), rng);
        PaintBirchSide(img, rng);
        PaintBirchTop(img, rng);
        PaintLeaves(img, TBirchLeaves, new Color(0.38f, 0.58f, 0.26f), new Color(0.28f, 0.45f, 0.18f),
                new Color(0.50f, 0.68f, 0.32f), rng);
        PaintLeaves(img, TSpruceLeaves, new Color(0.16f, 0.34f, 0.25f), new Color(0.11f, 0.25f, 0.19f),
                new Color(0.23f, 0.42f, 0.31f), rng);
        PaintLeaves(img, TJungleLeaves, new Color(0.24f, 0.46f, 0.18f), new Color(0.17f, 0.36f, 0.13f),
                new Color(0.31f, 0.53f, 0.23f), rng);
        PaintLeaves(img, TSwampLeaves, new Color(0.34f, 0.43f, 0.19f), new Color(0.26f, 0.34f, 0.14f),
                new Color(0.42f, 0.50f, 0.25f), rng);
        PaintRiverReeds(img, TRiverReeds, rng);
        PaintCactusSide(img, rng);
        PaintCactusTop(img, rng);
        PaintCraftingTop(img, rng);
        PaintCraftingSide(img, rng);
        PaintWool(img, rng);
        PaintLava(img, rng);
        PaintPlant(img, TTallGrass, rng, new Color(0.30f, 0.55f, 0.20f), new Color(0.20f, 0.40f, 0.13f), 0.90f, false);
        PaintPlant(img, TFlowerRed, rng, new Color(0.30f, 0.52f, 0.18f), new Color(0.86f, 0.20f, 0.18f), 0.85f, true);
        PaintPlant(img, TFlowerYellow, rng, new Color(0.30f, 0.52f, 0.18f), new Color(0.94f, 0.82f, 0.20f), 0.85f, true);
        PaintTorch(img, rng);
        PaintChest(img, rng);
        PaintFurnace(img, rng);
        PaintFarmland(img, rng);
        PaintWheat(img, TWheat0, rng, 0.35f, false);
        PaintWheat(img, TWheat1, rng, 0.65f, false);
        PaintWheat(img, TWheat2, rng, 0.95f, true);
        PaintBed(img, rng);
        PaintLadder(img);
        PaintEnchantingTop(img, rng);
        PaintEnchantingSide(img, rng);
        PaintBuildingTexture(img, TPlanksBuilding, new Color(0.49f, 0.32f, 0.18f), rng, 8);
        PaintBuildingTexture(img, TCobbleBuilding, new Color(0.43f, 0.44f, 0.45f), rng, 11);
        PaintBuildingTexture(img, TBrickBuilding, new Color(0.58f, 0.25f, 0.19f), rng, 7);
        PaintPlanksTop(img, TPlanksTop, rng);
        PaintPlanksSide(img, TPlanksSide, rng);
        PaintPlanksEnd(img, TPlanksEnd, rng);
        PaintWeatheredCobblestone(img, TMossyCobblestone,
                new Color(0.43f, 0.46f, 0.41f), rng, true);
        PaintWeatheredCobblestone(img, TWeatheredCobblestone,
                new Color(0.49f, 0.49f, 0.47f), rng, false);
        PaintWeatheredBrick(img, TCrackedBrick, rng, false);
        PaintWeatheredBrick(img, TMossyBrick, rng, true);
        return img;
    }

    private static void Px(Image img, int tile, int x, int y, Color c) =>
        img.SetPixel((tile % AtlasCols) * TilePx + x, (tile / AtlasCols) * TilePx + y, c);

    private static Color Jitter(RandomNumberGenerator rng, Color c, float amount)
    {
        float d = rng.RandfRange(-amount, amount);
        return new Color(Mathf.Clamp(c.R + d, 0.0f, 1.0f), Mathf.Clamp(c.G + d, 0.0f, 1.0f),
                Mathf.Clamp(c.B + d, 0.0f, 1.0f), c.A);
    }

    private static void FillNoise(Image img, int tile, Color baseColor, float amount, float alpha,
            RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y += TextureDetailPx)
        {
            for (int x = 0; x < TilePx; x += TextureDetailPx)
            {
                Color c = Jitter(rng, baseColor, amount);
                c.A *= alpha;
                for (int dy = 0; dy < TextureDetailPx; dy++)
                for (int dx = 0; dx < TextureDetailPx; dx++)
                    Px(img, tile, x + dx, y + dy, c);
            }
        }
    }

    private static void PaintBuildingTexture(Image img, int tile, Color baseColor,
            RandomNumberGenerator rng, int courseHeight)
    {
        FillNoise(img, tile, baseColor, 0.045f, 1.0f, rng);
        Color seam = new(Mathf.Max(0.0f, baseColor.R * 0.48f),
                Mathf.Max(0.0f, baseColor.G * 0.48f), Mathf.Max(0.0f, baseColor.B * 0.48f));
        Color highlight = Jitter(rng, baseColor, 0.10f);
        for (int y = 0; y < TilePx; y++)
        {
            bool horizontal = y % courseHeight == 0;
            if (horizontal)
                for (int x = 0; x < TilePx; x++) Px(img, tile, x, y, seam);
        }

        for (int y = 0; y < TilePx; y += courseHeight)
        {
            int offset = (y / courseHeight % 2) * (courseHeight / 2);
            for (int x = offset; x < TilePx; x += courseHeight)
            {
                for (int dy = 1; dy < courseHeight - 1 && y + dy < TilePx; dy++)
                {
                    Px(img, tile, x, y + dy, seam);
                    if (x + 1 < TilePx) Px(img, tile, x + 1, y + dy, seam);
                }
            }
        }

        for (int i = 0; i < 48; i++)
            Px(img, tile, rng.RandiRange(1, TilePx - 2), rng.RandiRange(1, TilePx - 2), Jitter(rng, highlight, 0.035f));
    }

    /// <summary>
    /// Original 4x4 stonework pattern with broad 8px blocks. Moss sits in
    /// irregular seams so it reads as material aging rather than noisy tint.
    /// </summary>
    private static void PaintWeatheredCobblestone(Image img, int tile, Color baseColor,
            RandomNumberGenerator rng, bool mossy)
    {
        const int cell = 8;
        Color mortar = new(0.25f, 0.27f, 0.25f);
        Color mossDark = new(0.20f, 0.30f, 0.15f);
        Color mossLight = new(0.34f, 0.45f, 0.22f);
        for (int cy = 0; cy < 4; cy++)
        for (int cx = 0; cx < 4; cx++)
        {
            Color stone = Jitter(rng, baseColor, 0.075f);
            int ox = cx * cell;
            int oy = cy * cell;
            for (int py = 0; py < cell; py++)
            for (int px = 0; px < cell; px++)
            {
                bool joint = px == 0 || py == 0;
                Color color = joint ? mortar : Jitter(rng, stone, 0.025f);
                if (!joint && (px == 1 || py == 1))
                    color = color.Darkened(0.08f);
                Px(img, tile, ox + px, oy + py, color);
            }

            if (mossy && rng.Randf() < 0.42f)
            {
                int width = rng.RandiRange(2, 4);
                int height = rng.RandiRange(2, 3);
                int mx = ox + rng.RandiRange(1, cell - width - 1);
                int my = oy + rng.RandiRange(1, cell - height - 1);
                for (int py = 0; py < height; py++)
                for (int px = 0; px < width; px++)
                    if (rng.Randf() > 0.18f)
                        Px(img, tile, mx + px, my + py,
                                Jitter(rng, (px + py) % 3 == 0 ? mossLight : mossDark, 0.035f));
            }
            else if (!mossy && rng.Randf() < 0.28f)
            {
                int chipX = ox + rng.RandiRange(1, 6);
                int chipY = oy + rng.RandiRange(1, 6);
                Px(img, tile, chipX, chipY, mortar);
                Px(img, tile, chipX + 1, chipY, stone.Darkened(0.18f));
            }
        }
    }

    /// <summary>Muted brick courses with fine cracks or moss-grown mortar.</summary>
    private static void PaintWeatheredBrick(Image img, int tile,
            RandomNumberGenerator rng, bool mossy)
    {
        Color brick = new(0.54f, 0.31f, 0.25f);
        Color mortar = new(0.48f, 0.45f, 0.40f);
        Color mossDark = new(0.20f, 0.32f, 0.17f);
        Color mossLight = new(0.35f, 0.46f, 0.23f);
        for (int y = 0; y < TilePx; y++)
        {
            int row = y / 8;
            int offset = row % 2 == 0 ? 0 : 8;
            for (int x = 0; x < TilePx; x++)
            {
                bool joint = y % 8 == 0 || (x + offset) % 16 == 0;
                Color color = joint ? mortar : Jitter(rng, brick, 0.055f);
                if (!joint && (y % 8 == 1 || (x + offset) % 16 == 1))
                    color = color.Darkened(0.10f);
                Px(img, tile, x, y, color);
            }
        }

        if (mossy)
        {
            for (int row = 0; row < 4; row++)
            {
                if (rng.Randf() > 0.68f)
                    continue;
                int length = rng.RandiRange(3, 6);
                int x = rng.RandiRange(1, TilePx - length - 1);
                int y = row * 8 + rng.RandiRange(0, 2);
                for (int i = 0; i < length; i++)
                {
                    int px = x + i;
                    Px(img, tile, px, y, Jitter(rng, i % 3 == 0 ? mossLight : mossDark, 0.025f));
                    if (y + 1 < TilePx && rng.Randf() < 0.65f)
                        Px(img, tile, px, y + 1, mossDark);
                }
            }
        }
        else
        {
            for (int crack = 0; crack < 5; crack++)
            {
                int x = rng.RandiRange(2, TilePx - 4);
                int y = rng.RandiRange(3, TilePx - 5);
                Color dark = new(0.28f, 0.25f, 0.23f);
                for (int step = 0; step < 3; step++)
                {
                    Px(img, tile, x + step, y + step / 2, dark);
                    if (step == 1)
                        Px(img, tile, x + step + 1, y + step / 2, dark);
                }
            }
        }
    }

    private static void Speckle(Image img, int tile, Color c, int count, float amount,
            RandomNumberGenerator rng)
    {
        int clusterCount = Math.Max(1, count / (TextureDetailPx * TextureDetailPx));
        for (int i = 0; i < clusterCount; i++)
        {
            int x = rng.RandiRange(0, TilePx / TextureDetailPx - 1) * TextureDetailPx;
            int y = rng.RandiRange(0, TilePx / TextureDetailPx - 1) * TextureDetailPx;
            Color speckle = Jitter(rng, c, amount);
            for (int dy = 0; dy < TextureDetailPx; dy++)
            for (int dx = 0; dx < TextureDetailPx; dx++)
                Px(img, tile, x + dx, y + dy, speckle);
        }
    }

    private static void PaintGrassTop(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TGrassTop, new Color(0.34f, 0.56f, 0.24f), 0.055f, 1.0f, rng);
        for (int i = 0; i < 176; i++)
        {
            Color c = new(0.29f, 0.49f, 0.20f);
            if (rng.Randf() < 0.5f)
            {
                c = new Color(0.39f, 0.63f, 0.28f);
            }

            Speckle(img, TGrassTop, c, 1, 0.02f, rng);
        }
    }

    private static void PaintGrassSide(Image img, RandomNumberGenerator rng)
    {
        PaintDirt(img, TGrassSide, rng);
        Span<int> edgeDepth = stackalloc int[TilePx];
        for (int x = 0; x < TilePx; x++)
        {
            // A deeper, irregular turf cap remains legible when the side is seen
            // obliquely or reduced to a small number of screen pixels.
            int depth = TilePx / 4 + rng.RandiRange(0, TilePx / 8);
            edgeDepth[x] = depth;
            for (int y = 0; y < depth; y++)
            {
                Color grass = y < depth - 2
                        ? new Color(0.32f, 0.56f, 0.22f)
                        : new Color(0.25f, 0.43f, 0.17f);
                Px(img, TGrassSide, x, y, Jitter(rng, grass, 0.055f));
            }
        }

        // One broken dark-soil seam and sparse roots anchor the turf edge;
        // avoid a hard, perfectly horizontal stripe on long exposed banks.
        for (int x = 0; x < TilePx; x++)
        {
            int edge = edgeDepth[x];
            Px(img, TGrassSide, x, edge, Jitter(rng, new Color(0.25f, 0.27f, 0.13f), 0.045f));
            if (x % 5 == 0 && edge + 1 < TilePx)
            {
                Px(img, TGrassSide, x, edge + 1, new Color(0.31f, 0.37f, 0.18f));
            }
        }
    }

    private static void PaintDirt(Image img, int tile, RandomNumberGenerator rng)
    {
        FillNoise(img, tile, new Color(0.45f, 0.34f, 0.24f), 0.045f, 1.0f, rng);
        Speckle(img, tile, new Color(0.37f, 0.27f, 0.18f), 144, 0.02f, rng);
    }

    private static void PaintStone(Image img, int tile, RandomNumberGenerator rng)
    {
        FillNoise(img, tile, new Color(0.54f, 0.54f, 0.56f), 0.04f, 1.0f, rng);
        for (int i = 0; i < 120; i++)
        {
            Color c = new(0.46f, 0.46f, 0.48f);
            if (rng.Randf() < 0.5f)
            {
                c = new Color(0.60f, 0.60f, 0.62f);
            }

            Speckle(img, tile, c, 1, 0.02f, rng);
        }
    }

    private static void PaintSand(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TSand, new Color(0.86f, 0.81f, 0.61f), 0.03f, 1.0f, rng);
        Speckle(img, TSand, new Color(0.79f, 0.73f, 0.53f), 104, 0.02f, rng);
    }

    private static void PaintSnow(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TSnow, new Color(0.91f, 0.93f, 0.97f), 0.02f, 1.0f, rng);
        Speckle(img, TSnow, new Color(0.84f, 0.88f, 0.94f), 90, 0.015f, rng);
        Speckle(img, TSnow, new Color(0.97f, 0.98f, 1.0f), 60, 0.01f, rng);
    }

    private static void PaintSnowSide(Image img, RandomNumberGenerator rng)
    {
        PaintDirt(img, TSnowSide, rng);
        for (int x = 0; x < TilePx; x++)
        {
            int depth = 6 + rng.RandiRange(0, 5);
            for (int y = 0; y < depth; y++)
            {
                Px(img, TSnowSide, x, y, Jitter(rng, new Color(0.91f, 0.93f, 0.97f), 0.02f));
            }
        }
    }

    private static void PaintGravel(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TGravel, new Color(0.52f, 0.50f, 0.48f), 0.06f, 1.0f, rng);
        for (int i = 0; i < 26; i++)
        {
            int cx = rng.RandiRange(0, TilePx - 4);
            int cy = rng.RandiRange(0, TilePx - 4);
            int w = 2 + rng.RandiRange(0, 2);
            float b = rng.RandfRange(0.36f, 0.68f);
            for (int y = 0; y < w; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Px(img, TGravel, (cx + x) % TilePx, (cy + y) % TilePx,
                            Jitter(rng, new Color(b, b * 0.98f, b * 0.95f), 0.03f));
                }
            }
        }
    }

    private static void PaintLogSide(Image img, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                float flow = Mathf.Sin(y * 0.19f + x * 0.11f) * 0.7f
                        + Mathf.Sin(y * 0.43f - x * 0.07f) * 0.3f;
                float groove = Mathf.Sin(x * 1.35f + flow * 0.42f);
                float shade = groove * 0.018f + Mathf.Sin(x * 0.31f + flow) * 0.012f;
                Color c = new(0.43f + shade, 0.315f + shade * 0.82f, 0.195f + shade * 0.58f);
                Px(img, TLogSide, x, y, Jitter(rng, c, 0.012f));
            }
        }

        // Long, irregular bark fissures reinforce the trunk's vertical direction.
        for (int x = 2; x < TilePx; x += 5)
        {
            float phase = rng.RandfRange(0.0f, Mathf.Tau);
            for (int y = 0; y < TilePx; y++)
            {
                int drift = Mathf.RoundToInt(Mathf.Sin(y * 0.16f + phase) * 0.7f);
                int px = Math.Clamp(x + drift, 0, TilePx - 1);
                if (rng.Randf() < 0.78f)
                    Px(img, TLogSide, px, y, new Color(0.32f, 0.235f, 0.145f));
                if (px + 1 < TilePx && rng.Randf() < 0.22f)
                    Px(img, TLogSide, px + 1, y, new Color(0.51f, 0.385f, 0.235f));
            }
        }

        // Sparse knots stay small and low-contrast so they do not turn into
        // noisy specks when a trunk is viewed from a distance.
        for (int knot = 0; knot < 2; knot++)
        {
            int cx = rng.RandiRange(5, TilePx - 6);
            int cy = rng.RandiRange(4, TilePx - 5);
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                float d = new Vector2(x, y).Length();
                if (d <= 2.2f)
                    Px(img, TLogSide, cx + x, cy + y,
                            d < 0.9f ? new Color(0.31f, 0.22f, 0.13f)
                                    : new Color(0.47f, 0.35f, 0.21f));
            }
        }
    }

    private static void PaintLogTop(Image img, RandomNumberGenerator rng) =>
        PaintLogEndGrain(img, TLogTop,
                new Color(0.60f, 0.45f, 0.29f),
                new Color(0.40f, 0.29f, 0.17f),
                new Color(0.73f, 0.56f, 0.36f), rng);

    private static void PaintLogEndGrain(Image img, int tile, Color sapwood,
            Color ringDark, Color ringLight, RandomNumberGenerator rng)
    {
        float center = (TilePx - 1) * 0.5f;
        for (int y = 0; y < TilePx; y++)
        for (int x = 0; x < TilePx; x++)
        {
            float dx = x - center;
            float dy = y - center;
            float angle = Mathf.Atan2(dy, dx);
            float radius = Mathf.Sqrt(dx * dx * 0.94f + dy * dy * 1.08f);
            float warp = Mathf.Sin(angle * 3.0f + 0.45f) * 0.38f
                    + Mathf.Sin(angle * 5.0f - 0.8f) * 0.16f;
            float rings = Mathf.Sin((radius + warp) * 1.75f + Mathf.Sin(angle * 2.0f) * 0.12f);
            Color c = sapwood.Lerp(rings < 0.0f ? ringDark : ringLight, 0.42f + Mathf.Abs(rings) * 0.18f);
            if (radius > TilePx * 0.5f - 2.2f)
                c = new Color(0.31f, 0.225f, 0.14f);
            else if (radius < 1.15f)
                c = new Color(0.34f, 0.245f, 0.15f);

            // Three fine radial checks and faint rays keep the rings organic.
            float ray = Mathf.Abs(Mathf.Sin(angle * 3.0f + 0.18f));
            if (radius > 3.0f && radius < 13.8f && ray < 0.035f)
                c = c.Lerp(ringDark, 0.38f);
            else if (radius > 4.0f && ray < 0.11f)
                c = c.Lerp(ringLight, 0.12f);

            Px(img, tile, x, y, Jitter(rng, c, 0.012f));
        }
    }

    /// <summary>Soft white wool: fluffy noise with scattered curl marks.</summary>
    private static void PaintWool(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TWool, new Color(0.93f, 0.92f, 0.89f), 0.025f, 1.0f, rng);
        for (int i = 0; i < 26; i++)
        {
            int cx = rng.RandiRange(1, TilePx - 3);
            int cy = rng.RandiRange(1, TilePx - 3);
            for (int k = 0; k < 3; k++)
            {
                Px(img, TWool, (cx + k) % TilePx, cy, new Color(0.84f, 0.83f, 0.80f));
            }
        }
    }

    private static void PaintBirchSide(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TBirchSide, new Color(0.87f, 0.86f, 0.80f), 0.02f, 1.0f, rng);
        for (int x = 2; x < TilePx; x += 7)
        for (int y = 0; y < TilePx; y++)
        {
            if (rng.Randf() < 0.55f)
                Px(img, TBirchSide, x, y, new Color(0.81f, 0.80f, 0.74f));
        }
        for (int i = 0; i < 12; i++)
        {
            int x = rng.RandiRange(0, TilePx - 1);
            int y = rng.RandiRange(0, TilePx - 1);
            int w = 2 + rng.RandiRange(0, 4);
            for (int k = 0; k < w; k++)
            {
                Px(img, TBirchSide, (x + k) % TilePx, y, new Color(0.25f, 0.24f, 0.22f));
            }
        }
    }

    private static void PaintBirchTop(Image img, RandomNumberGenerator rng) =>
        PaintBirchEndGrain(img, rng);

    private static void PaintBirchEndGrain(Image img, RandomNumberGenerator rng)
    {
        float center = (TilePx - 1) * 0.5f;
        Color pale = new(0.88f, 0.85f, 0.76f);
        Color dark = new(0.66f, 0.62f, 0.52f);
        Color light = new(0.95f, 0.92f, 0.84f);
        for (int y = 0; y < TilePx; y++)
        for (int x = 0; x < TilePx; x++)
        {
            float dx = x - center;
            float dy = y - center;
            float angle = Mathf.Atan2(dy, dx);
            float radius = Mathf.Sqrt(dx * dx + dy * dy * 1.06f);
            float warp = Mathf.Sin(angle * 3.0f + 0.3f) * 0.34f
                    + Mathf.Sin(angle * 5.0f) * 0.14f;
            float rings = Mathf.Sin((radius + warp) * 1.65f);
            Color c = pale.Lerp(rings < 0.0f ? dark : light, 0.28f + Mathf.Abs(rings) * 0.2f);
            if (radius > TilePx * 0.5f - 2.0f)
                c = new Color(0.72f, 0.68f, 0.58f);
            else if (radius < 1.15f)
                c = new Color(0.60f, 0.56f, 0.47f);

            if (radius > 3.0f && radius < 13.8f
                    && Mathf.Abs(Mathf.Sin(angle * 3.0f + 0.18f)) < 0.032f)
                c = c.Lerp(dark, 0.34f);
            Px(img, TBirchTop, x, y, Jitter(rng, c, 0.01f));
        }
    }

    /// <summary>Procedural emergent reed stalks with transparent gaps and warm seed heads.</summary>
    private static void PaintRiverReeds(Image img, int tile, RandomNumberGenerator rng)
    {
        Color stem = new(0.26f, 0.43f, 0.18f);
        Color stemLight = new(0.43f, 0.62f, 0.25f);
        Color stemShade = new(0.16f, 0.31f, 0.15f);
        Color seedHead = new(0.43f, 0.25f, 0.12f);
        Color seedLight = new(0.62f, 0.39f, 0.19f);
        (int X, int Top, int Lean)[] stalks = [(6, 11, 1), (15, 4, -1), (25, 9, 1)];
        foreach ((int originX, int top, int lean) in stalks)
        {
            int stemTop = top + (lean == -1 ? 0 : rng.RandiRange(0, 2));
            for (int y = stemTop; y < TilePx; y++)
            {
                int x = originX + (y - stemTop) / 12 * lean;
                if ((uint)x >= TilePx) continue;
                Px(img, tile, x, y, stemShade);
                Px(img, tile, x + 1, y, Jitter(rng, stem, 0.025f));
                if (y > stemTop + 5 && y < TilePx - 4 && (y + originX) % 5 == 0)
                {
                    int leafX = x + (lean > 0 ? 2 : -2);
                    Px(img, tile, leafX, y - 1, stemLight);
                    Px(img, tile, leafX + (lean > 0 ? 1 : -1), y, stem);
                }
            }

            // Warm, narrow seed heads remain identifiable after mip reduction.
            int headX = originX + (TilePx - 1 - stemTop) / 12 * lean;
            for (int y = Math.Max(0, stemTop - 5); y < stemTop; y++)
            for (int dx = -1; dx <= 1; dx++)
                Px(img, tile, headX + dx, y, dx == -1 ? seedLight : seedHead);
        }
    }

    /// <summary>
    /// Cutout leaves: dense colours with ~15% fully transparent holes so the
    /// canopy reads as foliage (alpha scissor on the opaque material).
    /// </summary>
    private static void PaintLeaves(Image img, int tile, Color baseColor, Color dark, Color light,
            RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        for (int x = 0; x < TilePx; x++)
        {
            // Keep transparent gaps irregular but clustered so distant canopies
            // read as leaf masses rather than a uniformly perforated grid.
            int clusterX = x / 4;
            int clusterY = y / 4;
            float holeChance = ((clusterX * 7 + clusterY * 11) % 5 == 0) ? 0.22f : 0.12f;
            if (rng.Randf() < holeChance)
            {
                Px(img, tile, x, y, new Color(0, 0, 0, 0));
                continue;
            }

            float palette = rng.Randf();
            Color source = palette < 0.18f ? dark : palette > 0.84f ? light : baseColor;
            Color c = Jitter(rng, source, source == baseColor ? 0.045f : 0.025f);
            Px(img, tile, x, y, c);
        }
    }

    private static void PaintWater(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TWater, new Color(0.14f, 0.33f, 0.70f), 0.03f, 0.82f, rng);
        for (int i = 0; i < 60; i++)
        {
            Px(img, TWater, rng.RandiRange(0, TilePx - 1), rng.RandiRange(0, TilePx - 1),
                    new Color(0.24f, 0.50f, 0.88f, 0.88f));
        }
    }

    private static void PaintPlanks(Image img, int tile, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            int band = y / 8;
            for (int x = 0; x < TilePx; x++)
            {
                Color c = new(0.63f, 0.47f, 0.28f);
                if (y % 8 == 7)
                {
                    c = new Color(0.44f, 0.32f, 0.18f);
                }
                else if ((x + band * 10) % TilePx == 0)
                {
                    c = new Color(0.48f, 0.35f, 0.20f);
                }

                Px(img, tile, x, y, Jitter(rng, c, 0.025f));
            }
        }
    }

    private static void PaintPlanksTop(Image img, int tile, RandomNumberGenerator rng)
    {
        Color[] boards =
        [
            new Color(0.65f, 0.48f, 0.29f),
            new Color(0.62f, 0.45f, 0.27f),
            new Color(0.67f, 0.50f, 0.31f),
            new Color(0.63f, 0.46f, 0.28f),
        ];
        for (int y = 0; y < TilePx; y++)
        for (int x = 0; x < TilePx; x++)
        {
            int board = y / 8;
            int localY = y % 8;
            Color baseColor = boards[board];
            float grain = Mathf.Sin(y * 2.1f + Mathf.Sin(x * 0.21f + board) * 0.45f) * 0.018f;
            float fleck = Mathf.Sin(x * 0.73f + y * 0.37f) * 0.006f;
            Color c = new(baseColor.R + grain + fleck, baseColor.G + grain * 0.82f + fleck,
                    baseColor.B + grain * 0.55f + fleck * 0.7f);
            if (localY == 0 || localY == 7)
                c = new Color(0.39f, 0.275f, 0.16f);
            else if (localY == 1)
                c = c.Lerp(new Color(0.80f, 0.62f, 0.39f), 0.18f);
            Px(img, tile, x, y, Jitter(rng, c, 0.008f));
        }

        // Tiny low-contrast knots are aligned to board courses rather than
        // scattered noise, and remain readable after nearest minification.
        for (int knot = 0; knot < 2; knot++)
        {
            int cx = rng.RandiRange(5, TilePx - 6);
            int cy = rng.RandiRange(0, 3) * 8 + 4;
            for (int y = -1; y <= 1; y++)
            for (int x = -2; x <= 2; x++)
            {
                float distance = new Vector2(x / 2.0f, y).Length();
                if (distance <= 1.0f)
                    Px(img, tile, cx + x, cy + y,
                            distance < 0.3f ? new Color(0.42f, 0.30f, 0.18f)
                                    : new Color(0.71f, 0.53f, 0.32f));
            }
        }
    }

    private static void PaintPlanksSide(Image img, int tile, RandomNumberGenerator rng)
    {
        Color[] boards =
        [
            new Color(0.57f, 0.405f, 0.24f),
            new Color(0.55f, 0.39f, 0.23f),
            new Color(0.59f, 0.425f, 0.25f),
            new Color(0.56f, 0.40f, 0.235f),
        ];
        for (int y = 0; y < TilePx; y++)
        for (int x = 0; x < TilePx; x++)
        {
            int board = y / 8;
            int localY = y % 8;
            float grain = Mathf.Sin(x * 0.55f + Mathf.Sin(x * 0.12f + board) * 0.6f) * 0.012f;
            Color c = boards[board] + new Color(grain, grain * 0.8f, grain * 0.5f);
            if (localY == 0 || localY == 7)
                c = new Color(0.35f, 0.245f, 0.145f);
            else if (localY == 1)
                c = c.Lerp(new Color(0.69f, 0.49f, 0.29f), 0.1f);
            Px(img, tile, x, y, Jitter(rng, c, 0.006f));
        }

        for (int knot = 0; knot < 2; knot++)
        {
            int cx = rng.RandiRange(5, TilePx - 6);
            int cy = rng.RandiRange(0, 3) * 8 + 4;
            Px(img, tile, cx - 1, cy, new Color(0.40f, 0.28f, 0.16f));
            Px(img, tile, cx, cy, new Color(0.34f, 0.24f, 0.14f));
            Px(img, tile, cx + 1, cy, new Color(0.40f, 0.28f, 0.16f));
        }
    }

    private static void PaintPlanksEnd(Image img, int tile, RandomNumberGenerator rng)
    {
        // Four board ends occupy the same vertical courses as the side-grain tile.
        for (int y = 0; y < TilePx; y++)
        for (int x = 0; x < TilePx; x++)
        {
            int board = y / 8;
            float dx = (x - 15.5f) * 0.9f;
            float dy = (y % 8 - 3.5f) * 1.8f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float rings = Mathf.Sin(radius * 1.5f + Mathf.Sin(Mathf.Atan2(dy, dx) * 3.0f) * 0.2f);
            Color baseColor = board % 2 == 0
                    ? new Color(0.62f, 0.45f, 0.27f)
                    : new Color(0.59f, 0.42f, 0.25f);
            Color c = baseColor.Lerp(rings < 0.0f
                    ? new Color(0.43f, 0.30f, 0.18f)
                    : new Color(0.72f, 0.53f, 0.32f), 0.22f + Mathf.Abs(rings) * 0.16f);
            if (radius < 0.9f) c = new Color(0.39f, 0.275f, 0.16f);
            if (y % 8 == 0 || y % 8 == 7) c = new Color(0.36f, 0.25f, 0.15f);
            if (radius > 4.0f && radius < 7.5f
                    && Mathf.Abs(Mathf.Sin(Mathf.Atan2(dy, dx) * 2.0f)) < 0.035f)
                c = c.Lerp(new Color(0.38f, 0.265f, 0.15f), 0.28f);
            Px(img, tile, x, y, Jitter(rng, c, 0.008f));
        }
    }

    /// <summary>Crafting table top: planks with a 3x3 grid carved in.</summary>
    private static void PaintCraftingTop(Image img, RandomNumberGenerator rng)
    {
        PaintPlanks(img, TCraftingTop, rng);
        int third = TilePx / 3;
        for (int i = 1; i < 3; i++)
        {
            for (int k = 0; k < TilePx; k++)
            {
                Px(img, TCraftingTop, i * third, k, new Color(0.38f, 0.27f, 0.15f));
                Px(img, TCraftingTop, k, i * third, new Color(0.38f, 0.27f, 0.15f));
            }
        }
    }

    private static void PaintEnchantingTop(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TEnchantTop, new Color(0.12f, 0.08f, 0.22f), 0.035f, 1.0f, rng);
        for (int i = 3; i < TilePx - 3; i++)
        {
            Px(img, TEnchantTop, i, 3, new Color(0.62f, 0.36f, 0.90f));
            Px(img, TEnchantTop, i, TilePx - 4, new Color(0.62f, 0.36f, 0.90f));
            Px(img, TEnchantTop, 3, i, new Color(0.62f, 0.36f, 0.90f));
            Px(img, TEnchantTop, TilePx - 4, i, new Color(0.62f, 0.36f, 0.90f));
        }
        RectPixels(img, TEnchantTop, 11, 8, 10, 16, new Color(0.22f, 0.70f, 0.88f));
        RectPixels(img, TEnchantTop, 8, 11, 16, 10, new Color(0.22f, 0.70f, 0.88f));
        RectPixels(img, TEnchantTop, 14, 11, 4, 10, new Color(0.88f, 0.76f, 0.34f));
    }

    private static void PaintEnchantingSide(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TEnchantSide, new Color(0.16f, 0.11f, 0.25f), 0.045f, 1.0f, rng);
        for (int y = 0; y < TilePx; y += 8)
            for (int x = 0; x < TilePx; x++)
                Px(img, TEnchantSide, x, y, new Color(0.36f, 0.22f, 0.48f));
        for (int i = 4; i < 28; i += 6)
            Px(img, TEnchantSide, i, 16, new Color(0.25f, 0.68f, 0.82f));
    }

    private static void RectPixels(Image img, int tile, int x, int y, int width, int height, Color color)
    {
        for (int py = y; py < y + height; py++)
            for (int px = x; px < x + width; px++)
                Px(img, tile, px, py, color);
    }
    /// <summary>Crafting table side: planks with a darker rim and tool marks.</summary>
    private static void PaintCraftingSide(Image img, RandomNumberGenerator rng)
    {
        PaintPlanks(img, TCraftingSide, rng);
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Px(img, TCraftingSide, x, y, Jitter(rng, new Color(0.40f, 0.29f, 0.17f), 0.03f));
            }
        }

        for (int x = 6; x < 14; x++)
        {
            Px(img, TCraftingSide, x, 12, new Color(0.72f, 0.72f, 0.74f));
        }

        for (int y = 8; y < 16; y++)
        {
            Px(img, TCraftingSide, 9, y, new Color(0.45f, 0.32f, 0.20f));
        }

        for (int x = 18; x < 27; x++)
        {
            Px(img, TCraftingSide, x, 14, new Color(0.72f, 0.72f, 0.74f));
        }
    }

    private static void PaintCobblestone(Image img, RandomNumberGenerator rng)
    {
        int cell = TilePx / 4;
        for (int cy = 0; cy < 4; cy++)
        {
            for (int cx = 0; cx < 4; cx++)
            {
                float b = rng.RandfRange(0.42f, 0.60f);
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        Color c = new(b, b, b * 1.03f);
                        if (x == 0 || y == 0)
                        {
                            c *= 0.62f;
                        }

                        Px(img, TCobblestone, cx * cell + x, cy * cell + y, Jitter(rng, c, 0.03f));
                    }
                }
            }
        }
    }

    private static void PaintBrick(Image img, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            int row = y / 8;
            int offset = 0;
            if (row % 2 == 1)
            {
                offset = 8;
            }

            for (int x = 0; x < TilePx; x++)
            {
                Color c = new(0.60f, 0.26f, 0.20f);
                if (y % 8 == 7 || (x + offset) % 16 == 0)
                {
                    c = new Color(0.72f, 0.70f, 0.65f);
                }

                Px(img, TBrick, x, y, Jitter(rng, c, 0.025f));
            }
        }
    }

    private static void PaintOre(Image img, int tile, Color oreColor, RandomNumberGenerator rng)
    {
        PaintStone(img, tile, rng);

        // Ore is a surface marking, not a light source. Larger, connected
        // angular deposits survive the atlas mip chain and remain recognizable
        // when a 32 px tile is viewed at distance. A dark edge and a restrained
        // highlight give each deposit shape without introducing emissive pixels.
        int count = 5 + rng.RandiRange(0, 2);
        for (int i = 0; i < count; i++)
        {
            int cx = rng.RandiRange(2, TilePx - 8);
            int cy = rng.RandiRange(2, TilePx - 8);
            int w = 5 + rng.RandiRange(0, 2);
            int h = 4 + rng.RandiRange(0, 2);
            Color shadow = oreColor.Lerp(new Color(0.12f, 0.13f, 0.15f), 0.38f);
            Color highlight = oreColor.Lerp(Colors.White, 0.22f);

            for (int y = -1; y <= h; y++)
            for (int x = -1; x <= w; x++)
            {
                int px = cx + x;
                int py = cy + y;
                if ((uint)px >= TilePx || (uint)py >= TilePx) continue;

                bool inside = x >= 0 && x < w && y >= 0 && y < h;
                if (!inside)
                {
                    // One-pixel mineral shadow separates the deposit from stone.
                    if (x == -1 || x == w || y == -1 || y == h)
                        Px(img, tile, px, py, Jitter(rng, shadow, 0.025f));
                    continue;
                }

                // Small stepped cuts keep the deposits crystalline rather than
                // reading as rounded blobs; the center remains broad at distance.
                bool cutCorner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1)
                        && rng.Randf() < 0.55f;
                if (cutCorner) continue;

                Color deposit = oreColor;
                if (y == 0 || (x == 1 && y > 0)) deposit = highlight;
                else if (x == w - 1 || y == h - 1) deposit = shadow;
                Px(img, tile, px, py, Jitter(rng, deposit, 0.035f));
            }
        }
    }

    private static void PaintGlass(Image img)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                // Keep the pane body nearly clear. A one-pixel cool edge and
                // restrained diagonal glint make the surface legible without
                // drawing a bright grid across large multi-block windows.
                Color c = new(0.85f, 0.95f, 1.0f, 0.12f);
                if (x == 0 || y == 0 || x == TilePx - 1 || y == TilePx - 1)
                {
                    c = new Color(0.80f, 0.93f, 1.0f, 0.34f);
                }

                Px(img, TGlass, x, y, c);
            }
        }

        for (int i = 0; i < 8; i++)
        {
            Px(img, TGlass, 6 + i, 6 + i, new Color(1, 1, 1, 0.30f));
        }
    }

    private static void PaintCactusSide(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TCactusSide, new Color(0.22f, 0.47f, 0.22f), 0.03f, 1.0f, rng);
        for (int ridge = 2; ridge < TilePx; ridge += 8)
        {
            for (int y = 0; y < TilePx; y++)
            {
                Px(img, TCactusSide, ridge, y, new Color(0.15f, 0.35f, 0.16f));
            }
        }

        for (int i = 0; i < 40; i++)
        {
            int x = rng.RandiRange(0, TilePx - 1);
            Px(img, TCactusSide, x, rng.RandiRange(0, TilePx - 1), new Color(0.75f, 0.80f, 0.55f));
        }
    }

    private static void PaintCactusTop(Image img, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                float d = new Vector2(x - TilePx / 2.0f + 0.5f, y - TilePx / 2.0f + 0.5f).Length();
                Color c = Jitter(rng, new Color(0.24f, 0.50f, 0.24f), 0.03f);
                if (d > TilePx / 2.0f - 3.0f)
                {
                    c = new Color(0.15f, 0.35f, 0.16f);
                }

                Px(img, TCactusTop, x, y, c);
            }
        }

        for (int i = 0; i < 24; i++)
        {
            Px(img, TCactusTop, rng.RandiRange(0, TilePx - 1), rng.RandiRange(0, TilePx - 1),
                    new Color(0.75f, 0.80f, 0.55f));
        }
    }

    /// <summary>Molten rock: dark crust veins over glowing orange, with bright bubbles.</summary>
    private static void PaintLava(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TLava, new Color(0.92f, 0.38f, 0.06f), 0.10f, 1.0f, rng);
        for (int i = 0; i < 22; i++)
        {
            int cx = rng.RandiRange(0, TilePx - 1);
            int cy = rng.RandiRange(0, TilePx - 1);
            int len = 3 + rng.RandiRange(0, 6);
            int dir = rng.RandiRange(0, 1);
            for (int k = 0; k < len; k++)
            {
                int x = (cx + (dir == 0 ? k : 0)) % TilePx;
                int y = (cy + (dir == 1 ? k : 0)) % TilePx;
                Px(img, TLava, x, y, new Color(0.42f, 0.14f, 0.03f));
            }
        }

        for (int i = 0; i < 14; i++)
        {
            int cx = rng.RandiRange(0, TilePx - 1);
            int cy = rng.RandiRange(0, TilePx - 1);
            Px(img, TLava, cx, cy, new Color(1.0f, 0.72f, 0.18f));
            Px(img, TLava, (cx + 1) % TilePx, cy, new Color(1.0f, 0.60f, 0.12f));
        }
    }

    /// <summary>
    /// Cutout plant tile: transparent background, blades of grass or a stem
    /// with petals — used by the mesher's crossed-quad path.
    /// </summary>
    private static void PaintPlant(Image img, int tile, RandomNumberGenerator rng,
            Color stem, Color accent, float height, bool flower)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Px(img, tile, x, y, new Color(0, 0, 0, 0));
            }
        }

        int top = (int)(TilePx * (1.0f - height));
        if (flower)
        {
            // Stem down the middle with two leaf pairs.
            int cx = TilePx / 2;
            for (int y = top; y < TilePx; y++)
            {
                Px(img, tile, cx, y, Jitter(rng, stem, 0.04f));
                Px(img, tile, cx - 1, y, Jitter(rng, stem.Darkened(0.15f), 0.03f));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 5; k++)
                {
                    Px(img, tile, cx + side * (1 + k / 2), top + 10 + k, Jitter(rng, stem, 0.04f));
                }
            }

            // Petal crown: a 7x6 blob with a warm centre.
            for (int y = top; y < top + 7; y++)
            {
                for (int x = cx - 3; x <= cx + 3; x++)
                {
                    float d = new Vector2(x - cx, (y - top) - 2.5f).Length();
                    if (d > 3.6f)
                    {
                        continue;
                    }

                    Color c = d < 1.4f ? accent.Lightened(0.25f) : accent;
                    Px(img, tile, x, y, Jitter(rng, c, 0.05f));
                }
            }
        }
        else
        {
            // Several blades of varying heights and slight lean.
            for (int b = 0; b < 9; b++)
            {
                int baseX = 2 + b * 3 + rng.RandiRange(0, 2);
                int bladeTop = top + rng.RandiRange(0, 7);
                int lean = rng.RandiRange(-1, 1);
                for (int y = bladeTop; y < TilePx; y++)
                {
                    int x = baseX + (y - bladeTop) * lean / 8;
                    if (x < 0 || x >= TilePx)
                    {
                        continue;
                    }

                    Px(img, tile, x, y, Jitter(rng, stem, 0.06f));
                    if (y > bladeTop + 2 && rng.Randf() < 0.5f)
                    {
                        Px(img, tile, x + 1, y, Jitter(rng, stem.Darkened(0.12f), 0.04f));
                    }
                }
            }
        }
    }

    private static void PaintTorch(Image img, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Px(img, TTorch, x, y, new Color(0, 0, 0, 0));
            }
        }

        // Wooden stick, slightly tapered, standing on the tile's bottom edge.
        for (int y = 12; y < TilePx; y++)
        {
            int half = y < 22 ? 1 : 2;
            for (int x = 15 - half; x <= 15 + half; x++)
            {
                Color wood = x == 15 ? new Color(0.55f, 0.36f, 0.18f) : new Color(0.38f, 0.24f, 0.12f);
                Px(img, TTorch, x, y, Jitter(rng, wood, 0.03f));
            }
        }

        // Flame: a small bright core with orange edges.
        for (int y = 4; y <= 12; y++)
        {
            int spread = y < 7 ? 2 : y < 10 ? 3 : 2;
            for (int x = 15 - spread; x <= 15 + spread; x++)
            {
                float d = new Vector2(x - 15, (y - 8) * 0.8f).Length();
                if (d > spread + 1.0f)
                {
                    continue;
                }

                Color c = d < 1.6f
                        ? new Color(1.0f, 0.94f, 0.55f)
                        : d < 2.8f
                                ? new Color(1.0f, 0.62f, 0.14f)
                                : new Color(0.86f, 0.28f, 0.06f);
                Px(img, TTorch, x, y, Jitter(rng, c, 0.05f));
            }
        }
    }

    private static void PaintFarmland(Image img, RandomNumberGenerator rng)
    {
        PaintDirt(img, TFarmlandTop, rng);
        // Broad, alternating furrow shadows and raised ridges survive the
        // nearest-filtered 32px tile at grazing first-person viewing angles.
        for (int y = 3; y < TilePx; y += 7)
        {
            for (int x = 2; x < TilePx - 2; x++)
            {
                Px(img, TFarmlandTop, x, y, new Color(0.25f, 0.16f, 0.095f));
                if (y + 1 < TilePx)
                    Px(img, TFarmlandTop, x, y + 1, new Color(0.32f, 0.22f, 0.14f));
                if (y + 2 < TilePx)
                    Px(img, TFarmlandTop, x, y + 2, new Color(0.57f, 0.41f, 0.25f));
            }
        }

        PaintDirt(img, TFarmlandSide, rng);
        for (int x = 0; x < TilePx; x++)
        {
            int capDepth = 4 + (x % 4 == 0 ? 1 : 0);
            for (int y = 0; y < capDepth; y++)
            {
                Px(img, TFarmlandSide, x, y,
                        Jitter(rng, new Color(0.24f, 0.16f, 0.095f), 0.035f));
            }

            if (capDepth < TilePx)
                Px(img, TFarmlandSide, x, capDepth,
                        Jitter(rng, new Color(0.45f, 0.31f, 0.19f), 0.035f));
        }
    }

    private static void PaintWheat(Image img, int tile, RandomNumberGenerator rng, float height, bool ripe)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Px(img, tile, x, y, new Color(0, 0, 0, 0));
            }
        }

        int top = (int)(TilePx * (1.0f - height));
        Color stem = ripe ? new Color(0.72f, 0.58f, 0.18f) : new Color(0.30f, 0.55f, 0.20f);
        Color head = ripe ? new Color(0.95f, 0.80f, 0.24f) : new Color(0.42f, 0.68f, 0.26f);
        for (int stalk = 0; stalk < 5; stalk++)
        {
            int bx = 5 + stalk * 6;
            int bend = (stalk % 2 == 0) ? 1 : -1;
            for (int y = top; y < TilePx; y++)
            {
                int x = bx + bend * (y - top) / 14;
                Px(img, tile, x, y, Jitter(rng, stem, 0.04f));
                if (ripe && y < top + 8)
                {
                    Px(img, tile, x + 1, y, Jitter(rng, head, 0.05f));
                    Px(img, tile, x - 1, y, Jitter(rng, head.Darkened(0.1f), 0.05f));
                }
            }
        }
    }

    private static void PaintBed(Image img, RandomNumberGenerator rng)
    {
        Color blanket = new(0.72f, 0.16f, 0.18f);
        Color blanketDark = new(0.48f, 0.09f, 0.12f);
        Color wood = new(0.50f, 0.34f, 0.18f);
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                bool edge = x < 3 || x >= TilePx - 3 || y < 3 || y >= TilePx - 3;
                Px(img, TBedTop, x, y, edge ? wood : blanket);
            }
        }

        // Pillow and blanket folds.
        for (int y = 4; y < 12; y++)
        {
            for (int x = 4; x < 21; x++)
            {
                Px(img, TBedTop, x, y, new Color(0.92f, 0.92f, 0.88f));
            }
        }

        for (int x = 4; x < TilePx - 4; x++)
        {
            Px(img, TBedTop, x, 22, blanketDark);
            Px(img, TBedTop, x, 23, blanketDark.Lightened(0.1f));
        }

        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                bool edge = x < 4 || x >= TilePx - 4 || y < 2 || y >= TilePx - 2;
                Color c = edge ? wood.Darkened(0.15f) : (y < 13 ? blanket : wood);
                Px(img, TBedSide, x, y, Jitter(rng, c, 0.03f));
            }
        }
    }

    private static void PaintLadder(Image img)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Px(img, TLadder, x, y, new Color(0, 0, 0, 0));
            }
        }

        Color wood = new(0.55f, 0.38f, 0.19f);
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 8; x < 12; x++)
            {
                Px(img, TLadder, x, y, wood);
                Px(img, TLadder, x + 12, y, wood.Darkened(0.1f));
            }
        }

        for (int y = 5; y < TilePx; y += 7)
        {
            for (int x = 8; x < 24; x++)
            {
                Px(img, TLadder, x, y, wood);
                Px(img, TLadder, x, y + 1, wood.Darkened(0.15f));
            }
        }
    }

    private static void PaintChest(Image img, RandomNumberGenerator rng)
    {
        Color wood = new(0.48f, 0.30f, 0.14f);
        Color dark = new(0.30f, 0.18f, 0.08f);
        Color band = new(0.68f, 0.52f, 0.22f);
        for (int y = 4; y < TilePx - 2; y++)
        {
            for (int x = 3; x < TilePx - 3; x++)
            {
                bool edge = x < 5 || x >= TilePx - 5 || y < 6 || y >= TilePx - 4;
                Px(img, TChest, x, y, Jitter(rng, edge ? dark : wood, 0.025f));
            }
        }

        // Iron bands and a simple latch on the front.
        for (int x = 3; x < TilePx - 3; x++)
        {
            Px(img, TChest, x, 9, band);
            Px(img, TChest, x, 10, band.Darkened(0.15f));
            Px(img, TChest, x, TilePx - 8, band);
            Px(img, TChest, x, TilePx - 7, band.Darkened(0.15f));
        }

        for (int y = 11; y < 22; y++)
        {
            for (int x = 15; x <= 17; x++)
            {
                Px(img, TChest, x, y, y < 15 ? band.Lightened(0.1f) : band.Darkened(0.2f));
            }
        }

        Px(img, TChest, 16, 15, new Color(0.12f, 0.10f, 0.07f));
    }

    private static void PaintFurnace(Image img, RandomNumberGenerator rng)
    {
        Color stone = new(0.42f, 0.42f, 0.44f);
        Color dark = new(0.20f, 0.20f, 0.22f);
        for (int y = 2; y < TilePx - 2; y++)
        {
            for (int x = 2; x < TilePx - 2; x++)
            {
                bool edge = x < 4 || x >= TilePx - 4 || y < 4 || y >= TilePx - 4;
                Px(img, TFurnace, x, y, Jitter(rng, edge ? dark : stone, 0.035f));
            }
        }

        // Fire mouth.
        for (int y = 15; y < 26; y++)
        {
            for (int x = 8; x < 24; x++)
            {
                bool lip = y < 17 || y >= 24 || x < 10 || x >= 22;
                Color c = lip ? dark : new Color(0.06f, 0.05f, 0.05f);
                Px(img, TFurnace, x, y, c);
            }
        }

        for (int y = 20; y < 24; y++)
        {
            int half = 23 - y;
            for (int x = 16 - half; x <= 16 + half; x++)
            {
                Px(img, TFurnace, x, y, y < 21 ? new Color(1.0f, 0.78f, 0.25f) : new Color(0.95f, 0.42f, 0.08f));
            }
        }
    }

    private static void PaintBedrock(Image img, RandomNumberGenerator rng)
    {
        FillNoise(img, TBedrock, new Color(0.24f, 0.24f, 0.26f), 0.10f, 1.0f, rng);
        Speckle(img, TBedrock, new Color(0.12f, 0.12f, 0.14f), 160, 0.03f, rng);
        Speckle(img, TBedrock, new Color(0.34f, 0.34f, 0.36f), 80, 0.03f, rng);
    }
}
