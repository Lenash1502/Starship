using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AsteroidTier
{
    public string tierName = "Tier";
    [Tooltip("Absolute maximum count of this tier allowed.")]
    public int maxCount;
    public float minScale;
    public float maxScale;

    [Header("Splitting")]
    [Tooltip("Random range of pieces this tier's asteroids break into on death. Leave both at 0 so it doesn't split.")]
    public int minSplitPieces;
    public int maxSplitPieces;
    [Tooltip("If set, one of the pieces is always a large chunk (see Large Chunk Fraction below) instead of following the normal per-piece scale formula.")]
    public bool splitsWithLargeChunk;

    [Header("Drift Speed")]
    [Tooltip("Multiplies this tier's max drift speed only (the minimum is untouched), widening its random speed range. Use higher values on large tiers so they're not all uniformly sluggish; leave at 1 for no change.")]
    public float driftSpeedVarianceMultiplier = 1f;
}

public class AsteroidFieldGenerator : MonoBehaviour
{
    [Header("References")]
    public GameObject[] asteroidPrefabs;
    public Transform playerCenter;

    [Header("Spawn Boundaries")]
    public float innerRadius = 100f;
    public float outerRadius = 1000f;

    [Header("Total Density")]
    [Tooltip("The absolute maximum number of asteroids that will exist in the field.")]
    public int totalAsteroids = 1500;
    public int maxSpawnAttemptsPerAsteroid = 20;

    [Header("Asteroid Size Tiers")]
    [Tooltip("Fixed-count tiers, largest first. Small asteroids (below) automatically fill whatever count is left over.")]
    public List<AsteroidTier> sizeTiers = new()
    {
        new AsteroidTier { tierName = "Gargantuan", maxCount = 100, minScale = 300f, maxScale = 1000f, minSplitPieces = 3, maxSplitPieces = 5, splitsWithLargeChunk = true, driftSpeedVarianceMultiplier = 5f },
        new AsteroidTier { tierName = "Humongous", maxCount = 200, minScale = 71f, maxScale = 150f, minSplitPieces = 3, maxSplitPieces = 5, splitsWithLargeChunk = true, driftSpeedVarianceMultiplier = 4f },
        new AsteroidTier { tierName = "Huge", maxCount = 500, minScale = 31f, maxScale = 70f, minSplitPieces = 2, maxSplitPieces = 3, splitsWithLargeChunk = true, driftSpeedVarianceMultiplier = 3f },
        new AsteroidTier { tierName = "Medium", maxCount = 1000, minScale = 16f, maxScale = 30f, minSplitPieces = 2, maxSplitPieces = 3, driftSpeedVarianceMultiplier = 2f },
    };

    [Header("Small Asteroids")]
    [Tooltip("Small asteroids will automatically fill the remaining count up to Total Asteroids.")]
    public float smallMinScale = 5f;
    public float smallMaxScale = 15f;

    [Header("Physics & Drift Settings")]
    [Tooltip("The lowest possible drift speed (before scale reduction).")]
    public float minDriftSpeed = 10f;
    [Tooltip("The absolute maximum drift speed (before scale reduction).")]
    public float maxDriftSpeed = 300f;

    [Tooltip("The lowest possible spin speed (before scale reduction).")]
    public float minSpinSpeed = 1f;
    [Tooltip("The absolute maximum spin speed (before scale reduction).")]
    public float maxSpinSpeed = 20f;

    [Header("Health Settings")]
    [Tooltip("Max health granted to each asteroid, per point of scale (e.g. a scale-10 asteroid gets 10 * this value).")]
    public float healthPerScale = 10f;

    [Header("Collision Damage (ships only)")]
    [Tooltip("Damage dealt on impact by a scale-10 asteroid at Collision Reference Speed. Only applies to ship-vs-asteroid impacts; see CollisionDamage.")]
    public float baseCollisionDamage = 25f;
    [Tooltip("Relative impact speed (units/sec) Base Collision Damage above is calibrated against.")]
    public float collisionReferenceSpeed = 50f;

