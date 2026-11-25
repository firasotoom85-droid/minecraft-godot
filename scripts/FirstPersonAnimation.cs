using System;
using Godot;

namespace Minecraft;

public enum FirstPersonAction
{
    None,
    Mine,
    Melee,
    Place,
    Use,
    Eat,
    BowRelease,
    ShieldBlock,
    Landing,
    Select,
}

/// <summary>Procedural pose offsets for the existing first-person arm and item sprite.</summary>
public readonly record struct FirstPersonPose(
        Vector3 ArmPosition,
        Vector3 ArmRotation,
        Vector3 ItemPosition,
        Vector3 ItemRotation,
        float ItemScale);

/// <summary>
/// Small deterministic action clock. Gameplay remains in Player; this class
/// only describes how the already-existing viewmodel is posed.
/// </summary>
public sealed class FirstPersonAnimationState
{
    public FirstPersonAction Action { get; private set; }
    public float Elapsed { get; private set; }
    public float Duration { get; private set; } = 0.22f;

    public float Progress => Action == FirstPersonAction.None
            ? 1.0f
            : Mathf.Clamp(Elapsed / Duration, 0.0f, 1.0f);

    public void Play(FirstPersonAction action)
    {
        if (action == FirstPersonAction.None)
            return;

        Action = action;
        Elapsed = 0.0f;
        Duration = FirstPersonAnimation.DurationOf(action);
    }

    public void Advance(double delta)
    {
        if (Action == FirstPersonAction.None || !double.IsFinite(delta) || delta <= 0.0)
            return;

        Elapsed = Mathf.Min(Elapsed + (float)delta, Duration);
        if (Elapsed >= Duration)
            Action = FirstPersonAction.None;
    }
}

public static class FirstPersonAnimation
{
    public static float DurationOf(FirstPersonAction action) => action switch
    {
        FirstPersonAction.Mine => 0.25f,
        FirstPersonAction.Melee => 0.34f,
        FirstPersonAction.Place => 0.24f,
        FirstPersonAction.Use => 0.26f,
        FirstPersonAction.Eat => 0.52f,
        FirstPersonAction.BowRelease => 0.22f,
        FirstPersonAction.ShieldBlock => 0.30f,
        FirstPersonAction.Landing => 0.28f,
        FirstPersonAction.Select => 0.18f,
        _ => 0.22f,
    };

