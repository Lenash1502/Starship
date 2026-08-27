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
    [Tooltip("Hide a socket entirely once it holds a part, instead of drawing it as a ring. Note " +
             "that a hidden socket cannot be clicked, so the part on it can no longer be swapped " +
             "from the menu.")]
    public bool hideOccupiedHardPoints = false;
    [Tooltip("How thick the ring on an occupied socket is: the inner edge as a fraction of the " +
             "radius, so higher is thinner.")]
    [Range(0f, 0.95f)] public float markerRingInnerRadius = 0.68f;
    [Tooltip("Hide sockets the hull stands in front of, so the far side of the ship stays clear " +
             "until it is rotated round.")]
    public bool hideOccludedHardPoints = true;
    [Tooltip("Frames between occlusion checks. Higher is cheaper, and slightly laggier while the " +
             "ship is being spun.")]
    [Range(1, 10)] public int occlusionCheckInterval = 2;
    public Color markerIdleColor = new Color(0.35f, 0.8f, 1f, 0.55f);
    public Color markerOccupiedColor = new Color(1f, 1f, 1f, 0.75f);
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
    [Tooltip("Let the builder drive the camera: fit the ship, zoom in on a clicked part, and take " +
             "the scroll wheel. Turn off to fly the camera yourself.")]
    public bool autoFrameCamera = true;
    [Tooltip("Room left around the ship. 1 is a tight fit against the edges of the view.")]
    public float framingPadding = 1.15f;
    [Tooltip("Seconds the camera takes to settle on a new distance. 0 snaps straight there.")]
    public float framingSmoothTime = 0.25f;
    [Tooltip("Fraction of the screen width the parts panel covers, so the ship centres in what is " +
             "left of the view. ShipBuilderUI keeps this up to date on its own.")]
    [Range(0f, 0.6f)] public float uiPanelFraction = 0f;

    [Header("Zoom")]
    [Tooltip("How far one notch of the scroll wheel moves the camera, as a proportion of distance.")]
    public float zoomSensitivity = 0.2f;
    [Tooltip("Closest the wheel can pull in, as a fraction of the framed distance.")]
    public float minZoom = 0.2f;
    [Tooltip("Furthest the wheel can push out, as a multiple of the framed distance.")]
    public float maxZoom = 3f;

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
    PlacedPart pressedPart;
    Vector2 pressTravel;

    Vector3 framingDirection = Vector3.forward;
    Vector3 framingTarget;
    Vector3 framingVelocity;
    bool hasFramingTarget;
    float assemblyRadius;

    PlacedPart focusedPart;
    Vector3 focusLocalCenter;
    float focusRadius;
    float zoomLevel = 1f;
    int occlusionFrameCounter;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int InnerRadiusId = Shader.PropertyToID("_InnerRadius");

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

        markerMaterial = new Material(LoadShader("ShipBuilderMarker", "ShipBuilder/Marker"));
        ApplyMarkerMaterialSettings();

        // A flat quad, shared by every marker: the shader spins it to face the camera, so the
        // markers read as circles from any angle without any per frame work here.
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Quad);
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

        // The side comes from the chain the socket hangs off, not just from its own name, so a
        // centred mount out on a left wing still only offers left hand parts.
        PartSide side = hardPoint.EffectiveSide;
        string tail = PartNaming.PrettySuffix(hardPoint.suffix);

        if (!string.IsNullOrEmpty(tail)) OfferSubtitle = tail + " mount point";
        else if (side == PartSide.None) OfferSubtitle = "Mount point";
        else OfferSubtitle = "Mount point, " + (side == PartSide.Left ? "left" : "right") + " side of the ship";

        CurrentOffer = catalog != null
            ? new List<ShipPartDefinition>(catalog.GetPartsFor(hardPoint.category, side))
            : new List<ShipPartDefinition>();

        OfferChanged?.Invoke();
    }

    // Clicking a piece of the ship offers what could stand in for it. For the core that is the hull
    // list, and picking a different hull strips the build back to bare metal - PlaceCore tears the
    // old core down first, and a part takes everything bolted to it with it. For anything else it
    // is the list for the socket that part occupies, so a wing can be swapped by clicking the wing
    // rather than hunting for its marker.
    public void OfferReplacementsFor(PlacedPart part)
    {
        if (part == null) return;

        if (part == Core) ShowCoreOffer();
        else if (part.attachedTo != null) SelectHardPoint(part.attachedTo);
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

        // Zooming in on something and then deleting it should drop back to the whole ship, which
        // the AssemblyChanged reframe that follows takes care of.
        if (focusedPart == part)
        {
            focusedPart = null;
            zoomLevel = 1f;
        }

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

        // Filled while the socket is open or being worked on, a hollow ring once something is
        // bolted there - a finished mount should stop competing with the model for attention.
        Color color = markerIdleColor;
        bool filled = true;

        if (hardPoint == SelectedHardPoint)
        {
            color = markerSelectedColor;
        }
        else if (hardPoint.IsOccupied)
        {
            color = markerOccupiedColor;
            filled = false;
        }

        hardPoint.marker.SetStyle(color, filled);
        RefreshMarkerVisibility(hardPoint);
    }

    // Single place that decides whether a socket is drawn at all, so the hide rules cannot undo
    // each other as markers are refreshed.
    void RefreshMarkerVisibility(HardPoint hardPoint)
    {
        if (hardPoint == null || hardPoint.marker == null) return;

        bool visible = !hardPoint.marker.Occluded;

        // The one being worked on always stays up, even under a rule that would hide it.
        if (hardPoint != SelectedHardPoint)
        {
            if (hideOccupiedHardPoints && hardPoint.IsOccupied) visible = false;
            if (hideEmptyHardPoints && catalog != null && !catalog.HasPartsFor(hardPoint.category, hardPoint.EffectiveSide)) visible = false;
        }

        hardPoint.marker.SetVisible(visible);
    }

    // Decides which sockets are on the far side of the hull.
    //
    // The ray is cast from the socket out toward the camera rather than the other way round, and
    // that direction is the whole trick: a ray that starts inside a collider does not register that
    // collider, so a socket buried in the plating it belongs to still counts as visible, while hull
    // standing between it and the camera blocks it. Because it runs against the same colliders as
    // picking, a marker is clickable exactly when it can be seen.
    void UpdateMarkerOcclusion()
    {
        if (builderCamera == null) return;

        Vector3 cameraPosition = builderCamera.transform.position;

        foreach (HardPoint hardPoint in hardPoints)
        {
            if (hardPoint == null || hardPoint.marker == null) continue;

            bool occluded = false;

            if (hideOccludedHardPoints)
            {
                Vector3 markerPosition = hardPoint.transform.position;
                Vector3 toCamera = cameraPosition - markerPosition;
                float distance = toCamera.magnitude;
                Vector3 direction = distance > 0.001f ? toCamera / distance : Vector3.forward;

                // Nudged off the start point so a socket sitting exactly on a collider surface does
                // not graze that surface and flicker in and out.
                const float startOffset = 0.02f;

                if (distance > startOffset &&
                    Physics.Raycast(markerPosition + direction * startOffset, direction, out RaycastHit hit,
                                    distance - startOffset, ~0, QueryTriggerInteraction.Ignore))
                {
                    // Only the ship itself hides a socket; the stand and the rest of the scene do not.
                    occluded = hit.collider.GetComponentInParent<PlacedPart>() != null;
                }
            }

            if (hardPoint.marker.Occluded == occluded) continue;

            hardPoint.marker.Occluded = occluded;
            RefreshMarkerVisibility(hardPoint);
        }
    }

    void ApplyMarkerMaterialSettings()
    {
        if (markerMaterial == null) return;

        markerMaterial.SetFloat(InnerRadiusId, markerRingInnerRadius);
    }

