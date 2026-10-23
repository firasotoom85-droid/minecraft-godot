using Godot;

namespace Minecraft;

/// <summary>Passive cow: slow wanderer, drops beef and leather.</summary>
public partial class Cow : Wanderer
{
    public Cow()
    {
        MaxHealth = 10.0f;
        Speed = 1.35f;
        DisplayName = "Cow";
        Loot =
        [
            (ItemId.RawBeef, 1, 3, 1.0f),
            (ItemId.Leather, 1, 2, 0.85f),
        ];
    }

    protected override void AddShape()
    {
        CollisionShape3D shape = new()
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.95f, 1.05f, 1.45f),
            },
            Position = new Vector3(0, 0.75f, 0),
        };
        AddChild(shape);
    }

    protected override void BuildBody()
    {
        Color hide = new(0.24f, 0.18f, 0.14f);
        Color patch = new(0.78f, 0.74f, 0.68f);
        Color muzzle = new(0.66f, 0.50f, 0.48f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.92f, 0.62f, 1.3f), new Vector3(0, 0.85f, 0), hide);
        AddBox(st, new Vector3(0.50f, 0.42f, 0.34f), new Vector3(0, 0.88f, 0.68f), patch);
        AddBox(st, new Vector3(0.48f, 0.46f, 0.52f), new Vector3(0, 1.02f, -0.72f), hide);
        AddBox(st, new Vector3(0.40f, 0.20f, 0.22f), new Vector3(0, 0.92f, -1.00f), muzzle);
        AddBox(st, new Vector3(0.50f, 0.26f, 0.50f), new Vector3(0, 1.32f, -0.76f), patch);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.07f, 0.26f, 0.07f), new Vector3(sx * 0.26f, 1.38f, -0.84f),
                    new Color(0.88f, 0.84f, 0.72f));
            AddBox(st, new Vector3(0.16f, 0.56f, 0.16f), new Vector3(sx * 0.30f, 0.30f, -0.45f), hide);
            AddBox(st, new Vector3(0.16f, 0.56f, 0.16f), new Vector3(sx * 0.30f, 0.30f, 0.45f), hide);
            AddBox(st, new Vector3(0.10f, 0.48f, 0.10f), new Vector3(sx * 0.32f, 0.24f, -0.05f), hide);
        }

        AddBox(st, new Vector3(0.18f, 0.08f, 0.08f), new Vector3(0, 0.62f, 0.50f), patch);
        CommitBody(st);
    }
}
