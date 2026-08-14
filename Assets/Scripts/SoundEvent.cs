using UnityEngine;
using UnityEngine.Audio;

// Data-driven definition of "what to play": one or more clips (a random one is picked each time,
// so repeated triggers like rapid-fire weapons don't sound identical every shot), plus
// volume/pitch jitter and mixer routing. Create instances via Assets > Create > Audio > Sound Event
// and drag clips in -- gameplay code never references an AudioClip directly, only a SoundEvent.
[CreateAssetMenu(menuName = "Audio/Sound Event", fileName = "New Sound Event")]
public class SoundEvent : ScriptableObject
{
    [Tooltip("One is picked at random each time this plays. Add a few takes of the same sound to avoid obvious repetition.")]
    public AudioClip[] clips;

    [Header("Volume")]
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Random +/- variance applied to Volume each time this plays.")]
    [Range(0f, 0.5f)] public float volumeJitter = 0f;

    [Header("Pitch")]
    public float pitch = 1f;
    [Tooltip("Random +/- variance applied to Pitch each time this plays.")]
    [Range(0f, 0.5f)] public float pitchJitter = 0f;

    [Header("Spatialization")]
    [Tooltip("0 = 2D (UI, music). 1 = fully 3D, volume falls off with distance from the listener.")]
    [Range(0f, 1f)] public float spatialBlend = 1f;
    public float minDistance = 5f;
    public float maxDistance = 500f;

    [Tooltip("Routes this sound through a mixer group (e.g. SFX, Weapons, Music) for category volume control. Leave blank to use the AudioSource's default output.")]
    public AudioMixerGroup mixerGroup;

    public AudioClip GetClip() => clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    public float GetVolume() => Mathf.Clamp01(volume + Random.Range(-volumeJitter, volumeJitter));
    public float GetPitch() => pitch + Random.Range(-pitchJitter, pitchJitter);
}