    /// <summary>
    /// Evaluate one action plus independent locomotion/held-state layers.
    /// All values are local offsets, so the existing viewmodel attachment and
    /// camera transform remain untouched.
    /// </summary>
    public static FirstPersonPose Evaluate(
            FirstPersonAction action,
            float progress,
            float bob,
            float idleSway,
            float bowDraw,
            float shieldGuard,
            float climb)
    {
        float p = Mathf.Clamp(progress, 0.0f, 1.0f);
        float draw = Mathf.Clamp(bowDraw, 0.0f, 1.0f);
        float guard = Mathf.Clamp(shieldGuard, 0.0f, 1.0f);
        float climbMotion = Mathf.Clamp(climb, -1.0f, 1.0f);

        Vector3 armPosition = new(0.0f, bob + idleSway, 0.0f);
        Vector3 armRotation = Vector3.Zero;
        Vector3 itemPosition = new(0.0f, bob * 1.12f + idleSway, 0.0f);
        Vector3 itemRotation = Vector3.Zero;
        float itemScale = 1.0f;

        float pulse = MathF.Sin(MathF.PI * p);
        switch (action)
        {
            case FirstPersonAction.Mine:
                {
                    float windup = 1.0f - SmoothStep(0.0f, 0.38f, p);
                    float impact = SmoothStep(0.22f, 0.43f, p)
                            * (1.0f - SmoothStep(0.43f, 1.0f, p));
                    armPosition += new Vector3(0.0f, -0.045f * windup + 0.035f * impact,
                            0.075f * windup - 0.13f * impact);
                    armRotation += new Vector3(-0.20f * windup + 0.92f * impact,
                            0.0f, -0.15f * impact);
                    itemPosition += new Vector3(0.0f, -0.025f * windup + 0.045f * impact,
                            0.045f * windup - 0.09f * impact);
                    itemRotation += new Vector3(-0.12f * windup + 0.42f * impact,
                            0.0f, -0.20f * impact);
                    break;
                }
            case FirstPersonAction.Melee:
                {
                    float windup = 1.0f - SmoothStep(0.0f, 0.30f, p);
                    float strike = SmoothStep(0.18f, 0.40f, p)
                            * (1.0f - SmoothStep(0.56f, 1.0f, p));
                    armPosition += new Vector3(-0.015f * strike, -0.055f * windup,
                            0.11f * windup - 0.16f * strike);
                    armRotation += new Vector3(-0.28f * windup + 0.88f * strike,
                            -0.10f * strike, -0.52f * strike);
                    itemPosition += new Vector3(-0.025f * strike, -0.025f * windup,
                            0.06f * windup - 0.14f * strike);
                    itemRotation += new Vector3(-0.20f * windup + 0.50f * strike,
                            0.0f, -0.58f * strike);
                    break;
                }
            case FirstPersonAction.Place:
                {
                    float tap = SmoothStep(0.08f, 0.32f, p)
                            * (1.0f - SmoothStep(0.32f, 0.88f, p));
                    armPosition += new Vector3(0.0f, -0.055f * tap, -0.045f * tap);
                    armRotation += new Vector3(0.36f * tap, 0.0f, 0.04f * tap);
                    itemPosition += new Vector3(0.0f, -0.075f * tap, -0.07f * tap);
                    itemRotation += new Vector3(0.20f * tap, 0.0f, 0.0f);
                    break;
                }
            case FirstPersonAction.Use:
                {
                    armPosition += new Vector3(0.0f, -0.025f * pulse, -0.045f * pulse);
                    armRotation += new Vector3(0.22f * pulse, 0.0f, -0.06f * pulse);
                    itemPosition += new Vector3(0.0f, -0.035f * pulse, -0.055f * pulse);
                    itemRotation += new Vector3(0.12f * pulse, 0.0f, -0.05f * pulse);
                    break;
                }
            case FirstPersonAction.Eat:
                {
                    float lift = MathF.Sin(MathF.PI * p);
                    float chew = MathF.Sin(MathF.PI * 2.0f * p) * (1.0f - p);
                    armPosition += new Vector3(0.0f, 0.04f * lift, -0.035f * lift);
                    armRotation += new Vector3(-0.22f * lift, 0.0f, -0.05f * lift);
                    itemPosition += new Vector3(-0.005f, 0.22f * lift, -0.11f * lift);
                    itemRotation += new Vector3(-0.30f * lift + 0.06f * chew, 0.0f, -0.08f * lift);
                    itemScale = 1.0f + 0.035f * lift;
                    break;
                }
            case FirstPersonAction.BowRelease:
                {
                    float drawBack = 1.0f - SmoothStep(0.0f, 0.42f, p);
                    float release = SmoothStep(0.32f, 0.62f, p)
                            * (1.0f - SmoothStep(0.62f, 1.0f, p));
                    armPosition += new Vector3(-0.025f * drawBack, 0.0f, 0.10f * drawBack - 0.07f * release);
                    armRotation += new Vector3(-0.12f * drawBack + 0.32f * release,
                            0.0f, 0.14f * release);
                    itemPosition += new Vector3(-0.025f * drawBack, 0.025f * drawBack,
                            0.08f * drawBack - 0.04f * release);
                    itemRotation += new Vector3(-0.08f * drawBack, 0.0f, -0.08f * drawBack);
                    break;
                }
            case FirstPersonAction.ShieldBlock:
                {
                    float raise = SmoothStep(0.0f, 0.24f, p)
                            * (1.0f - SmoothStep(0.42f, 1.0f, p));
                    armPosition += new Vector3(-0.055f * raise, 0.055f * raise, -0.035f * raise);
                    armRotation += new Vector3(-0.18f * raise, -0.10f * raise, 0.34f * raise);
                    itemPosition += new Vector3(-0.16f * raise, 0.10f * raise, 0.025f * raise);
                    itemRotation += new Vector3(-0.20f * raise, 0.0f, 0.30f * raise);
                    itemScale = 1.0f + 0.06f * raise;
                    break;
                }
            case FirstPersonAction.Landing:
                {
                    float impact = pulse;
                    armPosition += new Vector3(0.0f, -0.075f * impact, 0.035f * impact);
                    armRotation += new Vector3(0.18f * impact, 0.0f, -0.08f * impact);
                    itemPosition += new Vector3(0.0f, -0.05f * impact, 0.025f * impact);
                    break;
                }
            case FirstPersonAction.Select:
                {
                    float settle = pulse;
                    armPosition += new Vector3(0.0f, 0.02f * settle, -0.025f * settle);
                    armRotation += new Vector3(-0.10f * settle, 0.0f, 0.04f * settle);
                    itemPosition += new Vector3(0.0f, 0.04f * settle, -0.035f * settle);
                    break;
                }
        }

        // Held-state overlays are independent of one-shot action timing.
        armPosition += new Vector3(-0.035f * draw, 0.025f * draw, 0.045f * draw);
        armRotation += new Vector3(-0.12f * draw, 0.0f, 0.10f * draw);
        itemPosition += new Vector3(-0.035f * draw, 0.02f * draw, 0.055f * draw);
        itemRotation += new Vector3(-0.08f * draw, 0.0f, 0.04f * draw);

        armPosition += new Vector3(-0.075f * guard, 0.035f * guard, -0.045f * guard);
        armRotation += new Vector3(-0.18f * guard, -0.08f * guard, 0.22f * guard);
        itemPosition += new Vector3(-0.14f * guard, 0.08f * guard, 0.025f * guard);
        itemRotation += new Vector3(-0.18f * guard, 0.0f, 0.26f * guard);
        itemScale *= 1.0f + 0.04f * guard;

        armPosition += new Vector3(0.018f * climbMotion, 0.014f * MathF.Abs(climbMotion),
                -0.025f * MathF.Abs(climbMotion));
        armRotation += new Vector3(0.10f * climbMotion, 0.0f, 0.08f * climbMotion);
        itemPosition += new Vector3(0.018f * climbMotion, 0.01f * MathF.Abs(climbMotion), 0.0f);

        return new FirstPersonPose(armPosition, armRotation, itemPosition, itemRotation, itemScale);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp((value - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}
