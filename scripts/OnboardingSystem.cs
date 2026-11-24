using Godot;

namespace Minecraft;

public enum OnboardingStep
{
    Move,
    GatherWood,
    OpenInventory,
    CraftTable,
    CraftPickaxe,
    MineStone,
    PlaceTorch,
    GatherFood,
    EatFood,
    BuildShelter,
    UseFurnace,
    DefeatHostile,
    ExploreCave,
    VisitVillage,
    SurviveNight,
}

/// <summary>
/// Action-based first-hour progress. Facts are raised by successful gameplay
/// events, never inferred from starter inventory contents.
/// </summary>
public sealed class OnboardingProgress
{
    public const int ShelterBlockGoal = 8;

    public bool Moved { get; set; }
    public bool WoodBroken { get; set; }
    public bool InventoryOpened { get; set; }
    public bool CraftingTableCrafted { get; set; }
    public bool WoodenPickCrafted { get; set; }
    public bool StoneBroken { get; set; }
    public bool TorchPlaced { get; set; }
    public bool FoodGathered { get; set; }
    public bool FoodEaten { get; set; }
    public int SolidBlocksPlaced { get; set; }
    public bool FurnaceUsed { get; set; }
    public bool HostileDefeated { get; set; }
    public bool CaveExplored { get; set; }
    public bool VillageVisited { get; set; }
    public bool Slept { get; set; }
    public bool SawNight { get; set; }
    public bool SurvivedNight { get; set; }

    public static OnboardingProgress FromFlags(
            System.Collections.Generic.IReadOnlyDictionary<string, bool> flags)
    {
        bool Has(string key) => flags.TryGetValue(key, out bool value) && value;
        bool Fact(string id) => Has("onboard/fact/" + id);

        int shelterBlocks = 0;
        for (int i = 0; i < ShelterBlockGoal; i++)
        {
            if (Fact("shelter_" + i))
                shelterBlocks++;
        }

        return new OnboardingProgress
        {
            Moved = Fact("move"),
            WoodBroken = Fact("wood") || Has("onboard/wood"),
            InventoryOpened = Fact("inventory"),
            CraftingTableCrafted = Fact("table") || Has("onboard/table"),
            WoodenPickCrafted = Fact("pick") || Has("onboard/pick"),
            StoneBroken = Fact("stone") || Has("onboard/stone"),
            TorchPlaced = Fact("torch") || Has("onboard/torch"),
            FoodGathered = Fact("food"),
            FoodEaten = Fact("eat"),
            SolidBlocksPlaced = shelterBlocks,
            FurnaceUsed = Fact("furnace"),
            HostileDefeated = Fact("hostile"),
            CaveExplored = Fact("cave"),
            VillageVisited = Fact("village"),
            Slept = Fact("slept"),
            SawNight = Fact("saw_night"),
            SurvivedNight = Fact("survived_night") || Has("onboard/night"),
        };
    }

    public void RecordBlockBroken(BlockId id)
    {
        if (id is BlockId.Log or BlockId.BirchLog)
            WoodBroken = true;
        else if (id == BlockId.Stone)
            StoneBroken = true;
    }

    public void RecordBlockPlaced(BlockId id)
    {
        if (id == BlockId.Torch)
            TorchPlaced = true;

        // Tilling farmland is a placement event, but it is not shelter construction.
        if (id is not (BlockId.Torch or BlockId.Farmland) && BlockDb.IsSolid(id))
            SolidBlocksPlaced = System.Math.Min(ShelterBlockGoal, SolidBlocksPlaced + 1);
    }

    public void RecordItemGathered(ItemId id)
    {
        if (ItemDb.FoodOf(id) != null)
            FoodGathered = true;
    }

    public bool MarkInventoryOpened()
    {
        if (InventoryOpened)
            return false;

        InventoryOpened = true;
        return true;
    }

    public void RecordItemCrafted(ItemId id)
    {
        if (id == ItemId.CraftingTable)
            CraftingTableCrafted = true;
        else if (id == ItemId.WoodPick)
            WoodenPickCrafted = true;
    }

    public void RecordHostileDefeated(bool wasPassive)
    {
        if (!wasPassive)
            HostileDefeated = true;
    }

