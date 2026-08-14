using UnityEngine;

// Each muzzle rotates itself to face the crosshair (rather than inheriting the gun's rotation) so
// its attached visual effects point the right way; the gun (WeaponBase/its subclasses) still owns
// everything else -- fire rate, damage, hit detection, and so on.
public class WeaponMuzzle : MonoBehaviour
{
    [Header("Mechanical Settings")]
    [Tooltip("How smoothly the barrel tracks the crosshair.")]
    public float aimSmoothSpeed = 15f;

    [Header("Visual Effects")]
    public ParticleSystem mainVisualParticles;
    public ParticleSystem muzzleFlashParticles;

    public void AimAt(Vector3 targetPoint)
    {
        Vector3 direction = (targetPoint - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            transform.up = Vector3.Slerp(transform.up, direction, Time.deltaTime * aimSmoothSpeed);
        }
    }

    // Decoupled: Only scales the length based on range. Duration is left untouched.
    public void FireVisuals(float hitDistance)
    {
        if (mainVisualParticles != null)
        {
            mainVisualParticles.Stop();
            var main = mainVisualParticles.main;
            main.startSize3D = true;
            main.startSizeY = hitDistance; // Sets the length of the beam to match the hit distance
            mainVisualParticles.Play();
        }

        if (muzzleFlashParticles != null)
        {
            muzzleFlashParticles.Stop();
            muzzleFlashParticles.Play();
        }
    }
}