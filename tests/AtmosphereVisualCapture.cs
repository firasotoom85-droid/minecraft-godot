using System;
using System.Globalization;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Captures one deterministic real-scene atmosphere frame for visual review.
/// Usage:
/// godot --path . res://tests/AtmosphereVisualCapture.tscn --
///   <seed> <generator-version> <x> <z> <time-of-day> <Clear|Cloudy|Rain> <output.png> [include-torch:true|false] [view-distance:3-5] [none|sealed|sealed-torch|entrance|entrance-torch]
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
            bool includeTorch = args.Length > 7
                    && bool.TryParse(args[7], out bool parsedTorch) && parsedTorch;
            int captureViewDistance = Math.Clamp(ParseInt(args, 8, 3), 3, 5);
            string caveMode = args.Length > 9
                    ? args[9].Trim().ToLowerInvariant()
                    : includeTorch ? "entrance-torch" : "none";
            bool includeCave = caveMode is "sealed" or "sealed-torch"
                    or "entrance" or "entrance-torch";
            bool connectEntrance = caveMode is "entrance" or "entrance-torch";
            bool placeTorch = caveMode is "sealed-torch" or "entrance-torch";
            string exposureMode = args.Length > 10 ? args[10].Trim().ToLowerInvariant() : "none";
            if (exposureMode is not ("none" or "water-exit" or "biome-pair")
                    || (exposureMode != "none" && includeCave))
            {
                GD.PushError($"Unsupported or conflicting exposure capture mode '{exposureMode}'.");
                GetTree().Quit(2);
                return;
            }

            if (caveMode != "none" && !includeCave)
            {
                GD.PushError($"Unsupported cave capture mode '{caveMode}'.");
                GetTree().Quit(2);
                return;
            }

            int expectedChunkCount = (captureViewDistance * 2 + 1)
                    * (captureViewDistance * 2 + 1);

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
            world.SetViewDistance(captureViewDistance);
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
                Attributes = player.Camera.Attributes,
            };
            game.AddChild(camera);
            camera.GlobalPosition = target + new Vector3(62.0f, 72.0f, 78.0f);
            camera.LookAt(target + new Vector3(0.0f, 5.0f, 0.0f), Vector3.Up);

            GD.Print($"ATMOSPHERE_CAPTURE_START seed={seed} version={version} center=({centerX},{centerZ}) time={world.TimeOfDay:F3} weather={weatherState} cave_mode={caveMode} exposure_mode={exposureMode} view_distance={captureViewDistance} expected_chunks={expectedChunkCount} output={output}");
            double started = Time.GetTicksMsec() / 1000.0;
            int frames = 0;
            while (frames < 1800)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
                double elapsed = Time.GetTicksMsec() / 1000.0 - started;
                if (elapsed >= 8.0 && world.ChunkCount >= expectedChunkCount)
                {
                    break;
                }
            }

            if (world.ChunkCount < expectedChunkCount)
            {
                GD.PushError($"World did not stream the requested inspection radius (count={world.ChunkCount}, expected={expectedChunkCount}).");
                GetTree().Quit(6);
                return;
            }

            if (includeCave)
            {
                // Pick a nearby, flat, dry column so every mode starts from
                // the same deterministic terrain fixture and camera placement.
                // the surfaces it lights are not hidden by a river bank.
                int bestX = centerX;
                int bestZ = centerZ;
                int bestHeight = int.MinValue;
                int bestDistance = int.MaxValue;
                for (int dz = -24; dz <= 24; dz++)
                for (int dx = -24; dx <= 24; dx++)
                {
                    int sampleX = centerX + dx;
                    int sampleZ = centerZ + dz;
                    int sampleHeight = world.SurfaceHeight(sampleX, sampleZ);
                    if (sampleHeight <= 2 || sampleHeight + 2 >= world.Height)
                        continue;

                    BlockId surface = world.GetBlock(sampleX, sampleHeight, sampleZ);
                    if (!BlockDb.IsSolid(surface) || BlockDb.IsTransparent(surface)
                            || BlockDb.IsWater(surface))
                        continue;

                    int neighborDelta = 0;
                    for (int nz = -1; nz <= 1; nz++)
                    for (int nx = -1; nx <= 1; nx++)
                    {
                        if (nx == 0 && nz == 0)
                            continue;
                        neighborDelta = Math.Max(neighborDelta,
                                Math.Abs(world.SurfaceHeight(sampleX + nx, sampleZ + nz) - sampleHeight));
                    }
                    if (neighborDelta > 2)
                        continue;

                    int distance = dx * dx + dz * dz;
                    if (sampleHeight > bestHeight
                            || (sampleHeight == bestHeight && distance < bestDistance))
                    {
                        bestX = sampleX;
                        bestZ = sampleZ;
                        bestHeight = sampleHeight;
                        bestDistance = distance;
                    }
                }

                if (bestHeight != int.MinValue)
                {
                    centerX = bestX;
                    centerZ = bestZ;
                    height = bestHeight;
                    target = new Vector3(centerX + 0.5f, height + 1.0f, centerZ + 0.5f);
                }

                // Carve a small enclosed chamber below the surface. Every edit
                // uses SetBlock; this world is disposable and never saved.
                int floorY = Math.Max(2, height - 12);
                for (int y = floorY + 1; y <= floorY + 4; y++)
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (world.GetBlock(x, y, z) != BlockId.Air
                            && !world.SetBlock(x, y, z, BlockId.Air))
                    {
                        GD.PushError("Could not carve the temporary cave through VoxelWorld.SetBlock.");
                        GetTree().Quit(9);
                        return;
                    }
                }

                // Entrance modes cut the same narrow passage and vertical
                // shaft. Sealed modes leave the roof intact so the camera sees
                // a true zero-skylight room rather than a dim outdoor pocket.
                int entranceX = centerX + 10;
                int entranceSurface = world.SurfaceHeight(entranceX, centerZ);
                if (connectEntrance)
                {
                    for (int y = floorY + 1; y <= entranceSurface + 1; y++)
                    {
                        if (world.GetBlock(entranceX, y, centerZ) != BlockId.Air
                                && !world.SetBlock(entranceX, y, centerZ, BlockId.Air))
                        {
                            GD.PushError("Could not open the temporary skylight shaft through VoxelWorld.SetBlock.");
                            GetTree().Quit(9);
                            return;
                        }
                    }

                    for (int dx = 3; dx < 10; dx++)
                    for (int y = floorY + 2; y <= floorY + 3; y++)
                    {
                        int x = centerX + dx;
                        if (world.GetBlock(x, y, centerZ) != BlockId.Air
                                && !world.SetBlock(x, y, centerZ, BlockId.Air))
                        {
                            GD.PushError("Could not open the temporary cave passage through VoxelWorld.SetBlock.");
                            GetTree().Quit(9);
                            return;
                        }
                    }
                }

                int torchY = floorY + 1;
                if (placeTorch && !world.SetBlock(centerX, torchY, centerZ, BlockId.Torch))
                {
                    GD.PushError("Could not place the temporary visual-test torch through VoxelWorld.SetBlock.");
                    GetTree().Quit(9);
                    return;
                }

                target = new Vector3(centerX + 0.5f, torchY + 0.5f, centerZ + 0.5f);
                // DayNight samples the production first-person camera for
                // cave exposure even though this fixture renders from its
                // separate composition camera. Keep that camera inside the
                // same disposable chamber so the screenshot exercises the
                // actual baked-light-driven ambient/fog transition.
                player.GlobalPosition = new Vector3(
                        centerX + 0.5f, floorY + 0.15f, centerZ + 0.5f);
                camera.Fov = 64.0f;
                camera.GlobalPosition = target + new Vector3(2.0f, 1.2f, 2.0f);
                camera.LookAt(target, Vector3.Up);
                for (int frame = 0; frame < 60; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                bool hasEntranceExposure = world.TrySampleCachedSkyLight(
                        new Vector3(entranceX + 0.5f, entranceSurface + 1.5f, centerZ + 0.5f),
                        out float entranceSkyLight);
                float passageSkyLight = 0.0f;
                bool hasPassageExposure = connectEntrance && world.TrySampleCachedSkyLight(
                        new Vector3(centerX + 8.5f, floorY + 2.5f, centerZ + 0.5f),
                        out passageSkyLight);
                bool hasRoomExposure = world.TrySampleCachedSkyLight(
                        player.Camera.GlobalPosition, out float roomSkyLight);
                float cameraMaxSensitivity = player.Camera.Attributes
                        is CameraAttributesPractical cameraAttributes
                        ? cameraAttributes.AutoExposureMaxSensitivity : -1.0f;
                byte torchBlockLevel = 0;
                byte adjacentBlockLevel = 0;
                if (placeTorch)
                {
                    Vector2I lightChunk = new(
                            Mathf.FloorToInt(centerX / (float)VoxelWorld.ChunkX),
                            Mathf.FloorToInt(centerZ / (float)VoxelWorld.ChunkZ));
                    VoxelLightData light = world.LightData(lightChunk);
                    int localX = centerX - lightChunk.X * VoxelWorld.ChunkX;
                    int localZ = centerZ - lightChunk.Y * VoxelWorld.ChunkZ;
                    torchBlockLevel = light.BlockAt(localX, torchY, localZ, world.Height);
                    adjacentBlockLevel = light.BlockAt(
                            Mathf.Min(localX + 1, VoxelWorld.ChunkX - 1),
                            torchY, localZ, world.Height);
                }

                GD.Print($"CAVE_EXPOSURE_GRADIENT mode={caveMode} " +
                        $"outside={hasEntranceExposure}:{entranceSkyLight:F2} " +
                        $"passage={(connectEntrance ? hasPassageExposure.ToString() + ":" + passageSkyLight.ToString("F2") : "sealed")} " +
                        $"room={hasRoomExposure}:{roomSkyLight:F2} " +
                        $"darkness={DayNight.EvaluateCaveDarkness(roomSkyLight):F3} " +
                        $"camera_iso_max={cameraMaxSensitivity:F1} " +
                        $"ambient={dayNight.WorldEnv!.Environment.AmbientLightEnergy:F3} " +
                        $"torch_block={torchBlockLevel} adjacent_block={adjacentBlockLevel}");
            }
            else
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            if (exposureMode == "water-exit")
            {
                int floorY = Math.Max(2, height - 12);
                int waterY = floorY + 1;
                for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (world.GetBlock(x, waterY, z) != BlockId.Air
                            && !world.SetBlock(x, waterY, z, BlockId.Air))
                    {
                        GD.PushError("Could not clear the temporary immersion chamber through VoxelWorld.SetBlock.");
                        GetTree().Quit(9);
                        return;
                    }
                }

                // Connect a narrow dry shaft to the surface. The water pool is
                // kept in its own chamber so its volume is deterministic.
                int shaftX = centerX + 10;
                for (int y = waterY; y <= height + 1; y++)
                {
                    if (world.GetBlock(shaftX, y, centerZ) != BlockId.Air
                            && !world.SetBlock(shaftX, y, centerZ, BlockId.Air))
                    {
                        GD.PushError("Could not open the temporary water-exit shaft through VoxelWorld.SetBlock.");
                        GetTree().Quit(9);
                        return;
                    }
                }

                for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (world.GetBlock(x, waterY, z) != BlockId.Water
                            && !world.SetBlock(x, waterY, z, BlockId.Water))
                    {
                        GD.PushError("Could not fill the temporary immersion pool through VoxelWorld.SetBlock.");
                        GetTree().Quit(9);
                        return;
                    }
                }

                Vector3 waterTarget = new(centerX + 0.5f, waterY + 0.5f, centerZ + 0.5f);
                player.GlobalPosition = new Vector3(centerX + 0.5f, floorY + 0.05f, centerZ + 0.5f);
                camera.Fov = 68.0f;
                camera.GlobalPosition = waterTarget + new Vector3(2.0f, 0.8f, 2.0f);
                camera.LookAt(waterTarget, Vector3.Up);
                for (int frame = 0; frame < 90; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                camera.Environment = player.Camera.Environment;
                GD.Print($"WATER_EXPOSURE_SUBMERGED blend={player.WaterViewBlend:F3} " +
                        $"underwater={player.Underwater} " +
                        $"iso_min={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMinSensitivity ?? -1.0f:F1} " +
                        $"iso_max={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMaxSensitivity ?? -1.0f:F1}");
                SaveViewportCapture(AddOutputSuffix(output, "-underwater"));

                player.GlobalPosition = new Vector3(centerX + 10.5f, height + 1.0f, centerZ + 0.5f);
                camera.GlobalPosition = waterTarget + new Vector3(12.0f, 9.0f, 12.0f);
                camera.LookAt(waterTarget, Vector3.Up);
                for (int frame = 0; frame < 60; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                camera.Environment = player.Camera.Environment;
                output = AddOutputSuffix(output, "-emerged");
                GD.Print($"WATER_EXPOSURE_EMERGED blend={player.WaterViewBlend:F3} " +
                        $"underwater={player.Underwater} " +
                        $"iso_min={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMinSensitivity ?? -1.0f:F1} " +
                        $"iso_max={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMaxSensitivity ?? -1.0f:F1}");
            }
            else if (exposureMode == "biome-pair")
            {
                if (!FindNearestBiome(world, centerX, centerZ, Biome.Snow, 768, 8, out Vector2I snowPosition)
                        || !FindNearestBiome(world, centerX, centerZ, Biome.DenseForest, 768, 8, out Vector2I forestPosition))
                {
                    GD.PushError("Could not find both snow and shaded-forest columns within 768 blocks of the capture origin.");
                    GetTree().Quit(10);
                    return;
                }

                int snowHeight = world.SurfaceHeight(snowPosition.X, snowPosition.Y);
                player.GlobalPosition = new Vector3(snowPosition.X + 0.5f, snowHeight + 1.0f, snowPosition.Y + 0.5f);
                camera.GlobalPosition = player.GlobalPosition + new Vector3(18.0f, 12.0f, 22.0f);
                camera.LookAt(player.GlobalPosition + new Vector3(0.0f, 3.0f, 0.0f), Vector3.Up);
                await WaitForExposureCapture(world, expectedChunkCount);
                camera.Environment = player.Camera.Environment;
                GD.Print($"BIOME_EXPOSURE_SNOW biome={world.BiomeAt(snowPosition.X, snowPosition.Y, snowHeight)} position={snowPosition} height={snowHeight} " +
                        $"iso_min={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMinSensitivity ?? -1.0f:F1} " +
                        $"iso_max={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMaxSensitivity ?? -1.0f:F1}");
                SaveViewportCapture(AddOutputSuffix(output, "-snow"));

                int forestHeight = world.SurfaceHeight(forestPosition.X, forestPosition.Y);
                player.GlobalPosition = new Vector3(forestPosition.X + 0.5f, forestHeight + 1.0f, forestPosition.Y + 0.5f);
                camera.GlobalPosition = player.GlobalPosition + new Vector3(18.0f, 12.0f, 22.0f);
                camera.LookAt(player.GlobalPosition + new Vector3(0.0f, 3.0f, 0.0f), Vector3.Up);
                await WaitForExposureCapture(world, expectedChunkCount);
                camera.Environment = player.Camera.Environment;
                output = AddOutputSuffix(output, "-forest");
                GD.Print($"BIOME_EXPOSURE_FOREST biome={world.BiomeAt(forestPosition.X, forestPosition.Y, forestHeight)} position={forestPosition} height={forestHeight} " +
                        $"iso_min={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMinSensitivity ?? -1.0f:F1} " +
                        $"iso_max={(player.Camera.Attributes as CameraAttributesPractical)?.AutoExposureMaxSensitivity ?? -1.0f:F1}");
            }

            if (player.Camera.Attributes is CameraAttributesPractical exposureAttributes)
            {
                GD.Print($"ATMOSPHERE_EXPOSURE_CONTEXT biome={world.BiomeAt(
                        Mathf.FloorToInt(player.GlobalPosition.X),
                        Mathf.FloorToInt(player.GlobalPosition.Z),
                        world.SurfaceHeight(Mathf.FloorToInt(player.GlobalPosition.X),
                                Mathf.FloorToInt(player.GlobalPosition.Z)))} " +
                        $"underwater={player.Underwater} water_blend={player.WaterViewBlend:F3} " +
                        $"iso_min={exposureAttributes.AutoExposureMinSensitivity:F1} " +
                        $"iso_max={exposureAttributes.AutoExposureMaxSensitivity:F1} " +
                        $"adapt_speed={exposureAttributes.AutoExposureSpeed:F2}");
            }

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

    private void SaveViewportCapture(string output)
    {
        string absoluteOutput = ProjectSettings.GlobalizePath(output);
        string? directory = Path.GetDirectoryName(absoluteOutput);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        Error result = GetViewport().GetTexture().GetImage().SavePng(absoluteOutput);
        if (result != Error.Ok)
            throw new IOException($"Could not save exposure comparison capture {absoluteOutput}: {result}");
        GD.Print($"EXPOSURE_COMPARISON_CAPTURE success=true path={absoluteOutput}");
    }

    private static string AddOutputSuffix(string output, string suffix)
    {
        // Preserve res:// exactly. System.IO.Path normalizes the double slash
        // after the colon into res:/, which Godot then treats as a filesystem
        // directory rather than a project resource path.
        int separator = output.LastIndexOf('/');
        string prefix = separator >= 0 ? output[..(separator + 1)] : string.Empty;
        string filename = separator >= 0 ? output[(separator + 1)..] : output;
        string extension = Path.GetExtension(filename);
        string stem = Path.GetFileNameWithoutExtension(filename);
        return prefix + stem + suffix + extension;
    }

    private async System.Threading.Tasks.Task WaitForExposureCapture(
            VoxelWorld world, int expectedChunkCount)
    {
        double started = Time.GetTicksMsec() / 1000.0;
        int frames = 0;
        while (frames < 1800)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
            if (Time.GetTicksMsec() / 1000.0 - started >= 8.0
                    && world.ChunkCount >= expectedChunkCount)
                return;
        }

        if (world.ChunkCount < expectedChunkCount)
            throw new InvalidOperationException($"Biome capture streamed {world.ChunkCount} chunks; expected at least {expectedChunkCount}.");
    }

    private static bool FindNearestBiome(
            VoxelWorld world, int originX, int originZ, Biome target, int radius,
            int stride, out Vector2I position)
    {
        position = default;
        int bestDistance = int.MaxValue;
        bool found = false;
        for (int z = -radius; z <= radius; z += stride)
        for (int x = -radius; x <= radius; x += stride)
        {
            int distance = x * x + z * z;
            if (distance >= bestDistance)
                continue;
            int sampleX = originX + x;
            int sampleZ = originZ + z;
            int surface = world.SurfaceHeight(sampleX, sampleZ);
            Biome biome = world.BiomeAt(sampleX, sampleZ, surface);
            bool matches = target == Biome.DenseForest
                    ? biome is Biome.DenseForest or Biome.Taiga or Biome.Birch
                            or Biome.Jungle or Biome.Swamp
                    : biome == target;
            if (!matches)
                continue;
            bestDistance = distance;
            position = new Vector2I(sampleX, sampleZ);
            found = true;
        }

        return found;
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
