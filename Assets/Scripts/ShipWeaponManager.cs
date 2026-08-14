using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum FireMode { Sequential, Linked }

public class ShipWeaponManager : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public float targetingRange = 1000f;
    public LayerMask targetingMask = ~0;

    [Header("Fire Mode")]
    public FireMode currentFireMode = FireMode.Sequential;

    private PlayerControls controls;
    private InputAction switchModeAction;

    // Input tracking variables
    private bool isTriggerHeld = false;
    private float inputBufferTimer = 0f;
    private float bufferWindow = 0.2f; // Remembers a quick click for 200 milliseconds

    private List<WeaponBase> primaryWeapons = new List<WeaponBase>();
    private List<WeaponBase> secondaryWeapons = new List<WeaponBase>();

    private float nextFireTime;
    private int currentWeaponIndex = 0;

    // True for a real player; false when an AIWanderPilot on the same object drives targeting and
    // firing instead via SetAIFiring(), same split as SpaceshipController.IsPlayerControlled.
    public bool IsPlayerControlled { get; private set; }

    private Vector3 aiTargetPoint;
    private bool aiWantsToFire;

    void Awake()
    {
        IsPlayerControlled = GetComponent<AIWanderPilot>() == null;
        if (!IsPlayerControlled) return;

        controls = new PlayerControls();

        // Track both holding and clicking via input events
        controls.Ship.PrimaryWeapon.performed += ctx => {
            isTriggerHeld = true;
            inputBufferTimer = Time.time + bufferWindow; // Buffer a quick click
        };
        controls.Ship.PrimaryWeapon.canceled += ctx => {
            isTriggerHeld = false;
        };

        switchModeAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/x");
        switchModeAction.performed += ctx => ToggleFireMode();
    }

    void OnEnable()
    {
        if (!IsPlayerControlled) return;

        controls.Enable();
        switchModeAction.Enable();
    }

    void OnDisable()
    {
        if (!IsPlayerControlled) return;

        controls.Disable();
        switchModeAction.Disable();
    }

    // AI control surface: an AIWanderPilot calls this instead of the mouse/trigger driving
    // targetPoint/wantsToFire, so the firing logic in Update() behaves identically either way.
    public void SetAIFiring(Vector3 targetPoint, bool wantsToFire)
    {
        aiTargetPoint = targetPoint;
        aiWantsToFire = wantsToFire;
    }

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        WeaponBase[] allWeapons = GetComponentsInChildren<WeaponBase>();
        foreach (WeaponBase weapon in allWeapons)
        {
            if (weapon.CompareTag("PrimaryFire") && weapon.TargetGroup == WeaponGroup.Primary)
            {
                primaryWeapons.Add(weapon);
            }
            else if (weapon.TargetGroup == WeaponGroup.Secondary)
            {
                secondaryWeapons.Add(weapon);
            }
        }
    }

    private void ToggleFireMode()
    {
        if (isTriggerHeld) return;

        currentFireMode = currentFireMode == FireMode.Sequential ? FireMode.Linked : FireMode.Sequential;
        Debug.Log("<color=green>[Manager]</color> Switched Fire Mode to: " + currentFireMode);

        currentWeaponIndex = 0;
    }

    void Update()
    {
        Vector3 targetPoint = IsPlayerControlled ? GetCrosshairTargetPoint() : aiTargetPoint;

        foreach (WeaponBase weapon in primaryWeapons)
        {
            weapon.Aim(targetPoint);
        }

        // --- UNIFIED FIRING LOGIC WITH INPUT BUFFERING ---
        // Player: fire if the button is currently held OR if we have a buffered click waiting.
        // AI: fire exactly when AIWanderPilot's last SetAIFiring() call said to.
        bool wantsToFire = IsPlayerControlled ? (isTriggerHeld || Time.time < inputBufferTimer) : aiWantsToFire;

        if (wantsToFire && primaryWeapons.Count > 0)
        {
            if (Time.time >= nextFireTime)
            {
                float masterFireRate = primaryWeapons[0].FireRate;
                bool successfullyFired = false;

                if (currentFireMode == FireMode.Linked)
                {
                    foreach (var weapon in primaryWeapons)
                    {
                        if (weapon.TriggerFire(targetPoint)) successfullyFired = true;
                    }

                    if (successfullyFired) nextFireTime = Time.time + masterFireRate;
                }
                else if (currentFireMode == FireMode.Sequential)
                {
                    if (primaryWeapons[currentWeaponIndex].TriggerFire(targetPoint))
                    {
                        currentWeaponIndex = (currentWeaponIndex + 1) % primaryWeapons.Count;
                        nextFireTime = Time.time + (masterFireRate / primaryWeapons.Count);
                        successfullyFired = true;
                    }
                }

                // If it was just a quick single click (not held down), consume the buffer 
                // so it fires once and stops instead of looping infinitely.
                if (successfullyFired && !isTriggerHeld)
                {
                    inputBufferTimer = 0f;
                }
            }
        }
    }

    private Vector3 GetCrosshairTargetPoint()
    {
        if (Mouse.current == null) return transform.position + (transform.forward * targetingRange);

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray cameraRay = mainCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(cameraRay, out RaycastHit camHit, targetingRange, targetingMask, QueryTriggerInteraction.Ignore))
        {
            if (camHit.transform.root != transform.root) return camHit.point;
        }

        return cameraRay.GetPoint(targetingRange);
    }
}