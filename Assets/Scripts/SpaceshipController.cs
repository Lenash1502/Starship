using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class SpaceshipController : MonoBehaviour
{
    private PlayerControls controls;
    private Rigidbody rb;

    [Header("Thrust Speeds")]
    public float forwardThrust = 100f;
    public float lateralThrust = 50f;
    public float verticalThrust = 50f;

    [Header("Rotation (Virtual Joystick)")]
    public float pitchSpeed = 5f;
    public float yawSpeed = 5f;
    public float rollSpeed = 40f;
    public bool invertMouseY = false;

    [Tooltip("How far the mouse must move from the center (0 to 1) before the ship turns. 0.2 = 20% of the screen.")]
    [Range(0f, 1f)] public float deadzoneRadius = 0.15f;

    [Tooltip("How smoothly the ship ramps up and slows down its turning speed.")]
    public float turnSmoothing = 5f;

    [Header("Boost Settings")]
    public float boostPercentage = 100f;

    private bool isBoosting;
    private Vector3 moveInput;
    private float rollInput;
    private Vector2 aiAimIntent;

    // VJoy tracking variables
    private float currentPitch;
    private float currentYaw;

    // True when a real player (Input System + mouse) is flying this ship; false when an
    // AIWanderPilot on the same object is feeding inputs instead via the Set*() methods below.
    public bool IsPlayerControlled { get; private set; }

    // The raw (pre-smoothing) yaw/pitch aim intent for this frame, in [-1, 1] each axis.
    // SpaceshipVisualGimbal reads this instead of independently re-deriving it from the mouse,
    // so it stays correct for both player and AI-driven ships.
    public Vector2 RawAimIntent { get; private set; }

    // --- The Events ---
    public event Action<ThrusterDirection, bool> OnThrustStateChanged;
    public event Action<bool> OnBoostChanged;

    private readonly Dictionary<ThrusterDirection, bool> thrustStates = new()
    {
        { ThrusterDirection.Forward, false },
        { ThrusterDirection.Backward, false },
        { ThrusterDirection.Right, false },
        { ThrusterDirection.Left, false },
        { ThrusterDirection.Up, false },
        { ThrusterDirection.Down, false },
    };

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;

        // Every ship -- player or AI -- bounces off asteroids on collision. Whether it also takes
        // damage from the impact depends on whether it has a Health component: CollisionDamage
        // deliberately doesn't add one itself, so a ship with no Health (e.g. the player's, until
        // you wire up a game-over/health system for it) is immune to impact damage but still
        // physically collides normally. AI ships get Health anyway, from AIWanderPilot below.
        if (!TryGetComponent<CollisionDamage>(out var collisionDamage)) collisionDamage = gameObject.AddComponent<CollisionDamage>();
        collisionDamage.bounceOffAsteroids = true;

        // An AIWanderPilot on this object means it drives moveInput/rollInput/aim itself via the
        // Set*() methods below, so the Input System never gets wired up or enabled for this ship.
        IsPlayerControlled = GetComponent<AIWanderPilot>() == null;
        if (!IsPlayerControlled) return;

        controls = new PlayerControls();

        controls.Ship.Move.performed += ctx => moveInput = ctx.ReadValue<Vector3>();
        controls.Ship.Move.canceled += ctx => moveInput = Vector3.zero;

        controls.Ship.Roll.performed += ctx => rollInput = ctx.ReadValue<float>();
        controls.Ship.Roll.canceled += ctx => rollInput = 0f;

        controls.Ship.Boost.performed += ctx => { isBoosting = true; OnBoostChanged?.Invoke(true); };
        controls.Ship.Boost.canceled += ctx => { isBoosting = false; OnBoostChanged?.Invoke(false); };

        // We no longer read controls.Ship.Look, because we track the raw mouse position instead!
    }

    void OnEnable()
    {
        if (!IsPlayerControlled) return;

        controls.Enable();
        // CONFINED prevents the mouse from leaving the game window onto a second monitor
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        if (!IsPlayerControlled) return;

        controls.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // --- AI control surface: an AIWanderPilot (or any other non-player driver) calls these
    // instead of the Input System touching the fields directly, so ApplyThrust/ApplyRotation/
    // CheckThrustStates and every event they fire behave identically either way. ---
    public void SetMoveInput(Vector3 input) => moveInput = input;
    public void SetRollInput(float input) => rollInput = input;
    public void SetAimIntent(Vector2 intent) => aiAimIntent = intent;

    public void SetBoosting(bool boosting)
    {
        if (isBoosting == boosting) return;
        isBoosting = boosting;
        OnBoostChanged?.Invoke(boosting);
    }

    void Update()
    {
        CalculateVirtualJoystick();
    }

    void FixedUpdate()
    {
        ApplyThrust();
        ApplyRotation();
        CheckThrustStates();
    }

    private void CalculateVirtualJoystick()
    {
        // Raw intent: x = yaw, y = pitch, both in [-1, 1] and zeroed inside the deadzone.
        // Player ships derive this from the mouse; AI ships get it pushed in via SetAimIntent().
        Vector2 intent = IsPlayerControlled ? VirtualJoystickMath.CalculateRawIntent(deadzoneRadius) : aiAimIntent;
        RawAimIntent = intent;

        // Smoothly ease the current turning intent toward the target
        currentYaw = Mathf.Lerp(currentYaw, intent.x, Time.deltaTime * turnSmoothing);
        currentPitch = Mathf.Lerp(currentPitch, intent.y, Time.deltaTime * turnSmoothing);
    }

    private void ApplyThrust()
    {
        float zThrust = moveInput.z;
        float currentForwardThrust = forwardThrust;

        if (isBoosting)
        {
            zThrust = 1f;
            float multiplier = 1f + (boostPercentage / 100f);
            currentForwardThrust *= multiplier;
        }

        Vector3 appliedThrust = new (
            moveInput.x * lateralThrust,
            moveInput.y * verticalThrust,
            zThrust * currentForwardThrust
        );

        rb.AddRelativeForce(appliedThrust, ForceMode.Acceleration);
    }

    private void ApplyRotation()
    {
        float pitchInversion = invertMouseY ? 1f : -1f;

        // We now use our smoothed currentPitch and currentYaw to turn the ship!
        Vector3 appliedTorque = new (
            currentPitch * pitchSpeed * pitchInversion,
            currentYaw * yawSpeed,
            rollInput * rollSpeed
        );

        rb.AddRelativeTorque(appliedTorque, ForceMode.Acceleration);
    }

    private void CheckThrustStates()
    {
        // Because the new VJoy is smoothed, we check if the active rotation intent is > 0.1
        float intentThreshold = 0.1f;

        float pitchIntent = invertMouseY ? -currentPitch : currentPitch;
        float yawIntent = currentYaw;

        bool isForward = moveInput.z > 0.1f || isBoosting;
        bool isBackward = moveInput.z < -0.1f && !isBoosting;

        // If the player is strafing OR the ship is actively using torque to chase the crosshair
        bool isRight = moveInput.x > 0.1f || yawIntent > intentThreshold;
        bool isLeft = moveInput.x < -0.1f || yawIntent < -intentThreshold;

        bool isUp = moveInput.y > 0.1f || pitchIntent > intentThreshold;
        bool isDown = moveInput.y < -0.1f || pitchIntent < -intentThreshold;

        SetThrustState(ThrusterDirection.Forward, isForward);
        SetThrustState(ThrusterDirection.Backward, isBackward);
        SetThrustState(ThrusterDirection.Right, isRight);
        SetThrustState(ThrusterDirection.Left, isLeft);
        SetThrustState(ThrusterDirection.Up, isUp);
        SetThrustState(ThrusterDirection.Down, isDown);
    }

    private void SetThrustState(ThrusterDirection direction, bool isActive)
    {
        if (thrustStates[direction] == isActive) return;

        thrustStates[direction] = isActive;
        OnThrustStateChanged?.Invoke(direction, isActive);
    }
}