    [Header("Asteroid Impact Breaking")]
    [Tooltip("Chance a Small asteroid instantly breaks apart on any collision with another asteroid (Small has nothing smaller to hit it). Failing the roll just bounces it with no health loss.")]
    public float smallBreakChance = 0.30f;
    [Tooltip("Same idea for Medium asteroids colliding with Medium-or-larger asteroids. Medium is unaffected by Small impacts.")]
    public float mediumBreakChance = 0.45f;
    [Tooltip("Chance a Huge asteroid breaks in a Huge-vs-Huge collision, rolled separately per side. Huge is unaffected by anything else.")]
    public float hugeSameTierBreakChance = 0.50f;
    [Tooltip("Chance a Humongous asteroid breaks in a Humongous-vs-Humongous collision, rolled separately per side.")]
    public float humongousSameTierBreakChance = 0.60f;
    [Tooltip("Chance a Gargantuan asteroid breaks in a Gargantuan-vs-Gargantuan collision, rolled separately per side.")]
    public float gargantuanSameTierBreakChance = 0.75f;
    [Tooltip("Gargantuan is only damaged by Humongous impacts, and Humongous only by Huge impacts: this many qualifying hits are absorbed with no effect before break chance starts rolling.")]
    public int hitsBeforeBreakChance = 3;
    [Tooltip("Break chance on the first hit that actually rolls.")]
    public float initialBreakChance = 0.25f;
    [Tooltip("How much the break chance increases with each qualifying hit after the first roll.")]
    public float breakChanceIncreasePerHit = 0.15f;

    [Header("Death Effects")]
    [Tooltip("Effect(s) per damage cause (e.g. a different burst for a laser kill vs. an asteroid collision). Causes not listed here fall back to Default Death Effect Prefabs below.")]
    public List<DeathEffectSet> deathEffectSets = new();
    [Tooltip("Effect prefabs used for any damage cause not covered above. Leave empty to spawn nothing for those causes.")]
    public GameObject[] defaultDeathEffectPrefabs;
    [Tooltip("If true, every prefab in the matched set/default list fires at once. If false, one is picked at random each death.")]
    public bool playAllDeathEffects = false;
    [Tooltip("Self-destruct time (seconds) for a spawned effect if it has no ParticleSystem to time cleanup off of.")]
    public float deathEffectFallbackLifetime = 3f;

    [Header("Death Sounds")]
    [Tooltip("Sound per damage cause (e.g. a laser kill vs. an asteroid collision). Causes not listed here fall back to Default Death Sound below.")]
    public List<DeathSoundEntry> deathSounds = new();
    [Tooltip("Sound used for any damage cause not covered above. Leave blank for silence.")]
    public SoundEvent defaultDeathSound;

    [Header("Splitting")]
    [Tooltip("Random speed range (units/sec) each fragment is pushed away from the point of death, applied directly regardless of the fragment's mass.")]
    public float splitForceMin = 150f;
    public float splitForceMax = 250f;
    [Tooltip("Each fragment's scale is parentScale / pieceCount, then randomized by +/- this fraction.")]
    [Range(0f, 1f)] public float splitScaleJitter = 0.2f;
    [Tooltip("For tiers with Splits With Large Chunk enabled: the guaranteed large piece's scale, as a fraction of the parent's scale.")]
    [Range(0f, 1f)] public float largeChunkMinFraction = 0.7f;
    [Range(0f, 1f)] public float largeChunkMaxFraction = 0.8f;
    [Tooltip("Fragments spawn offset outward from the death point by (their own scale * this), so they start with less distance to travel before clearing each other's colliders.")]
    public float splitSpawnOffsetMultiplier = 0.5f;
    [Tooltip("Safety cutoff: freshly-split fragments' mutual collisions are re-enabled after this long even if they haven't fully cleared each other.")]
    public float splitCollisionIgnoreTimeout = 3f;

    private struct AsteroidData
    {
        public Vector3 position;
        public float radius;
    }

    private Dictionary<Vector3Int, List<AsteroidData>> spatialGrid;
    private float cellSize;
    public LayerMask collisionAvoidanceLayer = ~0;

