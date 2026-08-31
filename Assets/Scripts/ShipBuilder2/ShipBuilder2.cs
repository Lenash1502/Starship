using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// The builder screen for the part library in Assets/Prefabs/ShipParts.
//
// Every prefab there declares only the sockets it really has, and parks a blockout copy of the
// matching generic module inside each one, so a bare core already shows where its wings, tails and
// thrusters are meant to go. Those blockouts are scenery - SocketPlaceholder keeps them out of
// picking, physics and overlap - and vanish as real parts take their place.
//
// The flow starts at the core. Nothing is on the stand when the screen opens: the first choice is
// which hull to build on, and only then do the other categories open up. From there the player
// picks a model from the list, the sockets that can take it light up, hovering one shows it ghosted
// into place and clicking bolts it on. The model stays in hand so a row of nine tails can be placed
// without going back to the list each time. Clicking a blockout instead asks the list to open that
// category, which is how an empty socket says what it is waiting for.
//
// What the player picks is a model, not a prefab: Aegis_Wing_L and Aegis_Wing_R are one entry in the
// list, and the socket decides which half gets used. Sockets and models are matched on category,
// with the side narrowing it - a left shoulder takes the left wing, and a mount that names no side
// takes whichever half the model offers.
[DisallowMultipleComponent]
public class ShipBuilder2 : MonoBehaviour
{
    public const string CoreCategory = PartNaming.CoreCategory;

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

    [Header("Socket Orientation")]
    [Tooltip("Categories whose parts are modelled on the hull's own axis, the way the engines and " +
             "the cores both run down Z. A socket of one of these that faces against the hull is a " +
             "mistake rather than a style, and is turned round when the part it belongs to is " +
             "registered. Add categories here as more of the library is built out.")]
    public string[] autoOrientCategories = { "Engine" };
    [Tooltip("How far past square on a socket has to face before it counts as facing backwards. " +
             "0 turns anything more than ninety degrees out; raise it to leave sockets that point " +
             "sideways alone and only correct the ones pointing squarely aft.")]
    [Range(0f, 0.95f)] public float autoOrientThreshold;

    [Header("Socket Blockouts")]
    [Tooltip("Show the generic module the artist parked in each empty socket, so the shape of what " +
             "belongs there is visible before anything is chosen.")]
    public bool showSocketPlaceholders = true;
    public Color placeholderColor = new Color(0.30f, 0.45f, 0.62f, 0.16f);
    public Color placeholderRimColor = new Color(0.45f, 0.65f, 0.85f, 0.45f);

    [Header("Preview")]
    public Color ghostColor = new Color(0.35f, 0.85f, 1f, 0.3f);
    public Color ghostRimColor = new Color(0.6f, 0.95f, 1f, 1f);
    [Tooltip("Colour of the preview when the part would bury itself in existing structure.")]
    public Color ghostBlockedColor = new Color(1f, 0.2f, 0.15f, 0.35f);
    public Color ghostBlockedRimColor = new Color(1f, 0.45f, 0.35f, 1f);

    [Header("Overlap Warning")]
    [Tooltip("Fraction of the part's own volume that may sit inside other structure before the " +
             "preview turns red. Joins overlap a little by design, so this is not zero.")]
    [Range(0f, 1f)] public float overlapWarningThreshold = 0.25f;
    [Tooltip("Points sampled inside the part to measure how much of it is buried. Higher is more " +
             "accurate and slower; every candidate socket is measured once when a part is picked " +
             "up and again whenever the ship changes, never per frame.")]
    [Range(24, 600)] public int overlapSampleCount = 160;
    [Tooltip("Ignore overlap with the part the socket belongs to. A part is designed to seat into " +
             "its parent, so counting that would flag every placement.")]
    public bool ignoreOverlapWithParent = true;
    [Tooltip("Do not show a socket at all when the part would clash there. Turn off to show it " +
             "anyway, previewed as a red hologram and still refused on click.")]
    public bool hideBlockedSockets = true;

    [Header("Random Ship")]
    [Tooltip("How many rounds of filling the random builder runs. 1 fills the core's own sockets, " +
             "2 also fills the sockets on whatever those parts brought with them, and so on.")]
    [Range(1, 6)] public int randomDepth = 2;
    [Tooltip("Ceiling on how many parts one generated ship may have, so a deep run cannot lock the " +
             "editor up measuring overlap for thousands of placements.")]
    public int maxRandomParts = 150;
    [Tooltip("How many candidates are tried per socket before it is left empty. Each try costs one " +
             "overlap measurement.")]
    [Range(1, 12)] public int randomCandidateTries = 5;
    [Tooltip("Sample count used for the overlap test while generating. Lower than the interactive " +
             "one because a generated ship makes hundreds of these measurements in a row.")]
    [Range(24, 300)] public int randomOverlapSampleCount = 80;
    [Tooltip("Give two sockets that differ only by side the same model, so generated ships come out " +
             "symmetrical instead of odd on every shoulder.")]
    public bool mirrorRandomShip = true;

    [Header("Rotation")]
    [Tooltip("Degrees of yaw/pitch per pixel of mouse travel while dragging the model.")]
    public float rotationSpeed = 0.35f;
    [Tooltip("Pixels of travel before a press counts as a drag rather than a click.")]
    public float dragThreshold = 4f;

    // Raised when the part in hand changes, when the ship itself changes, and when the socket
    // under the pointer starts or stops refusing the part.
    public event Action HeldPartChanged;
    public event Action AssemblyChanged;
    public event Action PreviewChanged;

    // Raised when the player clicks the hologram standing in for a part that has not been chosen
    // yet. The blockout is the clearest statement of "something goes here", so acting on it means
    // opening that category in the list - which is the list's business, not the builder's.
    public event Action<string> CategoryRequested;

    public Transform AssemblyRoot { get; private set; }
    public PlacedPart Core { get; private set; }

    // The model the player picked out of the list, waiting to be dropped onto a socket. A model,
    // not a prefab: which of its mirrored halves gets used is decided by the socket it lands on.
    // Never a core - a core goes straight onto the stand.
    public ShipPartFamily HeldPart { get; private set; }

    // What the player last clicked on the ship while empty handed, so it can be removed.
    public PlacedPart SelectedPart { get; private set; }

