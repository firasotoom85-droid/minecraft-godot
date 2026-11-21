using System;
using Godot;

namespace Minecraft;

/// <summary>
/// Runs the real game scene and captures a GPU-rendered world-generation
/// inspection frame. Invoke with:
/// godot --path . res://tests/WorldgenVisualCapture.tscn -- <seed> <genVersion> <x> <z> <outputPath>
/// </summary>
public partial class WorldgenVisualCapture : Node
{
    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int seed = ParseArg(args, 0, 11235813);
        int version = ParseArg(args, 1, 7);
        int centerX = ParseArg(args, 2, -128);
        int centerZ = ParseArg(args, 3, -488);
        string output = args.Length > 4
                ? args[4]
                : $"res://docs/worldgen/screenshots/v{version}-seed-{seed}.png";

        if (version is < 1 or > 8)
        {
            GD.PushError($"Unsupported world generation version {version}.");
            GetTree().Quit(2);
            return;
        }

        DisplayServer.WindowSetSize(new Vector2I(1600, 900));
        Engine.MaxFps = 60;
        string worldDir = $"user://worlds/visual-{seed}-{version}-{Time.GetTicksUsec()}";
        WorldManifest manifest = new(
                $"Worldgen visual seed {seed}", seed, version,
                WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                WorldStore.NowUtc(), "creative", "normal");
        if (!WorldStore.WriteManifest(worldDir, manifest))
        {
            GD.PushError($"Could not create visual test world at {worldDir}.");
            GetTree().Quit(3);
            return;
        }

        GameSession.Instance.SelectWorld(worldDir);
        PackedScene gameScene = GD.Load<PackedScene>("res://scenes/main.tscn");
        if (gameScene == null)
        {
            GD.PushError("Could not load the existing main game scene.");
            GetTree().Quit(4);
            return;
        }

        Node3D game = gameScene.Instantiate<Node3D>();
        AddChild(game);
        VoxelWorld world = game.GetNode<VoxelWorld>("World");
        Player player = game.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);

        int height = world.SurfaceHeight(centerX, centerZ);
        Vector3 target = new(centerX + 0.5f, height + 1.0f, centerZ + 0.5f);
        player.GlobalPosition = target + new Vector3(0, 32.0f, 0);
        world.SetViewDistance(3);
        world.SetLodRadius(0.0f);
        player.Camera.Current = false;

        Camera3D camera = new()
        {
            Name = "WorldgenVisualCamera",
            Fov = 76.0f,
            Far = 500.0f,
            Current = true,
        };
        game.AddChild(camera);
        camera.GlobalPosition = target + new Vector3(62.0f, 72.0f, 78.0f);
        camera.LookAt(target + new Vector3(0.0f, 5.0f, 0.0f), Vector3.Up);

        GD.Print($"WORLDGEN_CAPTURE_START version={version} seed={seed} center=({centerX},{centerZ}) height={height} output={output}");
        double started = Time.GetTicksMsec() / 1000.0;
        int frames = 0;
        while (frames < 1200)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
            double elapsed = Time.GetTicksMsec() / 1000.0 - started;
            if (elapsed >= 8.0 && world.ChunkCount >= 16)
            {
                break;
            }
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Image image = GetViewport().GetTexture().GetImage();
        string absoluteOutput = ProjectSettings.GlobalizePath(output);
        Error saveResult = image.SavePng(absoluteOutput);
        GD.Print($"WORLDGEN_CAPTURE_RESULT success={(saveResult == Error.Ok)} chunks={world.ChunkCount} frames={frames} elapsed={Time.GetTicksMsec() / 1000.0 - started:F2}s size={image.GetWidth()}x{image.GetHeight()} path={absoluteOutput}");
        if (saveResult != Error.Ok)
        {
            GD.PushError($"Screenshot save failed: {saveResult}");
            GetTree().Quit(5);
            return;
        }

        game.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit();
    }

    private static int ParseArg(string[] args, int index, int fallback)
    {
        return index < args.Length && int.TryParse(args[index], out int value)
                ? value : fallback;
    }
}
