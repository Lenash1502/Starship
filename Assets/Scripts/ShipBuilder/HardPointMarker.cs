using UnityEngine;

// The clickable circle drawn at a hard point. Kept as its own component so the builder raycast can
// tell "the player clicked a socket" apart from "the player grabbed the hull to spin it".
[RequireComponent(typeof(MeshRenderer))]
public class HardPointMarker : MonoBehaviour
{
    public HardPoint hardPoint;

    // Set by ShipBuilder when the hull is standing between this socket and the camera. Cached here
    // so the per frame occlusion sweep only touches markers whose state actually flipped.
    public bool Occluded { get; set; }

    MeshRenderer meshRenderer;
    MaterialPropertyBlock properties;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        properties = new MaterialPropertyBlock();
    }

    // A solid disc marks a socket that is still free; a hollow ring marks one that already holds a
    // part, so a finished mount reads as an outline rather than a blob sitting on the model.
    public void SetStyle(Color color, bool filled)
    {
        SetStyle(color, filled, Color.clear, 0f);
    }

    // outlineWidth is a fraction of the radius; 0 draws no outline at all.
    public void SetStyle(Color color, bool filled, Color outlineColor, float outlineWidth)
    {
        if (meshRenderer == null) Awake();

        meshRenderer.GetPropertyBlock(properties);
        properties.SetColor(ColorId, color);
        properties.SetFloat(FillId, filled ? 1f : 0f);
        properties.SetColor(OutlineColorId, outlineColor);
        properties.SetFloat(OutlineWidthId, outlineWidth);
        meshRenderer.SetPropertyBlock(properties);
    }

    public void SetVisible(bool visible)
    {
        if (meshRenderer == null) Awake();
        meshRenderer.enabled = visible;

        // Picking follows the visuals: a hidden socket must not be clickable either.
        Collider blocker = GetComponent<Collider>();
        if (blocker != null) blocker.enabled = visible;
    }
}