    // The hull currently on the stand, so the Core tab can show which one is in use.
    public ShipPartFamily CoreFamily { get; private set; }

    // True while the socket under the pointer would bury the part in existing structure, which is
    // only reachable with hideBlockedSockets turned off - normally such a socket is not shown at all.
    public bool PreviewBlocked { get; private set; }

    // Every category the catalog holds parts for, in tab order, with Core first.
    public List<string> Categories { get; private set; } = new List<string>();

    readonly Dictionary<string, List<ShipPartFamily>> familiesByCategory =
        new Dictionary<string, List<ShipPartFamily>>();

    // Handed back for any category the catalog has nothing for. Shared rather than freshly built
    // each time, so a socket nobody can fill costs nothing to ask about.
    static readonly List<ShipPartFamily> emptyFamilies = new List<ShipPartFamily>();

    readonly List<HardPoint> sockets = new List<HardPoint>();
    readonly List<Collider> overlapTargets = new List<Collider>();
    readonly List<Collider> shipColliders = new List<Collider>();

    // Sockets where the part in hand would end up buried in existing structure. Worked out once
    // when the part is picked up and again whenever the ship changes, rather than per hover: the
    // answer only depends on geometry, and the whole point is to know before the player points at it.
    readonly HashSet<HardPoint> blockedSockets = new HashSet<HardPoint>();

    // Scratch lists for the random builder, reused so a two hundred part ship does not leave two
    // hundred dead lists behind.
    readonly List<ShipPartFamily> randomCandidates = new List<ShipPartFamily>();
    readonly Dictionary<(PlacedPart owner, string category, string suffix), ShipPartFamily> randomFamilies =
        new Dictionary<(PlacedPart, string, string), ShipPartFamily>();

    // The distinct prefabs the model in hand would use across the sockets being measured - at most
    // its left and its right half, so one probe each rather than one per socket.
    readonly List<ShipPartDefinition> probeVariants = new List<ShipPartDefinition>();

    Material ghostMaterial;
    Material ghostBlockedMaterial;
    Material placeholderMaterial;
    Material markerMaterial;
    Mesh markerMesh;

    GameObject ghost;
    HardPoint hoveredSocket;
    bool framingWholeShip = true;
    bool warnedAboutSampling;

