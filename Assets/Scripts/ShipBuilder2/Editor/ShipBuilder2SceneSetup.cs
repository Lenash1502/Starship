using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// Creates PlaneSelectTest2 and everything in it, alongside the part catalog it feeds on.
//
// The catalog is disposable: every relationship between parts and sockets lives in object names and
// folder names, so re-run the rebuild after adding, renaming or moving prefabs and the builder picks
// the change up. Sub-folders of the part folder are the categories the tab strip shows.
public static class ShipBuilder2SceneSetup
{
    // Kept at its original path so the scene's existing reference still resolves; despite the name
    // it is the catalog of real ship parts, not of the generic blockout modules.
    public const string CatalogPath = "Assets/Settings/ShipModuleCatalog.asset";
    public const string PartFolder = "Assets/Prefabs/ShipParts";
    public const string ScenePath = "Assets/Scenes/PlaneSelectTest2.unity";

    const string SelectionCircleName = "SelectionCircle";

    [MenuItem("Tools/Ship Builder 2/Rebuild Part Catalog")]
    public static ShipPartCatalog RebuildCatalogMenuItem()
    {
        ShipPartCatalog catalog = RebuildCatalog();
        Selection.activeObject = catalog;
        return catalog;
    }

    public static ShipPartCatalog RebuildCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ShipPartCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ShipPartCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.sourceFolder = PartFolder;
        catalog.parts.Clear();

        if (!AssetDatabase.IsValidFolder(PartFolder))
        {
            Debug.LogError($"[Ship Builder 2] Part folder not found: {PartFolder}");
            return catalog;
        }

        var misfiled = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PartFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            PartNaming.ParseLibraryName(prefab.name, out string modelName, out string category, out PartSide side);

            // The folder is the second opinion, never the answer: a prefab that disagrees with the
            // folder it sits in is almost always a typo in one of the two, and silently trusting
            // either would put it on a tab nobody expects.
            string folderCategory = PartNaming.CategoryFromFolder(FolderNameOf(path));
            if (folderCategory != category) misfiled.Add($"{prefab.name} is filed under {folderCategory}");

