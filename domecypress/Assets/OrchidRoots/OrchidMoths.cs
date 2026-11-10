using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Once every orchid in the OrchidRootSphere animation has opened, sphinx moths fly in
/// and hover in the gaps between the ghost orchids, flapping their wings. One moth per gap, taking
/// turns between the moth prefabs (Cocytius antaeus, Pachylia ficus, ...).
/// All the moths fly in together.
/// Put this on the same GameObject as OrchidRootSphere.
/// </summary>
[RequireComponent(typeof(OrchidRootSphere))]
public class OrchidMoths : MonoBehaviour
{
    [Tooltip("Moth prefabs, used in turn around the ring.")]
    public GameObject[] mothPrefabs;
    [Tooltip("Wing flapping animation (legacy clip), e.g. Assets/Cocytius/fly.anim.")]
    public AnimationClip flyClip;

    [Header("Timing")]
    [Tooltip("Seconds to wait after the last orchid has fully opened.")]
    public float delayAfterFinish = 0.5f;
    [Tooltip("How long the moths take to fly into place, in seconds.")]
    public float flyInSeconds = 3f;

    [Header("Placement")]
    [Tooltip("Wingspan of each moth, in meters (the orchids are several meters tall).")]
    public float wingspan = 6f;
    [Tooltip("Moves the moths toward (+) or away from (-) the middle of the dome, in degrees.")]
    [Range(-30f, 30f)] public float towardCenter = -8f;
    [Tooltip("The moths never hover lower than this many degrees above the dome's horizon, so they stay in view " +
             "even when the orchids sit low around the edge.")]
    [Range(0f, 60f)] public float minHeight = 12f;
    [Tooltip("Moves the moths toward the audience, in meters, so they hover in front of the flowers.")]
    public float towardAudience = 0.5f;
    [Tooltip("The moths fly in from this many degrees further down the dome.")]
    [Range(0f, 60f)] public float flyInFrom = 25f;
    [Tooltip("Turns the moth model if it isn't facing the right way (degrees). Updates live in Play mode.")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Hover")]
    [Tooltip("How far the moths bob up and down while hovering, in meters.")]
    public float hoverAmount = 0.2f;
    public float hoverSpeed = 1.5f;
    [Tooltip("Wing flapping speed (1 = the clip's own speed).")]
    public float flapSpeed = 1f;

    class Moth
    {
        public Transform transform;
        public Vector3 gapDir;              // direction from the dome center to the middle of the gap
        public float radius, size, phase;   // size = model size at scale 1
        public float spawnTime;             // when this moth started flying in
    }

    OrchidRootSphere _sphere;
    readonly List<Moth> _moths = new List<Moth>();
    float _finishedAt = -1f;
    float _arrivedAt = -1f;
    Coroutine _spawning;          // the coroutine sending moths in
    bool _allSpawned;             // true once every gap has its moth

    /// <summary>Seconds since the moths finished flying into place (-1 = not there yet).</summary>
    public float SecondsSinceArrived => _arrivedAt < 0f ? -1f : Time.time - _arrivedAt;

    void Awake() => _sphere = GetComponent<OrchidRootSphere>();

    void Update()
    {
        if (!_sphere.OrchidsOpen)
        {
            // animation (re)started: no moths yet
            RemoveMoths();
            _finishedAt = -1f;
            _arrivedAt = -1f;
            return;
        }
        if (_finishedAt < 0f) _finishedAt = Time.time;

        float t = Time.time - _finishedAt - delayAfterFinish;
        if (t < 0f) return;
        if (_spawning == null && !_allSpawned) _spawning = StartCoroutine(SpawnMoths());

        Vector3 center = transform.position;
        Vector3 up = DomeUp();
        bool allInPlace = _allSpawned && _moths.Count > 0;

        foreach (var m in _moths)
        {
            // each moth flies in from the moment it was created
            float k = Mathf.Clamp01((Time.time - m.spawnTime) / Mathf.Max(0.01f, flyInSeconds));
            if (k < 1f) allInPlace = false;
            k = k * k * (3f - 2f * k);   // ease in and out

            // fly along the dome from below into the gap, then hover there
            // Wingspan, Toward Center and Fly In From are read every frame, so they update live in Play mode
            Vector3 home = Vector3.RotateTowards(m.gapDir, up, towardCenter * Mathf.Deg2Rad, 0f);
            // keep the hover spot above the horizon so the moth is on the dome, not below its edge
            float height = 90f - Vector3.Angle(home, up);
            if (height < minHeight) home = Vector3.RotateTowards(home, up, (minHeight - height) * Mathf.Deg2Rad, 0f);
            Vector3 start = Vector3.RotateTowards(home, up, -flyInFrom * Mathf.Deg2Rad, 0f);
            Vector3 dir = Vector3.Slerp(start, home, k);
            Vector3 pos = center + dir * (m.radius - towardAudience);
            pos += up * Mathf.Sin(Time.time * hoverSpeed + m.phase) * hoverAmount;
            m.transform.position = pos;

            // back toward the audience, head pointing the way it flew in (up the dome)
            Vector3 toAudience = -dir;
            Vector3 heading = up - dir * Vector3.Dot(up, dir);
            if (heading.sqrMagnitude < 1e-6f) heading = Vector3.Cross(dir, Vector3.right);
            m.transform.rotation = Quaternion.LookRotation(heading.normalized, toAudience) * Quaternion.Euler(rotationOffset);

            float scale = m.size > 1e-5f ? wingspan / m.size : 1f;
            m.transform.localScale = Vector3.one * scale * Mathf.Clamp01(k * 4f);   // grow in quickly
        }
        if (allInPlace && _arrivedAt < 0f) _arrivedAt = Time.time;
    }

    Vector3 DomeUp() => _sphere.domeUp.sqrMagnitude > 0f ? _sphere.domeUp.normalized : Vector3.up;

    /// <summary>
    /// Coroutine: puts one moth in each gap between the flowers. With One Moth At A Time on,
    /// it waits Seconds Between Moths after each one; otherwise they all appear in the same frame.
    /// </summary>
    IEnumerator SpawnMoths()
    {
        if (mothPrefabs == null || mothPrefabs.Length == 0) { Debug.LogWarning("OrchidMoths: no moth prefabs set.", this); _allSpawned = true; yield break; }

        // where each flower is (center of its petals), sorted around the dome
        Vector3 center = transform.position;
        Vector3 up = DomeUp();
        Vector3 refAxis = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 refAxis2 = Vector3.Cross(up, refAxis);
        var flowers = new List<Vector3>();
        foreach (var orchid in _sphere.Orchids)
        {
            var rends = orchid.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            flowers.Add(b.center);
        }
        if (flowers.Count < 2) { _allSpawned = true; yield break; }
        flowers.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
        float Angle(Vector3 p) { Vector3 d = p - center; return Mathf.Atan2(Vector3.Dot(d, refAxis2), Vector3.Dot(d, refAxis)); }

        // one moth in each gap between neighboring flowers
        for (int i = 0; i < flowers.Count; i++)
        {
            Vector3 a = flowers[i] - center, b = flowers[(i + 1) % flowers.Count] - center;
            Vector3 gap = a.normalized + b.normalized;
            if (gap.sqrMagnitude < 1e-6f) continue;

            var prefab = mothPrefabs[i % mothPrefabs.Length];
            if (prefab == null) continue;
            var go = Instantiate(prefab, transform.position, Quaternion.identity, transform);
            go.name = prefab.name;
            RemoveImportedExtras(go);

            var moth = new Moth
            {
                transform = go.transform,
                gapDir = gap.normalized,
                radius = (a.magnitude + b.magnitude) * 0.5f,
                phase = i * 1.7f,
            };
            moth.size = ModelSize(go);
            go.transform.localScale = Vector3.zero;
            moth.spawnTime = Time.time;
            StartFlapping(go, i);
            _moths.Add(moth);

        }
        _allSpawned = true;
        _spawning = null;
        if (_moths.Count > 0) SwampAudio.PlayFlap();   // the moths take off together: one wing sound
    }

    // The moth FBX was exported from Blender with its scene's Sun light and Camera. Inside the dome they
    // would light the orchids a second time (washing them out to flat white), so switch them off.
    static void RemoveImportedExtras(GameObject go)
    {
        foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
    }

    // measure the model at scale 1 (its wingspan)
    float ModelSize(GameObject go)
    {
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0f;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
        return size;
    }

    void StartFlapping(GameObject go, int index)
    {
        if (flyClip == null) return;
        var animator = go.GetComponent<Animator>();
        if (animator != null) animator.enabled = false;   // the legacy Animation component plays the clip
        var anim = go.GetComponent<Animation>();
        if (anim == null) anim = go.AddComponent<Animation>();
        anim.AddClip(flyClip, "fly");
        var state = anim["fly"];
        state.wrapMode = WrapMode.Loop;
        state.speed = flapSpeed;
        state.time = index * 0.13f;   // so they don't all flap in sync
        anim.Play("fly");
    }

    void RemoveMoths()
    {
        if (_moths.Count == 0 && _spawning == null && !_allSpawned) return;   // nothing to clear
        if (_spawning != null) { StopCoroutine(_spawning); _spawning = null; }
        _allSpawned = false;
        foreach (var m in _moths) if (m.transform != null) Destroy(m.transform.gameObject);
        _moths.Clear();
    }

#if UNITY_EDITOR
    // fill in the project's moths and wing animation when the component is added
    void Reset() => FillDefaults();
    void OnValidate() => FillDefaults();

    void FillDefaults()
    {
        if (mothPrefabs == null || mothPrefabs.Length == 0)
        {
            var list = new List<GameObject>();
            foreach (var path in new[] { "Assets/prefab/Cocytius_Antaeus.prefab", "Assets/prefab/PachyliaFicus.prefab" })
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p != null) list.Add(p);
            }
            if (list.Count > 0) mothPrefabs = list.ToArray();
        }
        if (flyClip == null)
            flyClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Cocytius/fly.anim");
    }
#endif
}
