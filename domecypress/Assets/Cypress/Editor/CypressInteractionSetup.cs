using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up the space-bar ghost orchid / sphinx moth interaction in the cypress dome scene:
///  - "Viewer Anchor": the centre of the dome (starts at the Preview Camera spot; move it freely),
///  - the pfc Dome Tools "Dome Creator" rig as its child (same rig as the title scene),
///  - "Orchid Moth Interaction" with OrchidMothExperience wired to the moths, orchid and tree data,
///  - tree_anchors.json: every trunk's centre/radius by height, read from the imported trunk meshes.
/// Menu: Tools > Cypress > Interaction > ...
/// </summary>
static class CypressInteractionSetup
{
    const string AnchorName = "Viewer Anchor";
    const string InteractionName = "Orchid Moth Interaction";
    const string DomeCreatorPrefab = "Packages/com.pfc.dome-tools/Runtime/Dome Creator.prefab";
    const string TreeAnchorsPath = CypressAssets.Root + "/tree_anchors.json";
    const string CocytiusModel = "Assets/Cocytius/Cocytius_Antaeus.fbx";
    const string CocytiusTexture = "Assets/Cocytius/Cocytius_antaeus.png";
    const string FlyClip = "Assets/Cocytius/fly.anim";
    const string PachyliaFolder = "Assets/PachyliaFicus";
    const string PachyliaModel = PachyliaFolder + "/PachyliaFicus.fbx";
    const string OrchidFolder = "Assets/OrchidRoots/Resources/OrchidRoots";

    [MenuItem("Tools/Cypress/Interaction/Add Dome Rig + Orchid Moth Interaction")]
    static void Setup()
    {
        var scene = OpenCypressScene();
        if (!scene.IsValid()) return;
        var roots = scene.GetRootGameObjects();
        var dome = roots.FirstOrDefault(g => g.name == "CypressDome");
        if (dome == null) { Debug.LogError("Cypress: build the dome first (Tools > Cypress > Build Cypress Dome Scene)."); return; }
        var preview = roots.FirstOrDefault(g => g.name == "Preview Camera");

        // --- viewer anchor (centre of the dome image)
        var anchorGo = roots.FirstOrDefault(g => g.name == AnchorName);
        if (anchorGo == null)
        {
            anchorGo = new GameObject(AnchorName);
            SceneManager.MoveGameObjectToScene(anchorGo, scene);
            Undo.RegisterCreatedObjectUndo(anchorGo, "Create Viewer Anchor");
            if (preview != null)
            {
                anchorGo.transform.position = preview.transform.position;
                var fwd = preview.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-4f) anchorGo.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }
        }
        var anchor = anchorGo.transform;

