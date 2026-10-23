using Godot;

namespace Minecraft;

/// <summary>
/// Live 3D menu backdrop: builds a small synchronous slice of the selected
/// world and orbits a cinematic camera around it behind the menu UI.
/// </summary>
public partial class MenuBackdrop : Node3D
{
    private const float OrbitRadius = 34.0f;
    private const float OrbitHeight = 10.0f;
    private const float OrbitSpeed = 0.028f;

    private VoxelWorld _world = null!;
    private Camera3D _camera = null!;
    private Vector3 _target;
    private float _angle = 0.8f;

    public override void _Ready()
    {
        WorldEnvironment environment = new()
        {
            Name = "MenuEnvironment",
            Environment = WorldEnvironmentFactory.Build(),
        };
        Godot.Environment menuEnv = environment.Environment;
        menuEnv.FogDensity = 0.0022f;
        menuEnv.FogHeightDensity = 0.006f;
        menuEnv.VolumetricFogDensity = 0.0018f;
        menuEnv.GlowIntensity = 0.7f;
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
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120.0f,
            RotationDegrees = new Vector3(-34.0f, -52.0f, 0.0f),
        };
        AddChild(sun);

        _world = new VoxelWorld
        {
            Name = "MenuWorld",
            ViewDistance = 3,
        };
        AddChild(_world);

        Vector3 center = _world.SavedPlayerPos ?? _world.FindSpawn();
        _target = center + new Vector3(0, 7.0f, 0);
        _world.BuildPreview(center, 4);

        _camera = new Camera3D
        {
            Name = "MenuCamera",
            Current = true,
            Far = 600.0f,
            Fov = 62.0f,
        };
        AddChild(_camera);
        UpdateCamera();
    }

    public override void _Process(double delta)
    {
        _angle += (float)delta * OrbitSpeed;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        float y = OrbitHeight + Mathf.Sin(_angle * 0.7f) * 1.6f;
        float radius = OrbitRadius + Mathf.Sin(_angle * 0.37f) * 2.5f;
        Vector3 pos = _target + new Vector3(
                Mathf.Cos(_angle) * radius,
                y,
                Mathf.Sin(_angle) * radius);
        // Never let the orbit dip into a hillside.
        int ground = _world.SurfaceHeight(Mathf.FloorToInt(pos.X), Mathf.FloorToInt(pos.Z));
        pos.Y = Mathf.Max(pos.Y, ground + 6.0f);
        _camera.GlobalPosition = pos;
        _camera.LookAt(_target, Vector3.Up);
    }
}
