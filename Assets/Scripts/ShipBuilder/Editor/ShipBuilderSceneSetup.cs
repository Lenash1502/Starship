using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// One-click setup for the build screen. Creates whatever the open scene is missing - the stand the
// ship is assembled on, a camera framing it, a light, an EventSystem and the builder itself - and
// leaves anything that is already there alone, so it is safe to re-run.
public static class ShipBuilderSceneSetup
{
    const string SelectionCircleName = "SelectionCircle";

    [MenuItem("Tools/Ship Builder/Setup Builder Scene")]
    public static void SetupScene()
    {
        // Always rescan: prefabs get added between runs, and a stale catalog shows up as a category
        // that is mysteriously empty at runtime.
        ShipPartCatalog catalog = ShipPartCatalogBuilder.LoadOrCreateCatalog();
        ShipPartCatalogBuilder.Rebuild(catalog);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Transform circle = EnsureSelectionCircle();
        Camera camera = EnsureCamera(circle);
        EnsureLight();
        EnsureEventSystem();

        var existing = Object.FindAnyObjectByType<ShipBuilder>();
        GameObject builderObject = existing != null ? existing.gameObject : new GameObject("Ship Builder");
        if (existing == null) Undo.RegisterCreatedObjectUndo(builderObject, "Create Ship Builder");

        ShipBuilder builder = builderObject.GetComponent<ShipBuilder>();
        if (builder == null) builder = Undo.AddComponent<ShipBuilder>(builderObject);

        builder.catalog = catalog;
        builder.selectionCircle = circle;
        builder.selectionCircleName = circle.name;
        builder.builderCamera = camera;
        // The ship sits exactly on the stand transform; the camera pulls back to fit it instead.
        builder.assemblyOffset = Vector3.zero;

        if (builderObject.GetComponent<PartThumbnailRenderer>() == null) Undo.AddComponent<PartThumbnailRenderer>(builderObject);
        if (builderObject.GetComponent<ShipBuilderUI>() == null) Undo.AddComponent<ShipBuilderUI>(builderObject);

        EditorUtility.SetDirty(builderObject);
        EditorSceneManager.MarkSceneDirty(builderObject.scene);
        Selection.activeGameObject = builderObject;

        Debug.Log($"[Ship Builder] Scene ready. {catalog.parts.Count} parts in the catalog - press Play to build.");
    }

    // The stand is whatever the scene already uses to mark where the ship goes - an empty, a decal,
    // a lit ring. The builder only ever reads its transform, so nothing is generated on top of it.
    static Transform EnsureSelectionCircle()
    {
        Transform existing = ShipBuilder.FindSelectionCircle(SelectionCircleName);
        if (existing != null) return existing;

        var circle = new GameObject(SelectionCircleName);
        circle.transform.position = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(circle, "Create Selection Circle");

        Debug.Log("[Ship Builder] No selection circle found, so an empty one was created at the origin.");
        return circle.transform;
    }

    static Camera EnsureCamera(Transform stand)
    {
        Camera camera = Camera.main;
        bool created = camera == null;

        if (created)
        {
            var cameraObject = new GameObject("Builder Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create Builder Camera");

            // Front three quarter view: the models point their nose down +Z, so standing off to
            // +Z shows the front of the ship rather than its engines. Only the direction matters -
            // ShipBuilder pulls the camera back to whatever distance the ship needs at runtime.
            camera.transform.position = stand.position + new Vector3(6f, 3.5f, 13f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
        }

        // Auto framing slides the camera along its own view direction, so that direction has to
        // point at the stand for the result to be centred.
        Vector3 toStand = stand.position - camera.transform.position;
        if (toStand.sqrMagnitude > 0.0001f)
        {
            camera.transform.rotation = Quaternion.LookRotation(toStand, Vector3.up);
        }

        if (!created) Debug.Log($"[Ship Builder] Reusing the existing main camera ({camera.name}), aimed at {stand.name}.");
        return camera;
    }

    static void EnsureLight()
    {
        foreach (Light light in Object.FindObjectsByType<Light>())
        {
            if (light.type == LightType.Directional) return;
        }

        var lightObject = new GameObject("Builder Key Light");
        Light keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.4f;
        // Angled in from the camera side so the front of the ship is the lit side.
        lightObject.transform.rotation = Quaternion.Euler(40f, 200f, 0f);
        Undo.RegisterCreatedObjectUndo(lightObject, "Create Builder Light");
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
    }
}
