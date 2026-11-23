using System;
using System.Globalization;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Repeatable real-main-scene capture of a warm voxel-light source beside a
/// newly streamed chunk. Run once with atlas and once with greedy meshing:
/// godot --path . res://tests/ChunkLightingStreamingCapture.tscn --
///   1337 11 greedy res://docs/visual/screenshots/chunk-light-greedy
/// The runner writes before, fade-in, and settled PNGs without changing
/// production world/save data.
/// </summary>
public partial class ChunkLightingStreamingCapture : Node
{
    private const int StreamViewDistance = 2;
    private const int InitialWaitFrames = 1800;
    private const int StreamWaitFrames = 1800;
    private const int FadeSettleFrames = 36;

    public override async void _Ready()
    {
        string worldDir = string.Empty;
        Node3D? game = null;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int seed = ParseInt(args, 0, 1337);
            int version = ParseInt(args, 1, WorldStore.CurrentGenVersion);
            string meshingMode = args.Length > 2 ? args[2].ToLowerInvariant() : "greedy";
            string outputPrefix = args.Length > 3
                    ? args[3]
                    : $"user://tests/chunk-light-stream-{meshingMode}";

            if (version is < 1 or > WorldStore.CurrentGenVersion
                    || meshingMode is not ("atlas" or "greedy"))
            {
                throw new ArgumentException(
                        $"Expected generator version 1–{WorldStore.CurrentGenVersion} and meshing mode atlas|greedy; got {version}/{meshingMode}.");
            }

            DisplayServer.WindowSetSize(new Vector2I(1600, 900));
            Engine.MaxFps = 60;
            Settings.AutoQuality = false;
            Settings.GfxPreset = Gfx.DefaultPreset;
            Settings.ViewDistance = StreamViewDistance;
            Settings.GreedyMeshing = meshingMode == "greedy";

            worldDir = $"user://tests/chunk-light-stream-{seed}-{version}-{Time.GetTicksUsec()}";
            WorldManifest manifest = new(
                    $"Chunk lighting stream capture {meshingMode}", seed, version,
                    WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                    WorldStore.NowUtc(), "creative", "normal");
            if (!WorldStore.WriteManifest(worldDir, manifest))
            {
                throw new InvalidOperationException($"Could not create disposable world at {worldDir}.");
            }

            GameSession.Instance.SelectWorld(worldDir);
            PackedScene gameScene = GD.Load<PackedScene>("res://scenes/main.tscn")
                    ?? throw new InvalidOperationException("Could not load the production main scene.");
            game = gameScene.Instantiate<Node3D>();
            VoxelWorld world = game.GetNode<VoxelWorld>("World");
            world.BuildCollision = false;
            AddChild(game);

            Player player = game.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            player.Visible = false;
            player.Camera.Current = false;
            if (FindNode<Hud>(game) is { } hud) hud.Visible = false;
            if (FindNode<DayNight>(game) is not { } dayNight)
                throw new InvalidOperationException("Production main scene did not initialize DayNight.");
            dayNight.Paused = true;
            world.TimeOfDay = 0.50;
            if (FindNode<Weather>(game) is { } weather)
                weather.Current = Weather.State.Clear;
            if (FindNode<Gfx>(game) is { } gfx)
                gfx.ApplyPreset(Gfx.DefaultPreset, persist: false);

            // LOW-quality streaming settings keep the fixture representative of
            // the MX230 path; one meshing worker makes the arrival sequence
            // deterministic without changing the production worker policy.
            world.SetViewDistance(StreamViewDistance);
            world.MaxInFlight = 1;
            world.SetLodRadius(0.0f);
            int initialCenterCount = (StreamViewDistance * 2 + 1)
                    * (StreamViewDistance * 2 + 1);
            int initialFrames = 0;
            while (world.ChunkCount < initialCenterCount && initialFrames < InitialWaitFrames)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                initialFrames++;
            }

            if (world.ChunkCount < initialCenterCount)
            {
                throw new InvalidOperationException(
                        $"Initial view did not settle: chunks={world.ChunkCount}, expected>={initialCenterCount}.");
            }

