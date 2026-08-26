using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Drives the "build your own flyer" screen.
//
// Flow: the menu opens on the core list, because a ship is nothing until it has a hull. Placing a
// core drops it onto the selection circle and lights up every "<Category>HardPoint" empty inside it
// as a clickable blob. Clicking a blob narrows the right hand list to the parts that fit there,
// hovering a list entry ghosts that model into the socket, and clicking it bolts the real prefab on.
// Parts carry their own hard points, so the same loop repeats all the way out to the wing tips.
[DisallowMultipleComponent]
public class ShipBuilder : MonoBehaviour
{
    [Header("Data")]
    public ShipPartCatalog catalog;

    [Header("Scene")]
    [Tooltip("Stand the ship is built on. Found by name at startup when left empty.")]
    public Transform selectionCircle;
    public string selectionCircleName = "SelectionCircle";
    [Tooltip("Local offset of the assembly from the selection circle origin.")]
    public Vector3 assemblyOffset = Vector3.zero;
    public Camera builderCamera;

    [Header("Hard Point Markers")]
    public float markerSize = 0.35f;
    [Tooltip("Hide sockets that no prefab in the catalog can fill yet. Off while parts are still " +
             "being authored, so the empty mounts stay visible.")]
    public bool hideEmptyHardPoints = false;
    public Color markerIdleColor = new Color(0.35f, 0.8f, 1f, 0.55f);
    public Color markerOccupiedColor = new Color(0.35f, 1f, 0.55f, 0.3f);
    public Color markerSelectedColor = new Color(1f, 0.6f, 0.15f, 1f);

    [Header("Preview")]
    public Color ghostColor = new Color(0.35f, 0.85f, 1f, 0.3f);
    public Color ghostRimColor = new Color(0.6f, 0.95f, 1f, 1f);

    [Header("Rotation")]
    [Tooltip("Degrees of yaw/pitch per pixel of mouse travel while dragging the model.")]
    public float rotationSpeed = 0.35f;
    [Tooltip("Pixels of travel before a press counts as a drag rather than a click.")]
    public float dragThreshold = 4f;

    [Header("Camera Framing")]
    [Tooltip("Pull the camera back along its own view direction until the whole ship fits.")]
    public bool autoFrameCamera = true;
    [Tooltip("Room left around the ship. 1 is a tight fit against the edges of the view.")]
    public float framingPadding = 1.15f;
    [Tooltip("Seconds the camera takes to settle on a new distance. 0 snaps straight there.")]
    public float framingSmoothTime = 0.25f;
    [Tooltip("Fraction of the screen width the parts panel covers, so the ship centres in what is " +
             "left of the view. ShipBuilderUI keeps this up to date on its own.")]
    [Range(0f, 0.6f)] public float uiPanelFraction = 0f;

    // Raised when the right hand list should show something else, and when the ship itself changed.
    public event Action OfferChanged;
    public event Action AssemblyChanged;

    public Transform AssemblyRoot { get; private set; }
    public PlacedPart Core { get; private set; }
    public HardPoint SelectedHardPoint { get; private set; }
    public bool IsChoosingCore { get; private set; } = true;

    // What the menu is currently offering, plus the headings that describe it.
    public List<ShipPartDefinition> CurrentOffer { get; private set; } = new List<ShipPartDefinition>();
    public string OfferTitle { get; private set; } = "Core";
    public string OfferSubtitle { get; private set; } = "Choose a hull to start from";

    readonly List<HardPoint> hardPoints = new List<HardPoint>();

    Material ghostMaterial;
    Material markerMaterial;
    Mesh markerMesh;

    GameObject ghost;
    ShipPartDefinition ghostDefinition;
    PlacedPart hiddenForPreview;

    bool dragging;
    bool pressStartedOnModel;
    Vector2 pressTravel;

    Vector3 framingDirection = Vector3.forward;
    Vector3 framingTarget;
    Vector3 framingVelocity;
    bool hasFramingTarget;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");

