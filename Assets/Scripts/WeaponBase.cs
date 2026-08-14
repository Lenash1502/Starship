using UnityEngine;
using System;
using System.Collections.Generic;

public enum WeaponGroup { Primary, Secondary }

public abstract class WeaponBase : MonoBehaviour
{
    public abstract WeaponGroup TargetGroup { get; }
    public abstract float FireRate { get; }
    public abstract bool IsBurstFiring { get; }
    public abstract float Damage { get; }
    public abstract DamageCause Cause { get; }

    // Fired once per actual shot (e.g. once per muzzle in a multi-muzzle weapon), with the world
    // position it fired from. Listener components (see WeaponFireSound) subscribe to this instead
    // of weapons knowing anything about audio.
    public event Action<Vector3> OnFired;
    protected void RaiseFired(Vector3 position) => OnFired?.Invoke(position);

    protected Transform[] muzzles;

    public virtual void Start()
    {
        List<Transform> muzzleList = new List<Transform>();
        Transform[] allChildren = GetComponentsInChildren<Transform>();

        string claimedMuzzleNames = "";

        foreach (Transform child in allChildren)
        {
            if (child.CompareTag("Muzzle"))
            {
                WeaponBase closestWeapon = child.GetComponentInParent<WeaponBase>();
                if (closestWeapon == this)
                {
                    muzzleList.Add(child);
                    claimedMuzzleNames += $"'{child.name}' ";
                }
            }
        }

        muzzles = muzzleList.ToArray();

        if (muzzles.Length > 0)
        {
            Debug.Log($"<color=cyan>[Weapon Setup]</color> Weapon <b>'{gameObject.name}'</b> successfully claimed {muzzles.Length} muzzles: {claimedMuzzleNames}");
        }
        else
        {
            Debug.LogWarning($"<color=red>[Weapon Error]</color> Weapon '{gameObject.name}' claimed 0 muzzles! Check your Muzzle tags.");
        }
    }

    public virtual void Aim(Vector3 targetPoint) { }

    // CHANGED: Now returns true if fired, false if the weapon rejected the command
    public abstract bool TriggerFire(Vector3 targetPoint);
}