    public bool IsComplete(OnboardingStep step) => step switch
    {
        OnboardingStep.Move => Moved,
        OnboardingStep.GatherWood => WoodBroken,
        OnboardingStep.OpenInventory => InventoryOpened,
        OnboardingStep.CraftTable => CraftingTableCrafted,
        OnboardingStep.CraftPickaxe => WoodenPickCrafted,
        OnboardingStep.MineStone => StoneBroken,
        OnboardingStep.PlaceTorch => TorchPlaced,
        OnboardingStep.GatherFood => FoodGathered,
        OnboardingStep.EatFood => FoodEaten,
        OnboardingStep.BuildShelter => SolidBlocksPlaced >= ShelterBlockGoal,
        OnboardingStep.UseFurnace => FurnaceUsed,
        OnboardingStep.DefeatHostile => HostileDefeated,
        OnboardingStep.ExploreCave => CaveExplored,
        OnboardingStep.VisitVillage => VillageVisited,
        OnboardingStep.SurviveNight => Slept || SurvivedNight,
        _ => false,
    };
}

/// <summary>
/// Contextual, per-world first-hour guide. The six original "First Night"
/// flag names are preserved so partially completed worlds keep their progress.
/// </summary>
public partial class OnboardingSystem : Node
{
    private static readonly OnboardingStep[] Steps =
    [
        OnboardingStep.Move,
        OnboardingStep.GatherWood,
        OnboardingStep.OpenInventory,
        OnboardingStep.CraftTable,
        OnboardingStep.CraftPickaxe,
        OnboardingStep.MineStone,
        OnboardingStep.PlaceTorch,
        OnboardingStep.GatherFood,
        OnboardingStep.EatFood,
        OnboardingStep.BuildShelter,
        OnboardingStep.UseFurnace,
        OnboardingStep.DefeatHostile,
        OnboardingStep.ExploreCave,
        OnboardingStep.VisitVillage,
        OnboardingStep.SurviveNight,
    ];

    // Reuse the previous First Night step IDs wherever those steps still exist.
    private static readonly string[] StepIds =
    [
        "move", "wood", "inventory", "table", "pick", "stone", "torch",
        "food", "eat", "shelter", "furnace", "hostile", "cave", "village", "night",
    ];

    private static readonly string[] Hints =
    [
        "Move around — WASD / left stick; look with mouse / right stick",
        "Gather wood — mine a tree",
        "Open your inventory — E / controller inventory button",
        "Craft a crafting table",
        "Craft a wooden pickaxe",
        "Mine stone with your pickaxe",
        "Place a torch to light the dark",
        "Gather food — animals, fishing and crops provide supplies",
        "Eat when hungry — use the selected food item",
        "Build a shelter — place 8 solid blocks",
        "Open a furnace to inspect its cooking and smelting slots",
        "Defeat a hostile creature — watch for its attack wind-up",
        "Explore a cave below the surface",
        "Visit a village and speak with a resident",
        "Survive the first night — sleep or reach dawn",
    ];

    public static int StepCount => Steps.Length;

    private VoxelWorld? _world;
    private Player? _player;
    private Hud? _hud;
    private OnboardingProgress _progress = new();
    private int _step;
    private bool _done;
    private double _timer;
    private Vector3 _startPosition;

    public void Setup(VoxelWorld world, Player player, Hud hud, Mobs? mobs = null, InventoryUi? inventory = null)
    {
        _world = world;
        _player = player;
        _hud = hud;
        if (inventory != null)
            inventory.Opened += OnInventoryOpened;
        _startPosition = player.GlobalPosition;
        RestoreFacts();

        player.BlockPlaced += (_, blockId) =>
        {
            _progress.RecordBlockPlaced((BlockId)blockId);
            PersistFacts();
        };
        player.BlockBroken += (_, blockId) =>
        {
            _progress.RecordBlockBroken((BlockId)blockId);
            PersistFacts();
        };
        player.ItemGathered += (id, _) =>
        {
            _progress.RecordItemGathered(id);
            PersistFacts();
        };
        player.ItemCrafted += (id, _) =>
        {
            _progress.RecordItemCrafted(id);
            PersistFacts();
        };
        player.Ate += () =>
        {
            _progress.FoodEaten = true;
            PersistFacts();
        };
        player.FurnaceUsed += _ =>
        {
            _progress.FurnaceUsed = true;
            PersistFacts();
        };
        player.Slept += () =>
        {
            _progress.Slept = true;
            PersistFacts();
        };
        player.VillagerInteracted += _ =>
        {
            _progress.VillageVisited = true;
            PersistFacts();
        };
        if (mobs != null)
        {
            mobs.MobDefeated += mob =>
            {
                _progress.RecordHostileDefeated(mob.IsPassive);
                PersistFacts();
            };
        }

        _done = HasFlag("onboard/done");
        while (_step < Steps.Length && HasFlag("onboard/" + StepIds[_step]))
            _step++;
    }

