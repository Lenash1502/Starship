using System.Collections.Generic;
using UnityEngine;

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

    [Header("Gargantuan Asteroids")]
    [Tooltip("Absolute maximum count of Gargantuan asteroids allowed.")]
    public int maxGargantuanCount = 2;
    public float gargantuanMinScale = 101f;
    public float gargantuanMaxScale = 200f;

    [Header("Humongous Asteroids")]
    [Tooltip("Absolute maximum count of Humongous asteroids allowed.")]
    public int maxHumongousCount = 10;
    public float humongousMinScale = 51f;
    public float humongousMaxScale = 100f;

    [Header("Huge Asteroids")]
    [Tooltip("Absolute maximum count of Huge asteroids allowed.")]
    public int maxHugeCount = 40;
    public float hugeMinScale = 26f;
    public float hugeMaxScale = 50f;

    [Header("Medium Asteroids")]
    [Tooltip("Absolute maximum count of Medium asteroids allowed.")]
    public int maxMediumCount = 200;
    public float mediumMinScale = 11f;
    public float mediumMaxScale = 25f;

    [Header("Small Asteroids")]
    [Tooltip("Small asteroids will automatically fill the remaining count up to Total Asteroids.")]
    public float smallMinScale = 1f;
    public float smallMaxScale = 10f;

    [Header("Physics & Drift Settings")]
    [Tooltip("The lowest possible drift speed (before scale reduction).")]
    public float minDriftSpeed = 10f;
    [Tooltip("The absolute maximum drift speed (before scale reduction).")]
    public float maxDriftSpeed = 300f;

    [Tooltip("The lowest possible spin speed (before scale reduction).")]
    public float minSpinSpeed = 1f;
    [Tooltip("The absolute maximum spin speed (before scale reduction).")]
    public float maxSpinSpeed = 20f;

    private struct AsteroidData
    {
        public Vector3 position;
        public float radius;
    }

    private Dictionary<Vector3Int, List<AsteroidData>> spatialGrid;
    private float cellSize;
    public LayerMask collisionAvoidanceLayer = ~0;

    void Start()
    {
        GenerateAsteroidField();
    }

    private void GenerateAsteroidField()
    {
        if (asteroidPrefabs == null || asteroidPrefabs.Length == 0 || playerCenter == null) return;

        // Ensure grid cells are large enough for the absolute biggest possible asteroid
        float absoluteMaxScale = Mathf.Max(smallMaxScale, mediumMaxScale, hugeMaxScale, humongousMaxScale, gargantuanMaxScale);
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

    private void ApplyPhysics(GameObject asteroid, float scale)
    {
        if (!asteroid.TryGetComponent<Rigidbody>(out var rb)) rb = asteroid.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        rb.mass = Mathf.Pow(scale, 3) * 0.1f;

        float sizeDampener = Mathf.Sqrt(scale);

        float currentMinDrift = minDriftSpeed / sizeDampener;
        float currentMaxDrift = maxDriftSpeed / sizeDampener;
        float currentMinSpin = minSpinSpeed / sizeDampener;
        float currentMaxSpin = maxSpinSpeed / sizeDampener;

        float driftSpeed = Random.Range(currentMinDrift, currentMaxDrift);
        rb.linearVelocity = Random.onUnitSphere * driftSpeed;

        float spinSpeed = Random.Range(currentMinSpin, currentMaxSpin);
        rb.angularVelocity = Random.onUnitSphere * spinSpeed;
    }

    private List<float> GenerateScaleList()
    {
        List<float> scales = new();

        // 1. Grab the exact caps requested in the Inspector
        int numGargantuan = maxGargantuanCount;
        int numHumongous = maxHumongousCount;
        int numHuge = maxHugeCount;
        int numMedium = maxMediumCount;

        // 2. Small asteroids act as filler for whatever is left
        int numSmall = Mathf.Max(0, totalAsteroids - (numGargantuan + numHumongous + numHuge + numMedium));

        // 3. Generate the actual scales within explicit bounds
        for (int i = 0; i < numGargantuan; i++) scales.Add(Random.Range(gargantuanMinScale, gargantuanMaxScale));
        for (int i = 0; i < numHumongous; i++) scales.Add(Random.Range(humongousMinScale, humongousMaxScale));
        for (int i = 0; i < numHuge; i++) scales.Add(Random.Range(hugeMinScale, hugeMaxScale));
        for (int i = 0; i < numMedium; i++) scales.Add(Random.Range(mediumMinScale, mediumMaxScale));
        for (int i = 0; i < numSmall; i++) scales.Add(Random.Range(smallMinScale, smallMaxScale));

        // 4. Sort largest to smallest for 'Rocks in a Jar' generation algorithm
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