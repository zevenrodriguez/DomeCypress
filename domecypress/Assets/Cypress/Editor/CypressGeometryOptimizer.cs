using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Geometry optimisation for the cypress dome.
///   Tools > Cypress > Performance > Optimize Geometry (chunks + LODs)
///   Tools > Cypress > Performance > Undo Geometry Optimization
///
/// Every layer of the swamp was exported as ONE big mesh that covers the whole scene (all the
/// trunks in one mesh, all the air plants in another...). Unity can't skip or simplify part of a
/// mesh, so all ~1.9 million triangles were drawn every frame - and again for the shadows.
/// This tool:
///   1. cuts each big mesh into 12 m x 12 m chunks (so off-screen chunks are skipped),
///   2. gives each chunk Mesh LODs (simpler versions Unity switches to when it's small on screen),
///   3. hides small detail (ferns, litter, air plants...) beyond a distance from the viewer,
///   4. stops grass, litter and small plants casting shadows.
/// The original objects are only switched off, so Undo puts everything back.
/// </summary>
static class CypressGeometryOptimizer
{
    const string ChunkFolder = "Assets/Cypress/Models/Chunks";
    internal const string ContainerSuffix = " (chunks)";
    const float CellSize = 12f;
    const int MinTrianglesToSplit = 20000;

    // what kind of object each exported layer is (the name without "Cypress_")
    internal static readonly HashSet<string> Detail = new HashSet<string>
    {
        "AirPlants", "Ferns", "FernCards", "FloatingLeaves", "FloatingLitter", "SunkenLitter",
        "Sticks", "SpanishMoss", "Shrubs", "Pickerelweed", "ShortGrass", "Vines",
    };
    internal static readonly HashSet<string> Small = new HashSet<string> { "Knees", "KneesExtra", "Logs", "SawgrassTufts" };
    // flat alpha cards (leaves, litter, moss): simplifying would collapse the cards, so no Mesh LODs
    internal static readonly HashSet<string> Cards = new HashSet<string>
    {
        "LeafCards", "CanopyExtra", "YoungFoliage", "FernCards", "Ferns", "FloatingLeaves", "FloatingLitter",
        "SunkenLitter", "SpanishMoss", "Shrubs", "Pickerelweed",
    };
    internal static readonly HashSet<string> NoShadows = new HashSet<string> { "Ground", "Sawgrass", "SawgrassTufts" };

    const float DetailDistance = 35f;   // ferns, litter... hidden beyond this (meters from the viewer)
    const float SmallDistance = 55f;    // knees, logs...

    enum Kind { Main, Small, Detail }

    static Kind KindOf(string layer) => Detail.Contains(layer) ? Kind.Detail : Small.Contains(layer) ? Kind.Small : Kind.Main;

