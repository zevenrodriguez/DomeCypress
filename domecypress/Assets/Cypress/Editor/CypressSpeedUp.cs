using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Big frame-rate fix for the cypress dome.
///   Tools > Cypress > Performance > Speed Up Dome Rendering
///   Tools > Cypress > Performance > Undo Speed Up
///
/// The dome is drawn in Cubemap mode: the whole swamp is rendered 5 times per frame (one per cube face),
/// each at Cubemap Size x Cubemap Size, and every face redraws the shadows too. At 4096 that is ~84 million
/// pixels a frame before the dome image is even made. This:
///   - uses Dome Warp (one camera) when the dome's view angle is 180 or less; otherwise keeps the cubemap
///     (Dome Warp can't go past 180) and lowers Cubemap Size to 2048 - a quarter of the pixels, still
///     matching a 4096 dome master
///   - turns MSAA off (the cube faces can't use it, but it still costs memory and time)
///   - makes shadows cheaper for all those extra views: 1 cascade, 1024 shadow map, 25 m, no extra-light shadows
/// </summary>
static class CypressSpeedUp
{
    const string BackupPath = "UserSettings/CypressSpeedUpBackup.json";

    [Serializable]
    class Backup
    {
        public int cubemapSize; public bool warpWasActive; public bool hasDome;
        public int msaa, cascades, shadowRes; public float shadowDistance; public bool extraLightShadows;
    }

    [MenuItem("Tools/Cypress/Performance/Speed Up Dome Rendering")]
    static void Apply()
    {
        var b = new Backup();
        string modeNote = "Dome Creator not found";

        // dome rig
        if (FindDome(out var warpGo, out var cubeGo, out var cubeComp))
        {
            b.hasDome = true;
            b.warpWasActive = warpGo != null && warpGo.activeSelf;
            var so = new SerializedObject(cubeComp);
            var size = so.FindProperty("cubemapSize");
            var angle = so.FindProperty("viewAngle");
            b.cubemapSize = size.intValue;
            size.intValue = 2048;
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(cubeComp);

            float viewAngle = angle != null ? angle.floatValue : 180f;
            bool useWarp = viewAngle <= 180.5f && warpGo != null;
            SetActive(warpGo, useWarp);
            SetActive(cubeGo, !useWarp);
            modeNote = useWarp
                ? "Dome Warp (single camera)"
                : $"Cubemap kept (view angle {viewAngle:0} is over 180, which Dome Warp can't do), Cubemap Size 4096 -> 2048";
        }

        // render pipeline asset in use
        var pipeline = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        if (pipeline != null)
        {
            var so = new SerializedObject(pipeline);
            b.msaa = so.FindProperty("m_MSAA").intValue;
            b.cascades = so.FindProperty("m_ShadowCascadeCount").intValue;
            b.shadowRes = so.FindProperty("m_MainLightShadowmapResolution").intValue;
            b.shadowDistance = so.FindProperty("m_ShadowDistance").floatValue;
            b.extraLightShadows = so.FindProperty("m_AdditionalLightShadowsSupported").boolValue;
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_ShadowCascadeCount").intValue = 1;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 1024;
            so.FindProperty("m_ShadowDistance").floatValue = 25f;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
        }

        if (!File.Exists(BackupPath))   // keep the very first values so Undo goes all the way back
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
            File.WriteAllText(BackupPath, JsonUtility.ToJson(b, true));
        }
        Save();
        Debug.Log("Cypress: dome rendering sped up -\n  " + modeNote +
                  "\n  MSAA off, shadows: 1 cascade, 1024 map, 25 m, no extra-light shadows" +
                  "\nUndo with Tools > Cypress > Performance > Undo Speed Up.");
    }

    [MenuItem("Tools/Cypress/Performance/Undo Speed Up")]
    static void Revert()
    {
        if (!File.Exists(BackupPath)) { Debug.LogWarning("Cypress: nothing to undo."); return; }
        var b = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        if (b.hasDome && FindDome(out var warpGo, out var cubeGo, out var cubeComp))
        {
            var so = new SerializedObject(cubeComp);
            so.FindProperty("cubemapSize").intValue = b.cubemapSize;
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(cubeComp);
            SetActive(warpGo, b.warpWasActive);
            SetActive(cubeGo, !b.warpWasActive);
        }
        var pipeline = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        if (pipeline != null)
        {
            var so = new SerializedObject(pipeline);
            so.FindProperty("m_MSAA").intValue = b.msaa;
            so.FindProperty("m_ShadowCascadeCount").intValue = b.cascades;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = b.shadowRes;
            so.FindProperty("m_ShadowDistance").floatValue = b.shadowDistance;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = b.extraLightShadows;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
        }
        File.Delete(BackupPath);
        Save();
        Debug.Log("Cypress: speed-up undone.");
    }

    static bool FindDome(out GameObject warpGo, out GameObject cubeGo, out Component cubeComp)
    {
        warpGo = cubeGo = null; cubeComp = null;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "DomeRenderer") continue;
                var so = new SerializedObject(mb);
                var warp = so.FindProperty("domeWarping")?.objectReferenceValue as Component;
                var cube = so.FindProperty("cubemapRendering")?.objectReferenceValue as Component;
                warpGo = warp != null ? warp.gameObject : null;
                cubeGo = cube != null ? cube.gameObject : null;
                cubeComp = cube;
                return cube != null;
            }
        }
        return false;
    }

    static void SetActive(GameObject go, bool on)
    {
        if (go == null) return;
        Undo.RecordObject(go, "Dome mode");
        go.SetActive(on);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
    }

    static void Save()
    {
        AssetDatabase.SaveAssets();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        }
    }
}
