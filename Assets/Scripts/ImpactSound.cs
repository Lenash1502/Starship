using UnityEngine;

// Generic bump/scrape sound for any physical collision, independent of whether it dealt damage.
// Intended mainly for ships (bumping an asteroid should always make noise, even ones with no
// Health yet) -- attaching this to every asteroid too would mean a sound on every asteroid-vs-
// asteroid graze in a 1500-asteroid field, which is more spam than most scenes want by default.
[RequireComponent(typeof(CollisionDamage))]
public class ImpactSound : MonoBehaviour
{
    public SoundEvent impactSound;

    private CollisionDamage collisionDamage;

    void Awake() => collisionDamage = GetComponent<CollisionDamage>();
    void OnEnable() => collisionDamage.OnImpact += HandleImpact;
    void OnDisable() => collisionDamage.OnImpact -= HandleImpact;

    private void HandleImpact(Vector3 position)
    {
        if (impactSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(impactSound, position);
    }
}
