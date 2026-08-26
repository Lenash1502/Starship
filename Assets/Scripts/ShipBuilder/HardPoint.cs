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
