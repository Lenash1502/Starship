using UnityEngine;

// Handles asteroid-vs-asteroid impacts. No continuous HP damage here (that's CollisionDamage's job,
// scoped to ship-vs-asteroid only) -- each asteroid independently rolls a chance to instantly break
// apart based on its own tier and the tier of whatever it hit, calculated separately per side.
// Failing the roll (or not qualifying for one at all) just means it bounces off physically, no
// health lost. Set up as a plain lookup table per tier rather than a general formula because the
// rules genuinely are this asymmetric: Small and Medium break the same way whether hit by their own
// tier or anything larger; Huge, Humongous, and Gargantuan are each vulnerable to their own tier
// (flat chance, rolled separately per side) and to the one tier directly below them, which only
// starts mattering after absorbing a few hits first (see the escalating rule below).
[RequireComponent(typeof(Health))]
public class AsteroidBreaker : MonoBehaviour
{
    // Declared biggest-first so "smaller tier" / "larger tier" comparisons are just int comparisons.
    public enum Tier { Gargantuan, Humongous, Huge, Medium, Small }

    [Tooltip("This asteroid's size tier, set by AsteroidFieldGenerator when it's spawned.")]
    public Tier tier = Tier.Small;

    [Header("Small/Medium: break chance vs. their own tier or anything larger")]
    public float smallBreakChance = 0.30f;
    public float mediumBreakChance = 0.45f;

    [Header("Huge/Humongous/Gargantuan: break chance in a same-tier collision, rolled separately per side")]
    public float hugeSameTierBreakChance = 0.50f;
    public float humongousSameTierBreakChance = 0.60f;
    public float gargantuanSameTierBreakChance = 0.75f;

    [Header("Huge (from Medium) / Humongous (from Huge) / Gargantuan (from Humongous): only damaged by the tier directly below, and only after absorbing hits")]
    [Tooltip("Hits from the qualifying smaller tier absorbed with no effect before break chance starts rolling.")]
    public int hitsBeforeBreakChance = 3;
    [Tooltip("Break chance on the first hit that actually rolls (i.e. hit number hitsBeforeBreakChance + 1).")]
    public float initialBreakChance = 0.25f;
    [Tooltip("How much the break chance increases with each qualifying hit after the first roll.")]
    public float breakChanceIncreasePerHit = 0.15f;

    private Health health;
    private int asteroidLayer;
    private int weakerTierHitsTaken;

    void Awake()
    {
        health = GetComponent<Health>();
        asteroidLayer = LayerMask.NameToLayer("Asteroid");
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer != asteroidLayer) return;
        if (!collision.gameObject.TryGetComponent<AsteroidBreaker>(out var other)) return;

        float? breakChance = ComputeBreakChance(other.tier);
        if (breakChance == null) return;

        if (Random.value < breakChance.Value)
        {
            // Instant kill -- goes through the normal Health pipeline so splitting and the
            // Collision-cause death effect still fire exactly as they would from any other death.
            health.TakeDamage(float.MaxValue, DamageCause.Collision);
        }
    }

    // Null means this tier simply can't be hurt by otherTier at all -- no roll, not even a 0%.
    private float? ComputeBreakChance(Tier otherTier)
    {
        switch (tier)
        {
            case Tier.Small:
                // Nothing is smaller than Small, so every possible collision partner qualifies.
                return smallBreakChance;

            case Tier.Medium:
                // Medium-or-larger qualifies; Small does not.
                return otherTier <= Tier.Medium ? mediumBreakChance : (float?)null;

            case Tier.Huge:
                if (otherTier == Tier.Huge) return hugeSameTierBreakChance;
                if (otherTier == Tier.Medium) return RollEscalatingChance();
                return null;

            case Tier.Humongous:
                if (otherTier == Tier.Humongous) return humongousSameTierBreakChance;
                if (otherTier == Tier.Huge) return RollEscalatingChance();
                return null;

            case Tier.Gargantuan:
                if (otherTier == Tier.Gargantuan) return gargantuanSameTierBreakChance;
                if (otherTier == Tier.Humongous) return RollEscalatingChance();
                return null;

            default:
                return null;
        }
    }

    // Tracks accumulated hits from the one qualifying smaller tier and returns the current break
    // chance for this hit: 0 while still within the free-absorption window, then
    // initialBreakChance, +breakChanceIncreasePerHit per hit after that, capped at 100%.
    private float RollEscalatingChance()
    {
        weakerTierHitsTaken++;
        if (weakerTierHitsTaken <= hitsBeforeBreakChance) return 0f;

        int qualifyingHitNumber = weakerTierHitsTaken - hitsBeforeBreakChance;
        float chance = initialBreakChance + breakChanceIncreasePerHit * (qualifyingHitNumber - 1);
        return Mathf.Min(chance, 1f);
    }
}