    [MenuItem("Tools/Cypress/Performance/Optimize Geometry (chunks + LODs)")]
    static void Optimize()
    {
        var scene = SceneManager.GetActiveScene();
        var report = new List<string>();
        int before = 0, chunkCount = 0, lodMeshes = 0;
        bool lodApi = FindMeshLodMethod() != null;

        var renderers = FindSwampRenderers(scene);
        if (renderers.Count == 0) { Debug.LogWarning("Cypress: no swamp meshes found in the open scene (open CypressDome first)."); return; }

        var detailRoot = GetOrCreateCuller(scene, "Cull: small detail", DetailDistance);
        var smallRoot = GetOrCreateCuller(scene, "Cull: knees and logs", SmallDistance);

        try
        {
            for (int n = 0; n < renderers.Count; n++)
            {
                var r = renderers[n];
                var mf = r.GetComponent<MeshFilter>();
                string layer = LayerName(r.gameObject.name);
                var kind = KindOf(layer);
                EditorUtility.DisplayProgressBar("Optimizing cypress geometry", layer, n / (float)renderers.Count);

                var mesh = EnsureReadable(mf);
                if (mesh == null) continue;
                int tris = TriangleCount(mesh);
                before += tris;
                bool castShadows = kind != Kind.Detail && !NoShadows.Contains(layer) && r.shadowCastingMode != ShadowCastingMode.Off;

                if (tris < MinTrianglesToSplit)
                {
                    // small enough to leave whole - just apply the shadow and distance settings
                    Undo.RecordObject(r, "Cypress optimize");
                    if (!castShadows) r.shadowCastingMode = ShadowCastingMode.Off;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                    if (kind != Kind.Main) report.Add($"{layer}: {tris:N0} tris (kept whole, culled by distance)");
                    if (kind != Kind.Main) AddProxyToCuller(kind == Kind.Detail ? detailRoot : smallRoot, r);
                    continue;
                }

                // cut into chunks; the chunks sit under a container next to the original object
                var container = new GameObject(r.gameObject.name + ContainerSuffix);
                Undo.RegisterCreatedObjectUndo(container, "Cypress optimize");
                var parent = kind == Kind.Detail ? detailRoot : kind == Kind.Small ? smallRoot : null;
                if (parent != null) container.transform.SetParent(parent, false);
                else SceneManager.MoveGameObjectToScene(container, scene);
                container.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                container.transform.localScale = r.transform.lossyScale;

                var chunks = Split(mesh, r.transform.localToWorldMatrix);
                foreach (var (cell, chunkMesh) in chunks)
                {
                    chunkMesh.name = $"{r.gameObject.name}_{cell.x}_{cell.y}";
                    if (!Cards.Contains(layer) && TryGenerateMeshLods(chunkMesh)) lodMeshes++;
                    SaveMesh(chunkMesh);

                    var go = new GameObject(chunkMesh.name);
                    go.transform.SetParent(container.transform, false);
                    go.layer = r.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = chunkMesh;
                    var cr = go.AddComponent<MeshRenderer>();
                    cr.sharedMaterials = r.sharedMaterials;
                    cr.shadowCastingMode = castShadows ? r.shadowCastingMode : ShadowCastingMode.Off;
                    cr.receiveShadows = r.receiveShadows;
                    cr.lightProbeUsage = r.lightProbeUsage;
                    cr.reflectionProbeUsage = r.reflectionProbeUsage;
                    chunkCount++;
                }

                // switch the original off (kept so Undo can bring it back)
                Undo.RecordObject(r, "Cypress optimize");
                r.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                report.Add($"{layer}: {tris:N0} tris -> {chunks.Count} chunks" +
                           (kind == Kind.Detail ? $", hidden beyond {DetailDistance} m" :
                            kind == Kind.Small ? $", hidden beyond {SmallDistance} m" : "") +
                           (castShadows ? "" : ", no shadows"));
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Cypress: geometry optimized - {before:N0} triangles in {renderers.Count} meshes, now {chunkCount} chunks.\n  " +
                  string.Join("\n  ", report) + "\n" +
                  (lodApi ? $"Mesh LODs generated for {lodMeshes} chunks."
                          : "Mesh LOD generation isn't available in this Unity version - chunks and distance culling still apply.") +
                  "\nUndo with Tools > Cypress > Performance > Undo Geometry Optimization.");
    }

    [MenuItem("Tools/Cypress/Performance/Undo Geometry Optimization")]
    static void Revert()
    {
        var scene = SceneManager.GetActiveScene();
        // remove the chunk objects and the cullers
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name.StartsWith("Cull: ")) { Undo.DestroyObjectImmediate(root); continue; }
            if (root.name.EndsWith(ContainerSuffix)) Undo.DestroyObjectImmediate(root);
        }
        // switch the original meshes back on (shadow settings: reset the prefab overrides by hand if wanted)
        foreach (var r in FindSwampRenderers(scene, includeDisabled: true))
        {
            if (r.enabled) continue;
            Undo.RecordObject(r, "Cypress undo optimize");
            r.enabled = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        if (AssetDatabase.IsValidFolder(ChunkFolder)) AssetDatabase.DeleteAsset(ChunkFolder);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Cypress: geometry optimization undone - the original meshes are back.");
    }

