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

    public bool IsOccupied => occupant != null;

    // Which side of the ship this socket is really on.
    //
    // A socket that names no side is not necessarily central: "PrimaryWeaponHardPoint" inside
    // "Wing_Var3_L" is on the left wing, and offering it a right hand gun would be wrong. So when
    // the name is silent, the answer is inherited from whatever it is bolted to - walking out
    // through the part that owns this socket, the socket that part sits in, and so on until a name
    // does declare a side. The nearest declaration wins, and a core reaching the stand ends the
    // walk with no side at all.
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

            return PartSide.None;
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
