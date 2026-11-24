using System;
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
    public const float CollisionRadius = 0.35f;
    public const float CollisionHeight = 1.8f;

    internal readonly record struct MinedDrop(ItemId Id, int Count);
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
    [Signal] public delegate void EnchantingTableUsedEventHandler();
    [Signal] public delegate void ChestUsedEventHandler(Vector3I pos);
    [Signal] public delegate void FurnaceUsedEventHandler(Vector3I pos);
    [Signal] public delegate void SleptEventHandler();
    [Signal] public delegate void TraderUsedEventHandler();
    [Signal] public delegate void NoticeEventHandler(string text);

    public event System.Action<Villager>? VillagerInteracted;
    public event System.Action<ItemId, int>? ItemGathered;
    public event System.Action<ItemId, int>? ItemCrafted;
    public event System.Action<int>? TradeCompleted;
    [Signal] public delegate void AteEventHandler();
    [Signal] public delegate void CampfireUsedEventHandler(string message);
    [Signal] public delegate void RespawnedEventHandler();
    [Signal] public delegate void LandedEventHandler(Vector3 pos, float impact);
    [Signal] public delegate void BucketUsedEventHandler(Vector3 pos, bool lava, bool collected);

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
    /// <summary>Current smoothed camera immersion blend, retained briefly after leaving water.</summary>
    public float WaterViewBlend => _waterViewBlend;
    public bool Climbing;
    public int Selected;
    public GameMode Mode = GameMode.Creative;
    public Vector3? RespawnPoint;
    public PlayerStats Stats { get; } = new();
    public bool Dead => Stats.Dead;

    public Inventory Inventory { get; } = new();
    public BoatSystem? Boats;
    public Boat? CurrentBoat { get; private set; }
    private Node? _preBoatParent;

    public void InteractVillager(Villager villager) => VillagerInteracted?.Invoke(villager);

    public void RecordGathered(ItemId item, int count)
    {
        if (item != ItemId.None && count > 0) ItemGathered?.Invoke(item, count);
    }

    public void RecordCrafted(ItemId item, int count)
    {
        if (item != ItemId.None && count > 0) ItemCrafted?.Invoke(item, count);
    }

    public void RecordTrade(int count = 1)
    {
        if (count > 0) TradeCompleted?.Invoke(count);
    }

    private CollisionShape3D _bodyCollider = null!;
    public Camera3D Camera = null!;
    public RayCast3D Ray = null!;

    private bool _skipMotion;
    private double _coyote;
    private double _jumpBuffer;
    private MeshInstance3D _outline = null!;
    private MeshInstance3D _outlineContrast = null!;
    private Mesh _cubeOutlineMesh = null!;
    private Mesh _cubeOutlineContrastMesh = null!;
    private Vector3I _outlinedBlock = new(int.MinValue, 0, 0);
    private BlockId _outlinedId = BlockId.Air;
    private byte _outlinedState = byte.MaxValue;
    private static readonly System.Collections.Generic.Dictionary<(BlockId Id, byte State),
            BlockSelectionOutline.MeshSet> ShapeOutlineCache = [];
    private Node3D _arm = null!;
    private Sprite3D _heldItemSprite = null!;
    private StandardMaterial3D? _heldItemMaterial;
    private int _heldItemId = -1;
    private int _heldItemIconState = -1;
    private Vector3 _armBase = Vector3.Zero;
    private double _bobPhase;
    private double _idleArmPhase;
    private double _climbArmPhase;
    private float _bowDrawBlend;
    private float _shieldGuardBlend;
    private readonly FirstPersonAnimationState _firstPersonAnimation = new();
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
    private Godot.Environment? _lavaEnvironment;
    private float _lavaAirFogDensity;
    private Color _lavaAirFogColor;
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
        // Water immersion is blended by DayNight on the shared world
        // environment so sky, biome tint and fog never switch as a camera
        // override. Lava retains its separate close-range presentation.
        _lavaAirFogDensity = environment.FogMode == Godot.Environment.FogModeEnum.Depth
                ? WorldEnvironmentFactory.ImmersionAirFogDensity : environment.FogDensity;
        _lavaAirFogColor = environment.FogLightColor;
        _lavaEnvironment = (Godot.Environment)environment.Duplicate();
        _lavaEnvironment.FogEnabled = true;
        _lavaEnvironment.FogMode = Godot.Environment.FogModeEnum.Exponential;
        _lavaEnvironment.VolumetricFogEnabled = false;
    }

    public override void _Ready()
    {
        Camera = GetNode<Camera3D>("Camera3D");
        Ray = GetNode<RayCast3D>("Camera3D/RayCast3D");
        _bodyCollider = GetNode<CollisionShape3D>("Collision");
        Ray.TargetPosition = new Vector3(0, 0, -Reach);
        Ray.CollisionMask = (uint)(PhysicsLayer.Terrain | PhysicsLayer.Mobs);
        Ray.AddException(this);
        FloorSnapLength = 0.4f;
        // Eye adaptation: brightens caves/night, dims looking at the noon sky.
        // Narrow range on purpose: the night must stay moody, not look like day.
        CameraAttributesPractical attrs = new()
        {
            AutoExposureEnabled = true,
            AutoExposureMinSensitivity = DayNight.MinimumAutoExposureSensitivity,
            AutoExposureMaxSensitivity = DayNight.OutdoorAutoExposureMaxSensitivity,
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
        if (InputLocked) return;
        if (CurrentBoat != null)
        {
            if (@event.IsActionPressed("jump")) CurrentBoat.Dismount();
            return;
        }
        if (Stats.Dead) return;

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
        if (CurrentBoat is { } mounted)
        {
            Stats.Update(delta, false, mounted.Velocity.LengthSquared() > 0.1f, Mode);
            return;
        }
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
        RecoverFromVoidFall();

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

    /// <summary>Apply the void hit and recover in one physics-side transition.</summary>
    internal void RecoverFromVoidFall()
    {
        if (World == null || GlobalPosition.Y >= -8.0f)
            return;

        ApplyFallDamage(4.0f);
        GlobalPosition = World.FindSpawn() + new Vector3(0, 1.0f, 0);
        Velocity = Vector3.Zero;
        _fallStartY = null;
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
        if (_lavaEnvironment == null || Camera == null) return;
        float dt = (float)System.Math.Clamp(delta, 0.0, 0.1);
        float waterTarget = Underwater ? Mathf.Lerp(0.72f, 1.0f, _waterImmersion) : 0.0f;
        _waterViewBlend = Mathf.MoveToward(_waterViewBlend, waterTarget, dt * 2.2f);
        _lavaViewBlend = Mathf.MoveToward(_lavaViewBlend, InLava ? 1.0f : 0.0f, dt * 3.0f);

        _lavaEnvironment.FogDensity = Mathf.Lerp(
                _lavaAirFogDensity, 0.085f, _lavaViewBlend);
        _lavaEnvironment.FogLightColor = _lavaAirFogColor.Lerp(
                new Color(0.34f, 0.075f, 0.018f), _lavaViewBlend);

        // Water never replaces Camera.Environment: its smoothed blend is read
        // by DayNight and applied to the shared world fog instead.
        Camera.Environment = _lavaViewBlend > 0.001f ? _lavaEnvironment : null;
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

    }

    // -----------------------------------------------------------------------
    // Survival: damage, death, respawn, mode
    // -----------------------------------------------------------------------

    /// <summary>All incoming hurt routes here; creative and invulnerability refuse it.</summary>
    internal bool ApplyFallDamage(float amount) =>
            ApplyDamage(new DamageInfo(amount, DamageKind.Fall));

    public bool ApplyDamage(in DamageInfo info)
    {
        // Refuse invalid/deferred hits before shield durability or feedback can be consumed.
        if (!EquipmentProgression.ShouldProcessIncomingDamage(info.Amount, Mode, Stats.Invulnerable))
        {
            return false;
        }

        float amount = info.Amount;
        ItemStack held = Inventory.Hotbar[Selected];
        // Damage can arrive while a player node is being constructed or tested
        // outside the scene tree; engine transforms are invalid there.
        bool inTree = IsInsideTree();
        Vector3 forward = inTree ? -GlobalTransform.Basis.Z : Vector3.Forward;
        Vector3 position = inTree ? GlobalPosition : Vector3.Zero;
        if (Mode == GameMode.Survival && EquipmentProgression.BlocksWithShield(held, forward, position, info))
        {
            Inventory.DamageEquipment(Selected);
            StartPunch(FirstPersonAction.ShieldBlock);
            EmitSignal(SignalName.Notice, "Blocked");
            return false;
        }
        // Armor protects against physical/environmental hits, not hunger or drowning.
        if (EquipmentProgression.ShouldDamageArmor(info.Kind))
        {
            amount *= 1.0f - ArmorReduction;
        }

        bool applied = Stats.ApplyDamage(amount, Mode);
        if (applied && EquipmentProgression.ShouldDamageArmor(info.Kind))
        {
            DamageArmor();
        }

        if (applied && info.Knockback > 0.0f)
        {
            Vector3 away = position - info.Source;
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

    private float ArmorReduction => 1.0f - EquipmentProgression.ArmorDamageMultiplier(Inventory);

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

            if (EquipmentProgression.DurabilityDamage(stack, GD.Randf()) == 0) continue;
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

        // Death can happen while seated. The player is reparented under the boat
        // and its body collider is disabled, so respawning without leaving the
        // boat would strand the player as a dead-passenger with no movement.
        if (CurrentBoat is { } mounted)
        {
            if (GodotObject.IsInstanceValid(mounted))
            {
                if (mounted.Rider == this) mounted.SetRider(null);
                ExitBoat(mounted, mounted.GlobalPosition + Vector3.Up * 1.1f);
            }
            else
            {
                CurrentBoat = null;
                _preBoatParent = null;
                _bodyCollider.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
            }
        }

        Stats.ResetForRespawn();
        Vector3 target = RespawnPoint ?? (World?.FindSpawn() ?? GlobalPosition) + new Vector3(0, 1.0f, 0);
        if (IsInsideTree())
        {
            GlobalPosition = target;
        }
        else
        {
            Position = target;
        }

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
    public void LoadFromSave(string? mode, float health, float hunger, Vector3? respawn,
            int experience = 0, bool dead = false)
    {
        if (mode == "survival")
        {
            Mode = GameMode.Survival;
        }
        else if (mode == "creative")
        {
            Mode = GameMode.Creative;
        }

        Stats.Load(health, hunger, experience, dead && Mode == GameMode.Survival);
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
            StartPunch(FirstPersonAction.Landing);
            EmitSignal(SignalName.Landed, GlobalPosition, fallen);
            if (Mode == GameMode.Survival && fallen > FallSafe && World != null)
            {
                BlockId below = World.GetBlock(
                        Mathf.FloorToInt(GlobalPosition.X),
                        Mathf.FloorToInt(GlobalPosition.Y - 0.5f),
                        Mathf.FloorToInt(GlobalPosition.Z));
                if (!BlockDb.IsWater(below))
                {
                    ApplyFallDamage(Mathf.Floor(fallen - 3.0f));
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
        if (Inventory.Hotbar[Selected].Id == ItemId.Bow)
        {
            FireBow();
            ResetMining();
            return;
        }

        if (World == null)
        {
            ResetMining();
            return;
        }

        if (Ray.IsColliding() && Ray.GetCollider() is Boat boat)
        {
            boat.Break(this);
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
        StartPunch(FirstPersonAction.Mine);
    }

    /// <summary>One melee swing: tool tier picks the damage, swing cadence is capped.</summary>
    private void Swing(IDamageable target)
    {
        double now = GameplayClock.Now;
        ItemStack held = Inventory.Hotbar[Selected];
        WeaponInfo? weapon = ItemDb.WeaponOf(held.Id);
        ToolInfo? tool = ItemDb.ToolOf(held.Id);
        double cooldown = weapon is { AttackSpeed: > 0 } w ? w.AttackSpeed : SwingCooldown;
        if (now - _lastSwing < cooldown)
        {
            return;
        }

        _lastSwing = now;
        StartPunch(FirstPersonAction.Melee);
        float damage = (weapon?.Damage ?? (1.0f + (tool?.Tier ?? 0)))
                + EquipmentProgression.SharpnessBonus(held);
        bool critical = !IsOnFloor() && Velocity.Y < -0.1f;
        if (critical)
        {
            damage *= 1.5f;
        }

        float knockback = weapon?.Knockback ?? 3.0f;
        bool landed = target.ApplyDamage(new DamageInfo(
                damage, DamageKind.Melee, GlobalPosition, knockback, 1.5f));
        if (landed && EquipmentProgression.ShouldConsumeMeleeDurability(held.Id))
        {
            Inventory.DamageEquipment(Selected);
        }

        if (landed && critical)
        {
            EmitSignal(SignalName.Notice, "Critical hit!");
        }
    }

    public Vector3I TargetBlock()
    {
        Vector3 p = Ray.GetCollisionPoint() - Ray.GetCollisionNormal() * 0.01f;
        return new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
    }

    private bool TryGetTargetBlock(out Vector3I blockPosition, out BlockId blockId) =>
        TryGetTargetBlock(out blockPosition, out blockId, out _, out _);

    private bool TryGetTargetBlock(out Vector3I blockPosition, out BlockId blockId,
            out Vector3 hitPoint, out Vector3 hitNormal)
    {
        blockPosition = new Vector3I(int.MinValue, 0, 0);
        blockId = BlockId.Air;
        hitPoint = Vector3.Zero;
        hitNormal = Vector3.Zero;
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
                maxDistance, out blockPosition, out blockId, out hitPoint, out hitNormal);
    }

    /// <summary>
    /// Amanatides-Woo traversal. Cubes use cell entry, partial shapes use
    /// exact sub-voxel AABB intersections, and cross plants use their planes.
    /// </summary>
    internal bool TryRaycastVoxels(
            Vector3 origin, Vector3 direction, float maxDistance,
            out Vector3I blockPosition, out BlockId blockId,
            out Vector3 hitPoint, out Vector3 hitNormal)
    {
        blockPosition = new Vector3I(int.MinValue, 0, 0);
        blockId = BlockId.Air;
        hitPoint = Vector3.Zero;
        hitNormal = Vector3.Zero;
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
        Vector3 entryNormal = -direction;
        while (entryDistance <= maxDistance)
        {
            float exitDistance = Mathf.Min(nextX, Mathf.Min(nextY, nextZ));
            if (!World!.TryGetLoadedBlock(x, y, z, out BlockId current))
                return false;
            if (current != BlockId.Air && !BlockDb.IsWater(current) && current != BlockId.Lava)
            {
                Vector3I cell = new(x, y, z);
                float hitDistance = entryDistance;
                Vector3 normal = entryNormal;
                bool hit = true;
                if (BlockShapes.IsShape(current)
                        && !BlockShapes.IsFullCube(current, World.GetBlockState(x, y, z)))
                {
                    hit = BlockShapes.Raycast(origin, direction, cell, current,
                            World.GetBlockState(x, y, z), entryDistance,
                            Mathf.Min(exitDistance, maxDistance), out hitDistance, out normal);
                }
                else if (BlockDb.IsCross(current))
                {
                    hit = BlockDb.RayIntersectsCross(origin, direction, cell,
                            entryDistance, Mathf.Min(exitDistance, maxDistance), out hitDistance);
                    normal = -direction;
                }

                if (hit)
                {
                    blockPosition = cell;
                    blockId = current;
                    hitPoint = origin + direction * hitDistance;
                    hitNormal = normal;
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
                entryNormal = new Vector3(-stepX, 0.0f, 0.0f);
            }
            if (advanceY)
            {
                y += stepY;
                nextY += deltaY;
                if (!advanceX) entryNormal = new Vector3(0.0f, -stepY, 0.0f);
            }
            if (advanceZ)
            {
                z += stepZ;
                nextZ += deltaZ;
                if (!advanceX && !advanceY) entryNormal = new Vector3(0.0f, 0.0f, -stepZ);
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

    /// <summary>True when the ray currently rests on an enchanting table in reach.</summary>
    public bool EnchantingTableInReach()
    {
        if (World == null || !Ray.IsColliding() || Ray.GetCollider() is Mob) return false;
        Vector3I bp = TargetBlock();
        return World.GetBlock(bp.X, bp.Y, bp.Z) == BlockId.EnchantingTable;
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

        StartPunch(FirstPersonAction.Place);
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
        StartPunch(FirstPersonAction.Place);
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

    /// <summary>
    /// Consume one raw food item and add its cooked result atomically. A full
    /// inventory must not eat the raw item and silently discard the cooked one.
    /// </summary>
    internal static bool TryCook(Inventory inventory, ItemId raw)
    {
        if (ItemDb.CooksTo(raw) is not { } cooked || !inventory.Consume(raw, 1))
        {
            return false;
        }

        if (inventory.Add(cooked, 1) == 0)
        {
            return true;
        }

        // The result could not fit; restore the consumed raw item so the failed
        // action has no net cost.
        inventory.Add(raw, 1);
        return false;
    }

    /// <summary>Campfire use: cook the held raw food and/or rest (respawn + heal).</summary>
    private void UseCampfire(ItemStack stack)
    {
        if (World == null)
        {
            return;
        }

        bool cookable = ItemDb.CooksTo(stack.Id) != null;
        bool cooked = TryCook(Inventory, stack.Id);
        if (cookable && !cooked)
        {
            EmitSignal(SignalName.Notice, "Make room for the cooked food");
        }

        bool rested = false;
        if (GameplayClock.Now - _lastRest >= 3.0)
        {
            _lastRest = GameplayClock.Now;
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

        if (GameplayClock.Now - _lastEat < 0.7)
        {
            return;
        }

        _lastEat = GameplayClock.Now;
        Inventory.Consume(stack.Id, 1);
        Stats.AddHunger(food.Hunger);
        Stats.Heal(food.Heal);
        EmitSignal(SignalName.Ate);
        StartPunch(FirstPersonAction.Eat);
    }

    internal void FinishBreak(Vector3I bp, BlockId id, BlockDb.BlockDef def, ToolInfo? tool)
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

        byte brokenState = World.GetBlockState(bp.X, bp.Y, bp.Z);
        Span<MinedDrop> plannedDrops = stackalloc MinedDrop[3];
        int plannedDropCount = Mode == GameMode.Creative
                ? 0
                : BuildMinedDrops(id, def, tool, brokenState, plannedDrops);
        if (!CanFitMinedDrops(Inventory, plannedDrops[..plannedDropCount]))
        {
            EmitSignal(SignalName.InventoryFull);
            return;
        }

        if (!World.SetBlock(bp.X, bp.Y, bp.Z, BlockId.Air))
            return;

        if (id == BlockId.Chest)
        {
            World.RemoveContainer(bp);
        }
        else if (id == BlockId.Furnace)
        {
            World.RemoveFurnace(bp);
        }

        StartPunch(FirstPersonAction.Mine);
        Vector3 center = new(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
        EmitSignal(SignalName.BlockBroken, center, (int)id);

        // Every reward was capacity-checked before the world mutation. Use one
        // delivery path so ordinary and special drops report identical gather events.
        if (Mode != GameMode.Creative)
        {
            for (int i = 0; i < plannedDropCount; i++)
                GiveDrop(plannedDrops[i].Id, plannedDrops[i].Count);

            if (tool is not null)
                DamageTool();
        }
    }

    private int BuildMinedDrops(BlockId id, BlockDb.BlockDef def, ToolInfo? tool,
            byte brokenState, Span<MinedDrop> drops)
    {
        int count = 0;
        if (id == BlockId.Wheat2)
        {
            AddMinedDrop(drops, ref count, ItemId.Wheat, 1 + (int)(GD.Randf() * 3.0f));
            AddMinedDrop(drops, ref count, ItemId.Seeds, 1 + (int)(GD.Randf() * 2.0f));
        }
        else if (id is BlockId.Wheat0 or BlockId.Wheat1)
        {
            AddMinedDrop(drops, ref count, ItemId.Seeds, 1);
        }
        else if (id == BlockId.TallGrass && GD.Randf() < 0.15f)
        {
            AddMinedDrop(drops, ref count, ItemId.Seeds, 1);
        }

        if (BlockDb.CanHarvest(def, tool) && def.Drops)
        {
            ItemId drop = def.Drop != ItemId.None ? def.Drop : ItemDb.BlockItem(id);
            if (drop != ItemId.None && def.DropCount > 0)
                AddMinedDrop(drops, ref count, drop,
                        BlockShapes.DropCount(id, brokenState, def.DropCount));
        }

        // Apples are an oak-leaf bonus drop; birch foliage never produces them.
        if (id == BlockId.Leaves && GD.Randf() < 0.06f)
            AddMinedDrop(drops, ref count, ItemId.Apple, 1);

        return count;
    }

    private static void AddMinedDrop(Span<MinedDrop> drops, ref int count, ItemId id, int amount)
    {
        if (id == ItemId.None || amount <= 0) return;
        if ((uint)count >= (uint)drops.Length)
            throw new InvalidOperationException("Mined-drop plan exceeded its fixed capacity.");
        drops[count++] = new MinedDrop(id, amount);
    }

    internal static bool CanFitMinedDrops(Inventory inventory, ReadOnlySpan<MinedDrop> drops)
    {
        int emptySlots = 0;
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            if (inventory.Get(slot).IsEmpty) emptySlots++;

        int requiredEmptySlots = 0;
        for (int i = 0; i < drops.Length; i++)
        {
            ItemId id = drops[i].Id;
            if (id == ItemId.None || drops[i].Count <= 0) continue;

            bool seen = false;
            for (int previous = 0; previous < i; previous++)
            {
                if (drops[previous].Id == id)
                {
                    seen = true;
                    break;
                }
            }
            if (seen) continue;

            int total = 0;
            for (int j = i; j < drops.Length; j++)
                if (drops[j].Id == id) total += drops[j].Count;
            if (!inventory.CanFit(id, total)) return false;

            int maxStack = ItemDb.MaxStack(id);
            int existingSpace = 0;
            for (int slot = 0; slot < Inventory.SlotCount; slot++)
            {
                ItemStack stack = inventory.Get(slot);
                if (stack.Id == id)
                    existingSpace += Math.Max(maxStack - stack.Count, 0);
            }

            int remainder = Math.Max(total - existingSpace, 0);
            requiredEmptySlots += (remainder + maxStack - 1) / maxStack;
        }

        // Per-item CanFit checks share the same empty slots. Reserve the
        // combined number of slots needed so multi-item harvests are atomic.
        return requiredEmptySlots <= emptySlots;
    }
    internal void GiveDrop(ItemId id, int count)
    {
        if (count <= 0)
        {
            return;
        }

        int leftover = Inventory.Add(id, count);
        int gained = count - leftover;
        if (gained > 0) RecordGathered(id, gained);
        if (leftover > 0) EmitSignal(SignalName.InventoryFull);
    }

    private void DamageTool()
    {
        if (Inventory.DamageTool(Selected))
        {
            EmitSignal(SignalName.ToolBroke);
        }
    }

    private void FireBow()
    {
        if (Inventory.Hotbar[Selected].Id != ItemId.Bow) return;
        double now = GameplayClock.Now;
        if (now - _lastSwing < 0.8) return;
        if (Mode == GameMode.Survival && !Inventory.Consume(ItemId.Arrow, 1))
        {
            EmitSignal(SignalName.Notice, "Need arrows to fire");
            return;
        }
        _lastSwing = now;
        if (Mode == GameMode.Survival) Inventory.DamageEquipment(Selected);
        StartPunch(FirstPersonAction.BowRelease);
        ArrowProjectile arrow = new();
        Node parent = GetTree().CurrentScene ?? this;
        parent.AddChild(arrow);
        Vector3 direction = -Camera.GlobalTransform.Basis.Z;
        arrow.Launch(this, Camera.GlobalPosition + direction * 0.65f, direction);
    }
    public void EnterBoat(Boat boat)
    {
        if (CurrentBoat != null) return;
        CurrentBoat = boat;
        _preBoatParent = GetParent();
        _bodyCollider.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        Reparent(boat, false);
        Position = new Vector3(0.0f, 0.48f, 0.0f);
        Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
        _coyote = 0.0;
        _jumpBuffer = 0.0;
    }

    public void ExitBoat(Boat boat, Vector3 position)
    {
        if (CurrentBoat != boat) return;
        Node? destination = _preBoatParent is { } parent && GodotObject.IsInstanceValid(parent)
                ? parent : boat.GetParent();
        CurrentBoat = null;
        _preBoatParent = null;
        if (destination != null) Reparent(destination, true);
        GlobalPosition = position;
        _bodyCollider.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
        Velocity = Vector3.Zero;
        _wasSwimming = false;
    }

    public void PlaceBlock()
    {
        if (World == null)
        {
            return;
        }

        ItemId selectedItem = Inventory.Hotbar[Selected].Id;
        if (selectedItem == ItemId.Bow)
        {
            FireBow();
            return;
        }
        if (selectedItem == ItemId.Shield)
        {
            StartPunch(FirstPersonAction.ShieldBlock);
            return;
        }

        StartPunch(FirstPersonAction.Place);
        // Mobs first: right-click is "use" on a creature (tame a sheep).
        if (Ray.IsColliding() && Ray.GetCollider() is Mob mob)
        {
            if (mob.Interact(this))
            {
                StartPunch();
            }

            return;
        }

        if (Ray.IsColliding() && Ray.GetCollider() is Boat boat)
        {
            if (!boat.TryBoard(this))
                EmitSignal(SignalName.Notice, "That boat is occupied or out of reach");
            return;
        }

        ItemStack stack = Inventory.Hotbar[Selected];
        if (BucketMechanics.IsBucket(stack.Id))
        {
            UseBucket(stack);
            return;
        }
        if (stack.Id == ItemId.WoodenBoat)
        {
            PlaceBoat();
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

        if (EnchantingTableInReach())
        {
            EmitSignal(SignalName.EnchantingTableUsed);
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

        if (!TryGetTargetBlock(out Vector3I target, out BlockId targetId,
                    out Vector3 hitPoint, out Vector3 hitNormal))
            return;

        byte state = 0;
        Vector3 localHit = hitPoint - new Vector3(target.X, target.Y, target.Z);
        byte targetState = World.GetBlockState(target.X, target.Y, target.Z);
        bool combineSlab = BlockShapes.CanCombineSlab(place, targetId, targetState,
                hitNormal, localHit);
        Vector3I destination = combineSlab ? target : target + BlockShapes.CellStepFromNormal(hitNormal);
        if (combineSlab)
        {
            state = BlockShapes.SlabDouble;
        }
        else if (BlockShapes.Kind(place) == BlockShapeKind.Slab)
        {
            state = BlockShapes.SelectSlabState(hitNormal, localHit);
        }
        else if (BlockShapes.Kind(place) == BlockShapeKind.Stairs)
        {
            state = BlockShapes.StairStateFromPlacement(hitNormal, localHit, GlobalRotation.Y);
        }

        int bx = destination.X;
        int by = destination.Y;
        int bz = destination.Z;
        if (!World.TryGetLoadedBlock(bx, by, bz, out BlockId existing))
            return;
        if (!combineSlab && !CanReplacePlacementDestination(existing))
            return;

        if (BlockHitsPlayer(bx, by, bz, place, state))
            return;

        if (World.SetBlock(bx, by, bz, place, state))
        {
            if (place == BlockId.Chest)
                World.GetContainer(new Vector3I(bx, by, bz));
            else if (place == BlockId.Furnace)
                World.GetFurnace(new Vector3I(bx, by, bz));

            StartPunch(FirstPersonAction.Place);
            EmitSignal(SignalName.BlockPlaced,
                    new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f), (int)place);
            Inventory.Hotbar[Selected] = stack.Count <= 1
                    ? ItemStack.Empty : stack with { Count = stack.Count - 1 };
            Inventory.RaiseChanged();
        }
    }

    private void UseBucket(ItemStack stack)
    {
        if (World == null || !TryGetBucketRayHit(
                    out Vector3I hit, out BlockId hitId, out Vector3I faceNormal))
        {
            EmitSignal(SignalName.Notice, "Aim at a source fluid or a block face");
            return;
        }

        bool collected;
        bool lava;
        Vector3 actionPosition = new(hit.X + 0.5f, hit.Y + 0.5f, hit.Z + 0.5f);
        if (stack.Id == ItemId.EmptyBucket)
        {
            if (!BucketMechanics.TryCollect(World, hit, Inventory, Selected,
                        out BlockId collectedFluid))
            {
                EmitSignal(SignalName.Notice, BlockDb.IsWater(hitId)
                        ? "Only source water can be collected"
                        : "Aim at source water or lava");
                return;
            }

            collected = true;
            lava = collectedFluid == BlockId.Lava;
        }
        else
        {
            if (BlockDb.IsWater(hitId) || hitId == BlockId.Lava || faceNormal == Vector3I.Zero)
            {
                EmitSignal(SignalName.Notice, "Aim at an open block face to place fluid");
                return;
            }

            Vector3I destination = hit + faceNormal;
            if (!BucketMechanics.TryPlace(World, destination, Inventory, Selected,
                        out BlockId placed))
            {
                EmitSignal(SignalName.Notice, "There is no open space for that fluid");
                return;
            }

            actionPosition = new(destination.X + 0.5f, destination.Y + 0.5f, destination.Z + 0.5f);
            collected = false;
            lava = placed == BlockId.Lava;
        }

        StartPunch();
        EmitSignal(SignalName.BucketUsed, actionPosition, lava, collected);
    }

    private void PlaceBoat()
    {
        if (Boats == null || World == null
                || !TryGetBucketRayHit(out Vector3I target, out BlockId id, out _)
                || !BlockDb.IsWater(id))
        {
            EmitSignal(SignalName.Notice, "Aim at a water surface to place the boat");
            return;
        }

        if (!Boats.TryPlaceBoat(target, id))
        {
            EmitSignal(SignalName.Notice, "The boat needs open, loaded water");
            return;
        }

        Inventory.Hotbar[Selected] = ItemStack.Empty;
        Inventory.RaiseChanged();
        StartPunch(FirstPersonAction.Place);
    }

    /// <summary>
    /// Loaded-only voxel ray for fluids and boat placement. Unlike the normal
    /// block ray it deliberately stops at water/lava instead of skipping them.
    /// Unloaded cells are never generated or interpreted as air.
    /// </summary>
    private bool TryGetBucketRayHit(out Vector3I position, out BlockId block,
            out Vector3I faceNormal)
    {
        position = new Vector3I(int.MinValue, 0, 0);
        block = BlockId.Air;
        faceNormal = Vector3I.Zero;
        if (World == null) return false;

        Vector3 origin = Camera.GlobalPosition;
        Vector3 direction = -Camera.GlobalTransform.Basis.Z;
        if (direction.LengthSquared() < 0.0001f) return false;
        direction = direction.Normalized();
        float maxDistance = Reach + 0.02f;
        if (Ray.IsColliding())
            maxDistance = Mathf.Min(maxDistance, origin.DistanceTo(Ray.GetCollisionPoint()) + 0.02f);

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);
        int sx = direction.X > 0.0f ? 1 : direction.X < 0.0f ? -1 : 0;
        int sy = direction.Y > 0.0f ? 1 : direction.Y < 0.0f ? -1 : 0;
        int sz = direction.Z > 0.0f ? 1 : direction.Z < 0.0f ? -1 : 0;
        float dx = sx == 0 ? float.PositiveInfinity : Mathf.Abs(1.0f / direction.X);
        float dy = sy == 0 ? float.PositiveInfinity : Mathf.Abs(1.0f / direction.Y);
        float dz = sz == 0 ? float.PositiveInfinity : Mathf.Abs(1.0f / direction.Z);
        float tx = sx > 0 ? (x + 1.0f - origin.X) / direction.X
                : sx < 0 ? (origin.X - x) / -direction.X : float.PositiveInfinity;
        float ty = sy > 0 ? (y + 1.0f - origin.Y) / direction.Y
                : sy < 0 ? (origin.Y - y) / -direction.Y : float.PositiveInfinity;
        float tz = sz > 0 ? (z + 1.0f - origin.Z) / direction.Z
                : sz < 0 ? (origin.Z - z) / -direction.Z : float.PositiveInfinity;

        float distance = 0.0f;
        while (distance <= maxDistance)
        {
            if (!World.TryGetLoadedBlock(x, y, z, out BlockId current)) return false;
            if (BlockDb.IsWater(current) || current == BlockId.Lava
                    || BlockDb.IsSolid(current) && !BlockDb.IsCross(current))
            {
                position = new Vector3I(x, y, z);
                block = current;
                return true;
            }

            float next = Mathf.Min(tx, Mathf.Min(ty, tz));
            if (!float.IsFinite(next) || next > maxDistance) break;
            if (tx <= next + 0.00001f)
            {
                x += sx;
                tx += dx;
                faceNormal = new Vector3I(-sx, 0, 0);
            }
            else if (ty <= next + 0.00001f)
            {
                y += sy;
                ty += dy;
                faceNormal = new Vector3I(0, -sy, 0);
            }
            else
            {
                z += sz;
                tz += dz;
                faceNormal = new Vector3I(0, 0, -sz);
            }
            distance = next;
        }
        return false;
    }

    public void PickBlock()
    {
        if (World == null || (Ray.IsColliding() && Ray.GetCollider() is Mob)
                || !TryGetTargetBlock(out Vector3I bp, out BlockId id))
        {
            return;
        }
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
                // A reserved slot holds an unknown future item. Do not replace
                // it with an ordinary pick-block swap.
                if (Inventory.HasUnrecognizedSaveSlot(Selected))
                {
                    return;
                }

                (Inventory.Hotbar[Selected], Inventory.Backpack[i]) =
                        (Inventory.Backpack[i], Inventory.Hotbar[Selected]);
                Inventory.RaiseChanged();
                return;
            }
        }
    }

    internal static bool CanReplacePlacementDestination(BlockId existing) =>
            existing == BlockId.Air || BlockDb.IsWater(existing) || BlockDb.IsReplaceable(existing);

    internal static bool BlockIntersectsPlayerCapsule(BlockId id, byte state,
            Vector3 blockOrigin, Vector3 capsuleBase, float radius, float height)
    {
        if (radius <= 0.0f || height < radius * 2.0f) return false;

        byte mask = BlockShapes.OccupancyMask(id, state);
        if (mask == 0) return false;

        float segmentMinY = capsuleBase.Y + radius;
        float segmentMaxY = capsuleBase.Y + height - radius;
        float radiusSquared = radius * radius;
        const float epsilon = 0.00001f;

        for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    if ((mask & (1 << (x + z * 2 + y * 4))) == 0) continue;

                    float minX = blockOrigin.X + x * 0.5f;
                    float maxX = minX + 0.5f;
                    float minY = blockOrigin.Y + y * 0.5f;
                    float maxY = minY + 0.5f;
                    float minZ = blockOrigin.Z + z * 0.5f;
                    float maxZ = minZ + 0.5f;

                    float closestX = Mathf.Clamp(capsuleBase.X, minX, maxX);
                    float closestZ = Mathf.Clamp(capsuleBase.Z, minZ, maxZ);
                    float dx = capsuleBase.X - closestX;
                    float dz = capsuleBase.Z - closestZ;
                    float dy = segmentMaxY < minY ? minY - segmentMaxY
                            : segmentMinY > maxY ? segmentMinY - maxY : 0.0f;

                    if (dx * dx + dy * dy + dz * dz < radiusSquared - epsilon)
                        return true;
                }

        return false;
    }

    private bool BlockHitsPlayer(int bx, int by, int bz, BlockId id, byte state) =>
            BlockIntersectsPlayerCapsule(id, state, new Vector3(bx, by, bz),
                    GlobalPosition, CollisionRadius, CollisionHeight);

    public void RestoreOrientation(float? yaw, float? pitch)
    {
        if (yaw is float savedYaw && float.IsFinite(savedYaw))
        {
            float tau = Mathf.Pi * 2.0f;
            float normalized = savedYaw % tau;
            if (normalized > Mathf.Pi) normalized -= tau;
            else if (normalized < -Mathf.Pi) normalized += tau;
            Rotation = new Vector3(Rotation.X, normalized, Rotation.Z);
        }

        if (Camera != null && pitch is float savedPitch && float.IsFinite(savedPitch))
        {
            Vector3 cameraRotation = Camera.Rotation;
            cameraRotation.X = Mathf.Clamp(savedPitch, -1.55f, 1.55f);
            Camera.Rotation = cameraRotation;
        }
    }

    public void Select(int i)
    {
        int next = Mathf.Wrap(i, 0, Inventory.HotbarSize);
        if (next != Selected)
            StartPunch(FirstPersonAction.Select);
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

        if (Inventory.Hotbar[Selected].Id == ItemId.Bow)
        {
            _mineTimer -= delta;
            if (_mineTimer <= 0.0)
            {
                _mineTimer = 0.8;
                FireBow();
            }
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
            StartPunch(FirstPersonAction.Mine);
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
            StartPunch(FirstPersonAction.Mine);
        }

        int stage = BlockDb.CrackStage(_mineProgress);
        if (stage != _crackStage)
        {
            _crackMat.AlbedoTexture = CrackTextureLazy.Value[stage];
            _crackStage = stage;
        }

        _crack.Visible = true;
        byte mineState = World.GetBlockState(bp.X, bp.Y, bp.Z);
        if (BlockShapes.Kind(id) == BlockShapeKind.Slab && mineState != BlockShapes.SlabDouble)
        {
            _crack.Scale = new Vector3(1.0f, 0.5f, 1.0f);
            float centerY = mineState == BlockShapes.SlabTop ? 0.75f : 0.25f;
            _crack.GlobalPosition = new Vector3(bp.X + 0.5f, bp.Y + centerY, bp.Z + 0.5f);
        }
        else
        {
            _crack.Scale = Vector3.One;
            _crack.GlobalPosition = new Vector3(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
        }
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
        BlockSelectionOutline.MeshSet cube = BlockSelectionOutline.BuildCube();
        _cubeOutlineMesh = cube.Silhouette;
        _cubeOutlineContrastMesh = cube.Contrast;

        _outline = new MeshInstance3D
        {
            Name = "Outline",
            Mesh = _cubeOutlineMesh,
            MaterialOverride = BlockSelectionOutline.CreateSilhouetteMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _outlineContrast = new MeshInstance3D
        {
            Name = "OutlineContrast",
            Mesh = _cubeOutlineContrastMesh,
            MaterialOverride = BlockSelectionOutline.CreateContrastMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_outline);
        AddChild(_outlineContrast);
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
        if (World == null || _outline == null || _outlineContrast == null)
            return;

        if (TryGetTargetBlock(out Vector3I bp, out BlockId id))
        {
            byte state = World.GetBlockState(bp.X, bp.Y, bp.Z);
            if (bp != _outlinedBlock || id != _outlinedId || state != _outlinedState)
            {
                _outlinedBlock = bp;
                _outlinedId = id;
                _outlinedState = state;
                if (BlockShapes.IsShape(id) && !BlockShapes.IsFullCube(id, state))
                {
                    if (!ShapeOutlineCache.TryGetValue((id, state), out BlockSelectionOutline.MeshSet shape))
                    {
                        shape = BlockSelectionOutline.BuildShape(id, state);
                        ShapeOutlineCache[(id, state)] = shape;
                    }
                    _outline.Mesh = shape.Silhouette;
                    _outlineContrast.Mesh = shape.Contrast;
                }
                else
                {
                    _outline.Mesh = _cubeOutlineMesh;
                    _outlineContrast.Mesh = _cubeOutlineContrastMesh;
                }
            }

            Vector3 targetPosition = new(bp.X + 0.5f, bp.Y + 0.5f, bp.Z + 0.5f);
            _outline.GlobalPosition = targetPosition;
            _outlineContrast.GlobalPosition = targetPosition;
            _outline.Visible = true;
            _outlineContrast.Visible = true;
        }
        else
        {
            _outline.Visible = false;
            _outlineContrast.Visible = false;
            _outlinedBlock = new Vector3I(int.MinValue, 0, 0);
            _outlinedId = BlockId.Air;
            _outlinedState = byte.MaxValue;
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
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            Shaded = true,
        };
        _arm.AddChild(_heldItemSprite);
        Camera.AddChild(_arm);
    }

    private static StandardMaterial3D MakeHeldItemMaterial(ItemId id, Texture2D icon)
    {
        SurfaceResponse response = MaterialResponse.Get(MaterialResponse.ForItem(id));
        return new StandardMaterial3D
        {
            AlbedoTexture = icon,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            Roughness = response.Roughness,
            Metallic = response.Metallic,
            MetallicSpecular = response.Specular,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
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

    private void StartPunch(FirstPersonAction action = FirstPersonAction.Use)
    {
        _firstPersonAnimation.Play(action);
    }

    private void UpdateArm(float delta)
    {
        if (_arm == null)
            return;

        _firstPersonAnimation.Advance(delta);

        ItemStack held = Inventory.Hotbar[Selected];
        int heldId = held.IsEmpty ? 0 : (int)held.Id;
        int iconState = held.IsEmpty ? 0 : ItemDb.IconVisualState(held);
        bool itemChanged = heldId != _heldItemId;
        if (itemChanged || iconState != _heldItemIconState)
        {
            _heldItemId = heldId;
            _heldItemIconState = iconState;
            if (itemChanged)
            {
                _bowDrawBlend = 0.0f;
                _shieldGuardBlend = 0.0f;
            }

            Texture2D? icon = held.IsEmpty ? null : ItemDb.Icon(held);
            _heldItemSprite.Texture = icon;
            if (itemChanged)
            {
                _heldItemMaterial = icon == null ? null : MakeHeldItemMaterial(held.Id, icon);
                _heldItemSprite.MaterialOverride = _heldItemMaterial;
            }
            else if (_heldItemMaterial != null)
            {
                _heldItemMaterial.AlbedoTexture = icon;
            }

            _heldItemSprite.Visible = !held.IsEmpty;
        }

        bool active = !InputLocked && !Stats.Dead;
        double now = GameplayClock.Now;
        float bowTarget = active && held.Id == ItemId.Bow
                && Input.IsActionPressed("break_block") && now - _lastSwing > 0.16
                ? 1.0f : 0.0f;
        float shieldTarget = active && held.Id == ItemId.Shield
                && Input.IsActionPressed("place_block") ? 1.0f : 0.0f;
        _bowDrawBlend = Mathf.MoveToward(_bowDrawBlend, bowTarget, delta * 4.5f);
        _shieldGuardBlend = Mathf.MoveToward(_shieldGuardBlend, shieldTarget, delta * 7.0f);

        float horiz = Velocity.XZ().Length();
        bool groundedMove = IsOnFloor() && horiz > 0.5f && !Stats.Dead;
        if (groundedMove)
            _bobPhase += delta * (7.0 + horiz);
        else
            _bobPhase = System.Math.Max(0.0, _bobPhase - delta * 4.0);

        float bobAmount = Settings.HeadBob && groundedMove
                ? Mathf.Clamp(horiz / WalkSpeed, 0.0f, 1.6f) * 0.026f
                : 0.0f;
        float bob = Mathf.Sin((float)_bobPhase) * bobAmount;
        bool idle = horiz < 0.15f && IsOnFloor() && !Swimming && !Climbing && !Flying;
        _idleArmPhase += delta * 1.7;
        float idleSway = idle ? Mathf.Sin((float)_idleArmPhase) * 0.0035f : 0.0f;

        float climb = 0.0f;
        if (Climbing && Mathf.Abs(Velocity.Y) > 0.1f)
        {
            _climbArmPhase += delta * (4.0f + Mathf.Abs(Velocity.Y));
            climb = Mathf.Sin((float)_climbArmPhase);
        }

        FirstPersonPose pose = FirstPersonAnimation.Evaluate(
                _firstPersonAnimation.Action,
                _firstPersonAnimation.Progress,
                bob,
                idleSway,
                _bowDrawBlend,
                _shieldGuardBlend,
                climb);

        if (Swimming)
        {
            _swimCameraPhase += delta * (5.0f + Velocity.Length() * 0.35f);
            float stroke = Mathf.Sin(_swimCameraPhase);
            _arm.Position = _armBase + new Vector3(
                    stroke * 0.035f, Mathf.Abs(stroke) * 0.025f,
                    -0.045f - Mathf.Abs(stroke) * 0.06f) + pose.ArmPosition * 0.45f;
            _arm.Rotation = new Vector3(
                    -0.62f + stroke * 0.16f, 0.1f, -0.18f + stroke * 0.1f)
                    + pose.ArmRotation * 0.45f;
            _heldItemSprite.Position = new Vector3(
                    0.12f + stroke * 0.035f, -0.53f - Mathf.Abs(stroke) * 0.03f, -0.16f)
                    + pose.ItemPosition * 0.45f;
            _heldItemSprite.Rotation = new Vector3(
                    -0.35f + stroke * 0.18f, 0.0f, -0.12f + stroke * 0.2f)
                    + pose.ItemRotation * 0.45f;
            _heldItemSprite.Scale = Vector3.One * pose.ItemScale;
            return;
        }

        _arm.Position = _armBase + pose.ArmPosition;
        _arm.Rotation = new Vector3(-0.25f, 0.1f, -0.18f) + pose.ArmRotation;
        _heldItemSprite.Position = new Vector3(0.12f, -0.53f, -0.10f) + pose.ItemPosition;
        _heldItemSprite.Rotation = new Vector3(0.0f, 0.0f, -0.12f) + pose.ItemRotation;
        _heldItemSprite.Scale = Vector3.One * pose.ItemScale;
    }
}
