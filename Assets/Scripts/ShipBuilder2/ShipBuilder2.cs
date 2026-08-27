using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// The second builder screen, for the redesigned modules in Assets/Prefabs/ShipModules.
//
// The flow is the reverse of the original ShipBuilder. There the player picked a socket and then
// chose what went in it; here the ship starts with its core already on the stand, the player picks
// a module from the list first, and the sockets that can take it light up. Hovering one shows the
// module ghosted into place, clicking bolts it on, and the module stays in hand so a run of nine
// tails can be placed without going back to the list each time.
//
// The module prefabs carry no side suffixes or numbering - every socket is just "<Category>HardPoint"
// and there may be many of them side by side - so matching is category only.
[DisallowMultipleComponent]
public class ShipBuilder2 : MonoBehaviour
{
    public const string CoreCategory = "Core";

    [Header("Data")]
    public ShipPartCatalog catalog;

    [Header("Scene")]
    [Tooltip("Stand the ship is built on. Found by name at startup when left empty.")]
    public Transform selectionCircle;
    public string selectionCircleName = "SelectionCircle";
    public BuilderCamera builderCamera;

    [Header("Socket Markers")]
    public float markerSize = 0.35f;
    [Tooltip("How thick the ring on a filled socket is: the inner edge as a fraction of the radius.")]
    [Range(0f, 0.95f)] public float markerRingInnerRadius = 0.68f;
    [Tooltip("Colour sockets differently when the hull stands between them and the camera, so the " +
             "far side of the ship is still reachable but never mistaken for the near side.")]
    public bool markOccludedSockets = true;
    [Range(1, 10)] public int occlusionCheckInterval = 2;
    public Color markerFreeColor = new Color(0.35f, 0.8f, 1f, 0.55f);
    public Color markerHoverColor = new Color(1f, 0.6f, 0.15f, 1f);
    [Tooltip("Sockets on the far side of the hull.")]
    public Color markerOccludedColor = new Color(1f, 0.92f, 0.15f, 0.85f);
    public Color markerOccludedOutlineColor = new Color(0f, 0f, 0f, 1f);
    [Tooltip("Thickness of the far side outline, as a fraction of the marker radius.")]
    [Range(0f, 0.5f)] public float markerOccludedOutlineWidth = 0.24f;

    [Header("Preview")]
    public Color ghostColor = new Color(0.35f, 0.85f, 1f, 0.3f);
    public Color ghostRimColor = new Color(0.6f, 0.95f, 1f, 1f);
    [Tooltip("Colour of the preview when the module would bury itself in existing structure.")]
    public Color ghostBlockedColor = new Color(1f, 0.2f, 0.15f, 0.35f);
    public Color ghostBlockedRimColor = new Color(1f, 0.45f, 0.35f, 1f);

    [Header("Overlap Warning")]
    [Tooltip("Fraction of the module's own volume that may sit inside other structure before the " +
             "preview turns red. Joins overlap a little by design, so this is not zero.")]
    [Range(0f, 1f)] public float overlapWarningThreshold = 0.25f;
    [Tooltip("Points sampled inside the module to measure how much of it is buried. Higher is more " +
             "accurate and slower; every candidate socket is measured once when a module is picked " +
             "up and again whenever the ship changes, never per frame.")]
    [Range(24, 600)] public int overlapSampleCount = 160;
    [Tooltip("Ignore overlap with the part the socket belongs to. A module is designed to seat into " +
             "its parent, so counting that would flag every placement.")]
    public bool ignoreOverlapWithParent = true;
    [Tooltip("Do not show a socket at all when the module would clash there. Turn off to show it " +
             "anyway, previewed as a red hologram and still refused on click.")]
    public bool hideBlockedSockets = true;

    [Header("Rotation")]
    [Tooltip("Degrees of yaw/pitch per pixel of mouse travel while dragging the model.")]
    public float rotationSpeed = 0.35f;
    [Tooltip("Pixels of travel before a press counts as a drag rather than a click.")]
    public float dragThreshold = 4f;

