using Godot;

namespace Minecraft;

/// <summary>A wandering trader. Right-click opens the barter panel.</summary>
public partial class Trader : Wanderer
{
    public Trader()
    {
        MaxHealth = 20.0f;
        Speed = 1.15f;
        DisplayName = "Wanderer";
    }

    protected override void AddShape()
    {
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.62f, 1.75f, 0.45f),
            },
            Position = new Vector3(0, 0.88f, 0),
        });
    }

    protected override void BuildBody()
    {
        Color cloak = new(0.28f, 0.34f, 0.46f);
        Color cloakLight = new(0.40f, 0.48f, 0.60f);
        Color skin = new(0.72f, 0.55f, 0.40f);
        Color pack = new(0.48f, 0.30f, 0.14f);
        Color gold = new(0.95f, 0.78f, 0.24f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.60f, 0.95f, 0.38f), new Vector3(0, 1.05f, 0), cloak);
        AddBox(st, new Vector3(0.38f, 0.36f, 0.36f), new Vector3(0, 1.68f, 0), skin);
        AddBox(st, new Vector3(0.54f, 0.30f, 0.44f), new Vector3(0, 1.86f, 0), cloakLight);
        AddBox(st, new Vector3(0.56f, 0.50f, 0.32f), new Vector3(0, 1.04f, 0.30f), pack);
        AddBox(st, new Vector3(0.18f, 0.12f, 0.05f), new Vector3(0, 1.04f, 0.48f), gold);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.15f, 0.68f, 0.16f), new Vector3(sx * 0.38f, 1.04f, 0), cloak);
            AddBox(st, new Vector3(0.16f, 0.58f, 0.18f), new Vector3(sx * 0.14f, 0.32f, 0), cloak);
        }

        CommitBody(st);
    }

    public override bool Interact(Player player)
    {
        player.EmitSignal(Player.SignalName.TraderUsed);
        return true;
    }
}
