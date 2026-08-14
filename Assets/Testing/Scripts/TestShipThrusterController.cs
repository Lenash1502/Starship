using System.Collections.Generic;
using UnityEngine;

// Testing-folder variant of the flight feel from Scripts/SpaceshipController.cs (mouse-look via
// virtual joystick, WASD/QE translation, Z/C roll), ported to fire boosters instead of applying
// force/torque to the Rigidbody directly. Put this on the ship root next to the Rigidbody.
//
// No booster needs a tag or a declared direction: on Start, CalibrateBoosters() actually fires
// each one alone from a standing start and reads the resulting velocity/angular velocity straight
// off the Rigidbody, so whatever a booster really does -- accounting for the ship's actual mass,
// center of mass and rotational inertia -- is measured rather than assumed. Force and torque are
// linear, so firing several boosters together simply sums their individually-measured effects;
// that's what the runtime allocator uses to decide which subset of boosters to fire each step.
//
// Mouse/key input isn't fed to the allocator as a raw firing direction -- it's a target angular/
// linear velocity (zero when centered/idle). Each step, the allocator is driven by the error
// between that target and the ship's actual current velocity, so it's always actively closing the
// gap: accelerating toward a commanded turn, braking if it overshoots, and settling to a genuine
// stop once idle, rather than firing at full thrust for as long as any input (however small)
// lingers with no regard for how fast the ship is already moving.
//
// Holding X pins both target velocities to zero, so boosters fire to cancel out whatever movement
// and rotation the ship currently has; releasing X hands control straight back to mouse/keys.
[RequireComponent(typeof(Rigidbody))]
public class TestShipThrusterController : MonoBehaviour
{
    [Header("Rotation (Virtual Joystick)")]
    [Tooltip("How far the mouse must move from the center (0 to 1) before the ship turns. 0.2 = 20% of the screen.")]
    [Range(0f, 1f)] public float deadzoneRadius = 0.15f;
    [Tooltip("How smoothly the ship ramps up and slows down its turning intent.")]
    public float turnSmoothing = 5f;
    public bool invertMouseY = false;

    [Header("Allocation")]
    [Tooltip("How much a booster's alignment with the current translation desire counts toward firing it.")]
    public float translationWeight = 1f;
    [Tooltip("How much a booster's alignment with the current rotation desire counts toward firing it.")]
    public float rotationWeight = 1f;
    [Tooltip("Minimum combined alignment score (translationWeight + rotationWeight is the max) before a booster bothers firing.")]
    public float firingThreshold = 0.3f;

    [Header("Rate Control")]
    [Tooltip("Treat mouse/key input as a target velocity to hold (and brake toward, including a full stop) rather than a raw firing direction.")]
    public bool autoStabilizeRotation = true;
    public bool autoStabilizeTranslation = true;
    [Tooltip("Angular speed (rad/s) at full rotation input.")]
    public float maxTurnRate = 2f;
    [Tooltip("Linear speed (m/s) at full translation input.")]
    public float maxTranslateSpeed = 20f;
    [Tooltip("Angular velocity error (rad/s) below which boosters stop correcting rotation.")]
    public float angularDeadzone = 0.02f;
    [Tooltip("Linear velocity error (m/s) below which boosters stop correcting translation.")]
    public float linearDeadzone = 0.05f;

    private Rigidbody rb;
    private Transform shipTransform;
    private Booster[] allBoosters;

    // Each booster's measured effect on the ship (local space), from firing it alone for one
    // physics step starting at zero velocity -- see CalibrateBoosters.
    private Vector3[] calibratedLinearEffect;
    private Vector3[] calibratedAngularEffect;

    private float currentPitch;
    private float currentYaw;

    private static readonly (string Label, Vector3 Translation, Vector3 Rotation)[] RequiredCapabilities =
    {
        ("translate forward",  new Vector3(0, 0, 1),  Vector3.zero),
        ("translate backward", new Vector3(0, 0, -1), Vector3.zero),
        ("translate right",    new Vector3(1, 0, 0),  Vector3.zero),
        ("translate left",     new Vector3(-1, 0, 0), Vector3.zero),
        ("translate up",       new Vector3(0, 1, 0),  Vector3.zero),
        ("translate down",     new Vector3(0, -1, 0), Vector3.zero),
        ("pitch nose up",      Vector3.zero, new Vector3(1, 0, 0)),
        ("pitch nose down",    Vector3.zero, new Vector3(-1, 0, 0)),
        ("yaw nose right",     Vector3.zero, new Vector3(0, 1, 0)),
        ("yaw nose left",      Vector3.zero, new Vector3(0, -1, 0)),
        ("roll right",         Vector3.zero, new Vector3(0, 0, 1)),
        ("roll left",          Vector3.zero, new Vector3(0, 0, -1)),
    };

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        shipTransform = transform;

        allBoosters = GetComponentsInChildren<Booster>();

