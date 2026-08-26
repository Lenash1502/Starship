using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Which side of the hull a part or a hard point belongs to. Parsed from the "_L" / "_R" suffix
// the source models already use (Wing_Var3_L, EngineHardPoint_R, ...).
public enum PartSide
{
    None,
    Left,
    Right
}

// The ship part library has no metadata assets behind it - every relationship is encoded in the
// object names. A hard point is an empty child called "<Category>HardPoint<suffix>" and a part
// prefab is called "<Category>_Var<n>[_L|_R]". This class is the single place that knows how to
// read those names, so a rename convention change only has to be handled here.
public static class PartNaming
{
    public const string HardPointToken = "HardPoint";
    public const string CoreCategory = "Core";

    // Categories that appear on hard points but not on any prefab folder name, either because the
    // model author made a typo or because the hard point is more specific than the part that fits it.
    static readonly Dictionary<string, string> CategoryAliases = new Dictionary<string, string>
    {
        { "Thurster", "Thruster" },
        { "Weapon", "PrimaryWeapon" },
        { "PrimaryWeaponTurret", "PrimaryWeapon" },
        { "SpecialWeaponTurret", "SpecialWeapon" },
    };

    public static string NormalizeCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return category;
        return CategoryAliases.TryGetValue(category, out string mapped) ? mapped : category;
    }

    // "EngineHardPoint_R" -> category "Engine", side Right, suffix "_R".
    // "SpecialWeaponTurretHardPoint_2-2" -> category "SpecialWeapon", side None, suffix "_2-2".
    public static bool TryParseHardPoint(string objectName, out string category, out PartSide side, out string suffix)
    {
        category = null;
        suffix = null;
        side = PartSide.None;

        if (string.IsNullOrEmpty(objectName)) return false;

        int index = objectName.IndexOf(HardPointToken, System.StringComparison.Ordinal);
        if (index <= 0) return false;

        category = NormalizeCategory(objectName.Substring(0, index));
        suffix = objectName.Substring(index + HardPointToken.Length);
        side = SideFromSuffix(suffix);
        return true;
    }

    public static bool IsHardPointName(string objectName)
    {
        return TryParseHardPoint(objectName, out _, out _, out _);
    }

    // "Wing_Var10_L" -> category "Wing", variant 10, side Left. A bare "Booster" is variant 0.
    public static void ParsePart(string prefabName, out string category, out int variant, out PartSide side)
    {
        category = prefabName;
        variant = 0;
        side = PartSide.None;

        string[] tokens = prefabName.Split('_');
        category = NormalizeCategory(tokens[0]);

        for (int i = 1; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token.StartsWith("Var", System.StringComparison.Ordinal))
            {
                int.TryParse(token.Substring(3), out variant);
            }
            else if (token == "L")
            {
                side = PartSide.Left;
            }
            else if (token == "R")
            {
                side = PartSide.Right;
            }
        }
    }

    static PartSide SideFromSuffix(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return PartSide.None;

        foreach (string token in suffix.Split('_', '-'))
        {
            if (token == "L") return PartSide.Left;
            if (token == "R") return PartSide.Right;
        }
        return PartSide.None;
    }

    // A part fits a hard point when neither of them insists on a side the other contradicts.
    // A centred hard point ("WingHardPoint") accepts left and right models alike, and a sideless
    // model ("Engine_Var4") drops onto either shoulder.
    public static bool SidesMatch(PartSide partSide, PartSide hardPointSide)
    {
        if (partSide == PartSide.None || hardPointSide == PartSide.None) return true;
        return partSide == hardPointSide;
    }

    // "SecondaryWeapon" -> "Secondary Weapon".
    public static string PrettyCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return string.Empty;

        var builder = new StringBuilder(category.Length + 4);
        for (int i = 0; i < category.Length; i++)
        {
            if (i > 0 && char.IsUpper(category[i]) && !char.IsUpper(category[i - 1])) builder.Append(' ');
            builder.Append(category[i]);
        }
        return builder.ToString();
    }

    // "_L_2" -> "Left 2", "_Up" -> "Up", "" -> "".
    public static string PrettySuffix(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return string.Empty;

        // Split on underscores only: a dashed token like "1-4" is a turret index ("gun 1 of 4")
        // and reads wrong once the dash is turned into a space.
        var parts = new List<string>();
        foreach (string token in suffix.Split('_'))
        {
            if (string.IsNullOrEmpty(token)) continue;
            if (token == "L") parts.Add("Left");
            else if (token == "R") parts.Add("Right");
            else parts.Add(token);
        }
        return string.Join(" ", parts);
    }

    public static string PrettyPartName(string category, int variant, PartSide side)
    {
        var builder = new StringBuilder(PrettyCategory(category));
        if (variant > 0) builder.Append(' ').Append(variant);
        if (side == PartSide.Left) builder.Append(" L");
        else if (side == PartSide.Right) builder.Append(" R");
        return builder.ToString();
    }
}
