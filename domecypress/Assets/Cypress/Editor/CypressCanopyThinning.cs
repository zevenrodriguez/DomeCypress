using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Two performance tools for the cypress dome.
///
/// Tools > Cypress > Performance > Thin Distant Canopy
///   The leaf cards of the outer trees are mostly hidden behind the nearer canopy, but every one of them is
///   still drawn (5 times per frame in cubemap mode) and the overlapping see-through leaves are the most
///   expensive thing on the GPU. This keeps every leaf card near the viewer, keeps 60% of them at the
///   middle distance and 35% far out, and makes the remaining far cards a little bigger so the canopy
///   still reads as full. The original chunk meshes are kept: Restore Full Canopy puts them back.
///
/// Tools > Cypress > Performance > Remove Wasted Camera Work
///   Turns anti-aliasing off on cameras that don't draw the scene: the camera that copies the dome image
///   onto the 4096 dome master (UI only) and the black Screen Output Camera.
/// Run Optimize Geometry (chunks + LODs) first - the thinning works on its chunks.
/// </summary>
static class CypressCanopyThinning
{
    // leaf-card layers (flat alpha cards in the exported models)
    static readonly HashSet<string> CardLayers = new HashSet<string> { "LeafCards", "CanopyExtra", "YoungFoliage", "SpanishMoss" };

    const float NearDistance = 12f;   // meters from the viewer: all cards kept
    const float FarDistance = 24f;    // beyond this: FarKeep
    const float MidKeep = 0.6f;
    const float FarKeep = 0.35f;
    const string ThinSuffix = "_thin";