    bool dragging;
    bool pressStartedOnModel;
    PlacedPart pressedPart;
    HardPoint pressedPlaceholder;
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
        CollectCatalog();
    }

    void OnDestroy()
    {
        if (ghostMaterial != null) Destroy(ghostMaterial);
        if (ghostBlockedMaterial != null) Destroy(ghostBlockedMaterial);
        if (placeholderMaterial != null) Destroy(placeholderMaterial);
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

        // The blockouts share the preview's shader but sit far fainter: they are a hint about what
        // could go there, and must never compete with the part actually being previewed.
        placeholderMaterial = new Material(ghostShader);
        if (placeholderMaterial.HasProperty(ColorId)) placeholderMaterial.SetColor(ColorId, placeholderColor);
        if (placeholderMaterial.HasProperty(RimColorId)) placeholderMaterial.SetColor(RimColorId, placeholderRimColor);

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

    // ---------------------------------------------------------------- catalog

    // Splits the catalog into the tabs the list shows, folding each model's mirrored halves into a
    // single entry. Done once at startup: the library only changes when prefabs are added in the
    // editor, which means a catalog rebuild anyway.
    void CollectCatalog()
    {
        Categories.Clear();
        familiesByCategory.Clear();

        if (catalog == null)
        {
            Debug.LogError("[Ship Builder 2] No part catalog assigned. Run Tools > Ship Builder 2 > Rebuild Part Catalog.", this);
            return;
        }

        Categories = catalog.CategoriesInOrder();

        foreach (string category in Categories)
        {
            var families = new List<ShipPartFamily>();
            var byModel = new Dictionary<string, ShipPartFamily>();

            // The catalog is already sorted by model name, so grouping in order keeps the tab in
            // the same order the asset is in.
            foreach (ShipPartDefinition definition in catalog.PartsInCategory(category))
            {
                string key = definition.modelName ?? definition.prefab.name;

                if (!byModel.TryGetValue(key, out ShipPartFamily family))
                {
                    family = new ShipPartFamily
                    {
                        category = category,
                        modelName = key,
                        displayName = key
                    };

                    byModel.Add(key, family);
                    families.Add(family);
                }

                family.Add(definition);
            }

            familiesByCategory[category] = families;
        }

        if (!familiesByCategory.ContainsKey(CoreCategory))
        {
            Debug.LogError($"[Ship Builder 2] No Core prefabs in the catalog ({catalog.sourceFolder}). " +
                           "Nothing can be built until there is a hull to build on.", this);
        }
    }

    // The models on one tab. Never null, so callers can walk it without checking - a category with
    // no prefabs yet, which is most of the weapon mounts today, comes back empty. The list belongs
    // to the builder and is read only by convention: a generated ship asks for a category it cannot
    // fill dozens of times per run, and handing back a fresh list each time is pure garbage.
    public List<ShipPartFamily> FamiliesInCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return emptyFamilies;

        return familiesByCategory.TryGetValue(category, out List<ShipPartFamily> found) ? found : emptyFamilies;
    }

    // Whether a category is worth offering right now: everything waits on the core, and once the
    // core is down a category with no free socket for it has nowhere to go.
    public bool CategoryIsReachable(string category)
    {
        if (category == CoreCategory) return true;
        if (Core == null) return false;

        foreach (HardPoint socket in sockets)
        {
            if (socket != null && !socket.IsOccupied && socket.category == category) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- building

    // Puts a hull on the stand. Choosing a different core rebuilds from scratch, because the parts
    // hanging off the old one were fitted to sockets this one may not even have.
    public void PlaceCore(ShipPartFamily family)
    {
        if (family == null) return;

        // A hull sits on the stand rather than in a socket, so there is no side to resolve against.
        ShipPartDefinition definition = family.VariantFor(PartSide.None);
        if (definition == null || !definition.IsValid) return;

        ClearShip();

        GameObject instance = Instantiate(definition.prefab, AssemblyRoot);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Core = RegisterPart(instance, definition, null);
        Core.family = family;
        CoreFamily = family;

        RefreshPlacementOptions();
        FrameWholeShip(true);
        AssemblyChanged?.Invoke();
        HeldPartChanged?.Invoke();
    }

    // Strips the stand back to nothing. The old hierarchy is deactivated before it is destroyed:
    // Destroy only takes effect at the end of the frame, and everything that runs in between -
    // overlap measurement, picking, framing - must not still be finding the ship that just left.
    public void ClearShip()
    {
        ClearGhost();
        hoveredSocket = null;
        pressedPlaceholder = null;
        pressedPart = null;
        HeldPart = null;
        SelectedPart = null;
        Core = null;
        CoreFamily = null;

        sockets.Clear();
        blockedSockets.Clear();

        if (AssemblyRoot == null) return;

        for (int i = AssemblyRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = AssemblyRoot.GetChild(i).gameObject;
            child.transform.SetParent(null, false);
            child.SetActive(false);
            Destroy(child);
        }
    }

    // Picks a model up out of the list. Nothing is placed yet: the sockets that can take it simply
    // start showing, and the next click on one of them commits it. Cores are the exception - there
    // is only one place a hull can go, so choosing one puts it straight on the stand.
    public void HoldPart(ShipPartFamily family)
    {
        if (family == null) return;

        if (family.category == CoreCategory)
        {
            // Re-picking the hull already on the stand would only throw the ship away for nothing.
            if (CoreFamily == family) return;

            PlaceCore(family);
            return;
        }

        // Everything else hangs off the core, so there is nothing to hold before one is chosen.
        if (Core == null) return;

        // Clicking the model already in hand puts it back down.
        HeldPart = HeldPart == family ? null : family;

        SelectedPart = null;
        ClearGhost();
        RefreshPlacementOptions();
        HeldPartChanged?.Invoke();
    }

    public void DropHeldPart()
    {
        if (HeldPart == null) return;

        HeldPart = null;
        ClearGhost();
        RefreshPlacementOptions();
        HeldPartChanged?.Invoke();
    }

    // A socket takes the model in hand if it names that category, is still free, and the model has
    // a half that belongs on that side. Occupied ones are not offered; clearing one out goes through
    // Remove instead.
    public bool Accepts(HardPoint socket)
    {
        if (socket == null || HeldPart == null) return false;
        if (socket.IsOccupied || socket.category != HeldPart.category) return false;
        if (HeldPart.VariantFor(socket.EffectiveSide) == null) return false;

        // A socket where the part would clash is simply not offered, so there is no marker to
        // point at and no click to refuse.
        return !hideBlockedSockets || !blockedSockets.Contains(socket);
    }

    public void PlaceOn(HardPoint socket)
    {
        if (!Accepts(socket)) return;

        // Refused outright, whether or not the socket was hidden. Several sockets sit close enough
        // together that filling both would leave two parts in nearly the same space.
        if (blockedSockets.Contains(socket)) return;

        ClearGhost();
        hoveredSocket = null;

        // The socket decides which half of the model gets used: a left shoulder takes the left wing
        // without the player ever having been asked which one they meant.
        AttachPart(HeldPart, socket);

        // The model stays in hand, so a row of identical sockets can be filled in one go.
        RefreshPlacementOptions();
        ReframeAfterChange();
        AssemblyChanged?.Invoke();
    }

    // Bolts a part into a socket and registers it, without any of the refreshing that follows a
    // player placement - the random builder does hundreds of these and refreshes once at the end.
    //
    // Parenting to the socket is the whole placement: the empty already carries the position,
    // orientation and scale the model author intended for whatever bolts on there. The flip rides
    // on top of that, for the mounts where what the author intended is not what the part wants.
    PlacedPart AttachPart(ShipPartFamily family, HardPoint socket, PartFlip flip = PartFlip.None, bool handSwapped = false)
    {
        if (family == null || socket == null) return null;

        ShipPartDefinition definition = ResolveVariant(family, socket, handSwapped);
        if (definition == null || !definition.IsValid) return null;

        GameObject instance = Instantiate(definition.prefab, socket.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = PlacementRotation(socket, definition.category, flip);
        instance.transform.localScale = Vector3.one;

        PlacedPart placed = RegisterPart(instance, definition, socket);
        placed.family = family;
        placed.flip = flip;
        placed.handSwapped = handSwapped;
        placed.autoOriented = NeedsAutoOrient(socket, definition.category);

        socket.occupant = placed;
        socket.SetPlaceholdersVisible(false);

        return placed;
    }

    // Which half of a model goes into a socket: the one the socket's side asks for, unless the
    // player has overruled that for this particular part.
    static ShipPartDefinition ResolveVariant(ShipPartFamily family, HardPoint socket, bool handSwapped)
    {
        ShipPartDefinition chosen = family.VariantFor(socket.EffectiveSide);
        if (!handSwapped) return chosen;

        // Falls back to the side the socket asked for when the model has no other half, so the
        // override can never leave a socket empty.
        return family.Opposite(chosen) ?? chosen;
    }

    public void RemoveSelectedPart()
    {
        RemoveModule(SelectedPart);
    }

    // ---------------------------------------------------------------- reorienting

    public void FlipSelectedPart(PartFlip half)
    {
        FlipPart(SelectedPart, half);
    }

    // Turns a placed part through a half turn about one of its axes.
    //
    // Some sockets were authored pointing the wrong way for the part that ends up in them - an
    // engine facing forward, a gun aimed back down the hull - and which way is wrong depends on the
    // configuration, so it cannot be fixed once in the prefab. Anything hanging off the part comes
    // round with it, since it is all parented underneath.
    public void FlipPart(PlacedPart part, PartFlip half)
    {
        if (part == null || half == PartFlip.None) return;

        // Turning the hull on its stand would only turn the whole ship, which is what dragging is
        // for.
        if (part == Core) return;

        part.flip = PartFlips.Compose(part.flip, half);

        // Layered on top of the automatic correction rather than replacing it, so a player who
        // turns a straightened engine gets the mount's original facing back - an override of the
        // correction, not a fight with it.
        part.transform.localRotation = PlacementRotation(part.attachedTo, part.definition.category, part.flip);

        // The part now occupies different space, so both the flank its own sockets sit on and what
        // would clash with it have changed.
        Physics.SyncTransforms();
        RefreshGeometricSides(part);

        RefreshPlacementOptions();
        AssemblyChanged?.Invoke();
    }

    // Whether a part can be swapped for the opposite half of its own model.
    public bool CanMirror(PlacedPart part)
    {
        return part != null && part != Core && part.attachedTo != null
               && part.family != null && part.family.IsMirrored;
    }

    public void MirrorSelectedPart()
    {
        MirrorPart(SelectedPart);
    }

    // Swaps a part for the other half of its model - the left wing for the right one - in the socket
    // it is already in.
    //
    // This is the answer when the side was read wrong rather than when the orientation was: an
    // unsuffixed mount is assigned a flank by where it sits, and where that guess is off, no amount
    // of turning will produce the shape of the other hand. A mirrored prefab is used rather than a
    // negative scale, which would invert the model's normals and light it inside out.
    //
    // Everything hanging off the part is rebuilt onto the matching sockets of its mirror image. The
    // children are re-resolved rather than copied across, so a left wing's left hand guns become
    // right hand guns on the right wing without anyone having to say so.
    public void MirrorPart(PlacedPart part)
    {
        if (!CanMirror(part)) return;

        HardPoint socket = part.attachedTo;
        bool wasSelected = SelectedPart == part;

        PlacementSnapshot snapshot = CaptureSubtree(part);
        snapshot.handSwapped = !snapshot.handSwapped;

        ClearGhost();
        hoveredSocket = null;
        RemovePart(part);

        PlacedPart rebuilt = RestoreSubtree(snapshot, socket, out int dropped);
        SelectedPart = wasSelected ? rebuilt : null;

        RefreshPlacementOptions();

        // The camera was anchored to the part that has just been destroyed, so it has to be given
        // the replacement rather than being left to fall back to the whole ship - the player is
        // mirroring this part precisely because they are looking at it.
        if (rebuilt != null && wasSelected) FocusOn(rebuilt);
        else ReframeAfterChange();

        AssemblyChanged?.Invoke();

        if (dropped > 0)
        {
            Debug.LogWarning($"[Ship Builder 2] Mirroring {snapshot.family.displayName} dropped {dropped} " +
                             "attached part(s): the mirrored model does not have the sockets they were in.", this);
        }
    }

    // What a part is, rather than which objects it is made of, so it can be built again from
    // scratch. Models rather than prefabs, so the rebuild re-resolves every side from the sockets
    // it lands in.
    class PlacementSnapshot
    {
        public ShipPartFamily family;
        public PartFlip flip;
        public bool handSwapped;

        // Which socket of the parent this sat in. Matched by name on the way back, because the two
        // halves of a mirrored model carry the same socket names.
        public string socketName;

        public readonly List<PlacementSnapshot> children = new List<PlacementSnapshot>();
    }

    PlacementSnapshot CaptureSubtree(PlacedPart part)
    {
        var snapshot = new PlacementSnapshot
        {
            family = part.family,
            flip = part.flip,
            handSwapped = part.handSwapped
        };

        foreach (HardPoint socket in part.hardPoints)
        {
            if (socket == null || socket.occupant == null) continue;

            PlacementSnapshot child = CaptureSubtree(socket.occupant);
            child.socketName = socket.name;
            snapshot.children.Add(child);
        }

        return snapshot;
    }

    PlacedPart RestoreSubtree(PlacementSnapshot snapshot, HardPoint socket, out int dropped)
    {
        dropped = 0;

        PlacedPart placed = AttachPart(snapshot.family, socket, snapshot.flip, snapshot.handSwapped);
        if (placed == null)
        {
            dropped = CountParts(snapshot);
            return null;
        }

        // Several sockets on a part can share a name, so each one is claimed as it is filled and the
        // next child of that name goes to the following one.
        var claimed = new HashSet<HardPoint>();

        foreach (PlacementSnapshot child in snapshot.children)
        {
            HardPoint target = FindSocketByName(placed, child.socketName, claimed);
            if (target == null)
            {
                dropped += CountParts(child);
                continue;
            }

            claimed.Add(target);
            RestoreSubtree(child, target, out int lost);
            dropped += lost;
        }

        return placed;
    }

    static HardPoint FindSocketByName(PlacedPart part, string socketName, HashSet<HardPoint> claimed)
    {
        foreach (HardPoint socket in part.hardPoints)
        {
            if (socket == null || socket.IsOccupied) continue;
            if (claimed.Contains(socket)) continue;
            if (socket.name != socketName) continue;

            return socket;
        }
        return null;
    }

    static int CountParts(PlacementSnapshot snapshot)
    {
        int count = 1;
        foreach (PlacementSnapshot child in snapshot.children) count += CountParts(child);
        return count;
    }

    // Takes a part off the ship, along with everything hanging from it. The core is not removable
    // here: it is the thing the rest is built on, and swapping it goes through the Core tab.
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
            if (pressedPlaceholder == socket) pressedPlaceholder = null;
            blockedSockets.Remove(socket);
            sockets.Remove(socket);
        }

        if (part.attachedTo != null)
        {
            part.attachedTo.occupant = null;
            part.attachedTo.SetPlaceholdersVisible(showSocketPlaceholders);
        }

        if (part == Core) Core = null;
        if (SelectedPart == part) SelectedPart = null;

        // Out of the hierarchy and switched off before the deferred Destroy lands, so the overlap
        // pass that runs immediately after this is not still measuring against a part that is gone.
        part.transform.SetParent(null, false);
        part.gameObject.SetActive(false);
        Destroy(part.gameObject);
    }

    PlacedPart RegisterPart(GameObject instance, ShipPartDefinition definition, HardPoint attachedTo)
    {
        var placed = instance.AddComponent<PlacedPart>();
        placed.definition = definition;
        placed.attachedTo = attachedTo;

        ScanForSockets(instance.transform, placed);

        // After the scan, so the blockouts sitting in this part's own sockets are already tagged and
        // stay out of its renderer list.
        placed.CaptureRenderers();

        // Measured once here rather than per query: the sockets do not move relative to the part
        // they belong to, however the ship is turned.
        float deadZone = CentrelineDeadZone(placed);

        foreach (HardPoint socket in placed.hardPoints)
        {
            socket.geometricSide = MeasureGeometricSide(socket, deadZone);
            socket.marker = CreateMarker(socket);
            socket.SetPlaceholdersVisible(showSocketPlaceholders);
        }

        return placed;
    }

    // ---------------------------------------------------------------- orientation

    // How a part sits in a socket: the automatic correction for a mount that faces the wrong way,
    // with whatever half turn the player has since asked for on top.
    //
    // Nothing here touches the socket, let alone the prefab it came from. The sockets stay exactly
    // as the artist left them - the blockout standing in an empty one still shows the mount's own
    // orientation - and the correction lives on the part that gets bolted in.
    Quaternion PlacementRotation(HardPoint socket, string category, PartFlip flip)
    {
        return AutoOrientRotation(socket, category) * PartFlips.ToRotation(flip);
    }

    // Straightens a part whose mount faces against the hull.
    //
    // The engines, like the cores, are all modelled pointing down Z, so a socket whose own forward
    // runs the other way seats one facing backwards. Nothing in the part or in the socket's name
    // says so - the only evidence is the direction the mount points, which is what this reads.
    //
    // A half turn about the socket's up axis is the correction: a half turn negates everything
    // perpendicular to its axis, so forward reverses exactly whatever the mount's orientation
    // happens to be, and up is left where it was.
    Quaternion AutoOrientRotation(HardPoint socket, string category)
    {
        return NeedsAutoOrient(socket, category) ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
    }

    public bool NeedsAutoOrient(HardPoint socket, string category)
    {
        if (socket == null || AssemblyRoot == null) return false;
        if (!ShouldAutoOrient(category)) return false;

        // Both directions are read in world space and turn together when the player spins the model,
        // so the comparison is about the ship rather than about the camera.
        return Vector3.Dot(socket.transform.forward, AssemblyRoot.forward) < -autoOrientThreshold;
    }

    bool ShouldAutoOrient(string category)
    {
        if (autoOrientCategories == null || string.IsNullOrEmpty(category)) return false;

        foreach (string wanted in autoOrientCategories)
        {
            if (PartNaming.NormalizeCategory(wanted) == category) return true;
        }
        return false;
    }

    // Which flank a socket sits on, measured against the ship's centreline. Left is negative X
    // throughout this library: every mirrored pair in it puts "_L" at a negative offset and "_R" at
    // the matching positive one, and the mounts that are genuinely central sit at exactly zero.
    //
    // The measurement is in the assembly's frame rather than the owning part's, because that is the
    // frame the answer is about. It also survives the player spinning the model, which turns the
    // assembly itself and so leaves everything under it where it was, and it stays true when a part
    // is flipped - a half turn really does swing its sockets across to the other flank.
    PartSide MeasureGeometricSide(HardPoint socket, float deadZone)
    {
        if (AssemblyRoot == null) return PartSide.None;

        float x = AssemblyRoot.InverseTransformPoint(socket.transform.position).x;

        if (x < -deadZone) return PartSide.Left;
        if (x > deadZone) return PartSide.Right;
        return PartSide.None;
    }

    // How far off the centreline a socket has to sit before it counts as being on one side.
    //
    // Scaled to the part rather than fixed, so it means the same thing on a capital hull and on a
    // fin: a socket that is meant to be central but landed a hair off zero stays central, while the
    // real shoulder mounts - out at a third of the half width or more - are never in doubt.
    float CentrelineDeadZone(PlacedPart part)
    {
        if (AssemblyRoot == null) return 0f;

        float halfWidth = 0f;

        foreach (Renderer renderer in part.renderers)
        {
            if (renderer == null) continue;

            Bounds bounds = renderer.bounds;
            Vector3 extents = bounds.extents;

            // The corners have to be walked because the assembly's frame is not axis aligned with
            // the world once the ship has been turned.
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);

                float x = AssemblyRoot.InverseTransformPoint(bounds.center + offset).x;
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(x));
            }
        }

        return halfWidth * 0.05f;
    }

    // Re-reads the flank of every socket at or below a part. Needed after a half turn, which moves
    // that part's sockets across the hull without any of them changing name.
    void RefreshGeometricSides(PlacedPart part)
    {
        if (part == null) return;

        float deadZone = CentrelineDeadZone(part);

        foreach (HardPoint socket in part.hardPoints)
        {
            if (socket == null) continue;

            socket.geometricSide = MeasureGeometricSide(socket, deadZone);
            if (socket.occupant != null) RefreshGeometricSides(socket.occupant);
        }
    }

    // Walks a freshly spawned part looking for its sockets.
    //
    // Recursive rather than a flat GetComponentsInChildren because the walk has to stop at each
    // hard point: everything below one is the blockout model parked there, and that model declares
    // hard points of its own. Registering those would offer the player sockets on a part that does
    // not exist yet.
    void ScanForSockets(Transform node, PlacedPart placed)
    {
        foreach (Transform child in node)
        {
            if (!PartNaming.TryParseHardPoint(child.name, out string category, out PartSide side, out string suffix))
            {
                ScanForSockets(child, placed);
                continue;
            }

            var socket = child.gameObject.AddComponent<HardPoint>();
            socket.category = category;
            socket.side = side;
            socket.suffix = suffix;
            socket.owner = placed;

            // Whatever the prefab already had inside the socket is blockout. Captured now, before
            // any marker or real part is parented here, so only the artist's models are tagged.
            foreach (Transform blockout in child)
            {
                var placeholder = blockout.gameObject.AddComponent<SocketPlaceholder>();
                placeholder.Capture(socket);
                placeholder.ApplyMaterial(placeholderMaterial);
                socket.placeholders.Add(placeholder);
            }

            placed.hardPoints.Add(socket);
            sockets.Add(socket);
        }
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

        // Sockets are scaled to the part they expect, so undo that to keep every marker the same
        // physical size no matter how deep in the hierarchy it sits.
        Vector3 parentScale = socket.transform.lossyScale;
        float compensation = Mathf.Max(0.0001f, (Mathf.Abs(parentScale.x) + Mathf.Abs(parentScale.y) + Mathf.Abs(parentScale.z)) / 3f);
        markerObject.transform.localScale = Vector3.one * (markerSize / compensation);

        var marker = markerObject.AddComponent<HardPointMarker>();
        marker.hardPoint = socket;
        return marker;
    }

    // ---------------------------------------------------------------- random ships

    public void GenerateRandomShip()
    {
        GenerateRandomShip(randomDepth);
    }

    // Builds a whole ship without the player: a random hull, then depth rounds of filling whatever
    // sockets the last round opened up.
    //
    // Every placement goes through the same overlap test the interactive builder uses, so a
    // generated ship never has two parts sharing the same space - a socket whose candidates all
    // clash is simply left empty, which is also what happens to categories with no prefabs yet.
    public void GenerateRandomShip(int depth)
    {
        List<ShipPartFamily> cores = FamiliesInCategory(CoreCategory);
        if (cores.Count == 0)
        {
            Debug.LogError("[Ship Builder 2] Cannot generate a ship: the catalog has no cores.", this);
            return;
        }

        var random = new System.Random();
        var timer = System.Diagnostics.Stopwatch.StartNew();

        randomFamilies.Clear();
        PlaceCore(cores[random.Next(cores.Count)]);
        if (Core == null) return;

        var frontier = new List<PlacedPart> { Core };
        var next = new List<PlacedPart>();
        int placedCount = 0;

        for (int level = 0; level < Mathf.Max(1, depth) && frontier.Count > 0; level++)
        {
            next.Clear();

            foreach (PlacedPart parent in frontier)
            {
                if (parent == null) continue;

                // Indexed rather than foreach: a part placed in this loop adds its own sockets to
                // the shared list, and those belong to the next round, not this one.
                int socketCount = parent.hardPoints.Count;
                for (int i = 0; i < socketCount; i++)
                {
                    if (placedCount >= maxRandomParts) break;

                    HardPoint socket = parent.hardPoints[i];
                    if (socket == null || socket.IsOccupied) continue;

                    ShipPartFamily chosen = ChooseRandomPart(socket, random);
                    if (chosen == null) continue;

                    PlacedPart placed = AttachPart(chosen, socket);
                    if (placed == null) continue;

                    next.Add(placed);
                    placedCount++;
                }
            }

            frontier.Clear();
            frontier.AddRange(next);
        }

        RefreshPlacementOptions();
        FrameWholeShip(true);
        AssemblyChanged?.Invoke();

        Debug.Log($"[Ship Builder 2] Generated {Core.definition.displayName} with {placedCount} parts " +
                  $"at depth {depth} in {timer.ElapsedMilliseconds} ms.", this);
    }

    // Picks a model that fits a socket and does not bury itself in what is already there, or
    // nothing if the category has no prefabs or every try clashed.
    ShipPartFamily ChooseRandomPart(HardPoint socket, System.Random random)
    {
        List<ShipPartFamily> available = FamiliesInCategory(socket.category);
        if (available.Count == 0) return null;

        PartSide socketSide = socket.EffectiveSide;

        randomCandidates.Clear();
        foreach (ShipPartFamily family in available)
        {
            if (family.Fits(socketSide)) randomCandidates.Add(family);
        }
        if (randomCandidates.Count == 0) return null;

        Shuffle(randomCandidates, random);

        // A socket and its mirror image differ only by the side in their names, so remembering what
        // went into one and putting the same model into the other is what makes generated ships
        // symmetrical. The remembered model is moved to the front rather than forced, so one that
        // would clash on this side still gives way to something that fits.
        (PlacedPart, string, string) mirrorKey = MirrorKey(socket);
        if (mirrorRandomShip && randomFamilies.TryGetValue(mirrorKey, out ShipPartFamily twin))
        {
            int found = randomCandidates.IndexOf(twin);
            if (found > 0)
            {
                randomCandidates.RemoveAt(found);
                randomCandidates.Insert(0, twin);
            }
        }

        // Gathered once for the socket rather than once per try: the ship does not change until
        // something is actually placed.
        GatherShipColliders();

        int tries = Mathf.Min(randomCandidates.Count, Mathf.Max(1, randomCandidateTries));
        for (int i = 0; i < tries; i++)
        {
            ShipPartFamily candidate = randomCandidates[i];
            ShipPartDefinition variant = candidate.VariantFor(socketSide);
            if (variant == null) continue;

            if (MeasureCandidateOverlap(variant, socket, randomOverlapSampleCount) >= overlapWarningThreshold) continue;

            if (mirrorRandomShip) randomFamilies[mirrorKey] = candidate;
            return candidate;
        }

        return null;
    }

    // Identifies a socket independently of which side it is on, so "WingHardPoint_L" and
    // "WingHardPoint_R" on the same part share an entry.
    static (PlacedPart, string, string) MirrorKey(HardPoint socket)
    {
        return (socket.owner, socket.category, PartNaming.SuffixWithoutSide(socket.suffix));
    }

    static void Shuffle(List<ShipPartFamily> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ---------------------------------------------------------------- markers

    // Works out where the part in hand could actually go, then redraws the markers to match.
    // Called when the part changes and whenever the ship gains or loses a part, which is exactly
    // when the answer can change - it does not depend on where the camera is or how the ship is
    // turned, so spinning the model costs nothing.
    void RefreshPlacementOptions()
    {
        EvaluateBlockedSockets();
        RefreshAllMarkers();
        RefreshPlaceholders();
    }

    void RefreshPlaceholders()
    {
        foreach (HardPoint socket in sockets)
        {
            if (socket == null) continue;
            socket.SetPlaceholdersVisible(showSocketPlaceholders && !socket.IsOccupied);
        }
    }

    // Tries the model in hand against every free socket of its category and records the ones where
    // it would clash.
    //
    // A single probe is moved from socket to socket rather than one being spawned per socket. The
    // model can resolve to a different prefab on either side of the hull, so the sockets are worked
    // through one variant at a time: two Instantiates for a mirrored wing, one for anything else,
    // no matter how many sockets there are.
    void EvaluateBlockedSockets()
    {
        blockedSockets.Clear();
        if (HeldPart == null || AssemblyRoot == null) return;

        GatherShipColliders();
        if (shipColliders.Count == 0) return;

        probeVariants.Clear();
        foreach (HardPoint socket in sockets)
        {
            if (!IsCandidateSocket(socket)) continue;

            ShipPartDefinition variant = HeldPart.VariantFor(socket.EffectiveSide);
            if (variant != null && !probeVariants.Contains(variant)) probeVariants.Add(variant);
        }

        foreach (ShipPartDefinition variant in probeVariants)
        {
            GameObject probe = CreateProbe(variant);

            foreach (HardPoint socket in sockets)
            {
                if (!IsCandidateSocket(socket)) continue;
                if (HeldPart.VariantFor(socket.EffectiveSide) != variant) continue;

                probe.transform.SetParent(socket.transform, false);
                probe.transform.localPosition = Vector3.zero;
                probe.transform.localRotation = PlacementRotation(socket, variant.category, PartFlip.None);
                probe.transform.localScale = Vector3.one;

                float overlap = MeasureOverlapFraction(probe, ignoreOverlapWithParent ? socket.owner : null, overlapSampleCount);
                if (overlap >= overlapWarningThreshold) blockedSockets.Add(socket);
            }

            DestroyProbe(probe);
        }
    }

    // A free socket of the right category. Deliberately not Accepts, which also consults the very
    // set this pass is building.
    bool IsCandidateSocket(HardPoint socket)
    {
        return socket != null && !socket.IsOccupied && HeldPart != null && socket.category == HeldPart.category;
    }

    // One candidate against one socket. Used by the random builder, which asks about a different
    // prefab every time and so cannot reuse a single probe the way the pass above does.
    float MeasureCandidateOverlap(ShipPartDefinition definition, HardPoint socket, int samples)
    {
        if (shipColliders.Count == 0) return 0f;

        GameObject probe = CreateProbe(definition);
        probe.transform.SetParent(socket.transform, false);
        probe.transform.localPosition = Vector3.zero;
        probe.transform.localRotation = PlacementRotation(socket, definition.category, PartFlip.None);
        probe.transform.localScale = Vector3.one;

        float overlap = MeasureOverlapFraction(probe, ignoreOverlapWithParent ? socket.owner : null, samples);

        DestroyProbe(probe);
        return overlap;
    }

    // An invisible stand-in for a part, used to ask "would this fit here" without committing to it.
    GameObject CreateProbe(ShipPartDefinition definition)
    {
        GameObject probe = Instantiate(definition.prefab);
        probe.name = "PlacementProbe";
        probe.hideFlags = HideFlags.HideInHierarchy;

        // The blockouts inside the probe's own sockets are not part of it. Left in, they would be
        // measured as its volume and report a clash against structure the real part never touches.
        SocketPlaceholder.SuppressAllIn(probe);

        // Invisible, and triggers so it never shows up in picking, hovering or the occlusion sweep.
        foreach (Renderer renderer in probe.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (Collider collider in probe.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled) collider.isTrigger = true;
        }

        return probe;
    }

    void DestroyProbe(GameObject probe)
    {
        if (probe == null) return;

        // Deactivated first: Destroy lands at the end of the frame, and the measurements that follow
        // in this same frame must not find the probe still parented into the ship.
        probe.transform.SetParent(null, false);
        probe.SetActive(false);
        Destroy(probe);
    }

    // Every solid collider belonging to a placed part. Gathered once per pass so moving the probe
    // between sockets does not re-walk the hierarchy each time.
    void GatherShipColliders()
    {
        shipColliders.Clear();
        if (AssemblyRoot == null) return;

        foreach (Collider collider in AssemblyRoot.GetComponentsInChildren<Collider>())
        {
            // The blockouts keep their colliders, switched off. They have to be dropped explicitly:
            // GetComponentsInChildren filters on the object being active, not on the component being
            // enabled, and a disabled collider is worse than useless here - ClosestPoint hands back
            // the point it was given, which this class reads as "inside", so every sample would
            // count as buried and nothing would ever be placeable.
            if (!collider.enabled) continue;

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
        if (socket == null || HeldPart == null) return;

        // The same resolution the placement will use, so the hologram is the part that would
        // actually be bolted on rather than a stand-in for its mirror image.
        ShipPartDefinition variant = HeldPart.VariantFor(socket.EffectiveSide);
        if (variant == null) return;

        ghost = Instantiate(variant.prefab, socket.transform);
        ghost.name = "PartPreview";
        ghost.transform.localPosition = Vector3.zero;

        // The same correction the placement will apply, so the hologram is not showing a facing the
        // part will not end up in.
        ghost.transform.localRotation = PlacementRotation(socket, variant.category, PartFlip.None);
        ghost.transform.localScale = Vector3.one;

        // The preview shows the part, not the blockouts waiting inside its own empty sockets.
        SocketPlaceholder.SuppressAllIn(ghost);

        // Left enabled but turned into triggers: the overlap measurement needs to query their
        // shapes, while every raycast in this class either ignores triggers or looks for something
        // the preview does not have.
        foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled) collider.isTrigger = true;
        }

        // Already known from the pass that ran when the part was picked up, so hovering costs
        // nothing. With hideBlockedSockets on this is always false, because a blocked socket has no
        // marker to hover in the first place.
        SetPreviewBlocked(blockedSockets.Contains(socket));

        Material material = PreviewBlocked ? ghostBlockedMaterial : ghostMaterial;
        foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;

            var ghostMaterials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < ghostMaterials.Length; i++) ghostMaterials[i] = material;
            renderer.sharedMaterials = ghostMaterials;
        }
    }

    // How much of the part would end up buried inside structure that is already there, as a
    // fraction of its own volume.
    //
    // Measured by scattering points through the part's own colliders and asking how many of them
    // land inside somebody else's. Sampling the real collider shapes rather than their bounding
    // boxes matters here: a wing is mostly empty space inside its own box, and judging by boxes
    // would call every wing a clash. All the part colliders are boxes, capsules and spheres, so
    // Collider.ClosestPoint answers "is this point inside" exactly.
    float MeasureOverlapFraction(GameObject candidate, PlacedPart ignore, int sampleBudget)
    {
        Collider[] all = candidate.GetComponentsInChildren<Collider>();
        if (all.Length == 0 || AssemblyRoot == null) return 0f;

        // Disabled colliders belong to the blockouts inside the candidate's own sockets, which are
        // not part of its volume.
        var candidateColliders = new List<Collider>(all.Length);
        foreach (Collider collider in all)
        {
            if (collider.enabled) candidateColliders.Add(collider);
        }
        if (candidateColliders.Count == 0) return 0f;

        // Both Collider.bounds and Collider.ClosestPoint read the physics engine's copy of the
        // world, not the transforms. Unity only refreshes that copy before a simulation step, so
        // the preview - parented and positioned microseconds ago - and any part placed since the
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
        int perCollider = Mathf.Max(8, sampleBudget / candidateColliders.Count);
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
            // Reaching here means no point could be placed inside the part's own colliders, so
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
                // Nothing else is expected on these parts; fall back to something that at least
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

        float radius = ComputeAssemblyRadius();
        if (radius <= 0f) return;

        builderCamera.Frame(AssemblyRoot, Vector3.zero, radius, resetZoom);
    }

    // After the ship gains or loses a part: widen the shot if it was showing the whole ship, and
    // recover if the camera was zoomed in on the very part that just went away.
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

        // Only this part's own meshes: focusing a wing should not frame the guns hanging off it.
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

    // Radius of a sphere around the stand containing every placed part. Measured from the stand
    // rather than from the bounds centre so that spinning the ship does not change the answer.
    float ComputeAssemblyRadius()
    {
        Vector3 origin = AssemblyRoot.position;
        float radius = 0f;

        foreach (Renderer renderer in AssemblyRoot.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.GetComponentInParent<HardPointMarker>() != null) continue;

            // Blockouts are a hint, not the ship. Framing on them would make the shot jump every
            // time one is replaced by the real part, which is usually a different size.
            if (renderer.GetComponentInParent<SocketPlaceholder>() != null) continue;

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

    public int FreeSocketCount
    {
        get
        {
            int count = 0;
            foreach (HardPoint socket in sockets)
            {
                if (socket != null && !socket.IsOccupied) count++;
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
        // before a simulation step. Dragging the ship round moves every collider on it, and a part
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
            if (!dragging)
            {
                if (pressedPart != null) SelectPart(pressedPart);
                else if (pressedPlaceholder != null) RequestCategoryFor(pressedPlaceholder);
            }

            dragging = false;
            pressStartedOnModel = false;
            pressedPart = null;
            pressedPlaceholder = null;
            pressTravel = Vector2.zero;
        }

        // Right click takes a part off the ship. Aimed at nothing in particular it puts the held
        // part back down instead, or pulls the camera out to the whole ship.
        if (mouse.rightButton.wasPressedThisFrame && !overUI)
        {
            PlacedPart target = PickPart(view, screenPosition);

            if (target != null && target != Core)
            {
                RemoveModule(target);
            }
            else if (HeldPart != null)
            {
                DropHeldPart();
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

        UpdateReorientShortcuts();
    }

    // Straightening a mount is fiddly work - turn it, look, turn it again - so it is on the keyboard
    // as well as on the buttons, and stays under the hand that is already spinning the model.
    void UpdateReorientShortcuts()
    {
        if (SelectedPart == null) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.fKey.wasPressedThisFrame) FlipSelectedPart(PartFlip.Turn);
        else if (keyboard.tKey.wasPressedThisFrame) FlipSelectedPart(PartFlip.Tilt);
        else if (keyboard.rKey.wasPressedThisFrame) FlipSelectedPart(PartFlip.Roll);
        else if (keyboard.mKey.wasPressedThisFrame) MirrorSelectedPart();
    }

    void LateUpdate()
    {
        if (++occlusionFrameCounter < Mathf.Max(1, occlusionCheckInterval)) return;

        occlusionFrameCounter = 0;
        UpdateMarkerOcclusion();
    }

    // While a part is in hand, the socket under the pointer previews it. Dragging the ship around
    // suppresses the preview, so a rotate does not flicker ghosts across every socket it passes.
    void UpdateHover(Camera view, Vector2 screenPosition, bool overUI)
    {
        if (HeldPart == null || overUI || dragging)
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
        pressedPlaceholder = null;

        // A socket lit up for the model in hand takes the click and gets the part.
        if (hoveredSocket != null)
        {
            PlaceOn(hoveredSocket);
            return;
        }

        // Solid structure first: a blockout only answers where there is nothing real in front of it.
        pressedPart = PickPart(view, screenPosition);
        if (pressedPart == null) pressedPlaceholder = PickPlaceholder(view, screenPosition);

        // Either one counts as having grabbed the model, so a drag that starts on a hologram spins
        // the ship instead of doing nothing.
        pressStartedOnModel = pressedPart != null || pressedPlaceholder != null;
    }

    // Nearest empty socket's blockout under the pointer. Blockouts are triggers, so this is the only
    // query in the class that goes looking for them - everything else steps straight past.
    HardPoint PickPlaceholder(Camera view, Vector2 screenPosition)
    {
        RaycastHit[] hits = Physics.RaycastAll(view.ScreenPointToRay(screenPosition), 5000f, ~0, QueryTriggerInteraction.Collide);

        float nearest = float.MaxValue;
        HardPoint best = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearest) continue;

            var placeholder = hit.collider.GetComponentInParent<SocketPlaceholder>();
            if (placeholder == null || placeholder.socket == null || placeholder.socket.IsOccupied) continue;

            nearest = hit.distance;
            best = placeholder.socket;
        }

        return best;
    }

    // Clicking the hologram in an empty socket is the player pointing at a gap and asking what goes
    // in it, so the answer is to open that category in the list. Anything already in hand of a
    // different category is put down first - having asked for tails, holding a wing is just a trap
    // waiting to place the wrong part on the next click.
    void RequestCategoryFor(HardPoint socket)
    {
        if (socket == null || string.IsNullOrEmpty(socket.category)) return;

        if (HeldPart != null && HeldPart.category != socket.category) DropHeldPart();

        SelectedPart = null;
        CategoryRequested?.Invoke(socket.category);
    }

    // Nearest placed part under the pointer. Triggers are ignored, which excludes both the socket
    // markers and the preview - only the solid ship answers. The blockouts have no live colliders
    // at all, so a click passes straight through them.
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

    // Clicking a part with empty hands zooms in on it and marks it as the one a Remove would take.
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
