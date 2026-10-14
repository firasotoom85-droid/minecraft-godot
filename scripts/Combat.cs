using Godot;

namespace Minecraft;

/// <summary>What caused a damage event — drives death messages and feedback.</summary>
public enum DamageKind : byte
{
    Generic,
    Melee,
    Fall,
    Drown,
    Starve,
    Lightning,
    Fire,
}

/// <summary>
/// One routing unit of damage. Every hurt in the game goes through this —
/// falls, drowning, starvation, mob hits and lightning all construct one.
/// <see cref="Source"/> is where the hit came from (knockback direction),
/// <see cref="Knockback"/> and <see cref="Lift"/> carry the push impulse.
/// </summary>
public readonly record struct DamageInfo(
    float Amount,
    DamageKind Kind,
    Vector3 Source = default,
    float Knockback = 0.0f,
    float Lift = 0.0f)
{
    public static DamageInfo Of(float amount, DamageKind kind) => new(amount, kind);
}

/// <summary>The damage pipeline seam: anything that can be hurt implements it.</summary>
public interface IDamageable
{
    /// <summary>Apply damage; returns false when it was refused (dead/invulnerable/creative).</summary>
    bool ApplyDamage(in DamageInfo info);
}
