using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Live quality presets. Every heavy setting lives here so it can be tuned
/// in one place. F6 cycles presets, F7/F8 change the stream radius, F3/F9
/// are handled by other systems.
/// </summary>
public partial class Gfx : Node
{
    [Signal] public delegate void PresetChangedEventHandler(string text);

    /// <summary>msaa: 0=off, 1=2x, 2=4x, 3=8x. lod: far-view LOD ring radius in metres (0 = off).</summary>
    public sealed record Preset(
        string Name, int View, int Inflight, float ShadowDistance, int ShadowSize,
        bool Ssao, bool Ssr, bool Glow, bool VolFog, float Scale, int Msaa,
        bool Taa, bool Fxaa, float Lod, bool Fsr2);

    // Ordered low -> max.
    public static readonly IReadOnlyList<Preset> Presets =
    [
        new("LOW", 5, 3, 60.0f, 1024, false, false, false, false, 0.85f, 0, false, true, 0.0f, false),
        new("HIGH", 8, 5, 90.0f, 2048, true, false, true, false, 1.0f, 0, true, false, 320.0f, false),
        new("ULTRA", 10, 6, 110.0f, 4096, true, true, true, false, 1.1f, 0, true, false, 512.0f, false),
        new("MAX", 12, 6, 130.0f, 4096, true, true, true, true, 1.25f, 0, true, false, 640.0f, false),
        new("MAX · FSR2", 12, 6, 130.0f, 4096, true, true, true, true, 0.7f, 0, false, false, 640.0f, true),
    ];

    public const int DefaultPreset = 3;  // MAX

    public int Current = DefaultPreset;
    public VoxelWorld? World;
    public DirectionalLight3D? Sun;
    public WorldEnvironment? WorldEnv;

    // Dynamic quality governor: sustained-low fps steps the preset down and,
    // once comfortable again, slowly back up toward the preset the player
    // originally picked. Sampling pauses while the window is unfocused (the
    // OS throttles it) and during the first seconds after load (streaming
    // burst), and every step has a cooldown so it cannot oscillate.
    private double _slowTimer;
    private double _fastTimer;
    private double _cooldown;
    private double _settle = 6.0;
    private int _ceiling = DefaultPreset;
    private bool _autoStep;

    /// <summary>The preset record in effect right now.</summary>
    public Preset CurrentPreset => Presets[Current];

    public void Setup(VoxelWorld world, DirectionalLight3D sun, WorldEnvironment env)
    {
        World = world;
        Sun = sun;
        WorldEnv = env;
        _ceiling = Settings.GfxPreset;
    }

    /// <summary>Frame-rate governor: adapt the preset to what the machine holds.</summary>
    public override void _Process(double delta)
    {
        if (World == null || !Settings.AutoQuality)
        {
            return;
        }

        _settle -= delta;
        _cooldown -= delta;
        if (_settle > 0.0 || _cooldown > 0.0 || GetTree().Paused || !GetWindow().HasFocus())
        {
            return;
        }

        double fps = Engine.GetFramesPerSecond();
        _slowTimer = fps < 26.0 ? _slowTimer + delta : 0.0;
        _fastTimer = fps > 55.0 ? _fastTimer + delta : 0.0;
        if (_slowTimer >= 4.0)
        {
            _slowTimer = 0.0;
            _fastTimer = 0.0;
            if (Current > 0)
            {
                StepPreset(Current - 1,
                        $"quality auto-adjusted — {Presets[Current - 1].Name}  ·  was {fps:0} fps");
                _cooldown = 15.0;
            }
            else
            {
                _cooldown = 60.0;
            }
        }
        else if (_fastTimer >= 45.0)
        {
            _slowTimer = 0.0;
            _fastTimer = 0.0;
            if (Current < _ceiling)
            {
                StepPreset(Current + 1, $"quality auto-raised — {Presets[Current + 1].Name}");
                _cooldown = 90.0;
            }
        }
    }

    private void StepPreset(int index, string toast)
    {
        _autoStep = true;
        ApplyPreset(index);
        _autoStep = false;
        EmitSignal(SignalName.PresetChanged, toast);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.IsActionPressed("cycle_preset"))
        {
            ApplyPreset(Mathf.Wrap(Current + 1, 0, Presets.Count));
        }
        else if (key.IsActionPressed("view_dec"))
        {
            StepView(-1);
        }
        else if (key.IsActionPressed("view_inc"))
        {
            StepView(1);
        }
    }

    public void ApplyPreset(int index, bool persist = true)
    {
        Current = Mathf.Clamp(index, 0, Presets.Count - 1);
        Preset p = CurrentPreset;
        if (World != null)
        {
            World.MaxInFlight = p.Inflight;
            World.SetViewDistance(p.View);
            World.SetLodRadius(p.Lod);
        }

        if (Sun != null)
        {
            Sun.DirectionalShadowMaxDistance = p.ShadowDistance;
        }

        // 32-bit depth: 16-bit quantises badly at 100+ m shadow distances and
        // shows up as crawling acne on large flat faces.
        RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowSize, false);
        if (WorldEnv is { Environment: not null })
        {
            Godot.Environment env = WorldEnv.Environment;
            env.SsaoEnabled = p.Ssao;
            env.SsrEnabled = p.Ssr;
            env.GlowEnabled = p.Glow;
            env.VolumetricFogEnabled = p.VolFog;
        }

        Viewport? vp = GetViewport();
        if (vp != null)
        {
            // Order matters: FSR2 rejects supersampled scales and bilinear never
            // cares, so pick the mode first for bilinear and the scale first for
            // FSR2 to avoid a transient invalid combination (engine falls back).
            // TAA is owned by the preset in both branches: leaving it stale from
            // a previous preset makes FSR2 run with two temporal filters, and a
            // later switch back to bilinear would silently disable TAA.
            vp.UseTaa = p.Taa && !p.Fsr2;
            if (p.Fsr2)
            {
                vp.Scaling3DScale = p.Scale;
                vp.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr2;
            }
            else
            {
                vp.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
                vp.Scaling3DScale = p.Scale;
            }

            vp.ScreenSpaceAA = p.Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
            vp.UseDebanding = true;
            vp.Msaa3D = (Viewport.Msaa)p.Msaa;
        }

        if (!_autoStep)
        {
            EmitSignal(SignalName.PresetChanged, $"{p.Name} quality  ·  view {p.View}  ·  F6 cycle");
        }

        if (persist)
        {
            Settings.GfxPreset = Current;
            Settings.ViewDistance = p.View;
            Settings.Save();
            if (!_autoStep)
            {
                _ceiling = Current;   // a manual choice resets the governor ceiling
            }
        }
    }

    private void StepView(int delta)
    {
        if (World == null)
        {
            return;
        }

        World.SetViewDistance(World.ViewDistance + delta);
        Settings.ViewDistance = World.ViewDistance;
        Settings.Save();
        EmitSignal(SignalName.PresetChanged, $"view distance {World.ViewDistance}");
    }
}
