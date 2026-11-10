using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws the sawgrass marsh ring exported by BlenderTools/export_cypress_unity.py with GPU instancing.
/// Every camera (game, scene view, each dome face) picks a level of detail per clump by distance:
/// LOD0 (full clump) up close, LOD1 (half the blades) in the middle, LOD2 (a few wide cards) far away.
/// Instances are bucketed into grid cells so whole cells outside the view are skipped.
/// </summary>
[ExecuteAlways]
public class GrassInstancer : MonoBehaviour
{
    [Tooltip("cypress_scene.json from the Blender export (grass.instances).")]
    public TextAsset sceneData;
    [Tooltip("Grass_L0, Grass_L1, Grass_L2 from CypressGrass.fbx.")]
    public Mesh[] lodMeshes = new Mesh[3];
    public Material material;

    [Header("Level of detail")]
    public float lod1Distance = 22f;
    public float lod2Distance = 40f;
    [Tooltip("Clumps further than this from the camera are not drawn.")]
    public float cullDistance = 150f;
    [Tooltip("Size of the culling cells, in meters.")]
    public float cellSize = 8f;

    [Header("Keep grass under the trees")]
    [Tooltip("tree_anchors.json (trunk positions). Filled in automatically in the editor.")]
    public TextAsset treeData;
    [Tooltip("Only grass within this many meters of a cypress trunk is drawn - everything out in the open " +
             "marsh beyond the canopy is removed. 0 = keep all the grass.")]
    public float maxDistanceFromTrees = 5f;

    [Header("Rendering")]
    public ShadowCastingMode castShadows = ShadowCastingMode.On;
    public bool receiveShadows = true;

    [Serializable] class SceneFile { public GrassData grass; }
    [Serializable] class TreeFile { public TreeEntry[] trees; }
    [Serializable] class TreeEntry { public float[] flat; }   // x, y, z, radius repeated; the first one is the trunk base
    [Serializable] class GrassData { public float lod1Distance; public float lod2Distance; public int stride; public float[] instances; }

    class Cell
    {
        public Bounds bounds;
        public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        public readonly List<Vector3> positions = new List<Vector3>();
    }

    const int BatchSize = 1023;
    readonly List<Cell> _cells = new List<Cell>();
    readonly Matrix4x4[][] _lodBuffers = new Matrix4x4[3][];
    readonly int[] _lodCounts = new int[3];
    readonly Plane[] _planes = new Plane[6];
    Matrix4x4 _loadedWith;
    int _total;

    public int InstanceCount => _total;

    void OnEnable()
    {
        Load();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        FindTreeData();
#endif
        if (isActiveAndEnabled) Load();
    }

    /// <summary>Reads the instance list and buckets it into culling cells (in world space).</summary>
    public void Load()
    {
        _cells.Clear();
        _total = 0;
        if (sceneData == null) return;
        var file = JsonUtility.FromJson<SceneFile>(sceneData.text);
        if (file?.grass?.instances == null) return;
        int stride = file.grass.stride > 0 ? file.grass.stride : 8;
        var data = file.grass.instances;
        var toWorld = transform.localToWorldMatrix;
        _loadedWith = toWorld;
        var trees = TreeBases();
        var lookup = new Dictionary<Vector2Int, Cell>();
        for (int i = 0; i + stride <= data.Length; i += stride)
        {
            var pos = new Vector3(data[i], data[i + 1], data[i + 2]);
            // the JSON rounds to 5 decimals, which leaves the quaternion slightly off unit length;
            // re-normalize or Matrix4x4.TRS rejects it
            var rot = Quaternion.Normalize(new Quaternion(data[i + 3], data[i + 4], data[i + 5], data[i + 6]));
            float s = data[i + 7];
            var m = toWorld * Matrix4x4.TRS(pos, rot, new Vector3(s, s, s));
            Vector3 world = m.GetColumn(3);
            if (trees != null && !NearATree(world, trees)) continue;   // out in the open marsh: skip
            var key = new Vector2Int(Mathf.FloorToInt(world.x / cellSize), Mathf.FloorToInt(world.z / cellSize));
            if (!lookup.TryGetValue(key, out var cell))
            {
                cell = new Cell { bounds = new Bounds(world, Vector3.zero) };
                lookup[key] = cell;
                _cells.Add(cell);
            }
            cell.matrices.Add(m);
            cell.positions.Add(world);
            cell.bounds.Encapsulate(world);
            _total++;
        }
        foreach (var cell in _cells)
            cell.bounds.Expand(new Vector3(1.5f, 4f, 1.5f));   // blade height and lean
        for (int l = 0; l < 3; l++)
            _lodBuffers[l] = new Matrix4x4[Mathf.Max(_total, 1)];
    }

    /// <summary>Trunk base positions (x, z) from the tree data, or null to keep all the grass.</summary>
    List<Vector2> TreeBases()
    {
        if (treeData == null || maxDistanceFromTrees <= 0f) return null;
        var file = JsonUtility.FromJson<TreeFile>(treeData.text);
        if (file?.trees == null) return null;
        var list = new List<Vector2>();
        foreach (var t in file.trees)
            if (t.flat != null && t.flat.Length >= 3) list.Add(new Vector2(t.flat[0], t.flat[2]));
        return list.Count > 0 ? list : null;
    }

    bool NearATree(Vector3 p, List<Vector2> trees)
    {
        float max = maxDistanceFromTrees * maxDistanceFromTrees;
        foreach (var t in trees)
        {
            float dx = p.x - t.x, dz = p.z - t.y;
            if (dx * dx + dz * dz <= max) return true;
        }
        return false;
    }

#if UNITY_EDITOR
    void Reset() => FindTreeData();
    void FindTreeData()
    {
        if (treeData == null)
            treeData = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Cypress/tree_anchors.json");
    }
#endif

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (_total == 0 || material == null || lodMeshes == null || lodMeshes.Length < 3) return;
        if (cam.cameraType == CameraType.Preview) return;
        if (transform.localToWorldMatrix != _loadedWith) Load();   // moved in the editor

        GeometryUtility.CalculateFrustumPlanes(cam, _planes);
        Vector3 camPos = cam.transform.position;
        float l1 = lod1Distance * lod1Distance, l2 = lod2Distance * lod2Distance, cull = cullDistance * cullDistance;
        _lodCounts[0] = _lodCounts[1] = _lodCounts[2] = 0;

        foreach (var cell in _cells)
        {
            if (cell.bounds.SqrDistance(camPos) > cull) continue;
            if (!GeometryUtility.TestPlanesAABB(_planes, cell.bounds)) continue;
            var positions = cell.positions;
            var matrices = cell.matrices;
            for (int i = 0; i < positions.Count; i++)
            {
                float d = (positions[i] - camPos).sqrMagnitude;
                if (d > cull) continue;
                int lod = d < l1 ? 0 : d < l2 ? 1 : 2;
                _lodBuffers[lod][_lodCounts[lod]++] = matrices[i];
            }
        }

        var rp = new RenderParams(material)
        {
            camera = cam,
            layer = gameObject.layer,
            shadowCastingMode = castShadows,
            receiveShadows = receiveShadows,
            worldBounds = new Bounds(camPos, Vector3.one * cullDistance * 2f),
        };
        for (int l = 0; l < 3; l++)
        {
            var mesh = lodMeshes[l];
            if (mesh == null) continue;
            int count = _lodCounts[l];
            for (int start = 0; start < count; start += BatchSize)
                Graphics.RenderMeshInstanced(rp, mesh, 0, _lodBuffers[l], Mathf.Min(BatchSize, count - start), start);
        }
    }
}
