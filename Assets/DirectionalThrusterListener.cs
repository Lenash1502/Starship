using UnityEngine;

// This creates a dropdown menu in the Inspector
public enum ThrusterDirection
{
    Forward, Backward, Up, Down, Left, Right
}

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

        // Subscribe to the correct event based on the Inspector dropdown choice
        switch (thrusterType)
        {
            case ThrusterDirection.Forward: shipController.OnForwardThrust += HandleThrust; break;
            case ThrusterDirection.Backward: shipController.OnBackwardThrust += HandleThrust; break;
            case ThrusterDirection.Up: shipController.OnUpThrust += HandleThrust; break;
            case ThrusterDirection.Down: shipController.OnDownThrust += HandleThrust; break;
            case ThrusterDirection.Left: shipController.OnLeftThrust += HandleThrust; break;
            case ThrusterDirection.Right: shipController.OnRightThrust += HandleThrust; break;
        }
    }

    void OnDisable()
    {
        if (shipController == null) return;

        // Always unsubscribe to prevent memory leaks!
        switch (thrusterType)
        {
            case ThrusterDirection.Forward: shipController.OnForwardThrust -= HandleThrust; break;
            case ThrusterDirection.Backward: shipController.OnBackwardThrust -= HandleThrust; break;
            case ThrusterDirection.Up: shipController.OnUpThrust -= HandleThrust; break;
            case ThrusterDirection.Down: shipController.OnDownThrust -= HandleThrust; break;
            case ThrusterDirection.Left: shipController.OnLeftThrust -= HandleThrust; break;
            case ThrusterDirection.Right: shipController.OnRightThrust -= HandleThrust; break;
        }
    }

    private void HandleThrust(bool isBoosting)
    {
        if (isBoosting)
        {
            pSystem.Play();
        }
        else
        {
            pSystem.Stop();
        }
    }
}