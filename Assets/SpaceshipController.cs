using UnityEngine;
using UnityEngine.InputSystem;
using System;

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
    private bool isFiring;
    private Vector3 moveInput;
    private float rollInput;

    // VJoy tracking variables
    private float currentPitch;
    private float currentYaw;

    // --- The Events ---
    public event Action<bool> OnForwardThrust;
    public event Action<bool> OnBackwardThrust;
    public event Action<bool> OnRightThrust;
    public event Action<bool> OnLeftThrust;
    public event Action<bool> OnUpThrust;
    public event Action<bool> OnDownThrust;
    public event Action<bool> OnFireLaser;

    private bool stateForward, stateBackward, stateRight, stateLeft, stateUp, stateDown, stateFire;

    void Awake()
    {
        controls = new PlayerControls();
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;

        controls.Ship.Move.performed += ctx => moveInput = ctx.ReadValue<Vector3>();
        controls.Ship.Move.canceled += ctx => moveInput = Vector3.zero;

        controls.Ship.Roll.performed += ctx => rollInput = ctx.ReadValue<float>();
        controls.Ship.Roll.canceled += ctx => rollInput = 0f;

        controls.Ship.Boost.performed += ctx => isBoosting = true;
        controls.Ship.Boost.canceled += ctx => isBoosting = false;

        controls.Ship.PrimaryWeapon.performed += ctx => isFiring = true;
        controls.Ship.PrimaryWeapon.canceled += ctx => isFiring = false;

        // We no longer read controls.Ship.Look, because we track the raw mouse position instead!
    }

    void OnEnable()
    {
        controls.Enable();
        // CONFINED prevents the mouse from leaving the game window onto a second monitor
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        controls.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
        CheckWeaponStates();
    }

    private void CalculateVirtualJoystick()
    {
        if (Mouse.current == null) return;

        // 1. Find the center of the screen
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // 2. Find where the mouse is
        Vector2 mousePos = Mouse.current.position.ReadValue();

        // 3. Calculate the offset and normalize it so it scales perfectly regardless of resolution
        Vector2 offset = mousePos - screenCenter;
        Vector2 normalizedOffset = new Vector2(offset.x / screenCenter.x, offset.y / screenCenter.y);

        float distance = normalizedOffset.magnitude;
        float targetYaw = 0f;
        float targetPitch = 0f;

        // 4. If the mouse is outside the deadzone, calculate how fast we should turn
        if (distance > deadzoneRadius)
        {
            // This math makes the turning start at 0 right at the edge of the deadzone, 
            // and ramp up to max speed as you reach the edge of the screen.
            float activeAmount = (distance - deadzoneRadius) / (1f - deadzoneRadius);
            Vector2 direction = normalizedOffset.normalized;

            targetYaw = direction.x * activeAmount;
            targetPitch = direction.y * activeAmount;

            // Clamp just to be safe if the cursor gets temporarily dragged off-screen
            targetYaw = Mathf.Clamp(targetYaw, -1f, 1f);
            targetPitch = Mathf.Clamp(targetPitch, -1f, 1f);
        }

        // 5. Smoothly ease the current turning intent toward the target 
        currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * turnSmoothing);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * turnSmoothing);
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

        Vector3 appliedThrust = new Vector3(
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
        Vector3 appliedTorque = new Vector3(
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

        if (isForward != stateForward) { stateForward = isForward; OnForwardThrust?.Invoke(stateForward); }
        if (isBackward != stateBackward) { stateBackward = isBackward; OnBackwardThrust?.Invoke(stateBackward); }
        if (isRight != stateRight) { stateRight = isRight; OnRightThrust?.Invoke(stateRight); }
        if (isLeft != stateLeft) { stateLeft = isLeft; OnLeftThrust?.Invoke(stateLeft); }
        if (isUp != stateUp) { stateUp = isUp; OnUpThrust?.Invoke(stateUp); }
        if (isDown != stateDown) { stateDown = isDown; OnDownThrust?.Invoke(stateDown); }
    }

    private void CheckWeaponStates()
    {
        if (isFiring != stateFire)
        {
            stateFire = isFiring;
            OnFireLaser?.Invoke(stateFire);
        }
    }
}