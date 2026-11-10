using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Fixes the Render Graph error "the output Render Texture must have a depth buffer".
/// The pfc Dome Tools package ships "DomeMaster (render texture)" with Depth Stencil Format = None,
/// and package files can't be edited (Unity restores them). This makes a copy of every camera
/// target texture that lacks depth inside Assets/Cypress/Dome/RenderTextures (same size and colour
/// format, plus a D24S8 depth buffer) and swaps every reference to it in the open scenes -
/// cameras, the Dome Renderer, canvases, raw images, materials - over to the copy.
/// Menu: Tools > Cypress > Dome > Fix Render Texture Depth (open scenes)
/// </summary>
static class CypressRenderTextureDepthFix
{
    const string Folder = "Assets/Cypress/Dome/RenderTextures";

    [MenuItem("Tools/Cypress/Dome/Fix Render Texture Depth (open scenes)")]
    static void FixMenu()
    {
        int swapped = Fix(out var created);
        if (created.Count == 0)
            Debug.Log("Cypress: every camera Output Texture in the open scenes already has a depth buffer.");
        else
            Debug.Log($"Cypress: gave {created.Count} render texture(s) a depth buffer ({string.Join(", ", created)}); " +
                      $"updated {swapped} reference(s). Save the scene(s).");
    }

    /// <summary>Returns the number of references swapped; `created` lists the textures that got a depth copy.</summary>
    public static int Fix(out List<string> created)
    {
        created = new List<string>();
        // 1. find camera targets without depth
        var replace = new Dictionary<RenderTexture, RenderTexture>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                var rt = cam.targetTexture;
                if (rt == null || replace.ContainsKey(rt)) continue;
                if (rt.depthStencilFormat != GraphicsFormat.None) continue;
                if (!EditorUtility.IsPersistent(rt)) continue;   // runtime-created textures are the script's job
                replace[rt] = GetOrCreateDepthCopy(rt);
                created.Add(rt.name);
            }
        }
        if (replace.Count == 0) return 0;

        // 2. swap every serialized reference in the open scenes (cameras, DomeRenderer, RawImage, canvases...)
        int count = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            bool dirty = false;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (it.objectReferenceValue is RenderTexture old && replace.TryGetValue(old, out var neu))
                    {
                        it.objectReferenceValue = neu;
                        changed = true; count++;
                    }
                }
                if (changed) { so.ApplyModifiedProperties(); dirty = true; }

                // materials used by renderers / UI that sample the old texture
                if (comp is Renderer r)
                {
                    var mats = r.sharedMaterials;
                    bool any = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var fixedMat = SwapInMaterial(mats[i], replace, ref count);
                        if (fixedMat != mats[i]) { mats[i] = fixedMat; any = true; }
                    }
                    if (any) { Undo.RecordObject(r, "Swap dome material"); r.sharedMaterials = mats; dirty = true; }
                }
                if (comp is UnityEngine.UI.Graphic g && g.material != null && g.material != g.defaultMaterial)
                {
                    var fixedMat = SwapInMaterial(g.material, replace, ref count);
                    if (fixedMat != g.material) { Undo.RecordObject(g, "Swap dome material"); g.material = fixedMat; dirty = true; }
                }
            }
            if (dirty) EditorSceneManager.MarkSceneDirty(scene);
        }
        AssetDatabase.SaveAssets();
        return count;
    }

    /// <summary>
    /// Points a material's texture slots at the depth copies. Project materials are edited in place;
    /// package materials are read-only, so a copy is saved next to the textures and returned instead.
    /// </summary>
    static Material SwapInMaterial(Material m, Dictionary<RenderTexture, RenderTexture> replace, ref int count)
    {
        if (m == null || !EditorUtility.IsPersistent(m)) return m;
        var hits = new List<string>();
        foreach (var name in m.GetTexturePropertyNames())
            if (m.GetTexture(name) is RenderTexture old && replace.ContainsKey(old)) hits.Add(name);
        if (hits.Count == 0) return m;

        var target = m;
        if (!AssetDatabase.GetAssetPath(m).StartsWith("Assets/"))
        {
            string copyPath = $"{Folder}/{m.name} (depth).mat";
            target = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
            if (target == null)
            {
                target = new Material(m) { name = Path.GetFileNameWithoutExtension(copyPath) };
                AssetDatabase.CreateAsset(target, copyPath);
            }
        }
        foreach (var name in hits)
        {
            target.SetTexture(name, replace[(RenderTexture)m.GetTexture(name)]);
            count++;
        }
        EditorUtility.SetDirty(target);
        return target;
    }

    static RenderTexture GetOrCreateDepthCopy(RenderTexture src)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Cypress/Dome")) AssetDatabase.CreateFolder("Assets/Cypress", "Dome");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Cypress/Dome", "RenderTextures");
        string path = $"{Folder}/{src.name} (depth).renderTexture";
        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (existing != null) return existing;

        var desc = src.descriptor;
        desc.depthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt;
        var rt = new RenderTexture(desc)
        {
            name = Path.GetFileNameWithoutExtension(path),
            filterMode = src.filterMode,
            wrapMode = src.wrapMode,
            anisoLevel = src.anisoLevel,
        };
        AssetDatabase.CreateAsset(rt, path);
        return rt;
    }
}
