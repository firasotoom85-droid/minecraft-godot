using Godot;

namespace Minecraft;

/// <summary>
/// First-person player: walk / sprint / jump / fly (F or double-tap Space),
/// mouse look, block breaking and placing with a 9-slot hotbar, hold-to-mine
/// repetition, a blocky first-person arm, Minecraft-style selection outline
/// and feel improvements (coyote time, jump buffer). Emits interaction
/// signals consumed by Fx (particles) and Sound (audio).
/// </summary>
public partial class Player : CharacterBody3D, IDamageable
{
    [Export] public float WalkSpeed = 5.0f;
    [Export] public float SprintSpeed = 8.5f;
    [Export] public float FlySpeed = 12.0f;
    [Export] public float JumpVelocity = 8.0f;
    [Export] public float Gravity = 24.0f;
    [Export] public float Reach = 5.0f;

    public bool InputLocked;    // menus / console own the input while true

    private const double CoyoteTime = 0.1;      // jump grace after leaving the ground
    private const double JumpBuffer = 0.12;     // early jump press forgiveness
    private const double MineRepeat = 0.22;     // hold-LMB repeat interval
    private const double DoubleTapTime = 0.3;   // double-tap space to fly
    private const float StepDistance = 1.9f;    // metres between footstep sounds
    private const double SwingCooldown = 0.45;  // melee cadence
    private const double FallSafe = 3.5f;       // fall height that hurts

    [Signal] public delegate void HotbarChangedEventHandler(int index, int itemId);
    [Signal] public delegate void FlyChangedEventHandler(bool flying);
    [Signal] public delegate void BlockBrokenEventHandler(Vector3 pos, int blockId);
    [Signal] public delegate void BlockPlacedEventHandler(Vector3 pos, int blockId);
    [Signal] public delegate void SteppedEventHandler(Vector3 pos, int blockId);
    [Signal] public delegate void SplashedEventHandler(Vector3 pos);
    [Signal] public delegate void SwimStrokeEventHandler(Vector3 pos);
    [Signal] public delegate void LavaEnteredEventHandler(Vector3 pos);
    [Signal] public delegate void MiningTickEventHandler(Vector3 pos, int blockId);
    [Signal] public delegate void ToolBrokeEventHandler();
    [Signal] public delegate void InventoryFullEventHandler();
    [Signal] public delegate void TableUsedEventHandler();
    [Signal] public delegate void ChestUsedEventHandler(Vector3I pos);
    [Signal] public delegate void FurnaceUsedEventHandler(Vector3I pos);
    [Signal] public delegate void SleptEventHandler();
    [Signal] public delegate void TraderUsedEventHandler();
    [Signal] public delegate void NoticeEventHandler(string text);
    [Signal] public delegate void AteEventHandler();
    [Signal] public delegate void CampfireUsedEventHandler(string message);
    [Signal] public delegate void RespawnedEventHandler();
    [Signal] public delegate void LandedEventHandler(Vector3 pos, float impact);

    private static readonly string[] HotbarActions =
    [
        "hotbar_1", "hotbar_2", "hotbar_3", "hotbar_4", "hotbar_5",
        "hotbar_6", "hotbar_7", "hotbar_8", "hotbar_9",
    ];

    public VoxelWorld? World;
    public bool Flying;
    public bool Underwater;
    public bool InWater;
    public bool InLava;
    public bool Swimming;
    public bool Climbing;
    public int Selected;
    public GameMode Mode = GameMode.Creative;
    public Vector3? RespawnPoint;
    public PlayerStats Stats { get; } = new();
    public bool Dead => Stats.Dead;

    public Inventory Inventory { get; } = new();

    public Camera3D Camera = null!;
    public RayCast3D Ray = null!;

    private bool _skipMotion;
    private double _coyote;
    private double _jumpBuffer;
    private MeshInstance3D _outline = null!;
    private Node3D _arm = null!;
    private Sprite3D _heldItemSprite = null!;
    private int _heldItemId = -1;
    private Vector3 _armBase = Vector3.Zero;
    private double _bobPhase;
    private double _punch;
    private double _mineTimer;
    private float _mineProgress;
    private double _mineSoundTimer;
    private Vector3I _mineTarget = new(int.MinValue, 0, 0);
    private BlockId _mineBlock = BlockId.Air;
    private int _crackStage = -1;
    private MeshInstance3D _crack = null!;
    private StandardMaterial3D _crackMat = null!;
    private static readonly System.Lazy<ImageTexture[]> CrackTextureLazy = new(BuildCrackTextures);
    private double _lastJumpTap = -10.0;
    private double _stepAccum;
    private bool _wasInWater;
    private bool _wasInLava;
    private bool _wasSwimming;
    private double _swimStrokeTimer;
    private float _waterViewBlend;
    private float _airFogDensity;
    private Color _airFogColor;
    private Godot.Environment? _underwaterEnvironment;
    private Godot.Environment? _lavaEnvironment;
    private float _lavaViewBlend;
    private float _waterImmersion;
    private float _swimCameraPhase;
    private double _lastSwing = -10.0;
    private double _lastEat = -10.0;
    private double _lastRest = -10.0;
    private float? _fallStartY;
    private Vector3 _push;   // knockback impulse, decays (movement overwrites Velocity each frame)
    private float _camBob;
    private float _landKick;
    private double _lavaTimer;
    private enum FishingState { Idle, Waiting, Biting }
    private FishingState _fishingState;
    private double _fishingTimer;

    public void SetWaterEnvironment(Godot.Environment environment)
    {
        _underwaterEnvironment = (Godot.Environment)environment.Duplicate();
        _airFogDensity = environment.FogDensity;
        _airFogColor = environment.FogLightColor;
        _underwaterEnvironment.FogEnabled = true;
        _underwaterEnvironment.VolumetricFogEnabled = false;
        _lavaEnvironment = (Godot.Environment)environment.Duplicate();
        _lavaEnvironment.FogEnabled = true;
        _lavaEnvironment.VolumetricFogEnabled = false;
    }

    public override void _Ready()
    {
        Camera = GetNode<Camera3D>("Camera3D");
        Ray = GetNode<RayCast3D>("Camera3D/RayCast3D");
        Ray.TargetPosition = new Vector3(0, 0, -Reach);
        Ray.CollisionMask = (uint)(PhysicsLayer.Terrain | PhysicsLayer.Mobs);
        Ray.AddException(this);
        FloorSnapLength = 0.4f;
        // Eye adaptation: brightens caves/night, dims looking at the noon sky.
        // Narrow range on purpose: the night must stay moody, not look like day.
        CameraAttributesPractical attrs = new()
        {
            AutoExposureEnabled = true,
            AutoExposureMinSensitivity = 40.0f,
            AutoExposureMaxSensitivity = 140.0f,
            AutoExposureScale = 0.3f,
            AutoExposureSpeed = 0.8f,
        };
        Camera.Attributes = attrs;
        BuildOutline();
        BuildCrack();
        BuildArm();
        // Mouse capture happens on the first mouse move / click (works regardless
        // of window focus at startup, unlike grabbing in _Ready).
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (InputLocked || Stats.Dead)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                _skipMotion = true;
                return;
            }

            // Capturing recentres the cursor; ignore that teleport and any
            // implausibly large delta so the view never snaps around.
            if (_skipMotion)
            {
                _skipMotion = false;
                return;
            }

            if (motion.Relative.Length() > 400.0f)
            {
                return;
            }

