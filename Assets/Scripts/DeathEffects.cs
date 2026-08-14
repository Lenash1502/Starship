using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DeathEffectSet
{
    public DamageCause cause;
    [Tooltip("Effect prefab(s) to spawn when this object dies from the cause above.")]
    public GameObject[] effectPrefabs;
    [Tooltip("If true, every prefab in this set fires at once. If false, one is picked at random.")]
    public bool playAll = false;
}

// Reusable death-VFX framework: drop this on anything with a Health component -- asteroids, ships,
// whatever else becomes destructible later -- and configure which effect(s) play for which
// DamageCause (e.g. a different burst for a laser kill than for an asteroid collision). Causes
// without a matching entry fall back to Default Effect Prefabs, so a bare setup with just the
// default list still works, and per-cause variety can be layered in later without touching code.
[RequireComponent(typeof(Health))]
public class DeathEffects : MonoBehaviour
{
    [Tooltip("Effect(s) to play for a specific damage cause. Causes not listed here fall back to Default Effect Prefabs below.")]
    public List<DeathEffectSet> effectSets = new();

    [Header("Fallback")]
    public GameObject[] defaultEffectPrefabs;
    public bool playAllDefaultEffects = false;

    [Tooltip("Self-destruct time (seconds) for a spawned effect if it has no ParticleSystem to time cleanup off of.")]
    public float fallbackLifetime = 3f;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        health.OnDied += HandleDied;
    }

    void OnDisable()
    {
        health.OnDied -= HandleDied;
    }

    private void HandleDied(DamageCause cause)
    {
        GameObject[] prefabs = defaultEffectPrefabs;
        bool playAll = playAllDefaultEffects;

        foreach (DeathEffectSet set in effectSets)
        {
            if (set.cause == cause)
            {
                prefabs = set.effectPrefabs;
                playAll = set.playAll;
                break;
            }
        }

        if (prefabs == null || prefabs.Length == 0) return;

        // Read these before we return control to Health.TakeDamage, which destroys this
        // GameObject right after this event finishes firing.
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        float scale = transform.localScale.x;

        if (playAll)
        {
            foreach (GameObject prefab in prefabs) SpawnEffect(prefab, position, rotation, scale);
        }
        else
        {
            SpawnEffect(prefabs[Random.Range(0, prefabs.Length)], position, rotation, scale);
        }
    }

    // Spawned as a standalone object (not parented to this one, which is about to be destroyed).
    // Relies on the prefab's ParticleSystem having its Main module's Scaling Mode set to Hierarchy
    // so it actually responds to the scale passed in.
    private void SpawnEffect(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return;

        GameObject instance = Instantiate(prefab, position, rotation);
        instance.transform.localScale *= scale;

        ParticleSystem vfx = instance.GetComponentInChildren<ParticleSystem>(true);
        if (vfx != null) vfx.Play();

        instance.AddComponent<DeathEffectCleanup>().Begin(vfx, fallbackLifetime);
    }
}
