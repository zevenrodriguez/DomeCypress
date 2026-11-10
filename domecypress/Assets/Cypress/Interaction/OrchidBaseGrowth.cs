using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Roots at the base of a ghost orchid on a cypress trunk.
/// Ghost orchids have no leaves - just a tangle of flat green roots stuck to the bark.
/// OrchidMothExperience creates one of these for every orchid; the roots creep out over the
/// bark while the flower opens.
/// </summary>
public class OrchidBaseGrowth : MonoBehaviour
{
    /// <summary>Gives the trunk centre and radius at height y (false if the trunk doesn't reach there).</summary>
    public delegate bool TrunkSampler(float y, out Vector3 centre, out float radius);

    [Tooltip("Seconds for the roots to creep out.")]
    public float growSeconds = 3f;

    // one root = a path over the bark and its thickness
    class Root { public List<Vector3> points = new List<Vector3>(); public float radius; }

    readonly List<Root> _roots = new List<Root>();
    Mesh _rootMesh;
    float _grown;          // 0..1

    const int RootSides = 6;

    /// <summary>
    /// Builds the roots around `basePos` (a point on the bark).
    /// `size` is the orchid's size in meters; the roots are scaled from it.
    /// </summary>
    public static OrchidBaseGrowth Create(Transform parent, Vector3 basePos, TrunkSampler trunk, float size,
                                          int rootCount, Material rootMaterial, float growSeconds)
    {
        var go = new GameObject("Orchid Roots");
        go.transform.SetParent(parent, true);
        go.transform.position = basePos;
        var growth = go.AddComponent<OrchidBaseGrowth>();
        growth.growSeconds = growSeconds;
        growth.BuildRoots(basePos, trunk, size, rootCount, rootMaterial);
        growth.SetGrowth(0f);
        return growth;
    }

    void Update()
    {
        if (_grown >= 1f) return;
        SetGrowth(Mathf.Min(1f, _grown + Time.deltaTime / Mathf.Max(0.1f, growSeconds)));
    }

    // ------------------------------------------------------------------ roots
    void BuildRoots(Vector3 basePos, TrunkSampler trunk, float size, int count, Material material)
    {
        if (count <= 0 || material == null) return;
        if (!trunk(basePos.y, out var c0, out _)) return;
        float startAngle = Mathf.Atan2(basePos.z - c0.z, basePos.x - c0.x);

        for (int i = 0; i < count; i++)
        {
            var root = new Root { radius = size * Random.Range(0.009f, 0.014f) };
            float length = size * Random.Range(0.5f, 1.3f);
            // most roots wrap sideways around the trunk; some run down, a few climb
            float drop = size * Random.Range(-0.7f, 0.35f);
            float side = (i % 2 == 0 ? 1f : -1f) * Random.Range(0.15f, 1f);
            float wiggle = Random.Range(0f, 10f);
            const int steps = 24;
            for (int k = 0; k <= steps; k++)
            {
                float s = k / (float)steps;
                float y = basePos.y + drop * s + Mathf.Sin(s * 7f + wiggle) * size * 0.03f;
                if (!trunk(y, out var c, out var r)) break;
                // turn the sideways distance along the bark into an angle around this trunk
                float angle = startAngle + side * length * s / Mathf.Max(0.05f, r)
                            + Mathf.Sin(s * 5f + wiggle) * 0.08f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                root.points.Add(c + outward * (r + root.radius * 0.6f));   // pressed flat onto the bark
            }
            if (root.points.Count >= 3) _roots.Add(root);
        }

        _rootMesh = new Mesh { name = "Orchid roots" };
        _rootMesh.MarkDynamic();
        var mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = _rootMesh;
        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
    }

    /// <summary>Rebuilds the root tubes, each drawn up to `grown` of its length (0..1).</summary>
    void RebuildRoots(float grown)
    {
        if (_rootMesh == null) return;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3 origin = transform.position;

        foreach (var root in _roots)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(root.points.Count * grown));
            if (grown <= 0f) continue;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = root.points[k];
                Vector3 dir = (root.points[Mathf.Min(k + 1, root.points.Count - 1)] - root.points[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 a = Vector3.Cross(dir, Vector3.up);
                if (a.sqrMagnitude < 1e-6f) a = Vector3.Cross(dir, Vector3.right);
                a.Normalize();
                Vector3 b = Vector3.Cross(dir, a);
                float s = k / (float)(n - 1);
                float radius = root.radius * Mathf.Lerp(1f, 0.35f, s);   // tapers to the tip
                float tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, s));   // OrchidRoot shader: uv.x = tip amount
                for (int j = 0; j < RootSides; j++)
                {
                    float ang = j / (float)RootSides * Mathf.PI * 2f;
                    Vector3 nrm = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                    verts.Add(p + nrm * radius - origin);
                    normals.Add(nrm);
                    uvs.Add(new Vector2(tip, s));
                }
                if (k == 0) continue;
                int row = verts.Count - RootSides, prev = row - RootSides;
                for (int j = 0; j < RootSides; j++)
                {
                    int j1 = (j + 1) % RootSides;
                    tris.Add(prev + j); tris.Add(row + j); tris.Add(row + j1);
                    tris.Add(prev + j); tris.Add(row + j1); tris.Add(prev + j1);
                }
            }
        }
        _rootMesh.Clear();
        _rootMesh.SetVertices(verts);
        _rootMesh.SetNormals(normals);
        _rootMesh.SetUVs(0, uvs);
        _rootMesh.SetTriangles(tris, 0);
        _rootMesh.RecalculateBounds();
    }

    // ------------------------------------------------------------------ growth
    void SetGrowth(float grown)
    {
        _grown = grown;
        RebuildRoots(Mathf.SmoothStep(0f, 1f, grown));
    }

    void OnDestroy()
    {
        if (_rootMesh != null) Destroy(_rootMesh);
    }
}
