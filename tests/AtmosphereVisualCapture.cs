using System;
using System.Globalization;
using System.IO;
using Godot;

namespace Minecraft;

/// <summary>
/// Captures one deterministic real-scene atmosphere frame for visual review.
/// Usage:
/// godot --path . res://tests/AtmosphereVisualCapture.tscn --
///   <seed> <generator-version> <x> <z> <time-of-day> <Clear|Cloudy|Rain|Storm> <output.png> [preset-index|include-torch] [view-distance:3-5] [cave-mode|include-hostiles] [exposure-mode] [include-hostiles]
///   Legacy form: [include-torch:true|false] [view-distance] [none|sealed|sealed-torch|entrance|entrance-torch|include-hostiles] [exposure-mode] [include-hostiles]
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
            Weather.State weatherState = Weather.State.Clear;
            if (args.Length > 5 && !TryParseWeather(args[5], out weatherState))
            {
                GD.PushError($"Unknown weather state '{args[5]}'. Expected Clear, Cloudy, Rain or Storm.");
                GetTree().Quit(2);
                return;
            }
            string output = args.Length > 6
                    ? args[6]
                    : $"res://docs/visual/screenshots/atmosphere-{weatherState.ToString().ToLowerInvariant()}.png";
            // One parser serves the historical include-torch/view-distance order
            // and the preset-first form. The third optional token is either a
            // cave mode (#74) or the hostile-silhouette flag (#71); the two are
            // mutually exclusive so a capture always has one deterministic setup.
            int presetIndex = Settings.GfxPreset;
            bool includeTorch = false;
            int captureViewDistance = 3;
            string caveMode = "none";
            string exposureMode = "none";
            bool includeHostiles = false;
            if (args.Length > 7)
            {
                if (bool.TryParse(args[7], out bool legacyIncludeTorch))
                {
                    includeTorch = legacyIncludeTorch;
                    captureViewDistance = Math.Clamp(ParseInt(args, 8, 3), 3, 5);
                    if (args.Length > 9)
                    {
                        string legacyToken = args[9].Trim().ToLowerInvariant();
                        if (bool.TryParse(legacyToken, out bool parsedHostiles))
                            includeHostiles = parsedHostiles;
                        else
                            caveMode = legacyToken;
                    }

                    exposureMode = args.Length > 10
                            ? args[10].Trim().ToLowerInvariant() : "none";
                    if (args.Length > 11 && bool.TryParse(args[11], out bool laterHostiles))
                        includeHostiles = laterHostiles;
                }
                else
                {
                    presetIndex = ParseInt(args, 7, Settings.GfxPreset);
                    captureViewDistance = Math.Clamp(ParseInt(args, 8, 3), 3, 5);
                    if (args.Length > 9 && !bool.TryParse(args[9], out includeTorch))
                    {
                        GD.PushError($"Invalid include-torch value '{args[9]}'. Expected true or false.");
                        GetTree().Quit(2);
                        return;
                    }

                    caveMode = args.Length > 10 ? args[10].Trim().ToLowerInvariant() : "none";
                    exposureMode = args.Length > 11
                            ? args[11].Trim().ToLowerInvariant() : "none";
                    if (args.Length > 12 && bool.TryParse(args[12], out bool parsedHostiles))
                        includeHostiles = parsedHostiles;
                }
            }

            if (presetIndex < 0 || presetIndex >= Gfx.Presets.Count)
            {
                GD.PushError($"Invalid graphics preset index {presetIndex}. Expected 0–{Gfx.Presets.Count - 1}.");
                GetTree().Quit(2);
                return;
            }

            bool includeCave = caveMode is "sealed" or "sealed-torch"
                    or "entrance" or "entrance-torch";
            bool connectEntrance = caveMode is "entrance" or "entrance-torch";
            bool placeTorch = caveMode is "sealed-torch" or "entrance-torch";
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

            if (includeHostiles && (includeCave || exposureMode != "none"))
            {
                GD.PushError("Hostile silhouette capture cannot be combined with cave or exposure modes.");
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
            Gfx? gfx = FindNode<Gfx>(game);
            WorldEnvironment? worldEnvironment = FindNode<WorldEnvironment>(game);
            if (dayNight == null || weather == null || gfx == null || worldEnvironment == null)
            {
                GD.PushError("Main scene did not initialize its existing day/night, weather, graphics and environment systems.");
                GetTree().Quit(5);
                return;
            }

            gfx.ApplyPreset(presetIndex, persist: false);
            double captureSettleSeconds = SettleSecondsForPreset(gfx.CurrentPreset);

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

            GD.Print($"ATMOSPHERE_CAPTURE_START seed={seed} version={version} center=({centerX},{centerZ}) time={world.TimeOfDay:F3} weather={weatherState} preset={Gfx.Presets[presetIndex].Name} torch={includeTorch} hostiles={includeHostiles} cave_mode={caveMode} exposure_mode={exposureMode} view_distance={captureViewDistance} settle_seconds={captureSettleSeconds:F1} expected_chunks={expectedChunkCount} output={output}");
            double started = Time.GetTicksMsec() / 1000.0;
            int frames = 0;
            while (frames < 1800)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
                double elapsed = Time.GetTicksMsec() / 1000.0 - started;
                if (elapsed >= captureSettleSeconds && world.ChunkCount >= expectedChunkCount)
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

            if (includeHostiles)
            {
                Mobs? mobs = FindNode<Mobs>(game);
                if (mobs == null)
                {
                    GD.PushError("Main scene did not initialize the existing mob director.");
                    GetTree().Quit(10);
                    return;
                }

                ConfigureHostileSilhouetteComposition(world, player, mobs, camera,
                        centerX, centerZ);
                for (int frame = 0; frame < 12; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
            GD.Print($"ATMOSPHERE_CAPTURE_RESULT success={(saveResult == Error.Ok)} chunks={world.ChunkCount} frames={frames} size={image.GetWidth()}x{image.GetHeight()} hostiles={includeHostiles} path={absoluteOutput}");
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

    private static void ConfigureHostileSilhouetteComposition(
            VoxelWorld world, Player player, Mobs mobs, Camera3D camera,
            int centerX, int centerZ)
    {
        // Use the production hostile models and material pipeline, but stop
        // the ecology director and actor physics so every capture has identical
        // subjects and framing without random spawn or movement variance.
        mobs.SetProcess(false);

        // A small disposable platform puts the subjects above generated
        // ridges, so the same camera framing is valid for every fixed seed.
        // Every voxel in this visual fixture still uses the production edit API.
        int platformY = world.Height - 12;
        for (int z = centerZ - 4; z <= centerZ + 4; z++)
        for (int x = centerX - 6; x <= centerX + 6; x++)
        {
            if (world.GetBlock(x, platformY, z) != BlockId.Stone
                    && !world.SetBlock(x, platformY, z, BlockId.Stone))
            {
                throw new InvalidOperationException(
                        "Could not build the temporary hostile inspection platform through VoxelWorld.SetBlock.");
            }
        }

        Mob[] subjects = [new Dreadwing(), new Shade(), new EmberWarden()];
        for (int i = 0; i < subjects.Length; i++)
        {
            int x = centerX + (i - 1) * 3;
            Vector3 position = new(x + 0.5f, platformY + 1.0f, centerZ + 0.5f);
            Mob subject = subjects[i];
            mobs.AddChild(subject);
            subject.Setup(world, mobs, position);
            subject.SetPhysicsProcess(false);
            subject.GlobalPosition = position;
            subject.GlobalRotation = Vector3.Zero;
        }

        player.GlobalPosition = new Vector3(centerX + 0.5f,
                platformY + 1.1f, centerZ + 5.0f);
        camera.Fov = 52.0f;
        camera.GlobalPosition = new Vector3(centerX + 0.5f,
                platformY + 3.6f, centerZ - 9.0f);
        camera.LookAt(new Vector3(centerX + 0.5f,
                platformY + 2.0f, centerZ + 0.5f), Vector3.Up);
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

    internal static double SettleSecondsForPreset(Gfx.Preset preset) =>
            preset.Fsr2 ? 16.0 : 8.0;

    internal static bool TryParseWeather(string value, out Weather.State state) =>
            Enum.TryParse(value, true, out state)
                    && Enum.IsDefined(typeof(Weather.State), state);
}