    // ------------------------------------------------------------------ finding the swamp meshes
    /// <summary>Mesh renderers in the scene that use the exported swamp models (not the grass LODs).</summary>
    static List<MeshRenderer> FindSwampRenderers(Scene scene, bool includeDisabled = false)
    {
        var list = new List<MeshRenderer>();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (!path.StartsWith("Assets/Cypress/Models/Cypress") || path.Contains("CypressGrass")) continue;
            if (!r.gameObject.name.StartsWith("Cypress_")) continue;
            if (!includeDisabled && (!r.enabled || !r.gameObject.activeInHierarchy)) continue;
            list.Add(r);
        }
        return list;
    }

    internal static string LayerName(string objectName)
    {
        string n = objectName.StartsWith("Cypress_") ? objectName.Substring(8) : objectName;
        int dot = n.IndexOf('.');
        return dot > 0 ? n.Substring(0, dot) : n;
    }

    static Transform GetOrCreateCuller(Scene scene, string name, float distance)
    {
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (go == null)
        {
            go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Cypress optimize");
        }
        var cull = go.GetComponent<CypressDistanceCull>();
        if (cull == null) cull = go.AddComponent<CypressDistanceCull>();
        cull.cullDistance = distance;
        return go.transform;
    }

    /// <summary>A detail mesh kept whole: copy it under the culler (the original is inside the FBX instance and can't move).</summary>
    static void AddProxyToCuller(Transform culler, MeshRenderer r)
    {
        var copy = new GameObject(r.gameObject.name + ContainerSuffix);
        Undo.RegisterCreatedObjectUndo(copy, "Cypress optimize");
        copy.transform.SetParent(culler, false);
        copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
        copy.transform.localScale = r.transform.lossyScale;
        copy.AddComponent<MeshFilter>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
        var cr = copy.AddComponent<MeshRenderer>();
        cr.sharedMaterials = r.sharedMaterials;
        cr.shadowCastingMode = r.shadowCastingMode;
        cr.receiveShadows = r.receiveShadows;
        Undo.RecordObject(r, "Cypress optimize");
        r.enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(r);
    }

    // ------------------------------------------------------------------ mesh helpers
    static int TriangleCount(Mesh m)
    {
        long n = 0;
        for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
        return (int)n;
    }

    /// <summary>The meshes must be readable to cut them up; switch Read/Write on in the model importer if needed.</summary>
    static Mesh EnsureReadable(MeshFilter mf)
    {
        var mesh = mf.sharedMesh;
        if (mesh == null || mesh.isReadable) return mesh;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
        {
            mi.isReadable = true;
            mi.SaveAndReimport();
        }
        return mf.sharedMesh;
    }

    /// <summary>Cuts a mesh into square cells (by where each triangle's centre lands in the world).</summary>
    static List<(Vector2Int cell, Mesh mesh)> Split(Mesh src, Matrix4x4 toWorld)
    {
        var pos = src.vertices;
        var nrm = src.normals;
        var tan = src.tangents;
        var col = src.colors32;
        var uvs = new List<Vector4>[4];
        for (int c = 0; c < 4; c++)
        {
            if (!src.HasVertexAttribute(VertexAttribute.TexCoord0 + c)) continue;
            uvs[c] = new List<Vector4>();
            src.GetUVs(c, uvs[c]);
        }

        int subCount = src.subMeshCount;
        var buckets = new Dictionary<Vector2Int, List<int>[]>();
        for (int s = 0; s < subCount; s++)
        {
            if (src.GetTopology(s) != MeshTopology.Triangles) continue;
            var idx = src.GetTriangles(s);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                Vector3 centre = toWorld.MultiplyPoint3x4((pos[idx[t]] + pos[idx[t + 1]] + pos[idx[t + 2]]) / 3f);
                var key = new Vector2Int(Mathf.FloorToInt(centre.x / CellSize), Mathf.FloorToInt(centre.z / CellSize));
                if (!buckets.TryGetValue(key, out var lists))
                {
                    lists = new List<int>[subCount];
                    for (int k = 0; k < subCount; k++) lists[k] = new List<int>();
                    buckets[key] = lists;
                }
                lists[s].Add(idx[t]); lists[s].Add(idx[t + 1]); lists[s].Add(idx[t + 2]);
            }
        }

        var result = new List<(Vector2Int, Mesh)>();
        foreach (var kv in buckets)
        {
            var map = new Dictionary<int, int>();
            var vPos = new List<Vector3>(); var vNrm = new List<Vector3>(); var vTan = new List<Vector4>(); var vCol = new List<Color32>();
            var vUv = new List<Vector4>[4];
            for (int c = 0; c < 4; c++) if (uvs[c] != null) vUv[c] = new List<Vector4>();
            var newTris = new List<int>[subCount];
            for (int s = 0; s < subCount; s++)
            {
                newTris[s] = new List<int>(kv.Value[s].Count);
                foreach (int old in kv.Value[s])
                {
                    if (!map.TryGetValue(old, out int ni))
                    {
                        ni = vPos.Count; map[old] = ni;
                        vPos.Add(pos[old]);
                        if (nrm.Length > 0) vNrm.Add(nrm[old]);
                        if (tan.Length > 0) vTan.Add(tan[old]);
                        if (col.Length > 0) vCol.Add(col[old]);
                        for (int c = 0; c < 4; c++) if (vUv[c] != null) vUv[c].Add(uvs[c][old]);
                    }
                    newTris[s].Add(ni);
                }
            }
            var m = new Mesh();
            if (vPos.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(vPos);
            if (vNrm.Count > 0) m.SetNormals(vNrm);
            if (vTan.Count > 0) m.SetTangents(vTan);
            if (vCol.Count > 0) m.SetColors(vCol);
            for (int c = 0; c < 4; c++) if (vUv[c] != null) m.SetUVs(c, vUv[c]);
            m.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++) m.SetTriangles(newTris[s], s, false);
            m.RecalculateBounds();
            m.UploadMeshData(false);
            result.Add((kv.Key, m));
        }
        return result;
    }

    static void SaveMesh(Mesh m)
    {
        if (!AssetDatabase.IsValidFolder(ChunkFolder)) AssetDatabase.CreateFolder("Assets/Cypress/Models", "Chunks");
        string path = $"{ChunkFolder}/{m.name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    // ------------------------------------------------------------------ Mesh LOD (Unity 6.2+)
    static MethodInfo _meshLodMethod;
    static bool _searched;

    /// <summary>Unity 6.2+ can build Mesh LODs inside a mesh (MeshLodUtility.GenerateMeshLods). Looked up by name so
    /// this file still compiles on versions without it.</summary>
    static MethodInfo FindMeshLodMethod()
    {
        if (_searched) return _meshLodMethod;
        _searched = true;
        // look in the Unity editor's own assemblies (where MeshLodUtility lives in Unity 6.2+)
        foreach (var asm in new[] { typeof(ModelImporter).Assembly, typeof(Editor).Assembly })
        {
            var t = asm.GetType("UnityEditor.MeshLodUtility");
            if (t == null) continue;
            _meshLodMethod = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(mi => mi.Name == "GenerateMeshLods" && mi.GetParameters().Length > 0
                                      && mi.GetParameters()[0].ParameterType == typeof(Mesh));
            break;
        }
        return _meshLodMethod;
    }

    static bool TryGenerateMeshLods(Mesh mesh)
    {
        var method = FindMeshLodMethod();
        if (method == null) return false;
        var ps = method.GetParameters();
        var args = new object[ps.Length];
        args[0] = mesh;
        for (int i = 1; i < ps.Length; i++)
        {
            if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
            else if (ps[i].ParameterType == typeof(int)) args[i] = -1;   // no limit on the number of LODs
            else args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
        }
        try { method.Invoke(null, args); return true; }
        catch (Exception e)
        {
            Debug.LogWarning($"Cypress: Mesh LOD generation failed for {mesh.name}: {(e.InnerException ?? e).Message}");
            return false;
        }
    }
}
