using System.Collections.Generic;
using UnityEngine;

// Simple combat state machine layered on top of reactive steering. Patrol wanders in a
// slowly-drifting random direction; Attack/CreateDistance/Escape all steer toward or away from
// Target instead. Whichever state is active, obstacle avoidance and field-bounds steering are
// always blended in, and the result is fed through SpaceshipController's aim/move inputs instead
// of touching the Rigidbody directly, so this is all you need: duplicate a player ship, add this
// component, done — SpaceshipController detects it and stops reading the Input System, and
// everything downstream that already reacts to it (thruster VFX via OnThrustStateChanged,
// SpaceshipVisualGimbal's tilt, camera punch) keeps working unchanged.
[RequireComponent(typeof(SpaceshipController))]
public class AIWanderPilot : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Ship to treat as 'the player' for Attack/Create Distance/Escape. Auto-found in the scene (the SpaceshipController with IsPlayerControlled true) if left blank.")]
    public Transform target;

    [Header("Combat State Machine")]
    [Tooltip("Random range (seconds) a state stays active before a new one is rolled.")]
    public float minStateDuration = 10f;
    public float maxStateDuration = 15f;
    [Tooltip("Health fraction (0-1) below which this ship forces itself into Escape, overriding whatever state it's in, regardless of the random rotation.")]
    [Range(0f, 1f)] public float escapeHealthThreshold = 0.25f;
    [Tooltip("In Attack, how close to get to the target before holding rather than continuing to close in.")]
    public float attackStandoffDistance = 150f;

    [Header("Attack Targeting")]
    [Tooltip("Tag used to find candidate aim points on the target ship's model (e.g. hull, wings, engines).")]
    public string shipPartTag = "ShipPart";
    [Tooltip("Random offset (world units), applied independently on each axis, away from the chosen part's position at the start of an engagement.")]
    public float aimOffsetRange = 5f;
    [Tooltip("How long (seconds) the weapon aim point takes to converge from that offset back to the chosen part's center. Smooths out laser jitter versus tracking the target's exact, fast-changing-at-close-range position directly.")]
    public float aimConvergeDuration = 4f;
    [Tooltip("Dot product between facing and direction-to-target required (alongside Attack Standoff Distance) before starting a fresh randomized approach toward the target. 1 = dead on, 0 = 90 degrees off.")]
    [Range(-1f, 1f)] public float attackFacingThreshold = 0.7f;

    public NPCState CurrentState { get; private set; } = NPCState.Patrol;

    // Escape is reactive-only (forced by low health), never picked by the random rotation.
    private static readonly NPCState[] RotationStates = { NPCState.Patrol, NPCState.Attack, NPCState.CreateDistance };

    [Header("Movement")]
    [Range(0f, 1f)] public float forwardThrottle = 1f;

    [Header("Wander (Patrol)")]
    [Tooltip("How often (seconds) a new random wander direction is picked.")]
    public float wanderInterval = 4f;
    [Tooltip("How far the new wander direction can deviate from the current facing, in degrees.")]
    public float wanderAngleRange = 60f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Radius of the probe cast ahead to detect asteroids.")]
    public float probeRadius = 20f;
    [Tooltip("How far ahead to look for obstacles.")]
    public float probeDistance = 200f;
    public LayerMask obstacleMask = ~0;
    [Tooltip("How strongly avoidance overrides steering as obstacles get closer.")]
    public float avoidanceStrength = 2f;
    [Tooltip("How much forward throttle is cut when something is dead ahead, so there's time to turn away.")]
    [Range(0f, 1f)] public float brakingStrength = 0.7f;

    [Header("Roll")]
    [Tooltip("Random range (seconds) of quiet time between roll maneuvers.")]
    public float minRollInterval = 3f;
    public float maxRollInterval = 8f;
    [Tooltip("How long each roll maneuver lasts, in seconds.")]
    public float rollDuration = 1.5f;
    [Tooltip("Roll input strength during a maneuver (direction is picked randomly each time).")]
    [Range(0f, 1f)] public float rollAmount = 1f;

    [Header("Field Bounds")]
    [Tooltip("The asteroid field this pilot must stay inside. Auto-found in the scene if left blank.")]
    public AsteroidFieldGenerator field;
    [Tooltip("Start steering back toward the field center once this far out, as a fraction of the field's outer radius.")]
    [Range(0f, 1f)] public float boundaryMargin = 0.85f;
    [Tooltip("How strongly the pull back toward center overrides other steering near the edge.")]
    public float boundaryStrength = 5f;

    private SpaceshipController shipController;
    private ShipWeaponManager weaponManager;
    private Rigidbody rb;
    private Health health;
    private readonly List<Transform> targetShipParts = new();

    private float stateTimer;
    private Vector3 wanderDirection;
    private float wanderTimer;
    private bool isRolling;
    private float rollSign;
    private float rollStateTimer;
    private bool isConvergingOnTarget;
    private Transform aimPart;
    private Vector3 aimOffset;
    private float aimConvergeElapsed;

    void Awake()
    {
        shipController = GetComponent<SpaceshipController>();
        weaponManager = GetComponent<ShipWeaponManager>();
        rb = GetComponent<Rigidbody>();
        if (!TryGetComponent<Health>(out health)) health = gameObject.AddComponent<Health>();

        wanderDirection = transform.forward;
        rollStateTimer = Random.Range(minRollInterval, maxRollInterval);
        stateTimer = Random.Range(minStateDuration, maxStateDuration);
    }

    void Start()
    {
        // Deferred to Start(): FindPlayerTarget() depends on the player ship's
        // SpaceshipController.IsPlayerControlled, which that ship only sets in its own Awake().
        // Unity doesn't guarantee Awake() order across different GameObjects, so doing this here
        // instead guarantees every ship's Awake() (including the player's) has already run.
        if (field == null) field = FindFirstObjectByType<AsteroidFieldGenerator>();
        if (target == null) target = FindPlayerTarget();

        if (target != null)
        {
            foreach (Transform part in target.GetComponentsInChildren<Transform>(true))
            {
                if (part.CompareTag(shipPartTag)) targetShipParts.Add(part);
            }
        }
    }

    void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
    }

    void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
    }

    private Transform FindPlayerTarget()
    {
        foreach (SpaceshipController ship in FindObjectsByType<SpaceshipController>(FindObjectsSortMode.None))
        {
            if (ship.IsPlayerControlled) return ship.transform;
        }

        return null;
    }

    // Getting shot while calmly patrolling should snap straight into Attack. Other states already
    // represent some kind of reaction (or Escape, which already takes priority via health), so
    // this only fires from Patrol.
    private void HandleDamaged(float amount)
    {
        if (CurrentState == NPCState.Patrol) EnterState(NPCState.Attack);
    }

    void FixedUpdate()
    {
        UpdateStateMachine();

        if (CurrentState == NPCState.Patrol) UpdateWanderDirection();
        UpdateRoll();

        Vector3 avoidance = ComputeObstacleAvoidance(out float closeness);
        Vector3 goalDirection = ComputeGoalDirection();
        Vector3 desiredDirection = (goalDirection + avoidance + ComputeBoundarySteering()).normalized;

        // Same steering channel a player's mouse feeds: x/y in [-1, 1] for how far off-center
        // (yaw/pitch) the target direction sits relative to the ship's current facing.
        Vector3 localDesired = transform.InverseTransformDirection(desiredDirection);
        Vector2 aimIntent = new(Mathf.Clamp(localDesired.x, -1f, 1f), Mathf.Clamp(localDesired.y, -1f, 1f));
        shipController.SetAimIntent(aimIntent);
        shipController.SetRollInput(isRolling ? rollSign * rollAmount : 0f);
        shipController.SetMoveInput(new Vector3(0f, 0f, ComputeThrottle(closeness)));

        if (weaponManager != null)
        {
            bool wantsToFire = CurrentState == NPCState.Attack && IsFacingTarget();
            Vector3 aimPoint = wantsToFire ? ComputeAttackAimPoint() : (target != null ? target.position : Vector3.zero);
            weaponManager.SetAIFiring(aimPoint, wantsToFire);
        }

        ClampToFieldBounds();
    }

    // Critically low health always forces (and holds) Escape, overriding the random rotation
    // entirely. Otherwise, once the current state's timer runs out, roll a new one at random.
    private void UpdateStateMachine()
    {
        if (health.HealthFraction < escapeHealthThreshold)
        {
            if (CurrentState != NPCState.Escape) EnterState(NPCState.Escape);
            return;
        }

        stateTimer -= Time.fixedDeltaTime;
        if (stateTimer > 0f) return;

        EnterState(PickRandomRotationState());
    }

    private void EnterState(NPCState newState)
    {
        CurrentState = newState;
        stateTimer = Random.Range(minStateDuration, maxStateDuration);
        shipController.SetBoosting(newState == NPCState.Escape);

        // Leaving Attack (or getting cut short mid-approach) means the next engagement should
        // start a fresh randomized approach rather than resuming mid-convergence.
        if (newState != NPCState.Attack) isConvergingOnTarget = false;
    }

    private bool IsFacingTarget()
    {
        if (target == null) return false;

        Vector3 toTarget = (target.position - transform.position).normalized;
        return Vector3.Dot(transform.forward, toTarget) >= attackFacingThreshold;
    }

    // Instead of aiming straight at the target's exact (fast-changing at close range) position,
    // pick a random part of its ship model, offset randomly away from it, and smoothly converge
    // back to that part's center over aimConvergeDuration once in range and roughly facing it —
    // this is what actually removes the jitter, since the aim point now moves in a slow,
    // continuous, predictable way instead of snapping to a volatile exact point every frame.
    private Vector3 ComputeAttackAimPoint()
    {
        if (targetShipParts.Count == 0) return target.position;

        if (!isConvergingOnTarget)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            bool inRange = distance <= attackStandoffDistance;

            if (!inRange || !IsFacingTarget()) return target.position;

            aimPart = targetShipParts[Random.Range(0, targetShipParts.Count)];
            aimOffset = new Vector3(
                Random.Range(-aimOffsetRange, aimOffsetRange),
                Random.Range(-aimOffsetRange, aimOffsetRange),
                Random.Range(-aimOffsetRange, aimOffsetRange));
            aimConvergeElapsed = 0f;
            isConvergingOnTarget = true;
        }

        // The chosen part may have been shot off/destroyed mid-engagement; fall back gracefully.
        if (aimPart == null) return target.position;

        aimConvergeElapsed += Time.fixedDeltaTime;
        float t = aimConvergeDuration > 0f ? Mathf.Clamp01(aimConvergeElapsed / aimConvergeDuration) : 1f;
        Vector3 currentOffset = Vector3.Lerp(aimOffset, Vector3.zero, t);

        return aimPart.position + currentOffset;
    }

    private NPCState PickRandomRotationState()
    {
        NPCState next;
        do
        {
            next = RotationStates[Random.Range(0, RotationStates.Length)];
        } while (next == CurrentState);

        return next;
    }

    private Vector3 ComputeGoalDirection()
    {
        switch (CurrentState)
        {
            case NPCState.Attack:
                if (target == null) return wanderDirection;
                Vector3 toTarget = target.position - transform.position;
                return toTarget.magnitude > attackStandoffDistance ? toTarget.normalized : transform.forward;

            case NPCState.CreateDistance:
            case NPCState.Escape:
                return target == null ? wanderDirection : (transform.position - target.position).normalized;

            case NPCState.Patrol:
            default:
                return wanderDirection;
        }
    }

    private float ComputeThrottle(float obstacleCloseness)
    {
        // Close enough in Attack: hold rather than ramming the target.
        if (CurrentState == NPCState.Attack && target != null && Vector3.Distance(transform.position, target.position) <= attackStandoffDistance)
        {
            return 0f;
        }

        float baseThrottle = CurrentState == NPCState.Escape ? 1f : forwardThrottle;
        return baseThrottle * (1f - obstacleCloseness * brakingStrength);
    }

    private void UpdateWanderDirection()
    {
        wanderTimer -= Time.fixedDeltaTime;
        if (wanderTimer > 0f) return;

        wanderTimer = wanderInterval;

        Quaternion randomOffset = Quaternion.Euler(
            Random.Range(-wanderAngleRange, wanderAngleRange),
            Random.Range(-wanderAngleRange, wanderAngleRange),
            0f);

        wanderDirection = (randomOffset * transform.forward).normalized;
    }

    private void UpdateRoll()
    {
        rollStateTimer -= Time.fixedDeltaTime;
        if (rollStateTimer > 0f) return;

        if (isRolling)
        {
            isRolling = false;
            rollStateTimer = Random.Range(minRollInterval, maxRollInterval);
        }
        else
        {
            isRolling = true;
            rollSign = Random.value < 0.5f ? -1f : 1f;
            rollStateTimer = rollDuration;
        }
    }

    private Vector3 ComputeObstacleAvoidance(out float closeness)
    {
        closeness = 0f;
        Vector3 avoidance = Vector3.zero;

        if (Physics.SphereCast(transform.position, probeRadius, transform.forward, out RaycastHit hit, probeDistance, obstacleMask))
        {
            closeness = 1f - Mathf.Clamp01(hit.distance / probeDistance);

            // Deflect sideways/up around the obstacle rather than straight back, so the ship
            // keeps moving instead of stalling out facing the thing it's avoiding.
            Vector3 away = (transform.position - hit.point).normalized;
            Vector3 deflect = Vector3.ProjectOnPlane(away, transform.forward).normalized;
            if (deflect == Vector3.zero) deflect = transform.right;

            avoidance = deflect * closeness * avoidanceStrength;
        }

        return avoidance;
    }

    // Soft pull back toward the field's center once close to the outer radius, so the ship turns
    // back naturally instead of visibly bouncing off an invisible wall.
    private Vector3 ComputeBoundarySteering()
    {
        if (field == null) return Vector3.zero;

        Vector3 toCenter = field.FieldCenter - transform.position;
        float distance = toCenter.magnitude;
        float boundaryStart = field.outerRadius * boundaryMargin;
        if (distance <= boundaryStart) return Vector3.zero;

        float pastMargin = Mathf.Clamp01((distance - boundaryStart) / Mathf.Max(field.outerRadius - boundaryStart, 0.01f));
        return toCenter.normalized * pastMargin * boundaryStrength;
    }

    // Hard fail-safe: guarantees the ship never actually leaves the field's outer radius even if
    // momentum ever carries it past the boundary faster than it can steer back.
    private void ClampToFieldBounds()
    {
        if (field == null) return;

        Vector3 offset = rb.position - field.FieldCenter;
        if (offset.magnitude <= field.outerRadius) return;

        rb.position = field.FieldCenter + offset.normalized * field.outerRadius;

        Vector3 outwardVelocity = Vector3.Project(rb.linearVelocity, offset.normalized);
        if (Vector3.Dot(outwardVelocity, offset) > 0f) rb.linearVelocity -= outwardVelocity;
    }
}
