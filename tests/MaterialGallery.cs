using System;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace Minecraft;

/// <summary>
/// Standalone inspection scene for every registered block. The fixture creates
/// an isolated disposable world and edits only through VoxelWorld.SetBlock.
/// Air and Bedrock use non-world preview geometry because Air has no surface
/// and SetBlock intentionally rejects Bedrock placement.
/// </summary>
public partial class MaterialGallery : Node3D
{
    public const int Columns = 8;
    public const int SlotSpacing = 2;
    private const int PreviewRadius = 1;
    private const int StreamTimeoutFrames = 1800;

    private VoxelWorld? _world;
    private Player? _streamingAnchor;
    private Camera3D? _camera;
    private string _temporaryWorldDir = string.Empty;
    private string _previousWorldDir = string.Empty;
    private bool _previousGreedyMeshing;

    public static int RegisteredBlockCount => Enum.GetValues<BlockId>().Length;
    public static int Rows => (RegisteredBlockCount + Columns - 1) / Columns;

    public static Vector2I SlotFor(BlockId id)
    {
        if (!BlockDb.IsDefined(id))
            throw new ArgumentOutOfRangeException(nameof(id), id, "Block is not registered.");

        int index = (int)id;
        return new Vector2I(index % Columns, index / Columns);
    }

    public static Vector3I OffsetFor(BlockId id)
    {
        Vector2I slot = SlotFor(id);
        return new Vector3I(
                slot.X * SlotSpacing - (Columns - 1),
                0,
                slot.Y * SlotSpacing - (Rows - 1));
    }

    public static bool UsesPreviewGeometry(BlockId id) =>
            id is BlockId.Air or BlockId.Bedrock;

    public override async void _Ready()
    {
        int exitCode = 0;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int seed = ParseInt(args, 0, 1337);
            string closeOutput = args.Length > 1 ? args[1] : "user://material-gallery-close.png";
            string distantOutput = args.Length > 2 ? args[2] : "user://material-gallery-distant.png";
            bool? greedyOverride = args.Length > 3
                    ? string.Equals(args[3], "greedy", StringComparison.OrdinalIgnoreCase) ? true
                    : string.Equals(args[3], "atlas", StringComparison.OrdinalIgnoreCase) ? false
                    : null
                    : null;

            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Engine.MaxFps = 60;
            BlockDb.EnsureTables();

            _previousGreedyMeshing = Settings.GreedyMeshing;
            if (greedyOverride.HasValue)
                Settings.GreedyMeshing = greedyOverride.Value;
            _previousWorldDir = GameSession.Instance.WorldDir;
            _temporaryWorldDir = $"user://tests/material-gallery-{seed}-{Time.GetTicksUsec()}";
            WorldManifest manifest = new(
                    $"Material gallery {seed}", seed, WorldStore.CurrentGenVersion,
                    WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                    WorldStore.NowUtc(), "creative", "normal");
            if (!WorldStore.WriteManifest(_temporaryWorldDir, manifest))
                throw new IOException($"Could not create temporary gallery world at {_temporaryWorldDir}.");

            GameSession.Instance.SelectWorld(_temporaryWorldDir);

            _world = new VoxelWorld
            {
                Name = "GalleryWorld",
                BuildCollision = false,
                ViewDistance = PreviewRadius,
                MaxInFlight = 2,
                LodRadius = 0.0f,
            };
            AddChild(_world);

            const int centerX = 8;
            const int centerZ = 8;
            _world.BuildPreview(new Vector3(centerX + 0.5f, 0.0f, centerZ + 0.5f), PreviewRadius);

            int baseY = Math.Min(_world.Height - 10,
                    Math.Max(_world.SurfaceHeight(centerX, centerZ) + 12, _world.SeaLevel + 14));
            CreateStreamingAnchor(centerX, centerZ, baseY);

            ConfigureLighting();
            Vector3 target = GalleryTarget(centerX, centerZ, baseY);
            _camera!.GlobalPosition = target + new Vector3(14.0f, 9.0f, 18.0f);
            _camera.LookAt(target, Vector3.Up);
            CreateGallery(centerX, centerZ, baseY);
            AddLegend();
            GD.Print($"MATERIAL_GALLERY_BUILD blocks={RegisteredBlockCount} rows={Rows} columns={Columns} baseY={baseY} greedy={Mesher.Greedy}");

            await WaitForMeshes();
            GD.Print($"MATERIAL_GALLERY_READY chunks={_world.ChunkCount} pending={Profiler.PendingPayloadCount}");

            Capture(closeOutput, "close");
            _camera.GlobalPosition = target + new Vector3(30.0f, 20.0f, 40.0f);
            _camera.LookAt(target, Vector3.Up);
            await WaitFrames(45);
            Capture(distantOutput, "distant");
        }
        catch (Exception exception)
        {
            exitCode = 8;
            GD.PushError($"Material gallery failed: {exception}");
        }
        finally
        {
            if (_world != null && GodotObject.IsInstanceValid(_world))
            {
                _world.Player = null;
                _world.QueueFree();
            }

            if (_streamingAnchor != null && GodotObject.IsInstanceValid(_streamingAnchor))
                _streamingAnchor.Free();

            await WaitFrames(2);
            CleanupTemporaryWorld();
            RestoreSelectedWorld();
            Settings.GreedyMeshing = _previousGreedyMeshing;
        }

