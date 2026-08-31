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

    // The model family the prefab belongs to: "Aegis" for both Aegis_Wing_L and Aegis_Wing_R. Two
    // definitions sharing a category and a model name are the same wing mirrored, which is how the
    // random builder keeps a ship symmetrical.
    public string modelName;

    public int variant;
    public PartSide side;
    public string displayName;

    public bool IsValid => prefab != null && !string.IsNullOrEmpty(category);
}

// The list the build menu offers from. Populated by Tools > Ship Builder 2 > Rebuild Part Catalog,
// which scans the prefab folder tree and decodes every name through PartNaming.
[CreateAssetMenu(fileName = "ShipPartCatalog", menuName = "Ship Builder/Part Catalog")]
public class ShipPartCatalog : ScriptableObject
{
    [Tooltip("Folder scanned when the catalog is rebuilt. Sub-folders are scanned too, one per category.")]
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

    // Every category the catalog actually holds parts for, in tab order. A fresh list each call:
    // this runs when the panel is built, not per frame, and the caller keeps it.
    public List<string> CategoriesInOrder()
    {
        var found = new List<string>();
        foreach (ShipPartDefinition def in parts)
        {
            if (def == null || !def.IsValid) continue;
            if (!found.Contains(def.category)) found.Add(def.category);
        }

        found.Sort(CompareCategories);
        return found;
    }

    // Everything in one category, whatever side it is for. A fresh list each call, for the same
    // reason as above.
    public List<ShipPartDefinition> PartsInCategory(string category)
    {
        var found = new List<ShipPartDefinition>();
        if (string.IsNullOrEmpty(category)) return found;

        string wanted = PartNaming.NormalizeCategory(category);
        foreach (ShipPartDefinition def in parts)
        {
            if (def == null || !def.IsValid) continue;
            if (def.category == wanted) found.Add(def);
        }
        return found;
    }

    public void SortParts()
    {
        parts.Sort((a, b) =>
        {
            int byCategory = CompareCategories(a.category, b.category);
            if (byCategory != 0) return byCategory;

            int byModel = string.CompareOrdinal(a.modelName, b.modelName);
            if (byModel != 0) return byModel;

            int byVariant = a.variant.CompareTo(b.variant);
            if (byVariant != 0) return byVariant;

            return a.side.CompareTo(b.side);
        });
    }

    static int CompareCategories(string a, string b)
    {
        int byOrder = PartNaming.CategoryOrder(a).CompareTo(PartNaming.CategoryOrder(b));
        return byOrder != 0 ? byOrder : string.CompareOrdinal(a, b);
    }
}
