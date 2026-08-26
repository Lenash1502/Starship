using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Rebuilds the part catalog from the prefab folder. Because every relationship between parts and
// sockets lives in the object names, the catalog is disposable - re-run this after adding, renaming
// or deleting prefabs and the build menu picks the change up.
public static class ShipPartCatalogBuilder
{
    public const string CatalogPath = "Assets/Settings/ShipPartCatalog.asset";

    [MenuItem("Tools/Ship Builder/Rebuild Part Catalog")]
    public static void RebuildMenuItem()
    {
        ShipPartCatalog catalog = LoadOrCreateCatalog();
        int count = Rebuild(catalog);

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;

        Debug.Log($"[Ship Builder] Catalog rebuilt with {count} parts from {catalog.sourceFolder}.");
    }

    public static ShipPartCatalog LoadOrCreateCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ShipPartCatalog>(CatalogPath);
        if (catalog != null) return catalog;

        catalog = ScriptableObject.CreateInstance<ShipPartCatalog>();

        string folder = System.IO.Path.GetDirectoryName(CatalogPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Settings");

        AssetDatabase.CreateAsset(catalog, CatalogPath);
        Rebuild(catalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    public static int Rebuild(ShipPartCatalog catalog)
    {
        catalog.parts.Clear();

        if (!AssetDatabase.IsValidFolder(catalog.sourceFolder))
        {
            Debug.LogWarning($"[Ship Builder] Part folder not found: {catalog.sourceFolder}");
            return 0;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { catalog.sourceFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            PartNaming.ParsePart(prefab.name, out string category, out int variant, out PartSide side);

            catalog.parts.Add(new ShipPartDefinition
            {
                prefab = prefab,
                category = category,
                variant = variant,
                side = side,
                displayName = PartNaming.PrettyPartName(category, variant, side)
            });
        }

        catalog.SortParts();
        return catalog.parts.Count;
    }

    // Handy sanity check: lists every hard point category the prefabs ask for that has no part to
    // put in it, so missing content shows up as a console line rather than an empty menu at runtime.
    [MenuItem("Tools/Ship Builder/Report Unfilled Hard Points")]
    public static void ReportUnfilledHardPoints()
    {
        ShipPartCatalog catalog = LoadOrCreateCatalog();

        var wanted = new HashSet<string>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { catalog.sourceFolder });
        foreach (string guid in guids)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) continue;

            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (PartNaming.TryParseHardPoint(child.name, out string category, out _, out _)) wanted.Add(category);
            }
        }

        var missing = new List<string>();
        foreach (string category in wanted)
        {
            if (!catalog.HasPartsFor(category, PartSide.None)) missing.Add(category);
        }

        if (missing.Count == 0) Debug.Log("[Ship Builder] Every hard point category has at least one part.");
        else Debug.LogWarning("[Ship Builder] No parts exist for: " + string.Join(", ", missing));
    }
}
