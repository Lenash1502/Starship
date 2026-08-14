using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraThrustOffset : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The main spaceship's controller. Auto-found on the parent if left blank.")]
    public SpaceshipController shipController;

    [Header("Position Offset Settings")]
    [Tooltip("How far the camera pushes back when thrusting forward.")]
    public Vector3 normalThrustOffset = new Vector3(0f, 0f, -2f);

    [Tooltip("How far the camera pushes back when boosting.")]
    public Vector3 boostThrustOffset = new Vector3(0f, 0f, -6f);

    [Tooltip("How far the camera pushes FORWARD when reversing. Use positive Z values.")]
    public Vector3 reverseThrustOffset = new Vector3(0f, 0f, 2f);

    [Tooltip("How quickly the camera slides to the offset and back to 0.")]
    public float positionSmoothTime = 0.3f;

    [Header("FOV Settings (Sense of Speed)")]
    [Tooltip("The camera's normal Field of View.")]
    public float normalFOV = 60f;

    [Tooltip("The camera's FOV when boosting (zooms out for warp effect).")]
    public float boostFOV = 75f;

    [Tooltip("The camera's FOV when reversing (zooms in slightly for braking effect).")]
    public float reverseFOV = 55f;

    [Tooltip("How quickly the FOV zooms in and out.")]
    public float fovSmoothTime = 0.2f;

    private Camera cam;
    private Vector3 defaultLocalPosition;
    private Vector3 positionVelocity;
    private float fovVelocity;

    private bool isThrusting;
    private bool isReversing;
    private bool isBoosting;

    void Start()
    {
        cam = GetComponent<Camera>();
        cam.fieldOfView = normalFOV;

        // Memorize the exact spot you placed the camera in the Unity Editor
        defaultLocalPosition = transform.localPosition;

        if (shipController == null) shipController = GetComponentInParent<SpaceshipController>();
    }

    void OnEnable()
    {
        if (shipController == null) return;

        shipController.OnThrustStateChanged += HandleThrustStateChanged;
        shipController.OnBoostChanged += HandleBoostChanged;
    }

    void OnDisable()
    {
        if (shipController == null) return;

        shipController.OnThrustStateChanged -= HandleThrustStateChanged;
        shipController.OnBoostChanged -= HandleBoostChanged;
    }

    private void HandleThrustStateChanged(ThrusterDirection direction, bool isActive)
    {
        if (direction == ThrusterDirection.Forward) isThrusting = isActive;
        else if (direction == ThrusterDirection.Backward) isReversing = isActive;
    }

    private void HandleBoostChanged(bool isActive)
    {
        isBoosting = isActive;
    }

    void LateUpdate()
    {
        // 1. Start with the default assumptions (no input = 0 offset, normal FOV)
        Vector3 targetOffset = Vector3.zero;
        float targetFOV = normalFOV;

        // 2. Determine our targets based on priority
        if (isBoosting)
        {
            // Boost overrides everything else
            targetOffset = boostThrustOffset;
            targetFOV = boostFOV;
        }
        else if (isThrusting)
        {
            targetOffset = normalThrustOffset;
        }
        else if (isReversing)
        {
            // Reversing pushes the camera the opposite way
            targetOffset = reverseThrustOffset;
            targetFOV = reverseFOV;
        }

        // 3. Calculate the final target local position
        Vector3 targetLocalPosition = defaultLocalPosition + targetOffset;

        // 4. Smoothly slide the camera's local position
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            targetLocalPosition,
            ref positionVelocity,
            positionSmoothTime
        );

        // 5. Smoothly zoom the FOV 
        cam.fieldOfView = Mathf.SmoothDamp(
            cam.fieldOfView,
            targetFOV,
            ref fovVelocity,
            fovSmoothTime
        );
    }
}