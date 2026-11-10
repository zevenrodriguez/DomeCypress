using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Uses the 'mothsphere' (where the orchids open and the moths fly) as the centre of attention for
/// level of detail: everything in and near the sphere keeps full detail and shadows, the rest of the
/// swamp gets cheaper the further it is from the sphere.
///   Tools > Cypress > Performance > Focus Detail On Moth Sphere
///   Tools > Cypress > Performance > Clear Moth Sphere Focus
/// Run Optimize Geometry (chunks + LODs) first - this works on its chunks.
///
/// Zones, measured from the edge of the sphere:
///   inside / within Near Band   full detail, shadows as before
///   up to Far Band              shadows off
///   beyond Far Band             shadows off and the simplest Mesh LOD
/// Small detail (ferns, litter, air plants...) is hidden beyond Detail Distance, knees and logs
/// beyond Small Distance (both measured from the sphere's edge).
/// </summary>
static class CypressFocusDetail
{
    const string SphereName = "mothsphere";
    const float NearBand = 4f;
    const float FarBand = 15f;
    const float DetailDistance = 8f;
    const float SmallDistance = 18f;

    [MenuItem("Tools/Cypress/Performance/Focus Detail On Moth Sphere")]
    static void Focus()
    {
        var scene = SceneManager.GetActiveScene();
        var sphere = FindSphere(scene);
        if (sphere == null) { Debug.LogWarning($"Cypress: no '{SphereName}' object in the open scene."); return; }
        var containers = Containers(scene);
        if (containers.Length == 0)
        {
            Debug.LogWarning("Cypress: run Tools > Cypress > Performance > Optimize Geometry (chunks + LODs) first - the focus works on its chunks.");
            return;
        }

        int full = 0, noShadow = 0, lowest = 0;
        foreach (var container in containers)
        {
            string layer = CypressGeometryOptimizer.LayerName(container.name.Replace(CypressGeometryOptimizer.ContainerSuffix, ""));
            bool canShadow = !CypressGeometryOptimizer.Detail.Contains(layer) && !CypressGeometryOptimizer.NoShadows.Contains(layer);
            foreach (var r in container.GetComponentsInChildren<MeshRenderer>(true))
            {
                float d = CypressDistanceCull.DistanceOutside(sphere, r.bounds);
                Undo.RecordObject(r, "Focus detail");
                if (d <= NearBand)
                {
                    r.shadowCastingMode = canShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    SetForcedLod(r, -1);
                    full++;
                }
                else
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    int simplest = d > FarBand ? LowestLod(r) : 0;
                    if (simplest > 0 && SetForcedLod(r, simplest)) lowest++;
                    else SetForcedLod(r, -1);
                    noShadow++;
                }
            }
        }

        // detail and knees/logs are hidden by distance from the sphere's edge instead of from the viewer
        SetCuller(scene, "Cull: small detail", sphere, DetailDistance);
        SetCuller(scene, "Cull: knees and logs", sphere, SmallDistance);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Cypress: detail focused on '{SphereName}' - {full} chunks at full detail, {noShadow} without shadows " +
                  $"({lowest} of them on their simplest Mesh LOD). Small detail hidden beyond {DetailDistance} m and " +
                  $"knees/logs beyond {SmallDistance} m of the sphere. Moving or resizing the sphere? Run this again.");
    }

    [MenuItem("Tools/Cypress/Performance/Clear Moth Sphere Focus")]
    static void Clear()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var container in Containers(scene))
        {
            string layer = CypressGeometryOptimizer.LayerName(container.name.Replace(CypressGeometryOptimizer.ContainerSuffix, ""));
            bool canShadow = !CypressGeometryOptimizer.Detail.Contains(layer) && !CypressGeometryOptimizer.NoShadows.Contains(layer);
            foreach (var r in container.GetComponentsInChildren<MeshRenderer>(true))
            {
                Undo.RecordObject(r, "Clear focus");
                r.shadowCastingMode = canShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                SetForcedLod(r, -1);
            }
        }
        SetCuller(scene, "Cull: small detail", null, 35f);
        SetCuller(scene, "Cull: knees and logs", null, 55f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Cypress: moth sphere focus cleared - back to distance from the viewer.");
    }

    // ------------------------------------------------------------------ helpers
    static Transform FindSphere(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == SphereName) return t;
        return null;
    }

    /// <summary>The "... (chunks)" objects made by Optimize Geometry (at the root or under a culler).</summary>
    static Transform[] Containers(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name.EndsWith(CypressGeometryOptimizer.ContainerSuffix))
            .ToArray();
    }

    static void SetCuller(Scene scene, string name, Transform focus, float distance)
    {
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (go == null) return;
        var cull = go.GetComponent<CypressDistanceCull>();
        if (cull == null) return;
        Undo.RecordObject(cull, "Focus detail");
        cull.focusArea = focus;
        cull.cullDistance = distance;
    }

    /// <summary>Forces a Mesh LOD on the renderer (-1 = let Unity choose). Returns false if not supported.</summary>
    static bool SetForcedLod(Renderer r, int lod)
    {
        var so = new SerializedObject(r);
        var prop = so.FindProperty("m_ForceMeshLod");
        if (prop == null) return false;
        prop.intValue = lod;
        so.ApplyModifiedPropertiesWithoutUndo();
        return lod >= 0;
    }

    static PropertyInfo _lodCount;

    /// <summary>The simplest Mesh LOD this chunk has (0 if it has none).</summary>
    static int LowestLod(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 0;
        var mesh = mf.sharedMesh;
        if (_lodCount == null) _lodCount = typeof(Mesh).GetProperty("lodCount", BindingFlags.Public | BindingFlags.Instance);
        if (_lodCount == null) return 0;
        int count = System.Convert.ToInt32(_lodCount.GetValue(mesh));
        return Mathf.Max(0, count - 1);
    }
}
