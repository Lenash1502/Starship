using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LaserBeamVisual : MonoBehaviour
{
    [Tooltip("How long the laser beam stays on screen before disappearing.")]
    public float duration = 0.05f;

    private LineRenderer lineRenderer;
    private float spawnTime;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        spawnTime = Time.time;

        // FAILSAFE: Force Unity to destroy this object after 'duration' no matter what happens
        Destroy(gameObject, duration);
    }

    public void SetBeam(Vector3 startPoint, Vector3 endPoint)
    {
        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, endPoint);
    }

    void Update()
    {
        float alpha = 1f - ((Time.time - spawnTime) / duration);
        if (alpha <= 0f) return;

        Color color = lineRenderer.startColor;
        color.a = alpha;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }
}