        // --- Dome Creator rig (pfc Dome Tools), like the title scene
        if (anchor.Find("Dome Creator") == null && !roots.Any(g => g.name == "Dome Creator"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DomeCreatorPrefab);
            if (prefab != null)
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                rig.name = "Dome Creator";
                rig.transform.SetParent(anchor, false);
                rig.transform.localPosition = Vector3.zero;
                rig.transform.localRotation = Quaternion.identity;
                Undo.RegisterCreatedObjectUndo(rig, "Add Dome Creator");
            }
            else Debug.LogWarning($"Cypress: {DomeCreatorPrefab} not found - is the pfc Dome Tools package installed? Add the Dome Creator under '{AnchorName}' by hand.");
        }
        // the dome rig renders the show now; keep the flat preview camera but switch it off
        if (preview != null && preview.activeSelf)
        {
            Undo.RecordObject(preview, "Disable Preview Camera");
            preview.SetActive(false);
        }

        // --- tree data
        var treeData = BuildTreeAnchors(dome);

        // --- interaction object
        var go = roots.FirstOrDefault(g => g.name == InteractionName);
        if (go == null)
        {
            go = new GameObject(InteractionName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create Orchid Moth Interaction");
        }
        var exp = go.GetComponent<OrchidMothExperience>();
        if (exp == null) exp = go.AddComponent<OrchidMothExperience>();
        exp.anchor = anchor;
        exp.environmentRoot = dome.transform;
        exp.treeData = treeData;
        exp.flyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(FlyClip);
        exp.orchidModel = AssetDatabase.LoadAssetAtPath<GameObject>(OrchidFolder + "/GhostOrchid.fbx");
        exp.orchidBaseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(OrchidFolder + "/GhostOrchid_BaseColor.png");
        exp.orchidNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(OrchidFolder + "/GhostOrchid_Normal.png");

        var cocytiusTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CocytiusTexture);
        var pachyliaTex = FindTexture(PachyliaFolder);
        exp.moths = new[]
        {
            new OrchidMothExperience.MothSpecies
            {
                name = "Cocytius antaeus",
                model = AssetDatabase.LoadAssetAtPath<GameObject>(CocytiusModel),
                texture = cocytiusTex,
                tint = Color.white,
            },
            new OrchidMothExperience.MothSpecies
            {
                name = "Pachylia ficus",
                model = AssetDatabase.LoadAssetAtPath<GameObject>(PachyliaModel),
                // no Pachylia texture yet: reuse the Cocytius one, warmed to the fig sphinx's tawny brown
                texture = pachyliaTex != null ? pachyliaTex : cocytiusTex,
                tint = pachyliaTex != null ? Color.white : new Color(0.86f, 0.70f, 0.55f),
            },
        };
        EditorUtility.SetDirty(exp);
        if (exp.moths.Any(m => m.model == null)) Debug.LogWarning("Cypress: a moth model wasn't found - check the Moths list on 'Orchid Moth Interaction'.");
        if (pachyliaTex == null) Debug.Log("Cypress: no texture in Assets/PachyliaFicus - Pachylia ficus uses a tinted Cocytius texture for now. Drop one in that folder and re-run setup, or assign it on the component.");

        // Render Graph needs a depth buffer on camera output textures (the package's DomeMaster RT has none)
        CypressRenderTextureDepthFix.Fix(out _);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = go;
        Debug.Log("Cypress: interaction ready. Press Play, then Space = ghost orchid, Space again = moth. Move 'Viewer Anchor' to reposition the dome.");
    }

    [MenuItem("Tools/Cypress/Interaction/Rebuild Tree Anchors")]
    static void RebuildMenu()
    {
        var scene = SceneManager.GetActiveScene();
        var dome = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "CypressDome");
        if (dome == null) { Debug.LogError("Cypress: open the CypressDome scene first."); return; }
        var data = BuildTreeAnchors(dome);
        var exp = Object.FindAnyObjectByType<OrchidMothExperience>();
        if (exp != null && data != null) { exp.treeData = data; EditorUtility.SetDirty(exp); }
    }

    // ------------------------------------------------------------------ tree anchors
    [System.Serializable] class TreeFileFlat { public TreeFlat[] trees; }
    [System.Serializable] class TreeFlat { public string kind; public float[] flat; }

    /// <summary>Splits each trunk mesh into its separate trunks and samples centre + radius every 0.25 m.</summary>
    static TextAsset BuildTreeAnchors(GameObject dome)
    {
        var trees = new List<TreeFlat>();
        var toRoot = dome.transform.worldToLocalMatrix;
        foreach (var (objName, kind) in new[] { ("Cypress_BaldTrunks", "bald"), ("Cypress_PondTrunks", "pond"), ("Cypress_YoungTrunks", "young") })
        {
            var mf = dome.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == objName);
            if (mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            Vector3[] v;
            int[] tris;
            try { v = mesh.vertices; tris = mesh.triangles; }
            catch (System.Exception e) { Debug.LogWarning($"Cypress: can't read {objName} ({e.Message})."); continue; }
            if (v == null || v.Length == 0) { Debug.LogWarning($"Cypress: {objName} mesh isn't readable."); continue; }
            var m = toRoot * mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);

            // union-find over triangles -> one component per trunk
            var parent = new int[v.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = Find(tris[t]), b = Find(tris[t + 1]), c = Find(tris[t + 2]);
                if (a != b) parent[b] = a;
                c = Find(c); a = Find(a);
                if (a != c) parent[c] = a;
            }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                int r = Find(i);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(i);
            }
            foreach (var g in groups.Values)
            {
                if (g.Count < 40) continue;
                float top = g.Max(i => v[i].y);
                var flat = new List<float>();
                for (float h = 0.5f; h <= Mathf.Min(8f, top - 0.2f); h += 0.25f)
                {
                    float cx = 0, cz = 0; int n = 0;
                    foreach (int i in g) if (Mathf.Abs(v[i].y - h) < 0.14f) { cx += v[i].x; cz += v[i].z; n++; }
                    if (n < 6) continue;
                    cx /= n; cz /= n;
                    float rad = 0;
                    foreach (int i in g) if (Mathf.Abs(v[i].y - h) < 0.14f) rad += new Vector2(v[i].x - cx, v[i].z - cz).magnitude;
                    flat.AddRange(new[] { cx, h, cz, rad / n });
                }
                if (flat.Count >= 16) trees.Add(new TreeFlat { kind = kind, flat = flat.ToArray() });
            }
        }
        if (trees.Count == 0)
        {
            Debug.LogError("Cypress: no trunks found to build tree anchors (are Cypress_BaldTrunks / Cypress_PondTrunks in the scene?).");
            return null;
        }
        File.WriteAllText(TreeAnchorsPath, JsonUtility.ToJson(new TreeFileFlat { trees = trees.ToArray() }));
        AssetDatabase.ImportAsset(TreeAnchorsPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"Cypress: tree anchors for {trees.Count} trunks written to {TreeAnchorsPath}.");
        return AssetDatabase.LoadAssetAtPath<TextAsset>(TreeAnchorsPath);
    }

    // ------------------------------------------------------------------ helpers
    static Texture2D FindTexture(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return null;
        var guid = AssetDatabase.FindAssets("t:Texture2D", new[] { folder }).FirstOrDefault();
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
    }

    static Scene OpenCypressScene()
    {
        var scene = SceneManager.GetSceneByPath(CypressAssets.ScenePath);
        if (scene.isLoaded) { SceneManager.SetActiveScene(scene); return scene; }
        if (!File.Exists(CypressAssets.ScenePath))
        {
            Debug.LogError("Cypress: build the scene first (Tools > Cypress > Build Cypress Dome Scene).");
            return default;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return default;
        return EditorSceneManager.OpenScene(CypressAssets.ScenePath, OpenSceneMode.Single);
    }
}
