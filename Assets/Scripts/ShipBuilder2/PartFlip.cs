using UnityEngine;

// A half turn applied to a part after it is installed, to rescue a socket whose transform points the
// wrong way. Some mounts were authored facing forward where the part wants to face aft, or upside
// down, and the fix belongs on the placed part rather than in the prefab - the same socket is right
// for other configurations.
//
// Turn is a half turn about up, which is the one that fixes an engine or a gun facing backwards.
// Tilt is about the part's right, Roll about its forward.
public enum PartFlip
{
    None,
    Turn,
    Tilt,
    Roll,
}

public static class PartFlips
{
    public static Quaternion ToRotation(PartFlip flip)
    {
        switch (flip)
        {
            case PartFlip.Turn: return Quaternion.Euler(0f, 180f, 0f);
            case PartFlip.Tilt: return Quaternion.Euler(180f, 0f, 0f);
            case PartFlip.Roll: return Quaternion.Euler(0f, 0f, 180f);
            default: return Quaternion.identity;
        }
    }

    // Half turns about the three axes are closed under composition: doing the same one twice comes
    // back to square one, and any two different ones land on the third. So four orientations is not
    // a shortlist, it is all of them - which is why the state is one value rather than three
    // independent switches that would have to explain why two of them look like the third.
    public static PartFlip Compose(PartFlip current, PartFlip half)
    {
        if (half == PartFlip.None) return current;
        if (current == PartFlip.None) return half;
        if (current == half) return PartFlip.None;

        // The remaining one of the three.
        if (current != PartFlip.Turn && half != PartFlip.Turn) return PartFlip.Turn;
        if (current != PartFlip.Tilt && half != PartFlip.Tilt) return PartFlip.Tilt;
        return PartFlip.Roll;
    }

    public static string Describe(PartFlip flip)
    {
        switch (flip)
        {
            case PartFlip.Turn: return "turned";
            case PartFlip.Tilt: return "tilted";
            case PartFlip.Roll: return "rolled";
            default: return "as placed";
        }
    }
}
