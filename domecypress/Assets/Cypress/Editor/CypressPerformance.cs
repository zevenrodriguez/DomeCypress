using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click performance settings for the cypress dome.
///   Tools > Cypress > Performance > Apply Fast Dome Settings
///   Tools > Cypress > Performance > Restore Previous Settings
/// What "Apply" changes (and saves the old values so "Restore" can put them back):
///   1. Dome Creator: Dome Warp (one camera) instead of Cubemap (5-6 cameras); cubemap size 4096 -> 2048
///   2. Active URP asset: shadow cascades 2, shadow distance 30, soft shadow quality Medium
///   3. Marsh grass LOD: LOD1 at 15 m, LOD2 at 28 m, cull at 90 m
///   4. Ghost orchid textures: max size 1024
/// </summary>
static class CypressPerformance
{
    const string BackupPath = "UserSettings/CypressPerformanceBackup.json";
    const string OrchidFolder = "Assets/OrchidRoots/Resources/OrchidRoots";
    static readonly string[] OrchidTextures = { "GhostOrchid_BaseColor.png", "GhostOrchid_Normal.png" };

    // fast values
    const int CubemapSize = 2048;
    const int ShadowCascades = 2;
    const float ShadowDistance = 30f;
    const int SoftShadowMedium = 2;            // URP SoftShadowQuality: 1 Low, 2 Medium, 3 High
    const float GrassLod1 = 15f, GrassLod2 = 28f, GrassCull = 90f;
    const int OrchidMaxSize = 1024;

    [Serializable]
    class Backup
    {
        public bool hasDome; public bool domeWasWarp; public int cubemapSize;
        public bool hasPipeline; public int cascades; public float shadowDistance; public int softShadowQuality;
        public bool hasGrass; public float lod1, lod2, cull;
        public List<string> texturePaths = new List<string>();
        public List<int> textureSizes = new List<int>();
    }

    [MenuItem("Tools/Cypress/Performance/Apply Fast Dome Settings")]
    static void Apply()
    {
        var backup = new Backup();
        var log = new List<string>();

        // 1. dome rig
        if (FindDome(out var warpGo, out var cubeGo, out var cubemapComp))
        {
            backup.hasDome = true;
            backup.domeWasWarp = warpGo != null && warpGo.activeSelf;
            if (cubemapComp != null)
            {
                var so = new SerializedObject(cubemapComp);
                var size = so.FindProperty("cubemapSize");
                if (size != null) { backup.cubemapSize = size.intValue; size.intValue = CubemapSize; so.ApplyModifiedProperties(); }
            }
            SetDomeMode(warpGo, cubeGo, useWarp: true);
            log.Add($"Dome Creator -> Dome Warp (single camera), cubemap size {CubemapSize}");
        }
        else log.Add("Dome Creator not found in the open scenes - skipped");

        // 2. shadows on the URP asset in use
        var pipeline = ActivePipeline();
        if (pipeline != null)
        {
            var so = new SerializedObject(pipeline);
            backup.hasPipeline = true;
            backup.cascades = so.FindProperty("m_ShadowCascadeCount").intValue;
            backup.shadowDistance = so.FindProperty("m_ShadowDistance").floatValue;
            backup.softShadowQuality = so.FindProperty("m_SoftShadowQuality").intValue;
            Undo.RecordObject(pipeline, "Cypress fast shadows");
            so.FindProperty("m_ShadowCascadeCount").intValue = ShadowCascades;
            so.FindProperty("m_ShadowDistance").floatValue = ShadowDistance;
            so.FindProperty("m_SoftShadowQuality").intValue = SoftShadowMedium;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            log.Add($"{pipeline.name}: {ShadowCascades} cascades, shadow distance {ShadowDistance}, soft shadows Medium");
        }

        // 3. grass LOD
        var grass = UnityEngine.Object.FindAnyObjectByType<GrassInstancer>(FindObjectsInactive.Include);
        if (grass != null)
        {
            backup.hasGrass = true;
            backup.lod1 = grass.lod1Distance; backup.lod2 = grass.lod2Distance; backup.cull = grass.cullDistance;
            SetGrass(grass, GrassLod1, GrassLod2, GrassCull);
            log.Add($"Marsh grass LOD {GrassLod1} / {GrassLod2} m, cull {GrassCull} m");
        }

        // 4. orchid textures
        foreach (var file in OrchidTextures)
        {
            string path = $"{OrchidFolder}/{file}";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                backup.texturePaths.Add(path);
                backup.textureSizes.Add(ti.maxTextureSize);
                SetMaxSize(ti, OrchidMaxSize);
            }
        }
        if (backup.texturePaths.Count > 0) log.Add($"Ghost orchid textures max size {OrchidMaxSize}");

