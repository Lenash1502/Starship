using UnityEngine;

public class SpaceshipVisualGimbal : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Rigidbody of the main parent spaceship.")]
    public Rigidbody shipRigidbody;
    [Tooltip("The main spaceship's controller. Auto-found on the parent if left blank.")]
    public SpaceshipController shipController;

    [Header("Virtual Joystick Aiming")]
    [Tooltip("How far the nose tilts Up/Down when the crosshair is at the edge of the screen.")]
    public float maxAimPitch = 15f;

    [Tooltip("How far the nose tilts Left/Right when the crosshair is at the edge of the screen.")]
    public float maxAimYaw = 20f;

    public bool invertMouseY = false;

    [Header("Physical Bank (Roll) Settings")]
    [Tooltip("The maximum angle the ship can roll/bank physically.")]
    public float maxBankAngle = 35f;
    [Tooltip("How strongly the ship banks when turning left/right.")]
    public float turnBankMultiplier = 15f;
    [Tooltip("How strongly the ship banks when strafing left/right.")]
    public float strafeBankMultiplier = 0.5f;

    // --- NEW VARIABLE ---
    [Tooltip("Caps the physics drift influence so high-speed boosting doesn't invert the roll.")]
    public float maxStrafeVelocity = 20f;

    [Header("Global Settings")]
    [Tooltip("How fast the visual model chases the crosshair and springs back.")]
    public float tiltSmoothSpeed = 10f;

    private Quaternion initialLocalRotation;

    void Start()
    {
        initialLocalRotation = transform.localRotation;
        if (shipRigidbody == null) shipRigidbody = GetComponentInParent<Rigidbody>();
        if (shipController == null) shipController = GetComponentInParent<SpaceshipController>();
    }

    void Update()
    {
        if (shipRigidbody == null || shipController == null) return;

        // --------------------------------------------------------
        // 1. CALCULATE AIM (PITCH & YAW) FROM CROSSHAIR
        // --------------------------------------------------------
        // Raw intent: x = yaw, y = pitch, both in [-1, 1]. Comes from the mouse for a player ship
        // or from AIWanderPilot for an AI one — SpaceshipController is the single source either way.
        Vector2 intent = shipController.RawAimIntent;

        float pitchSign = invertMouseY ? 1f : -1f;

        float targetYaw = Mathf.Clamp(intent.x * maxAimYaw, -maxAimYaw, maxAimYaw);
        float targetPitch = Mathf.Clamp(pitchSign * intent.y * maxAimPitch, -maxAimPitch, maxAimPitch);

        // --------------------------------------------------------
        // 2. CALCULATE BANK (ROLL) FROM PHYSICS
        // --------------------------------------------------------
        Vector3 localVelocity = shipRigidbody.transform.InverseTransformDirection(shipRigidbody.linearVelocity);
        Vector3 localAngularVel = shipRigidbody.transform.InverseTransformDirection(shipRigidbody.angularVelocity);

        // --- NEW MATH ---
        // Clamp the sideways velocity so extreme momentum can't overpower the steering
        float clampedStrafe = Mathf.Clamp(localVelocity.x, -maxStrafeVelocity, maxStrafeVelocity);

        // Calculate the bank using the clamped strafe instead of raw velocity
        float targetBank = (localAngularVel.y * turnBankMultiplier) + (clampedStrafe * strafeBankMultiplier);
        targetBank = Mathf.Clamp(targetBank, -maxBankAngle, maxBankAngle);

        // --------------------------------------------------------
        // 3. APPLY ROTATION
        // --------------------------------------------------------
        Quaternion targetRotation = initialLocalRotation * Quaternion.Euler(targetPitch, targetYaw, -targetBank);

        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            targetRotation,
            Time.deltaTime * tiltSmoothSpeed
        );
    }
}