            catalog.parts.Add(new ShipPartDefinition
            {
                prefab = prefab,
                category = category,
                modelName = modelName,
                variant = 0,
                side = side,
                displayName = PartNaming.PrettyLibraryName(modelName, side)
            });
        }

        catalog.SortParts();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        var summary = new List<string>();
        foreach (string category in catalog.CategoriesInOrder())
        {
            summary.Add($"{category} {catalog.PartsInCategory(category).Count}");
        }

        Debug.Log($"[Ship Builder 2] Part catalog rebuilt: {catalog.parts.Count} parts from {PartFolder}.\n  "
                  + string.Join("\n  ", summary));

        if (misfiled.Count > 0)
        {
            Debug.LogWarning($"[Ship Builder 2] {misfiled.Count} prefabs disagree with the folder they are in:\n  "
                             + string.Join("\n  ", misfiled));
        }

        return catalog;
    }

    static string FolderNameOf(string assetPath)
    {
        string directory = System.IO.Path.GetDirectoryName(assetPath);
        return string.IsNullOrEmpty(directory) ? string.Empty : System.IO.Path.GetFileName(directory);
    }

    // Content coverage: walks every socket in every part prefab, counts what the catalog would offer
    // there, and calls out the ones with nothing to put in them - so a category with no prefabs yet
    // shows up as a console line rather than as a socket that never lights up at runtime.
    [MenuItem("Tools/Ship Builder 2/Report Unfilled Hard Points")]
    public static void ReportUnfilledHardPoints()
    {
        ShipPartCatalog catalog = RebuildCatalog();

        var socketNames = new Dictionary<string, int>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PartFolder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) continue;

            CountSockets(prefab.transform, socketNames);
        }

        var empty = new List<string>();
        int emptySocketCount = 0;

        foreach (KeyValuePair<string, int> socket in socketNames)
        {
            PartNaming.TryParseHardPoint(socket.Key, out string category, out PartSide side, out _);
            if (catalog.HasPartsFor(category, side)) continue;

            empty.Add($"{socket.Key} ({socket.Value} sockets)");
            emptySocketCount += socket.Value;
        }

        Debug.Log($"[Ship Builder 2] {socketNames.Count} distinct mounts across the part prefabs.");

        if (empty.Count == 0)
        {
            Debug.Log("[Ship Builder 2] Every mount has at least one part to offer.");
        }
        else
        {
            empty.Sort();
            Debug.LogWarning($"[Ship Builder 2] {empty.Count} mounts have nothing to offer, covering {emptySocketCount} sockets:\n  "
                             + string.Join("\n  ", empty));
        }
    }

    // Stops at each socket, exactly as the runtime scan does: the sockets declared by the blockout
    // model parked inside one belong to a part that has not been chosen yet, so counting them would
    // report shortages that do not exist.
    static void CountSockets(Transform node, Dictionary<string, int> into)
    {
        foreach (Transform child in node)
        {
            if (PartNaming.IsHardPointName(child.name))
            {
                into.TryGetValue(child.name, out int count);
                into[child.name] = count + 1;
                continue;
            }

            CountSockets(child, into);
        }
    }

    // Throws the panel away and builds it again from the current style settings. Needed after the
    // panel grows a new section, since the hierarchy in the scene is a baked copy of what
    // BuildHierarchy produces rather than something rebuilt on load.
    [MenuItem("Tools/Ship Builder 2/Rebuild UI Panel")]
    public static void RebuildUIPanel()
    {
        var ui = Object.FindAnyObjectByType<ShipBuilder2UI>();
        if (ui == null)
        {
            Debug.LogError("[Ship Builder 2] No ShipBuilder2UI in the open scene.");
            return;
        }

        if (ui.canvas != null) Object.DestroyImmediate(ui.canvas.gameObject);

        ui.canvas = null;
        ui.panelRect = null;
        ui.tabBar = null;
        ui.tabTemplate = null;
        ui.content = null;
        ui.itemTemplate = null;
        ui.partToolsRoot = null;
        ui.turnButton = null;
        ui.tiltButton = null;
        ui.rollButton = null;
        ui.mirrorButton = null;
        ui.randomRoot = null;
        ui.generateButton = null;
        ui.depthSlider = null;
        ui.depthLabel = null;
        ui.titleText = null;
        ui.subtitleText = null;
        ui.footerText = null;
        ui.removeButton = null;

        ui.BuildHierarchy();

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Selection.activeGameObject = ui.gameObject;

        Debug.Log("[Ship Builder 2] UI panel rebuilt.");
    }

    [MenuItem("Tools/Ship Builder 2/Create PlaneSelectTest2 Scene")]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ShipPartCatalog catalog = RebuildCatalog();

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var stand = new GameObject(SelectionCircleName);
        stand.transform.position = Vector3.zero;

        Camera camera = CreateCamera(stand.transform);
        CreateLight();
        CreateEventSystem();

        var builderObject = new GameObject("Ship Builder 2");

        var rig = builderObject.AddComponent<BuilderCamera>();
        rig.view = camera;

        var builder = builderObject.AddComponent<ShipBuilder2>();
        builder.catalog = catalog;
        builder.selectionCircle = stand.transform;
        builder.selectionCircleName = stand.name;
        builder.builderCamera = rig;

        builderObject.AddComponent<PartThumbnailRenderer>();

        var ui = builderObject.AddComponent<ShipBuilder2UI>();
        ui.builder = builder;
        ui.BuildHierarchy();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        Selection.activeGameObject = builderObject;
        Debug.Log($"[Ship Builder 2] {ScenePath} created with {catalog.parts.Count} parts. Press Play and pick a core.");
    }

    static Camera CreateCamera(Transform stand)
    {
        var cameraObject = new GameObject("Builder Camera") { tag = "MainCamera" };
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();

        // Front three quarter view. Only the direction matters: BuilderCamera slides the camera
        // along it to whatever distance the ship needs.
        camera.transform.position = stand.position + new Vector3(6f, 3.5f, 13f);
        camera.transform.rotation = Quaternion.LookRotation(stand.position - camera.transform.position, Vector3.up);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        return camera;
    }

    static void CreateLight()
    {
        var lightObject = new GameObject("Builder Key Light");
        Light keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.4f;
        // Angled in from the camera side so the front of the ship is the lit side.
        lightObject.transform.rotation = Quaternion.Euler(40f, 200f, 0f);
    }

    static void CreateEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
