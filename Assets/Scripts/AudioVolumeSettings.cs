using UnityEngine;
using UnityEngine.Audio;

// User-facing volume control. Category volume lives on the AudioMixer rather than on individual
// AudioSources: SoundManager's pooled sources and EngineSound's own source all route into a mixer
// group, so one exposed parameter turns down every sound in that category at once -- including
// sounds already mid-playback, which per-source volume scaling can't do.
//
// Goes on the SoundManager GameObject (it inherits that object's DontDestroyOnLoad, so settings
// survive scene loads).
public class AudioVolumeSettings : MonoBehaviour
{
    public enum Channel { Master, Music, Sfx, Ui }

    public static AudioVolumeSettings Instance { get; private set; }

    [Header("Mixer")]
    public AudioMixer mixer;

    [Header("Exposed Parameter Names")]
    [Tooltip("Must match the names given when exposing each group's Volume in the Audio Mixer window.")]
    public string masterParameter = "MasterVolume";
    public string musicParameter = "MusicVolume";
    public string sfxParameter = "SFXVolume";
    public string uiParameter = "UIVolume";

    [Header("Defaults")]
    [Range(0f, 1f)] public float defaultMaster = 1f;
    [Range(0f, 1f)] public float defaultMusic = 0.6f;
    [Range(0f, 1f)] public float defaultSfx = 1f;
    [Range(0f, 1f)] public float defaultUi = 1f;

    // Mixer volumes are decibels, but sliders and saved settings are a linear 0-1. Below this the
    // conversion is meaningless, so anything quieter is treated as fully muted.
    private const float MinLinear = 0.0001f;
    private const float MutedDecibels = -80f;
    private const string PrefKeyPrefix = "Audio.Volume.";

    private readonly float[] volumes = new float[4];

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        for (int i = 0; i < volumes.Length; i++)
        {
            Channel channel = (Channel)i;
            volumes[i] = PlayerPrefs.GetFloat(PrefKey(channel), DefaultFor(channel));
        }
    }

    // Applied in Start rather than Awake: AudioMixer.SetFloat is unreliable before the mixer has
    // finished initialising, and silently does nothing if called too early.
    void Start() => ApplyAll();

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public float GetVolume(Channel channel) => volumes[(int)channel];

    public void SetVolume(Channel channel, float linear)
    {
        linear = Mathf.Clamp01(linear);
        volumes[(int)channel] = linear;
        Apply(channel, linear);
        PlayerPrefs.SetFloat(PrefKey(channel), linear);
    }

    public void ResetToDefaults()
    {
        for (int i = 0; i < volumes.Length; i++) SetVolume((Channel)i, DefaultFor((Channel)i));
    }

    // Save is deferred rather than flushed on every slider tick -- dragging a slider fires
    // SetVolume many times per second, and PlayerPrefs.Save writes to disk synchronously.
    public void Save() => PlayerPrefs.Save();

    private void ApplyAll()
    {
        for (int i = 0; i < volumes.Length; i++) Apply((Channel)i, volumes[i]);
    }

    private void Apply(Channel channel, float linear)
    {
        if (mixer == null) return;

        string parameter = ParameterFor(channel);
        if (string.IsNullOrEmpty(parameter)) return;

        if (!mixer.SetFloat(parameter, LinearToDecibels(linear)))
        {
            Debug.LogWarning(
                $"{nameof(AudioVolumeSettings)}: mixer has no exposed parameter named '{parameter}'. " +
                "Expose the group's Volume in the Audio Mixer window and rename it to match.", this);
        }
    }

    private string ParameterFor(Channel channel) => channel switch
    {
        Channel.Master => masterParameter,
        Channel.Music => musicParameter,
        Channel.Sfx => sfxParameter,
        Channel.Ui => uiParameter,
        _ => null,
    };

    private float DefaultFor(Channel channel) => channel switch
    {
        Channel.Master => defaultMaster,
        Channel.Music => defaultMusic,
        Channel.Sfx => defaultSfx,
        Channel.Ui => defaultUi,
        _ => 1f,
    };

    private static string PrefKey(Channel channel) => PrefKeyPrefix + channel;

    public static float LinearToDecibels(float linear) =>
        linear <= MinLinear ? MutedDecibels : Mathf.Log10(linear) * 20f;

    public static float DecibelsToLinear(float decibels) =>
        decibels <= MutedDecibels ? 0f : Mathf.Pow(10f, decibels / 20f);

    // --- Convenience hooks for UnityEvent wiring in the Inspector ---

    public void SetMasterVolume(float linear) => SetVolume(Channel.Master, linear);
    public void SetMusicVolume(float linear) => SetVolume(Channel.Music, linear);
    public void SetSfxVolume(float linear) => SetVolume(Channel.Sfx, linear);
    public void SetUiVolume(float linear) => SetVolume(Channel.Ui, linear);
}
