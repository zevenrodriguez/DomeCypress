using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Saves the Orchid Root Sphere settings and the cloned GhostOrchid transforms while in Play mode,
// so changes made in Play mode aren't lost when you stop. Then "Apply Saved Settings" puts the
// settings back on the component after you exit Play mode.
public static class OrchidPlayModeValues
{
    const string SavePath = "Assets/OrchidRoots/orchid_playmode_values.json";
    const string SettingsPath = "Assets/OrchidRoots/orchid_playmode_settings.json";
    const string PosePath = "Assets/OrchidRoots/Resources/OrchidRoots/orchid_final_pose.json";

    [MenuItem("Tools/Orchids/Save Play Mode Values")]
    static void Save()
    {
        var sphere = Object.FindAnyObjectByType<OrchidRootSphere>();
        if (sphere == null) { Debug.LogWarning("No OrchidRootSphere in the open scene."); return; }

        // 1) the component's Inspector settings
        string settings = EditorJsonUtility.ToJson(sphere, true);
        File.WriteAllText(SettingsPath, settings);

        // 2) every cloned orchid's transform (local = relative to the OrchidRootSphere)
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"frame\": {sphere.CurrentFrame:F1},");
        sb.AppendLine("  \"orchids\": [");
        int n = 0;
        foreach (Transform child in sphere.transform)
        {
            if (child.name != "GhostOrchid") continue;
            Vector3 p = child.localPosition, s = child.localScale, e = child.localEulerAngles, w = child.position;
            Quaternion q = child.localRotation;
            if (n++ > 0) sb.AppendLine(",");
            sb.Append($"    {{ \"index\": {n - 1}, \"active\": {child.gameObject.activeSelf.ToString().ToLower()}, " +
                      $"\"localPosition\": [{p.x:F4}, {p.y:F4}, {p.z:F4}], " +
                      $"\"localEuler\": [{e.x:F2}, {e.y:F2}, {e.z:F2}], " +
                      $"\"localRotation\": [{q.x:F5}, {q.y:F5}, {q.z:F5}, {q.w:F5}], " +
                      $"\"localScale\": [{s.x:F4}, {s.y:F4}, {s.z:F4}], " +
                      $"\"worldPosition\": [{w.x:F4}, {w.y:F4}, {w.z:F4}] }}");
        }
        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText(SavePath, sb.ToString());

        // 3) the same transforms as the final pose the orchids open into on the next Play
        Directory.CreateDirectory(Path.GetDirectoryName(PosePath));
        File.WriteAllText(PosePath, sb.ToString());
        AssetDatabase.ImportAsset(PosePath);

        Debug.Log($"Saved {n} orchid transforms to {PosePath} (used as the final pose) and the settings to {SettingsPath}.");
    }

    [MenuItem("Tools/Orchids/Apply Saved Settings (after Play mode)")]
    static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode first, then apply."); return; }
        if (!File.Exists(SettingsPath)) { Debug.LogWarning("Nothing saved yet."); return; }
        var sphere = Object.FindAnyObjectByType<OrchidRootSphere>();
        if (sphere == null) { Debug.LogWarning("No OrchidRootSphere in the open scene."); return; }

        Undo.RecordObject(sphere, "Apply Saved Orchid Settings");
        EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), sphere);
        sphere.poseOrchidsByHand = false;   // posing is a Play mode tool; the saved pose takes over
        EditorUtility.SetDirty(sphere);
        EditorSceneManager.MarkSceneDirty(sphere.gameObject.scene);
        Debug.Log("Applied the saved Play mode settings. Save the scene (Ctrl+S) to keep them.");
    }
}
