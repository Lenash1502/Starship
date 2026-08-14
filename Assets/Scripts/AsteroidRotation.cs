using UnityEngine;

public class AsteroidRotation : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Minimum rotation speed in degrees per second.")]
    [SerializeField] private float minSpeed = 5f;

    [Tooltip("Base maximum rotation speed. Will be reduced if scale is > 10.")]
    [SerializeField] private float maxSpeed = 45f;

    private Vector3 randomRotationVector;

    void Start()
    {
        // 1. Find the largest dimension of the asteroid's scale
        float scaleSize = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);

        // 2. Calculate how many full 10-point increments it has above 10
        // Example: Scale 10-19.9 = 0 increments, Scale 20-29.9 = 1 increment, etc.
        int scaleIncrements = Mathf.Max(0, Mathf.FloorToInt(scaleSize / 10f) - 1);

        // 3. Reduce max speed by 15 per increment. 
        // Mathf.Max ensures it never drops below the minSpeed.
        float adjustedMaxSpeed = Mathf.Max(minSpeed, maxSpeed - (scaleIncrements * 15f));

        // 4. Calculate a random speed for each axis individually upon spawning
        float xSpeed = GetRandomDirectionalSpeed(adjustedMaxSpeed);
        float ySpeed = GetRandomDirectionalSpeed(adjustedMaxSpeed);
        float zSpeed = GetRandomDirectionalSpeed(adjustedMaxSpeed);

        // Store it in our Vector3
        randomRotationVector = new Vector3(xSpeed, ySpeed, zSpeed);
    }

    void Update()
    {
        // Apply the rotation every frame
        transform.Rotate(randomRotationVector * Time.deltaTime);
    }

    /// <summary>
    /// Generates a random speed between min and our newly adjusted max.
    /// </summary>
    private float GetRandomDirectionalSpeed(float calculatedMaxSpeed)
    {
        // Pick a random speed between the limits
        float speed = Random.Range(minSpeed, calculatedMaxSpeed);

        // 50% chance to rotate in the opposite direction
        float directionMultiplier = (Random.value > 0.5f) ? 1f : -1f;

        return speed * directionMultiplier;
    }
}