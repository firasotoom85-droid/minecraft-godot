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
        new("Planks", true, false, [TPlanks, TPlanks, TPlanks, TPlanks, TPlanks, TPlanks],
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
                0.4f, ToolKind.Axe),
        new("Crafting Table", true, false,
                [TCraftingTop, TPlanks, TCraftingSide, TCraftingSide, TCraftingSide, TCraftingSide],
                2.5f, ToolKind.Axe),
        new("Wool", true, false, [TWool, TWool, TWool, TWool, TWool, TWool], 0.8f),
        new("Lava", false, true, [TLava, TLava, TLava, TLava, TLava, TLava], 0.0f, Drops: false),
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
        new("Enchanting Table", true, false,
                [TEnchantTop, TEnchantSide, TEnchantSide, TEnchantSide, TEnchantSide, TEnchantSide],
                3.0f, ToolKind.Pick, 2),
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
    /// a hand or a non-matching tool uses hardness × 5.
    /// </summary>
    public static float MiningSeconds(BlockDef def, ToolInfo? tool)
    {
        if (def.Hardness <= 0.0f || def.Hardness >= 1000.0f)
        {
            return float.PositiveInfinity;
        }

        if (def.Tool != ToolKind.None && tool is { } held
                && held.Kind == def.Tool && held.Speed > 0.0f)
        {
            return def.Hardness * 1.5f / held.Speed;
        }

        return def.Hardness * 5.0f;
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
            _foliage = new byte[BlockCount];
            _foliage[(int)BlockId.Leaves] = 1;
            _foliage[(int)BlockId.BirchLeaves] = 1;
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

    /// <summary>
    /// Whether the face of <paramref name="selfId"/> towards <paramref name="neighborId"/> must be drawn.
    /// Foliage/foliage faces are kept so alpha-cutout canopies look full.
    /// </summary>
    public static bool RenderFace(BlockId selfId, BlockId neighborId)
    {
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

    public static bool IsFoliage(BlockId id) => id is BlockId.Leaves or BlockId.BirchLeaves;

    /// <summary>Cross-shaped plants: two crossed double-sided quads, no cube faces.</summary>
    public static bool IsCross(BlockId id) =>
        id is BlockId.TallGrass or BlockId.FlowerRed or BlockId.FlowerYellow or BlockId.Torch
                or BlockId.Wheat0 or BlockId.Wheat1 or BlockId.Wheat2;

    public static bool IsWheat(BlockId id) =>
        id is BlockId.Wheat0 or BlockId.Wheat1 or BlockId.Wheat2;

    /// <summary>Tile used for the hotbar icon of a block.</summary>
    public static int IconTile(BlockId id)
    {
        int[] tiles = Defs[(int)id].Tiles;
        return tiles[4] >= 0 ? tiles[4] : tiles[0];
    }

    private static readonly Dictionary<BlockId, Color> AvgColors = [];

    /// <summary>Average (opaque) colour of a block's icon tile — used to tint particles.</summary>
    public static Color AverageTileColor(BlockId id)
    {
        if (AvgColors.TryGetValue(id, out Color cached))
        {
            return cached;
        }

        int tile = IconTile(id);
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
                    n += 1;
                }
            }
        }

        Color col = new(1, 1, 1);
        if (n > 0)
        {
            acc /= n;
            col = new Color(acc.X, acc.Y, acc.Z);
        }

        AvgColors[id] = col;
        return col;
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
    /// Opaque terrain (alpha-scissor so the cutout leaf pixels work while the
    /// material still lives in the opaque pipeline and casts shadows).
    /// </summary>
    public static StandardMaterial3D MakeOpaqueMaterial()
    {
        StandardMaterial3D m = new()
        {
            AlbedoTexture = AtlasTexture(),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            TextureRepeat = false,
            VertexColorUseAsAlbedo = true,
            Roughness = 1.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.5f,
            // Per-sample coverage for cutout leaves (needs MSAA; no-op without it) so
            // distant foliage does not sparkle against the sky.
            AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage,
        };
        return m;
    }

    /// <summary>Alpha-blended glass.</summary>
    public static StandardMaterial3D MakeGlassMaterial()
    {
        StandardMaterial3D m = new()
        {
            AlbedoTexture = AtlasTexture(),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            TextureRepeat = false,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.15f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return m;
    }

    /// <summary>
    /// Greedy-mesh material: one texture-array layer per tile, so merged
    /// multi-block quads can tile their texture with UV fract() — impossible
    /// with a classic atlas (a merged quad would sample neighbouring tiles).
    /// Visually matches MakeOpaqueMaterial (same albedo × vertex colour,
    /// roughness 1, no specular, alpha scissor for cutout leaves).
    /// </summary>
    private const string OpaqueArrayShader = @"
shader_type spatial;
render_mode cull_back, depth_draw_opaque, specular_disabled, alpha_to_coverage;

uniform sampler2DArray tiles : source_color, filter_nearest;
uniform vec4 tint = vec4(1.0);   // wet-ground darkening (Weather)

varying vec2 tile_uv;
varying float tile_layer;

void vertex() {
    tile_uv = UV;
    tile_layer = UV2.x;
}

void fragment() {
    vec4 tex = texture(tiles, vec3(fract(tile_uv), tile_layer));
    ALBEDO = tex.rgb * COLOR.rgb * tint.rgb;
    ALPHA = tex.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
    ROUGHNESS = 1.0;
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

    /// <summary>Emissive lava: the atlas tile doubles as the emission map, so it glows in the dark.</summary>
    public static StandardMaterial3D MakeLavaMaterial()
    {
        StandardMaterial3D m = new()
        {
            AlbedoTexture = AtlasTexture(),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            TextureRepeat = false,
            VertexColorUseAsAlbedo = true,
            Roughness = 1.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.55f, 0.15f),
            EmissionTexture = AtlasTexture(),
            EmissionEnergyMultiplier = 1.2f,
        };
        return m;
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
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Color c = Jitter(rng, baseColor, amount);
                c.A *= alpha;
                Px(img, tile, x, y, c);
            }
        }
    }

    private static void Speckle(Image img, int tile, Color c, int count, float amount,
            RandomNumberGenerator rng)
    {
        for (int i = 0; i < count; i++)
        {
            Px(img, tile, rng.RandiRange(0, TilePx - 1), rng.RandiRange(0, TilePx - 1),
                    Jitter(rng, c, amount));
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
        for (int x = 0; x < TilePx; x++)
        {
            int depth = 6 + rng.RandiRange(0, 4);
            for (int y = 0; y < depth; y++)
            {
                Px(img, TGrassSide, x, y, Jitter(rng, new Color(0.34f, 0.56f, 0.24f), 0.05f));
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
        for (int x = 0; x < TilePx; x++)
        {
            float shade = rng.RandfRange(-0.045f, 0.045f);
            for (int y = 0; y < TilePx; y++)
            {
                Color c = new(0.42f + shade, 0.31f + shade * 0.8f, 0.19f + shade * 0.5f);
                Px(img, TLogSide, x, y, Jitter(rng, c, 0.015f));
            }
        }

        for (int i = 0; i < 12; i++)
        {
            int x = rng.RandiRange(0, TilePx - 1);
            int h = rng.RandiRange(8, 24);
            int y0 = rng.RandiRange(0, TilePx - h);
            for (int y = y0; y < y0 + h; y++)
            {
                Px(img, TLogSide, x, y, new Color(0.33f, 0.24f, 0.14f));
            }
        }
    }

    private static void PaintLogTop(Image img, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                float d = new Vector2(x - TilePx / 2.0f + 0.5f, y - TilePx / 2.0f + 0.5f).Length();
                Color c;
                if (d > TilePx / 2.0f - 1.5f)
                {
                    c = Jitter(rng, new Color(0.36f, 0.27f, 0.17f), 0.03f);
                }
                else
                {
                    float ring = Mathf.Sin(d * 1.2f) * 0.5f + 0.5f;
                    c = new Color(0.55f, 0.42f, 0.26f).Lerp(new Color(0.44f, 0.33f, 0.20f), ring);
                    c = Jitter(rng, c, 0.015f);
                }

                Px(img, TLogTop, x, y, c);
            }
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

    private static void PaintBirchTop(Image img, RandomNumberGenerator rng)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                float d = new Vector2(x - TilePx / 2.0f + 0.5f, y - TilePx / 2.0f + 0.5f).Length();
                Color c;
                if (d > TilePx / 2.0f - 1.5f)
                {
                    c = Jitter(rng, new Color(0.80f, 0.79f, 0.74f), 0.02f);
                }
                else
                {
                    float ring = Mathf.Sin(d * 1.1f) * 0.5f + 0.5f;
                    c = new Color(0.90f, 0.88f, 0.80f).Lerp(new Color(0.78f, 0.75f, 0.65f), ring);
                    c = Jitter(rng, c, 0.012f);
                }

                Px(img, TBirchTop, x, y, c);
            }
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
        {
            for (int x = 0; x < TilePx; x++)
            {
                if (rng.Randf() < 0.15f)
                {
                    Px(img, tile, x, y, new Color(0, 0, 0, 0));
                    continue;
                }

                Color c = Jitter(rng, baseColor, 0.07f);
                float r = rng.Randf();
                if (r < 0.20f)
                {
                    c = Jitter(rng, dark, 0.02f);
                }
                else if (r < 0.35f)
                {
                    c = Jitter(rng, light, 0.02f);
                }

                Px(img, tile, x, y, c);
            }
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
        int count = 8 + rng.RandiRange(0, 4);
        for (int i = 0; i < count; i++)
        {
            int cx = rng.RandiRange(0, TilePx - 5);
            int cy = rng.RandiRange(0, TilePx - 5);
            int w = 3 + rng.RandiRange(0, 1);
            int h = 3 + rng.RandiRange(0, 1);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Px(img, tile, (cx + x) % TilePx, (cy + y) % TilePx,
                            Jitter(rng, oreColor, 0.06f));
                }
            }
        }
    }

    private static void PaintGlass(Image img)
    {
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                Color c = new(0.85f, 0.95f, 1.0f, 0.16f);
                if (x < 2 || y < 2 || x >= TilePx - 2 || y >= TilePx - 2)
                {
                    c = new Color(0.80f, 0.93f, 1.0f, 0.62f);
                }

                Px(img, TGlass, x, y, c);
            }
        }

        for (int i = 0; i < 8; i++)
        {
            Px(img, TGlass, 6 + i, 6 + i, new Color(1, 1, 1, 0.45f));
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
        for (int y = 4; y < TilePx; y += 6)
        {
            for (int x = 3; x < TilePx - 3; x++)
            {
                Px(img, TFarmlandTop, x, y, new Color(0.30f, 0.21f, 0.13f));
                Px(img, TFarmlandTop, x, y + 1, new Color(0.52f, 0.38f, 0.24f));
            }
        }

        PaintDirt(img, TFarmlandSide, rng);
        for (int x = 0; x < TilePx; x++)
        {
            for (int y = 0; y < 7; y++)
            {
                Px(img, TFarmlandSide, x, y, Jitter(rng, new Color(0.34f, 0.24f, 0.15f), 0.04f));
            }
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