            float sensitivity = Settings.MouseSensitivity;
            float dy = motion.Relative.Y * (Settings.InvertY ? -1.0f : 1.0f);
            RotateY(-motion.Relative.X * sensitivity);
            Vector3 camRot = Camera.Rotation;
            camRot.X = Mathf.Clamp(camRot.X - dy * sensitivity, -1.55f, 1.55f);
            Camera.Rotation = camRot;
        }
        else if (@event is InputEventMouseButton button)
        {
            if (!button.Pressed)
            {
                if (button.IsAction("break_block"))
                {
                    ResetMining();
                    _mineTimer = 0.0;
                }

                return;
            }

            if (Input.MouseMode != Input.MouseModeEnum.Captured)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                _skipMotion = true;
                return;
            }

            if (button.IsActionPressed("break_block"))
            {
                Ray.ForceRaycastUpdate();
                AttackOrStartMining();
                _mineTimer = MineRepeat;
            }
            else if (button.IsActionPressed("place_block"))
            {
                Ray.ForceRaycastUpdate();
                PlaceBlock();
            }
            else if (button.IsActionPressed("pick_block"))
            {
                Ray.ForceRaycastUpdate();
                PickBlock();
            }
            else if (button.ButtonIndex == MouseButton.WheelUp)
            {
                Select(Selected - 1);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown)
            {
                Select(Selected + 1);
            }
        }
        else if (@event is InputEventJoypadButton pad && pad.Pressed)
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            if (pad.IsActionPressed("toggle_fly"))
            {
                SetFlying(!Flying);
            }
            else if (pad.IsActionPressed("break_block"))
            {
                Ray.ForceRaycastUpdate();
                AttackOrStartMining();
                _mineTimer = MineRepeat;
            }
            else if (pad.IsActionPressed("place_block"))
            {
                Ray.ForceRaycastUpdate();
                PlaceBlock();
            }
            else if (pad.IsActionPressed("pick_block"))
            {
                Ray.ForceRaycastUpdate();
                PickBlock();
            }
        }
        else if (@event is InputEventJoypadMotion padMotion && Mathf.Abs(padMotion.AxisValue) > 0.25f)
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.IsActionPressed("toggle_fly"))
            {
                SetFlying(!Flying);
            }
            else
            {
                for (int i = 0; i < HotbarActions.Length; i++)
                {
                    if (key.IsActionPressed(HotbarActions[i]))
                    {
                        Select(i);
                        break;
                    }
                }
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (World == null)
        {
            return;
        }

        float dt = (float)delta;
        bool locked = InputLocked || Stats.Dead;
        if (!locked)
        {
            Vector2 look = Input.GetVector("look_left", "look_right", "look_up", "look_down");
            if (look.LengthSquared() > 0.0001f)
            {
                RotateY(-look.X * 2.4f * dt);
                Vector3 cameraRotation = Camera.Rotation;
                cameraRotation.X = Mathf.Clamp(cameraRotation.X - look.Y * 2.0f * dt, -1.55f, 1.55f);
                Camera.Rotation = cameraRotation;
            }
        }
        if (!locked && Input.IsActionJustPressed("hotbar_prev")) Select(Selected - 1);
        if (!locked && Input.IsActionJustPressed("hotbar_next")) Select(Selected + 1);
        Vector2 inputDir = locked ? Vector2.Zero : Input.GetVector("left", "right", "forward", "back");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        bool sprintHeld = !locked && Input.IsActionPressed("sprint");
        Climbing = !Flying && (IsLadderAt(GlobalPosition + new Vector3(0, 1.0f, 0))
                || IsLadderAt(GlobalPosition + new Vector3(0, 0.2f, 0)));
        bool feetWet = IsWaterAt(GlobalPosition + new Vector3(0, 0.2f, 0));
        bool torsoWet = IsWaterAt(GlobalPosition + new Vector3(0, 0.9f, 0));
        bool headWet = IsWaterAt(Camera.GlobalPosition);
        float immersion = WaterPhysics.Immersion(feetWet, torsoWet, headWet);
        InWater = immersion > 0.0f;
        Underwater = headWet;
        _waterImmersion = immersion;
        InLava = IsLavaAt(GlobalPosition + new Vector3(0, 0.2f, 0))
                || IsLavaAt(GlobalPosition + new Vector3(0, 0.9f, 0))
                || IsLavaAt(Camera.GlobalPosition);
        Swimming = !Flying && !Climbing && !InLava && WaterPhysics.ShouldSwim(immersion);
        bool sprinting = sprintHeld && !Swimming && !InLava && !Climbing;
        float speed = sprinting ? SprintSpeed : WalkSpeed;
        Stats.Update(delta, sprinting, (sprinting || inputDir != Vector2.Zero) && !Flying, Mode);

        // Double-tap Space toggles fly (F also works); creative only.
        if (!locked && Mode == GameMode.Creative && Input.IsActionJustPressed("jump"))
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (now - _lastJumpTap < DoubleTapTime)
            {
                SetFlying(!Flying);
                _lastJumpTap = -10.0;
            }
            else
            {
                _lastJumpTap = now;
            }
        }

        Vector3 velocity = Velocity;
        if (Flying)
        {
            speed = FlySpeed;
            float up = 0.0f;
            if (!locked && Input.IsActionPressed("jump"))
            {
                up += 1.0f;
            }

            if (sprinting)
            {
                up -= 1.0f;
            }

            velocity = direction * speed;
            velocity.Y = up * speed;
        }
        else
        {
            if (Climbing)
            {
                speed = 3.2f;
                float targetY = 0.0f;
                bool vertical = false;
                if (!locked && (Input.IsActionPressed("jump") || Input.IsActionPressed("forward")))
                {
                    targetY = 3.6f;
                    vertical = true;
                }
                else if (!locked && Input.IsActionPressed("back"))
                {
                    targetY = -3.2f;
                    vertical = true;
                }

                velocity.Y = Mathf.MoveToward(velocity.Y, targetY, 14.0f * dt);
                // Keep horizontal input from sliding the player off the ladder
                // while climbing; strafing still works when no vertical key is held.
                velocity.X = vertical ? 0.0f : direction.X * speed;
                velocity.Z = vertical ? 0.0f : direction.Z * speed;
                _coyote = 0.0;
                _jumpBuffer = 0.0;
            }
            else if (Swimming)
            {
                speed = WaterPhysics.HorizontalSpeed;
                velocity = WaterPhysics.Step(velocity, direction, immersion,
                        !locked && Input.IsActionPressed("jump"), sprintHeld, dt,
                        IsOnCeiling(), IsOnFloor());
                _coyote = 0.0;
                _jumpBuffer = 0.0;
            }
            else if (InLava)
            {
                speed = 1.55f;
                float targetY = !locked && Input.IsActionPressed("jump") ? 2.0f : -1.1f;
                velocity.X = Mathf.MoveToward(velocity.X, direction.X * speed, 5.0f * dt);
                velocity.Z = Mathf.MoveToward(velocity.Z, direction.Z * speed, 5.0f * dt);
                velocity.Y = Mathf.MoveToward(velocity.Y, targetY, 4.0f * dt);
                _coyote = 0.0;
                _jumpBuffer = 0.0;
            }
            else
            {
                if (_wasSwimming) velocity.Y = Mathf.Clamp(velocity.Y, -2.0f, 2.0f);
                if (InWater) speed *= 0.78f;

                if (IsOnFloor())
                {
                    _coyote = CoyoteTime;
                }
                else
                {
                    _coyote = System.Math.Max(_coyote - delta, 0.0);
                    velocity.Y -= (float)(Gravity * delta * (InWater ? 0.35 : 1.0));
                }

                if (!locked && Input.IsActionJustPressed("jump"))
                {
                    _jumpBuffer = JumpBuffer;
                }
                else
                {
                    _jumpBuffer = System.Math.Max(_jumpBuffer - delta, 0.0);
                }

                if (_jumpBuffer > 0.0 && _coyote > 0.0)
                {
                    velocity.Y = JumpVelocity;
                    _jumpBuffer = 0.0;
                    _coyote = 0.0;
                }

                velocity.X = direction.X * speed;
                velocity.Z = direction.Z * speed;
            }
        }

        velocity += _push;
        _push = new Vector3(
                Mathf.MoveToward(_push.X, 0.0f, dt * 10.0f),
                Mathf.MoveToward(_push.Y, 0.0f, dt * 16.0f),
                Mathf.MoveToward(_push.Z, 0.0f, dt * 10.0f));
        Velocity = velocity;
        MoveAndSlide();
        if (Swimming && (inputDir != Vector2.Zero || Mathf.Abs(Velocity.Y) > 1.0f))
        {
            _swimStrokeTimer -= delta;
            if (_swimStrokeTimer <= 0.0)
            {
                _swimStrokeTimer = 0.52;
                EmitSignal(SignalName.SwimStroke, Camera.GlobalPosition);
            }
        }
        else _swimStrokeTimer = 0.0;
        _wasSwimming = Swimming;
        // Safety net: never let the player fall out of the world (trimesh slips).
        if (GlobalPosition.Y < -8.0f)
        {
            GlobalPosition = World.FindSpawn() + new Vector3(0, 1.0f, 0);
            Velocity = Vector3.Zero;
        }

        Ray.ForceRaycastUpdate();
        UpdateOutline();
        UpdateArm(dt);
        HoldMine(dt);
        UpdateSteps(dt);
        TrackFall();
        UpdateCameraFeel(dt, sprinting, direction);

        float baseFov = Settings.Fov;
        float targetFov = sprinting && !Flying && !Swimming && direction != Vector3.Zero ? baseFov + 5.0f : baseFov;
        Camera.Fov = Mathf.Lerp(Camera.Fov, targetFov, 8.0f * dt);
    }

    private bool IsWaterAt(Vector3 p)
    {
        if (World == null) return false;
        int x = Mathf.FloorToInt(p.X);
        int y = Mathf.FloorToInt(p.Y);
        int z = Mathf.FloorToInt(p.Z);
        BlockId id = World.GetBlock(x, y, z);
        if (!BlockDb.IsWater(id)) return false;
        bool fallingColumn = BlockDb.IsWater(World.GetBlock(x, y + 1, z));
        float surface = fallingColumn ? 1.0f : BlockDb.WaterHeight(id);
        return p.Y - y < surface;
    }

    private bool IsLavaAt(Vector3 p) => World != null
            && World.GetBlock(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z)) == BlockId.Lava;

    private void UpdateWaterCamera(double delta)
    {
        if (_underwaterEnvironment == null || _lavaEnvironment == null || Camera == null) return;
        float dt = (float)System.Math.Clamp(delta, 0.0, 0.1);
        float waterTarget = Underwater ? Mathf.Lerp(0.72f, 1.0f, _waterImmersion) : 0.0f;
        _waterViewBlend = Mathf.MoveToward(_waterViewBlend, waterTarget, dt * 2.2f);
        _lavaViewBlend = Mathf.MoveToward(_lavaViewBlend, InLava ? 1.0f : 0.0f, dt * 3.0f);

        float waterDensity = Mathf.Lerp(0.022f, 0.052f, _waterImmersion);
        _underwaterEnvironment.FogDensity = Mathf.Lerp(_airFogDensity, waterDensity, _waterViewBlend);
        _underwaterEnvironment.FogLightColor = _airFogColor.Lerp(
                new Color(0.018f, 0.105f, 0.205f), _waterViewBlend);
        _lavaEnvironment.FogDensity = Mathf.Lerp(_airFogDensity, 0.085f, _lavaViewBlend);
        _lavaEnvironment.FogLightColor = _airFogColor.Lerp(
                new Color(0.34f, 0.075f, 0.018f), _lavaViewBlend);

        Camera.Environment = _lavaViewBlend > 0.001f ? _lavaEnvironment
                : _waterViewBlend > 0.001f ? _underwaterEnvironment : null;
    }

    private bool IsLadderAt(Vector3 p) =>
        World != null && World.GetBlock(
                Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z)) == BlockId.Ladder;

    /// <summary>Head bob while walking and a landing dip after drops.</summary>
    private void UpdateCameraFeel(float dt, bool sprinting, Vector3 direction)
    {
        bool walking = IsOnFloor() && !Flying && !Swimming && !Climbing && direction != Vector3.Zero && !Stats.Dead;
        if (Swimming)
        {
            float sway = Settings.HeadBob ? Mathf.Sin(_swimCameraPhase * 0.55f) * 0.018f : 0.0f;
            _landKick = Mathf.MoveToward(_landKick, 0.0f, dt * 5.0f);
            Camera.Position = new Vector3(Mathf.Sin(_swimCameraPhase * 0.3f) * 0.012f,
                    1.62f + sway - _landKick, 0.0f);
            return;
        }

        _camBob = walking
                ? _camBob + dt * (sprinting ? 11.5f : 8.5f)
                : Mathf.MoveToward(_camBob, 0.0f, dt * 7.0f);
        float amp = Settings.HeadBob ? (sprinting ? 0.05f : 0.032f) : 0.0f;
        if (!Settings.HeadBob)
        {
            _camBob = 0.0f;
        }

        float bobY = Mathf.Sin(_camBob * 2.0f) * amp;
        float bobX = Mathf.Cos(_camBob) * amp * 0.7f;
        _landKick = Mathf.MoveToward(_landKick, 0.0f, dt * 5.0f);
        Camera.Position = new Vector3(bobX, 1.62f + bobY - _landKick, 0);
    }

    public override void _Process(double delta)
    {
        if (World == null)
        {
            return;
        }

        bool under = IsWaterAt(Camera.GlobalPosition);
        Underwater = under;
        InWater = IsWaterAt(GlobalPosition + new Vector3(0, 0.2f, 0))
                || IsWaterAt(GlobalPosition + new Vector3(0, 0.9f, 0)) || under;
        if (InWater && !_wasInWater) EmitSignal(SignalName.Splashed, Camera.GlobalPosition);
        _wasInWater = InWater;
        Stats.UpdateBreath(delta, under, Mode);
        UpdateWaterCamera(delta);

        bool inLava = IsLavaAt(GlobalPosition + new Vector3(0, 0.2f, 0))
                || IsLavaAt(GlobalPosition + new Vector3(0, 0.9f, 0))
                || IsLavaAt(Camera.GlobalPosition);
        if (inLava && !_wasInLava) EmitSignal(SignalName.LavaEntered, GlobalPosition);
        if (inLava)
        {
            _lavaTimer -= delta;
            if (_lavaTimer <= 0.0)
            {
                _lavaTimer = 0.5;
                ApplyDamage(new DamageInfo(4.0f, DamageKind.Fire));
            }
        }
        else _lavaTimer = 0.0;
        _wasInLava = inLava;
        InLava = inLava;

        // Fishing state: bite window, then escape.
        if (_fishingState != FishingState.Idle)
        {
            if (Inventory.Hotbar[Selected].Id != ItemId.FishingRod)
            {
                _fishingState = FishingState.Idle;
            }
            else
            {
                _fishingTimer -= delta;
                if (_fishingState == FishingState.Waiting && _fishingTimer <= 0.0)
                {
                    _fishingState = FishingState.Biting;
                    _fishingTimer = 1.6;
                    EmitSignal(SignalName.Notice, "A fish bites — right-click to reel");
                }
                else if (_fishingState == FishingState.Biting && _fishingTimer <= 0.0)
                {
                    _fishingState = FishingState.Idle;
                    EmitSignal(SignalName.Notice, "The fish got away");
                }
            }
        }

        // Sky-island worlds have no floor: falling out of the world hurts fast.
        if (GlobalPosition.Y < -8.0f)
        {
            ApplyDamage(new DamageInfo(4.0f, DamageKind.Fall));
        }
    }

    // -----------------------------------------------------------------------
    // Survival: damage, death, respawn, mode
    // -----------------------------------------------------------------------

    /// <summary>All incoming hurt routes here; creative and invulnerability refuse it.</summary>
    public bool ApplyDamage(in DamageInfo info)
    {
        float amount = info.Amount;
        // Armor protects against physical/environmental hits, not hunger or drowning.
        if (info.Kind is not (DamageKind.Drown or DamageKind.Starve))
        {
            amount *= 1.0f - ArmorReduction;
        }

        bool applied = Stats.ApplyDamage(amount, Mode);
        if (applied)
        {
            DamageArmor();
        }

        if (applied && info.Knockback > 0.0f)
        {
            Vector3 away = GlobalPosition - info.Source;
            away.Y = 0.0f;
            if (away.LengthSquared() > 0.001f)
            {
                away = away.Normalized();
            }

            _push += away * info.Knockback + new Vector3(0, info.Lift, 0);
        }

        return applied;
    }

    /// <summary>Total defense points from equipped armor (0..20).</summary>
    public int ArmorPoints
    {
        get
        {
            int points = 0;
            foreach (ItemStack stack in Inventory.Armor)
            {
                if (ItemDb.ArmorOf(stack.Id) is { } armor)
                {
                    points += armor.Points;
                }
            }

            return points;
        }
    }

    private float ArmorReduction => Mathf.Min(ArmorPoints * 0.04f, 0.80f);

    /// <summary>Every landed hit wears each equipped piece by one point.</summary>
    private void DamageArmor()
    {
        bool broke = false;
        for (int i = 0; i < Inventory.Armor.Length; i++)
        {
            ItemStack stack = Inventory.Armor[i];
            if (stack.IsEmpty)
            {
                continue;
            }

            int durability = stack.Durability - 1;
            if (durability <= 0)
            {
                Inventory.Armor[i] = ItemStack.Empty;
                broke = true;
            }
            else
            {
                Inventory.Armor[i] = stack with { Durability = durability };
            }
        }

        Inventory.RaiseChanged();
        if (broke)
        {
            EmitSignal(SignalName.Notice, "A piece of armor broke");
        }
    }

    /// <summary>Softcore respawn (D5): full bars at spawn/campfire, inventory kept.</summary>
    public void Respawn()
    {
        if (!Stats.Dead)
        {
            return;
        }

        Stats.ResetForRespawn();
        Vector3 target = RespawnPoint ?? (World?.FindSpawn() ?? GlobalPosition) + new Vector3(0, 1.0f, 0);
        GlobalPosition = target;
        Velocity = Vector3.Zero;
        _fallStartY = null;
        EmitSignal(SignalName.Respawned);
    }

    public void SetMode(GameMode mode)
    {
        Mode = mode;
        if (Mode != GameMode.Creative)
        {
            SetFlying(false);
        }

        EmitSignal(SignalName.Notice, mode == GameMode.Survival
                ? "survival mode — be careful out there"
                : "creative mode — fly free");
    }

    /// <summary>Restore persisted player state; null mode keeps the world default.</summary>
    public void LoadFromSave(string? mode, float health, float hunger, Vector3? respawn)
    {
        if (mode == "survival")
        {
            Mode = GameMode.Survival;
        }
        else if (mode == "creative")
        {
            Mode = GameMode.Creative;
        }

        Stats.Load(health, hunger);
        RespawnPoint = respawn;
    }

    public string SaveMode => Mode == GameMode.Survival ? "survival" : "creative";

    /// <summary>
    /// Fall tracking: landing feel (bob kick + dust) in every mode, damage
    /// only in survival.
    /// </summary>
    private void TrackFall()
    {
        if (Flying || Stats.Dead)
        {
            _fallStartY = null;
            return;
        }

        if (!IsOnFloor())
        {
            if (Underwater)
            {
                _fallStartY = null;   // water breaks the fall
            }
            else if (Climbing)
            {
                _fallStartY = null;   // ladders break the fall
            }
            else if (_fallStartY is not { } start || GlobalPosition.Y > start)
            {
                _fallStartY = GlobalPosition.Y;
            }

            return;
        }

        if (_fallStartY is { } from)
        {
            float fallen = from - GlobalPosition.Y;
            _fallStartY = null;
            if (fallen <= 2.0f)
            {
                return;
            }

            _landKick = Mathf.Min(fallen * 0.02f, 0.14f);
            EmitSignal(SignalName.Landed, GlobalPosition, fallen);
            if (Mode == GameMode.Survival && fallen > FallSafe && World != null)
            {
                BlockId below = World.GetBlock(
                        Mathf.FloorToInt(GlobalPosition.X),
                        Mathf.FloorToInt(GlobalPosition.Y - 0.5f),
                        Mathf.FloorToInt(GlobalPosition.Z));
                if (!BlockDb.IsWater(below))
                {
                    Stats.ApplyDamage(Mathf.Floor(fallen - 3.0f), Mode);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Block interaction
    // -----------------------------------------------------------------------

    /// <summary>LMB press: mine the closest voxel target or swing at a mob.</summary>
    public void AttackOrStartMining()
    {
        if (World == null)
        {
            ResetMining();
            return;
        }

        if (!TryGetTargetBlock(out Vector3I bp, out BlockId id))
        {
            ResetMining();
            if (Ray.IsColliding() && Ray.GetCollider() is IDamageable target)
            {
                Swing(target);
            }

            return;
        }

        BlockDb.BlockDef def = BlockDb.DefOf(id);
        if (Mode == GameMode.Creative)
        {
            FinishBreak(bp, id, def, null);
            return;
        }

        _mineTarget = bp;
        _mineBlock = id;
        _mineProgress = 0.0f;
        _mineSoundTimer = 0.0;
        _crackStage = -1;
        StartPunch();
    }

    /// <summary>One melee swing: tool tier picks the damage, swing cadence is capped.</summary>
    private void Swing(IDamageable target)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastSwing < SwingCooldown)
        {
            return;
        }

        _lastSwing = now;
        StartPunch();
        float damage = 1.0f + (ItemDb.ToolOf(Inventory.Hotbar[Selected].Id)?.Tier ?? 0);
        target.ApplyDamage(new DamageInfo(damage, DamageKind.Melee, GlobalPosition, 3.0f, 1.5f));
    }

    public Vector3I TargetBlock()
    {
        Vector3 p = Ray.GetCollisionPoint() - Ray.GetCollisionNormal() * 0.01f;
        return new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
    }

    private bool TryGetTargetBlock(out Vector3I blockPosition, out BlockId blockId)
    {
        blockPosition = new Vector3I(int.MinValue, 0, 0);
        blockId = BlockId.Air;
        if (World == null)
        {
            return false;
        }

        Vector3 origin = Camera.GlobalPosition;
        Vector3 direction = -Camera.GlobalTransform.Basis.Z;
        if (direction.LengthSquared() < 0.0001f)
        {
            return false;
        }

        direction = direction.Normalized();
        float maxDistance = Reach + 0.05f;
        if (Ray.IsColliding())
        {
            float colliderDistance = origin.DistanceTo(Ray.GetCollisionPoint());
            maxDistance = Mathf.Min(
                    maxDistance,
                    Ray.GetCollider() is IDamageable
                            ? colliderDistance - 0.001f
                            : colliderDistance + 0.02f);
        }

        return TryRaycastVoxels(origin + direction * 0.0001f, direction,
                maxDistance, out blockPosition, out blockId);
    }

    /// <summary>
    /// Amanatides-Woo voxel traversal. Solid cubes are selected by cell entry;
    /// cross plants are selected only when the ray intersects their visible
    /// diagonal quads, leaving them non-colliding for player movement.
    /// </summary>
    private bool TryRaycastVoxels(
            Vector3 origin, Vector3 direction, float maxDistance,
            out Vector3I blockPosition, out BlockId blockId)
    {
        blockPosition = new Vector3I(int.MinValue, 0, 0);
        blockId = BlockId.Air;
        if (maxDistance < 0.0f)
        {
            return false;
        }

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);
        int stepX = direction.X > 0.0f ? 1 : direction.X < 0.0f ? -1 : 0;
        int stepY = direction.Y > 0.0f ? 1 : direction.Y < 0.0f ? -1 : 0;
        int stepZ = direction.Z > 0.0f ? 1 : direction.Z < 0.0f ? -1 : 0;

        float deltaX = stepX == 0 ? float.PositiveInfinity : Mathf.Abs(1.0f / direction.X);
        float deltaY = stepY == 0 ? float.PositiveInfinity : Mathf.Abs(1.0f / direction.Y);
        float deltaZ = stepZ == 0 ? float.PositiveInfinity : Mathf.Abs(1.0f / direction.Z);
        float nextX = stepX > 0 ? (x + 1.0f - origin.X) / direction.X
                : stepX < 0 ? (origin.X - x) / -direction.X : float.PositiveInfinity;
        float nextY = stepY > 0 ? (y + 1.0f - origin.Y) / direction.Y
                : stepY < 0 ? (origin.Y - y) / -direction.Y : float.PositiveInfinity;
        float nextZ = stepZ > 0 ? (z + 1.0f - origin.Z) / direction.Z
                : stepZ < 0 ? (origin.Z - z) / -direction.Z : float.PositiveInfinity;

        float entryDistance = 0.0f;
        while (entryDistance <= maxDistance)
        {
            float exitDistance = Mathf.Min(nextX, Mathf.Min(nextY, nextZ));
            BlockId current = World!.GetBlock(x, y, z);
            if (current != BlockId.Air && !BlockDb.IsWater(current) && current != BlockId.Lava)
            {
                if (!BlockDb.IsCross(current)
                        || BlockDb.RayIntersectsCross(
                                origin, direction, new Vector3I(x, y, z),
                                entryDistance, Mathf.Min(exitDistance, maxDistance), out _))
                {
                    blockPosition = new Vector3I(x, y, z);
                    blockId = current;
                    return true;
                }
            }

            if (!float.IsFinite(exitDistance) || exitDistance > maxDistance)
            {
                break;
            }

            const float tieEpsilon = 0.00001f;
            bool advanceX = nextX <= exitDistance + tieEpsilon;
            bool advanceY = nextY <= exitDistance + tieEpsilon;
            bool advanceZ = nextZ <= exitDistance + tieEpsilon;
            if (advanceX)
            {
                x += stepX;
                nextX += deltaX;
            }
            if (advanceY)
            {
                y += stepY;
                nextY += deltaY;
            }
            if (advanceZ)
            {
                z += stepZ;
                nextZ += deltaZ;
            }

            entryDistance = exitDistance;
        }

        return false;
    }

    /// <summary>True when the ray currently rests on a crafting table in reach.</summary>
    public bool TableInReach()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        Vector3I bp = TargetBlock();
        return World.GetBlock(bp.X, bp.Y, bp.Z) == BlockId.CraftingTable;
    }

    /// <summary>True when the ray currently rests on a furnace in reach.</summary>
    public bool FurnaceInReach()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        Vector3I bp = TargetBlock();
        return World.GetBlock(bp.X, bp.Y, bp.Z) == BlockId.Furnace;
    }

    public bool BedInReach()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        Vector3I bp = TargetBlock();
        return World.GetBlock(bp.X, bp.Y, bp.Z) == BlockId.Bed;
    }

    /// <summary>Finds tillable soil under the crosshair.</summary>
    public bool HoeInReach(out Vector3I soil)
    {
        soil = default;
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        soil = TargetBlock();
        BlockId id = World.GetBlock(soil.X, soil.Y, soil.Z);
        return id is BlockId.Grass or BlockId.Dirt or BlockId.SnowGrass;
    }

    /// <summary>Finds farmland with air above it for planting.</summary>
    public bool SeedsInReach(out Vector3I plantAt)
    {
        plantAt = default;
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        Vector3I soil = TargetBlock();
        if (World.GetBlock(soil.X, soil.Y, soil.Z) != BlockId.Farmland)
        {
            return false;
        }

        int y = soil.Y + 1;
        if (World.GetBlock(soil.X, y, soil.Z) != BlockId.Air)
        {
            return false;
        }

        plantAt = new Vector3I(soil.X, y, soil.Z);
        return true;
    }

    private void UseHoe(Vector3I soil)
    {
        if (World == null || !World.SetBlock(soil.X, soil.Y, soil.Z, BlockId.Farmland))
        {
            return;
        }

        StartPunch();
        EmitSignal(SignalName.BlockPlaced,
                new Vector3(soil.X + 0.5f, soil.Y + 0.5f, soil.Z + 0.5f), (int)BlockId.Farmland);
        DamageTool();
        Inventory.RaiseChanged();
    }

    private void PlantSeeds(Vector3I plantAt)
    {
        PlantSeedsAt(plantAt);
    }

    /// <summary>Plant one seed on farmland; public for the dev/test path.</summary>
    public bool PlantSeedsAt(Vector3I plantAt)
    {
        if (World == null
                || World.GetBlock(plantAt.X, plantAt.Y, plantAt.Z) != BlockId.Air
                || World.GetBlock(plantAt.X, plantAt.Y - 1, plantAt.Z) != BlockId.Farmland
                || Inventory.CountOf(ItemId.Seeds) <= 0)
        {
            return false;
        }

        if (!World.SetBlock(plantAt.X, plantAt.Y, plantAt.Z, BlockId.Wheat0))
        {
            return false;
        }

        Inventory.Consume(ItemId.Seeds, 1);
        StartPunch();
        EmitSignal(SignalName.BlockPlaced,
                new Vector3(plantAt.X + 0.5f, plantAt.Y + 0.5f, plantAt.Z + 0.5f), (int)BlockId.Wheat0);
        Inventory.RaiseChanged();
        return true;
    }

    /// <summary>Cast or reel a fishing rod; the bite window is intentionally short.</summary>
    private void UseFishingRod()
    {
        if (_fishingState == FishingState.Biting)
        {
            CatchFish();
            return;
        }

        if (_fishingState == FishingState.Waiting)
        {
            EmitSignal(SignalName.Notice, "Wait for a bite…");
            return;
        }

        if (!WaterAhead())
        {
            EmitSignal(SignalName.Notice, "Cast toward water");
            return;
        }

        _fishingState = FishingState.Waiting;
        _fishingTimer = GD.RandRange(2.5, 5.5);
        StartPunch();
        EmitSignal(SignalName.Notice, "Cast — wait for a bite");
    }

    private bool WaterAhead()
    {
        if (World == null)
        {
            return false;
        }

        Vector3 forward = -Camera.GlobalTransform.Basis.Z;
        for (int d = 1; d <= 6; d++)
        {
            Vector3 p = Camera.GlobalPosition + forward * d;
            if (World.GetBlock(
                    Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z)) == BlockId.Water)
            {
                return true;
            }
        }

        return false;
    }

    private void CatchFish()
    {
        _fishingState = FishingState.Idle;
        StartPunch();
        if (Inventory.Add(ItemId.RawFish, 1) > 0)
        {
            EmitSignal(SignalName.InventoryFull);
            return;
        }

        Inventory.RaiseChanged();
        EmitSignal(SignalName.Notice, "Caught a fish");
    }

    private void UseBed()
    {
        SleepAt(TargetBlock());
    }

    /// <summary>Sleep at a bed if it is night; public for the dev/test path.</summary>
    public bool SleepAt(Vector3I bedPos)
    {
        if (World == null || World.GetBlock(bedPos.X, bedPos.Y, bedPos.Z) != BlockId.Bed)
        {
            return false;
        }

        if (!DayNight.IsNight(World.TimeOfDay))
        {
            EmitSignal(SignalName.Notice, "You can only sleep at night");
            return false;
        }

        RespawnPoint = new Vector3(bedPos.X + 0.5f, bedPos.Y + 1.0f, bedPos.Z + 0.5f);
        World.TimeOfDay = 0.26;
        World.MarkDirty();
        StartPunch();
        EmitSignal(SignalName.Slept);
        EmitSignal(SignalName.Notice, "Slept until dawn — respawn set");
        return true;
    }

    /// <summary>True when the ray currently rests on a chest in reach.</summary>
    public bool ChestInReach()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        Vector3I bp = TargetBlock();
        return World.GetBlock(bp.X, bp.Y, bp.Z) == BlockId.Chest;
    }

    /// <summary>True when the ray rests on a campfire's log block.</summary>
    public bool CampfireInReach()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return false;
        }

        Vector3I bp = TargetBlock();
        return World.IsCampfireLog(bp.X, bp.Y, bp.Z);
    }

    /// <summary>Campfire use: cook the held raw food and/or rest (respawn + heal).</summary>
    private void UseCampfire(ItemStack stack)
    {
        if (World == null)
        {
            return;
        }

        bool cooked = false;
        if (ItemDb.CooksTo(stack.Id) is { } cookedId && Inventory.Consume(stack.Id, 1))
        {
            Inventory.Add(cookedId, 1);
            cooked = true;
        }

        bool rested = false;
        if (Time.GetTicksMsec() / 1000.0 - _lastRest >= 3.0)
        {
            _lastRest = Time.GetTicksMsec() / 1000.0;
            rested = true;
            Vector3I bp = TargetBlock();
            RespawnPoint = new Vector3(bp.X + 0.5f, bp.Y + 1.0f, bp.Z + 0.5f);
            Stats.Heal(8.0f);
            Stats.AddHunger(3.0f);
        }

        if (!cooked && !rested)
        {
            return;
        }

        StartPunch();
        EmitSignal(SignalName.CampfireUsed, (cooked, rested) switch
        {
            (true, true) => "Cooked 1 · rested — respawn set, healed",
            (true, false) => "Cooked 1",
            _ => "Rested — respawn point set, healed",
        });
    }

    /// <summary>Eat one food item from the held stack.</summary>
    private void Eat(ItemStack stack)
    {
        if (ItemDb.FoodOf(stack.Id) is not { } food)
        {
            return;
        }

        if (Stats.Hunger >= PlayerStats.MaxHunger)
        {
            EmitSignal(SignalName.Notice, "Not hungry");
            return;
        }

        if (Time.GetTicksMsec() / 1000.0 - _lastEat < 0.7)
        {
            return;
        }

        _lastEat = Time.GetTicksMsec() / 1000.0;
        Inventory.Consume(stack.Id, 1);
        Stats.AddHunger(food.Hunger);
        Stats.Heal(food.Heal);
        EmitSignal(SignalName.Ate);
        StartPunch();
    }

    private void FinishBreak(Vector3I bp, BlockId id, BlockDb.BlockDef def, ToolInfo? tool)
    {
        _mineProgress = 0.0f;
        _mineTarget = new Vector3I(int.MinValue, 0, 0);
        _mineBlock = BlockId.Air;
        _crackStage = -1;
        if (_crack != null)
        {
            _crack.Visible = false;
        }

        if (World == null)
        {
            return;
        }

        if (Mode != GameMode.Creative && id == BlockId.Chest
                && World.TryGetContainer(bp, out Container? chest)
                && chest is { IsEmpty: false })
        {
            EmitSignal(SignalName.Notice, "Empty the chest first");
            return;
        }

        if (Mode != GameMode.Creative && id == BlockId.Furnace
                && World.TryGetFurnace(bp, out FurnaceData? furnace)
                && furnace is { IsEmpty: false })
        {
            EmitSignal(SignalName.Notice, "Empty the furnace first");
            return;
        }

        if (!World.SetBlock(bp.X, bp.Y, bp.Z, BlockId.Air))
        {
            return;
        }

        if (id == BlockId.Chest)
        {
            World.RemoveContainer(bp);
        }
        else if (id == BlockId.Furnace)
        {
            World.RemoveFurnace(bp);
        }

        StartPunch();
        Vector3 center = new(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
        EmitSignal(SignalName.BlockBroken, center, (int)id);

        // Creative mode is instant and intentionally bypasses survival drops and wear.
        if (Mode != GameMode.Creative)
        {
            if (id == BlockId.Wheat2)
            {
                GiveDrop(ItemId.Wheat, 1 + (int)(GD.Randf() * 3.0f));
                GiveDrop(ItemId.Seeds, 1 + (int)(GD.Randf() * 2.0f));
            }
            else if (id is BlockId.Wheat0 or BlockId.Wheat1)
            {
                GiveDrop(ItemId.Seeds, 1);
            }
            else if (id == BlockId.TallGrass && GD.Randf() < 0.15f)
            {
                GiveDrop(ItemId.Seeds, 1);
            }

            if (BlockDb.CanHarvest(def, tool) && def.Drops)
            {
                ItemId drop = def.Drop != ItemId.None ? def.Drop : ItemDb.BlockItem(id);
                if (drop != ItemId.None && def.DropCount > 0)
                {
                    int leftover = Inventory.Add(drop, def.DropCount);
                    if (leftover > 0)
                    {
                        EmitSignal(SignalName.InventoryFull);
                    }
                }
            }

            // Apples are an oak-leaf bonus drop; birch foliage never produces them.
            if (id == BlockId.Leaves && GD.Randf() < 0.06f)
            {
                GiveDrop(ItemId.Apple, 1);
            }

            if (tool is not null)
            {
                DamageTool();
            }
        }
    }

    private void GiveDrop(ItemId id, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (Inventory.Add(id, count) > 0)
        {
            EmitSignal(SignalName.InventoryFull);
        }
    }

    private void DamageTool()
    {
        if (Inventory.DamageTool(Selected))
        {
            EmitSignal(SignalName.ToolBroke);
        }
    }

    public void PlaceBlock()
    {
        if (World == null)
        {
            return;
        }

        StartPunch();
        // Mobs first: right-click is "use" on a creature (tame a sheep).
        if (Ray.IsColliding() && Ray.GetCollider() is Mob mob)
        {
            if (mob.Interact(this))
            {
                StartPunch();
            }

            return;
        }

        if (!Ray.IsColliding())
        {
            // Aiming at nothing: food still works (eat on the go).
            ItemStack held = Inventory.Hotbar[Selected];
            if (ItemDb.FoodOf(held.Id) != null)
            {
                Eat(held);
            }

            return;
        }

        ItemStack stack = Inventory.Hotbar[Selected];

        // Campfires cook what you hold raw and offer a rest (respawn + heal).
        if (CampfireInReach())
        {
            UseCampfire(stack);
            return;
        }

        if (ChestInReach())
        {
            EmitSignal(SignalName.ChestUsed, TargetBlock());
            return;
        }

        if (FurnaceInReach())
        {
            EmitSignal(SignalName.FurnaceUsed, TargetBlock());
            return;
        }

        if (BedInReach())
        {
            UseBed();
            return;
        }

        if (stack.Id == ItemId.FishingRod)
        {
            UseFishingRod();
            return;
        }

        if (ItemDb.ToolOf(stack.Id) is { Kind: ToolKind.Hoe } && HoeInReach(out Vector3I soil))
        {
            UseHoe(soil);
            return;
        }

        if (stack.Id == ItemId.Seeds && SeedsInReach(out Vector3I plantAt))
        {
            PlantSeeds(plantAt);
            return;
        }

        if (ItemDb.BlockFor(stack.Id) is not { } place)
        {
            // Non-block items are "use": tables open their UI, food is eaten.
            if (TableInReach())
            {
                EmitSignal(SignalName.TableUsed);
                return;
            }

            if (ItemDb.FoodOf(stack.Id) != null)
            {
                Eat(stack);
            }

            return;
        }

        Vector3 p = Ray.GetCollisionPoint() + Ray.GetCollisionNormal() * 0.5f;
        int bx = Mathf.FloorToInt(p.X);
        int by = Mathf.FloorToInt(p.Y);
        int bz = Mathf.FloorToInt(p.Z);
        BlockId existing = World.GetBlock(bx, by, bz);
        if (existing != BlockId.Air && !BlockDb.IsWater(existing))
        {
            return;
        }

        if (BlockHitsPlayer(bx, by, bz))
        {
            return;
        }

        if (World.SetBlock(bx, by, bz, place))
        {
            if (place == BlockId.Chest)
            {
                World.GetContainer(new Vector3I(bx, by, bz));
            }
            else if (place == BlockId.Furnace)
            {
                World.GetFurnace(new Vector3I(bx, by, bz));
            }

            StartPunch();
            EmitSignal(SignalName.BlockPlaced, new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f), (int)place);
            Inventory.Hotbar[Selected] = stack.Count <= 1 ? ItemStack.Empty : stack with { Count = stack.Count - 1 };
            Inventory.RaiseChanged();
        }
    }

    public void PickBlock()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob)
        {
            return;
        }

        Vector3I bp = TargetBlock();
        BlockId id = World.GetBlock(bp.X, bp.Y, bp.Z);
        if (id == BlockId.Air || BlockDb.IsWater(id) || id == BlockId.Bedrock)
        {
            return;
        }

        ItemId item = ItemDb.BlockItem(id);
        if (item == ItemId.None)
        {
            return;
        }

        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            if (Inventory.Hotbar[i].Id == item)
            {
                Select(i);
                return;
            }
        }

        for (int i = 0; i < Inventory.BackpackSize; i++)
        {
            if (Inventory.Backpack[i].Id == item)
            {
                (Inventory.Hotbar[Selected], Inventory.Backpack[i]) =
                        (Inventory.Backpack[i], Inventory.Hotbar[Selected]);
                Inventory.RaiseChanged();
                return;
            }
        }
    }

    private bool BlockHitsPlayer(int bx, int by, int bz)
    {
        Vector3 lo = GlobalPosition + new Vector3(-0.32f, 0.0f, -0.32f);
        Vector3 hi = GlobalPosition + new Vector3(0.32f, 1.8f, 0.32f);
        return bx + 1 > lo.X && bx < hi.X
                && by + 1 > lo.Y && by < hi.Y
                && bz + 1 > lo.Z && bz < hi.Z;
    }

    public void Select(int i)
    {
        int next = Mathf.Wrap(i, 0, Inventory.HotbarSize);
        if (next != Selected)
            StartPunch();
        Selected = next;
        EmitSignal(SignalName.HotbarChanged, Selected, (int)Inventory.Hotbar[Selected].Id);
    }

    private void SetFlying(bool value)
    {
        if (value && Mode != GameMode.Creative)
        {
            EmitSignal(SignalName.Notice, "flight is creative-only  (/gamemode creative)");
            return;
        }

        Flying = value;
        if (Flying)
        {
            Velocity = Vector3.Zero;
        }

        EmitSignal(SignalName.FlyChanged, Flying);
    }

    private void HoldMine(float delta)
    {
        if (InputLocked || Stats.Dead || !Input.IsActionPressed("break_block") || World == null)
        {
            ResetMining();
            _mineTimer = 0.0;
            return;
        }

        if (!TryGetTargetBlock(out Vector3I bp, out BlockId id))
        {
            ResetMining();
            if (Ray.IsColliding() && Ray.GetCollider() is IDamageable target)
            {
                _mineTimer -= delta;
                if (_mineTimer <= 0.0)
                {
                    _mineTimer = 0.5;
                    Swing(target);
                }
            }

            return;
        }

        if (bp != _mineTarget || id != _mineBlock)
        {
            _mineTarget = bp;
            _mineBlock = id;
            _mineProgress = 0.0f;
            _mineSoundTimer = 0.0;
            _crackStage = -1;
            StartPunch();
        }

        BlockDb.BlockDef def = BlockDb.DefOf(id);
        if (Mode == GameMode.Creative)
        {
            FinishBreak(bp, id, def, null);
            return;
        }

        ItemStack held = Inventory.Hotbar[Selected];
        ToolInfo? tool = ItemDb.ToolOf(held.Id);
        float miningSeconds = BlockDb.MiningSeconds(def, tool);
        if (!float.IsFinite(miningSeconds) || miningSeconds <= 0.0f)
        {
            _crack.Visible = false;
            return;
        }

        _mineProgress = Mathf.Min(_mineProgress + (float)delta / miningSeconds
                * WaterPhysics.MiningSpeedMultiplier(Underwater), 1.0f);

        _mineSoundTimer -= delta;
        if (_mineSoundTimer <= 0.0)
        {
            _mineSoundTimer = 0.25;
            Vector3 center = new(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
            EmitSignal(SignalName.MiningTick, center, (int)id);
            StartPunch();
        }

        int stage = BlockDb.CrackStage(_mineProgress);
        if (stage != _crackStage)
        {
            _crackMat.AlbedoTexture = CrackTextureLazy.Value[stage];
            _crackStage = stage;
        }

        _crack.Visible = true;
        _crack.GlobalPosition = new Vector3(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
        _crackMat.AlbedoColor = new Color(1, 1, 1, 0.9f);

        if (_mineProgress >= 1.0f)
        {
            FinishBreak(bp, id, def, tool);
        }
    }

    private void ResetMining()
    {
        _mineProgress = 0.0f;
        _mineTarget = new Vector3I(int.MinValue, 0, 0);
        _mineBlock = BlockId.Air;
        _crackStage = -1;
        if (_crack != null)
        {
            _crack.Visible = false;
        }
    }

    private void UpdateSteps(float delta)
    {
        float horiz = Velocity.XZ().Length();
        if (IsOnFloor() && !Swimming && !Climbing && horiz > 1.0f)
        {
            _stepAccum += horiz * delta;
            if (_stepAccum > StepDistance)
            {
                _stepAccum = 0.0;
                Vector3 below = GlobalPosition + new Vector3(0, -0.4f, 0);
                BlockId id = World!.GetBlock(Mathf.FloorToInt(below.X), Mathf.FloorToInt(below.Y), Mathf.FloorToInt(below.Z));
                if (id != BlockId.Air)
                {
                    EmitSignal(SignalName.Stepped, GlobalPosition, (int)id);
                }
            }
        }
        else
        {
            _stepAccum = 0.0;
        }
    }

    // -----------------------------------------------------------------------
    // Selection outline
    // -----------------------------------------------------------------------

    private void BuildOutline()
    {
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float t = 0.012f;
        for (int axis = 0; axis < 3; axis++)
        {
            for (int a = 0; a < 2; a++)
            {
                for (int b = 0; b < 2; b++)
                {
                    Vector3 center = new(0.5f, 0.5f, 0.5f);
                    Vector3 size = new(t * 2.0f, t * 2.0f, t * 2.0f);
                    size[axis] = 1.0f + t * 2.0f;
                    center[(axis + 1) % 3] = a;
                    center[(axis + 2) % 3] = b;
                    AddBox(st, center, size, Colors.White);
                }
            }
        }

        StandardMaterial3D mat = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.05f, 0.05f, 0.05f, 0.9f),
        };
        _outline = new MeshInstance3D
        {
            Name = "Outline",
            Mesh = st.Commit(),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_outline);
    }

    private void BuildCrack()
    {
        _crackMat = new StandardMaterial3D
        {
            AlbedoTexture = CrackTextureLazy.Value[0],
            AlbedoColor = new Color(1, 1, 1, 0.9f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        _crack = new MeshInstance3D
        {
            Name = "Crack",
            Mesh = new BoxMesh { Size = new Vector3(1.006f, 1.006f, 1.006f) },
            MaterialOverride = _crackMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_crack);
    }

    private static ImageTexture[] BuildCrackTextures()
    {
        const int stageCount = 10;
        const int size = 32;
        ImageTexture[] textures = new ImageTexture[stageCount];
        for (int stage = 0; stage < stageCount; stage++)
        {
            Image img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            img.Fill(new Color(0, 0, 0, 0));
            RandomNumberGenerator rng = new() { Seed = 4242 };

            // Each stage preserves earlier fissures and adds new branches.
            for (int line = 0; line <= stage; line++)
            {
                float x = size / 2.0f + rng.RandfRange(-4.0f, 4.0f);
                float y = size / 2.0f + rng.RandfRange(-4.0f, 4.0f);
                float angle = rng.RandfRange(0.0f, Mathf.Tau);
                for (int step = 0; step < 18; step++)
                {
                    angle += rng.RandfRange(-0.5f, 0.5f);
                    x += Mathf.Cos(angle);
                    y += Mathf.Sin(angle);
                    int px = Mathf.Wrap((int)x, 0, size);
                    int py = Mathf.Wrap((int)y, 0, size);
                    Color crack = new(0.035f, 0.035f, 0.035f, 1.0f);
                    img.SetPixel(px, py, crack);
                    if (rng.Randf() < 0.55f)
                    {
                        img.SetPixel(Mathf.Wrap(px + 1, 0, size), py, crack);
                    }
                    if (stage >= 6 && step % 5 == 0)
                    {
                        img.SetPixel(px, Mathf.Wrap(py + 1, 0, size), crack);
                    }
                }
            }

            textures[stage] = ImageTexture.CreateFromImage(img);
        }

        return textures;
    }

    private static void AddBox(SurfaceTool st, Vector3 center, Vector3 size, Color col)
    {
        for (int f = 0; f < 6; f++)
        {
            st.SetNormal(BlockDb.FaceDirs[f]);
            st.SetColor(col);
            foreach (int t in Mesher.TriOrder)
            {
                Vector3 v = BlockDb.FaceVerts[f * 4 + t];
                st.SetUV(Vector2.Zero);
                st.AddVertex(center + (v - new Vector3(0.5f, 0.5f, 0.5f)) * size);
            }
        }
    }

    private void UpdateOutline()
    {
        if (World == null || _outline == null)
        {
            return;
        }

        if (TryGetTargetBlock(out Vector3I bp, out _))
        {
            _outline.GlobalPosition = new Vector3(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
            _outline.Visible = true;
        }
        else
        {
            _outline.Visible = false;
        }
    }

    // -----------------------------------------------------------------------
    // First-person arm
    // -----------------------------------------------------------------------

    private void BuildArm()
    {
        _arm = new Node3D();
        _arm.Name = "Arm";
        StandardMaterial3D skin = new()
        {
            AlbedoColor = new Color(0.87f, 0.66f, 0.50f),
            Roughness = 1.0f,
        };
        StandardMaterial3D sleeve = new()
        {
            AlbedoColor = new Color(0.32f, 0.53f, 0.82f),
            Roughness = 1.0f,
        };
        AddArmPart(MakeBoxMesh(new Vector3(0.22f, 0.55f, 0.22f), new Vector3(0, -0.27f, 0)), sleeve);
        AddArmPart(MakeBoxMesh(new Vector3(0.245f, 0.30f, 0.245f), new Vector3(0, -0.70f, 0)), skin);
        _armBase = new Vector3(0.46f, -0.42f, -0.55f);
        _arm.Position = _armBase;
        _arm.Rotation = new Vector3(-0.25f, 0.1f, -0.18f);
        _heldItemSprite = new Sprite3D
        {
            Name = "HeldItem",
            PixelSize = 0.008f,
            Position = new Vector3(0.12f, -0.53f, -0.10f),
            Rotation = new Vector3(0.0f, 0.0f, -0.12f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Shaded = false,
        };
        _arm.AddChild(_heldItemSprite);
        Camera.AddChild(_arm);
    }

    private void AddArmPart(ArrayMesh mesh, Material material)
    {
        MeshInstance3D part = new()
        {
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _arm.AddChild(part);
    }

    private static ArrayMesh MakeBoxMesh(Vector3 size, Vector3 offset)
    {
        SurfaceTool st = new();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, offset, size, Colors.White);
        return st.Commit();
    }

    private void StartPunch()
    {
        _punch = 1.0;
    }

    private void UpdateArm(float delta)
    {
        if (_arm == null)
        {
            return;
        }

        ItemStack held = Inventory.Hotbar[Selected];
        int heldId = held.IsEmpty ? 0 : (int)held.Id;
        if (heldId != _heldItemId)
        {
            _heldItemId = heldId;
            _heldItemSprite.Texture = held.IsEmpty ? null : ItemDb.Icon(held.Id);
            _heldItemSprite.Visible = !held.IsEmpty;
        }

        if (Swimming)
        {
            _swimCameraPhase += delta * (5.0f + Velocity.Length() * 0.35f);
            float stroke = Mathf.Sin(_swimCameraPhase);
            _arm.Position = _armBase + new Vector3(stroke * 0.035f, Mathf.Abs(stroke) * 0.025f,
                    -0.045f - Mathf.Abs(stroke) * 0.06f);
            _arm.Rotation = new Vector3(-0.62f + stroke * 0.16f, 0.1f, -0.18f + stroke * 0.1f);
            _heldItemSprite.Position = new Vector3(0.12f + stroke * 0.035f, -0.53f - Mathf.Abs(stroke) * 0.03f, -0.16f);
            _heldItemSprite.Rotation = new Vector3(-0.35f + stroke * 0.18f, 0.0f, -0.12f + stroke * 0.2f);
            return;
        }

        float horiz = Velocity.XZ().Length();
        if (IsOnFloor() && horiz > 0.5f)
        {
            _bobPhase += delta * (7.0 + horiz);
        }

        float bob = (float)(System.Math.Sin(_bobPhase) * 0.03 * Mathf.Clamp(horiz / WalkSpeed, 0.0f, 1.6f));
        double swing = 0.0;
        if (_punch > 0.0)
        {
            _punch = System.Math.Max(_punch - delta / 0.22, 0.0);
            swing = System.Math.Sin(_punch * System.Math.PI);
        }

        float swingF = (float)swing;
        _arm.Position = _armBase + new Vector3(bob * 0.5f, bob - 0.06f * swingF, -0.20f * swingF);
        _arm.Rotation = new Vector3(-0.25f - 0.85f * swingF, 0.1f, -0.18f);
        _heldItemSprite.Position = new Vector3(0.12f, -0.53f - 0.04f * swingF, -0.10f - 0.12f * swingF);
        _heldItemSprite.Rotation = new Vector3(-0.12f * swingF, 0.0f, -0.12f - 0.35f * swingF);
    }
}
