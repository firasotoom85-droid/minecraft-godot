using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Minecraft;

/// <summary>
/// Renders one fixed, scenario-specific comparison using the real main scene.
/// Each fixture shares the same renderer preset and production lighting path.
/// Usage:
/// godot --path . res://tests/ForestCanopyLighting.tscn -- [seed] [output.png] [LOW|BALANCED|HIGH|ULTRA|MAX]
/// </summary>
public partial class LightingComparisonCapture : Node
{
    [Export]
    public string Scenario { get; set; } = "forest-canopy";

    private readonly record struct Fixture(
            Vector3 PlayerPosition,
            Vector3 LookTarget,
            Vector3I LightProbe,
            string LocationDescription);

    private readonly record struct LightSample(byte Sky, byte DirectSky, byte SkyBounce, byte Block);

    public override async void _Ready()
    {
        string worldDir = string.Empty;
        string previousWorldDir = GameSession.Instance.WorldDir;
        WorldManifest? previousManifest = GameSession.Instance.Manifest;
        bool previousAutoQuality = Settings.AutoQuality;
        int previousMaxFps = Engine.MaxFps;
        float previousPaleSurfaceContrast = ChunkMaterials.PaleSurfaceContrastStrength;
        Node3D? game = null;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int seed = ParseInt(args, 0, 1337);
            bool probeOnly = Array.Exists(args, argument => argument == "--probe-only");
            if (!LightingComparisonProfiles.TryGet(Scenario, out LightingComparisonProfiles.Profile profile))
                throw new ArgumentException($"Unknown lighting comparison scenario '{Scenario}'.");

            int presetIndex = ParsePreset(args.Length > 2 ? args[2] : "LOW");
            string output = args.Length > 1
                    ? args[1]
                    : $"user://tests/lighting-comparisons/{Scenario}-seed-{seed}.png";

            // Positional arg 3 is the pale-surface mode, but probe-only runs put
            // their --flag in the same slot; a flag must not be read as a mode.
            string contrastMode = args.Length > 3
                    && !args[3].StartsWith("--", StringComparison.Ordinal)
                    ? args[3].Trim().ToLowerInvariant() : "contrast";
            if (contrastMode is not ("baseline" or "contrast"))
                throw new ArgumentException(
                        $"Unknown pale-surface capture mode '{contrastMode}'; use baseline or contrast.");
            float captureContrast = contrastMode == "baseline"
                    ? 0.0f : MaterialResponse.PaleSurfaceDirectionalContrast;
            ChunkMaterials.SetPaleSurfaceContrast(captureContrast);

            DisplayServer.WindowSetSize(new Vector2I(
                    LightingComparisonProfiles.CaptureWidth,
                    LightingComparisonProfiles.CaptureHeight));

            // Probe-only runs read baked light without presenting or resizing
            // a window, so they stay usable in a headless analysis job.
            if (!probeOnly)
                DisplayServer.WindowSetSize(new Vector2I(
                        LightingComparisonProfiles.CaptureWidth,
                        LightingComparisonProfiles.CaptureHeight));
            Engine.MaxFps = 60;
            Settings.AutoQuality = false;

            worldDir = $"user://tests/lighting-comparisons/{Scenario}-{seed}-{Time.GetTicksUsec()}";
            WorldManifest manifest = new(
                    $"Lighting comparison: {profile.DisplayName}", seed,
                    WorldStore.CurrentGenVersion, WorldStore.DeriveNoiseSeeds(seed),
                    WorldStore.NowUtc(), WorldStore.NowUtc(), "creative", "normal");
            if (!WorldStore.WriteManifest(worldDir, manifest))
                throw new IOException($"Could not create disposable comparison world at {worldDir}.");

            GameSession.Instance.SelectWorld(worldDir);
            PackedScene gameScene = GD.Load<PackedScene>("res://scenes/main.tscn")
                    ?? throw new InvalidOperationException("Could not load the real main game scene.");
            game = gameScene.Instantiate<Node3D>();
            AddChild(game);

            VoxelWorld world = game.GetNode<VoxelWorld>("World");
            Player player = game.GetNode<Player>("Player");
            Hud hud = game.GetNode<Hud>("HUD");
            DayNight dayNight = FindNode<DayNight>(game)
                    ?? throw new InvalidOperationException("Main scene did not initialize DayNight.");
            Weather weather = FindNode<Weather>(game)
                    ?? throw new InvalidOperationException("Main scene did not initialize Weather.");
            Gfx gfx = FindNode<Gfx>(game)
                    ?? throw new InvalidOperationException("Main scene did not initialize Gfx.");

            player.SetPhysicsProcess(false);
            player.SetProcess(false);
            player.SetProcessInput(false);
            player.SetProcessUnhandledInput(false);
            player.SetProcessUnhandledKeyInput(false);
            player.Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            player.Camera.Current = false;
            hud.Visible = false;
            dayNight.Paused = true;
            dayNight.Cloudiness = 0.0;
            world.TimeOfDay = profile.TimeOfDay;
            weather.Current = Weather.State.Clear;
            weather.SetProcess(false);
            FindNode<Mobs>(game)?.SetProcess(false);
            FindNode<VillageSystem>(game)?.SetProcess(false);
            FindNode<DungeonSystem>(game)?.SetProcess(false);

            // LOW remains the default reference path for MX230-class GPUs.
            // A small fixed view radius bounds this visual fixture without
            // changing the quality-preset values used by the running game.
            gfx.ApplyPreset(presetIndex, false);
            world.SetViewDistance(LightingComparisonProfiles.ReferenceViewDistance);
            world.BuildCollision = false;

            Fixture fixture = BuildFixture(profile, world);
            player.GlobalPosition = fixture.PlayerPosition;
            world.BuildPreview(fixture.PlayerPosition, LightingComparisonProfiles.ChunkBuildRadius);

            Camera3D camera = new()
            {
                Name = "LightingComparisonCamera",
                Fov = profile.FieldOfView,
                Near = 0.1f,
                Far = 220.0f,
                Current = true,
            };
            game.AddChild(camera);
            camera.GlobalPosition = fixture.LookTarget + profile.CameraOffset;
            camera.LookAt(fixture.LookTarget, Vector3.Up);

            DirectionalLight3D sun = game.GetNode<DirectionalLight3D>("Sun");
            DirectionalLight3D moon = dayNight.Moon;
            int lightNodes = CountNodes<Light3D>(game);
            int directionalLights = CountNodes<DirectionalLight3D>(game);
            int omniLights = CountNodes<OmniLight3D>(game);
            if (lightNodes != 2 || directionalLights != 2 || omniLights != 0)
                throw new InvalidOperationException(
                        $"The comparison scene must contain only the existing sun and moon (lights={lightNodes}, " +
                        $"directional={directionalLights}, omni={omniLights}); emitter lighting must remain baked.");

            Gfx.Preset preset = Gfx.Presets[presetIndex];
            if (sun.DirectionalShadowMaxDistance != preset.ShadowDistance
                    || moon.DirectionalShadowMaxDistance != preset.ShadowDistance)
                throw new InvalidOperationException(
                        $"The {preset.Name} comparison must apply its {preset.ShadowDistance:F1} m shadow range to both sun and moon.");

            GD.Print($"LIGHTING_COMPARISON_START scenario={Scenario} seed={seed} gen={world.WorldGenVersion} " +
                    $"preset={preset.Name} time={profile.TimeOfDay:F3} weather=Clear " +
                    $"pale_mode={contrastMode} pale_contrast={captureContrast:F2} " +
                    $"view={world.ViewDistance} render_scale={preset.Scale:F2} " +
                    $"shadow_distance={preset.ShadowDistance:F1} shadow_atlas={preset.ShadowSize} " +
                    $"shadow_filter={preset.ShadowFilterQuality} sun_shadow_distance={sun.DirectionalShadowMaxDistance:F1} " +
                    $"moon_shadow_distance={moon.DirectionalShadowMaxDistance:F1} " +
                    $"sun_angular_distance={sun.LightAngularDistance:F2} moon_angular_distance={moon.LightAngularDistance:F2} " +
                    $"sun_blur={sun.ShadowBlur:F2} moon_blur={moon.ShadowBlur:F2} " +
                    $"light_nodes={lightNodes} directional_lights={directionalLights} omni_lights={omniLights} " +
                    $"location={fixture.LocationDescription}");

            for (int frame = 0; frame < LightingComparisonProfiles.WarmupFrames; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            List<LightingProbeComparison.Sample> probeGrid =
                    CaptureProbeGrid(world, fixture.LightProbe);
            LightSample probe = ReadLight(world, fixture.LightProbe);
            BlockId probeBlock = world.GetBlock(
                    fixture.LightProbe.X, fixture.LightProbe.Y, fixture.LightProbe.Z);
            int sources = world.NearbyLightSources(
                    fixture.LightProbe.X - VoxelLighting.BlockLightRadius,
                    fixture.LightProbe.X + VoxelLighting.BlockLightRadius,
                    fixture.LightProbe.Z - VoxelLighting.BlockLightRadius,
                    fixture.LightProbe.Z + VoxelLighting.BlockLightRadius).Count;
            IReadOnlyList<LightingProbeComparison.ReferenceSample> reference =
                    LightingProbeComparison.LoadReference(Scenario);
            LightingProbeComparison.Drift drift = LightingProbeComparison.Compare(
                    probeGrid, reference);
            string absoluteOutput = ProjectSettings.GlobalizePath(output);
            string? directory = Path.GetDirectoryName(absoluteOutput);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            Error saveResult = Error.Ok;
            // Probe-only runs never read back the viewport, so the frame grab is
            // created only when a PNG is actually produced.
            Image? image = probeOnly ? null : GetViewport().GetTexture().GetImage();
            if (image != null)
            {
                saveResult = image.SavePng(absoluteOutput);
                if (saveResult != Error.Ok)
                    throw new IOException($"Could not save screenshot: {saveResult}.");

                if (profile.ReferenceScreenshot is { } referencePng)
                {
                    Image referenceImage = Image.LoadFromFile(
                            ProjectSettings.GlobalizePath(referencePng));
                    LightingScreenshotAudit.Result audit =
                            LightingScreenshotAudit.Compare(image, referenceImage);
                    GD.Print($"LIGHTING_COMPARISON_AUDIT scenario={Scenario} " +
                            $"tiles={audit.ComparedTiles} regressed={audit.RegressedTiles} " +
                            $"worst_drop={audit.WorstTileDrop * 255.0f:F1}/255 passed={audit.Passed} " +
                            $"details=\"{audit.Details}\"");
                    if (!audit.Passed)
                        throw new InvalidOperationException(
                                $"Screenshot lighting audit failed for '{Scenario}': {audit.Details}. " +
                                $"Captured frame retained at {absoluteOutput}.");
                }
                else
                {
                    GD.Print($"LIGHTING_COMPARISON_AUDIT scenario={Scenario} passed=skipped " +
                            $"details=\"no committed reference; frame saved for manual review\"");
                }
            }

            string metricsOutput = Path.ChangeExtension(absoluteOutput, ".lighting.json");
            var metrics = new
            {
                schema_version = 1,
                scenario = Scenario,
                seed,
                generator_version = world.WorldGenVersion,
                preset = preset.Name,
                reference_version = LightingProbeComparison.ReferenceVersion,
                reference_file = LightingProbeComparison.ReferencePath,
                samples = probeGrid,
                reference_samples = reference,
                drift,
            };
            File.WriteAllText(metricsOutput, JsonSerializer.Serialize(metrics,
                    new JsonSerializerOptions { WriteIndented = true }));

            GD.Print($"LIGHTING_COMPARISON_SAMPLE scenario={Scenario} probe=({fixture.LightProbe.X}," +
                    $"{fixture.LightProbe.Y},{fixture.LightProbe.Z}) block={probeBlock} " +
                    $"sky={probe.Sky} direct_sky={probe.DirectSky} cave_bounce={probe.SkyBounce} " +
                    $"block_light={probe.Block} nearby_emitters={sources} omni_lights={omniLights}");
            GD.Print($"LIGHTING_COMPARISON_PROBE_GRID scenario={Scenario} " +
                    $"captured={drift.CapturedSamples} reference={drift.ReferenceSamples} " +
                    $"compared={drift.ComparedSamples} unreferenced={drift.UnreferencedSamples} " +
                    $"missing={drift.MissingSamples} coverage={drift.ReferenceCoverage:P1} " +
                    $"json={metricsOutput}");
            GD.Print($"LIGHTING_COMPARISON_DRIFT scenario={Scenario} " +
                    $"mean_channel={drift.MeanAbsoluteChannelDrift:F3} " +
                    $"max_channel={drift.MaxChannelDrift:F0} " +
                    $"sky_mean={drift.MeanAbsoluteSkyDrift:F3} sky_max={drift.MaxSkyDrift:F0} " +
                    $"block_mean={drift.MeanAbsoluteBlockDrift:F3} block_max={drift.MaxBlockDrift:F0} " +
                    $"rgb_mean=({drift.MeanAbsoluteRedDrift:F5},{drift.MeanAbsoluteGreenDrift:F5}," +
                    $"{drift.MeanAbsoluteBlueDrift:F5}) " +
                    $"rgb_max=({drift.MaxRedDrift:F5},{drift.MaxGreenDrift:F5},{drift.MaxBlueDrift:F5}) " +
                    $"intensity_mean={drift.MeanAbsoluteIntensityDrift:F5} " +
                    $"intensity_max={drift.MaxIntensityDrift:F5} " +
                    $"outside_tolerance={drift.OutsideToleranceSamples} tolerance={drift.Tolerance:F5}");
            if (!probeOnly)
            {
                GD.Print($"LIGHTING_COMPARISON_RENDER scenario={Scenario} " +
                        $"size={image!.GetWidth()}x{image.GetHeight()} " +
                        $"pale_mode={contrastMode} pale_contrast={captureContrast:F2} " +
                        $"ssao={preset.Ssao} ssr={preset.Ssr} glow={preset.Glow} volumetric_fog={preset.VolFog} " +
                        $"shadows={preset.ShadowSize} path={absoluteOutput} success={saveResult == Error.Ok}");
            }
            else
            {
                GD.Print($"LIGHTING_COMPARISON_RENDER scenario={Scenario} probe_only=true " +
                        $"pale_mode={contrastMode} pale_contrast={captureContrast:F2} " +
                        $"ssao={preset.Ssao} ssr={preset.Ssr} glow={preset.Glow} volumetric_fog={preset.VolFog} " +
                        $"shadows={preset.ShadowSize} metrics={metricsOutput}");
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Lighting comparison '{Scenario}' failed: {exception}");
            GetTree().Quit(2);
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

            GameSession.Instance.WorldDir = previousWorldDir;
            GameSession.Instance.Manifest = previousManifest;
            Settings.AutoQuality = previousAutoQuality;
            Engine.MaxFps = previousMaxFps;
            ChunkMaterials.SetPaleSurfaceContrast(previousPaleSurfaceContrast);
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
                    GD.PushWarning($"Could not remove temporary comparison world {absoluteWorldDir}: {exception.Message}");
                }
            }
        }

