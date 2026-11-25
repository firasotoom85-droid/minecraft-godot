using Godot;

namespace Minecraft;

/// <summary>
/// GPU-rendered manual inspection of the production wood atlas and shape mesher.
/// Run with:
/// godot --path . res://tests/WoodArtVisualCapture.tscn -- [seed] [outputPath]
/// </summary>
public partial class WoodArtVisualCapture : Node
{
    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int seed = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 1337;
        string output = args.Length > 1 ? args[1] : "user://wood-art-validation-distant.png";
        string closeOutput = args.Length > 2 ? args[2] : "user://wood-art-validation-close.png";

        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        Engine.MaxFps = 60;
        string worldDir = $"user://worlds/wood-art-{seed}-{Time.GetTicksUsec()}";
        WorldManifest manifest = new(
                $"Wood art visual seed {seed}", seed, WorldStore.CurrentGenVersion,
                WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                WorldStore.NowUtc(), "creative", "normal");
        if (!WorldStore.WriteManifest(worldDir, manifest))
        {
            GD.PushError($"Could not create wood art visual world at {worldDir}.");
            GetTree().Quit(2);
            return;
        }

        GameSession.Instance.SelectWorld(worldDir);
        PackedScene gameScene = GD.Load<PackedScene>("res://scenes/main.tscn");
        if (gameScene == null)
        {
            GD.PushError("Could not load the production main scene.");
            CleanupWorld(worldDir);
            GetTree().Quit(3);
            return;
        }

        Node3D game = gameScene.Instantiate<Node3D>();
        AddChild(game);
        VoxelWorld world = game.GetNode<VoxelWorld>("World");
        Player player = game.GetNode<Player>("Player");
        player.SetPhysicsProcess(false);
        player.Camera.Current = false;
        game.GetNode<CanvasLayer>("HUD").Visible = false;
        world.SetLodRadius(0.0f);

        const int centerX = 0;
        const int centerZ = 0;
        int baseY = System.Math.Max(
                world.SurfaceHeight(centerX, centerZ) + 3, world.SeaLevel + 6);
        Vector3 spawn = new(centerX + 0.5f, baseY + 8.0f, centerZ + 0.5f);
        player.GlobalPosition = spawn;
        world.SetViewDistance(2);

