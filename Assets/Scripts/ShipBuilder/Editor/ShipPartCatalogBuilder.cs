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

    // Sanity check on content coverage: walks every hard point in every prefab, counts what the
    // catalog would offer there, and calls out the ones with nothing to put in them - so missing
    // content shows up as a console line rather than as an empty menu at runtime.
    [MenuItem("Tools/Ship Builder/Report Unfilled Hard Points")]
    public static void ReportUnfilledHardPoints()
    {
        ShipPartCatalog catalog = LoadOrCreateCatalog();
        Rebuild(catalog);

        // Distinct mount names, with how many times each one appears across the prefabs.
        var sockets = new Dictionary<string, int>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { catalog.sourceFolder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) continue;

            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (!PartNaming.IsHardPointName(child.name)) continue;

                sockets.TryGetValue(child.name, out int count);
                sockets[child.name] = count + 1;
            }
        }

        var empty = new List<string>();
        int emptySocketCount = 0;

        foreach (KeyValuePair<string, int> socket in sockets)
        {
            PartNaming.TryParseHardPoint(socket.Key, out string category, out PartSide side, out _);
            if (catalog.HasPartsFor(category, side)) continue;

            empty.Add($"{socket.Key} ({socket.Value} sockets)");
            emptySocketCount += socket.Value;
        }

        Debug.Log($"[Ship Builder] {sockets.Count} distinct mounts across the prefabs.");

        if (empty.Count == 0)
        {
            Debug.Log("[Ship Builder] Every mount has at least one part to offer.");
        }
        else
        {
            empty.Sort();
            Debug.LogWarning($"[Ship Builder] {empty.Count} mounts have nothing to offer, covering {emptySocketCount} sockets:\n  "
                             + string.Join("\n  ", empty));
        }
    }
}
