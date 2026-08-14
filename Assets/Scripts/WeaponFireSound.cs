using UnityEngine;

[RequireComponent(typeof(WeaponBase))]
public class WeaponFireSound : MonoBehaviour
{
    public SoundEvent fireSound;

    private WeaponBase weapon;

    void Awake() => weapon = GetComponent<WeaponBase>();
    void OnEnable() => weapon.OnFired += HandleFired;
    void OnDisable() => weapon.OnFired -= HandleFired;

    private void HandleFired(Vector3 position)
    {
        if (fireSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(fireSound, position);
    }
}
