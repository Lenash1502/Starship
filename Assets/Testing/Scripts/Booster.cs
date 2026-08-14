using UnityEngine;

// A booster fires at full force along its own mounted direction (transform.forward) whenever
// told to -- it doesn't know or declare which way that "means" (forward/up/etc). No tag needed:
// TestShipThrusterController discovers each booster's real effect on the ship empirically (see
// CalibrateBoosters) by actually firing it and measuring the result, rather than trusting a
// naming convention or hand-derived torque formula. So a booster can be placed and rotated
// however the ship design calls for.
public class Booster : ShipPart
{
    [Header("Booster Stats")]
    [Tooltip("Force this booster produces while firing, in Newtons.")]
    public float thrustForce = 1000f;

    [Tooltip("Not used yet -- reserved for a future power/fuel system.")]
    public float energyConsumption = 0f;

    [Tooltip("Set by the ship controller each physics step, purely for inspection -- doesn't drive anything itself.")]
    [HideInInspector] public bool isFiring;

    private Rigidbody shipRigidbody;

    protected override void Awake()
    {
        base.Awake();

        // Resolved in Awake, not Start: TestShipThrusterController fires every booster from its
        // own Start() to calibrate them, and Unity only guarantees that ALL Awakes finish before
        // ANY Start runs -- not that a child's Start runs before its parent's.
        shipRigidbody = GetComponentInParent<Rigidbody>();
        if (shipRigidbody == null)
            Debug.LogWarning($"{name}: no Rigidbody found in parents -- this booster can't apply force.", this);
    }

    // Fires at full thrust along the booster's own current forward direction.
    public void Fire()
    {
        isFiring = true;
        if (shipRigidbody == null) return;
        shipRigidbody.AddForceAtPosition(transform.forward * thrustForce, transform.position, ForceMode.Force);
    }
}
