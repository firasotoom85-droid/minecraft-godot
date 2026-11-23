using Godot;

namespace Minecraft;

/// <summary>
/// Photo mode (P): a detached free-flying camera with a frozen clock, time
/// scrubbing (arrow keys) and a clean HUD-less frame. The player stays
/// exactly where they were; leaving restores everything.
/// </summary>
public partial class PhotoMode : Node
{
    public Player? PlayerRef;
    public VoxelWorld? World;
    public Hud? HudRef;
    public DayNight? DayNightRef;

    public bool Active { get; private set; }

    private Camera3D _cam = null!;
    private float _yaw;
    private float _pitch;
    private bool _hudWasVisible;
    private bool _playerWasInputLocked;
    private bool _playerCameraWasCurrent;
    private bool _dayNightWasPaused;

    public void Setup(Player player, VoxelWorld world, Hud hud, DayNight daynight)
    {
        PlayerRef = player;
        World = world;
        HudRef = hud;
        DayNightRef = daynight;
        _cam = new Camera3D
        {
            Name = "PhotoCam",
            Far = 600.0f,
            Fov = Settings.Fov,
            Current = false,
        };
        AddChild(_cam);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (PlayerRef is not { } player)
            return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.IsActionPressed("photo_mode"))
        {
            if (CanTogglePhotoMode(Active, player.InputLocked, player.Stats.Dead, GetTree().Paused))
            {
                Toggle();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (GetTree().Paused || player.Stats.Dead || (player.InputLocked && !Active))
            return;

        if (Active && @event is InputEventMouseMotion motion
                && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sens = Settings.MouseSensitivity * 0.8f;
            _yaw -= motion.Relative.X * sens;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sens, -1.5f, 1.5f);
        }
    }

    internal static bool CanTogglePhotoMode(bool active, bool inputLocked, bool dead, bool treePaused) =>
            active || (!inputLocked && !dead && !treePaused);

    private void Toggle()
    {
        if (PlayerRef == null || HudRef == null)
        {
            return;
        }

        if (Active)
        {
            ExitPhotoMode(restoreCamera: true, showToast: true);
            return;
        }

        _playerWasInputLocked = PlayerRef.InputLocked;
        _playerCameraWasCurrent = PlayerRef.Camera.Current;
        _hudWasVisible = HudRef.Visible;
        _dayNightWasPaused = DayNightRef?.Paused ?? false;
        Active = true;
        _cam.GlobalPosition = PlayerRef.Camera.GlobalPosition;
        _yaw = PlayerRef.Camera.GlobalRotation.Y;
        _pitch = PlayerRef.Camera.Rotation.X;
        _cam.Current = true;
        PlayerRef.InputLocked = true;
        HudRef.Visible = false;
        if (DayNightRef != null)
            DayNightRef.Paused = true;
    }

    private void ExitPhotoMode(bool restoreCamera, bool showToast)
    {
        if (!Active)
            return;
        Active = false;
        if (_cam != null && GodotObject.IsInstanceValid(_cam))
            _cam.Current = false;
        if (PlayerRef != null && GodotObject.IsInstanceValid(PlayerRef))
        {
            PlayerRef.InputLocked = _playerWasInputLocked;
            if (restoreCamera && PlayerRef.Camera != null && GodotObject.IsInstanceValid(PlayerRef.Camera))
                PlayerRef.Camera.Current = _playerCameraWasCurrent;
        }
        if (HudRef != null && GodotObject.IsInstanceValid(HudRef))
        {
            HudRef.Visible = _hudWasVisible;
            if (showToast)
                HudRef.ShowToast("photo mode off");
        }
        if (DayNightRef != null && GodotObject.IsInstanceValid(DayNightRef))
            DayNightRef.Paused = _dayNightWasPaused;
    }

    public override void _ExitTree()
    {
        ExitPhotoMode(restoreCamera: false, showToast: false);
    }

    public override void _Process(double delta)
    {
        if (!Active || PlayerRef == null || World == null)
        {
            return;
        }

        float dt = (float)delta;
        float speed = Input.IsActionPressed("sprint") ? 30.0f : 11.0f;
        Vector2 dir = Input.GetVector("left", "right", "forward", "back");
        Vector3 fwd = new(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        Vector3 right = new(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        Vector3 move = (fwd * dir.Y + right * dir.X);
        if (move.LengthSquared() > 0.0f)
        {
            move = move.Normalized() * speed;
        }

        if (Input.IsActionPressed("jump"))
        {
            move.Y += speed;
        }

        _cam.GlobalPosition += move * dt;
        _cam.Rotation = new Vector3(_pitch, _yaw, 0);

        // Time scrub while the clock is frozen: arrows sweep the sun.
        double scrub = 0.0;
        if (Input.IsPhysicalKeyPressed(Key.Left))
        {
            scrub -= 0.09 * delta;
        }

        if (Input.IsPhysicalKeyPressed(Key.Right))
        {
            scrub += 0.09 * delta;
        }

        if (scrub != 0.0)
        {
            World.TimeOfDay = ((World.TimeOfDay + scrub) % 1.0 + 1.0) % 1.0;
        }
    }
}