    // Raised when the module in hand changes, when the ship itself changes, and when the socket
    // under the pointer starts or stops refusing the module.
    public event Action HeldModuleChanged;
    public event Action AssemblyChanged;
    public event Action PreviewChanged;

    public Transform AssemblyRoot { get; private set; }
    public PlacedPart Core { get; private set; }

    // The module the player picked out of the list, waiting to be dropped onto a socket.
    public ShipPartDefinition HeldModule { get; private set; }

    // What the player last clicked on the ship while empty handed, so it can be removed.
    public PlacedPart SelectedPart { get; private set; }

    // The seven attachable modules, in catalog order. The core is not among them: it is the root.
    public List<ShipPartDefinition> Modules { get; private set; } = new List<ShipPartDefinition>();

    // True while the socket under the pointer would bury the module in existing structure, which is
    // only reachable with hideBlockedSockets turned off - normally such a socket is not shown at all.
    public bool PreviewBlocked { get; private set; }

    readonly List<HardPoint> sockets = new List<HardPoint>();
    readonly List<Collider> overlapTargets = new List<Collider>();
    readonly List<Collider> shipColliders = new List<Collider>();

    // Sockets where the module in hand would end up buried in existing structure. Worked out once
    // when the module is picked up and again whenever the ship changes, rather than per hover: the
    // answer only depends on geometry, and the whole point is to know before the player points at it.
    readonly HashSet<HardPoint> blockedSockets = new HashSet<HardPoint>();

    Material ghostMaterial;
    Material ghostBlockedMaterial;
    Material markerMaterial;
    Mesh markerMesh;

    GameObject ghost;
    HardPoint hoveredSocket;
    bool framingWholeShip = true;
    bool warnedAboutSampling;

    bool dragging;
    bool pressStartedOnModel;
    PlacedPart pressedPart;
    Vector2 pressTravel;
    int occlusionFrameCounter;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int InnerRadiusId = Shader.PropertyToID("_InnerRadius");

    void Awake()
    {
        if (builderCamera == null) builderCamera = GetComponent<BuilderCamera>();
        if (selectionCircle == null) selectionCircle = FindSelectionCircle(selectionCircleName);

        var rootObject = new GameObject("Assembly");
        AssemblyRoot = rootObject.transform;
        AssemblyRoot.SetParent(selectionCircle != null ? selectionCircle : transform, false);

        CreateSharedResources();
        CollectModules();
    }

    void Start()
    {
        PlaceCore();
    }

    void OnDestroy()
    {
        if (ghostMaterial != null) Destroy(ghostMaterial);
        if (ghostBlockedMaterial != null) Destroy(ghostBlockedMaterial);
        if (markerMaterial != null) Destroy(markerMaterial);
    }

