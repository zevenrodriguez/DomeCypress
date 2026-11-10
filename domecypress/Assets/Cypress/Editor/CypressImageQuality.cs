using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the dome image less pixelated / jagged.
/// Menu: Tools > Cypress > Dome > Smooth Image (less pixelated)
///   1. MSAA 4x on the URP asset in use (smooth geometry edges: trunks, branches, knees)
///   2. SMAA anti-aliasing on every camera in the open scenes (smooths everything, incl. leaf edges)
///   3. Leaf / plant / litter textures: trilinear filtering, anisotropic 4, and "preserve coverage" mip maps
///      so far-away alpha-cut leaves stay full instead of breaking up into speckles
///   4. Dome master render textures: mip maps, so the on-screen preview is smooth when shrunk to fit
/// </summary>
static class CypressImageQuality
{
    static readonly string[] CutoutTextures =
    {
        "Assets/Cypress/Textures/cypress_leaf_atlas.png",
        "Assets/Cypress/Textures/swamp_understory_atlas.png",
        "Assets/Cypress/Textures/floating_litter_atlas.png",
    };

    [MenuItem("Tools/Cypress/Dome/Smooth Image (less pixelated)")]
    static void Apply()
    {
        // 1. MSAA on the active URP asset
        var pipeline = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        if (pipeline != null)
        {
            var so = new SerializedObject(pipeline);
            var msaa = so.FindProperty("m_MSAA");
            if (msaa != null) { msaa.intValue = 4; so.ApplyModifiedProperties(); EditorUtility.SetDirty(pipeline); }
        }

        // 2. SMAA on every camera (the dome rig's cameras included)
        int cams = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = Undo.AddComponent<UniversalAdditionalCameraData>(cam.gameObject);
                Undo.RecordObject(data, "Smooth image");
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                cams++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // 3. alpha-cut plant textures
        foreach (var path in CutoutTextures)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
            ti.mipmapEnabled = true;
            ti.mipMapsPreserveCoverage = true;     // keeps leaves full at a distance instead of thinning out
            ti.alphaTestReferenceValue = 0.5f;     // matches the leaf shaders' cutoff
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
        }

        // 4. dome master textures in the project: mip maps for a smooth downscaled preview
        foreach (var guid in AssetDatabase.FindAssets("t:RenderTexture", new[] { "Assets/Cypress" }))
        {
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(AssetDatabase.GUIDToAssetPath(guid));
            if (rt == null || rt.dimension != TextureDimension.Tex2D) continue;
            rt.Release();
            rt.useMipMap = true;
            rt.autoGenerateMips = true;
            rt.filterMode = FilterMode.Trilinear;
            EditorUtility.SetDirty(rt);
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"Cypress: smoother image - MSAA 4x, SMAA on {cams} camera(s), trilinear + coverage-preserving mips " +
                  "on the plant textures, mip-mapped dome master. Note: the Traditional (cubemap) dome mode renders " +
                  "its faces without anti-aliasing; Dome Warp mode gets the full benefit.");
    }
}
