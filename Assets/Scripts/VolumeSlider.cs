using UnityEngine;
using UnityEngine.UI;

// Binds one UI Slider to one AudioVolumeSettings channel. Doing this in code rather than through
// the Slider's OnValueChanged UnityEvent means the slider also *reads back* the saved value when
// the options menu opens, instead of showing whatever position it was left at in the prefab.
[RequireComponent(typeof(Slider))]
public class VolumeSlider : MonoBehaviour
{
    public AudioVolumeSettings.Channel channel = AudioVolumeSettings.Channel.Master;

    private Slider slider;

    void Awake()
    {
        slider = GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    void OnEnable()
    {
        if (AudioVolumeSettings.Instance != null)
        {
            slider.SetValueWithoutNotify(AudioVolumeSettings.Instance.GetVolume(channel));
        }

        slider.onValueChanged.AddListener(HandleValueChanged);
    }

    void OnDisable()
    {
        slider.onValueChanged.RemoveListener(HandleValueChanged);

        // Flush to disk once the player is done dragging, not on every frame of the drag.
        if (AudioVolumeSettings.Instance != null) AudioVolumeSettings.Instance.Save();
    }

    private void HandleValueChanged(float value)
    {
        if (AudioVolumeSettings.Instance != null) AudioVolumeSettings.Instance.SetVolume(channel, value);
    }
}
