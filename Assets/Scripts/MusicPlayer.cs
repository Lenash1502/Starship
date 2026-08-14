using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Drives a MusicPlaylist through SoundManager. Runs as one long coroutine rather than an Update
// state machine: "play a track, wait for its tail, fade out, sit in silence, repeat" is a sequence,
// and reads like one here.
//
// Belongs on the SoundManager GameObject. If you put it on a scene-local object instead, unloading
// that scene kills the coroutine and whatever track was playing never advances.
public class MusicPlayer : MonoBehaviour
{
    public MusicPlaylist playlist;
    public bool playOnStart = true;

    // Shuffle state is runtime-only and deliberately lives here, not on the playlist asset: fields
    // mutated on a ScriptableObject persist between play sessions in the editor, so a bag stored
    // there would come back half-empty on the next Play.
    private readonly List<int> bag = new();
    private int bagSourceCount = -1;
    private int lastIndex = -1;

    private Coroutine playbackRoutine;
    private Coroutine ambienceRoutine;

    public bool IsPlaying => playbackRoutine != null;

    void Start()
    {
        if (playOnStart) Play();
    }

    void OnDisable() => StopInternal();

    public void Play() => Play(playlist);

    public void Play(MusicPlaylist newPlaylist)
    {
        if (newPlaylist == null || !newPlaylist.HasTracks)
        {
            Debug.LogWarning($"{nameof(MusicPlayer)}: no playlist assigned, or it has no tracks.", this);
            return;
        }

        if (newPlaylist != playlist)
        {
            playlist = newPlaylist;
            ResetSelection();
        }

        StopInternal();
        playbackRoutine = StartCoroutine(PlaybackLoop());
    }

    public void Stop(float fadeDuration = -1f)
    {
        StopInternal();
        if (SoundManager.Instance != null) SoundManager.Instance.StopMusic(fadeDuration);
    }

    // Crossfades straight into a new track, discarding whatever the current one had left.
    public void Skip()
    {
        if (!IsPlaying) return;
        StopInternal();
        playbackRoutine = StartCoroutine(PlaybackLoop());
    }

    private void StopInternal()
    {
        if (playbackRoutine != null) StopCoroutine(playbackRoutine);
        if (ambienceRoutine != null) StopCoroutine(ambienceRoutine);
        playbackRoutine = null;
        ambienceRoutine = null;
    }

    // Gaps only ever fall between tracks, never before the first one -- opening the game on silence
    // would read as a loading bug rather than as pacing.
    private IEnumerator PlaybackLoop()
    {
        while (true)
        {
            if (SoundManager.Instance == null) yield break;

            AudioClip clip = SelectNextTrack();
            if (clip == null) yield break;

            MusicPlaylist.TransitionMode mode = playlist.ResolveTransition();

            // Guard against a fade longer than the track it's fading: without this the "wait for the
            // tail" check below is already true on the first frame and the loop spins through the
            // whole playlist in seconds.
            float halfTrack = clip.length * 0.5f;

            if (mode == MusicPlaylist.TransitionMode.Crossfade)
            {
                float fade = Mathf.Min(playlist.crossfadeDuration, halfTrack);
                SoundManager.Instance.PlayMusic(clip, false, fade);
                yield return WaitForTrackTail(fade);
            }
            else
            {
                float fadeOut = Mathf.Min(playlist.fadeOutDuration, halfTrack);
                SoundManager.Instance.PlayMusic(clip, false, Mathf.Min(playlist.fadeInDuration, halfTrack));

                yield return WaitForTrackTail(fadeOut);
                SoundManager.Instance.StopMusic(fadeOut);
                yield return new WaitForSecondsRealtime(fadeOut);

                yield return Gap(mode == MusicPlaylist.TransitionMode.Ambience);
            }
        }
    }

    // Unscaled throughout, matching SoundManager's fades: a track shouldn't freeze mid-crossfade
    // because the game paused with timeScale 0.
    private IEnumerator WaitForTrackTail(float tail)
    {
        // The source starts within PlayMusic, but MusicTimeRemaining reads 0 until the clip is
        // actually rolling, which would look like "already finished". One frame settles it.
        yield return null;

        while (SoundManager.Instance != null && SoundManager.Instance.MusicTimeRemaining > tail)
        {
            yield return null;
        }
    }

    private IEnumerator Gap(bool withAmbience)
    {
        float duration = playlist.RollSilenceDuration();

        if (!withAmbience)
        {
            yield return new WaitForSecondsRealtime(duration);
            yield break;
        }

        ambienceRoutine = StartCoroutine(AmbienceDuringGap(duration));
        yield return new WaitForSecondsRealtime(duration);
        ambienceRoutine = null;
    }

    private IEnumerator AmbienceDuringGap(float duration)
    {
        float elapsed = 0f;
        float nextAt = playlist.RollAmbienceInterval();

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            if (elapsed >= nextAt)
            {
                SoundEvent ambience = playlist.GetAmbienceSound();
                if (ambience != null && SoundManager.Instance != null) SoundManager.Instance.Play2D(ambience);
                nextAt = elapsed + playlist.RollAmbienceInterval();
            }

            yield return null;
        }
    }

    // --- Track selection ---

    private void ResetSelection()
    {
        bag.Clear();
        bagSourceCount = -1;
        lastIndex = -1;
    }

    private AudioClip SelectNextTrack()
    {
        AudioClip[] tracks = playlist.tracks;
        if (tracks == null || tracks.Length == 0) return null;

        int index = playlist.trackOrder switch
        {
            MusicPlaylist.TrackOrder.Sequential => (lastIndex + 1) % tracks.Length,
            MusicPlaylist.TrackOrder.Random => PickRandom(tracks.Length),
            _ => TakeFromBag(tracks.Length),
        };

        lastIndex = index;

        // A null entry in the middle of the array shouldn't kill playback for good.
        return tracks[index] != null ? tracks[index] : FindAnyClip(tracks);
    }

    // Picks uniformly from everything except the track that just played, by drawing from a range one
    // shorter and stepping over the excluded index.
    private int PickRandom(int count)
    {
        if (count == 1 || lastIndex < 0) return Random.Range(0, count);

        int index = Random.Range(0, count - 1);
        return index >= lastIndex ? index + 1 : index;
    }

    private int TakeFromBag(int count)
    {
        if (bag.Count == 0 || bagSourceCount != count)
        {
            bag.Clear();
            bagSourceCount = count;
            for (int i = 0; i < count; i++) bag.Add(i);
        }

        int pick = Random.Range(0, bag.Count);

        // A freshly refilled bag can otherwise open with the same track that closed the last one --
        // the one repeat shuffle is supposed to prevent.
        if (bag.Count > 1 && bag[pick] == lastIndex) pick = (pick + 1) % bag.Count;

        int index = bag[pick];
        bag.RemoveAt(pick);
        return index;
    }

    private static AudioClip FindAnyClip(AudioClip[] tracks)
    {
        foreach (AudioClip clip in tracks)
        {
            if (clip != null) return clip;
        }

        return null;
    }
}