    void Awake()
    {
        if (builderCamera == null) builderCamera = Camera.main;
        if (builderCamera != null) framingDirection = builderCamera.transform.forward;

        if (selectionCircle == null) selectionCircle = FindSelectionCircle(selectionCircleName);

        // Everything the player builds lives under one child of the stand, so spinning the ship
        // never spins the stand itself. The child sits on the stand transform exactly - the stand
        // is a marker in the scene, not something the ship has to clear.
        var rootObject = new GameObject("Assembly");
        AssemblyRoot = rootObject.transform;
        AssemblyRoot.SetParent(selectionCircle != null ? selectionCircle : transform, false);
        AssemblyRoot.localPosition = assemblyOffset;

        AssemblyChanged += RequestFraming;

        CreateSharedResources();
    }

    // Looks the stand up by name, tolerating the spacing the object happens to use: "SelectionCircle",
    // "Selection Circle" and "selection_circle" all resolve to the same anchor.
    public static Transform FindSelectionCircle(string wantedName)
    {
        if (string.IsNullOrEmpty(wantedName)) return null;

        GameObject exact = GameObject.Find(wantedName);
        if (exact != null) return exact.transform;

        string wanted = NormalizeName(wantedName);
        foreach (Transform candidate in FindObjectsByType<Transform>())
        {
            if (NormalizeName(candidate.name) == wanted) return candidate;
        }
        return null;
    }

    static string NormalizeName(string value)
    {
        return value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
    }

    void Start()
    {
        ShowCoreOffer();
    }

    void OnDestroy()
    {
        if (ghostMaterial != null) Destroy(ghostMaterial);
        if (markerMaterial != null) Destroy(markerMaterial);
    }

    void CreateSharedResources()
    {
        ghostMaterial = new Material(LoadShader("ShipBuilderGhost", "ShipBuilder/Ghost"));
        if (ghostMaterial.HasProperty(ColorId)) ghostMaterial.SetColor(ColorId, ghostColor);
        if (ghostMaterial.HasProperty(RimColorId)) ghostMaterial.SetColor(RimColorId, ghostRimColor);

        markerMaterial = new Material(LoadShader("ShipBuilderOverlay", "ShipBuilder/Overlay"));

        // One sphere mesh borrowed from a throwaway primitive, shared by every marker.
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerMesh = template.GetComponent<MeshFilter>().sharedMesh;
        Destroy(template);
    }

    // The builder shaders live in a Resources folder so a player build keeps them: nothing in the
    // scene references them directly, and Unity strips shaders it cannot see being used.
    static Shader LoadShader(string resourceName, string shaderName)
    {
        Shader shader = Resources.Load<Shader>(resourceName);
        if (shader == null) shader = Shader.Find(shaderName);
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return shader;
    }

    // ---------------------------------------------------------------- offers

    public void ShowCoreOffer()
    {
        ClearHover();
        SetSelectedHardPoint(null);
        IsChoosingCore = true;
        OfferTitle = "Core";
        OfferSubtitle = Core == null ? "Choose a hull to start from" : "Swap the hull (clears the build)";
        CurrentOffer = catalog != null ? new List<ShipPartDefinition>(catalog.GetCores()) : new List<ShipPartDefinition>();
        OfferChanged?.Invoke();
    }

    public void SelectHardPoint(HardPoint hardPoint)
    {
        if (hardPoint == null)
        {
            ShowCoreOffer();
            return;
        }

        ClearHover();
        SetSelectedHardPoint(hardPoint);
        IsChoosingCore = false;
        OfferTitle = PartNaming.PrettyCategory(hardPoint.category);

        string tail = PartNaming.PrettySuffix(hardPoint.suffix);
        OfferSubtitle = string.IsNullOrEmpty(tail) ? "Mount point" : tail + " mount point";

        CurrentOffer = catalog != null
            ? new List<ShipPartDefinition>(catalog.GetPartsFor(hardPoint.category, hardPoint.side))
            : new List<ShipPartDefinition>();

        OfferChanged?.Invoke();
    }

    void SetSelectedHardPoint(HardPoint hardPoint)
    {
        HardPoint previous = SelectedHardPoint;
        SelectedHardPoint = hardPoint;
        RefreshMarker(previous);
        RefreshMarker(hardPoint);
    }

    // ---------------------------------------------------------------- placing

