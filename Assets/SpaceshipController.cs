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

    [Header("Rotation Speeds")]
    public float pitchSpeed = 5f;
    public float yawSpeed = 5f;
    public float rollSpeed = 40f;
    public bool invertMouseY = false;

    private Vector3 moveInput;
    private Vector2 lookInput;
    private float rollInput;

    // --- NEW: The 6 Directional Events ---
    public event Action<bool> OnForwardThrust;
    public event Action<bool> OnBackwardThrust;
    public event Action<bool> OnRightThrust;
    public event Action<bool> OnLeftThrust;
    public event Action<bool> OnUpThrust;
    public event Action<bool> OnDownThrust;

    // State trackers to ensure we only fire events when a state actually changes
    private bool stateForward, stateBackward, stateRight, stateLeft, stateUp, stateDown;

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

        controls.Ship.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        controls.Ship.Look.canceled += ctx => lookInput = Vector2.zero;
    }

    void OnEnable()
    {
        controls.Enable();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        controls.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void FixedUpdate()
    {
        ApplyThrust();
        ApplyRotation();
        CheckThrustStates(); // Triggers the particle events
    }

    private void ApplyThrust()
    {
        Vector3 appliedThrust = new Vector3(
            moveInput.x * lateralThrust,
            moveInput.y * verticalThrust,
            moveInput.z * forwardThrust
        );
        rb.AddRelativeForce(appliedThrust, ForceMode.Acceleration);
    }

    private void ApplyRotation()
    {
        float pitchInversion = invertMouseY ? 1f : -1f;
        Vector3 appliedTorque = new Vector3(
            lookInput.y * pitchSpeed * pitchInversion,
            lookInput.x * yawSpeed,
            rollInput * rollSpeed
        );
        rb.AddRelativeTorque(appliedTorque, ForceMode.Acceleration);
    }

    // --- NEW: Event Broadcasting Logic ---
    // --- UPDATED: Event Broadcasting Logic ---
    private void CheckThrustStates()
    {
        // 1. Mouse delta can be noisy. This threshold prevents thrusters 
        // from flickering due to microscopic mouse movements.
        float lookThreshold = 0.5f;

        // Calculate pitch intent while respecting your Invert Mouse setting
        float pitchIntent = invertMouseY ? -lookInput.y : lookInput.y;
        float yawIntent = lookInput.x;

        // 2. Evaluate current inputs, combining Translation (WASD) and Rotation (Mouse)
        bool isForward = moveInput.z > 0.1f;
        bool isBackward = moveInput.z < -0.1f;

        // Moving Right (D) OR Looking Right (Mouse X+) triggers the Right intent
        // (You will assign this to the LEFT thruster in the Inspector)
        bool isRight = moveInput.x > 0.1f || yawIntent > lookThreshold;

        // Moving Left (A) OR Looking Left (Mouse X-) triggers the Left intent
        // (You will assign this to the RIGHT thruster in the Inspector)
        bool isLeft = moveInput.x < -0.1f || yawIntent < -lookThreshold;

        // Moving Up (Q) OR Looking Up (Pitch+) triggers the Up intent
        // (You will assign this to the BOTTOM thruster in the Inspector)
        bool isUp = moveInput.y > 0.1f || pitchIntent > lookThreshold;

        // Moving Down (E) OR Looking Down (Pitch-) triggers the Down intent
        // (You will assign this to the TOP thruster in the Inspector)
        bool isDown = moveInput.y < -0.1f || pitchIntent < -lookThreshold;

        // 3. Fire events ONLY if the state has changed this frame
        if (isForward != stateForward) { stateForward = isForward; OnForwardThrust?.Invoke(stateForward); }
        if (isBackward != stateBackward) { stateBackward = isBackward; OnBackwardThrust?.Invoke(stateBackward); }
        if (isRight != stateRight) { stateRight = isRight; OnRightThrust?.Invoke(stateRight); }
        if (isLeft != stateLeft) { stateLeft = isLeft; OnLeftThrust?.Invoke(stateLeft); }
        if (isUp != stateUp) { stateUp = isUp; OnUpThrust?.Invoke(stateUp); }
        if (isDown != stateDown) { stateDown = isDown; OnDownThrust?.Invoke(stateDown); }
    }
}