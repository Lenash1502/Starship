using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DeathSoundEntry
{
    public DamageCause cause;
    public SoundEvent sound;
}

// Cause-keyed death sound -- mirrors DeathEffects' per-DamageCause selection exactly, just for
// audio instead of VFX. Drop on anything with a Health component.
[RequireComponent(typeof(Health))]
public class DeathSound : MonoBehaviour
{
    [Tooltip("Sound to play for a specific damage cause. Causes not listed here fall back to Default Sound below.")]
    public List<DeathSoundEntry> sounds = new();
    public SoundEvent defaultSound;

    private Health health;

    void Awake() => health = GetComponent<Health>();
    void OnEnable() => health.OnDied += HandleDied;
    void OnDisable() => health.OnDied -= HandleDied;

    private void HandleDied(DamageCause cause)
    {
        SoundEvent sound = defaultSound;
        foreach (DeathSoundEntry entry in sounds)
        {
            if (entry.cause == cause)
            {
                sound = entry.sound;
                break;
            }
        }

        if (sound != null && SoundManager.Instance != null) SoundManager.Instance.Play(sound, transform.position);
    }
}
