using System.Threading.Tasks;
using Godot;

namespace Minecraft;

/// <summary>Integration coverage for voxel light, acoustic signals, chunk seams and physics visibility.</summary>
public static class MobPerceptionTests
{
    public static async Task Run(Node host)
    {
        string directory = $"user://tests/mob-perception-{Time.GetTicksUsec()}";
        DirAccess.MakeDirRecursiveAbsolute(directory);
        WorldStore.WriteManifest(directory, new WorldManifest(
                "Mob perception", 90210, 1, WorldStore.LegacyNoiseSeeds, 0, 0));
        GameSession.Instance.SelectWorld(directory);

        VoxelWorld world = new();
        Player player = new();
        Mobs manager = new();
        MobPerceptionProbe probe = new();
        Sheep parent = new();
        Sheep juvenile = new();
        Fx fx = new();
        Sound sound = new();
        Weather weather = new();
        Chicken chicken = new();
        Cow cow = new();
        Node3D target = new();
        StaticBody3D wall = new()
        {
            CollisionLayer = (uint)PhysicsLayer.Terrain,
            CollisionMask = 0,
        };

        try
        {
            host.AddChild(world);
            world.BuildPreview(new Vector3(-8, 55, 0), 1);

            // A sealed room straddles the -16 X chunk seam: sample in chunk -2,
            // place/remove the torch in chunk -1, and use SetBlock throughout.
            for (int z = -2; z <= 2; z++)
            for (int y = 52; y <= 58; y++)
            for (int x = -19; x <= -14; x++)
            {
                bool interior = x >= -18 && x <= -15
                        && z >= -1 && z <= 1 && y >= 53 && y <= 57;
                world.SetBlock(x, y, z, interior ? BlockId.Air : BlockId.Stone);
            }

            TestAssert.Equal(new Vector2I(-2, 0), Mob.ChunkForBlock(-17, 0),
                    "negative block coordinates use floor chunk division");
            TestAssert.Equal(new Vector2I(-1, 0), Mob.ChunkForBlock(-16, 0),
                    "negative chunk seam maps its right cell to the adjacent chunk");
            TestAssert.Equal(new Vector2I(1, 0), Mob.ChunkForBlock(16, 0),
                    "positive chunk seam mapping remains unchanged");

            Vector3 sample = new(-16.9f, 54.5f, 0.5f);
            TestAssert.Check(world.SetBlock(-16, 54, 0, BlockId.Torch),
                    "torch can be placed on the opposite side of the cave chunk seam");
            TestAssert.Check(Mob.TrySampleLight(world, sample, out byte litSky, out byte litBlock),
                    "loaded cave cell returns cached voxel light");
            TestAssert.Equal((byte)0, litSky, "sealed cave room excludes skylight");
            TestAssert.Check(litBlock >= 12,
                    $"torch illuminates the adjacent chunk cell (received {litBlock})");
            TestAssert.Check(!Mob.CanDetectAtLight(litSky, litBlock, 5),
                    "torch light suppresses dark-adapted cave detection");

            TestAssert.Check(world.SetBlock(-16, 54, 0, BlockId.Air),
                    "torch removal uses the existing voxel mutation path");
            TestAssert.Check(Mob.TrySampleLight(world, sample, out byte darkSky, out byte darkBlock),
                    "light cache can be sampled after emitter removal");
            TestAssert.Equal((byte)0, darkSky, "sealed cave remains skylight-free after torch removal");
            TestAssert.Equal((byte)0, darkBlock, "removing the only torch clears its block-light spill");
            TestAssert.Check(Mob.CanDetectAtLight(darkSky, darkBlock, 5),
                    "dark cave conditions restore the stalker's detection state");

            // The player is deliberately kept out of the tree: this test only
            // emits its existing signals and does not need its camera/viewmodel.
            manager.Setup(world, player, fx, sound, weather);
            Vector3 noisePosition = new(-16.5f, 54.0f, 0.5f);
            player.EmitSignal(Player.SignalName.MiningTick, noisePosition, (int)BlockId.Stone);
            TestAssert.Check(manager.TryGetRecentNoise(noisePosition + new Vector3(4, 0, 0),
                    1.0f, 0, out Vector3 heardMining, out long miningId),
                    "existing mining signal creates an audible creature stimulus");
            TestAssert.Equal(noisePosition, heardMining, "mining pulse retains its voxel source position");
            player.EmitSignal(Player.SignalName.BlockPlaced, noisePosition, (int)BlockId.Torch);
            TestAssert.Check(manager.TryGetRecentNoise(noisePosition,
                    1.0f, miningId, out Vector3 heardPlacement, out long placementId),
                    "existing block-placement signal creates a newer audible stimulus");
            TestAssert.Equal(noisePosition, heardPlacement, "placement pulse retains its world position");
            TestAssert.Check(placementId > miningId, "sound events receive increasing sequence identifiers");
            TestAssert.Check(!manager.TryGetRecentNoise(new Vector3(100, 54, 100),
                    1.0f, 0, out _, out _),
                    "out-of-range creatures do not hear local mining/placement events");
            TestAssert.Check(Mobs.NoiseHearingScore(1, 10, 1, 0.5f)
                    > Mobs.NoiseHearingScore(1, 10, 1, 4.5f),
                    "sound intensity falls with age");
            TestAssert.Equal(0.0f, Mobs.NoiseHearingScore(1, 10, 1, 5.1f),
                    "sound pulses expire after their bounded lifetime");

            host.AddChild(probe);
            probe.Setup(world, manager, new Vector3(-23, 61, 0.5f));
            probe.GlobalPosition = new Vector3(-23, 61, 0.5f);
            probe.SetPhysicsProcess(false);
            TestAssert.Check(probe.TryHear(out Vector3 investigationDirection),
                    "hostile perception accepts an existing player noise pulse");
            TestAssert.Check(investigationDirection.X > 0.9f,
                    "sound investigation steers toward the mining/placement source");
            TestAssert.Check(manager.NoiseInvestigations > 0,
                    "manager records actual creature sound investigations");

            host.AddChild(parent);
            host.AddChild(juvenile);
            parent.SetPhysicsProcess(false);
            juvenile.SetPhysicsProcess(false);
            parent.GlobalPosition = new Vector3(-12, 54, 0);
            juvenile.GlobalPosition = new Vector3(-20, 54, 0);
            juvenile.SetBaby(30.0f, parent, null);
            TestAssert.Check(juvenile.HasLivingGuardian,
                    "newborn records its living parent as a guardian");
            TestAssert.Check(juvenile.GuardianFollowDirection().X > 0.9f,
                    "separated juvenile follows its parent");
            juvenile.GlobalPosition = parent.GlobalPosition + new Vector3(1, 0, 0);
            TestAssert.Equal(Vector3.Zero, juvenile.GuardianFollowDirection(),
                    "juvenile stays with its parent inside the formation distance");

            host.AddChild(target);
            // Test above the generated terrain roof so only the fixture wall
            // participates in the visibility ray.
            target.GlobalPosition = new Vector3(-9, 61, 0.5f);
            wall.Position = new Vector3(-16, 62.15f, 0.5f);
            wall.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(1.0f, 3.0f, 3.0f) },
            });
            host.AddChild(wall);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            TestAssert.Check(!probe.CanSee(target),
                    "terrain-layer wall blocks creature line of sight");
            TestAssert.Check(!probe.CanSee(target),
                    "cached blocked visibility remains consistent during its short validity window");

            host.RemoveChild(wall);
            wall.Free();
            target.GlobalPosition = new Vector3(-8, 61, 0.5f);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            TestAssert.Check(probe.CanSee(target),
                    "line of sight refreshes after obstruction removal and target movement");

            TestAssert.Check(Wanderer.FlightProfileFor(chicken).SpeedMultiplier
                            > Wanderer.FlightProfileFor(cow).SpeedMultiplier,
                    "small birds use a stronger flight burst than cattle");
            TestAssert.Check(Mobs.HerdAlignmentDirection(new Vector3(1, 0, 0)).X > 0.2f,
                    "herd steering adds a bounded shared-heading alignment term");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(wall))
            {
                if (wall.IsInsideTree()) wall.GetParent()?.RemoveChild(wall);
                wall.Free();
            }
            if (GodotObject.IsInstanceValid(target))
            {
                if (target.IsInsideTree()) target.GetParent()?.RemoveChild(target);
                target.Free();
            }
            if (GodotObject.IsInstanceValid(probe))
            {
                if (probe.IsInsideTree()) host.RemoveChild(probe);
                probe.Free();
            }
            if (GodotObject.IsInstanceValid(parent))
            {
                if (parent.IsInsideTree()) host.RemoveChild(parent);
                parent.Free();
            }
            if (GodotObject.IsInstanceValid(juvenile))
            {
                if (juvenile.IsInsideTree()) host.RemoveChild(juvenile);
                juvenile.Free();
            }
            if (GodotObject.IsInstanceValid(manager)) manager.Free();
            if (GodotObject.IsInstanceValid(player)) player.Free();
            if (GodotObject.IsInstanceValid(fx)) fx.Free();
            if (GodotObject.IsInstanceValid(sound)) sound.Free();
            if (GodotObject.IsInstanceValid(weather)) weather.Free();
            if (GodotObject.IsInstanceValid(chicken)) chicken.Free();
            if (GodotObject.IsInstanceValid(cow)) cow.Free();
            host.RemoveChild(world);
            world.Free();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            DirAccess.RemoveAbsolute(directory + "/manifest.json");
            DirAccess.RemoveAbsolute(directory);
        }
    }
}

public partial class MobPerceptionProbe : Mob
{
    protected override bool UsesGravity => false;
    protected override bool UsesVoxelSteering => false;

    public bool CanSee(Node3D target) => HasLineOfSight(target);
    public bool TryHear(out Vector3 direction) => TryInvestigateSound(out direction);
}
