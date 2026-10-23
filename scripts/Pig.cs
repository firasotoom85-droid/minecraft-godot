using Godot;

namespace Minecraft;

/// <summary>Passive pig: pink wanderer, drops porkchops.</summary>
public partial class Pig : Wanderer
{
    public Pig()
    {
        MaxHealth = 8.0f;
        Speed = 1.45f;
        DisplayName = "Pig";
        Loot = [(ItemId.RawPork, 1, 3, 1.0f)];
    }

    protected override void AddShape()
    {
        CollisionShape3D shape = new()
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.82f, 0.85f, 1.15f),
            },
            Position = new Vector3(0, 0.62f, 0),
        };
        AddChild(shape);
    }

    protected override void BuildBody()
    {
        Color skin = new(0.91f, 0.56f, 0.58f);
        Color snout = new(0.96f, 0.68f, 0.68f);
        Color hoof = new(0.38f, 0.24f, 0.20f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.82f, 0.56f, 1.08f), new Vector3(0, 0.70f, 0), skin);
        AddBox(st, new Vector3(0.48f, 0.44f, 0.50f), new Vector3(0, 0.78f, -0.66f), skin);
        AddBox(st, new Vector3(0.34f, 0.22f, 0.16f), new Vector3(0, 0.72f, -0.94f), snout);
        AddBox(st, new Vector3(0.10f, 0.08f, 0.05f), new Vector3(-0.08f, 0.74f, -1.02f), hoof);
        AddBox(st, new Vector3(0.10f, 0.08f, 0.05f), new Vector3(0.08f, 0.74f, -1.02f), hoof);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.16f, 0.18f, 0.16f), new Vector3(sx * 0.22f, 1.03f, -0.64f), skin.Darkened(0.08f));
            AddBox(st, new Vector3(0.15f, 0.36f, 0.15f), new Vector3(sx * 0.28f, 0.24f, -0.34f), skin);
            AddBox(st, new Vector3(0.15f, 0.36f, 0.15f), new Vector3(sx * 0.28f, 0.24f, 0.34f), skin);
        }

        AddBox(st, new Vector3(0.14f, 0.12f, 0.10f), new Vector3(0, 0.82f, 0.58f), skin.Darkened(0.08f));
        CommitBody(st);
    }
}
