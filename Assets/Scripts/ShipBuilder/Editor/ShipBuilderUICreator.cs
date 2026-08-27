using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bakes the parts panel into the open scene as ordinary uGUI objects.
//
// ShipBuilderUI can put the same panel together on play, but then there is nothing to select and
// nothing to tweak. Running the identical construction code at edit time leaves a real hierarchy
// behind - canvas, panel, header, scroll view, item template - with every reference on the
// component already filled in, so the look can be changed in the inspector and it sticks.
public static class ShipBuilderUICreator
{
    [MenuItem("Tools/Ship Builder/Create UI In Scene")]
    public static void CreateUI()
    {
        var ui = Object.FindAnyObjectByType<ShipBuilderUI>();
        if (ui == null)
        {
            Debug.LogError("[Ship Builder] No ShipBuilderUI in the scene. Run Tools > Ship Builder > Setup Builder Scene first.");
            return;
        }

        if (ui.canvas != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Ship Builder UI",
                "This scene already has a builder panel. Replacing it throws away any styling done to the existing one.",
                "Replace", "Cancel");

            if (!replace) return;
            Undo.DestroyObjectImmediate(ui.canvas.gameObject);
        }

        Undo.RecordObject(ui, "Create Ship Builder UI");
        ui.BuildHierarchy();
        Undo.RegisterCreatedObjectUndo(ui.canvas.gameObject, "Create Ship Builder UI");

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Selection.activeGameObject = ui.canvas.gameObject;

        Debug.Log("[Ship Builder] Panel created in the scene. Restyle it freely - " +
                  "'Item Template' under Content is the entry every part in the list is cloned from.");
    }

    [MenuItem("Tools/Ship Builder/Create UI In Scene", true)]
    static bool CreateUIValidate()
    {
        return Object.FindAnyObjectByType<ShipBuilderUI>() != null;
    }
}
