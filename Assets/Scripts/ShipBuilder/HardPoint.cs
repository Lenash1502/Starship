using System.Collections.Generic;
using UnityEngine;

// Runtime marker component added by ShipBuilder to every "<Category>HardPoint<suffix>" transform it
// finds inside a placed part. The prefabs themselves stay script-free; all of this is attached to
// the instances in the build scene.
public class HardPoint : MonoBehaviour
{
    public string category;
    public PartSide side;
    public string suffix;

    // The part this socket belongs to, and the part currently plugged into it (if any).
    public PlacedPart owner;
    public PlacedPart occupant;

    [HideInInspector] public HardPointMarker marker;

    // The blockout models the artist parked in this socket to show what belongs here. Shown while
    // the socket is empty and hidden the moment a real part takes its place.
    public readonly List<SocketPlaceholder> placeholders = new List<SocketPlaceholder>();

    public bool IsOccupied => occupant != null;

    public void SetPlaceholdersVisible(bool visible)
    {
        foreach (SocketPlaceholder placeholder in placeholders)
        {
            if (placeholder != null) placeholder.SetVisible(visible);
        }
    }

    // Which side this socket sits on judging by where it is, rather than by what it is called.
    // Filled in once when the part is registered; PartSide.None means it is on the centreline.
    [HideInInspector] public PartSide geometricSide;

    // Which side of the ship this socket is really on, settled in three steps.
    //
    // The name comes first. Failing that the answer is inherited from whatever the socket is bolted
    // to - "PrimaryWeaponHardPoint" inside "Aegis_Wing_L" is on the left wing, and offering it a
    // right hand gun would be wrong - so the walk goes out through the part that owns this socket,
    // the socket that part sits in, and so on until a name does declare a side. The nearest
    // declaration wins.
    //
    // When nothing along that chain says anything, which is the case for the unsuffixed mounts
    // scattered down both flanks of most hulls, the position of the socket decides. Without that
    // last step every one of those mounts would quietly take the same half of a mirrored model, and
    // a hull would come out wearing left wings on both shoulders.
    public PartSide EffectiveSide
    {
        get
        {
            if (side != PartSide.None) return side;

            PlacedPart part = owner;
            while (part != null)
            {
                if (part.definition != null && part.definition.side != PartSide.None) return part.definition.side;

                HardPoint mount = part.attachedTo;
                if (mount == null) break;
                if (mount.side != PartSide.None) return mount.side;

                part = mount.owner;
            }

            return geometricSide;
        }
    }

    public string DisplayName
    {
        get
        {
            string pretty = PartNaming.PrettyCategory(category);
            string tail = PartNaming.PrettySuffix(suffix);
            return string.IsNullOrEmpty(tail) ? pretty : pretty + " (" + tail + ")";
        }
    }
}