    private void OnInventoryOpened()
    {
        if (_progress.MarkInventoryOpened())
            PersistFacts();
    }

    public override void _Process(double delta)
    {
        if (_world == null || _player == null)
            return;

        if (_done || !Settings.ShowHints || _player.Mode == GameMode.Creative || _player.Stats.Dead)
        {
            _hud?.HideHint();
            return;
        }

        if (!_progress.Moved)
        {
            Vector2 displacement = new(
                    _player.GlobalPosition.X - _startPosition.X,
                    _player.GlobalPosition.Z - _startPosition.Z);
            if (displacement.LengthSquared() >= 4.0f)
            {
                _progress.Moved = true;
                PersistFacts();
            }
        }

        if (DayNight.IsNight(_world.TimeOfDay))
        {
            if (!_progress.SawNight)
            {
                _progress.SawNight = true;
                PersistFacts();
            }
        }
        else if (_progress.SawNight && !_progress.SurvivedNight)
        {
            _progress.SurvivedNight = true;
            PersistFacts();
        }

        _timer -= delta;
        if (_timer > 0.0)
            return;

        _timer = 0.3;
        if (_step < Steps.Length && Steps[_step] == OnboardingStep.ExploreCave
                && !_progress.CaveExplored && IsInsideNaturalCave())
        {
            _progress.CaveExplored = true;
            PersistFacts();
        }

        while (_step < Steps.Length && _progress.IsComplete(Steps[_step]))
        {
            _world.Flags["onboard/" + StepIds[_step]] = true;
            _world.MarkDirty();
            _step++;
        }

        if (_step >= Steps.Length)
        {
            _world.Flags["onboard/done"] = true;
            _world.MarkDirty();
            _done = true;
            _hud?.HideHint();
            _hud?.ShowToast("First-hour guide complete — the world is yours");
            return;
        }

        _hud?.SetHint(Hints[_step], _step + 1, Steps.Length);
    }

    private bool IsInsideNaturalCave()
    {
        if (_world == null || _player == null)
            return false;

        int x = Mathf.FloorToInt(_player.GlobalPosition.X);
        int y = Mathf.FloorToInt(_player.GlobalPosition.Y);
        int z = Mathf.FloorToInt(_player.GlobalPosition.Z);
        int surface = _world.SurfaceHeight(x, z);
        return surface - y >= 8
                && _world.IsCave(x, y, z, surface)
                && _world.GetBlock(x, y, z) == BlockId.Air
                && _world.GetBlock(x, y + 1, z) == BlockId.Air;
    }

    private void RestoreFacts()
    {
        if (_world != null)
            _progress = OnboardingProgress.FromFlags(_world.Flags);
    }

    private void PersistFacts()
    {
        if (_world == null)
            return;

        bool changed = false;
        changed |= PersistFact("move", _progress.Moved);
        changed |= PersistFact("wood", _progress.WoodBroken);
        changed |= PersistFact("inventory", _progress.InventoryOpened);
        changed |= PersistFact("table", _progress.CraftingTableCrafted);
        changed |= PersistFact("pick", _progress.WoodenPickCrafted);
        changed |= PersistFact("stone", _progress.StoneBroken);
        changed |= PersistFact("torch", _progress.TorchPlaced);
        changed |= PersistFact("food", _progress.FoodGathered);
        changed |= PersistFact("eat", _progress.FoodEaten);
        changed |= PersistFact("furnace", _progress.FurnaceUsed);
        changed |= PersistFact("hostile", _progress.HostileDefeated);
        changed |= PersistFact("cave", _progress.CaveExplored);
        changed |= PersistFact("village", _progress.VillageVisited);
        changed |= PersistFact("slept", _progress.Slept);
        changed |= PersistFact("saw_night", _progress.SawNight);
        changed |= PersistFact("survived_night", _progress.SurvivedNight);
        for (int i = 0; i < _progress.SolidBlocksPlaced; i++)
            changed |= PersistFact("shelter_" + i, true);

        if (changed)
            _world.MarkDirty();
    }

    private bool PersistFact(string id, bool value)
    {
        if (_world == null || !value)
            return false;

        string key = "onboard/fact/" + id;
        if (HasFlag(key))
            return false;

        _world.Flags[key] = true;
        return true;
    }

    private bool HasFlag(string key) =>
            _world != null && _world.Flags.TryGetValue(key, out bool value) && value;
}
