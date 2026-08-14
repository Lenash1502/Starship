using UnityEngine;

public class VFXCleanup : MonoBehaviour
{
    [Tooltip("How many seconds before this VFX deletes itself.")]
    public float lifetime = 2f;

    void Awake()
    {
        // Instantly starts a countdown to destroy the object the moment it spawns
        Destroy(gameObject, lifetime);
    }
}