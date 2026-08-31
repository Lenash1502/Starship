using System.Collections.Generic;
using UnityEngine;

// Bookkeeping for a part instance that currently sits on the build stand. Added at runtime when the
// builder spawns a prefab, so the assembly can be walked, costed and torn down again.
public class PlacedPart : MonoBehaviour
{
    public ShipPartDefinition definition;

    // The model the definition was resolved from. Kept so the part can be rebuilt as its own mirror
    // image without going back to the list to ask which model this was.
    public ShipPartFamily family;

    // Half turn the player asked for on top of however the part was seated.
    public PartFlip flip;

    // True when the builder straightened this part on placement because its mount faced against the
    // hull. Recorded rather than baked in anywhere: the socket and the prefab behind it are left
    // exactly as the artist made them, and only this instance is turned.
    public bool autoOriented;

    // True when the player forced the half of the model opposite to the one the socket's side asked
    // for. Remembered so that rebuilding this part - which happens when its parent is mirrored -
    // does not quietly undo the correction.
    public bool handSwapped;

    // Null for the core, which sits on the selection circle rather than in a socket.
    public HardPoint attachedTo;

    public readonly List<HardPoint> hardPoints = new List<HardPoint>();

    // This part's own meshes: not the socket markers floating around it, and not the blockout
    // models sitting in its empty sockets - hiding or framing a wing should not drag either along.
    public readonly List<Renderer> renderers = new List<Renderer>();

    public void CaptureRenderers()
    {
        renderers.Clear();
        GetComponentsInChildren(true, renderers);

        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            Renderer renderer = renderers[i];
            if (renderer == null
                || renderer.GetComponentInParent<SocketPlaceholder>() != null
                || renderer.GetComponentInParent<HardPointMarker>() != null)
            {
                renderers.RemoveAt(i);
            }
        }
    }

    public void SetRenderersVisible(bool visible)
    {
        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                renderers.RemoveAt(i);
                continue;
            }
            renderer.enabled = visible;
        }

        // Anything bolted onto this part hides with it, otherwise a wing preview would leave its
        // guns hanging in mid air.
        foreach (HardPoint hardPoint in hardPoints)
        {
            if (hardPoint != null && hardPoint.occupant != null) hardPoint.occupant.SetRenderersVisible(visible);
        }
    }

    public float Weight
    {
        get
        {
            ShipPart stats = GetComponent<ShipPart>();
            return stats != null ? stats.weight : 0f;
        }
    }
}
