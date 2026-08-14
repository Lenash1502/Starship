using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class DirectionalThrusterListener : MonoBehaviour
{
    [Tooltip("Drag the main Spaceship object here")]
    public SpaceshipController shipController;

    [Tooltip("Which direction should trigger this specific booster?")]
    public ThrusterDirection thrusterType;

    private ParticleSystem pSystem;

    void Awake()
    {
        pSystem = GetComponent<ParticleSystem>();
    }

    void OnEnable()
    {
        if (shipController == null) return;
        shipController.OnThrustStateChanged += HandleThrustStateChanged;
    }

    void OnDisable()
    {
        if (shipController == null) return;

        // Always unsubscribe to prevent memory leaks!
        shipController.OnThrustStateChanged -= HandleThrustStateChanged;
    }

    private void HandleThrustStateChanged(ThrusterDirection direction, bool isActive)
    {
        // Only react to the direction this booster was assigned to in the Inspector
        if (direction != thrusterType) return;

        if (isActive)
        {
            pSystem.Play();
        }
        else
        {
            pSystem.Stop();
        }
    }
}