    void CreateSharedResources()
    {
        Shader ghostShader = LoadShader("ShipBuilderGhost", "ShipBuilder/Ghost");

        ghostMaterial = new Material(ghostShader);
        if (ghostMaterial.HasProperty(ColorId)) ghostMaterial.SetColor(ColorId, ghostColor);
        if (ghostMaterial.HasProperty(RimColorId)) ghostMaterial.SetColor(RimColorId, ghostRimColor);

        ghostBlockedMaterial = new Material(ghostShader);
        if (ghostBlockedMaterial.HasProperty(ColorId)) ghostBlockedMaterial.SetColor(ColorId, ghostBlockedColor);
        if (ghostBlockedMaterial.HasProperty(RimColorId)) ghostBlockedMaterial.SetColor(RimColorId, ghostBlockedRimColor);

        markerMaterial = new Material(LoadShader("ShipBuilderMarker", "ShipBuilder/Marker"));
        markerMaterial.SetFloat(InnerRadiusId, markerRingInnerRadius);

        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Quad);
        markerMesh = template.GetComponent<MeshFilter>().sharedMesh;
        Destroy(template);
    }

    // Deliberately its own copy rather than borrowing the first builder's: the two screens are meant
    // to stand alone, so neither breaks if the other is deleted.
    public static Transform FindSelectionCircle(string wantedName)
    {
        if (string.IsNullOrEmpty(wantedName)) return null;

        GameObject exact = GameObject.Find(wantedName);
        if (exact != null) return exact.transform;

        // Tolerate whatever spacing the object happens to use.
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

    static Shader LoadShader(string resourceName, string shaderName)
    {
        Shader shader = Resources.Load<Shader>(resourceName);
        if (shader == null) shader = Shader.Find(shaderName);
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return shader;
    }

    // Everything in the catalog except the core, which is never chosen because it is always there.
    void CollectModules()
    {
        Modules.Clear();
        if (catalog == null) return;

        foreach (ShipPartDefinition definition in catalog.parts)
        {
            if (definition == null || !definition.IsValid) continue;
            if (definition.category == CoreCategory) continue;

            Modules.Add(definition);
        }
    }

    // ---------------------------------------------------------------- building

    // The screen opens with a ship already on the stand, so there is something to hang modules off
    // from the first frame.
    public void PlaceCore()
    {
        if (Core != null) return;
        if (catalog == null) return;

        ShipPartDefinition coreDefinition = null;
        foreach (ShipPartDefinition definition in catalog.parts)
        {
            if (definition != null && definition.IsValid && definition.category == CoreCategory)
            {
                coreDefinition = definition;
                break;
            }
        }

        if (coreDefinition == null)
        {
            Debug.LogError($"[Ship Builder 2] No Core prefab in the catalog ({(catalog != null ? catalog.sourceFolder : "none")}).", this);
            return;
        }

        GameObject instance = Instantiate(coreDefinition.prefab, AssemblyRoot);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Core = RegisterPart(instance, coreDefinition, null);

        RefreshPlacementOptions();
        FrameWholeShip(true);
        AssemblyChanged?.Invoke();
    }

    // Picks a module up out of the list. Nothing is placed yet: the sockets that can take it simply
    // start showing, and the next click on one of them commits it.
    public void HoldModule(ShipPartDefinition definition)
    {
        if (definition == null || !definition.IsValid) return;

        // Clicking the module already in hand puts it back down.
        HeldModule = HeldModule == definition ? null : definition;

        SelectedPart = null;
        ClearGhost();
        RefreshPlacementOptions();
        HeldModuleChanged?.Invoke();
    }

    public void DropHeldModule()
    {
        if (HeldModule == null) return;

        HeldModule = null;
        ClearGhost();
        RefreshPlacementOptions();
        HeldModuleChanged?.Invoke();
    }

    // A socket takes the module in hand if it names that category and is still free. Occupied ones
    // are not offered: there is exactly one prefab per category, so dropping the same module onto a
    // filled socket would achieve nothing. Clearing one out goes through Remove instead.
    public bool Accepts(HardPoint socket)
    {
        if (socket == null || HeldModule == null) return false;
        if (socket.IsOccupied || socket.category != HeldModule.category) return false;

        // A socket where the module would clash is simply not offered, so there is no marker to
        // point at and no click to refuse.
        return !hideBlockedSockets || !blockedSockets.Contains(socket);
    }

    public void PlaceOn(HardPoint socket)
    {
        if (!Accepts(socket)) return;

        // Refused outright, whether or not the socket was hidden. Several sockets sit close enough
        // together that filling both would leave two modules in nearly the same space.
        if (blockedSockets.Contains(socket)) return;

        ShipPartDefinition definition = HeldModule;
        ClearGhost();
        hoveredSocket = null;

        // Parenting to the socket is the whole placement: the empty already carries the position,
        // orientation and scale the model author intended for whatever bolts on there.
        GameObject instance = Instantiate(definition.prefab, socket.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        PlacedPart placed = RegisterPart(instance, definition, socket);
        socket.occupant = placed;

        // The module stays in hand, so a row of identical sockets can be filled in one go.
        RefreshPlacementOptions();
        ReframeAfterChange();
        AssemblyChanged?.Invoke();
    }

    public void RemoveSelectedPart()
    {
        RemoveModule(SelectedPart);
    }

    // Takes a module off the ship, along with everything hanging from it. The core is not removable:
    // it is the thing the rest is built on.
    public void RemoveModule(PlacedPart part)
    {
        if (part == null || part == Core) return;

        ClearGhost();
        hoveredSocket = null;

        RemovePart(part);
        SelectedPart = null;

        RefreshPlacementOptions();
        ReframeAfterChange();
        AssemblyChanged?.Invoke();
    }

    void RemovePart(PlacedPart part)
    {
        if (part == null) return;

        // Take the whole branch down: a wing leaving takes its guns and engines with it.
        foreach (HardPoint socket in part.hardPoints)
        {
            if (socket == null) continue;
            if (socket.occupant != null) RemovePart(socket.occupant);

            if (hoveredSocket == socket) hoveredSocket = null;
            sockets.Remove(socket);
        }

        if (part.attachedTo != null) part.attachedTo.occupant = null;
        if (part == Core) Core = null;
        if (SelectedPart == part) SelectedPart = null;

        Destroy(part.gameObject);
    }

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

            var socket = child.gameObject.AddComponent<HardPoint>();
            socket.category = category;
            socket.side = side;
            socket.suffix = suffix;
            socket.owner = placed;
            socket.marker = CreateMarker(socket);

            placed.hardPoints.Add(socket);
            sockets.Add(socket);
        }

        return placed;
    }

    HardPointMarker CreateMarker(HardPoint socket)
    {
        var markerObject = new GameObject("SocketMarker");
        markerObject.transform.SetParent(socket.transform, false);

        markerObject.AddComponent<MeshFilter>().sharedMesh = markerMesh;
        MeshRenderer renderer = markerObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = markerMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        markerObject.AddComponent<SphereCollider>().isTrigger = true;

        // Sockets are scaled to the module they expect, so undo that to keep every marker the same
        // physical size no matter how deep in the hierarchy it sits.
        Vector3 parentScale = socket.transform.lossyScale;
        float compensation = Mathf.Max(0.0001f, (Mathf.Abs(parentScale.x) + Mathf.Abs(parentScale.y) + Mathf.Abs(parentScale.z)) / 3f);
        markerObject.transform.localScale = Vector3.one * (markerSize / compensation);

        var marker = markerObject.AddComponent<HardPointMarker>();
        marker.hardPoint = socket;
        return marker;
    }

    // ---------------------------------------------------------------- markers

    // Works out where the module in hand could actually go, then redraws the markers to match.
    // Called when the module changes and whenever the ship gains or loses a part, which is exactly
    // when the answer can change - it does not depend on where the camera is or how the ship is
    // turned, so spinning the model costs nothing.
    void RefreshPlacementOptions()
    {
        EvaluateBlockedSockets();
        RefreshAllMarkers();
    }

    // Tries the held module against every free socket of its category and records the ones where it
    // would clash. A single probe instance is moved from socket to socket rather than one being
    // spawned per socket, so a pass costs one Instantiate no matter how many candidates there are.
    void EvaluateBlockedSockets()
    {
        blockedSockets.Clear();
        if (HeldModule == null || AssemblyRoot == null) return;

        GatherShipColliders();
        if (shipColliders.Count == 0) return;

        GameObject probe = Instantiate(HeldModule.prefab);
        probe.name = "PlacementProbe";
        probe.hideFlags = HideFlags.HideInHierarchy;

        // Invisible, and triggers so it never shows up in picking, hovering or the occlusion sweep.
        foreach (Renderer renderer in probe.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (Collider collider in probe.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;

        foreach (HardPoint socket in sockets)
        {
            if (socket == null || socket.IsOccupied) continue;
            if (socket.category != HeldModule.category) continue;

            probe.transform.SetParent(socket.transform, false);
            probe.transform.localPosition = Vector3.zero;
            probe.transform.localRotation = Quaternion.identity;
            probe.transform.localScale = Vector3.one;

            float overlap = MeasureOverlapFraction(probe, ignoreOverlapWithParent ? socket.owner : null);
            if (overlap >= overlapWarningThreshold) blockedSockets.Add(socket);
        }

        Destroy(probe);
    }

    // Every solid collider belonging to a placed module. Gathered once per pass so moving the probe
    // between sockets does not re-walk the hierarchy each time.
    void GatherShipColliders()
    {
        shipColliders.Clear();
        if (AssemblyRoot == null) return;

        foreach (Collider collider in AssemblyRoot.GetComponentsInChildren<Collider>())
        {
            // Triggers here are socket markers, the preview and the probe.
            if (collider.isTrigger) continue;
            if (collider.GetComponentInParent<PlacedPart>() == null) continue;

            shipColliders.Add(collider);
        }
    }

    // Only the sockets that can take what is in hand are drawn. With nothing in hand the ship shows
    // clean rather than under a fog of markers.
    void RefreshAllMarkers()
    {
        foreach (HardPoint socket in sockets) RefreshMarker(socket);
    }

    void RefreshMarker(HardPoint socket)
    {
        if (socket == null || socket.marker == null) return;

        bool relevant = Accepts(socket);
        socket.marker.SetVisible(relevant);

        if (!relevant) return;

        // Sockets behind the hull stay reachable but are called out, so one on the far side is never
        // mistaken for one facing the player. The outline rides along even while hovered, so the
        // highlight colour does not wipe out that cue.
        bool behindHull = markOccludedSockets && socket.marker.Occluded;
        bool hovered = socket == hoveredSocket;

        Color fill = hovered ? markerHoverColor : behindHull ? markerOccludedColor : markerFreeColor;

        socket.marker.SetStyle(
            fill, true,
            behindHull ? markerOccludedOutlineColor : Color.clear,
            behindHull ? markerOccludedOutlineWidth : 0f);
    }

    // Decides which sockets are on the far side of the hull. The ray is cast from the socket out
    // toward the camera rather than the other way round: a ray that starts inside a collider does
    // not register that collider, so a socket buried in the plating it belongs to still counts as
    // visible, while hull standing between it and the camera blocks it.
    void UpdateMarkerOcclusion()
    {
        Camera view = builderCamera != null ? builderCamera.view : Camera.main;
        if (view == null) return;

        Vector3 cameraPosition = view.transform.position;

        foreach (HardPoint socket in sockets)
        {
            if (socket == null || socket.marker == null) continue;

            bool occluded = false;

            if (markOccludedSockets)
            {
                Vector3 socketPosition = socket.transform.position;
                Vector3 toCamera = cameraPosition - socketPosition;
                float distance = toCamera.magnitude;
                Vector3 direction = distance > 0.001f ? toCamera / distance : Vector3.forward;

                const float startOffset = 0.02f;

                if (distance > startOffset &&
                    Physics.Raycast(socketPosition + direction * startOffset, direction, out RaycastHit hit,
                                    distance - startOffset, ~0, QueryTriggerInteraction.Ignore))
                {
                    occluded = hit.collider.GetComponentInParent<PlacedPart>() != null;
                }
            }

            if (socket.marker.Occluded == occluded) continue;

            socket.marker.Occluded = occluded;
            RefreshMarker(socket);
        }
    }

    // ---------------------------------------------------------------- preview

    void SetHoveredSocket(HardPoint socket)
    {
        if (hoveredSocket == socket) return;

        HardPoint previous = hoveredSocket;
        hoveredSocket = socket;

        RefreshMarker(previous);
        RefreshMarker(hoveredSocket);

        ClearGhost();
        if (socket == null || HeldModule == null) return;

        ghost = Instantiate(HeldModule.prefab, socket.transform);
        ghost.name = "ModulePreview";
        ghost.transform.localPosition = Vector3.zero;
        ghost.transform.localRotation = Quaternion.identity;
        ghost.transform.localScale = Vector3.one;

        // Left enabled but turned into triggers: the overlap measurement needs to query their
        // shapes, while every raycast in this class either ignores triggers or looks for something
        // the preview does not have.
        foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;

        // Already known from the pass that ran when the module was picked up, so hovering costs
        // nothing. With hideBlockedSockets on this is always false, because a blocked socket has no
        // marker to hover in the first place.
        SetPreviewBlocked(blockedSockets.Contains(socket));

        Material material = PreviewBlocked ? ghostBlockedMaterial : ghostMaterial;
        foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
        {
            var ghostMaterials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < ghostMaterials.Length; i++) ghostMaterials[i] = material;
            renderer.sharedMaterials = ghostMaterials;
        }
    }

    // How much of the module would end up buried inside structure that is already there, as a
    // fraction of its own volume.
    //
    // Measured by scattering points through the module's own colliders and asking how many of them
    // land inside somebody else's. Sampling the real collider shapes rather than their bounding
    // boxes matters here: a wing is mostly empty space inside its own box, and judging by boxes
    // would call every wing a clash. All the module colliders are boxes, capsules and spheres, so
    // Collider.ClosestPoint answers "is this point inside" exactly.
    float MeasureOverlapFraction(GameObject candidate, PlacedPart ignore)
    {
        Collider[] candidateColliders = candidate.GetComponentsInChildren<Collider>();
        if (candidateColliders.Length == 0 || AssemblyRoot == null) return 0f;

        // Both Collider.bounds and Collider.ClosestPoint read the physics engine's copy of the
        // world, not the transforms. Unity only refreshes that copy before a simulation step, so
        // the preview - parented and positioned microseconds ago - and any module placed since the
        // last FixedUpdate are still sitting whereever their prefab put them as far as physics is
        // concerned. Without this the preview measures itself against the wrong pose and quietly
        // finds nothing to hit.
        Physics.SyncTransforms();

        Bounds candidateBounds = candidateColliders[0].bounds;
        foreach (Collider collider in candidateColliders) candidateBounds.Encapsulate(collider.bounds);

        overlapTargets.Clear();
        foreach (Collider collider in shipColliders)
        {
            if (collider == null) continue;

            PlacedPart owner = collider.GetComponentInParent<PlacedPart>();
            if (owner == null || owner == ignore) continue;
            if (!collider.bounds.Intersects(candidateBounds)) continue;

            overlapTargets.Add(collider);
        }

        if (overlapTargets.Count == 0) return 0f;

        // Fixed seed so hovering the same socket twice gives the same answer instead of flickering
        // around the threshold.
        var random = new System.Random(9871);
        int perCollider = Mathf.Max(8, overlapSampleCount / candidateColliders.Length);
        int sampled = 0;
        int buried = 0;

        foreach (Collider candidateCollider in candidateColliders)
        {
            // Sampled in the collider's own local space, never its world bounds. A world axis
            // aligned box changes shape as the ship is turned, which would hand the same random
            // sequence a different set of points at every angle and make the answer wobble with the
            // view. Local space does not move when the ship does, so the same points get tested
            // every time and the measurement stays purely geometric.
            Bounds box = LocalBounds(candidateCollider);
            Transform frame = candidateCollider.transform;
            int kept = 0;

            // Rejection sampling: points are drawn from that local box and thrown away unless they
            // are really inside the shape, so the fraction is of solid volume.
            for (int attempt = 0; attempt < perCollider * 8 && kept < perCollider; attempt++)
            {
                var local = new Vector3(
                    Mathf.Lerp(box.min.x, box.max.x, (float)random.NextDouble()),
                    Mathf.Lerp(box.min.y, box.max.y, (float)random.NextDouble()),
                    Mathf.Lerp(box.min.z, box.max.z, (float)random.NextDouble()));

                Vector3 point = frame.TransformPoint(local);
                if (!Contains(candidateCollider, point)) continue;

                kept++;
                sampled++;

                foreach (Collider target in overlapTargets)
                {
                    if (!Contains(target, point)) continue;

                    buried++;
                    break;
                }
            }
        }

        if (sampled == 0 && !warnedAboutSampling)
        {
            // Reaching here means no point could be placed inside the module's own colliders, so
            // the fraction below is meaningless rather than merely zero. Worth saying out loud:
            // silently reporting "no overlap" is how a broken measurement hides.
            warnedAboutSampling = true;
            Debug.LogWarning($"[Ship Builder 2] Could not sample inside {candidate.name}: overlap cannot be judged for it.", this);
        }

        return sampled > 0 ? (float)buried / sampled : 0f;
    }

    static bool Contains(Collider collider, Vector3 point)
    {
        // ClosestPoint hands back the point itself when it is inside the shape.
        return (collider.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
    }

    // The collider's extent in its own local space, which - unlike Collider.bounds - is the same
    // whichever way the ship happens to be facing. Generous is fine: anything outside the real shape
    // is rejected by the ClosestPoint test anyway, it only costs a few wasted samples.
    static Bounds LocalBounds(Collider collider)
    {
        switch (collider)
        {
            case BoxCollider box:
                return new Bounds(box.center, box.size);

            case SphereCollider sphere:
                return new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));

            case CapsuleCollider capsule:
                float diameter = capsule.radius * 2f;
                float length = Mathf.Max(capsule.height, diameter);

                // direction is 0, 1 or 2 for the X, Y or Z axis.
                var size = new Vector3(
                    capsule.direction == 0 ? length : diameter,
                    capsule.direction == 1 ? length : diameter,
                    capsule.direction == 2 ? length : diameter);
                return new Bounds(capsule.center, size);

            case MeshCollider mesh when mesh.sharedMesh != null:
                return mesh.sharedMesh.bounds;

            default:
                // Nothing else is expected on these modules; fall back to something that at least
                // covers the collider, undoing the transform scale so it stays a local measurement.
                Vector3 scale = collider.transform.lossyScale;
                Vector3 worldSize = collider.bounds.size;
                return new Bounds(Vector3.zero, new Vector3(
                    worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                    worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                    worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z))));
        }
    }

    void SetPreviewBlocked(bool value)
    {
        if (PreviewBlocked == value) return;

        PreviewBlocked = value;
        PreviewChanged?.Invoke();
    }

    void ClearGhost()
    {
        SetPreviewBlocked(false);
        if (ghost == null) return;

        // Deactivate before destroying: Destroy only takes effect at the end of the frame, and the
        // camera framing that runs in between must not measure a preview on its way out.
        ghost.SetActive(false);
        Destroy(ghost);
        ghost = null;
    }

    // ---------------------------------------------------------------- camera

    public void FrameWholeShip(bool resetZoom)
    {
        if (builderCamera == null || AssemblyRoot == null) return;

        framingWholeShip = true;
        builderCamera.Frame(AssemblyRoot, Vector3.zero, ComputeAssemblyRadius(), resetZoom);
    }

    // After the ship gains or loses a module: widen the shot if it was showing the whole ship, and
    // recover if the camera was zoomed in on the very module that just went away.
    void ReframeAfterChange()
    {
        if (builderCamera == null) return;

        if (framingWholeShip || !builderCamera.HasSubject) FrameWholeShip(framingWholeShip ? false : true);
    }

    public void FocusOn(PlacedPart part)
    {
        if (builderCamera == null || part == null) return;

        Bounds bounds = default;
        bool measured = false;

        // Only this module's own meshes: focusing a wing should not frame the guns hanging off it.
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

        if (!measured) return;

        framingWholeShip = false;
        builderCamera.Frame(part.transform, part.transform.InverseTransformPoint(bounds.center),
                            Mathf.Max(0.05f, bounds.extents.magnitude), true);
    }

    // Radius of a sphere around the stand containing every placed module. Measured from the stand
    // rather than from the bounds centre so that spinning the ship does not change the answer.
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
            foreach (HardPoint socket in sockets)
            {
                if (socket != null && socket.IsOccupied) count++;
            }
            return count;
        }
    }

    public float TotalMass
    {
        get
        {
            float mass = Core != null ? Core.Weight : 0f;
            foreach (HardPoint socket in sockets)
            {
                if (socket != null && socket.occupant != null) mass += socket.occupant.Weight;
            }
            return mass;
        }
    }

    // ---------------------------------------------------------------- input

    void Update()
    {
        Mouse mouse = Mouse.current;
        Camera view = builderCamera != null ? builderCamera.view : Camera.main;
        if (mouse == null || view == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Vector2 screenPosition = mouse.position.ReadValue();

        // Everything below queries physics, and physics only refreshes its copy of the transforms
        // before a simulation step. Dragging the ship round moves every collider on it, and a module
        // placed this frame is not there yet at all, so bring physics up to date first - otherwise
        // hovering, picking and occlusion all work against where things used to be.
        Physics.SyncTransforms();

        UpdateHover(view, screenPosition, overUI);

        if (mouse.leftButton.wasPressedThisFrame && !overUI) BeginPress(view, screenPosition);

        if (mouse.leftButton.isPressed && pressStartedOnModel)
        {
            Vector2 delta = mouse.delta.ReadValue();
            pressTravel += new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));

            if (!dragging && pressTravel.magnitude >= dragThreshold) dragging = true;
            if (dragging) RotateAssembly(view, delta);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (!dragging && pressedPart != null) SelectPart(pressedPart);

            dragging = false;
            pressStartedOnModel = false;
            pressedPart = null;
            pressTravel = Vector2.zero;
        }

        // Right click takes a module off the ship. Aimed at nothing in particular it puts the held
        // module back down instead, or pulls the camera out to the whole ship.
        if (mouse.rightButton.wasPressedThisFrame && !overUI)
        {
            PlacedPart target = PickPart(view, screenPosition);

            if (target != null && target != Core)
            {
                RemoveModule(target);
            }
            else if (HeldModule != null)
            {
                DropHeldModule();
            }
            else
            {
                SelectedPart = null;
                FrameWholeShip(true);
                AssemblyChanged?.Invoke();
            }
        }

        if (!overUI)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && builderCamera != null) builderCamera.Zoom(scroll);
        }
    }

    void LateUpdate()
    {
        if (++occlusionFrameCounter < Mathf.Max(1, occlusionCheckInterval)) return;

        occlusionFrameCounter = 0;
        UpdateMarkerOcclusion();
    }

    // While a module is in hand, the socket under the pointer previews it. Dragging the ship around
    // suppresses the preview, so a rotate does not flicker ghosts across every socket it passes.
    void UpdateHover(Camera view, Vector2 screenPosition, bool overUI)
    {
        if (HeldModule == null || overUI || dragging)
        {
            SetHoveredSocket(null);
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(view.ScreenPointToRay(screenPosition), 5000f, ~0, QueryTriggerInteraction.Collide);

        float nearest = float.MaxValue;
        HardPoint best = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearest) continue;

            var marker = hit.collider.GetComponentInParent<HardPointMarker>();
            if (marker == null || marker.hardPoint == null) continue;
            if (!Accepts(marker.hardPoint)) continue;

            nearest = hit.distance;
            best = marker.hardPoint;
        }

        SetHoveredSocket(best);
    }

    void BeginPress(Camera view, Vector2 screenPosition)
    {
        pressTravel = Vector2.zero;
        dragging = false;
        pressStartedOnModel = false;
        pressedPart = null;

        // A socket lit up for the module in hand takes the click and gets the module.
        if (hoveredSocket != null)
        {
            PlaceOn(hoveredSocket);
            return;
        }

        pressedPart = PickPart(view, screenPosition);
        pressStartedOnModel = pressedPart != null;
    }

    // Nearest placed module under the pointer. Triggers are ignored, which excludes both the socket
    // markers and the preview - only the solid ship answers.
    PlacedPart PickPart(Camera view, Vector2 screenPosition)
    {
        RaycastHit[] hits = Physics.RaycastAll(view.ScreenPointToRay(screenPosition), 5000f, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        PlacedPart best = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearest) continue;

            var part = hit.collider.GetComponentInParent<PlacedPart>();
            if (part == null) continue;

            nearest = hit.distance;
            best = part;
        }

        return best;
    }

    // Clicking a module with empty hands zooms in on it and marks it as the one a Remove would take.
    void SelectPart(PlacedPart part)
    {
        SelectedPart = part == Core ? null : part;
        FocusOn(part);
        AssemblyChanged?.Invoke();
    }

    void RotateAssembly(Camera view, Vector2 delta)
    {
        if (AssemblyRoot == null) return;

        AssemblyRoot.Rotate(view.transform.up, -delta.x * rotationSpeed, Space.World);
        AssemblyRoot.Rotate(view.transform.right, delta.y * rotationSpeed, Space.World);
    }
}