        GD.Print($"WOOD_ART_STREAM_START center=({centerX},{centerZ}) baseY={baseY}");
        double streamingStarted = Time.GetTicksMsec() / 1000.0;
        bool centerReady = false;
        int streamingFrames = 0;
        while (streamingFrames < 1200)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            streamingFrames++;
            if (!centerReady)
                centerReady = world.SetBlock(centerX, baseY - 1, centerZ, BlockId.Stone);
            double elapsed = Time.GetTicksMsec() / 1000.0 - streamingStarted;
            if (centerReady && world.ChunkCount >= 9 && elapsed >= 4.0)
                break;
        }
        if (!centerReady)
        {
            GD.PushError("The capture player's center chunk did not finish streaming.");
            CleanupWorld(worldDir);
            GetTree().Quit(5);
            return;
        }

        int edits = 1;

        bool Put(int x, int y, int z, BlockId id, byte state = 0)
        {
            if (!world.SetBlock(centerX + x, baseY + y, centerZ + z, id, state))
                return false;
            edits++;
            return true;
        }

        // Flatten a compact display pad and remove nearby canopy/terrain clutter.
        for (int z = -5; z <= 5; z++)
            for (int x = -8; x <= 8; x++)
            {
                Put(x, -1, z, BlockId.Stone);
                for (int y = 0; y <= 6; y++)
                    Put(x, y, z, BlockId.Air);
            }

        // Logs show their end-grain cap and vertical bark in the same lighting.
        Put(-7, 0, -2, BlockId.Log);
        Put(-7, 1, -2, BlockId.Log);
        Put(-7, 2, -2, BlockId.Log);
        Put(-5, 0, -2, BlockId.BirchLog);
        Put(-5, 1, -2, BlockId.BirchLog);
        Put(-5, 2, -2, BlockId.BirchLog);

        // Full blocks, both slab halves and a double slab share the plank grain.
        Put(-3, 0, -2, BlockId.Planks);
        Put(-2, 0, -2, BlockId.Planks);
        Put(-1, 0, -2, BlockId.PlanksSlab, BlockShapes.SlabBottom);
        Put(0, 0, -2, BlockId.PlanksSlab, BlockShapes.SlabTop);
        Put(1, 0, -2, BlockId.PlanksSlab, BlockShapes.SlabDouble);

        // Four stair facings exercise the tread rotation and side/end swap.
        for (byte facing = 0; facing < 4; facing++)
            Put(3 + facing, 0, -2, BlockId.PlanksStairs, facing);
        Put(3, 0, 1, BlockId.PlanksStairs, (byte)(BlockShapes.StairFacingMask | BlockShapes.StairUpsideDown));
        Put(4, 0, 1, BlockId.PlanksStairs, (byte)(1 | BlockShapes.StairUpsideDown));

        // Material-response row: stone families, four ores, foliage cutouts,
        // and a small glass pane are viewed together under identical lighting.
        Put(-7, 0, 2, BlockId.Stone);
        Put(-6, 0, 2, BlockId.Cobblestone);
        Put(-5, 0, 2, BlockId.Brick);
        Put(-4, 0, 2, BlockId.Gravel);
        Put(-3, 0, 2, BlockId.CoalOre);
        Put(-2, 0, 2, BlockId.IronOre);
        Put(-1, 0, 2, BlockId.GoldOre);
        Put(0, 0, 2, BlockId.DiamondOre);
        Put(1, 0, 2, BlockId.Leaves);
        Put(2, 0, 2, BlockId.BirchLeaves);
        Put(4, 0, 2, BlockId.Glass);
        Put(4, 1, 2, BlockId.Glass);
        Put(4, 2, 2, BlockId.Glass);
        Put(5, 0, 2, BlockId.Glass);
        Put(5, 1, 2, BlockId.Glass);
        Put(5, 2, 2, BlockId.Glass);

        // Edge/AO comparison row: each partial block is supported by the
        // lower block so its occupied half touches the adjacent cube corner.
        // Lower and upper slabs intentionally use different occupancy at the
        // same corner; upright and inverted stairs expose their internal riser.
        Put(-4, 0, 3, BlockId.Stone);
        Put(-3, 0, 3, BlockId.Stone);
        Put(-3, 1, 3, BlockId.PlanksSlab, BlockShapes.SlabBottom);
        Put(-2, 0, 3, BlockId.Stone);
        Put(-1, 0, 3, BlockId.Stone);
        Put(-1, 1, 3, BlockId.PlanksSlab, BlockShapes.SlabTop);
        Put(0, 0, 3, BlockId.Stone);
        Put(1, 0, 3, BlockId.Stone);
        Put(1, 1, 3, BlockId.PlanksStairs, 0);
        Put(2, 0, 3, BlockId.Stone);
        Put(3, 0, 3, BlockId.Stone);
        Put(3, 1, 3, BlockId.PlanksStairs, BlockShapes.StairUpsideDown);

        Camera3D camera = new()
        {
            Name = "WoodArtValidationCamera",
            Fov = 54.0f,
            Far = 180.0f,
            Current = true,
        };
        game.AddChild(camera);
        Vector3 target = new(centerX + 0.5f, baseY + 1.0f, centerZ + 3.0f);
        camera.GlobalPosition = target + new Vector3(12.5f, 8.5f, 17.0f);
        camera.LookAt(target, Vector3.Up);

        GD.Print($"WOOD_ART_CAPTURE_START seed={seed} baseY={baseY} edits={edits}");
        double started = Time.GetTicksMsec() / 1000.0;
        int frames = 0;
        while (frames < 900)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
            if (frames >= 120 && world.ChunkCount >= 9
                    && Time.GetTicksMsec() / 1000.0 - started >= 4.0)
                break;
        }

        for (int settle = 0; settle < 90; settle++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Image image = GetViewport().GetTexture().GetImage();
        string absoluteOutput = ProjectSettings.GlobalizePath(output);
        Error result = image.SavePng(absoluteOutput);
        GD.Print($"WOOD_ART_CAPTURE_RESULT view=distant success={(result == Error.Ok)} chunks={world.ChunkCount} frames={frames} edits={edits} size={image.GetWidth()}x{image.GetHeight()} path={absoluteOutput}");
        if (result != Error.Ok)
        {
            GD.PushError($"Wood-art distant screenshot save failed: {result}");
            game.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CleanupWorld(worldDir);
            GetTree().Quit(6);
            return;
        }

        // A second close frame makes grain scale, board seams and end grain
        // inspectable without changing the production camera or game state.
        camera.Fov = 48.0f;
        camera.GlobalPosition = target + new Vector3(6.8f, 4.8f, 9.0f);
        camera.LookAt(target, Vector3.Up);
        for (int settle = 0; settle < 45; settle++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        image = GetViewport().GetTexture().GetImage();
        string absoluteCloseOutput = ProjectSettings.GlobalizePath(closeOutput);
        result = image.SavePng(absoluteCloseOutput);
        GD.Print($"WOOD_ART_CAPTURE_RESULT view=close success={(result == Error.Ok)} size={image.GetWidth()}x{image.GetHeight()} path={absoluteCloseOutput}");
        if (result != Error.Ok)
            GD.PushError($"Wood-art close screenshot save failed: {result}");

        game.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        CleanupWorld(worldDir);
        GetTree().Quit(result == Error.Ok ? 0 : 7);
    }

    private static void CleanupWorld(string worldDir)
    {
        string worldsRoot = ProjectSettings.GlobalizePath("user://worlds");
        string testWorld = ProjectSettings.GlobalizePath(worldDir);
        if (System.IO.Path.GetDirectoryName(testWorld) == worldsRoot
                && System.IO.Path.GetFileName(testWorld).StartsWith("wood-art-", System.StringComparison.Ordinal)
                && System.IO.Directory.Exists(testWorld))
            System.IO.Directory.Delete(testWorld, recursive: true);
    }
}