    // Ghosts the part the pointer is resting on into the socket it would land in, so the player can
    // see the fit before committing. Called from the list entry hover events.
    public void HoverPart(ShipPartDefinition definition)
    {
        if (definition == null || !definition.IsValid) return;
        if (!IsChoosingCore && SelectedHardPoint == null) return;
        if (ghostDefinition == definition) return;

        ClearHover();
        ghostDefinition = definition;

        Transform anchor = IsChoosingCore ? AssemblyRoot : SelectedHardPoint.transform;
        ghost = Instantiate(definition.prefab, anchor);
        ghost.name = "PartPreview";
        ghost.transform.localPosition = Vector3.zero;
        ghost.transform.localRotation = Quaternion.identity;
        ghost.transform.localScale = Vector3.one;

        foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
        {
            var ghostMaterials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < ghostMaterials.Length; i++) ghostMaterials[i] = ghostMaterial;
            renderer.sharedMaterials = ghostMaterials;
        }

        // Whatever already sits there steps aside so the preview reads clearly.
        hiddenForPreview = IsChoosingCore ? Core : SelectedHardPoint.occupant;
        if (hiddenForPreview != null) hiddenForPreview.SetRenderersVisible(false);
    }

    public void ClearHover()
    {
        if (ghost != null)
        {
            // Deactivate before destroying: Destroy only takes effect at the end of the frame, and
            // camera framing that runs in between must not measure a preview that is on its way out.
            ghost.SetActive(false);
            Destroy(ghost);
        }
        ghost = null;
        ghostDefinition = null;

        if (hiddenForPreview != null)
        {
            hiddenForPreview.SetRenderersVisible(true);
            hiddenForPreview = null;
        }
    }

    // Commits whatever the current offer is pointing at: a hull while the core list is open,
    // otherwise a part for the selected socket.
    public void ChoosePart(ShipPartDefinition definition)
    {
        if (definition == null || !definition.IsValid) return;

        ClearHover();

        if (IsChoosingCore) PlaceCore(definition);
        else PlacePart(SelectedHardPoint, definition);
    }

    public void PlaceCore(ShipPartDefinition definition)
    {
        if (Core != null) RemovePart(Core);

        GameObject instance = Instantiate(definition.prefab, AssemblyRoot);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Core = RegisterPart(instance, definition, null);

        OfferSubtitle = "Swap the hull (clears the build)";
        OfferChanged?.Invoke();
        AssemblyChanged?.Invoke();
    }

    public void PlacePart(HardPoint hardPoint, ShipPartDefinition definition)
    {
        if (hardPoint == null || definition == null || !definition.IsValid) return;

        // Re-picking a socket that is already filled swaps the part rather than stacking two.
        if (hardPoint.occupant != null) RemovePart(hardPoint.occupant);

        // Parenting to the hard point is the whole placement: the empty already carries the
        // position, orientation and scale the model author intended for whatever bolts on there.
        GameObject instance = Instantiate(definition.prefab, hardPoint.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        PlacedPart placed = RegisterPart(instance, definition, hardPoint);
        hardPoint.occupant = placed;
        RefreshMarker(hardPoint);

        AssemblyChanged?.Invoke();
    }

    public void RemoveSelectedPart()
    {
        if (SelectedHardPoint == null || SelectedHardPoint.occupant == null) return;

        ClearHover();
        RemovePart(SelectedHardPoint.occupant);
        AssemblyChanged?.Invoke();
    }

    public void ClearAssembly()
    {
        ClearHover();
        if (Core != null) RemovePart(Core);
        Core = null;
        ShowCoreOffer();
        AssemblyChanged?.Invoke();
    }

    void RemovePart(PlacedPart part)
    {
        if (part == null) return;

        // Take the whole branch down: a wing leaving takes its guns and fins with it.
        foreach (HardPoint hardPoint in part.hardPoints)
        {
            if (hardPoint == null) continue;
            if (hardPoint.occupant != null) RemovePart(hardPoint.occupant);
            if (SelectedHardPoint == hardPoint) SelectedHardPoint = null;
            hardPoints.Remove(hardPoint);
        }

        if (part.attachedTo != null)
        {
            part.attachedTo.occupant = null;
            RefreshMarker(part.attachedTo);
        }

        if (part == Core) Core = null;
        if (hiddenForPreview == part) hiddenForPreview = null;

        Destroy(part.gameObject);
    }

    // Wires a freshly spawned instance into the build: records it, finds the hard point empties it
    // introduces and gives each of them a clickable marker.
    PlacedPart RegisterPart(GameObject instance, ShipPartDefinition definition, HardPoint attachedTo)
    {
        var placed = instance.AddComponent<PlacedPart>();
        placed.definition = definition;
        placed.attachedTo = attachedTo;
        placed.CaptureRenderers();

        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (child == instance.transform) continue;
            if (!PartNaming.TryParseHardPoint(child.name, out string category, out PartSide side, out string suffix)) continue;

            var hardPoint = child.gameObject.AddComponent<HardPoint>();
            hardPoint.category = category;
            hardPoint.side = side;
            hardPoint.suffix = suffix;
            hardPoint.owner = placed;
            hardPoint.marker = CreateMarker(hardPoint);

            placed.hardPoints.Add(hardPoint);
            hardPoints.Add(hardPoint);
            RefreshMarker(hardPoint);

            if (hideEmptyHardPoints && catalog != null && !catalog.HasPartsFor(category, side))
            {
                hardPoint.marker.SetVisible(false);
            }
        }

        return placed;
    }

    HardPointMarker CreateMarker(HardPoint hardPoint)
    {
        var markerObject = new GameObject("HardPointMarker");
        markerObject.transform.SetParent(hardPoint.transform, false);

        markerObject.AddComponent<MeshFilter>().sharedMesh = markerMesh;
        MeshRenderer renderer = markerObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = markerMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        markerObject.AddComponent<SphereCollider>().isTrigger = true;

        // Hard point empties are scaled to the part they expect, so undo that to keep every
        // marker the same physical size no matter how deep in the hierarchy it sits.
        Vector3 parentScale = hardPoint.transform.lossyScale;
        float compensation = Mathf.Max(0.0001f, (Mathf.Abs(parentScale.x) + Mathf.Abs(parentScale.y) + Mathf.Abs(parentScale.z)) / 3f);
        markerObject.transform.localScale = Vector3.one * (markerSize / compensation);

        var marker = markerObject.AddComponent<HardPointMarker>();
        marker.hardPoint = hardPoint;
        return marker;
    }

    void RefreshMarker(HardPoint hardPoint)
    {
        if (hardPoint == null || hardPoint.marker == null) return;

        Color color = markerIdleColor;
        if (hardPoint == SelectedHardPoint) color = markerSelectedColor;
        else if (hardPoint.IsOccupied) color = markerOccupiedColor;

        hardPoint.marker.SetColor(color);
    }

    public void SetMarkersVisible(bool visible)
    {
        foreach (HardPoint hardPoint in hardPoints)
        {
            if (hardPoint != null && hardPoint.marker != null) hardPoint.marker.SetVisible(visible);
        }
    }

    // ---------------------------------------------------------------- camera framing

    // Pulls the camera back along the direction it was already pointing until the whole assembly
    // fits, and slides it sideways so the ship centres in the part of the screen the parts panel
    // does not cover. Runs whenever the ship gains or loses a part.
    public void RequestFraming()
    {
        if (!autoFrameCamera || builderCamera == null || AssemblyRoot == null) return;

        float radius = ComputeAssemblyRadius();
        if (radius <= 0f)
        {
            hasFramingTarget = false;
            return;
        }

        // The panel hides the right hand slice of the screen, so the ship has a narrower cone to
        // fit inside than the camera's full field of view suggests.
        float usableWidth = Mathf.Clamp(1f - uiPanelFraction, 0.2f, 1f);

        float distance;
        float halfWidthAtDistance;

        if (builderCamera.orthographic)
        {
            builderCamera.orthographicSize = radius * framingPadding / usableWidth;
            halfWidthAtDistance = builderCamera.orthographicSize * builderCamera.aspect;
            distance = radius * 2f + builderCamera.nearClipPlane;
        }
        else
        {
            float halfVertical = builderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * builderCamera.aspect);
            float halfUsable = Mathf.Atan(Mathf.Tan(halfHorizontal) * usableWidth);

            // Distance at which a sphere of this radius fits inside the cone, taken for both axes.
            distance = Mathf.Max(radius / Mathf.Sin(halfVertical), radius / Mathf.Sin(halfUsable)) * framingPadding;
            halfWidthAtDistance = Mathf.Tan(halfHorizontal) * distance;
        }

        Vector3 sideStep = builderCamera.transform.right * (halfWidthAtDistance * uiPanelFraction);
        framingTarget = AssemblyRoot.position - framingDirection * distance + sideStep;
        hasFramingTarget = true;

        if (framingSmoothTime <= 0f)
        {
            builderCamera.transform.position = framingTarget;
            framingVelocity = Vector3.zero;
        }

        // Never let the ship fall out the back of the clip planes once it grows.
        builderCamera.farClipPlane = Mathf.Max(builderCamera.farClipPlane, distance + radius * 3f);
    }

    void LateUpdate()
    {
        if (!hasFramingTarget || builderCamera == null || framingSmoothTime <= 0f) return;

        builderCamera.transform.position = Vector3.SmoothDamp(
            builderCamera.transform.position, framingTarget, ref framingVelocity, framingSmoothTime);
    }

    // Radius of a sphere around the stand that contains every placed part. Measured from the stand
    // rather than from the bounds centre so that spinning the ship does not change the answer and
    // set the camera lurching.
    float ComputeAssemblyRadius()
    {
        Vector3 origin = AssemblyRoot.position;
        float radius = 0f;

        foreach (Renderer renderer in AssemblyRoot.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.GetComponentInParent<HardPointMarker>() != null) continue;

            Bounds bounds = renderer.bounds;
            Vector3 extents = bounds.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);
                radius = Mathf.Max(radius, Vector3.Distance(origin, bounds.center + offset));
            }
        }

        return radius;
    }

    // ---------------------------------------------------------------- stats

    public int PartCount
    {
        get
        {
            int count = Core != null ? 1 : 0;
            foreach (HardPoint hardPoint in hardPoints)
            {
                if (hardPoint != null && hardPoint.IsOccupied) count++;
            }
            return count;
        }
    }

    public float TotalMass
    {
        get
        {
            float mass = Core != null ? Core.Weight : 0f;
            foreach (HardPoint hardPoint in hardPoints)
            {
                if (hardPoint != null && hardPoint.occupant != null) mass += hardPoint.occupant.Weight;
            }
            return mass;
        }
    }

    // ---------------------------------------------------------------- input

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || builderCamera == null) return;

        if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI())
        {
            BeginPress(mouse.position.ReadValue());
        }

        if (mouse.leftButton.isPressed && pressStartedOnModel)
        {
            Vector2 delta = mouse.delta.ReadValue();
            pressTravel += new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));

            if (!dragging && pressTravel.magnitude >= dragThreshold) dragging = true;
            if (dragging) RotateAssembly(delta);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            dragging = false;
            pressStartedOnModel = false;
            pressTravel = Vector2.zero;
        }
    }

    void BeginPress(Vector2 screenPosition)
    {
        pressTravel = Vector2.zero;
        dragging = false;
        pressStartedOnModel = false;

        Ray ray = builderCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Collide);

        // Markers draw on top of the hull, so they have to win the pick as well - otherwise a
        // socket tucked against the fuselage could never be clicked.
        HardPointMarker marker = FindClosest<HardPointMarker>(hits);
        if (marker != null && marker.hardPoint != null)
        {
            SelectHardPoint(marker.hardPoint);
            return;
        }

        pressStartedOnModel = FindClosest<PlacedPart>(hits) != null;
    }

    // Nearest hit whose collider belongs to a T, ignoring anything else the ray passes through.
    static T FindClosest<T>(RaycastHit[] hits) where T : Component
    {
        float nearest = float.MaxValue;
        T best = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearest) continue;

            T candidate = hit.collider.GetComponentInParent<T>();
            if (candidate == null) continue;

            nearest = hit.distance;
            best = candidate;
        }
        return best;
    }

    // Yaw around the camera up axis and pitch around its right axis, so the drag always follows the
    // mouse no matter which way the ship has already been spun.
    void RotateAssembly(Vector2 delta)
    {
        if (AssemblyRoot == null) return;

        Transform view = builderCamera.transform;
        AssemblyRoot.Rotate(view.up, -delta.x * rotationSpeed, Space.World);
        AssemblyRoot.Rotate(view.right, delta.y * rotationSpeed, Space.World);
    }

    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
