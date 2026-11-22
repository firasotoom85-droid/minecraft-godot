using System;
using System.Globalization;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Captures one deterministic real-scene atmosphere frame for visual review.
/// Usage:
/// godot --path . res://tests/AtmosphereVisualCapture.tscn --
///   <seed> <generator-version> <x> <z> <time-of-day> <Clear|Cloudy|Rain> <output.png>
/// </summary>
public partial class AtmosphereVisualCapture : Node
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
            int centerX = ParseInt(args, 2, 512);
            int centerZ = ParseInt(args, 3, -372);
            double timeOfDay = ParseDouble(args, 4, 0.5);
            Weather.State weatherState = ParseWeather(args, 5);
            string output = args.Length > 6
                    ? args[6]
                    : $"res://docs/visual/screenshots/atmosphere-{weatherState.ToString().ToLowerInvariant()}.png";

            if (version is < 1 or > WorldStore.CurrentGenVersion)
            {
                GD.PushError($"Unsupported world generation version {version}; supported versions are 1–{WorldStore.CurrentGenVersion}.");
                GetTree().Quit(2);
                return;
            }

            DisplayServer.WindowSetSize(new Vector2I(1600, 900));
            Engine.MaxFps = 60;
            worldDir = $"user://tests/atmosphere-{seed}-{version}-{Time.GetTicksUsec()}";
            WorldManifest manifest = new(
                    $"Atmosphere visual seed {seed}", seed, version,
                    WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                    WorldStore.NowUtc(), "creative", "normal");
            if (!WorldStore.WriteManifest(worldDir, manifest))
            {
                GD.PushError($"Could not create visual test world at {worldDir}.");
                GetTree().Quit(3);
                return;
            }

            GameSession.Instance.SelectWorld(worldDir);
            PackedScene? gameScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            if (gameScene == null)
            {
                GD.PushError("Could not load the existing main game scene.");
                GetTree().Quit(4);
                return;
            }

            game = gameScene.Instantiate<Node3D>();
            AddChild(game);

            VoxelWorld world = game.GetNode<VoxelWorld>("World");
            Player player = game.GetNode<Player>("Player");
            DayNight? dayNight = FindNode<DayNight>(game);
            Weather? weather = FindNode<Weather>(game);
            if (dayNight == null || weather == null)
            {
                GD.PushError("Main scene did not initialize its existing day/night and weather systems.");
                GetTree().Quit(5);
                return;
            }

            player.SetPhysicsProcess(false);
            player.Camera.Current = false;
            Hud? hud = FindNode<Hud>(game);
            if (hud != null)
            {
                hud.Visible = false;
            }

            world.TimeOfDay = timeOfDay - Math.Floor(timeOfDay);
            dayNight.Paused = true;
            weather.Current = weatherState;
            weather.Cloudiness = Weather.TargetCloudinessFor(weatherState);
            world.SetViewDistance(3);
            world.SetLodRadius(0.0f);

            int height = world.SurfaceHeight(centerX, centerZ);
            Vector3 target = new(centerX + 0.5f, height + 1.0f, centerZ + 0.5f);
            player.GlobalPosition = target + new Vector3(0, 32.0f, 0);

            Camera3D camera = new()
            {
                Name = "AtmosphereVisualCamera",
                Fov = 76.0f,
                Far = 500.0f,
                Current = true,
            };
            game.AddChild(camera);
            camera.GlobalPosition = target + new Vector3(62.0f, 72.0f, 78.0f);
            camera.LookAt(target + new Vector3(0.0f, 5.0f, 0.0f), Vector3.Up);

            GD.Print($"ATMOSPHERE_CAPTURE_START seed={seed} version={version} center=({centerX},{centerZ}) time={world.TimeOfDay:F3} weather={weatherState} output={output}");
            double started = Time.GetTicksMsec() / 1000.0;
            int frames = 0;
            while (frames < 1800)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
                double elapsed = Time.GetTicksMsec() / 1000.0 - started;
                if (elapsed >= 8.0 && world.ChunkCount >= 16)
                {
                    break;
                }
            }

            if (world.ChunkCount < 16)
            {
                GD.PushError($"World did not stream enough chunks for a useful capture (count={world.ChunkCount}).");
                GetTree().Quit(6);
                return;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string absoluteOutput = ProjectSettings.GlobalizePath(output);
            string? outputDirectory = Path.GetDirectoryName(absoluteOutput);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            Image image = GetViewport().GetTexture().GetImage();
            Error saveResult = image.SavePng(absoluteOutput);
            GD.Print($"ATMOSPHERE_CAPTURE_RESULT success={(saveResult == Error.Ok)} chunks={world.ChunkCount} frames={frames} size={image.GetWidth()}x{image.GetHeight()} path={absoluteOutput}");
            if (saveResult != Error.Ok)
            {
                GD.PushError($"Screenshot save failed: {saveResult}");
                GetTree().Quit(7);
                return;
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Atmosphere visual capture failed: {exception}");
            GetTree().Quit(8);
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
                    {
                        Directory.Delete(absoluteWorldDir, recursive: true);
                    }
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
        if (root is T match)
        {
            return match;
        }

        foreach (Node child in root.GetChildren())
        {
            T? found = FindNode<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static int ParseInt(string[] args, int index, int fallback) =>
            index < args.Length && int.TryParse(args[index], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static double ParseDouble(string[] args, int index, double fallback) =>
            index < args.Length && double.TryParse(args[index], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
                    ? value : fallback;

    private static Weather.State ParseWeather(string[] args, int index) =>
            index < args.Length && Enum.TryParse(args[index], true, out Weather.State state)
                    && state != Weather.State.Storm
                    ? state : Weather.State.Clear;
}
