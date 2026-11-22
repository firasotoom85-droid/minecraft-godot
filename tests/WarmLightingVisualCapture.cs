using System;
using System.Globalization;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Repeatable real-main-scene comparison of torch-only, campfire-only and
/// overlapping warm emitters. Usage:
/// godot --path . res://tests/WarmLightingVisualCapture.tscn --
///   <seed> <generator-version> <campfire-cell-x> <campfire-cell-z>
///   <torch|campfire|overlap> <output.png>
/// </summary>
public partial class WarmLightingVisualCapture : Node
{
    public override async void _Ready()
    {
        string worldDir = string.Empty;
        Node3D? game = null;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int seed = ParseInt(args, 0, 1337);
            int version = ParseInt(args, 1, WorldStore.CurrentGenVersion);
            int cellX = ParseInt(args, 2, 0);
            int cellZ = ParseInt(args, 3, 0);
            string mode = args.Length > 4 ? args[4].ToLowerInvariant() : "overlap";
            string output = args.Length > 5
                    ? args[5]
                    : $"user://tests/warm-lighting-{mode}.png";

            if (version is < 1 or > WorldStore.CurrentGenVersion
                    || mode is not ("torch" or "campfire" or "overlap"))
            {
                GD.PushError($"Invalid capture arguments: version={version}, mode={mode}.");
                GetTree().Quit(2);
                return;
            }

            DisplayServer.WindowSetSize(new Vector2I(1600, 900));
            Engine.MaxFps = 60;
            Settings.AutoQuality = false;
            worldDir = $"user://tests/warm-light-{seed}-{version}-{Time.GetTicksUsec()}";
            WorldManifest manifest = new(
                    $"Warm lighting comparison {mode}", seed, version,
                    WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                    WorldStore.NowUtc(), "creative", "normal");
            if (!WorldStore.WriteManifest(worldDir, manifest))
                throw new InvalidOperationException($"Could not create temporary world at {worldDir}.");

            GameSession.Instance.SelectWorld(worldDir);
            PackedScene gameScene = GD.Load<PackedScene>("res://scenes/main.tscn")
                    ?? throw new InvalidOperationException("Could not load the main game scene.");
            game = gameScene.Instantiate<Node3D>();
            AddChild(game);

            VoxelWorld world = game.GetNode<VoxelWorld>("World");
            Player player = game.GetNode<Player>("Player");
            player.SetPhysicsProcess(false);
            player.Visible = false;
            player.Camera.Current = false;
            if (FindNode<Hud>(game) is { } hud) hud.Visible = false;
            if (FindNode<DayNight>(game) is not { } dayNight)
                throw new InvalidOperationException("Main scene did not initialize DayNight.");
            dayNight.Paused = true;
            world.TimeOfDay = 0.0;
            world.SetViewDistance(3);
            world.SetLodRadius(0.0f);
            if (FindNode<Gfx>(game) is { } gfx) gfx.ApplyPreset(Gfx.DefaultPreset, false);

            Vector3I? selectedFire = world.GetCampfireSite(cellX, cellZ);
            for (int radius = 1; radius <= 8 && selectedFire == null; radius++)
            {
                for (int dz = -radius; dz <= radius && selectedFire == null; dz++)
                for (int dx = -radius; dx <= radius && selectedFire == null; dx++)
                    selectedFire = world.GetCampfireSite(cellX + dx, cellZ + dz);
            }

            Vector3I fire = selectedFire
                    ?? throw new InvalidOperationException("No generated campfire was found near the requested cell.");
            GD.Print($"WARM_LIGHT_FIXTURE cell=({Mathf.FloorToInt(fire.X / (float)Campfires.Cell)},{Mathf.FloorToInt(fire.Z / (float)Campfires.Cell)}) site=({fire.X},{fire.Y},{fire.Z})");
            Vector3 center = new(fire.X + 0.5f, fire.Y + 1.0f, fire.Z + 0.5f);
            player.GlobalPosition = center + new Vector3(0, 1.0f, 0);

            // The real world owns chunk generation, the fire particles, and
            // every edit; the capture never writes directly into chunk arrays.
            world.BuildPreview(center, 1);
            if (world.GetBlock(fire.X, fire.Y, fire.Z) != BlockId.Log)
                throw new InvalidOperationException("The generated campfire log is not present in the fixture.");

            if (mode == "torch")
            {
                // A torch emits from its own non-solid voxel; remove the
                // campfire anchor so this fixture has exactly one warm source.
                if (!world.SetBlock(fire.X, fire.Y, fire.Z, BlockId.Stone)
                        || !world.SetBlock(fire.X, fire.Y + 1, fire.Z, BlockId.Torch))
                    throw new InvalidOperationException("Could not place the isolated torch through SetBlock.");
            }
            else if (mode == "overlap")
            {
                int placed = 0;
                foreach ((int dx, int dz) in new[] { (-3, 0), (3, 0), (0, -3), (0, 3) })
                {
                    int x = fire.X + dx;
                    int z = fire.Z + dz;
                    int y = world.SurfaceHeight(x, z) + 1;
                    if ((uint)y >= (uint)world.Height || world.GetBlock(x, y, z) != BlockId.Air)
                        continue;
                    if (world.SetBlock(x, y, z, BlockId.Torch)) placed++;
                    if (placed == 2) break;
                }

                if (placed == 0)
                    throw new InvalidOperationException("Could not place comparison torches near the campfire.");
            }

            Camera3D camera = new()
            {
                Name = "WarmLightingComparisonCamera",
                Fov = 56.0f,
                Far = 180.0f,
                Current = true,
            };
            game.AddChild(camera);
            camera.GlobalPosition = center + new Vector3(5.0f, 3.5f, 6.0f);
            camera.LookAt(center + new Vector3(0.0f, 0.35f, 0.0f), Vector3.Up);

            GD.Print($"WARM_LIGHT_CAPTURE_START seed={seed} version={version} cell=({cellX},{cellZ}) mode={mode}");
            double started = Time.GetTicksMsec() / 1000.0;
            for (int frame = 0; frame < 240; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            string absoluteOutput = ProjectSettings.GlobalizePath(output);
            string? directory = Path.GetDirectoryName(absoluteOutput);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            Image image = GetViewport().GetTexture().GetImage();
            Error saveResult = image.SavePng(absoluteOutput);
            int sourceCount = world.NearbyLightSources(
                    fire.X - 12, fire.X + 12, fire.Z - 12, fire.Z + 12).Count;
            Vector2I lightChunk = new(
                    Mathf.FloorToInt(fire.X / (float)VoxelWorld.ChunkX),
                    Mathf.FloorToInt(fire.Z / (float)VoxelWorld.ChunkZ));
            VoxelLightData light = world.LightData(lightChunk);
            int localX = fire.X - lightChunk.X * VoxelWorld.ChunkX;
            int localZ = fire.Z - lightChunk.Y * VoxelWorld.ChunkZ;
            byte flameLevel = light.BlockAt(localX, fire.Y + 1, localZ, world.Height);
            byte adjacentLevel = light.BlockAt(Mathf.Min(localX + 1, VoxelWorld.ChunkX - 1),
                    fire.Y + 1, localZ, world.Height);
            GD.Print($"WARM_LIGHT_CAPTURE_RESULT success={(saveResult == Error.Ok)} mode={mode} chunks={world.ChunkCount} sources={sourceCount} flame_level={flameLevel} adjacent_level={adjacentLevel} elapsed={Time.GetTicksMsec() / 1000.0 - started:F2}s size={image.GetWidth()}x{image.GetHeight()} path={absoluteOutput}");
            if (saveResult != Error.Ok)
                throw new IOException($"Screenshot save failed: {saveResult}.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Warm lighting visual capture failed: {exception}");
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
