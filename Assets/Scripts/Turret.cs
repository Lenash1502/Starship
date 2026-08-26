using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Drives a Yaw -> Pitch -> Gun rig so the gun tracks a world point, without letting the barrel swing
// into the ship it is bolted to.
//
// Put this on the fixed turret point (the parent of the Yaw node). At Start it sweeps every
// orientation the rig can reach and bakes two things per orientation: whether the gun body fits
// there, and whether a shot from there would pass through our own hull. See TurretClearanceMask.
//
// The sweep tests the ClearanceCollider, which should wrap only the *exposed* part of the gun. That
// is what makes deliberate embedding free: the length of barrel buried in the wing is not inside the
// collider, so it is never tested, and only a barrel that swings somewhere it has no business being
// -- through the cockpit, into the far wing -- registers as a hit.
public class Turret : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Child that yaws. Leave empty to look for a child named 'Yaw'.")]
    public Transform yawJoint;

    [Tooltip("Child of the yaw joint that pitches. Leave empty to look for a child named 'Pitch'.")]
    public Transform pitchJoint;

    [Tooltip("The bore. Its +Y is the firing direction, matching WeaponMuzzle. Leave empty to use the first muzzle found under the pitch joint.")]
    public Transform bore;

    [Tooltip("Collider wrapping the exposed part of the gun, used to test what the barrel would hit. Leave empty to look for a child named 'ClearanceCollider'.")]
    public Collider clearanceCollider;

    [Header("Axes")]
    [Tooltip("Axis the yaw joint turns about, in its own local space.")]
    public Vector3 yawAxis = Vector3.up;

    [Tooltip("Axis the pitch joint turns about, in its own local space. Negate it if the gun pitches the wrong way.")]
    public Vector3 pitchAxis = Vector3.right;

    [Header("Traverse")]
    [Tooltip("How fast the turret slews, in degrees per second. This is the mechanical limit -- it is what makes the gun lag a fast-crossing target.")]
    public float traverseSpeed = 120f;

    [Tooltip("Hard yaw limits in degrees, relative to the rest pose. A full -180..180 range makes the turret free-traversing.")]
    public float yawMin = -180f;
    public float yawMax = 180f;

    [Tooltip("Hard pitch limits in degrees, relative to the rest pose.")]
    public float pitchMin = -80f;
    public float pitchMax = 80f;

    [Header("Firing Solution")]
    [Tooltip("How close the bore has to be to the target before the gun is allowed to shoot, in degrees. Without this the turret fires wildly off-axis while it is still slewing.")]
    public float onTargetCone = 3f;

    [Tooltip("How far ahead to look for our own hull when checking the line of fire. Only needs to cover the ship.")]
    public float selfCheckRange = 80f;

    [Tooltip("Widens the line-of-fire check so shots that graze along the hull count as blocked too.")]
    public float selfCheckRadius = 0.3f;

    [Header("Bake")]
    [Tooltip("Grid resolution in degrees. Smaller is more faithful around the edges of the cockpit and costs bake time quadratically.")]
    public float bakeResolution = 3f;

    [Tooltip("How deep the exposed gun may sink into the hull before the orientation counts as blocked, in world units. This is the knob for 'a bit of overlap is fine, through the canopy is not'.")]
    public float allowedPenetration = 0.1f;

    [Tooltip("Hull colliders to ignore entirely, for housings the gun is meant to sit inside.")]
    public List<Collider> ignoredHullColliders = new List<Collider>();

    [Tooltip("Spread the bake over several frames instead of stalling on the first one. Worth leaving on if ships get reassembled mid-flight.")]
    public bool bakeAcrossFrames = true;

    [Tooltip("Milliseconds of bake work per frame when spreading it out.")]
    public float bakeBudgetMs = 2f;

    [Header("Debug")]
    [Tooltip("Draws the baked mask around the turret when it is selected: green reachable and clear to fire, yellow reachable but no line of fire, red blocked.")]
    public bool drawMaskGizmo = true;

    public bool IsBaked { get; private set; }

    // Rest pose of the joints, captured before anything rotates them. Every orientation the bake
    // considers is expressed as an offset from here, so the rig can be authored at any angle.
    private Vector3 yawLocalPosition, yawLocalScale;
    private Quaternion yawRestRotation;
    private Vector3 pitchLocalPosition, pitchLocalScale;
    private Quaternion pitchRestRotation;
    private Matrix4x4 clearanceFromPitch;
    private Matrix4x4 boreFromPitch;

    // The rest bore and the basis we resolve yaw/pitch in, in this transform's space.
    private Vector3 restBore, restRight, restUp;

    private TurretClearanceMask mask;
    private Collider[] hullColliders;
    private readonly RaycastHit[] selfHits = new RaycastHit[8];

    private float currentYaw, currentPitch;
    private float desiredYaw, desiredPitch;

    // Where the target actually is, before limits and the clearance mask get a say. The gun is only
    // on target when it is lined up with *this* -- if the mask pushed the aim somewhere else, the
    // turret is pointing at the nearest legal direction, which is not the same as having a shot.
    private float requestedYaw, requestedPitch;

    // Several muzzles can share one turret, and LaserWeapon.Aim() calls through once per muzzle.
    // Without this the turret would take one traverse step per muzzle and slew N times too fast.
    private int lastAimFrame = -1;

    private void Awake()
    {
        ResolveRig();
        if (!ValidateRig()) return;

        yawLocalPosition = yawJoint.localPosition;
        yawLocalScale = yawJoint.localScale;
        yawRestRotation = yawJoint.localRotation;

        pitchLocalPosition = pitchJoint.localPosition;
        pitchLocalScale = pitchJoint.localScale;
        pitchRestRotation = pitchJoint.localRotation;

        clearanceFromPitch = pitchJoint.worldToLocalMatrix * clearanceCollider.transform.localToWorldMatrix;
        boreFromPitch = pitchJoint.worldToLocalMatrix * bore.localToWorldMatrix;

        // The clearance volume is a measuring tool, not a physical part of the ship. As a trigger it
        // stays valid for ComputePenetration while dropping out of real collisions and every query
        // that passes QueryTriggerInteraction.Ignore -- including the weapon's own hit raycast.
        clearanceCollider.isTrigger = true;

        BuildRestBasis();
    }

    private void Start()
    {
        if (!IsRigValid()) return;

        if (bakeAcrossFrames) StartCoroutine(BakeRoutine());
        else RunBakeSynchronously();
    }

    private void ResolveRig()
    {
        if (yawJoint == null) yawJoint = FindChildNamed(transform, "Yaw");
        if (pitchJoint == null && yawJoint != null) pitchJoint = FindChildNamed(yawJoint, "Pitch");

        if (clearanceCollider == null)
        {
            Transform found = FindChildNamed(transform, "ClearanceCollider");
            if (found != null) clearanceCollider = found.GetComponent<Collider>();
        }

        if (bore == null && pitchJoint != null)
        {
            foreach (Transform child in pitchJoint.GetComponentsInChildren<Transform>())
            {
                if (child.CompareTag("Muzzle")) { bore = child; break; }
            }
        }
    }

    private static Transform FindChildNamed(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != root && child.name.Trim() == childName) return child;
        }
        return null;
    }

    private bool IsRigValid() => yawJoint != null && pitchJoint != null && bore != null && clearanceCollider != null;

    private bool ValidateRig()
    {
        if (IsRigValid()) return true;

        Debug.LogWarning($"<color=red>[Turret Error]</color> Turret '{gameObject.name}' is missing part of its rig " +
            $"(yaw: {yawJoint != null}, pitch: {pitchJoint != null}, bore: {bore != null}, clearance: {clearanceCollider != null}). " +
            "Assign them in the inspector; the gun will not track until you do.");
        return false;
    }

    // Yaw and pitch are solved in a basis built from where the bore points at rest, so the rig does
    // not have to be authored with the barrel down any particular world axis.
    private void BuildRestBasis()
    {
        restBore = transform.InverseTransformDirection(bore.up).normalized;

        Vector3 worldYawAxis = yawJoint.TransformDirection(yawAxis);
        Vector3 localYawAxis = transform.InverseTransformDirection(worldYawAxis);

        restUp = Vector3.ProjectOnPlane(localYawAxis, restBore);
        restUp = restUp.sqrMagnitude < 0.001f ? Vector3.up : restUp.normalized;

        restRight = Vector3.Cross(restUp, restBore).normalized;
    }

    // ------------------------------------------------------------------ aiming

    // Called every frame by the weapon whether or not the trigger is down -- tracking is continuous
    // and firing is gated separately, which is what lets the gun swivel while it fires.
    public void Track(Vector3 targetPoint)
    {
        if (!IsRigValid()) return;

        // One traverse step per frame however many muzzles ask for it.
        if (lastAimFrame == Time.frameCount) return;
        lastAimFrame = Time.frameCount;

        SolveAngles(targetPoint, out requestedYaw, out requestedPitch);

        desiredYaw = Mathf.Clamp(requestedYaw, yawMin, yawMax);
        desiredPitch = Mathf.Clamp(requestedPitch, pitchMin, pitchMax);

        // The mask is the interesting constraint: it knows the barrel cannot go *there* specifically,
        // rather than just within a rectangle of angles.
        if (mask != null) mask.ClampToClear(ref desiredYaw, ref desiredPitch);

        float step = traverseSpeed * Time.deltaTime;
        currentYaw = Mathf.MoveTowardsAngle(currentYaw, desiredYaw, step);
        currentPitch = Mathf.MoveTowards(currentPitch, desiredPitch, step);

        ApplyAngles(currentYaw, currentPitch);
    }

    private void SolveAngles(Vector3 targetPoint, out float yaw, out float pitch)
    {
        Vector3 local = transform.InverseTransformDirection((targetPoint - bore.position).normalized);

        float x = Vector3.Dot(local, restRight);
        float y = Vector3.Dot(local, restUp);
        float z = Vector3.Dot(local, restBore);

        yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(Mathf.Clamp(y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private void ApplyAngles(float yaw, float pitch)
    {
        yawJoint.localRotation = yawRestRotation * Quaternion.AngleAxis(yaw, yawAxis);
        pitchJoint.localRotation = pitchRestRotation * Quaternion.AngleAxis(-pitch, pitchAxis);
    }

    // ------------------------------------------------------- the firing solution

    // True when the gun is pointed close enough to where it was asked to point, and shooting from
    // there does not put a beam through our own hull.
    public bool HasFiringSolution()
    {
        if (!IsRigValid()) return false;

        // Compared against the requested angles, not the clamped ones. This covers both cases at
        // once: the turret is still slewing onto the target, or it has arrived at the nearest legal
        // orientation and the target is somewhere it is not allowed to point. Either way tracking
        // continues and the trigger simply does not connect.
        if (Mathf.Abs(Mathf.DeltaAngle(currentYaw, requestedYaw)) > onTargetCone) return false;
        if (Mathf.Abs(currentPitch - requestedPitch) > onTargetCone) return false;

        // The baked answer is free, but it only knows about the hull as it stood at bake time, and
        // an unfinished bake knows nothing at all. It gates the common case; the live check below is
        // what actually guarantees we never fire through ourselves.
        if (mask != null && !mask.HasLineOfFire(mask.IndexOf(currentYaw, currentPitch))) return false;

        return IsLineOfFireClear(bore.position, bore.up);
    }

    // Fired along the bore as it points right now, not as it was asked to point -- aiming lags, and
    // what matters is where the barrel actually is at the instant of the shot.
    private bool IsLineOfFireClear(Vector3 origin, Vector3 direction)
    {
        int count = Physics.SphereCastNonAlloc(origin, selfCheckRadius, direction, selfHits,
            selfCheckRange, ~0, QueryTriggerInteraction.Ignore);

        Transform shipRoot = transform.root;
        for (int i = 0; i < count; i++)
        {
            Collider hit = selfHits[i].collider;
            if (hit == null || hit.transform.root != shipRoot) continue;   // not us
            if (hit.transform.IsChildOf(transform)) continue;              // our own gun
            if (ignoredHullColliders.Contains(hit)) continue;              // our housing
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------- baking

    // Rebuild the mask -- call this after the ship is reassembled, since swapping a wing or a cockpit
    // changes what the barrel can hit.
    public void Rebake()
    {
        StopAllCoroutines();
        IsBaked = false;

        if (!IsRigValid()) return;

        if (bakeAcrossFrames) StartCoroutine(BakeRoutine());
        else RunBakeSynchronously();
    }

    private void RunBakeSynchronously()
    {
        IEnumerator bake = Bake();
        while (bake.MoveNext()) { }
    }

    private IEnumerator BakeRoutine()
    {
        System.Diagnostics.Stopwatch frameTimer = System.Diagnostics.Stopwatch.StartNew();
        IEnumerator bake = Bake();

        while (bake.MoveNext())
        {
            if (frameTimer.Elapsed.TotalMilliseconds < bakeBudgetMs) continue;

            yield return null;
            frameTimer.Restart();
        }
    }

    // Written as an iterator so the same code can run in one go or be time-sliced across frames: it
    // yields once per cell, and the caller decides when to hand the frame back.
    private IEnumerator Bake()
    {
        System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();

        TurretClearanceMask baking = new TurretClearanceMask(yawMin, yawMax, pitchMin, pitchMax, bakeResolution);
        GatherHullColliders();

        for (int cell = 0; cell < baking.CellCount; cell++)
        {
            baking.AnglesAt(cell, out float yaw, out float pitch);
            Matrix4x4 pitchMatrix = PitchMatrixFor(yaw, pitch);

            bool fits = GunFits(pitchMatrix);

            // No point asking whether a shot from an orientation the gun cannot reach is clear.
            bool canShoot = false;
            if (fits)
            {
                Matrix4x4 boreMatrix = pitchMatrix * boreFromPitch;
                canShoot = IsLineOfFireClear(boreMatrix.GetColumn(3), boreMatrix.rotation * Vector3.up);
            }

            baking.SetCell(cell, fits, canShoot);
            yield return null;
        }

        baking.BuildNearestClear();

        mask = baking;
        IsBaked = true;

        Debug.Log($"<color=cyan>[Turret Bake]</color> Turret <b>{gameObject.name}</b> baked {baking.CellCount} orientations " +
            $"against {hullColliders.Length} hull colliders in {total.Elapsed.TotalMilliseconds:F1} ms.");
    }

    // Where the pitch joint would sit for a candidate orientation, without touching the live rig.
    private Matrix4x4 PitchMatrixFor(float yaw, float pitch)
    {
        Matrix4x4 yawMatrix = transform.localToWorldMatrix * Matrix4x4.TRS(
            yawLocalPosition, yawRestRotation * Quaternion.AngleAxis(yaw, yawAxis), yawLocalScale);

        return yawMatrix * Matrix4x4.TRS(
            pitchLocalPosition, pitchRestRotation * Quaternion.AngleAxis(-pitch, pitchAxis), pitchLocalScale);
    }

    // ComputePenetration reports how deep two shapes overlap, in world units, which is exactly the
    // "is this overlap egregious, or is it just the gun sitting in its mount" question -- a barrel
    // seated a few centimetres into its housing passes, a barrel through the canopy does not.
    private bool GunFits(Matrix4x4 pitchMatrix)
    {
        Matrix4x4 clearanceMatrix = pitchMatrix * clearanceFromPitch;
        Vector3 position = clearanceMatrix.GetColumn(3);
        Quaternion rotation = clearanceMatrix.rotation;

        for (int i = 0; i < hullColliders.Length; i++)
        {
            Collider hull = hullColliders[i];
            if (hull == null) continue;

            if (Physics.ComputePenetration(
                    clearanceCollider, position, rotation,
                    hull, hull.transform.position, hull.transform.rotation,
                    out _, out float depth) && depth > allowedPenetration)
            {
                return false;
            }
        }

        return true;
    }

    // Everything on this ship the barrel could plausibly reach. The reach prefilter matters: without
    // it every cell tests every collider on the ship, and the bake goes from milliseconds to seconds.
    private void GatherHullColliders()
    {
        float reach = Vector3.Distance(pitchJoint.position, clearanceCollider.bounds.center)
                      + clearanceCollider.bounds.extents.magnitude;

        List<Collider> candidates = new List<Collider>();
        foreach (Collider candidate in transform.root.GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;
            if (candidate.transform.IsChildOf(transform)) continue;     // our own gun
            if (ignoredHullColliders.Contains(candidate)) continue;     // our housing

            Vector3 nearest = candidate.bounds.ClosestPoint(pitchJoint.position);
            if ((nearest - pitchJoint.position).sqrMagnitude > reach * reach) continue;

            candidates.Add(candidate);
        }

        hullColliders = candidates.ToArray();
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmosSelected()
    {
        if (!drawMaskGizmo || mask == null || bore == null) return;

        float radius = Mathf.Max(selfCheckRange * 0.05f, 1f);

        for (int cell = 0; cell < mask.CellCount; cell++)
        {
            mask.AnglesAt(cell, out float yaw, out float pitch);

            Matrix4x4 boreMatrix = PitchMatrixFor(yaw, pitch) * boreFromPitch;
            Vector3 direction = boreMatrix.rotation * Vector3.up;

            Gizmos.color = !mask.IsClear(cell) ? Color.red
                         : !mask.HasLineOfFire(cell) ? Color.yellow
                         : Color.green;

            Gizmos.DrawRay(bore.position + direction * radius, direction * (radius * 0.25f));
        }
    }
}
