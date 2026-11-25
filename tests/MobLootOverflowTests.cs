using Godot;

namespace Minecraft;

/// <summary>Regression coverage for all-or-nothing mob reward ownership.</summary>
public static class MobLootOverflowTests
{
    public static void Run(Node host)
    {
        Mobs manager = new();
        Player player = new();
        player.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = Player.CollisionRadius, Height = Player.CollisionHeight },
            Position = Vector3.Up * (Player.CollisionHeight * 0.5f),
        });
        Camera3D camera = new() { Name = "Camera3D" };
        camera.AddChild(new RayCast3D { Name = "RayCast3D" });
        player.AddChild(camera);
        MobDeathProbe mob = new()
        {
            Health = 10.0f,
            Loot =
            [
                (ItemId.Feather, 2, 2, 1.0f),
                (ItemId.Leather, 1, 1, 1.0f),
            ],
        };

        host.AddChild(manager);
        host.AddChild(player);
        host.AddChild(mob);
        manager.PlayerRef = player;
        mob.Manager = manager;
        mob.Died += manager.OnMobDied;

        int deathEvents = 0;
        mob.Died += _ => deathEvents++;

        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            player.Inventory.Set(slot, ItemDb.New(ItemId.Dirt, 64));
        }

        mob.ForceDie();
        TestAssert.Equal(1.0f, mob.Health,
                "mob death is deferred when its complete loot cannot fit");
        TestAssert.Check(!mob.Dead,
                "full inventory does not remove the mob before loot is recoverable");
        TestAssert.Equal(0, deathEvents,
                "blocked mob death does not raise the death event");
        TestAssert.Equal(0, player.Inventory.CountOf(ItemId.Feather),
                "blocked mob death does not partially insert feather loot");
        TestAssert.Equal(0, player.Inventory.CountOf(ItemId.Leather),
                "blocked mob death does not partially insert leather loot");

        // One free slot cannot accept both distinct loot types; the mob and its
        // already-rolled loot plan remain unchanged on a repeated death attempt.
        player.Inventory.Set(0, ItemStack.Empty);
        mob.ForceDie();
        TestAssert.Equal(1.0f, mob.Health,
                "mob remains alive when only one of two required item slots is free");
        TestAssert.Equal(0, deathEvents,
                "repeated blocked death attempts do not duplicate death events");
        TestAssert.Equal(0, player.Inventory.CountOf(ItemId.Feather),
                "partial multi-item reward capacity inserts nothing");

        // Once both slots are available, the same pre-rolled loot plan is
        // delivered as one transaction and the death event is raised once.
        player.Inventory.Set(1, ItemStack.Empty);
        mob.ForceDie();
        TestAssert.Check(mob.Dead,
                "mob death proceeds once the complete reward set fits");
        TestAssert.Equal(1, deathEvents,
                "successful retry raises exactly one death event");
        TestAssert.Equal(2, player.Inventory.CountOf(ItemId.Feather),
                "successful retry delivers the complete feather reward");
        TestAssert.Equal(1, player.Inventory.CountOf(ItemId.Leather),
                "successful retry delivers the complete leather reward");

        mob.ForceDie();
        TestAssert.Equal(1, deathEvents,
                "repeated direct death after completion cannot duplicate loot");
        TestAssert.Equal(2, player.Inventory.CountOf(ItemId.Feather),
                "completed mob loot is not delivered twice");
        TestAssert.Equal(1, player.Inventory.CountOf(ItemId.Leather),
                "completed mixed-item loot is not delivered twice");

        player.QueueFree();
        manager.QueueFree();
    }
}
