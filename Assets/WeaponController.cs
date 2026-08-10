using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The main camera used for aiming. Leave blank to auto-detect Camera.main.")]
    public Camera mainCamera;
    [Tooltip("The LineRenderer prefab that acts as the laser beam visual.")]
    public GameObject laserVisualPrefab;

    [Header("Weapon Settings")]
    [Tooltip("How far the lasers can travel.")]
    public float weaponRange = 1000f;
    [Tooltip("Time in seconds between shots.")]
    public float fireRate = 0.15f;
    [Tooltip("The physics layers the lasers are allowed to hit (e.g., Asteroids).")]
    public LayerMask hitMask = ~0;

    private PlayerControls controls;
    private List<Transform> primaryMuzzles = new List<Transform>();

    private bool isFiring;
    private float nextFireTime;

    void Awake()
    {
        controls = new PlayerControls();
        controls.Ship.PrimaryWeapon.performed += ctx => isFiring = true;
        controls.Ship.PrimaryWeapon.canceled += ctx => isFiring = false;
    }

    void OnEnable() => controls.Enable();
    void OnDisable() => controls.Disable();

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // Automatically find all child hardpoints tagged "PrimaryFire"
        Transform[] allChildren = GetComponentsInChildren<Transform>();
        foreach (Transform child in allChildren)
        {
            if (child.CompareTag("PrimaryFire"))
            {
                primaryMuzzles.Add(child);
            }
        }

        if (primaryMuzzles.Count == 0)
        {
            Debug.LogWarning("No objects found with the 'PrimaryFire' tag!");
        }
    }

    void Update()
    {
        if (isFiring && Time.time >= nextFireTime)
        {
            FireWeapons();
            nextFireTime = Time.time + fireRate;
        }
    }

    private void FireWeapons()
    {
        if (Mouse.current == null || primaryMuzzles.Count == 0) return;
        if (mainCamera == null) mainCamera = Camera.main;

        // ---------------------------------------------------------
        // STEP 1: Find the target point in 3D space via the Crosshair
        // ---------------------------------------------------------
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray cameraRay = mainCamera.ScreenPointToRay(mousePos);
        Vector3 targetPoint;

        // Raycast from camera to find what the crosshair is pointing at
        if (Physics.Raycast(cameraRay, out RaycastHit camHit, weaponRange, hitMask))
        {
            // Make sure the camera ray doesn't accidentally lock onto the player's own ship
            if (camHit.transform.root != transform.root)
            {
                targetPoint = camHit.point;
            }
            else
            {
                targetPoint = cameraRay.GetPoint(weaponRange);
            }
        }
        else
        {
            targetPoint = cameraRay.GetPoint(weaponRange);
        }

        // ---------------------------------------------------------
        // STEP 2: Fire lasers from the muzzles toward the target point
        // ---------------------------------------------------------
        foreach (Transform muzzle in primaryMuzzles)
        {
            Vector3 fireDirection = (targetPoint - muzzle.position).normalized;
            Vector3 impactPoint = muzzle.position + (fireDirection * weaponRange);

            // Raycast from the gun muzzle. We use RaycastAll to safely skip hitting our own ship.
            RaycastHit[] hits = Physics.RaycastAll(muzzle.position, fireDirection, weaponRange, hitMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); // Sort by closest hit

            foreach (var hit in hits)
            {
                // Ignore hits on our own spaceship's colliders
                if (hit.transform.root != transform.root)
                {
                    impactPoint = hit.point;

                    // TODO: Deal damage to hit.collider here if needed!
                    break;
                }
            }

            // ---------------------------------------------------------
            // STEP 3: Generate the VFX Line
            // ---------------------------------------------------------
            if (laserVisualPrefab != null)
            {
                GameObject laser = Instantiate(laserVisualPrefab, muzzle.position, Quaternion.identity);
                LaserParticleVisual vfx = laser.GetComponent<LaserParticleVisual>(); // <-- TO THIS
                if (vfx != null)
                {
                    vfx.SetBeam(muzzle.position, impactPoint);
                }
            }
        }
    }
}