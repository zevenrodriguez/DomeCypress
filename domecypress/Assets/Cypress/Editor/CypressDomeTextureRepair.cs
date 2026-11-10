using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fixes "UnassignedReferenceException: The variable targetTexture of RealtimeCubemap has not been assigned".
/// The Dome Creator's render textures (Target Texture on the cubemap and dome-warp cameras, Dome Content
/// Texture on the Dome Renderer) had been cleared in the scene, so nothing reached the dome image.
/// This puts them back to the Dome Creator prefab's own textures in every open scene.
/// Menu: Tools > Cypress > Dome > Repair Dome Textures (open scenes)
/// </summary>
static class CypressDomeTextureRepair
{
    // the Dome Creator prefab's content texture (DomeTexture.renderTexture in the pfc Dome Tools package)
    const string ContentTextureGuid = "54b745df278d953498d9484d22bd4730";

    [MenuItem("Tools/Cypress/Dome/Repair Dome Textures (open scenes)")]
    static void Repair()
    {
        var content = AssetDatabase.LoadAssetAtPath<RenderTexture>(AssetDatabase.GUIDToAssetPath(ContentTextureGuid));
        int fixedCount = 0;

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string type = mb.GetType().Name;
                string field = type == "RealtimeCubemap" || type == "DomeWarp" ? "targetTexture"
                             : type == "DomeRenderer" ? "domeContentTexture" : null;
                if (field == null) continue;

                var so = new SerializedObject(mb);
                var prop = so.FindProperty(field);
                if (prop == null || prop.objectReferenceValue != null) continue;

                // first choice: undo the scene's override so it uses the prefab's texture again
                if (PrefabUtility.IsPartOfPrefabInstance(mb) && prop.prefabOverride)
                {
                    PrefabUtility.RevertPropertyOverride(prop, InteractionMode.UserAction);
                    so.Update();
                    prop = so.FindProperty(field);
                }
                // still empty: assign the content texture directly
                if (prop.objectReferenceValue == null && content != null)
                {
                    prop.objectReferenceValue = content;
                    so.ApplyModifiedProperties();
                }
                if (prop.objectReferenceValue != null)
                {
                    fixedCount++;
                    changed = true;
                    Debug.Log($"Cypress: {type}.{field} on '{mb.gameObject.name}' -> {prop.objectReferenceValue.name}", mb);
                }
            }
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        }

        Debug.Log(fixedCount > 0
            ? $"Cypress: repaired {fixedCount} dome texture slot(s) and saved the scene(s)."
            : "Cypress: all dome texture slots in the open scenes are already filled in.");
    }
}
