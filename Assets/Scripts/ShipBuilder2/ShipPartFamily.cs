using UnityEngine;

// One model in the part list, with its mirrored variants folded together.
//
// Aegis_Wing_L and Aegis_Wing_R are one wing, not two: offering both as separate cells asks the
// player to answer a question the ship has already answered, since a socket on the left shoulder
// can only ever take the left model. So the list shows "Aegis" once and the side is resolved from
// whichever socket it is dropped onto.
//
// Built at startup from the flat catalog by grouping on category and model name, which is why the
// catalog asset itself stays a plain list - the grouping is a presentation choice, not data.
public class ShipPartFamily
{
    public string category;
    public string modelName;
    public string displayName;

    // At most one of each. A mirrored model fills left and right; an unsuffixed one such as
    // Apex_Engine fills centred and is the answer for any socket.
    public ShipPartDefinition left;
    public ShipPartDefinition right;
    public ShipPartDefinition centred;

    public bool IsMirrored => left != null && right != null;

    // What the cell shows. The centred model if there is one, otherwise either half of the pair -
    // the two are mirror images, so the icon reads the same whichever is photographed.
    public GameObject IconPrefab
    {
        get
        {
            ShipPartDefinition icon = centred ?? left ?? right;
            return icon != null ? icon.prefab : null;
        }
    }

    public void Add(ShipPartDefinition definition)
    {
        if (definition == null || !definition.IsValid) return;

        switch (definition.side)
        {
            case PartSide.Left:
                if (left == null) left = definition;
                break;

            case PartSide.Right:
                if (right == null) right = definition;
                break;

            default:
                if (centred == null) centred = definition;
                break;
        }
    }

    // Which prefab this model contributes to a socket on the given side, or null if it has nothing
    // that belongs there - a pair with only a left model has no answer for a right hand socket.
    //
    // A socket that names no side takes the centred model if there is one; failing that it takes the
    // left, arbitrarily but consistently, so the same mount always ends up with the same part rather
    // than flipping between runs.
    public ShipPartDefinition VariantFor(PartSide socketSide)
    {
        switch (socketSide)
        {
            case PartSide.Left:
                return left ?? centred;

            case PartSide.Right:
                return right ?? centred;

            default:
                return centred ?? left ?? right;
        }
    }

    public bool Fits(PartSide socketSide)
    {
        return VariantFor(socketSide) != null;
    }

    // The other half of the pair, or null for a model that has no other half.
    //
    // This is the escape hatch for a socket whose side the builder read wrong: the mounts that name
    // no side are placed by where they sit, and where that guess is off the answer is to force the
    // opposite hand rather than to argue with the heuristic.
    public ShipPartDefinition Opposite(ShipPartDefinition definition)
    {
        if (definition == null) return null;
        if (definition == left) return right;
        if (definition == right) return left;
        return null;
    }
}
