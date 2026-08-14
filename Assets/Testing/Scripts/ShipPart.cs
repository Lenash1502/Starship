using UnityEngine;

// Base stats shared by every physical part on a ship (boosters, hull sections, etc).
// Weight feeds into the ship's total Rigidbody mass; HP is a placeholder for a future damage system.
public class ShipPart : MonoBehaviour
{
    [Header("Part Stats")]
    [Tooltip("Mass this part contributes to the ship's total Rigidbody mass, in kg.")]
    public float weight = 1f;

    [Tooltip("Hit points this part can take before it's destroyed.")]
    public float maxHP = 100f;

    [HideInInspector] public float currentHP;

    protected virtual void Awake()
    {
        currentHP = maxHP;
    }
}
