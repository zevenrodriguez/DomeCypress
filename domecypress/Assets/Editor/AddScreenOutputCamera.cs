using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Fixes "Display 1 - No cameras rendering" when using pfc Dome Tools.
// The Dome Creator's camera renders into a RenderTexture (not the screen),
// and the DomeMaster canvas shows that texture as a UI overlay.
// Unity wants at least one camera drawing to Display 1, so this adds
// a cheap camera that renders nothing and just clears the screen to black.
public static class AddScreenOutputCamera
{
    const string CameraName = "Screen Output Camera";

    [MenuItem("Tools/Dome/Add Screen Output Camera")]
    static void Add()
    {
        // Don't add a second one if it already exists
        GameObject existing = GameObject.Find(CameraName);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log(CameraName + " already exists in this scene.");
            return;
        }

        GameObject go = new GameObject(CameraName);
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;          // Nothing - draws no objects
        cam.depth = -100;             // Renders before every other camera
        cam.targetDisplay = 0;        // Display 1
        cam.targetTexture = null;     // Draw to the screen
        cam.orthographic = true;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1f;

        Undo.RegisterCreatedObjectUndo(go, "Add " + CameraName);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("Added " + CameraName + ". Save the scene (Ctrl+S) to keep it.");
    }
}
