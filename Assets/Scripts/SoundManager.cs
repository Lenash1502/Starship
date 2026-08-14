using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// Central playback: a pool of reusable AudioSources for one-shot SFX, plus a dedicated pair of
// sources for crossfading music. Gameplay code never references this directly -- small listener
// components (WeaponFireSound, DeathSound, ImpactSound, ...) subscribe to existing gameplay events
// and call SoundManager.Instance.Play() in response, exactly like DirectionalThrusterListener
// already does for thruster VFX. That keeps weapons/health/collision code completely audio-agnostic.
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("SFX Pool")]
    [Tooltip("AudioSources created up front. The pool grows automatically if every source is busy.")]
    public int initialPoolSize = 16;

    [Header("Mixer Routing")]
    [Tooltip("Fallback group for SoundEvents that don't specify one of their own. Leave blank to output straight to Master.")]
    public AudioMixerGroup defaultSfxGroup;
    [Tooltip("Group the two music sources output to. Category volume is controlled here, not per-source.")]
    public AudioMixerGroup musicGroup;

    [Header("Music")]
    [Tooltip("Seconds to crossfade when PlayMusic/StopMusic is called without an explicit duration.")]
    public float defaultMusicFadeDuration = 1.5f;

    private readonly List<AudioSource> pool = new();
    private AudioSource musicSourceA;
    private AudioSource musicSourceB;
    private AudioSource activeMusicSource;
    private Coroutine musicFadeRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < initialPoolSize; i++) pool.Add(CreatePooledSource());

        musicSourceA = CreateMusicSource();
        musicSourceB = CreateMusicSource();
        activeMusicSource = musicSourceA;
    }

    private AudioSource CreatePooledSource()
    {
        GameObject go = new("PooledAudioSource");
        go.transform.SetParent(transform);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = defaultSfxGroup;
        return source;
    }

    private AudioSource CreateMusicSource()
    {
        GameObject go = new("MusicSource");
        go.transform.SetParent(transform);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.outputAudioMixerGroup = musicGroup;
        return source;
    }

    // --- SFX ---

    // Positional one-shot: weapon fire, impacts, deaths.
    public void Play(SoundEvent sound, Vector3 position)
    {
        AudioClip clip = sound != null ? sound.GetClip() : null;
        if (clip == null) return;

        AudioSource source = GetFreeSource();
        source.transform.position = position;
        Configure(source, sound, clip);
        source.Play();
    }

    // Non-positional: UI feedback.
    public void Play2D(SoundEvent sound)
    {
        AudioClip clip = sound != null ? sound.GetClip() : null;
        if (clip == null) return;

        AudioSource source = GetFreeSource();
        Configure(source, sound, clip);
        source.spatialBlend = 0f;
        source.Play();
    }

    private void Configure(AudioSource source, SoundEvent sound, AudioClip clip)
    {
        source.clip = clip;
        source.volume = sound.GetVolume();
        source.pitch = sound.GetPitch();
        source.spatialBlend = sound.spatialBlend;
        source.minDistance = sound.minDistance;
        source.maxDistance = sound.maxDistance;
        source.outputAudioMixerGroup = sound.mixerGroup != null ? sound.mixerGroup : defaultSfxGroup;
    }

    private AudioSource GetFreeSource()
    {
        foreach (AudioSource source in pool)
        {
            if (!source.isPlaying) return source;
        }

        AudioSource created = CreatePooledSource();
        pool.Add(created);
        return created;
    }

    // --- Music ---

    public bool IsMusicPlaying => activeMusicSource != null && activeMusicSource.isPlaying;

    // Seconds left on the track that's currently taking over. MusicPlayer uses this to start the
    // next track early enough that the two overlap, which is what makes a crossfade seamless --
    // waiting for the clip to actually end would leave a hole. Returns 0 when nothing is playing.
    public float MusicTimeRemaining
    {
        get
        {
            if (activeMusicSource == null || !activeMusicSource.isPlaying) return 0f;
            if (activeMusicSource.clip == null) return 0f;
            return Mathf.Max(0f, activeMusicSource.clip.length - activeMusicSource.time);
        }
    }

    public void PlayMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f)
    {
        if (clip == null) return;
        if (fadeDuration < 0f) fadeDuration = defaultMusicFadeDuration;

        AudioSource incoming = activeMusicSource == musicSourceA ? musicSourceB : musicSourceA;
        incoming.clip = clip;
        incoming.loop = loop;
        incoming.Play();

        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(CrossfadeMusic(activeMusicSource, incoming, fadeDuration));
        activeMusicSource = incoming;
    }

    public void StopMusic(float fadeDuration = -1f)
    {
        if (fadeDuration < 0f) fadeDuration = defaultMusicFadeDuration;
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(FadeOut(activeMusicSource, fadeDuration));
    }

    // Fades between 0 and full source volume, not between 0 and a user volume setting: how loud
    // music sits overall is the Music mixer group's job, so the player can move that slider
    // mid-track and hear it immediately instead of waiting for the next crossfade.
    private IEnumerator CrossfadeMusic(AudioSource outgoing, AudioSource incoming, float duration)
    {
        float elapsed = 0f;
        bool fadingOutgoing = outgoing.isPlaying;
        float outgoingStart = outgoing.volume;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? elapsed / duration : 1f;

            incoming.volume = Mathf.Lerp(0f, 1f, t);
            if (fadingOutgoing) outgoing.volume = Mathf.Lerp(outgoingStart, 0f, t);

            yield return null;
        }

        incoming.volume = 1f;
        if (fadingOutgoing)
        {
            outgoing.volume = 0f;
            outgoing.Stop();
        }
    }

    private IEnumerator FadeOut(AudioSource source, float duration)
    {
        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, duration > 0f ? elapsed / duration : 1f);
            yield return null;
        }

        source.volume = 0f;
        source.Stop();
    }
}
