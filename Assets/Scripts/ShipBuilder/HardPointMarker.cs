using UnityEngine;

// The clickable blob drawn at a hard point. Kept as its own component so the builder's raycast can
// tell "the player clicked a socket" apart from "the player grabbed the hull to spin it".
[RequireComponent(typeof(MeshRenderer))]
public class HardPointMarker : MonoBehaviour
{
    public HardPoint hardPoint;

    MeshRenderer meshRenderer;
    MaterialPropertyBlock properties;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        properties = new MaterialPropertyBlock();
    }

    public void SetColor(Color color)
    {
        if (meshRenderer == null) Awake();

        meshRenderer.GetPropertyBlock(properties);
        properties.SetColor(ColorId, color);
        properties.SetColor(BaseColorId, color);
        meshRenderer.SetPropertyBlock(properties);
    }

    public void SetVisible(bool visible)
    {
        if (meshRenderer == null) Awake();
        meshRenderer.enabled = visible;

        Collider blocker = GetComponent<Collider>();
        if (blocker != null) blocker.enabled = visible;
    }
}
