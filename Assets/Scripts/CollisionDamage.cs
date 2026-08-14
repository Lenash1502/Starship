using System;
using UnityEngine;

// Impact damage for ship-vs-asteroid collisions only. Asteroid-vs-asteroid impacts skip this
// entirely and are instead handled by AsteroidBreaker's much simpler chance-to-break-apart rules --
// this component just early-outs when both sides of the collision are asteroids. Damage is
// calibrated off a single designer-facing number (Base Collision Damage, "how much a scale-10
// asteroid's impact hurts at Reference Collision Speed") but actually scales with the OTHER
// object's mass and the real relative impact speed, so what matters is how hard the thing that hit
// you was moving, not how big you are.
//
// The physical bounce/response always resolves regardless. Damage only applies if this object
// already has a Health component -- it's deliberately NOT auto-added, so an object with no Health
// (e.g. a ship you haven't wired up yet) is simply immune to impact damage while still physically
// colliding and bouncing normally. Add or remove Health on the object to toggle its immunity.
[RequireComponent(typeof(Rigidbody))]
public class CollisionDamage : MonoBehaviour
{
    [Header("Collision Damage")]
    [Tooltip("Damage dealt by an impactor of Reference Mass hitting at Reference Collision Speed.")]
    public float baseCollisionDamage = 25f;
    [Tooltip("Relative impact speed (units/sec) Base Collision Damage is calibrated against.")]
    public float referenceCollisionSpeed = 50f;
    [Tooltip("Impactor mass Base Collision Damage is calibrated against. Defaults to a scale-10 asteroid's mass (scale^3 * 0.1, see AsteroidFieldGenerator.ApplyPhysics).")]
    public float referenceMass = 100f;

    [Header("Asteroid Bounce")]
    [Tooltip("Ships: colliding with something on the Asteroid layer pushes this object away opposite the asteroid's travel direction, instead of relying solely on the raw physics response. Leave off for asteroids.")]
    public bool bounceOffAsteroids = false;
    [Tooltip("Strength (units/sec, applied as an instant velocity change) of the corrective bounce above.")]
    public float bounceForce = 30f;

    // Fired on every physical collision (contact point), regardless of whether it dealt damage --
    // a generic "something hit me" signal for bump/scrape sound listeners (see ImpactSound), kept
    // separate from Health.OnDamaged since that only exists when Health is present.
    public event Action<Vector3> OnImpact;

    private Health health;
    private Rigidbody rb;
    private int asteroidLayer;
    private bool isAsteroid;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        TryGetComponent(out health); // left null if absent -- see class comment above
        asteroidLayer = LayerMask.NameToLayer("Asteroid");
        isAsteroid = gameObject.layer == asteroidLayer;
    }

    void OnCollisionEnter(Collision collision)
    {
        bool otherIsAsteroid = collision.gameObject.layer == asteroidLayer;

        OnImpact?.Invoke(collision.GetContact(0).point);

        // Asteroid-vs-asteroid is AsteroidBreaker's job now, not this formula. No Health means
        // this object is immune to impact damage, but it still physically collides/bounces below.
        if (health != null && !(isAsteroid && otherIsAsteroid))
        {
            float relativeSpeed = collision.relativeVelocity.magnitude;
            float impactorMass = collision.rigidbody != null ? collision.rigidbody.mass : 0f;

            if (impactorMass > 0f && relativeSpeed > 0f)
            {
                float damage = baseCollisionDamage * (impactorMass * relativeSpeed) / (referenceMass * referenceCollisionSpeed);
                health.TakeDamage(damage, DamageCause.Collision);
            }
        }

        if (bounceOffAsteroids && otherIsAsteroid)
        {
            Vector3 asteroidVelocity = collision.rigidbody != null ? collision.rigidbody.linearVelocity : Vector3.zero;

            // Push directly away from the asteroid's own heading; fall back to the contact normal
            // for a stationary asteroid, which has no travel direction to push opposite of.
            Vector3 bounceDirection = asteroidVelocity.sqrMagnitude > 0.0001f
                ? -asteroidVelocity.normalized
                : collision.GetContact(0).normal;

            rb.AddForce(bounceDirection * bounceForce, ForceMode.VelocityChange);
        }
    }
}