        GetTree().Quit();
    }

    private static Fixture BuildFixture(LightingComparisonProfiles.Profile profile, VoxelWorld world)
    {
        return profile.Id switch
        {
            "forest-canopy" => BuildForestFixture(profile, world),
            "torch-cave" => BuildTorchCaveFixture(profile, world),
            "river-valley" => BuildRiverFixture(profile, world),
            "village-street" => BuildVillageFixture(profile, world),
            "dungeon-arena" => BuildDungeonFixture(profile, world),
            "pale-surfaces" => BuildPaleSurfaceFixture(world),
            _ => throw new ArgumentOutOfRangeException(nameof(profile.Id), profile.Id,
                    "No fixture builder exists for this comparison profile."),
        };
    }

    private static Fixture BuildForestFixture(
            LightingComparisonProfiles.Profile profile, VoxelWorld world)
    {
        int bestCount = 0;
        int bestX = 0;
        int bestZ = 0;
        VoxelWorld.TreeInfo bestTree = default;
        for (int z = -768; z <= 768; z += 128)
        for (int x = -768; x <= 768; x += 128)
        {
            (int height, Biome biome) = world.SurfaceSampleAt(x, z);
            if (biome is not (Biome.DenseForest or Biome.Jungle or Biome.Taiga
                    or Biome.Birch or Biome.Swamp))
                continue;

            int count = 0;
            int nearestDistance = int.MaxValue;
            VoxelWorld.TreeInfo nearestTree = default;
            for (int dz = -24; dz <= 24; dz += 3)
            for (int dx = -24; dx <= 24; dx += 3)
            {
                if (world.GetTreeAt(x + dx, z + dz) is not { } tree)
                    continue;
                count++;
                int distance = dx * dx + dz * dz;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestTree = tree;
                }
            }

            int score = count * 10 + (biome == Biome.DenseForest ? 12 : 0);
            if (score > bestCount)
            {
                bestCount = score;
                bestX = x;
                bestZ = z;
                bestTree = nearestTree;
            }
        }

        if (bestCount == 0)
            throw new InvalidOperationException("The fixed seed did not yield a tree-bearing forest candidate.");

        (int playerSurfaceHeight, _) = world.SurfaceSampleAt(bestX, bestZ);
        Vector3 player = new(bestX + 0.5f, playerSurfaceHeight + 1.0f, bestZ + 0.5f);
        Vector3 target = new(bestTree.X + 0.5f,
                bestTree.Base + bestTree.Trunk + 1.0f, bestTree.Z + 0.5f);
        Vector3I probe = new(bestTree.X, bestTree.Base + bestTree.Trunk - 1, bestTree.Z);
        return new Fixture(player, target, probe,
                $"canopy=({bestX},{bestZ}) biome={world.SurfaceSampleAt(bestX, bestZ).Biome} sampled_trees={bestCount}");
    }

    private static Fixture BuildTorchCaveFixture(
            LightingComparisonProfiles.Profile profile, VoxelWorld world)
    {
        Vector3 spawn = world.FindSpawn();
        int x = Mathf.FloorToInt(spawn.X);
        int z = Mathf.FloorToInt(spawn.Z);
        int surface = world.SurfaceHeight(x, z);
        int floorY = Math.Max(8, surface - 12);
        int roofY = floorY + 5;

        // Build a compact stone chamber in the disposable world. Every changed
        // voxel passes through SetBlock so baked sky/block maps are invalidated
        // and rebuilt by the exact same mutation path as player edits.
        for (int dz = -4; dz <= 4; dz++)
        for (int dx = -4; dx <= 4; dx++)
        {
            WriteBlock(world, x + dx, floorY, z + dz, BlockId.Stone);
            WriteBlock(world, x + dx, roofY, z + dz, BlockId.Stone);
            for (int dy = 1; dy < 5; dy++)
            {
                bool wall = Math.Abs(dx) == 4 || Math.Abs(dz) == 4;
                WriteBlock(world, x + dx, floorY + dy, z + dz,
                        wall ? BlockId.Stone : BlockId.Air);
            }
        }

        // A one-voxel shaft leaves a controlled skylight gradient at the
        // chamber edge while the far wall remains lit by a single voxel torch.
        int shaftX = x + 2;
        for (int y = roofY; y <= surface + 1 && y < world.Height; y++)
            WriteBlock(world, shaftX, y, z, BlockId.Air);

        // Leave a 3×3 doorway in the camera-facing wall so the fixed camera
        // sees the interior light gradient rather than a close exterior wall.
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = 1; dy <= 3; dy++)
            WriteBlock(world, x + dx, floorY + dy, z + 4, BlockId.Air);

        int torchX = x;
        int torchY = floorY + 2;
        int torchZ = z - 3;
        WriteBlock(world, torchX, torchY, torchZ, BlockId.Torch);

        Vector3 player = new(x + 0.5f, floorY + 1.0f, z + 0.5f);
        Vector3 target = new(x + 0.5f, floorY + 2.8f, z + 0.5f);
        Vector3I probe = new(torchX, torchY, torchZ);
        return new Fixture(player, target, probe,
                $"chamber=({x},{floorY},{z}) shaft=({shaftX},{surface + 1},{z})");
    }

    private static Fixture BuildRiverFixture(
            LightingComparisonProfiles.Profile profile, VoxelWorld world)
    {
        WorldGenV8 generator = new(world.WorldSeed, world.PresetName);
        float bestScore = float.MinValue;
        int bestX = 0;
        int bestZ = 0;
        int bestHeight = 0;
        float bestRiver = 0.0f;

        for (int z = -1536; z <= 1536; z += 8)
        for (int x = -1536; x <= 1536; x += 8)
        {
            WorldGenV7.SurfaceSample surface = generator.SampleSurface(x, z, world.SeaLevel);
            if (surface.Height >= world.SeaLevel)
                continue;

            float river = generator.ClimateAt(x, z, world.SeaLevel + 12).RiverStrength;
            if (river < 0.78f)
                continue;

            int bank = Math.Max(
                    Math.Max(generator.SurfaceHeight(x - 28, z, world.SeaLevel),
                            generator.SurfaceHeight(x + 28, z, world.SeaLevel)),
                    Math.Max(generator.SurfaceHeight(x, z - 28, world.SeaLevel),
                            generator.SurfaceHeight(x, z + 28, world.SeaLevel)));
            float score = river * 40.0f + Math.Min(24, bank - surface.Height) * 1.5f;
            if (score > bestScore)
            {
                bestScore = score;
                bestX = x;
                bestZ = z;
                bestHeight = surface.Height;
                bestRiver = river;
            }
        }

        if (bestScore == float.MinValue)
            throw new InvalidOperationException("The fixed seed did not yield a river-channel candidate.");

        Vector3 player = new(bestX + 0.5f, bestHeight + 2.0f, bestZ + 0.5f);
        Vector3 target = player + new Vector3(0.0f, 2.0f, 0.0f);
        Vector3I probe = new(bestX, bestHeight + 1, bestZ);
        return new Fixture(player, target, probe,
                $"channel=({bestX},{bestZ}) river_strength={bestRiver:F2} bank_height_delta={bestScore:F1}");
    }

    private static Fixture BuildVillageFixture(
            LightingComparisonProfiles.Profile profile, VoxelWorld world)
    {
        VillageInfo? best = null;
        int bestDistance = int.MaxValue;
        for (int radius = 0; radius <= 18; radius++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                    continue;
                if (world.GetVillageSite(dx, dz) is not { } village)
                    continue;

                int distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    best = village;
                    bestDistance = distance;
                }
            }

            if (best != null)
                break;
        }

        if (best is not { } site)
            throw new InvalidOperationException("No deterministic V8+ village was found within the fixture search radius.");

        Vector3 player = new(site.X + 0.5f, site.Y + 1.0f, site.Z + 0.5f);
        Vector3 target = player + profile.TargetOffset;
        Vector3I probe = FindNearestBlock(world, new Vector3I(site.X, site.Y + 4, site.Z),
                BlockId.Torch, 24, 8)
                ?? throw new InvalidOperationException("The selected village has no generated torch for its light probe.");
        return new Fixture(player, target, probe,
                $"theme={site.Theme} centre=({site.X},{site.Y},{site.Z}) torch=({probe.X},{probe.Y},{probe.Z})");
    }

    private static Fixture BuildPaleSurfaceFixture(VoxelWorld world)
    {
        Vector3 spawn = world.FindSpawn();
        int centerX = Mathf.RoundToInt(spawn.X);
        int centerZ = Mathf.RoundToInt(spawn.Z);
        int floorY = Math.Max(world.SeaLevel + 5, world.SurfaceHeight(centerX, centerZ) + 3);

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -6; dx <= 6; dx++)
            floorY = Math.Max(floorY,
                    world.SurfaceHeight(centerX + dx, centerZ + dz) + 2);

        if (floorY >= world.Height - 5)
            throw new InvalidOperationException("Pale-material fixture has no safe build height.");

        // A dark cobblestone base separates the sample swatches. All fixture
        // mutations use the production SetBlock ownership path.
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -6; dx <= 6; dx++)
        {
            int x = centerX + dx;
            int z = centerZ + dz;
            WriteBlock(world, x, floorY - 1, z, BlockId.Cobblestone);
            for (int y = floorY; y <= floorY + 2; y++)
                WriteBlock(world, x, y, z, BlockId.Air);

            BlockId sample = dx <= -4 ? BlockId.Snow
                    : dx >= 4 ? BlockId.Stone
                    : dx is >= -1 and <= 1 ? BlockId.Sand
                    : BlockId.Air;
            if (sample != BlockId.Air)
                WriteBlock(world, x, floorY, z, sample);
        }

        Vector3 player = new(centerX + 0.5f, floorY + 1.0f, centerZ + 0.5f);
        Vector3 target = new(centerX + 0.5f, floorY + 0.35f, centerZ + 0.5f);
        Vector3I probe = new(centerX, floorY, centerZ);
        return new Fixture(player, target, probe,
                $"snow=({centerX - 5},{floorY},{centerZ}) sand=({centerX},{floorY},{centerZ}) pale_stone=({centerX + 5},{floorY},{centerZ})");
    }

    private static Fixture BuildDungeonFixture(
            LightingComparisonProfiles.Profile profile, VoxelWorld world)
    {
        VoxelWorld.StructureInfo? found = world.FindNearestStructure(3, 0, 0, 28);
        if (found is not { } site)
            throw new InvalidOperationException("No deterministic Ember Deep arena was found within 28 structure cells.");

        int arenaY = Structures.DungeonArenaY(site);
        if (arenaY < 8)
            throw new InvalidOperationException("The selected Ember Deep site is too low for its arena template.");

        Vector3 player = new(site.X + 0.5f, arenaY + 1.0f, site.Z + 0.5f);
        Vector3 target = player + profile.TargetOffset;
        Vector3I probe = new(site.X - 6, arenaY + 2, site.Z - 2);
        return new Fixture(player, target, probe,
                $"EmberDeep site=({site.X},{site.Y},{site.Z}) arena_y={arenaY}");
    }

    private static Vector3I? FindNearestBlock(VoxelWorld world, Vector3I centre,
            BlockId block, int horizontalRadius, int verticalRadius)
    {
        Vector3I? nearest = null;
        int nearestDistance = int.MaxValue;
        for (int y = Math.Max(1, centre.Y - verticalRadius);
                y <= Math.Min(world.Height - 1, centre.Y + verticalRadius); y++)
        for (int z = centre.Z - horizontalRadius; z <= centre.Z + horizontalRadius; z++)
        for (int x = centre.X - horizontalRadius; x <= centre.X + horizontalRadius; x++)
        {
            if (world.GetBlock(x, y, z) != block)
                continue;
            int dx = x - centre.X;
            int dy = y - centre.Y;
            int dz = z - centre.Z;
            int distance = dx * dx + dy * dy + dz * dz;
            if (distance < nearestDistance)
            {
                nearest = new Vector3I(x, y, z);
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private static void WriteBlock(VoxelWorld world, int x, int y, int z, BlockId block)
    {
        if (y <= 0 || y >= world.Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, "Lighting fixture edit is outside the world.");
        if (world.GetBlock(x, y, z) == block)
            return;
        if (!world.SetBlock(x, y, z, block))
            throw new InvalidOperationException($"SetBlock rejected fixture edit ({x},{y},{z}) -> {block}.");
    }

    private static List<LightingProbeComparison.Sample> CaptureProbeGrid(
            VoxelWorld world, Vector3I centre)
    {
        List<LightingProbeComparison.Sample> samples =
                new(LightingProbeComparison.GridOffsets.Count);
        foreach (Vector3I offset in LightingProbeComparison.GridOffsets)
        {
            Vector3I position = centre + offset;
            if (position.Y < 0 || position.Y >= world.Height)
                throw new InvalidOperationException(
                        $"Probe grid sample {LightingProbeComparison.IdForOffset(offset)} " +
                        $"is outside world height at Y={position.Y}.");

            LightSample light = ReadLight(world, position);
            BlockId block = world.GetBlock(position.X, position.Y, position.Z);
            samples.Add(new LightingProbeComparison.Sample(
                    LightingProbeComparison.IdForOffset(offset),
                    position.X, position.Y, position.Z,
                    light.Sky, light.Block, block.ToString()));
        }

        return samples;
    }

    private static LightSample ReadLight(VoxelWorld world, Vector3I position)
    {
        Vector2I chunk = new(
                Mathf.FloorToInt(position.X / (float)VoxelWorld.ChunkX),
                Mathf.FloorToInt(position.Z / (float)VoxelWorld.ChunkZ));
        int localX = position.X - chunk.X * VoxelWorld.ChunkX;
        int localZ = position.Z - chunk.Y * VoxelWorld.ChunkZ;
        VoxelLightData light = world.LightData(chunk);
        return new LightSample(
                light.SkyAt(localX, position.Y, localZ, world.Height),
                light.DirectSkyAt(localX, position.Y, localZ, world.Height),
                light.BouncedSkyAt(localX, position.Y, localZ, world.Height),
                light.BlockAt(localX, position.Y, localZ, world.Height));
    }

    private static T? FindNode<T>(Node root) where T : Node
    {
        if (root is T match)
            return match;
        foreach (Node child in root.GetChildren())
        {
            T? found = FindNode<T>(child);
            if (found != null)
                return found;
        }
        return null;
    }

    private static int CountNodes<T>(Node root) where T : Node
    {
        int count = root is T ? 1 : 0;
        foreach (Node child in root.GetChildren())
            count += CountNodes<T>(child);
        return count;
    }

    private static int ParseInt(string[] args, int index, int fallback) =>
            index < args.Length && int.TryParse(args[index], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static int ParsePreset(string name)
    {
        for (int index = 0; index < Gfx.Presets.Count; index++)
            if (string.Equals(Gfx.Presets[index].Name, name, StringComparison.OrdinalIgnoreCase))
                return index;
        throw new ArgumentException($"Unknown graphics preset '{name}'.");
    }
}
