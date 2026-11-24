using Godot;

namespace Minecraft;

/// <summary>
/// Lightweight live 3D menu backdrop. It keeps the existing procedural voxel
/// presentation but uses a small render-only preview and a separate frame
/// budget so an idle menu does not cost as much as active gameplay.
/// </summary>
public partial class MenuBackdrop : Node3D
{
    private const int PreviewRadius = 2;
    private const float OrbitRadius = 34.0f;
    private const float OrbitHeight = 10.0f;
    private const float OrbitSpeed = 0.028f;
    private const int MenuFrameCap = 30;

    private VoxelWorld _world = null!;
    private Camera3D _camera = null!;
    private Vector3 _target;
    private float _angle = 0.8f;
    private int _lastGroundX = int.MinValue;
    private int _lastGroundZ = int.MinValue;
    private int _cachedGroundHeight;
    private int _previousMaxFps;
    private bool _capturedMaxFps;
    private Viewport? _menuViewport;
    private float _previousRenderScale;
    private Viewport.Scaling3DModeEnum _previousScalingMode;
    private Viewport.ScreenSpaceAAEnum _previousScreenSpaceAa;
    private Viewport.Msaa _previousMsaa;
    private bool _previousTaa;
    private bool _capturedViewportSettings;

    public override void _Ready()
    {
        _previousMaxFps = Engine.MaxFps;
        _capturedMaxFps = true;
        Engine.MaxFps = MenuFrameCap;

        // The menu owns its temporary viewport quality. Main.Gfx reapplies the
        // player's selected preset when gameplay starts.
        Viewport viewport = GetViewport();
        _menuViewport = viewport;
        _previousRenderScale = viewport.Scaling3DScale;
        _previousScalingMode = viewport.Scaling3DMode;
        _previousScreenSpaceAa = viewport.ScreenSpaceAA;
        _previousMsaa = viewport.Msaa3D;
        _previousTaa = viewport.UseTaa;
        _capturedViewportSettings = true;

        viewport.UseTaa = false;
        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = 0.80f;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        viewport.Msaa3D = Viewport.Msaa.Disabled;

        WorldEnvironment environment = new()
        {
            Name = "MenuEnvironment",
            Environment = WorldEnvironmentFactory.Build(),
        };
        Godot.Environment menuEnv = environment.Environment;
        // The backdrop keeps its authored exponential atmosphere values;
        // gameplay's camera-depth haze is reserved for streamed world views.
        menuEnv.FogMode = Godot.Environment.FogModeEnum.Exponential;
        menuEnv.FogDensity = 0.0022f;
        menuEnv.FogHeightDensity = 0.006f;
        menuEnv.VolumetricFogEnabled = false;
        menuEnv.SsaoEnabled = false;
        menuEnv.SsrEnabled = false;
        menuEnv.GlowEnabled = false;
        menuEnv.TonemapExposure = 1.12f;
        menuEnv.FogLightColor = new Color(0.95f, 0.78f, 0.58f);
        if (menuEnv.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = new Color(0.10f, 0.32f, 0.68f);
            sky.SkyHorizonColor = new Color(1.0f, 0.72f, 0.46f);
            sky.GroundHorizonColor = new Color(0.95f, 0.78f, 0.58f);
            sky.GroundBottomColor = new Color(0.18f, 0.20f, 0.22f);
        }
        AddChild(environment);

        DirectionalLight3D sun = new()
        {
            Name = "MenuSun",
            LightColor = new Color(1.0f, 0.88f, 0.70f),
            LightEnergy = 1.3f,
            ShadowEnabled = false,
            RotationDegrees = new Vector3(-34.0f, -52.0f, 0.0f),
        };
        AddChild(sun);

        _world = new VoxelWorld
        {
            Name = "MenuWorld",
            ViewDistance = PreviewRadius,
            BuildCollision = false,
        };
        AddChild(_world);

        Vector3 center = _world.SavedPlayerPos ?? _world.FindSpawn();
        _target = center + new Vector3(0, 7.0f, 0);
        _world.BuildPreviewStreaming(center, PreviewRadius);

        _camera = new Camera3D
        {
            Name = "MenuCamera",
            Current = true,
            Far = 240.0f,
            Fov = 62.0f,
        };
        AddChild(_camera);
        UpdateCamera();
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || !GetWindow().HasFocus())
        {
            return;
        }

        _angle += (float)delta * OrbitSpeed;
        UpdateCamera();
    }

    public override void _ExitTree()
    {
        if (_capturedMaxFps)
        {
            Engine.MaxFps = _previousMaxFps;
            _capturedMaxFps = false;
        }

        if (_capturedViewportSettings && GodotObject.IsInstanceValid(_menuViewport))
        {
            // Restore the previous viewport state before another scene applies
            // its own graphics preset. FSR2 requires its scale to be restored
            // before switching the scaling mode.
            if (_previousScalingMode == Viewport.Scaling3DModeEnum.Fsr2)
            {
                _menuViewport!.Scaling3DScale = _previousRenderScale;
                _menuViewport.Scaling3DMode = _previousScalingMode;
            }
            else
            {
                _menuViewport!.Scaling3DMode = _previousScalingMode;
                _menuViewport.Scaling3DScale = _previousRenderScale;
            }

            _menuViewport.UseTaa = _previousTaa;
            _menuViewport.ScreenSpaceAA = _previousScreenSpaceAa;
            _menuViewport.Msaa3D = _previousMsaa;
            _capturedViewportSettings = false;
            _menuViewport = null;
        }
    }

    private void UpdateCamera()
    {
        float y = OrbitHeight + Mathf.Sin(_angle * 0.7f) * 1.6f;
        float radius = OrbitRadius + Mathf.Sin(_angle * 0.37f) * 2.5f;
        Vector3 pos = _target + new Vector3(
                Mathf.Cos(_angle) * radius,
                y,
                Mathf.Sin(_angle) * radius);

        // Terrain height is stable for a voxel x/z cell. Cache it instead of
        // running the generator's height query for every rendered frame.
        int groundX = Mathf.FloorToInt(pos.X);
        int groundZ = Mathf.FloorToInt(pos.Z);
        if (groundX != _lastGroundX || groundZ != _lastGroundZ)
        {
            _lastGroundX = groundX;
            _lastGroundZ = groundZ;
            _cachedGroundHeight = _world.SurfaceHeight(groundX, groundZ);
        }

        pos.Y = Mathf.Max(pos.Y, _cachedGroundHeight + 6.0f);
        _camera.GlobalPosition = pos;
        _camera.LookAt(_target, Vector3.Up);
    }
}
