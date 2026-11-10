using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Slows the title sequence down: adds 2 seconds to every step.
///   Tools > Orchids > Title Timing: Add 2 Seconds To Each Step
/// Steps: flowers opening, pause before the moths, moths flying in, pause before the fade, fade to black,
/// title fading in, title on screen, next scene fading in. Run it again to add another 2 seconds.
/// Undo (Ctrl+Z) takes it back.
/// </summary>
public static class OrchidTitleTiming
{
    const float Extra = 2f;

    [MenuItem("Tools/Orchids/Title Timing: Add 2 Seconds To Each Step")]
    static void AddTwoSeconds()
    {
        var sphere = Object.FindAnyObjectByType<OrchidRootSphere>();
        var moths = Object.FindAnyObjectByType<OrchidMoths>();
        var ending = Object.FindAnyObjectByType<OrchidTitleEnding>();
        if (sphere == null && moths == null && ending == null)
        {
            Debug.LogWarning("Open the title scene first.");
            return;
        }

        if (sphere != null)
        {
            Undo.RecordObject(sphere, "Title timing");
            sphere.extraBloomSeconds += Extra;          // flowers opening
            EditorUtility.SetDirty(sphere);
        }
        if (moths != null)
        {
            Undo.RecordObject(moths, "Title timing");
            moths.delayAfterFinish += Extra;            // pause before the moths
            moths.flyInSeconds += Extra;                // moths flying in
            EditorUtility.SetDirty(moths);
        }
        if (ending != null)
        {
            Undo.RecordObject(ending, "Title timing");
            ending.waitAfterMoths += Extra;             // pause before the fade
            ending.fadeOutSeconds += Extra;             // fade to black
            ending.titleFadeInSeconds += Extra;         // title fading in
            ending.secondsOnTitle += Extra;             // title on screen
            ending.nextSceneFadeInSeconds += Extra;     // next scene fading in
            EditorUtility.SetDirty(ending);
        }

        var scene = (sphere != null ? sphere.gameObject : moths != null ? moths.gameObject : ending.gameObject).scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Added 2 seconds to each step of the title sequence and saved the scene.");
    }
}
