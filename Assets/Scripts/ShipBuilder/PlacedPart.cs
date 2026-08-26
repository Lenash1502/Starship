using System.Collections.Generic;
using UnityEngine;

// Bookkeeping for a part instance that currently sits on the build stand. Added at runtime when the
// builder spawns a prefab, so the assembly can be walked, costed and torn down again.
public class PlacedPart : MonoBehaviour
{
    public ShipPartDefinition definition;

    // Null for the core, which sits on the selection circle rather than in a socket.
    public HardPoint attachedTo;

    public readonly List<HardPoint> hardPoints = new List<HardPoint>();

    // Captured before hard point markers are spawned, so hiding a part for a preview never hides
    // the little sockets floating around it.
    public readonly List<Renderer> renderers = new List<Renderer>();

    public void CaptureRenderers()
    {
        renderers.Clear();
        GetComponentsInChildren(true, renderers);
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
