using UnityEngine;

// Looping/attached audio (engine hum, boost) doesn't belong in SoundManager's pooled one-shot
// system -- it owns its own AudioSource for the ship's whole lifetime instead, the same way
// DirectionalThrusterListener owns its own ParticleSystem rather than borrowing one. Pooling only
// pays off for bursty, short-lived sounds.
[RequireComponent(typeof(AudioSource))]
public class EngineSound : MonoBehaviour
{
    public SpaceshipController shipController;
    public SoundEvent idleLoop;
    public SoundEvent boostLoop;

    private AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
    }

    void OnEnable()
    {
        if (shipController == null) return;
        shipController.OnBoostChanged += HandleBoostChanged;
    }

    void OnDisable()
    {
        if (shipController == null) return;
        shipController.OnBoostChanged -= HandleBoostChanged;
    }

    void Start()
    {
        PlayLoop(idleLoop);
    }

    private void HandleBoostChanged(bool boosting)
    {
        PlayLoop(boosting ? boostLoop : idleLoop);
    }

    private void PlayLoop(SoundEvent sound)
    {
        AudioClip clip = sound != null ? sound.GetClip() : null;
        if (clip == null)
        {
            source.Stop();
            return;
        }

        source.clip = clip;
        source.volume = sound.GetVolume();
        source.pitch = sound.GetPitch();
        // Same fallback as SoundManager.Configure, so an engine SoundEvent with no group set still
        // lands in the SFX category instead of bypassing it straight to Master.
        source.outputAudioMixerGroup = sound.mixerGroup != null ? sound.mixerGroup
            : SoundManager.Instance != null ? SoundManager.Instance.defaultSfxGroup
            : null;
        source.Play();
    }
}
