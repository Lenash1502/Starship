using UnityEngine;

// Keeps a camera pointed at whatever the builder wants looked at, pulled back far enough to hold it
// all in view.
//
// One code path serves both jobs: framing the whole ship is framing the assembly root, and zooming
// in on a module is framing that module. The centre is given in the anchor's local space so the
// camera keeps tracking it while the ship is rotated.
public class BuilderCamera : MonoBehaviour
{
    [Header("Camera")]
    public Camera view;

    [Header("Framing")]
    [Tooltip("Room left around the subject. 1 is a tight fit against the edges of the view.")]
    public float framingPadding = 1.15f;
    [Tooltip("Seconds the camera takes to settle on a new distance. 0 snaps straight there.")]
    public float smoothTime = 0.25f;
    [Tooltip("Fraction of the screen width covered by the panel, so the subject centres in what is " +
             "left of the view. The UI keeps this up to date on its own.")]
    [Range(0f, 0.6f)] public float uiPanelFraction;

    [Header("Zoom")]
    [Tooltip("How far one notch of the scroll wheel moves the camera, as a proportion of distance.")]
    public float zoomSensitivity = 0.2f;
    public float minZoom = 0.2f;
    public float maxZoom = 3f;

    Transform anchor;
    Vector3 localCenter;
    float radius;
    float zoom = 1f;

    // Captured once: the camera never turns, it only slides along the direction it was set up with.
    Vector3 viewDirection = Vector3.forward;
    Vector3 targetPosition;
    Vector3 velocity;
    bool hasTarget;

    public bool HasSubject => anchor != null && radius > 0f;

    void Awake()
    {
        if (view == null) view = Camera.main;
        if (view != null) viewDirection = view.transform.forward;
    }

    // Point the camera at something. localCenter is relative to anchor, so a module that swings
    // round with the ship stays framed.
    public void Frame(Transform subjectAnchor, Vector3 subjectLocalCenter, float subjectRadius, bool resetZoom)
    {
        anchor = subjectAnchor;
        localCenter = subjectLocalCenter;
        radius = subjectRadius;

        if (resetZoom) zoom = 1f;

        UpdateTarget();
    }

    public void Zoom(float scrollDelta)
    {
        // Wheels report 120 per notch on Windows and roughly 1 elsewhere; normalise to notches so
        // the sensitivity setting means the same thing on both.
        float notches = Mathf.Abs(scrollDelta) >= 20f ? scrollDelta / 120f : scrollDelta;

        // Exponential, so every notch changes the distance by the same proportion.
        zoom = Mathf.Clamp(zoom * Mathf.Exp(-notches * zoomSensitivity), minZoom, maxZoom);
        UpdateTarget();
    }

    void LateUpdate()
    {
        // The subject moves with the ship, so where the camera wants to be is re-derived each frame.
        UpdateTarget();

        if (!hasTarget || view == null || smoothTime <= 0f) return;

        view.transform.position = Vector3.SmoothDamp(view.transform.position, targetPosition, ref velocity, smoothTime);
    }

    void UpdateTarget()
    {
        if (view == null || anchor == null || radius <= 0f)
        {
            hasTarget = false;
            return;
        }

        Vector3 center = anchor.TransformPoint(localCenter);

        // The panel hides the right hand slice of the screen, so the subject has a narrower cone to
        // fit inside than the camera's full field of view suggests.
        float usableWidth = Mathf.Clamp(1f - uiPanelFraction, 0.2f, 1f);

        float distance;
        float halfWidthAtDistance;

        if (view.orthographic)
        {
            view.orthographicSize = radius * framingPadding * zoom / usableWidth;
            halfWidthAtDistance = view.orthographicSize * view.aspect;
            distance = radius * 2f + view.nearClipPlane;
        }
        else
        {
            float halfVertical = view.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * view.aspect);
            float halfUsable = Mathf.Atan(Mathf.Tan(halfHorizontal) * usableWidth);

            // Distance at which a sphere of this radius fits inside the cone, taken for both axes.
            distance = Mathf.Max(radius / Mathf.Sin(halfVertical), radius / Mathf.Sin(halfUsable));
            distance *= framingPadding * zoom;

            // Zooming right in on a small fin must not push the camera inside the near plane.
            distance = Mathf.Max(distance, view.nearClipPlane + radius * 1.05f);
            halfWidthAtDistance = Mathf.Tan(halfHorizontal) * distance;
        }

        Vector3 sideStep = view.transform.right * (halfWidthAtDistance * uiPanelFraction);
        targetPosition = center - viewDirection * distance + sideStep;
        hasTarget = true;

        if (smoothTime <= 0f)
        {
            view.transform.position = targetPosition;
            velocity = Vector3.zero;
        }

        view.farClipPlane = Mathf.Max(view.farClipPlane, distance + radius * 3f);
    }
}
