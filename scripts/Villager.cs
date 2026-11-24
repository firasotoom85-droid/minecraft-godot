using Godot;

namespace Minecraft;

public static class VillagerLifecycleRules
{
    public static bool CanInteract(bool dead, bool queuedForDeletion) =>
            !dead && !queuedForDeletion;
}

public partial class Villager : Mob
{
    private Vector3 _work;
    private Vector3 _plaza;
    private Vector3 _destination;
    private float _destinationRefresh;
    private float _fleeCooldown;

    public VillagerProfession Profession { get; private set; }
    public string ResidentId { get; private set; } = "";
    public int TradesCompleted { get; private set; }

    public Villager(VillagerSpawnInfo spawn, string residentId, int tradesCompleted = 0)
    {
        ResidentId = residentId;
        Profession = spawn.Profession;
        DisplayName = spawn.Name;
        Home = spawn.Home;
        _work = spawn.Work;
        _plaza = new Vector3((spawn.Home.X + spawn.Work.X) * 0.5f,
                spawn.Home.Y, (spawn.Home.Z + spawn.Work.Z) * 0.5f);
        TradesCompleted = tradesCompleted;
        MaxHealth = 20.0f;
        Health = MaxHealth;
        Speed = 1.25f;
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.34f, Height = 1.75f },
            Position = new Vector3(0, 0.88f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color tunic = Profession switch
        {
            VillagerProfession.Farmer => new Color(0.42f, 0.62f, 0.28f),
            VillagerProfession.Merchant => new Color(0.35f, 0.48f, 0.72f),
            VillagerProfession.Blacksmith => new Color(0.42f, 0.42f, 0.46f),
            VillagerProfession.Librarian => new Color(0.58f, 0.40f, 0.68f),
            _ => new Color(0.62f, 0.48f, 0.28f),
        };
        Color skin = new(0.76f, 0.57f, 0.42f);
        Color trim = Profession == VillagerProfession.Guard
                ? new Color(0.72f, 0.22f, 0.18f) : new Color(0.82f, 0.72f, 0.48f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.58f, 0.92f, 0.38f), new Vector3(0, 0.95f, 0), tunic);
        AddBox(st, new Vector3(0.39f, 0.42f, 0.39f), new Vector3(0, 1.64f, 0), skin);
        AddBox(st, new Vector3(0.62f, 0.20f, 0.42f), new Vector3(0, 1.91f, 0), trim);
        AddBox(st, new Vector3(0.14f, 0.62f, 0.17f), new Vector3(-0.36f, 0.98f, 0), tunic);
        AddBox(st, new Vector3(0.14f, 0.62f, 0.17f), new Vector3(0.36f, 0.98f, 0), tunic);
        AddBox(st, new Vector3(0.17f, 0.58f, 0.18f), new Vector3(-0.14f, 0.28f, 0), trim);
        AddBox(st, new Vector3(0.17f, 0.58f, 0.18f), new Vector3(0.14f, 0.28f, 0), trim);
        if (Profession == VillagerProfession.Farmer)
            AddBox(st, new Vector3(0.56f, 0.16f, 0.48f), new Vector3(0, 1.98f, 0), new Color(0.45f, 0.29f, 0.14f));
        if (Profession == VillagerProfession.Guard)
            AddBox(st, new Vector3(0.12f, 0.54f, 0.12f), new Vector3(0.43f, 1.0f, 0.02f), new Color(0.72f, 0.72f, 0.76f));
        CommitBody(st);
    }

    protected override Vector3 Think(double delta)
    {
        Vector3 threat = Manager?.HostileThreatDirection(this, 12.0f) ?? Vector3.Zero;
        if (threat.LengthSquared() > 0.01f)
        {
            _fleeCooldown = 2.5f;
            Walking = true;
            Dir = threat;
            return threat;
        }

        _fleeCooldown = Mathf.Max(0.0f, _fleeCooldown - (float)delta);
        if (_fleeCooldown > 0.0f) return Dir;
        _destinationRefresh -= (float)delta;
        if (_destinationRefresh <= 0.0f)
        {
            _destinationRefresh = 1.2f;
            _destination = VillagerSchedule.DestinationAt(World?.TimeOfDay ?? 0.5,
                    Home, _work, _plaza);
        }

        Vector3 toward = _destination - GlobalPosition;
        toward.Y = 0.0f;
        if (toward.LengthSquared() <= 1.35f * 1.35f)
        {
            Walking = false;
            return Vector3.Zero;
        }
        Walking = true;
        Dir = toward.Normalized();
        return Dir;
    }

    public override bool Interact(Player player)
    {
        if (!VillagerLifecycleRules.CanInteract(Dead, IsQueuedForDeletion())) return false;
        player.InteractVillager(this);
        return true;
    }

    public void RecordTrade() => TradesCompleted++;

    public string DialogueFor(double timeOfDay)
    {
        string greeting = VillagerSchedule.ActivityAt(timeOfDay) switch
        {
            VillagerSchedule.Activity.Work => "I'm busy with my work, but I have a moment.",
            VillagerSchedule.Activity.Gathering => "The square is lively today.",
            _ => "It's quiet at home. What can I do for you?",
        };
        string role = Profession switch
        {
            VillagerProfession.Farmer => "The fields are looking healthy.",
            VillagerProfession.Merchant => "Gold ingots keep the village trade moving.",
            VillagerProfession.Blacksmith => "Good tools make long journeys safer.",
            VillagerProfession.Librarian => "Knowledge is worth bringing back from the caves.",
            _ => "Keep an eye on the paths after sunset.",
        };
        return $"{greeting}\n{role}";
    }

    public VillagerSave ToSave()
    {
        // VillageSystem snapshots residents from _ExitTree after the scene has
        // already removed them from the tree; Position remains valid there.
        Vector3 position = IsInsideTree() ? GlobalPosition : Position;
        return new VillagerSave(
                ResidentId, DisplayName, (int)Profession,
                [position.X, position.Y, position.Z],
                [Home.X, Home.Y, Home.Z],
                [_work.X, _work.Y, _work.Z],
                Health, TradesCompleted);
    }
}
