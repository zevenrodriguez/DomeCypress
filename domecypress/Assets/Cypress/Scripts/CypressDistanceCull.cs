using UnityEngine;

/// <summary>
/// Hides small scene detail (ferns, litter, air plants, knees...) that is too far away to matter on the dome.
/// Put it on a parent object; it checks every child renderer a few times a second and switches it off when
/// it is further than Cull Distance from the viewer - or, if a Focus Area is set (the 'mothsphere', where the
/// orchids and moths are), further than Cull Distance outside that area.
/// (Distance-based, so it works with the dome camera's very wide view, where screen-size LODs don't.)
/// </summary>
public class CypressDistanceCull : MonoBehaviour
{
    [Tooltip("The viewer (the dome centre). Found automatically as 'Viewer Anchor' if left empty.")]
    public Transform viewer;
    [Tooltip("Optional: the main area of interest (the mothsphere). When set, distances are measured from " +
             "the edge of this sphere instead of from the viewer, so everything near the action keeps its detail.")]
    public Transform focusArea;
    [Tooltip("Renderers further than this are not drawn, in meters.")]
    public float cullDistance = 35f;
    [Tooltip("How often the distances are checked, in seconds.")]
    public float checkInterval = 0.25f;

    Renderer[] _renderers;
    float _nextCheck;

    void OnEnable()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (viewer == null)
        {
            var anchor = GameObject.Find("Viewer Anchor");
            if (anchor != null) viewer = anchor.transform;
        }
        _nextCheck = 0f;
    }

    void OnDisable()
    {
        // show everything again when switched off
        if (_renderers == null) return;
        foreach (var r in _renderers) if (r != null) r.forceRenderingOff = false;
    }

    void Update()
    {
        if (Time.time < _nextCheck || _renderers == null) return;
        _nextCheck = Time.time + checkInterval;
        Vector3 eye = viewer != null ? viewer.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            float distance = focusArea != null ? DistanceOutside(focusArea, r.bounds) : Mathf.Sqrt(r.bounds.SqrDistance(eye));
            r.forceRenderingOff = distance > cullDistance;
        }
    }

    /// <summary>
    /// How far the bounds are outside a (possibly stretched) Unity sphere, in meters; 0 if they touch it.
    /// Unity's sphere mesh has radius 0.5 in its own space.
    /// </summary>
    public static float DistanceOutside(Transform sphere, Bounds bounds)
    {
        Vector3 nearest = bounds.ClosestPoint(sphere.position);
        Vector3 local = sphere.InverseTransformPoint(nearest);
        float outside = local.magnitude - 0.5f;
        if (outside <= 0f) return 0f;
        // back to meters, using the sphere's scale in that direction
        Vector3 scale = sphere.lossyScale;
        Vector3 dir = local.normalized;
        float metersPerUnit = new Vector3(dir.x * scale.x, dir.y * scale.y, dir.z * scale.z).magnitude;
        return outside * metersPerUnit;
    }
}