        // keep the first backup: applying twice must not overwrite the original values
        if (!File.Exists(BackupPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
            File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
        }

        SaveAll();
        Debug.Log("Cypress: fast dome settings applied -\n  " + string.Join("\n  ", log) +
                  "\nUse Tools > Cypress > Performance > Restore Previous Settings to undo.");
    }

    [MenuItem("Tools/Cypress/Performance/Restore Previous Settings")]
    static void Restore()
    {
        if (!File.Exists(BackupPath)) { Debug.LogWarning("Cypress: no saved settings to restore (Apply Fast Dome Settings hasn't been run)."); return; }
        var b = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));

        if (b.hasDome && FindDome(out var warpGo, out var cubeGo, out var cubemapComp))
        {
            if (cubemapComp != null)
            {
                var so = new SerializedObject(cubemapComp);
                var size = so.FindProperty("cubemapSize");
                if (size != null) { size.intValue = b.cubemapSize; so.ApplyModifiedProperties(); }
            }
            SetDomeMode(warpGo, cubeGo, b.domeWasWarp);
        }

        var pipeline = ActivePipeline();
        if (b.hasPipeline && pipeline != null)
        {
            var so = new SerializedObject(pipeline);
            so.FindProperty("m_ShadowCascadeCount").intValue = b.cascades;
            so.FindProperty("m_ShadowDistance").floatValue = b.shadowDistance;
            so.FindProperty("m_SoftShadowQuality").intValue = b.softShadowQuality;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
        }

        var grass = UnityEngine.Object.FindAnyObjectByType<GrassInstancer>(FindObjectsInactive.Include);
        if (b.hasGrass && grass != null) SetGrass(grass, b.lod1, b.lod2, b.cull);

        for (int i = 0; i < b.texturePaths.Count; i++)
            if (AssetImporter.GetAtPath(b.texturePaths[i]) is TextureImporter ti) SetMaxSize(ti, b.textureSizes[i]);

        File.Delete(BackupPath);
        SaveAll();
        Debug.Log("Cypress: previous dome, shadow, grass and texture settings restored.");
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Finds the Dome Creator's DomeRenderer and reads its warp / cubemap objects.</summary>
    static bool FindDome(out GameObject warpGo, out GameObject cubeGo, out Component cubemapComp)
    {
        warpGo = cubeGo = null; cubemapComp = null;
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
                cubemapComp = cube;
                return warpGo != null || cubeGo != null;
            }
        }
        return false;
    }

    static void SetDomeMode(GameObject warpGo, GameObject cubeGo, bool useWarp)
    {
        if (warpGo != null) { Undo.RecordObject(warpGo, "Dome mode"); warpGo.SetActive(useWarp); PrefabUtility.RecordPrefabInstancePropertyModifications(warpGo); }
        if (cubeGo != null) { Undo.RecordObject(cubeGo, "Dome mode"); cubeGo.SetActive(!useWarp); PrefabUtility.RecordPrefabInstancePropertyModifications(cubeGo); }
    }

    static void SetGrass(GrassInstancer grass, float lod1, float lod2, float cull)
    {
        Undo.RecordObject(grass, "Grass LOD");
        grass.lod1Distance = lod1;
        grass.lod2Distance = lod2;
        grass.cullDistance = cull;
        EditorUtility.SetDirty(grass);
    }

    static void SetMaxSize(TextureImporter ti, int size)
    {
        if (ti.maxTextureSize == size) return;
        ti.maxTextureSize = size;
        ti.SaveAndReimport();
    }

    static RenderPipelineAsset ActivePipeline()
    {
        return QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
    }

    static void SaveAll()
    {
        AssetDatabase.SaveAssets();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded && scene.IsValid()) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        }
    }
}