#if UNITY_EDITOR
    // Lets the marker look be dialled in while the game is running, which is the only way to judge
    // whether the depth bias is clearing the hull properly.
    void OnValidate()
    {
        if (!Application.isPlaying || markerMaterial == null) return;

        ApplyMarkerMaterialSettings();
        foreach (HardPoint hardPoint in hardPoints) RefreshMarker(hardPoint);
    }
#endif

    public void SetMarkersVisible(bool visible)
    {
        foreach (HardPoint hardPoint in hardPoints)
        {
            if (hardPoint != null && hardPoint.marker != null) hardPoint.marker.SetVisible(visible);
        }
    }

    // ---------------------------------------------------------------- camera framing

    // What the camera is looking at: a single part while one is focused, the whole ship otherwise.
    public PlacedPart FocusedPart => focusedPart;
    public bool IsFocusedOnPart => focusedPart != null;

    // Zooms in on one part. The centre is remembered in that part's own local space, so the camera
    // keeps tracking it while the ship is dragged around.
    public void FocusOn(PlacedPart part)
    {
        if (part == null)
        {
            FocusOnAssembly();
            return;
        }

        if (!TryMeasurePart(part, out Vector3 center, out float radius)) return;

        focusedPart = part;
        focusLocalCenter = part.transform.InverseTransformPoint(center);
        focusRadius = radius;
        zoomLevel = 1f;

        UpdateFramingTarget();
    }

    // Pulls back out to the whole ship.
    public void FocusOnAssembly()
    {
        focusedPart = null;
        zoomLevel = 1f;
        RequestFraming();
    }

    // Full recompute, for when the ship itself changed shape.
    public void RequestFraming()
    {
        if (!autoFrameCamera || AssemblyRoot == null) return;

        assemblyRadius = ComputeAssemblyRadius();
        UpdateFramingTarget();
    }

    // Works out where the camera wants to be. Cheap enough to run every frame, which is what keeps
    // a focused part centred while the ship is being spun.
    void UpdateFramingTarget()
    {
        if (!autoFrameCamera || builderCamera == null || AssemblyRoot == null) return;

        Vector3 center;
        float radius;

        // A destroyed part compares equal to null, so removing what was focused falls back to the
        // whole ship on its own.
        if (focusedPart != null)
        {
            center = focusedPart.transform.TransformPoint(focusLocalCenter);
            radius = focusRadius;
        }
        else
        {
            center = AssemblyRoot.position;
            radius = assemblyRadius;
        }

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
            builderCamera.orthographicSize = radius * framingPadding * zoomLevel / usableWidth;
            halfWidthAtDistance = builderCamera.orthographicSize * builderCamera.aspect;
            distance = radius * 2f + builderCamera.nearClipPlane;
        }
        else
        {
            float halfVertical = builderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * builderCamera.aspect);
            float halfUsable = Mathf.Atan(Mathf.Tan(halfHorizontal) * usableWidth);

            // Distance at which a sphere of this radius fits inside the cone, taken for both axes.
            distance = Mathf.Max(radius / Mathf.Sin(halfVertical), radius / Mathf.Sin(halfUsable));
            distance *= framingPadding * zoomLevel;

            // Zooming right in on a small fin must not push the camera inside the near plane.
            distance = Mathf.Max(distance, builderCamera.nearClipPlane + radius * 1.05f);
            halfWidthAtDistance = Mathf.Tan(halfHorizontal) * distance;
        }

        Vector3 sideStep = builderCamera.transform.right * (halfWidthAtDistance * uiPanelFraction);
        framingTarget = center - framingDirection * distance + sideStep;
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
        // Which sockets the hull is covering changes as the ship turns and as the camera moves, so
        // it is re-tested on a slow tick rather than only when the build changes.
        if (++occlusionFrameCounter >= Mathf.Max(1, occlusionCheckInterval))
        {
            occlusionFrameCounter = 0;
            UpdateMarkerOcclusion();
        }

        // A focused part moves with the ship, so its framing has to be re-derived every frame.
        if (focusedPart != null) UpdateFramingTarget();

        if (!hasFramingTarget || builderCamera == null || framingSmoothTime <= 0f) return;

        builderCamera.transform.position = Vector3.SmoothDamp(
            builderCamera.transform.position, framingTarget, ref framingVelocity, framingSmoothTime);
    }

    // Bounds of one part on its own. Uses the renderer list captured when the part was placed, which
    // predates both its hard point markers and anything later bolted onto it - so focusing the core
    // frames the hull rather than the entire ship hanging off it.
    static bool TryMeasurePart(PlacedPart part, out Vector3 center, out float radius)
    {
        center = part.transform.position;
        radius = 0f;

        Bounds bounds = default;
        bool measured = false;

        foreach (Renderer renderer in part.renderers)
        {
            if (renderer == null) continue;

            if (!measured)
            {
                bounds = renderer.bounds;
                measured = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!measured) return false;

        center = bounds.center;
        radius = Mathf.Max(0.05f, bounds.extents.magnitude);
        return true;
    }

    void ApplyZoom(float scrollDelta)
    {
        // Wheels report 120 per notch on Windows and roughly 1 elsewhere; normalise to notches so
        // the sensitivity setting means the same thing on both.
        float notches = Mathf.Abs(scrollDelta) >= 20f ? scrollDelta / 120f : scrollDelta;

        // Exponential, so every notch changes the distance by the same proportion.
        zoomLevel = Mathf.Clamp(zoomLevel * Mathf.Exp(-notches * zoomSensitivity), minZoom, maxZoom);
        UpdateFramingTarget();
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

        bool overUI = IsPointerOverUI();

        if (mouse.leftButton.wasPressedThisFrame && !overUI)
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
            // A press that never travelled far enough to become a drag is a click: zoom in on the
            // piece and open up what can go in its place.
            if (!dragging && pressedPart != null)
            {
                FocusOn(pressedPart);
                OfferReplacementsFor(pressedPart);
            }

            dragging = false;
            pressStartedOnModel = false;
            pressedPart = null;
            pressTravel = Vector2.zero;
        }

        // Right click backs out to the whole ship again.
        if (mouse.rightButton.wasPressedThisFrame && !overUI) FocusOnAssembly();

        // The wheel belongs to the parts list while the pointer is over it.
        if (!overUI)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) ApplyZoom(scroll);
        }
    }

    void BeginPress(Vector2 screenPosition)
    {
        pressTravel = Vector2.zero;
        dragging = false;
        pressStartedOnModel = false;
        pressedPart = null;

        Ray ray = builderCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Collide);

        // Markers draw over the hull, so they win the pick too. A socket on the far side is not a
        // candidate here at all: the occlusion sweep disables its collider along with its renderer.
        HardPointMarker marker = FindClosest<HardPointMarker>(hits, out _);
        if (marker != null && marker.hardPoint != null)
        {
            SelectHardPoint(marker.hardPoint);
            return;
        }

        // Remembered rather than acted on now: the same press might turn into a rotate drag, and
        // only a press that stays put counts as a click on this part.
        pressedPart = FindClosest<PlacedPart>(hits, out _);
        pressStartedOnModel = pressedPart != null;
    }

    // Nearest hit whose collider belongs to a T, ignoring anything else the ray passes through.
    static T FindClosest<T>(RaycastHit[] hits, out float distance) where T : Component
    {
        distance = float.MaxValue;
        T best = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= distance) continue;

            T candidate = hit.collider.GetComponentInParent<T>();
            if (candidate == null) continue;

            distance = hit.distance;
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
