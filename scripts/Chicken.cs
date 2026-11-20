using Godot;

namespace Minecraft;

/// <summary>Passive chicken: small, fast wanderer, drops chicken and feathers.</summary>
public partial class Chicken : Wanderer
{
    public Chicken()
    {
        MaxHealth = 4.0f;
        Speed = 1.8f;
        DisplayName = "Chicken";
        Loot =
        [
            (ItemId.RawChicken, 1, 1, 1.0f),
            (ItemId.Feather, 1, 2, 0.8f),
        ];
    }

    protected override void AddShape()
    {
        CollisionShape3D shape = new()
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(0.48f, 0.62f, 0.58f),
            },
            Position = new Vector3(0, 0.4f, 0),
        };
        AddChild(shape);
    }

    protected override void BuildBody()
    {
        Color feather = new(0.94f, 0.92f, 0.84f);
        Color wing = new(0.82f, 0.80f, 0.72f);
        Color beak = new(0.95f, 0.66f, 0.18f);
        Color comb = new(0.82f, 0.16f, 0.16f);
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.44f, 0.42f, 0.52f), new Vector3(0, 0.47f, 0), feather);
        AddBox(st, new Vector3(0.28f, 0.28f, 0.30f), new Vector3(0, 0.72f, -0.24f), feather);
        AddBox(st, new Vector3(0.12f, 0.08f, 0.14f), new Vector3(0, 0.68f, -0.44f), beak);
        AddBox(st, new Vector3(0.06f, 0.08f, 0.10f), new Vector3(0, 0.58f, -0.40f), comb);
        AddBox(st, new Vector3(0.06f, 0.12f, 0.16f), new Vector3(0, 0.84f, -0.24f), comb);
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            AddBox(st, new Vector3(0.06f, 0.22f, 0.30f), new Vector3(sx * 0.24f, 0.48f, 0.02f), wing);
        }

        AddBox(st, new Vector3(0.18f, 0.10f, 0.22f), new Vector3(0, 0.62f, 0.24f), wing);
        CommitBody(st);
        AddAnimatedLegs(beak.Darkened(0.25f), 0.26f, 0.07f, 0.10f, 0.12f, 0.28f);
    }
}
