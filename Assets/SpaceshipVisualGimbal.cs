using UnityEngine;
using UnityEngine.InputSystem; // Required to read the mouse

public class SpaceshipVisualGimbal : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Rigidbody of the main parent spaceship.")]
    public Rigidbody shipRigidbody;

    [Header("Virtual Joystick Aiming")]
    [Tooltip("Match this EXACTLY to the deadzone in your SpaceshipController.")]
    [Range(0f, 1f)] public float deadzoneRadius = 0.15f;

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

    [Header("Global Settings")]
    [Tooltip("How fast the visual model chases the crosshair and springs back.")]
    public float tiltSmoothSpeed = 10f;

    private Quaternion initialLocalRotation;

    void Start()
    {
        initialLocalRotation = transform.localRotation;
        if (shipRigidbody == null) shipRigidbody = GetComponentInParent<Rigidbody>();
    }

    void Update()
    {
        if (shipRigidbody == null || Mouse.current == null) return;

        // --------------------------------------------------------
        // 1. CALCULATE AIM (PITCH & YAW) FROM CROSSHAIR
        // --------------------------------------------------------
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector2 offset = mousePos - screenCenter;
        Vector2 normalizedOffset = new Vector2(offset.x / screenCenter.x, offset.y / screenCenter.y);

        float distance = normalizedOffset.magnitude;
        float targetPitch = 0f;
        float targetYaw = 0f;

        // If the crosshair leaves the deadzone, aim the nose!
        if (distance > deadzoneRadius)
        {
            float activeAmount = (distance - deadzoneRadius) / (1f - deadzoneRadius);
            Vector2 direction = normalizedOffset.normalized;

            float yIntent = invertMouseY ? -direction.y : direction.y;

            // Map the screen position to our maximum allowed visual angles
            targetYaw = direction.x * activeAmount * maxAimYaw;

            // In Unity, negative X rotation usually pitches the nose UP
            targetPitch = -yIntent * activeAmount * maxAimPitch;

            // Clamp just in case the mouse gets dragged out of the game window
            targetYaw = Mathf.Clamp(targetYaw, -maxAimYaw, maxAimYaw);
            targetPitch = Mathf.Clamp(targetPitch, -maxAimPitch, maxAimPitch);
        }

        // --------------------------------------------------------
        // 2. CALCULATE BANK (ROLL) FROM PHYSICS
        // --------------------------------------------------------
        Vector3 localVelocity = shipRigidbody.transform.InverseTransformDirection(shipRigidbody.linearVelocity);
        Vector3 localAngularVel = shipRigidbody.transform.InverseTransformDirection(shipRigidbody.angularVelocity);

        float targetBank = (localAngularVel.y * turnBankMultiplier) + (localVelocity.x * strafeBankMultiplier);
        targetBank = Mathf.Clamp(targetBank, -maxBankAngle, maxBankAngle);

        // --------------------------------------------------------
        // 3. APPLY ROTATION
        // --------------------------------------------------------
        // Combine the Crosshair Aim (Pitch/Yaw) with the Physics Bank (Roll)
        Quaternion targetRotation = initialLocalRotation * Quaternion.Euler(targetPitch, targetYaw, -targetBank);

        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            targetRotation,
            Time.deltaTime * tiltSmoothSpeed
        );
    }
}