            for (int frame = 0; frame < 12; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Vector2I initialCenter = world.PlayerChunk();
            Vector2I targetChunk = initialCenter + new Vector2I(3, 0);
            int sourceX = targetChunk.X * VoxelWorld.ChunkX - 1;
            int sourceZ = targetChunk.Y * VoxelWorld.ChunkZ + VoxelWorld.ChunkZ / 2;
            int targetSurface = world.SurfaceHeight(sourceX + 1, sourceZ);
            int sourceSurface = world.SurfaceHeight(sourceX, sourceZ);
            int torchY = -1;
            for (int y = Math.Clamp(Math.Max(sourceSurface, targetSurface) + 1,
                            2, world.Height - 2); y < world.Height - 1; y++)
            {
                if (world.GetBlock(sourceX, y, sourceZ) == BlockId.Air
                        && world.GetBlock(sourceX + 1, y, sourceZ) == BlockId.Air)
                {
                    torchY = y;
                    break;
                }
            }

            if (torchY < 0)
                throw new InvalidOperationException("Could not find a shared air cell above the generated seam columns.");

            Vector2I sourceChunk = new(
                    Mathf.FloorToInt(sourceX / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(sourceZ / (float)VoxelWorld.ChunkZ));
            if (!world.HasChunk(sourceChunk) || world.HasChunk(targetChunk))
            {
                throw new InvalidOperationException(
                        $"Streaming fixture requires the source chunk {sourceChunk} loaded and target {targetChunk} unloaded.");
            }

            // The source is in the last cell of the already loaded neighbour;
            // the first cell of the not-yet-loaded target is above both local
            // surfaces, giving the baked 14-level source a clear seam crossing.
            if (world.GetBlock(sourceX, torchY, sourceZ) != BlockId.Torch
                    && !world.SetBlock(sourceX, torchY, sourceZ, BlockId.Torch))
            {
                throw new InvalidOperationException("Could not place the seam torch through SetBlock.");
            }

            Camera3D camera = new()
            {
                Name = "ChunkLightingStreamingCamera",
                Fov = 72.0f,
                Far = 180.0f,
                Current = true,
            };
            game.AddChild(camera);
            camera.GlobalPosition = new Vector3(sourceX - 2.5f, torchY + 1.4f, sourceZ + 1.5f);
            camera.LookAt(camera.GlobalPosition + new Vector3(26.0f, -2.5f, 0.0f), Vector3.Up);

            Vector3 destination = new(
                    targetChunk.X * VoxelWorld.ChunkX + VoxelWorld.ChunkX / 2.0f,
                    torchY + 1.0f,
                    sourceZ + 0.5f);
            string beforePath = outputPrefix + ".before.png";
            string fadePath = outputPrefix + ".fade.png";
            string settledPath = outputPrefix + ".settled.png";

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (world.HasChunk(targetChunk))
                throw new InvalidOperationException("Target chunk became loaded before the pre-stream camera capture.");

            GD.Print(
                    $"CHUNK_LIGHT_STREAM_START seed={seed} version={version} mode={meshingMode} " +
                    $"initial={initialCenter} target={targetChunk} " +
                    $"torch=({sourceX},{torchY},{sourceZ}) initial_chunks={world.ChunkCount}");

            // Capture the same fixed camera before the player moves into the
            // target area, guaranteeing that the target chunk is not loaded.
            if (world.HasChunk(targetChunk))
                throw new InvalidOperationException("Target chunk was already loaded before the streaming comparison.");
            Capture(beforePath);

            player.GlobalPosition = destination;
            int streamFrames = 0;
            while (!world.HasChunk(targetChunk) && streamFrames < StreamWaitFrames)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                streamFrames++;
            }

            if (!world.HasChunk(targetChunk))
            {
                throw new InvalidOperationException(
                        $"Target chunk {targetChunk} did not stream within {StreamWaitFrames} frames.");
            }

            // HasChunk becomes true after FadeIn starts on the newly applied
            // chunk. A short hold records its transition; the final frame is
            // captured after the existing 0.4-second fade has completed.
            for (int frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Capture(fadePath);

            for (int frame = 0; frame < FadeSettleFrames; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Capture(settledPath);

            VoxelLightData targetLight = world.LightData(targetChunk);
            int localZ = sourceZ - targetChunk.Y * VoxelWorld.ChunkZ;
            byte boundaryBlockLight = targetLight.BlockAt(0, torchY, localZ, world.Height);
            bool hasCameraSky = world.TrySampleCachedSkyLight(camera.GlobalPosition, out float cameraSky);
            GD.Print(
                    $"CHUNK_LIGHT_STREAM_RESULT mode={meshingMode} before_target_loaded=false " +
                    $"fade_target_loaded={world.HasChunk(targetChunk)} boundary_block_light={boundaryBlockLight} " +
                    $"camera_sky={hasCameraSky}:{cameraSky:F2} initial_frames={initialFrames} " +
                    $"stream_frames={streamFrames} chunks={world.ChunkCount} " +
                    $"before={ProjectSettings.GlobalizePath(beforePath)} " +
                    $"fade={ProjectSettings.GlobalizePath(fadePath)} " +
                    $"settled={ProjectSettings.GlobalizePath(settledPath)}");

            if (boundaryBlockLight < 9)
            {
                throw new InvalidOperationException(
                        $"Expected propagated torch light across the streamed seam (at least 9), got {boundaryBlockLight}.");
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Chunk lighting streaming capture failed: {exception}");
            GetTree().Quit(3);
            return;
        }
        finally
        {
            if (game != null && GodotObject.IsInstanceValid(game))
            {
                game.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            if (!string.IsNullOrEmpty(worldDir))
            {
                string absoluteWorldDir = ProjectSettings.GlobalizePath(worldDir);
                try
                {
                    if (Directory.Exists(absoluteWorldDir))
                        Directory.Delete(absoluteWorldDir, recursive: true);
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"Could not remove temporary visual world {absoluteWorldDir}: {exception.Message}");
                }
            }
        }

        GetTree().Quit();
    }

    private void Capture(string path)
    {
        string absolutePath = ProjectSettings.GlobalizePath(path);
        string? directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(absolutePath);
        if (result != Error.Ok)
            throw new IOException($"Could not write chunk-stream image {absolutePath}: {result}.");
    }

    private static T? FindNode<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (Node child in root.GetChildren())
        {
            T? found = FindNode<T>(child);
            if (found != null) return found;
        }

        return null;
    }

    private static int ParseInt(string[] args, int index, int fallback) =>
            index < args.Length && int.TryParse(args[index], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value) ? value : fallback;
}
