using Godot;

namespace Minecraft;

/// <summary>
/// Sheep wander and flock while wild. Wheat can be used to breed them; an
/// empty hand still tames one, and subsequent interactions toggle stay/follow.
/// </summary>
public partial class Sheep : Wanderer
{
    public bool Tamed;
    public bool Staying;

    public Sheep()
    {
        MaxHealth = 10.0f;
        Speed = 1.6f;
        DisplayName = "Sheep";
        Loot =
        [
            (ItemId.RawMutton, 1, 2, 1.0f),
            (ItemId.Wool, 1, 1, 1.0f),
        ];
    }

    protected override void AddShape()
    {
        CollisionShape3D shape = new()
        {
            Shape = new BoxShape3D { Size = new Vector3(0.85f, 0.9f, 1.1f) },
            Position = new Vector3(0, 0.75f, 0),
        };
        AddChild(shape);
    }

    protected override void BuildBody()
    {
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(0.85f, 0.72f, 1.1f), new Vector3(0, 0.78f, 0.05f), new Color(0.93f, 0.92f, 0.89f));
        AddBox(st, new Vector3(0.42f, 0.42f, 0.34f), new Vector3(0, 1.02f, -0.66f), new Color(0.93f, 0.92f, 0.89f));
        AddBox(st, new Vector3(0.44f, 0.26f, 0.16f), new Vector3(0, 0.98f, -0.86f), new Color(0.75f, 0.66f, 0.60f));
        foreach (float sx in new[] { -1.0f, 1.0f })
        {
            foreach (float sz in new[] { -1.0f, 1.0f })
            {
                AddBox(st, new Vector3(0.16f, 0.5f, 0.16f), new Vector3(sx * 0.26f, 0.25f, sz * 0.34f),
                        new Color(0.30f, 0.28f, 0.27f));
            }
        }

        CommitBody(st);
    }

    /// <summary>Wheat is the breeding food; otherwise the original tame/stay/follow controls remain.</summary>
    public override bool Interact(Player player)
    {
        if (TryFeed(player))
        {
            return true;
        }

        if (!Tamed)
        {
            Tamed = true;
            Staying = false;
            RaiseSignalled("tame");
            player.EmitSignal(Player.SignalName.Notice, "Tamed — it follows you now  (use again: stay)");
        }
        else if (!Staying)
        {
            Staying = true;
            Home = GlobalPosition;
            RaiseSignalled("stay");
            player.EmitSignal(Player.SignalName.Notice, "Waiting here  (use again: follow)");
        }
        else
        {
            Staying = false;
            RaiseSignalled("follow");
            player.EmitSignal(Player.SignalName.Notice, "Following you");
        }

        return true;
    }

    protected override Vector3 Think(double delta)
    {
        if (Tamed && !Staying && Manager?.PlayerRef is { } player && !player.Stats.Dead)
        {
            Vector3 toPlayer = player.GlobalPosition - GlobalPosition;
            toPlayer.Y = 0.0f;
            float distance = toPlayer.Length();
            if (distance > 48.0f)
            {
                Vector3 target = player.GlobalPosition + new Vector3(2, 0, 2);
                Vector2I cp = new(
                        Mathf.FloorToInt(target.X / VoxelWorld.ChunkX),
                        Mathf.FloorToInt(target.Z / VoxelWorld.ChunkZ));
                if (World?.HasChunk(cp) == true)
                {
                    GlobalPosition = target;
                }

                return Vector3.Zero;
            }

            return distance > 3.2f ? toPlayer : Vector3.Zero;
        }

        return base.Think(delta);
    }
}