    [Header("Population Maintenance")]
    [Tooltip("When the active asteroid count drops below this fraction of Total Asteroids, a same-category replacement is spawned off-camera within the field.")]
    [Range(0f, 1f)] public float populationReplenishThreshold = 0.8f;
    [Tooltip("Camera to avoid spawning replacement asteroids in view of. Defaults to Camera.main if left blank.")]
    public Camera boundsCamera;

    private readonly List<Transform> activeAsteroids = new();

    // The point the field was actually generated around, captured once so it stays correct even
    // if playerCenter (typically the player ship) keeps moving afterward. Asteroids never move
    // once spawned, so this is the fixed center other systems (e.g. AIWanderPilot) should bound
    // themselves against, not playerCenter.position.
    public Vector3 FieldCenter { get; private set; }

    void Start()
    {
        GenerateAsteroidField();
    }

    private void GenerateAsteroidField()
    {
        if (asteroidPrefabs == null || asteroidPrefabs.Length == 0 || playerCenter == null) return;

        FieldCenter = playerCenter.position;

        // Ensure grid cells are large enough for the absolute biggest possible asteroid
        float absoluteMaxScale = smallMaxScale;
        foreach (AsteroidTier tier in sizeTiers) absoluteMaxScale = Mathf.Max(absoluteMaxScale, tier.maxScale);
        cellSize = absoluteMaxScale;

        spatialGrid = new Dictionary<Vector3Int, List<AsteroidData>>();

        List<float> plannedScales = GenerateScaleList();
        int successfullySpawned = 0;

        foreach (float currentScale in plannedScales)
        {
            bool placedSuccessfully = false;
            int attempts = 0;
            float currentRadius = currentScale / 2f;

            while (!placedSuccessfully && attempts < maxSpawnAttemptsPerAsteroid)
            {
                attempts++;
                Vector3 randomDirection = Random.onUnitSphere;
                float randomDistance = Random.Range(innerRadius, outerRadius);
                Vector3 potentialPosition = playerCenter.position + (randomDirection * randomDistance);

                if (IsValidPosition(potentialPosition, currentRadius))
                {
                    GameObject prefabToSpawn = asteroidPrefabs[Random.Range(0, asteroidPrefabs.Length)];
                    GameObject newAsteroid = Instantiate(prefabToSpawn, potentialPosition, Random.rotation);
                    newAsteroid.transform.SetParent(this.transform);
                    newAsteroid.transform.localScale = Vector3.one * currentScale;

                    ApplyPhysics(newAsteroid, currentScale);

                    Vector3Int gridCell = GetGridCell(potentialPosition);
                    if (!spatialGrid.ContainsKey(gridCell))
                    {
                        spatialGrid[gridCell] = new List<AsteroidData>();
                    }

                    spatialGrid[gridCell].Add(new AsteroidData
                    {
                        position = potentialPosition,
                        radius = currentRadius
                    });

                    placedSuccessfully = true;
                    successfullySpawned++;
                }
            }
        }

        Debug.Log($"Generated {successfullySpawned} out of {totalAsteroids} requested asteroids dynamically.");
    }

    // Checked whenever an asteroid dies (see ApplyPhysics's Health.OnDied hookup): if the field has
    // dropped below Population Replenish Threshold of Total Asteroids, spawn one same-category
    // replacement to bring it back up. Deaths that split into fragments usually don't need this at
    // all (the fragments already backfill the count); it mainly kicks in for tiers that don't split,
    // like Small asteroids getting worn down over time.
    private void MaintainPopulation(AsteroidBreaker.Tier tier)
    {
        if (activeAsteroids.Count >= totalAsteroids * populationReplenishThreshold) return;

        SpawnAsteroidOffCamera(PickScaleInTier(tier));
    }

