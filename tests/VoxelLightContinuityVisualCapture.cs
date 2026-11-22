using System;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Repeatable Forward+ capture of transmission through glass, water, foliage
/// gaps, bottom slabs and stairs using the production world and chunk mesher.
/// Every fixture edit is routed through VoxelWorld.SetBlock.
/// Usage:
/// godot --path . res://tests/VoxelLightContinuityVisualCapture.tscn --
///   <seed> <generator-version> <center-x> <center-z> <output.png>
/// </summary>
public partial class VoxelLightContinuityVisualCapture : Node
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
            int centerX = ParseInt(args, 2, 8);
            int centerZ = ParseInt(args, 3, 8);
            string output = args.Length > 4 ? args[4] : "/tmp/voxel-light-continuity.png";

            if (version is < 1 or > WorldStore.CurrentGenVersion)
                throw new ArgumentOutOfRangeException(nameof(args), "Generator version is outside the supported range.");

            DisplayServer.WindowSetSize(new Vector2I(1600, 900));
            Engine.MaxFps = 60;
            Settings.AutoQuality = false;
            worldDir = $"user://tests/voxel-light-continuity-{seed}-{version}-{Time.GetTicksUsec()}";
            WorldManifest manifest = new(
                    "Voxel light continuity comparison", seed, version,
                    WorldStore.DeriveNoiseSeeds(seed), WorldStore.NowUtc(),
                    WorldStore.NowUtc(), "creative", "normal");
            if (!WorldStore.WriteManifest(worldDir, manifest))
                throw new IOException($"Could not create temporary world at {worldDir}.");

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
            // Freeze the existing event-driven fluid owner so the source-water
            // cell remains in the fixed comparison tunnel for every capture.
            if (FindNode<FluidSystem>(game) is { } fluids)
            {
                fluids.SetProcess(false);
                fluids.SetPhysicsProcess(false);
            }
            world.TimeOfDay = 0.50;
            world.SetViewDistance(3);
            world.SetLodRadius(0.0f);
            if (FindNode<Gfx>(game) is { } gfx) gfx.ApplyPreset(Gfx.DefaultPreset, false);

            Vector3 previewCenter = new(centerX + 0.5f, 0.0f, centerZ + 0.5f);
            world.BuildPreview(previewCenter, 1);

            int baseY = 0;
            for (int dz = -5; dz <= 5; dz++)
            for (int dx = -7; dx <= 7; dx++)
                baseY = Math.Max(baseY, world.SurfaceHeight(centerX + dx, centerZ + dz));
            baseY += 8;
            if (baseY + 6 >= world.Height)
                throw new InvalidOperationException("Selected fixture coordinate has insufficient vertical clearance.");

            // Four isolated, roofed one-cell tunnels. Their only light path is
            // the centre voxel, so sampled levels compare material/shape passage
            // instead of light travelling around the test block.
            BuildTunnel(world, centerX, centerZ - 4, baseY, BlockId.Glass, 0);
            BuildTunnel(world, centerX, centerZ - 2, baseY, BlockId.Water, 0);
            BuildTunnel(world, centerX, centerZ, baseY, BlockId.PlanksSlab, BlockShapes.SlabBottom);
            BuildTunnel(world, centerX, centerZ + 2, baseY, BlockId.PlanksStairs, 0);

            // Two separate, walled skylight shafts compare a leaf canopy voxel
            // with a true air gap at the same height.
            Vector3I foliageColumn = new(centerX - 3, baseY + 4, centerZ + 5);
            Vector3I gapColumn = new(centerX + 3, baseY + 4, centerZ + 5);
            BuildSkylightShaft(world, foliageColumn, baseY + 5, BlockId.Leaves);
            BuildSkylightShaft(world, gapColumn, baseY + 5, BlockId.Air);

            // A second isolated slab orientation checks that the underside is
            // closed even though the upper cavity is open.
            world.SetBlock(centerX - 6, baseY + 1, centerZ + 4,
                    BlockId.PlanksSlab, BlockShapes.SlabTop);

            Camera3D camera = new()
            {
                Name = "VoxelLightContinuityCamera",
                Fov = 54.0f,
                Far = 180.0f,
                Current = true,
            };
            game.AddChild(camera);
            camera.GlobalPosition = new Vector3(centerX + 1.5f, baseY + 9.0f, centerZ + 14.0f);
            camera.LookAt(new Vector3(centerX + 0.5f, baseY + 1.0f, centerZ + 0.5f), Vector3.Up);

            GD.Print($"VOXEL_LIGHT_CONTINUITY_START seed={seed} version={version} center=({centerX},{centerZ}) baseY={baseY}");
            for (int frame = 0; frame < 240; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            int sampleX = centerX + 4;
            int sampleZ0 = centerZ - 4;
            int sampleZ1 = centerZ - 2;
            int sampleZ2 = centerZ;
            int sampleZ3 = centerZ + 2;
            byte glassLevel = SampleBlockLight(world, sampleX, baseY + 1, sampleZ0);
            byte waterLevel = SampleBlockLight(world, sampleX, baseY + 1, sampleZ1);
            byte slabLevel = SampleBlockLight(world, sampleX, baseY + 1, sampleZ2);
            byte stairLevel = SampleBlockLight(world, sampleX, baseY + 1, sampleZ3);
            byte foliageSky = SampleSkyLight(world, foliageColumn.X, foliageColumn.Y - 1, foliageColumn.Z);
            byte foliageBelow = SampleSkyLight(world, foliageColumn.X, foliageColumn.Y - 2, foliageColumn.Z);
            byte gapBelow = SampleSkyLight(world, gapColumn.X, gapColumn.Y - 1, gapColumn.Z);
            GD.Print($"VOXEL_LIGHT_CONTINUITY_SAMPLES glass={glassLevel} water={waterLevel} bottom_slab={slabLevel} upright_stairs={stairLevel} foliage_sky={foliageSky} foliage_below={foliageBelow} gap_below={gapBelow} chunks={world.ChunkCount}");

            string absoluteOutput = ProjectSettings.GlobalizePath(output);
            string? directory = Path.GetDirectoryName(absoluteOutput);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            Image image = GetViewport().GetTexture().GetImage();
            Error saveResult = image.SavePng(absoluteOutput);
            GD.Print($"VOXEL_LIGHT_CONTINUITY_RESULT success={(saveResult == Error.Ok)} size={image.GetWidth()}x{image.GetHeight()} path={absoluteOutput}");
            if (saveResult != Error.Ok)
                throw new IOException($"Screenshot save failed: {saveResult}.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Voxel light continuity capture failed: {exception}");
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

    private static void BuildTunnel(VoxelWorld world, int centerX, int centerZ,
            int baseY, BlockId passageBlock, byte state)
    {
        for (int x = centerX - 5; x <= centerX + 8; x++)
            world.SetBlock(x, baseY, centerZ, BlockId.Stone);
        // The visual fixture stays open-topped so the camera can inspect each
        // source and passage block. Exact path isolation is asserted headlessly.


        for (int x = centerX - 5; x <= centerX + 8; x++)
        for (int y = baseY; y <= baseY + 2; y++)
        {
            world.SetBlock(x, y, centerZ - 1, BlockId.Stone);
            world.SetBlock(x, y, centerZ + 1, BlockId.Stone);
        }

        for (int y = baseY; y <= baseY + 2; y++)
        {
            world.SetBlock(centerX - 5, y, centerZ, BlockId.Stone);
            world.SetBlock(centerX + 8, y, centerZ, BlockId.Stone);
        }

        for (int x = centerX - 4; x <= centerX + 7; x++)
            world.SetBlock(x, baseY + 1, centerZ, BlockId.Air);

        world.SetBlock(centerX - 4, baseY + 1, centerZ, BlockId.Torch);
        world.SetBlock(centerX + 1, baseY + 1, centerZ, passageBlock, state);
    }

    private static void BuildSkylightShaft(VoxelWorld world, Vector3I sample,
            int canopyY, BlockId canopyBlock)
    {
        for (int y = sample.Y - 4; y <= canopyY; y++)
        {
            world.SetBlock(sample.X - 1, y, sample.Z, BlockId.Stone);
            world.SetBlock(sample.X + 1, y, sample.Z, BlockId.Stone);
            world.SetBlock(sample.X, y, sample.Z - 1, BlockId.Stone);
            world.SetBlock(sample.X, y, sample.Z + 1, BlockId.Stone);
        }

        world.SetBlock(sample.X, canopyY, sample.Z, canopyBlock);
        for (int y = sample.Y - 4; y < canopyY; y++)
            world.SetBlock(sample.X, y, sample.Z, BlockId.Air);
    }

    private static byte SampleBlockLight(VoxelWorld world, int x, int y, int z)
    {
        Vector2I cp = new(
                Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
        VoxelLightData data = world.LightData(cp);
        return data.BlockAt(x - cp.X * VoxelWorld.ChunkX, y,
                z - cp.Y * VoxelWorld.ChunkZ, world.Height);
    }

    private static byte SampleSkyLight(VoxelWorld world, int x, int y, int z)
    {
        Vector2I cp = new(
                Mathf.FloorToInt(x / (float)VoxelWorld.ChunkX),
                Mathf.FloorToInt(z / (float)VoxelWorld.ChunkZ));
        VoxelLightData data = world.LightData(cp);
        return data.SkyAt(x - cp.X * VoxelWorld.ChunkX, y,
                z - cp.Y * VoxelWorld.ChunkZ, world.Height);
    }

    private static int ParseInt(string[] args, int index, int fallback) =>
        args.Length > index && int.TryParse(args[index], out int value) ? value : fallback;

    private static T? FindNode<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (Node child in root.GetChildren())
            if (FindNode<T>(child) is { } found) return found;
        return null;
    }
}
