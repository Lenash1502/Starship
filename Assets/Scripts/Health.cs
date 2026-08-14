using UnityEngine;
using System;

public class Health : MonoBehaviour, IDamageable
{
    [Tooltip("Starting and maximum health of this object.")]
    [SerializeField] private float maxHealth = 100f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    // Fires once, right before this object is destroyed, so other components
    // (e.g. an asteroid-splitting behavior) can react before the GameObject disappears.
    // Carries the killing blow's DamageCause so listeners (e.g. DeathEffects) can pick an
    // appropriate reaction per cause.
    public event Action<DamageCause> OnDied;

    // Fires on every non-lethal or lethal hit, with the raw damage amount, so other components
    // (e.g. an NPC's combat state machine) can react to being attacked.
    public event Action<float> OnDamaged;

    private float currentHealth;
    private bool isInitialized;
    private bool isDead;

    void Awake()
    {
        InitializeIfNeeded();
    }

    private void InitializeIfNeeded()
    {
        if (isInitialized) return;

        currentHealth = maxHealth;
        isInitialized = true;
    }

    // Lets code that spawns this object (e.g. AsteroidFieldGenerator) scale max health at runtime.
    public void SetMaxHealth(float value)
    {
        maxHealth = value;
        currentHealth = maxHealth;
        isInitialized = true;
    }

    public void TakeDamage(float amount, DamageCause cause = DamageCause.Unknown)
    {
        if (isDead) return;

        InitializeIfNeeded();

        currentHealth -= amount;
        OnDamaged?.Invoke(amount);

        if (currentHealth <= 0f)
        {
            isDead = true;
            OnDied?.Invoke(cause);
            Destroy(gameObject);
        }
    }
}
