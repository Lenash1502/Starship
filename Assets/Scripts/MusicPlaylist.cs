using UnityEngine;

// A set of tracks plus the rules for moving between them. Same split as SoundEvent: the asset says
// what to play and how it should feel, the MonoBehaviour (MusicPlayer) does the playing. Keeping it
// as an asset means you can author a calm exploration playlist and a tense combat one with
// different pacing, and swap between them at runtime without touching any scene wiring.
[CreateAssetMenu(menuName = "Audio/Music Playlist", fileName = "New Music Playlist")]
public class MusicPlaylist : ScriptableObject
{
    public enum TrackOrder
    {
        [InspectorName("Shuffle (no repeats until all played)")] Shuffle,
        [InspectorName("Random (may cluster)")] Random,
        Sequential,
    }

    public enum TransitionMode
    {
        [InspectorName("Crossfade (seamless blend)")] Crossfade,
        [InspectorName("Silence gap")] Silence,
        [InspectorName("Silence gap with ambience")] Ambience,
        [InspectorName("Random per transition")] RandomPerTransition,
    }

    [Tooltip("Tracks to draw from. Order only matters for Sequential.")]
    public AudioClip[] tracks;

    [Tooltip("Shuffle deals every track once before any repeats, so you never hear the same one twice " +
             "in ten minutes. Random can cluster -- it's genuinely random, which usually sounds less random.")]
    public TrackOrder trackOrder = TrackOrder.Shuffle;

    [Tooltip("How one track gives way to the next.")]
    public TransitionMode transition = TransitionMode.Crossfade;

    [Header("Crossfade")]
    [Tooltip("Overlap between outgoing and incoming track. Clamped to half the track length for very short clips.")]
    [Min(0f)] public float crossfadeDuration = 6f;

    [Header("Silence / Ambience")]
    [Min(0f)] public float fadeOutDuration = 4f;
    [Min(0f)] public float fadeInDuration = 4f;
    [Tooltip("Quiet stretch between tracks. A duration is rolled fresh for every gap.")]
    [Min(0f)] public float minSilence = 10f;
    [Min(0f)] public float maxSilence = 30f;

    [Header("Ambience")]
    [Tooltip("Played at random intervals during the gap, in Ambience mode only. Set each SoundEvent's " +
             "Mixer Group to Music so these follow the music slider rather than the SFX one.")]
    public SoundEvent[] ambienceSounds;
    [Min(0f)] public float minAmbienceInterval = 3f;
    [Min(0f)] public float maxAmbienceInterval = 12f;

    public bool HasTracks => tracks != null && tracks.Length > 0;

    public float RollSilenceDuration() => Random.Range(Mathf.Min(minSilence, maxSilence), Mathf.Max(minSilence, maxSilence));

    public float RollAmbienceInterval() =>
        Random.Range(Mathf.Min(minAmbienceInterval, maxAmbienceInterval), Mathf.Max(minAmbienceInterval, maxAmbienceInterval));

    public SoundEvent GetAmbienceSound() =>
        ambienceSounds == null || ambienceSounds.Length == 0 ? null : ambienceSounds[Random.Range(0, ambienceSounds.Length)];

    // RandomPerTransition resolves to one of the three concrete modes; Ambience degrades to plain
    // Silence when no ambience clips are assigned, so a half-filled asset still behaves sensibly.
    public TransitionMode ResolveTransition()
    {
        TransitionMode mode = transition;

        if (mode == TransitionMode.RandomPerTransition)
        {
            mode = (TransitionMode)Random.Range(0, 3);
        }

        if (mode == TransitionMode.Ambience && (ambienceSounds == null || ambienceSounds.Length == 0))
        {
            mode = TransitionMode.Silence;
        }

        return mode;
    }
}