        float totalWeight = 0f;
        foreach (var part in GetComponentsInChildren<ShipPart>())
            totalWeight += part.weight;
        if (totalWeight > 0f) rb.mass = totalWeight;
    }

    void Start()
    {
        CalibrateBoosters();
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        Vector2 intent = TestVirtualJoystickMath.CalculateRawIntent(deadzoneRadius);
        currentYaw = Mathf.Lerp(currentYaw, intent.x, Time.deltaTime * turnSmoothing);
        currentPitch = Mathf.Lerp(currentPitch, intent.y, Time.deltaTime * turnSmoothing);
    }

    void FixedUpdate()
    {
        // While held, X commands a target velocity of zero directly, so the same braking logic
        // below fires whatever boosters cancel current velocity/spin. Releasing X hands control
        // straight back to mouse/keys, with no separate mode to fall out of.
        bool fullStop = Input.GetKey(KeyCode.X);

        Vector3 translationIntent = fullStop ? Vector3.zero : GetTranslationIntentLocal();
        Vector3 rotationIntent = fullStop ? Vector3.zero : GetRotationIntentLocal();

        Vector3 desiredTranslation;
        if (autoStabilizeTranslation)
        {
            Vector3 targetVelocity = translationIntent * maxTranslateSpeed;
            Vector3 localVelocity = shipTransform.InverseTransformDirection(rb.linearVelocity);
            Vector3 velocityError = targetVelocity - localVelocity;
            desiredTranslation = velocityError.magnitude > linearDeadzone ? velocityError : Vector3.zero;
        }
        else
        {
            desiredTranslation = translationIntent;
        }

        Vector3 desiredRotation;
        if (autoStabilizeRotation)
        {
            Vector3 targetAngularVelocity = rotationIntent * maxTurnRate;
            Vector3 localAngularVelocity = shipTransform.InverseTransformDirection(rb.angularVelocity);
            Vector3 angularVelocityError = targetAngularVelocity - localAngularVelocity;
            desiredRotation = angularVelocityError.magnitude > angularDeadzone ? angularVelocityError : Vector3.zero;
        }
        else
        {
            desiredRotation = rotationIntent;
        }

        for (int i = 0; i < allBoosters.Length; i++)
        {
            if (ScoreBooster(i, desiredTranslation, desiredRotation) > firingThreshold)
                allBoosters[i].Fire();
            else
                allBoosters[i].isFiring = false;
        }
    }

    private Vector3 GetTranslationIntentLocal()
    {
        float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float y = (Input.GetKey(KeyCode.Q) ? 1f : 0f) - (Input.GetKey(KeyCode.E) ? 1f : 0f);
        float z = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        return new Vector3(x, y, z);
    }

    private Vector3 GetRotationIntentLocal()
    {
        float pitchInversion = invertMouseY ? 1f : -1f;
        float roll = (Input.GetKey(KeyCode.Z) ? 1f : 0f) - (Input.GetKey(KeyCode.C) ? 1f : 0f);
        return new Vector3(currentPitch * pitchInversion, currentYaw, roll);
    }

    // How well booster `index` serves the given desire, as a sum of cosine-similarity terms
    // (each in [-1, 1], weighted). Used both live in FixedUpdate and by the Start-time coverage
    // check, so the warning always matches what the allocator can actually do.
    private float ScoreBooster(int index, Vector3 desiredTranslationLocal, Vector3 desiredRotationLocal)
    {
        float score = 0f;

        // Only guards against genuine numerical zeros (e.g. a booster whose torque about this
        // axis is exactly cancelled by its position) -- not "small but real" effects. A weak
        // booster or a heavy ship can easily produce a one-tick calibration velocity well under
        // 1 mm/s, which is still a perfectly valid direction to score against.
        const float calibrationZeroEpsilon = 1e-10f;

        if (desiredTranslationLocal.sqrMagnitude > 0.0001f && calibratedLinearEffect[index].sqrMagnitude > calibrationZeroEpsilon)
            score += Vector3.Dot(calibratedLinearEffect[index].normalized, desiredTranslationLocal.normalized) * translationWeight;

        if (desiredRotationLocal.sqrMagnitude > 0.0001f && calibratedAngularEffect[index].sqrMagnitude > calibrationZeroEpsilon)
            score += Vector3.Dot(calibratedAngularEffect[index].normalized, desiredRotationLocal.normalized) * rotationWeight;

        return score;
    }

    // Fires each booster alone from a standing start and reads the resulting velocity/angular
    // velocity straight off the Rigidbody -- an actual measurement via one manual physics step,
    // not a hand-derived formula. Physics.simulationMode is switched to Script for the duration so
    // this doesn't advance the rest of the scene, and the ship's original motion state is restored
    // afterward so none of this is visible to the player.
    private void CalibrateBoosters()
    {
        calibratedLinearEffect = new Vector3[allBoosters.Length];
        calibratedAngularEffect = new Vector3[allBoosters.Length];

        Vector3 savedPos = rb.position;
        Quaternion savedRot = rb.rotation;
        Vector3 savedVelocity = rb.linearVelocity;
        Vector3 savedAngularVelocity = rb.angularVelocity;

        SimulationMode previousMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        for (int i = 0; i < allBoosters.Length; i++)
        {
            rb.position = savedPos;
            rb.rotation = savedRot;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            allBoosters[i].Fire();
            Physics.Simulate(Time.fixedDeltaTime);

            calibratedLinearEffect[i] = shipTransform.InverseTransformDirection(rb.linearVelocity);
            calibratedAngularEffect[i] = shipTransform.InverseTransformDirection(rb.angularVelocity);
            allBoosters[i].isFiring = false;
        }

        rb.position = savedPos;
        rb.rotation = savedRot;
        rb.linearVelocity = savedVelocity;
        rb.angularVelocity = savedAngularVelocity;
        Physics.simulationMode = previousMode;

        ReportUncoveredCapabilities();
    }

    private void ReportUncoveredCapabilities()
    {
        var missing = new List<string>();

        foreach (var task in RequiredCapabilities)
        {
            bool covered = false;
            for (int i = 0; i < allBoosters.Length; i++)
            {
                if (ScoreBooster(i, task.Translation, task.Rotation) > firingThreshold) { covered = true; break; }
            }
            if (!covered) missing.Add(task.Label);
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning(
                $"{name}: current booster layout can't perform: {string.Join(", ", missing)}. " +
                "Add boosters covering those directions or reposition existing ones.",
                this);
        }
    }
}