    // Spawns one new asteroid of the given scale at a random valid spot in the field, retrying if
    // the spot overlaps an existing asteroid or falls inside the camera's frustum (so it doesn't
    // visibly pop in).
    private void SpawnAsteroidOffCamera(float scale)
    {
        if (asteroidPrefabs == null || asteroidPrefabs.Length == 0) return;

        // Camera.main requires a camera tagged "MainCamera" — don't silently skip the visibility
        // check just because nothing in the scene happens to be tagged that way.
        Camera cam = boundsCamera != null ? boundsCamera : Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        Plane[] frustumPlanes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;

        float radius = scale / 2f;

        for (int attempt = 0; attempt < maxSpawnAttemptsPerAsteroid; attempt++)
        {
            Vector3 randomDirection = Random.onUnitSphere;
            float randomDistance = Random.Range(innerRadius, outerRadius);
            Vector3 position = FieldCenter + randomDirection * randomDistance;

            if (frustumPlanes != null && GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds(position, Vector3.one * scale))) continue;
            if (Physics.CheckSphere(position, radius, collisionAvoidanceLayer)) continue;

            GameObject prefabToSpawn = asteroidPrefabs[Random.Range(0, asteroidPrefabs.Length)];
            GameObject newAsteroid = Instantiate(prefabToSpawn, position, Random.rotation);
            newAsteroid.transform.SetParent(this.transform);
            newAsteroid.transform.localScale = Vector3.one * scale;

            ApplyPhysics(newAsteroid, scale);
            return;
        }
    }

    // Picks a random scale within a specific tier's range, for spawning a like-for-like
    // replacement. Mirrors GetTierRank's index mapping (sizeTiers ordered biggest-first, 1:1 with
    // AsteroidBreaker.Tier's first four values); Small uses the filler range directly.
    private float PickScaleInTier(AsteroidBreaker.Tier tier)
    {
        int index = (int)tier;
        if (index < sizeTiers.Count) return Random.Range(sizeTiers[index].minScale, sizeTiers[index].maxScale);

        return Random.Range(smallMinScale, smallMaxScale);
    }

    private void ApplyPhysics(GameObject asteroid, float scale)
    {
        activeAsteroids.Add(asteroid.transform);

        if (!asteroid.TryGetComponent<Health>(out var health)) health = asteroid.AddComponent<Health>();
        health.SetMaxHealth(scale * healthPerScale);

        Transform asteroidTransform = asteroid.transform;
        AsteroidBreaker.Tier tierRank = GetTierRank(scale);

        (int minSplitPieces, int maxSplitPieces, bool splitsWithLargeChunk, float driftSpeedVarianceMultiplier) = GetTierInfo(scale);
        if (maxSplitPieces > 0)
        {
            health.OnDied += _ => SplitAsteroid(asteroidTransform.position, scale, minSplitPieces, maxSplitPieces, splitsWithLargeChunk);
        }

        // Whatever else happens on death (splitting or not), this asteroid is no longer part of
        // the field once it's gone -- drop it from the count and top the field back up if needed.
        health.OnDied += _ =>
        {
            activeAsteroids.Remove(asteroidTransform);
            MaintainPopulation(tierRank);
        };

        if (!asteroid.TryGetComponent<Rigidbody>(out var rb)) rb = asteroid.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        rb.mass = Mathf.Pow(scale, 3) * 0.1f;

        float sizeDampener = Mathf.Sqrt(scale);

        float currentMinDrift = minDriftSpeed / sizeDampener;
        float currentMaxDrift = (maxDriftSpeed / sizeDampener) * driftSpeedVarianceMultiplier;
        float currentMinSpin = minSpinSpeed / sizeDampener;
        float currentMaxSpin = maxSpinSpeed / sizeDampener;

        float driftSpeed = Random.Range(currentMinDrift, currentMaxDrift);
        rb.linearVelocity = Random.onUnitSphere * driftSpeed;

        float spinSpeed = Random.Range(currentMinSpin, currentMaxSpin);
        rb.angularVelocity = Random.onUnitSphere * spinSpeed;

        if (!asteroid.TryGetComponent<CollisionDamage>(out var collisionDamage)) collisionDamage = asteroid.AddComponent<CollisionDamage>();
        collisionDamage.baseCollisionDamage = baseCollisionDamage;
        collisionDamage.referenceCollisionSpeed = collisionReferenceSpeed;

        if (!asteroid.TryGetComponent<AsteroidBreaker>(out var breaker)) breaker = asteroid.AddComponent<AsteroidBreaker>();
        breaker.tier = tierRank;
        breaker.smallBreakChance = smallBreakChance;
        breaker.mediumBreakChance = mediumBreakChance;
        breaker.hugeSameTierBreakChance = hugeSameTierBreakChance;
        breaker.humongousSameTierBreakChance = humongousSameTierBreakChance;
        breaker.gargantuanSameTierBreakChance = gargantuanSameTierBreakChance;
        breaker.hitsBeforeBreakChance = hitsBeforeBreakChance;
        breaker.initialBreakChance = initialBreakChance;
        breaker.breakChanceIncreasePerHit = breakChanceIncreasePerHit;

        if (!asteroid.TryGetComponent<DeathEffects>(out var deathEffects)) deathEffects = asteroid.AddComponent<DeathEffects>();
        deathEffects.effectSets = deathEffectSets;
        deathEffects.defaultEffectPrefabs = defaultDeathEffectPrefabs;
        deathEffects.playAllDefaultEffects = playAllDeathEffects;
        deathEffects.fallbackLifetime = deathEffectFallbackLifetime;

        if (!asteroid.TryGetComponent<DeathSound>(out var deathSound)) deathSound = asteroid.AddComponent<DeathSound>();
        deathSound.sounds = deathSounds;
        deathSound.defaultSound = defaultDeathSound;
    }

    // sizeTiers is configured biggest-first (Gargantuan, Humongous, Huge, Medium), which lines up
    // 1:1 with AsteroidBreaker.Tier's first four values by index; anything that doesn't match one
    // of those ranges is a small filler asteroid, i.e. AsteroidBreaker.Tier.Small.
    private AsteroidBreaker.Tier GetTierRank(float scale)
    {
        for (int i = 0; i < sizeTiers.Count; i++)
        {
            AsteroidTier tier = sizeTiers[i];
            if (scale >= tier.minScale && scale <= tier.maxScale) return (AsteroidBreaker.Tier)i;
        }

        return AsteroidBreaker.Tier.Small;
    }

    // Looks up which tier a given scale belongs to and returns its split-piece range and drift
    // speed variance. Works for both initially-generated scales and the smaller scales produced by
    // SplitAsteroid. Scales outside every tier (i.e. the small filler asteroids) get the defaults
    // below, which leaves their drift speed untouched.
    private (int minPieces, int maxPieces, bool splitsWithLargeChunk, float driftSpeedVarianceMultiplier) GetTierInfo(float scale)
    {
        foreach (AsteroidTier tier in sizeTiers)
        {
            if (scale >= tier.minScale && scale <= tier.maxScale) return (tier.minSplitPieces, tier.maxSplitPieces, tier.splitsWithLargeChunk, tier.driftSpeedVarianceMultiplier);
        }

        return (0, 0, false, 1f);
    }

    private void SplitAsteroid(Vector3 origin, float parentScale, int minPieces, int maxPieces, bool splitsWithLargeChunk)
    {
        int pieceCount = Random.Range(minPieces, maxPieces + 1);
        float baseFragmentScale = parentScale / pieceCount;

        // One randomly-chosen piece becomes the guaranteed large chunk; the rest use the normal formula.
        int largeChunkIndex = splitsWithLargeChunk ? Random.Range(0, pieceCount) : -1;

        List<Collider> pieceColliders = new(pieceCount);

        for (int i = 0; i < pieceCount; i++)
        {
            float pieceScale = i == largeChunkIndex
                ? parentScale * Random.Range(largeChunkMinFraction, largeChunkMaxFraction)
                : baseFragmentScale * Random.Range(1f - splitScaleJitter, 1f + splitScaleJitter);

            // Spawn already nudged outward along the direction it'll be pushed, so it starts
            // with less distance to travel before clearing its siblings' colliders.
            Vector3 direction = Random.onUnitSphere;
            Vector3 spawnPosition = origin + direction * (pieceScale * splitSpawnOffsetMultiplier);

            GameObject prefabToSpawn = asteroidPrefabs[Random.Range(0, asteroidPrefabs.Length)];
            GameObject piece = Instantiate(prefabToSpawn, spawnPosition, Random.rotation);
            piece.transform.SetParent(this.transform);
            piece.transform.localScale = Vector3.one * pieceScale;

            ApplyPhysics(piece, pieceScale);

            if (piece.TryGetComponent<Rigidbody>(out var rb))
            {
                float force = Random.Range(splitForceMin, splitForceMax);
                rb.AddForce(direction * force, ForceMode.VelocityChange);
            }

            if (piece.TryGetComponent<Collider>(out var pieceCollider))
            {
                pieceColliders.Add(pieceCollider);
            }
        }

        if (pieceColliders.Count > 1) StartCoroutine(IgnoreCollisionsUntilClear(pieceColliders));
    }

    // Freshly-split fragments spawn overlapping at the same point, so they'd otherwise
    // shove each other apart unnaturally hard. Ignore collisions between them until they've
    // physically separated (or, failing that, until the safety timeout expires).
    private IEnumerator IgnoreCollisionsUntilClear(List<Collider> colliders)
    {
        List<(Collider a, Collider b)> pairs = new();
        for (int i = 0; i < colliders.Count; i++)
        {
            for (int j = i + 1; j < colliders.Count; j++)
            {
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
                pairs.Add((colliders[i], colliders[j]));
            }
        }

        float elapsed = 0f;
        while (pairs.Count > 0 && elapsed < splitCollisionIgnoreTimeout)
        {
            yield return null;
            elapsed += Time.deltaTime;

            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                (Collider a, Collider b) = pairs[i];
                if (a == null || b == null)
                {
                    pairs.RemoveAt(i);
                    continue;
                }

                float distance = Vector3.Distance(a.bounds.center, b.bounds.center);
                float combinedRadius = a.bounds.extents.magnitude + b.bounds.extents.magnitude;

                if (distance > combinedRadius)
                {
                    Physics.IgnoreCollision(a, b, false);
                    pairs.RemoveAt(i);
                }
            }
        }

        // Safety: whatever's left after the timeout gets re-enabled regardless.
        foreach ((Collider a, Collider b) in pairs)
        {
            if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
        }
    }

    private List<float> GenerateScaleList()
    {
        List<float> scales = new();

        // 1. Generate the actual scales within explicit bounds for each fixed-count tier
        int tieredCount = 0;
        foreach (AsteroidTier tier in sizeTiers)
        {
            for (int i = 0; i < tier.maxCount; i++) scales.Add(Random.Range(tier.minScale, tier.maxScale));
            tieredCount += tier.maxCount;
        }

        // 2. Small asteroids act as filler for whatever is left
        int numSmall = Mathf.Max(0, totalAsteroids - tieredCount);
        for (int i = 0; i < numSmall; i++) scales.Add(Random.Range(smallMinScale, smallMaxScale));

        // 3. Sort largest to smallest for 'Rocks in a Jar' generation algorithm
        scales.Sort((a, b) => b.CompareTo(a));

        return scales;
    }

    private Vector3Int GetGridCell(Vector3 position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.y / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }

    private bool IsValidPosition(Vector3 position, float newAsteroidRadius)
    {
        Vector3Int targetCell = GetGridCell(position);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int neighborCell = new(targetCell.x + x, targetCell.y + y, targetCell.z + z);
                    if (spatialGrid.TryGetValue(neighborCell, out List<AsteroidData> existingAsteroids))
                    {
                        foreach (AsteroidData existing in existingAsteroids)
                        {
                            float actualDistance = Vector3.Distance(position, existing.position);
                            float requiredDistance = newAsteroidRadius + existing.radius;

                            if (actualDistance < requiredDistance) return false;
                        }
                    }
                }
            }
        }

        if (Physics.CheckSphere(position, newAsteroidRadius, collisionAvoidanceLayer))
        {
            return false;
        }

        return true;
    }
}