        GetTree().Quit(exitCode);
    }

    private void CreateStreamingAnchor(int centerX, int centerZ, int baseY)
    {
        Player anchor = new()
        {
            Name = "GalleryStreamingAnchor",
            World = _world,
            CollisionLayer = 0,
            CollisionMask = 0,
        };
        anchor.SetProcess(false);
        anchor.SetPhysicsProcess(false);
        anchor.SetProcessUnhandledInput(false);
        anchor.SetProcessUnhandledKeyInput(false);
        anchor.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
        });
        Camera3D anchorCamera = new()
        {
            Name = "Camera3D",
            Current = false,
            Position = new Vector3(0.0f, 1.62f, 0.0f),
        };
        anchorCamera.AddChild(new RayCast3D
        {
            Name = "RayCast3D",
            TargetPosition = new Vector3(0.0f, 0.0f, -5.0f),
        });
        anchor.AddChild(anchorCamera);
        AddChild(anchor);
        anchor.Position = new Vector3(centerX + 0.5f, baseY + 4.0f, centerZ + 0.5f);
        anchor.SetProcess(false);
        anchor.SetPhysicsProcess(false);
        _streamingAnchor = anchor;
        _world!.Player = anchor;
    }

    private void ConfigureLighting()
    {
        WorldEnvironment worldEnvironment = new()
        {
            Name = "GalleryEnvironment",
            Environment = WorldEnvironmentFactory.Build(_world!.WorldGenVersion),
        };
        worldEnvironment.Environment!.FogEnabled = false;
        worldEnvironment.Environment.VolumetricFogEnabled = false;
        worldEnvironment.Environment.SsaoEnabled = true;
        worldEnvironment.Environment.SsrEnabled = false;
        worldEnvironment.Environment.GlowEnabled = false;
        worldEnvironment.Environment.AmbientLightEnergy = 1.0f;
        AddChild(worldEnvironment);

        DirectionalLight3D keyLight = new()
        {
            Name = "GalleryKeyLight",
            RotationDegrees = new Vector3(-48.0f, -35.0f, 0.0f),
            LightColor = new Color(1.0f, 0.97f, 0.91f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 80.0f,
            ShadowBias = 0.04f,
            ShadowNormalBias = 1.0f,
        };
        AddChild(keyLight);

        _camera = new Camera3D
        {
            Name = "GalleryCamera",
            Fov = 55.0f,
            Far = 180.0f,
            Current = true,
        };
        AddChild(_camera);
    }

    private void CreateGallery(int centerX, int centerZ, int baseY)
    {
        AddStage(centerX, centerZ, baseY);
        int edits = 0;
        foreach (BlockId id in Enum.GetValues<BlockId>())
        {
            Vector3I offset = OffsetFor(id);
            int x = centerX + offset.X;
            int z = centerZ + offset.Z;

            // Clear only the five display cells above each pedestal. The
            // gallery never changes the surrounding terrain or generation.
            for (int y = baseY; y <= baseY + 5; y++)
            {
                if (_world!.GetBlock(x, y, z) != BlockId.Air)
                {
                    if (!_world.SetBlock(x, y, z, BlockId.Air))
                        throw new InvalidOperationException($"Could not clear gallery cell for {id} at {x},{y},{z}.");
                    edits++;
                }
            }

            if (!_world!.SetBlock(x, baseY - 1, z, BlockId.Cobblestone))
                throw new InvalidOperationException($"Could not place pedestal for {id}.");
            edits++;

            Vector3 samplePosition = new(x, baseY, z);
            if (id == BlockId.Air)
            {
                AddAirPreview(samplePosition);
            }
            else if (id == BlockId.Bedrock)
            {
                AddBedrockPreview(samplePosition);
            }
            else
            {
                // The production block database, lighting and mesher handle
                // every registered material, including fluids and cutouts.
                if (!_world.SetBlock(x, baseY, z, id))
                    throw new InvalidOperationException($"Could not place registered block {id} at {x},{baseY},{z}.");
                edits++;
            }

            AddBlockLabel(id, samplePosition + new Vector3(0.5f, 1.35f, 0.5f));
        }

        GD.Print($"MATERIAL_GALLERY_SAMPLES edits={edits} bedrock_proxy=1 air_proxy=1");
    }

    private void AddStage(int centerX, int centerZ, int baseY)
    {
        StandardMaterial3D stageMaterial = new()
        {
            AlbedoColor = new Color(0.22f, 0.25f, 0.28f),
            Roughness = 0.96f,
            Metallic = 0.0f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        MeshInstance3D stage = new()
        {
            Name = "NeutralGalleryStage",
            Mesh = new PlaneMesh
            {
                Size = new Vector2(120.0f, 120.0f),
                SubdivideWidth = 1,
                SubdivideDepth = 1,
            },
            MaterialOverride = stageMaterial,
            Position = new Vector3(centerX + 0.5f, baseY - 1.02f, centerZ + 0.5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(stage);
    }

    private void AddAirPreview(Vector3 position)
    {
        StandardMaterial3D outlineMaterial = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.55f, 0.78f, 0.95f, 0.9f),
            NoDepthTest = false,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MeshInstance3D outline = new()
        {
            Name = "AirPreview",
            Mesh = BuildAirOutline(),
            MaterialOverride = outlineMaterial,
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(outline);
    }


    private static ArrayMesh BuildAirOutline()
    {
        Vector3[] corners =
        [
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        ];
        int[] edges =
        [
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];
        SurfaceTool surface = new();
        surface.Begin(Mesh.PrimitiveType.Lines);
        for (int i = 0; i < edges.Length; i++)
        {
            surface.AddVertex(corners[edges[i]]);
        }
        return surface.Commit();
    }

    private void AddBedrockPreview(Vector3 position)
    {
        MeshInstance3D preview = new()
        {
            Name = "BedrockPreview",
            Mesh = BuildPreviewCube(BlockId.Bedrock),
            MaterialOverride = ChunkMaterials.Opaque,
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(preview);
    }

    /// <summary>
    /// Bedrock cannot be placed through the world mutation API. This display-
    /// only cube uses the exact atlas/texture-array tile mapping and material
    /// response used by the production renderer, without editing world data.
    /// </summary>
    private static ArrayMesh BuildPreviewCube(BlockId id)
    {
        SurfaceTool surface = new();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int face = 0; face < 6; face++)
        {
            int tile = BlockDb.FaceTile(id, face);
            if (tile < 0)
                continue;

            SurfaceFamily family = MaterialResponse.ForTile(tile);
            float shade = BlockDb.FaceColors[face].R;
            Color lit = VoxelLighting.VertexColor(VoxelLighting.Pack(15, 0), shade);
            for (int triangleVertex = 0; triangleVertex < 6; triangleVertex++)
            {
                int corner = Mesher.TriOrder[triangleVertex];
                int vertexIndex = face * 4 + corner;
                surface.SetNormal(BlockDb.FaceDirs[face]);
                surface.SetColor(new Color(lit.R, lit.G, lit.B, (float)family));
                if (Mesher.Greedy)
                {
                    surface.SetUV(BlockDb.FaceUvs[vertexIndex]);
                    surface.SetUV2(new Vector2(tile, 0.0f));
                }
                else
                {
                    surface.SetUV(BlockDb.MapUv(BlockDb.TileUvRect(tile), BlockDb.FaceUvs[vertexIndex]));
                }
                surface.AddVertex(BlockDb.FaceVerts[vertexIndex]);
            }
        }

        return surface.Commit();
    }

    private void AddBlockLabel(BlockId id, Vector3 position)
    {
        Label3D label = new()
        {
            Name = $"BlockLabel{(byte)id:00}",
            Text = $"#{(byte)id:00}",
            FontSize = 20,
            PixelSize = 0.008f,
            OutlineSize = 5,
            Modulate = new Color(0.96f, 0.97f, 1.0f),
            OutlineModulate = new Color(0.025f, 0.035f, 0.05f, 1.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            FixedSize = false,
            Position = position,
        };
        AddChild(label);
    }

    private async System.Threading.Tasks.Task WaitForMeshes()
    {
        int frames = 0;
        int stableFrames = 0;
        while (frames < StreamTimeoutFrames && stableFrames < 30)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
            bool idle = _world!.ChunkCount >= 9
                    && !Profiler.WorkersActive
                    && Profiler.PendingPayloadCount == 0
                    && Profiler.RebuildQueued == 0
                    && Profiler.FreshQueued == 0;
            stableFrames = idle ? stableFrames + 1 : 0;
        }

        if (stableFrames < 30)
            throw new TimeoutException($"Gallery meshes did not settle (chunks={_world!.ChunkCount}, pending={Profiler.PendingPayloadCount}, rebuilds={Profiler.RebuildQueued}).");
        await WaitFrames(2);
    }

    private void Capture(string output, string view)
    {
        string absoluteOutput = ProjectSettings.GlobalizePath(output);
        string? directory = Path.GetDirectoryName(absoluteOutput);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(absoluteOutput);
        if (result != Error.Ok)
            throw new IOException($"Could not save {view} material gallery capture to {absoluteOutput}: {result}.");

        GD.Print($"MATERIAL_GALLERY_CAPTURE view={view} size={image.GetWidth()}x{image.GetHeight()} success=true path={absoluteOutput}");
    }

    private void AddLegend()
    {
        CanvasLayer layer = new()
        {
            Name = "MaterialGalleryLegend",
            Layer = 5,
        };
        AddChild(layer);

        int legendRows = (RegisteredBlockCount + 9) / 10;
        AddLegendPanel(layer, "BlockIdLegend", 12.0f, 0, legendRows);
    }

    private static void AddLegendPanel(CanvasLayer layer, string name, float top, int firstIndex, int rowCount)
    {
        ColorRect backing = new()
        {
            Name = $"{name}Backing",
            Position = new Vector2(12.0f, top),
            Size = new Vector2(1256.0f, 198.0f),
            Color = new Color(0.025f, 0.035f, 0.045f, 0.86f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layer.AddChild(backing);

        Label legend = new()
        {
            Name = name,
            Position = new Vector2(22.0f, top + 6.0f),
            Size = new Vector2(1236.0f, 186.0f),
            Text = BuildLegendText(firstIndex, rowCount),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        legend.AddThemeFontSizeOverride("font_size", 10);
        legend.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        layer.AddChild(legend);
    }

    private static string BuildLegendText(int firstIndex, int rowCount)
    {
        const int legendColumns = 10;
        StringBuilder text = new();
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < legendColumns; column++)
            {
                int index = firstIndex + row * legendColumns + column;
                if (index >= RegisteredBlockCount)
                    continue;

                if (column > 0)
                    text.Append("  ");
                BlockId id = (BlockId)index;
                text.Append($"#{index:00} {BlockDb.NameOf(id)}");
            }

            if (row + 1 < rowCount)
                text.AppendLine();
        }

        return text.ToString();
    }

    private static Vector3 GalleryTarget(int centerX, int centerZ, int baseY) =>
            new(centerX + 0.5f, baseY + 0.55f, centerZ + 0.5f);

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void CleanupTemporaryWorld()
    {
        if (string.IsNullOrEmpty(_temporaryWorldDir))
            return;

        string testsRoot = ProjectSettings.GlobalizePath("user://tests");
        string path = ProjectSettings.GlobalizePath(_temporaryWorldDir);
        if (Path.GetDirectoryName(path) != testsRoot
                || !Path.GetFileName(path).StartsWith("material-gallery-", StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to remove non-gallery world path {path}.");

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private void RestoreSelectedWorld()
    {
        if (_previousWorldDir.Length > 0 && WorldStore.ReadManifest(_previousWorldDir) != null)
            GameSession.Instance.SelectWorld(_previousWorldDir);
        else
        {
            GameSession.Instance.WorldDir = string.Empty;
            GameSession.Instance.Manifest = null;
        }
    }

    private static int ParseInt(string[] args, int index, int fallback) =>
            index < args.Length && int.TryParse(args[index], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value) ? value : fallback;
}