    [MenuItem("Tools/Cypress/Performance/Thin Distant Canopy")]
    static void Thin()
    {
        var scene = SceneManager.GetActiveScene();
        var viewer = FindViewer(scene);
        int thinned = 0, before = 0, after = 0;

        foreach (var mf in CardChunks(scene))
        {
            var r = mf.GetComponent<MeshRenderer>();
            var original = OriginalMesh(mf.sharedMesh);
            if (original == null || r == null) continue;
            float d = Mathf.Sqrt(r.bounds.SqrDistance(viewer));
            float keep = d <= NearDistance ? 1f : d <= FarDistance ? MidKeep : FarKeep;

            Undo.RecordObject(mf, "Thin canopy");
            if (keep >= 1f) { mf.sharedMesh = original; continue; }

            var thin = ThinCards(original, keep);
            before += (int)(original.GetIndexCount(0) / 3);
            after += (int)(thin.GetIndexCount(0) / 3);
            string path = AssetDatabase.GetAssetPath(original).Replace(".asset", ThinSuffix + ".asset");
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(thin, path);
            mf.sharedMesh = thin;
            thinned++;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(thinned == 0
            ? "Cypress: no leaf-card chunks to thin - run Optimize Geometry (chunks + LODs) first."
            : $"Cypress: thinned {thinned} distant canopy chunks: {before:N0} -> {after:N0} triangles. " +
              "Undo with Tools > Cypress > Performance > Restore Full Canopy.");
    }

    [MenuItem("Tools/Cypress/Performance/Restore Full Canopy")]
    static void Restore()
    {
        var scene = SceneManager.GetActiveScene();
        int n = 0;
        foreach (var mf in CardChunks(scene))
        {
            var original = OriginalMesh(mf.sharedMesh);
            if (original == null || original == mf.sharedMesh) continue;
            Undo.RecordObject(mf, "Restore canopy");
            mf.sharedMesh = original;
            n++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Cypress: restored the full canopy on {n} chunks.");
    }

    [MenuItem("Tools/Cypress/Performance/Remove Wasted Camera Work")]
    static void Cameras()
    {
        int n = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                // only UI (the dome master copy) or nothing at all (the black screen camera)
                bool notScene = cam.cullingMask == 0 || cam.cullingMask == 1 << 5;
                if (!notScene) continue;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) continue;
                Undo.RecordObject(data, "Camera work");
                data.antialiasing = AntialiasingMode.None;
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.requiresColorOption = CameraOverrideOption.Off;
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                n++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log($"Cypress: switched off anti-aliasing, post-processing, shadows and depth/colour copies on {n} camera(s) that don't draw the scene.");
    }

    // ------------------------------------------------------------------ helpers
    static Vector3 FindViewer(Scene scene)
    {
        var anchor = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Viewer Anchor");
        return anchor != null ? anchor.transform.position : Vector3.zero;
    }

    /// <summary>MeshFilters of the leaf-card chunks made by Optimize Geometry.</summary>
    static IEnumerable<MeshFilter> CardChunks(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.EndsWith(CypressGeometryOptimizer.ContainerSuffix)) continue;
            string layer = CypressGeometryOptimizer.LayerName(t.name.Replace(CypressGeometryOptimizer.ContainerSuffix, ""));
            if (!CardLayers.Contains(layer)) continue;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) yield return mf;
        }
    }

    /// <summary>The un-thinned chunk mesh (the thinned copy sits next to it with "_thin" on the end).</summary>
    static Mesh OriginalMesh(Mesh m)
    {
        if (m == null) return null;
        string path = AssetDatabase.GetAssetPath(m);
        if (!path.EndsWith(ThinSuffix + ".asset")) return m;
        return AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace(ThinSuffix + ".asset", ".asset"));
    }

    /// <summary>
    /// Keeps a share of the leaf cards (each card = a group of triangles sharing vertices) and scales the
    /// kept ones up around their centre so the canopy stays about as full.
    /// </summary>
    static Mesh ThinCards(Mesh src, float keep)
    {
        var mesh = Object.Instantiate(src);
        mesh.name = src.name + ThinSuffix;
        var verts = src.vertices;
        int subCount = src.subMeshCount;

        // group triangles into cards: triangles that share a vertex belong together
        var parent = Enumerable.Range(0, verts.Length).ToArray();
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        var allTris = new List<int[]>();
        for (int s = 0; s < subCount; s++)
        {
            var tri = src.GetTriangles(s);
            allTris.Add(tri);
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                int a = Find(tri[i]);
                parent[Find(tri[i + 1])] = a;
                parent[Find(tri[i + 2])] = a;
            }
        }

        // decide per card (the same random choice for a card every time, so re-running gives the same result)
        var keepCard = new Dictionary<int, bool>();
        bool Kept(int root)
        {
            if (!keepCard.TryGetValue(root, out bool k))
            {
                k = (((uint)root * 2654435761u) % 1000u) / 1000f < keep;
                keepCard[root] = k;
            }
            return k;
        }

        // grow the kept cards a little to fill the gaps (area grows with the square of the scale)
        float grow = Mathf.Min(1.5f, 1f / Mathf.Sqrt(keep)) * 0.85f + 0.15f;
        var sum = new Dictionary<int, Vector3>();
        var count = new Dictionary<int, int>();
        for (int v = 0; v < verts.Length; v++)
        {
            int root = Find(v);
            sum[root] = (sum.TryGetValue(root, out var p) ? p : Vector3.zero) + verts[v];
            count[root] = (count.TryGetValue(root, out int c) ? c : 0) + 1;
        }
        var newVerts = (Vector3[])verts.Clone();
        for (int v = 0; v < verts.Length; v++)
        {
            int root = Find(v);
            if (!Kept(root)) continue;
            Vector3 centre = sum[root] / count[root];
            newVerts[v] = centre + (verts[v] - centre) * grow;
        }
        mesh.vertices = newVerts;

        for (int s = 0; s < subCount; s++)
        {
            var tri = allTris[s];
            var kept = new List<int>(tri.Length);
            for (int i = 0; i + 2 < tri.Length; i += 3)
                if (Kept(Find(tri[i]))) { kept.Add(tri[i]); kept.Add(tri[i + 1]); kept.Add(tri[i + 2]); }
            mesh.SetTriangles(kept, s, false);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}
