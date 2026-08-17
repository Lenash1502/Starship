using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class LaserWeapon : WeaponBase
{
    [Header("Base Requirements")]
    [SerializeField] private WeaponGroup weaponGroup = WeaponGroup.Primary;
    [SerializeField] private float fireRate = 0.5f;
    [SerializeField] private float damage = 10f;

    [Header("Firing Mode")]
    [SerializeField] private bool isBurstFiring = false;

    public override WeaponGroup TargetGroup => weaponGroup;
    public override float FireRate => fireRate;
    public override bool IsBurstFiring => isBurstFiring;
    public override float Damage => damage;
    public override DamageCause Cause => DamageCause.LaserWeapon;

    [Header("Weapon Specific Settings")]
    public float weaponRange = 1000f;
    public LayerMask hitMask = ~0;
    public GameObject impactPrefab;

    private WeaponMuzzle[] weaponMuzzles;

    // THE LOCK: Prevents overlap and spamming
    private bool isCurrentlyFiring = false;

    public override void Start()
    {
        base.Start();

        List<WeaponMuzzle> validMuzzles = new List<WeaponMuzzle>();
        for (int i = 0; i < muzzles.Length; i++)
        {
            WeaponMuzzle wm = muzzles[i].GetComponent<WeaponMuzzle>();
            if (wm != null) validMuzzles.Add(wm);
        }
        weaponMuzzles = validMuzzles.ToArray();
    }

    public override void Aim(Vector3 targetPoint)
    {
        if (weaponMuzzles == null) return;
        foreach (var muzzle in weaponMuzzles)
        {
            if (muzzle != null) muzzle.AimAt(targetPoint);
        }
    }

    public override bool TriggerFire(Vector3 targetPoint)
    {
        if (weaponMuzzles == null || weaponMuzzles.Length == 0) return false;

        // REJECT OVERRIDE: If the coroutine is already running, completely ignore the click!
        if (isCurrentlyFiring) return false;

        if (IsBurstFiring && weaponMuzzles.Length > 1)
        {
            StartCoroutine(BurstFireRoutine(targetPoint));
        }
        else
        {
            StartCoroutine(SimultaneousFireRoutine(targetPoint));
        }

        return true; // We successfully accepted the command
    }

    private IEnumerator BurstFireRoutine(Vector3 targetPoint)
    {
        isCurrentlyFiring = true; // Lock the weapon

        float delayBetweenShots = FireRate / weaponMuzzles.Length;

        for (int i = 0; i < weaponMuzzles.Length; i++)
        {
            if (weaponMuzzles[i] != null) ExecuteFire(weaponMuzzles[i], targetPoint);

            // Wait between shots
            if (i < weaponMuzzles.Length - 1) yield return new WaitForSeconds(delayBetweenShots);
        }

        // Wait out the final shot's cooldown slice so the total lock time perfectly equals FireRate
        yield return new WaitForSeconds(delayBetweenShots);

        isCurrentlyFiring = false; // Unlock the weapon
    }

    private IEnumerator SimultaneousFireRoutine(Vector3 targetPoint)
    {
        isCurrentlyFiring = true; // Lock the weapon

        foreach (var muzzle in weaponMuzzles)
        {
            if (muzzle != null) ExecuteFire(muzzle, targetPoint);
        }

        // Lock the weapon out for its entire cooldown duration
        yield return new WaitForSeconds(FireRate);

        isCurrentlyFiring = false; // Unlock the weapon
    }

    private void ExecuteFire(WeaponMuzzle currentMuzzle, Vector3 targetPoint)
    {
        Vector3 fireDirection = currentMuzzle.transform.up;

        Vector3 impactPoint = currentMuzzle.transform.position + (fireDirection * weaponRange);
        Vector3 impactNormal = -fireDirection;
        bool hitSomething = false;

        RaycastHit[] hits = Physics.RaycastAll(currentMuzzle.transform.position, fireDirection, weaponRange, hitMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.transform.root != transform.root)
            {
                impactPoint = hit.point;
                impactNormal = hit.normal;
                hitSomething = true;

                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(Damage, Cause);

                break;
            }
        }

        // The muzzle stretches its beam to end here, so the visual matches the raycast exactly --
        // at the hit when there is one, at the edge of weaponRange when the shot goes wide.
        currentMuzzle.FireVisuals(impactPoint);
        RaiseFired(currentMuzzle.transform.position);

        if (hitSomething && impactPrefab != null)
        {
            Instantiate(impactPrefab, impactPoint, Quaternion.LookRotation(impactNormal));
        }
    }
}