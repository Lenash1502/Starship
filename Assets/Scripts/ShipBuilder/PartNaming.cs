using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Which side of the hull a part or a hard point belongs to. Parsed from the "_L" / "_R" suffix
// the source models already use (Aegis_Wing_L, EngineHardPoint_R, ...).
public enum PartSide
{
    None,
    Left,
    Right
}

// The ship part library has no metadata assets behind it - every relationship is encoded in the
// object names. A hard point is an empty child called "<Category>HardPoint<suffix>" and a part
// prefab is called "<Model>_<Category>[_L|_R]", filed in a folder named after its category. This
// class is the single place that knows how to read those names, so a rename convention change only
// has to be handled here.
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

    // The order categories are offered in, which is roughly the order a ship gets built: the hull
    // first, then the big structure hanging off it, then the things that bolt onto that. Anything
    // not listed sorts after these.
    static readonly string[] CategoryDisplayOrder =
    {
        "Core",
        "CoreAttachment",
        "ModuleConnection",
        "Wing",
        "Tail",
        "Fin",
        "Engine",
        "Thruster",
        "Booster",
        "Reactor",
        "PrimaryWeapon",
        "SecondaryWeapon",
        "SpecialWeapon",
    };

    public static string NormalizeCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return category;
        return CategoryAliases.TryGetValue(category, out string mapped) ? mapped : category;
    }

    // Where a category sits in the tab strip. Unknown categories land after the known ones.
    public static int CategoryOrder(string category)
    {
        for (int i = 0; i < CategoryDisplayOrder.Length; i++)
        {
            if (CategoryDisplayOrder[i] == category) return i;
        }
        return CategoryDisplayOrder.Length;
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

    // The current library convention: "<Model>_<Category>[_L|_R]".
    //
    // The category is the last token before any side suffix, not the first token, because the model
    // name is the part that varies and may itself contain underscores - "Q_Propulsor_Engine_L" is
    // the Q Propulsor, an Engine, left hand. Reading from the back is what makes those names work.
    public static void ParseLibraryName(string prefabName, out string modelName, out string category, out PartSide side)
    {
        modelName = prefabName;
        category = prefabName;
        side = PartSide.None;

        if (string.IsNullOrEmpty(prefabName)) return;

        string[] tokens = prefabName.Split('_');
        int last = tokens.Length - 1;

        if (last > 0 && (tokens[last] == "L" || tokens[last] == "R"))
        {
            side = tokens[last] == "L" ? PartSide.Left : PartSide.Right;
            last--;
        }

        category = NormalizeCategory(tokens[last]);

        // Everything ahead of the category token is the model name. A bare "Booster" has none, so
        // it stands in for itself rather than coming back empty.
        modelName = last > 0 ? string.Join(" ", tokens, 0, last) : tokens[last];
    }

    // "Wings" -> "Wing". Folder names are plural, categories are not; used to sanity check a prefab
    // name against the folder it was filed in.
    public static string CategoryFromFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return folderName;

        bool plural = folderName.Length > 1
                      && folderName[folderName.Length - 1] == 's'
                      && folderName[folderName.Length - 2] != 's';

        return NormalizeCategory(plural ? folderName.Substring(0, folderName.Length - 1) : folderName);
    }

    // "Wing_Var10_L" -> category "Wing", variant 10, side Left. A bare "Booster" is variant 0.
    // The older library convention, kept for the first builder's catalog.
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

    // "_1_L" -> "_1". Two sockets that differ only by side are the same socket mirrored, which is
    // what lets the random builder hang the matching model on both shoulders.
    public static string SuffixWithoutSide(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return string.Empty;

        var kept = new List<string>();
        foreach (string token in suffix.Split('_'))
        {
            if (string.IsNullOrEmpty(token) || token == "L" || token == "R") continue;
            kept.Add(token);
        }
        return kept.Count == 0 ? string.Empty : "_" + string.Join("_", kept);
    }

    // Whether a part may sit on a hard point, judged purely on the sides the two names claim.
    //
    // The suffix only constrains the pairing when both names carry one: "Aegis_Wing_L" fits
    // "WingHardPoint_L" and never "WingHardPoint_R". If either name leaves the side out, anything
    // goes - a centred mount such as "WingHardPoint" takes left and right models alike, and a
    // centred model such as "Apex_Engine" drops onto either shoulder.
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

    // What a cell in the part list is labelled. The category is already the tab heading, so only the
    // model and the side it belongs on are worth repeating.
    public static string PrettyLibraryName(string modelName, PartSide side)
    {
        if (side == PartSide.Left) return modelName + " L";
        if (side == PartSide.Right) return modelName + " R";
        return modelName;
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
