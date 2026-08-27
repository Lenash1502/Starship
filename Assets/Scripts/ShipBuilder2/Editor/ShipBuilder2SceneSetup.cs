using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// Creates PlaneSelectTest2 and everything in it, alongside the module catalog it feeds on.
//
// Kept entirely separate from the first builder's setup so the original scene and scripts stay
// exactly as they were and the two can be compared side by side.
public static class ShipBuilder2SceneSetup
{
    public const string CatalogPath = "Assets/Settings/ShipModuleCatalog.asset";
    public const string ModuleFolder = "Assets/Prefabs/ShipModules";
    public const string ScenePath = "Assets/Scenes/PlaneSelectTest2.unity";

    const string SelectionCircleName = "SelectionCircle";

    [MenuItem("Tools/Ship Builder 2/Rebuild Module Catalog")]
    public static ShipPartCatalog RebuildCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ShipPartCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ShipPartCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.sourceFolder = ModuleFolder;
        catalog.parts.Clear();

        if (!AssetDatabase.IsValidFolder(ModuleFolder))
        {
            Debug.LogError($"[Ship Builder 2] Module folder not found: {ModuleFolder}");
            return catalog;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ModuleFolder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
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
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Ship Builder 2] Module catalog rebuilt: {catalog.parts.Count} modules from {ModuleFolder}.");
        return catalog;
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
        Debug.Log($"[Ship Builder 2] {ScenePath} created with {catalog.parts.Count} modules. Press Play - the core is already on the stand.");
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
