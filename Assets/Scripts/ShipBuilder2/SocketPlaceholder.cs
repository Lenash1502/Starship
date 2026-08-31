using System.Collections.Generic;
using UnityEngine;

// The blockout model the artist parked inside a hard point to show what belongs there.
//
// Every "<Category>HardPoint" in the part library carries one instance of the matching generic
// module from Assets/Prefabs/ShipModules, so a bare core already reads as a ship: you can see where
// the wings, tails and thrusters are meant to go before choosing any of them. That makes them
// scenery, not structure - they must never register their own hard points, never count as something
// a new part could clash with, and never end up in a part thumbnail.
//
// They are worth clicking, though: a hologram is the clearest statement of "a wing goes here", so
// clicking one opens that category in the list. That is why the colliders are kept alive as
// triggers rather than switched off - triggers are ignored by the picking, occlusion and overlap
// queries, and answer only the one raycast that goes looking for them.
//
// This component is added at runtime to the root of each blockout so all of the above can be
// recognised with a single GetComponentInParent.
public class SocketPlaceholder : MonoBehaviour
{
    // The socket this blockout is standing in for, so a click on it can name a category.
    public HardPoint socket;

    readonly List<Renderer> renderers = new List<Renderer>();
    readonly List<Collider> colliders = new List<Collider>();

    public void Capture(HardPoint owner)
    {
        socket = owner;

        renderers.Clear();
        GetComponentsInChildren(true, renderers);

        colliders.Clear();
        GetComponentsInChildren(true, colliders);

        // Solid, the blockout would take clicks meant for the hull, cast occlusion shadows over its
        // own socket marker, and make every real part look buried. As a trigger it is invisible to
        // all three and still clickable.
        foreach (Collider collider in colliders) collider.isTrigger = true;
    }

    // Meshes and colliders together: a blockout that has been replaced by the real part must not go
    // on quietly answering raycasts from inside it.
    public void SetVisible(bool visible)
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

        for (int i = colliders.Count - 1; i >= 0; i--)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                colliders.RemoveAt(i);
                continue;
            }
            collider.enabled = visible;
        }
    }

    public void ApplyMaterial(Material material)
    {
        if (material == null) return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            var applied = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < applied.Length; i++) applied[i] = material;
            renderer.sharedMaterials = applied;
        }
    }

    // Switches every blockout inside a loose prefab instance off - meshes and colliders both -
    // without any of the runtime bookkeeping above.
    //
    // Used wherever a prefab is instantiated for something other than building: the thumbnail rig,
    // where a wing photographed with its ghost guns attached would not look like the wing being
    // offered, and the overlap probe, where counting the blockouts as part of the candidate's own
    // volume would report every socket on the ship as a clash.
    public static void SuppressAllIn(GameObject root)
    {
        if (root == null) return;

        foreach (Transform child in root.transform)
        {
            SuppressBelow(child);
        }
    }

    static void SuppressBelow(Transform node)
    {
        if (PartNaming.IsHardPointName(node.name))
        {
            // Everything hanging off a hard point is blockout, including the hard points the
            // blockout itself declares - so the walk stops here.
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in node.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return;
        }

        foreach (Transform child in node)
        {
            SuppressBelow(child);
        }
    }
}
