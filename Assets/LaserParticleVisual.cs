using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class LaserParticleVisual : MonoBehaviour
{
    [Tooltip("How long the particle effect stays alive before destroying itself.")]
    public float duration = 0.05f;

    void Awake()
    {
        // Failsafe to ensure particles never pile up in the hierarchy
        Destroy(gameObject, duration);
    }

    /// <summary>
    /// Positions and scales the particle system to bridge the gap between muzzle and impact.
    /// </summary>
    public void SetBeam(Vector3 startPoint, Vector3 endPoint)
    {
        // 1. Calculate distance and direction between gun and target
        float distance = Vector3.Distance(startPoint, endPoint);
        Vector3 direction = (endPoint - startPoint).normalized;

        // 2. Position the particle effect right in the middle of the beam
        transform.position = startPoint + (direction * (distance * 0.5f));

        // 3. Rotate the particle system to look directly at the target
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        // 4. Scale the particle system along the Z-axis to span the exact distance
        // (Assumes your particle system's local Z axis is its forward length)
        Vector3 currentScale = transform.localScale;
        currentScale.z = distance;
        transform.localScale = currentScale;
    }
}