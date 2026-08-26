using System;
using System.Collections.Generic;
using UnityEngine;

// One buildable prefab, with the naming metadata already decoded so the builder never has to
// re-parse strings while the menu is open.
[Serializable]
public class ShipPartDefinition
{
    public GameObject prefab;
    public string category;
    public int variant;
    public PartSide side;
    public string displayName;

    public bool IsValid => prefab != null && !string.IsNullOrEmpty(category);
}

// The list the build menu offers from. Populated by Tools > Ship Builder > Rebuild Part Catalog,
// which scans the prefab folder and decodes every name through PartNaming.
[CreateAssetMenu(fileName = "ShipPartCatalog", menuName = "Ship Builder/Part Catalog")]
public class ShipPartCatalog : ScriptableObject
{
    [Tooltip("Folder scanned by Tools > Ship Builder > Rebuild Part Catalog.")]
    public string sourceFolder = "Assets/Prefabs/ShipParts";

    public List<ShipPartDefinition> parts = new List<ShipPartDefinition>();

    // Reused between queries so opening a category does not allocate a new list every time.
    readonly List<ShipPartDefinition> results = new List<ShipPartDefinition>();

    public List<ShipPartDefinition> GetCores()
    {
        return GetPartsFor(PartNaming.CoreCategory, PartSide.None);
    }

    // Every part whose category matches and whose side does not contradict the hard point's side.
    public List<ShipPartDefinition> GetPartsFor(string category, PartSide hardPointSide)
    {
        results.Clear();
        if (string.IsNullOrEmpty(category)) return results;

        string wanted = PartNaming.NormalizeCategory(category);
        foreach (ShipPartDefinition def in parts)
        {
            if (def == null || !def.IsValid) continue;
            if (def.category != wanted) continue;
            if (!PartNaming.SidesMatch(def.side, hardPointSide)) continue;
            results.Add(def);
        }
        return results;
    }

    public bool HasPartsFor(string category, PartSide hardPointSide)
    {
        return GetPartsFor(category, hardPointSide).Count > 0;
    }

    public void SortParts()
    {
        parts.Sort((a, b) =>
        {
            int byCategory = string.CompareOrdinal(a.category, b.category);
            if (byCategory != 0) return byCategory;
            int byVariant = a.variant.CompareTo(b.variant);
            if (byVariant != 0) return byVariant;
            return a.side.CompareTo(b.side);
        });
    }
}
