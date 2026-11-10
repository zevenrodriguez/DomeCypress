using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Ghost orchids and sphinx moths in the cypress dome.
///  Space : ghost orchids open one after another on several trees around the viewer (anchor), on the
///          trunk side facing the viewer. Then a sphinx moth (Cocytius antaeus or Pachylia ficus,
///          alternating) emerges from the centre of the dome for each orchid, flies to it and hovers
///          there feeding. After Seconds Before Fading (20 s) the moth and its orchid fade out and are
///          destroyed, which frees the tree for the next Space. (Auto Play does the same once at the start.)
///  Extra Moths : when ticked, a coroutine sends another moth out of the centre every few seconds.
///          It flies to one of the open orchids (taking turns), feeds for a while, then flies off and fades.
/// Tree positions come from tree_anchors.json (Tools > Cypress > Interaction > Rebuild Tree Anchors).
/// </summary>
public class OrchidMothExperience : MonoBehaviour
{
    // ------------------------------------------------------------------ inspector
    [Header("Viewer")]
    [Tooltip("Centre of the dome / the viewer. Move this (it carries the Dome Creator rig) to reposition the experience.")]
    public Transform anchor;
    [Tooltip("The CypressDome root; tree positions are relative to it.")]
    public Transform environmentRoot;

    [Header("Trees")]
    public TextAsset treeData;
    [Tooltip("Sphere marking where orchids may open (the 'mothsphere' child). Orchids only go on trees inside it. " +
             "Move and scale it in the scene; it's hidden in Play mode. Leave empty to use Min/Max Tree Distance instead.")]
    public Transform orchidArea;
    [Tooltip("Pick among this many trees nearest the anchor.")]
    [Range(1, 20)] public int nearestTreeCount = 6;
    public float minTreeDistance = 2.0f;
    public float maxTreeDistance = 14f;
    [Tooltip("Height range on the trunk (meters above the water) where orchids grow.")]
    public Vector2 orchidHeight = new Vector2(2.2f, 4.2f);

    [Header("Ghost orchid")]
    public GameObject orchidModel;
    public Texture2D orchidBaseColor;
    public Texture2D orchidNormal;
    [Tooltip("Largest dimension of the orchid model, meters. Real flowers are ~12 cm; larger reads better on a dome.")]
    public float orchidSize = 0.35f;
    [Tooltip("Multiplies Orchid Size (3 = three times bigger).")]
    public float orchidScale = 3f;
    [Tooltip("Turns each flower around its stem if its face isn't square to the viewer (degrees; same idea as the title scene).")]
    [Range(-180f, 180f)] public float orchidFaceYaw = 17f;
    [Tooltip("Spins each flower around the way it faces, in degrees (0 = upright).")]
    [Range(-180f, 180f)] public float orchidFaceRoll = 0f;
    public float orchidGrowSeconds = 3f;
    [Range(0f, 1f)] public float orchidSmoothness = 0.35f;
    [Tooltip("Soft white glow so the orchid stands out in the dim swamp.")]
    [ColorUsage(false, true)] public Color orchidGlow = new Color(0.08f, 0.08f, 0.07f);

    [Header("Orchid roots")]
    [Tooltip("Grow a tangle of roots over the bark at the base of each orchid.")]
    public bool orchidRoots = true;
    [Range(0, 20)] public int orchidRootCount = 9;

    [Header("Moths")]
    public MothSpecies[] moths = new MothSpecies[0];
    [Tooltip("Animation clip with the wing beat (legacy clip, e.g. Cocytius/fly.anim).")]
    public AnimationClip flyClip;
    [Tooltip("Wingspan the moth is scaled to, meters (real: 12-17 cm).")]
    public float mothWingspan = 0.35f;
    public float flightSpeed = 1.6f;
    [Tooltip("How fast the wings flap (1 = the fly animation's own speed; hawk moths beat fast). Applies to new moths.")]
    [Range(0.5f, 6f)] public float wingFlapSpeed = 3f;
    [Tooltip("How wide the wings beat (1 = the fly animation as made, 2 = twice the angle). Applies to new moths.")]
    [Range(1f, 3f)] public float wingFlapAngle = 1.8f;
    [Tooltip("Seconds the moth feeds at its orchid before the moth and the orchid fade out and are removed.")]
    public float secondsBeforeFading = 20f;
    public float fadeSeconds = 3f;
    [Tooltip("Rotation applied to the moth model so it flies head-first. Change if it flies sideways/backwards.")]
    public Vector3 mothFacingFix = Vector3.zero;
    [Tooltip("Where moths hover, measured from the top of the orchid: 0 = level with the top of the flower, " +
             "+ = higher, - = lower (meters).")]
    public float mothHoverOffset = -2f;
    [Tooltip("Where the moth appears, relative to the anchor (local space).")]
    public Vector3 mothSpawnOffset = new Vector3(0f, 0.3f, 0.4f);

    [Header("Input")]
    public bool useSpaceBar = true;

    [Header("Several orchids and moths")]
    [Tooltip("Also play the sequence once by itself when the scene starts (otherwise only on Space).")]
    public bool autoPlay = false;
    [Tooltip("How many orchids open (each on its own tree, spread around the viewer).")]
    [Range(1, 12)] public int orchidCount = 5;
    [Tooltip("Auto Play only: seconds after the scene starts (leaves time for the fade-in).")]
    public float startDelay = 2.5f;
    [Tooltip("Seconds between one orchid starting to open and the next.")]
    public float orchidInterval = 1.2f;
    [Tooltip("Seconds after the last orchid has opened before the first moth sets off.")]
    public float mothDelay = 2f;
    [Tooltip("Seconds between one moth setting off and the next.")]
    public float mothInterval = 1.5f;

    [Header("Extra moths")]
    [Tooltip("Turn on to send a new moth to the open orchids every few seconds. Can be switched during Play.")]
    public bool extraMoths = false;
    [Tooltip("Seconds between extra moths.")]
    public float secondsBetweenExtraMoths = 5f;
    [Tooltip("How long each extra moth feeds before flying off.")]
    public float extraMothFeedSeconds = 10f;
    [Tooltip("Most extra moths flying at the same time (keeps the frame rate steady).")]
    [Range(1, 20)] public int maxExtraMoths = 6;

    [Serializable]
    public class MothSpecies
    {
        public string name = "Moth";
        public GameObject model;
        public Texture2D texture;
        public Color tint = Color.white;
    }

    /// <summary>Orchid size actually used (Orchid Size x Orchid Scale).</summary>
    float OrchidSize => orchidSize * orchidScale;

    // ------------------------------------------------------------------ state
    public enum Phase { Idle, OrchidGrowing, OrchidWaiting, MothFlying, Feeding, Fading }

    /// <summary>One orchid on a tree and the moth that visits it.</summary>
    class Visit
    {
        public Phase phase = Phase.OrchidGrowing;
        public GameObject orchid, moth;
        public OrchidBaseGrowth orchidBase;   // roots at the base of the flower
        public Transform flower;              // the flower model
        public List<FadeSlot> orchidFades;    // the flower's see-through (bright) materials, kept for its whole life
        public Vector3 pos, facing;
        public int tree;
        public readonly List<Material> mothMaterials = new List<Material>();
    }

    [Serializable] class TreeFileFlat { public TreeFlat[] trees; }
    [Serializable] class TreeFlat { public string kind; public float[] flat; }       // x,y,z,r repeated

    class Tree
    {
        public string kind;
        public List<Vector4> samples = new List<Vector4>();   // world xyz + radius
        public Vector3 basePos;
    }

    readonly List<Tree> _trees = new List<Tree>();
    readonly List<Visit> _visits = new List<Visit>();
    int _mothIndex;
    Material _orchidOpaque;
    readonly List<Material> _spawnedMaterials = new List<Material>();
    int _extraMothCount;      // extra moths in the air right now
    int _extraTargetIndex;    // which orchid the next extra moth visits

    // ------------------------------------------------------------------ setup
    void Start()
    {
        if (anchor == null) anchor = transform;
        if (orchidModel == null) orchidModel = Resources.Load<GameObject>("OrchidRoots/GhostOrchid");
        if (orchidBaseColor == null) orchidBaseColor = Resources.Load<Texture2D>("OrchidRoots/GhostOrchid_BaseColor");
        if (orchidNormal == null) orchidNormal = Resources.Load<Texture2D>("OrchidRoots/GhostOrchid_Normal");
        if (orchidArea == null) orchidArea = transform.Find("mothsphere");
        if (orchidArea != null)
        {
            // the sphere is only a marker: hide it and stop it blocking anything
            foreach (var r in orchidArea.GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var c in orchidArea.GetComponentsInChildren<Collider>()) c.enabled = false;
        }
        LoadTrees();
        if (_trees.Count == 0)
            Debug.LogWarning("OrchidMothExperience: no tree data. Run Tools > Cypress > Interaction > Rebuild Tree Anchors.", this);
        else if (autoPlay)
            StartCoroutine(Sequence(startDelay));
        StartCoroutine(ExtraMothLoop());
    }

    bool _sequenceRunning;

    /// <summary>Several orchids open one after another on different trees, then a moth flies to each of them.</summary>
    IEnumerator Sequence(float delay)
    {
        _sequenceRunning = true;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        var opened = new List<Visit>();
        for (int i = 0; i < orchidCount; i++)
        {
            var v = StartOrchid();
            if (v == null) break;   // no more free trees in range
            opened.Add(v);
            yield return new WaitForSeconds(orchidInterval);
        }

        // wait until every orchid is fully open
        while (opened.Exists(v => v.phase == Phase.OrchidGrowing)) yield return null;
        yield return new WaitForSeconds(mothDelay);

        foreach (var v in opened)
        {
            if (v.phase != Phase.OrchidWaiting) continue;
            StartCoroutine(MothVisit(v));
            yield return new WaitForSeconds(mothInterval);
        }
        _sequenceRunning = false;
    }

    void LoadTrees()
    {
        _trees.Clear();
        if (treeData == null) return;
        var file = JsonUtility.FromJson<TreeFileFlat>(treeData.text);
        if (file?.trees == null) return;
        var m = environmentRoot != null ? environmentRoot.localToWorldMatrix : Matrix4x4.identity;
        float s = environmentRoot != null ? environmentRoot.lossyScale.x : 1f;
        foreach (var t in file.trees)
        {
            if (t.flat == null || t.flat.Length < 8) continue;
            var tree = new Tree { kind = t.kind };
            for (int i = 0; i + 3 < t.flat.Length; i += 4)
            {
                Vector3 p = m.MultiplyPoint3x4(new Vector3(t.flat[i], t.flat[i + 1], t.flat[i + 2]));
                tree.samples.Add(new Vector4(p.x, p.y, p.z, t.flat[i + 3] * s));
            }
            tree.basePos = tree.samples[0];
            _trees.Add(tree);
        }
    }

    // ------------------------------------------------------------------ input
    void Update()
    {
        if (useSpaceBar && SpacePressed()) Advance();
    }

    static bool SpacePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.spaceKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Space);
#else
        return false;
#endif
    }

    /// <summary>What Space does: orchids open on several trees, then the moths fly to them.
    /// Pressing it again while that's still playing does nothing; afterwards it adds a new set on free trees.</summary>
    public void Advance()
    {
        if (!_sequenceRunning) StartCoroutine(Sequence(0f));
    }

    /// <summary>Picks a free tree and starts an orchid opening on it (null if no tree is free).</summary>
    Visit StartOrchid()
    {
        if (!PickSpot(out var pos, out var facing, out int tree))
        {
            Debug.LogWarning("OrchidMothExperience: no free tree found near the anchor for an orchid.", this);
            return null;
        }
        var v = new Visit { pos = pos, facing = facing, tree = tree };
        _visits.Add(v);
        StartCoroutine(GrowOrchid(v));
        return v;
    }

    // ------------------------------------------------------------------ orchid
    IEnumerator GrowOrchid(Visit v)
    {
        v.phase = Phase.OrchidGrowing;
        Vector3 pos = v.pos, facing = v.facing;

        var orchid = v.orchid = new GameObject("Ghost Orchid (interaction)");
        orchid.transform.SetParent(transform, true);
        // a little out from the bark so the bigger flower doesn't sink into the trunk
        orchid.transform.position = pos + facing * OrchidSize * 0.25f;
        // face forward, straight at the viewer, upright: the flower face is model +Z and the top
        // petal model +Y (same as the title scene's OrchidRootSphere)
        Vector3 toViewer = anchor.position - orchid.transform.position;
        if (toViewer.sqrMagnitude < 1e-6f) toViewer = facing;
        orchid.transform.rotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up)
                                    * Quaternion.Euler(0f, 0f, orchidFaceRoll)
                                    * Quaternion.Euler(0f, orchidFaceYaw, 0f);
        if (orchidModel != null)
        {
            var model = Instantiate(orchidModel, orchid.transform, false);
            ApplyOrchidMaterial(model);
            FitSize(model.transform, OrchidSize, out _);
            v.flower = model.transform;
        }
        else
        {
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(fallback.GetComponent<Collider>());
            fallback.transform.SetParent(orchid.transform, false);
            fallback.transform.localScale = Vector3.one * OrchidSize * 0.5f;
            v.flower = fallback.transform;
        }

        // roots creep over the bark while the flower opens
        if (orchidRoots && v.tree >= 0 && v.tree < _trees.Count)
        {
            var tree = _trees[v.tree];
            v.orchidBase = OrchidBaseGrowth.Create(transform, pos,
                (float y, out Vector3 c, out float r) => TrunkAt(tree, y, out c, out r),
                OrchidSize, orchidRootCount, RootMaterial(), orchidGrowSeconds);
        }

        // pop open (ease-out-back, like the title-scene orchids) while fading in. The flower keeps its
        // see-through material afterwards too: it skips the scene's ambient-occlusion darkening, so the
        // ghost orchid stays bright and glowing instead of darkening once it has opened.
        var fades = v.orchidFades = PrepareFade(orchid);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.1f, orchidGrowSeconds);
            float u = Mathf.Clamp01(t), um1 = u - 1f;
            float ease = 1f + 2.4f * um1 * um1 * um1 + 1.4f * um1 * um1;
            orchid.transform.localScale = Vector3.one * Mathf.Max(0f, ease);
            SetFade(fades, Mathf.SmoothStep(0f, 1f, u * 1.5f));
            yield return null;
        }
        orchid.transform.localScale = Vector3.one;
        SetFade(fades, 1f);
        // fully visible now: write depth again so the petals and roots sort correctly
        foreach (var slot in fades) if (slot.fadeMat != null) slot.fadeMat.SetFloat("_ZWrite", 1f);
        // from now on the roots belong to the orchid, so they fade out and are removed with it
        if (v.orchidBase != null) v.orchidBase.transform.SetParent(orchid.transform, true);
        v.phase = Phase.OrchidWaiting;
    }

    /// <summary>The orchid root material from the title scene (green velamen with brown patches).</summary>
    static Material RootMaterial() => Resources.Load<Material>("OrchidRoots/OrchidRoot");


    bool PickSpot(out Vector3 pos, out Vector3 facing, out int tree)
    {
        pos = facing = Vector3.zero;
        tree = -1;
        if (_trees.Count == 0) return false;
        Vector3 a = anchor.position;

        // trees in range that don't already have an orchid, nearest first
        var used = new HashSet<int>();
        foreach (var v in _visits) used.Add(v.tree);
        var order = new List<(float d, int i)>();
        for (int i = 0; i < _trees.Count; i++)
        {
            if (used.Contains(i)) continue;
            var b = _trees[i].basePos;
            float d = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            bool ok = orchidArea != null ? TreeInArea(_trees[i]) : d >= minTreeDistance && d <= maxTreeDistance;
            if (ok) order.Add((d, i));
        }
        order.Sort((x, y) => x.d.CompareTo(y.d));
        // with an area, every tree inside it can be used; otherwise only the nearest few
        int poolSize = orchidArea != null ? order.Count : Mathf.Max(nearestTreeCount, orchidCount * 2);
        var candidates = new List<int>();
        foreach (var o in order)
        {
            if (candidates.Count >= poolSize) break;
            candidates.Add(o.i);
        }
        if (candidates.Count == 0) return false;

        // spread the orchids around the viewer: prefer trees in directions that don't have an orchid yet
        float Spread(int ti)
        {
            Vector3 d = _trees[ti].basePos - a; d.y = 0f;
            float best = 180f;
            foreach (var v in _visits)
            {
                Vector3 e = v.pos - a; e.y = 0f;
                best = Mathf.Min(best, Vector3.Angle(d, e));
            }
            return best + UnityEngine.Random.Range(0f, 20f);   // a little randomness
        }
        // score each tree once, then sort by the stored scores (Spread has a random part, so calling it
        // inside the sort gives different answers for the same tree and the sort throws an error)
        var score = new Dictionary<int, float>();
        foreach (int ti in candidates) score[ti] = Spread(ti);
        candidates.Sort((x, y) => score[y].CompareTo(score[x]));

        // try the best-spread trees first until one has a clear line of sight from the anchor
        foreach (int ti in candidates)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float h = UnityEngine.Random.Range(orchidHeight.x, orchidHeight.y);
                if (!TrunkAt(_trees[ti], h, out var c, out var r)) continue;
                if (!InArea(c)) continue;
                var toViewer = a - c; toViewer.y = 0f;
                if (toViewer.sqrMagnitude < 1e-4f) continue;
                toViewer.Normalize();
                // a little to one side so it isn't dead-centre behind the trunk silhouette
                toViewer = Quaternion.AngleAxis(UnityEngine.Random.Range(-35f, 35f), Vector3.up) * toViewer;
                var p = c + toViewer * (r + 0.03f);
                if (Blocked(a, p, ti)) continue;
                pos = p; facing = toViewer; tree = ti;
                return true;
            }
        }
        return false;
    }

    /// <summary>True if the point is inside the orchid area sphere (or there is no area).</summary>
    bool InArea(Vector3 p)
    {
        if (orchidArea == null) return true;
        // Unity's sphere mesh has radius 0.5 in its own space, so this also works when it's stretched
        return orchidArea.InverseTransformPoint(p).magnitude <= 0.5f;
    }

    /// <summary>True if part of the trunk within the orchid height range is inside the area.</summary>
    bool TreeInArea(Tree t)
    {
        for (float h = orchidHeight.x; h <= orchidHeight.y; h += 0.25f)
            if (TrunkAt(t, h, out var c, out _) && InArea(c)) return true;
        return false;
    }

    static bool TrunkAt(Tree t, float y, out Vector3 centre, out float radius)
    {
        centre = Vector3.zero; radius = 0f;
        var s = t.samples;
        if (s.Count == 0 || y < s[0].y || y > s[s.Count - 1].y) return false;
        for (int i = 0; i < s.Count - 1; i++)
        {
            if (y < s[i].y || y > s[i + 1].y) continue;
            float k = Mathf.InverseLerp(s[i].y, s[i + 1].y, y);
            var v = Vector4.Lerp(s[i], s[i + 1], k);
            centre = new Vector3(v.x, v.y, v.z); radius = v.w;
            return true;
        }
        return false;
    }

    bool Blocked(Vector3 from, Vector3 to, int self)
    {
        for (int i = 0; i < _trees.Count; i++)
        {
            if (i == self) continue;
            for (float k = 0.15f; k < 0.95f; k += 0.1f)
            {
                var p = Vector3.Lerp(from, to, k);
                if (!TrunkAt(_trees[i], p.y, out var c, out var r)) continue;
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude < (r + 0.15f) * (r + 0.15f)) return true;
            }
        }
        return false;
    }

    void ApplyOrchidMaterial(GameObject model)
    {
        if (_orchidOpaque == null)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;
            _orchidOpaque = new Material(lit) { name = "GhostOrchid (interaction)" };
            if (orchidBaseColor != null) _orchidOpaque.SetTexture("_BaseMap", orchidBaseColor);
            _orchidOpaque.SetColor("_BaseColor", Color.white);
            if (orchidNormal != null) { _orchidOpaque.SetTexture("_BumpMap", orchidNormal); _orchidOpaque.EnableKeyword("_NORMALMAP"); }
            _orchidOpaque.SetFloat("_Metallic", 0f);
            _orchidOpaque.SetFloat("_Smoothness", orchidSmoothness);
            _orchidOpaque.SetFloat("_Cull", (float)CullMode.Off);
            if (orchidGlow.maxColorComponent > 0f)
            {
                _orchidOpaque.SetColor("_EmissionColor", orchidGlow);
                _orchidOpaque.EnableKeyword("_EMISSION");
            }
            _spawnedMaterials.Add(_orchidOpaque);
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = _orchidOpaque;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
        }
    }

    // ------------------------------------------------------------------ moth
    IEnumerator MothVisit(Visit v)
    {
        if (moths == null || moths.Length == 0 || v.orchid == null) yield break;
        v.phase = Phase.MothFlying;
        var species = moths[_mothIndex++ % moths.Length];

        // hover point: in front of the flower, at the height set by Moth Height Above Flower
        float wing = mothWingspan;
        Vector3 hover = HoverPoint(v, wing * 0.6f);
        Vector3 start = anchor.TransformPoint(mothSpawnOffset);

        var moth = v.moth = CreateMoth(species, start, v.mothMaterials);
        SwampAudio.PlayFlap();   // wing sound as it leaves the centre

        var fades = PrepareFade(moth);
        // flight: a curving path out of the centre of the dome, swinging up and around to the flower
        Vector3 side = Vector3.Cross(Vector3.up, (hover - start).normalized);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side *= UnityEngine.Random.value < 0.5f ? -1f : 1f;
        Vector3 p1 = start + Vector3.up * 1.2f + side * 1.5f + anchor.forward * 0.8f;
        Vector3 p2 = hover + v.facing * 2.2f + Vector3.up * 0.8f - side * 0.8f;
        float length = Vector3.Distance(start, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, hover);
        float duration = Mathf.Max(3f, length / Mathf.Max(0.1f, flightSpeed));
        float t = 0f;
        Vector3 prev = start;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 p = Bezier(start, p1, p2, hover, u);
            float flutter = Mathf.Sin(Time.time * 9f) * 0.03f * (1f - u);
            moth.transform.position = p + Vector3.up * flutter;
            Vector3 vel = p - prev;
            if (vel.sqrMagnitude > 1e-8f)
            {
                // face along the path, bank into turns
                var look = Quaternion.LookRotation(vel.normalized, Vector3.up);
                float bank = Vector3.Dot(Vector3.Cross(moth.transform.forward, vel.normalized), Vector3.up) * -400f;
                look *= Quaternion.Euler(0f, 0f, Mathf.Clamp(bank, -35f, 35f));
                moth.transform.rotation = Quaternion.Slerp(moth.transform.rotation, look, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
            prev = p;
            SetFade(fades, Mathf.Clamp01(t * 6f));        // materialises as it leaves the centre
            yield return null;
        }
        EndFade(fades);

        // feeding: hover facing the flower, small bobs and darts like a hawk moth at a spur
        v.phase = Phase.Feeding;
        var faceFlower = Quaternion.LookRotation(-v.facing, Vector3.up);
        float fed = 0f;
        while (fed < secondsBeforeFading)
        {
            fed += Time.deltaTime;
            float dart = Mathf.PerlinNoise(fed * 0.35f, 0.3f) - 0.5f;
            Vector3 offset = Vector3.up * Mathf.Sin(fed * 2.3f) * 0.025f
                           + Vector3.Cross(Vector3.up, v.facing) * Mathf.Sin(fed * 0.9f) * 0.03f
                           + v.facing * dart * 0.08f;
            moth.transform.position = Vector3.Lerp(moth.transform.position, hover + offset, 1f - Mathf.Exp(-6f * Time.deltaTime));
            var wobble = Quaternion.Euler(Mathf.Sin(fed * 1.7f) * 4f, Mathf.Sin(fed * 1.1f) * 6f, 0f);
            moth.transform.rotation = Quaternion.Slerp(moth.transform.rotation, faceFlower * wobble, 1f - Mathf.Exp(-4f * Time.deltaTime));
            yield return null;
        }

        // both fade away together
        v.phase = Phase.Fading;
        // the moth and the flower fade out (the flower still has its see-through material); the roots
        // dissolve with their own fade
        var all = PrepareFade(moth);
        if (v.orchidFades != null)
            foreach (var slot in v.orchidFades) { if (slot.fadeMat != null) slot.fadeMat.SetFloat("_ZWrite", 0f); all.Add(slot); }
        if (v.orchidBase != null) all.AddRange(PrepareFade(v.orchidBase.gameObject));
        float f = 0f;
        while (f < 1f)
        {
            f += Time.deltaTime / Mathf.Max(0.1f, fadeSeconds);
            SetFade(all, 1f - Mathf.SmoothStep(0f, 1f, f));
            moth.transform.position += Vector3.up * Time.deltaTime * 0.05f;
            yield return null;
        }
        HideAndDestroy(moth); HideAndDestroy(v.orchid);
        foreach (var fd in all) if (fd.fadeMat != null) Destroy(fd.fadeMat);
        foreach (var m in v.mothMaterials) if (m != null) Destroy(m);
        v.mothMaterials.Clear();
        _visits.Remove(v);   // its tree is free again
    }

    /// <summary>Makes a flapping moth at `start` (the model sits under a "Body" child that carries the facing fix).</summary>
    GameObject CreateMoth(MothSpecies species, Vector3 start, List<Material> createdMaterials)
    {
        var moth = new GameObject($"{species.name} (interaction)");
        moth.transform.SetParent(transform, true);
        moth.transform.position = start;
        var body = new GameObject("Body").transform;
        body.SetParent(moth.transform, false);
        // the moth models' heads point backwards (-Z), so turn them 180 degrees to fly and feed
        // head-first; Moth Facing Fix is added on top for any further tweaking
        body.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(mothFacingFix);
        if (species.model != null)
        {
            var model = Instantiate(species.model, body, false);
            model.transform.localPosition = Vector3.zero;   // prefabs may be saved away from the origin
            ApplyMothMaterial(model, species, createdMaterials);
            FitSize(model.transform, mothWingspan, out _);
            // centre the visible moth on its pivot, so it ends up exactly where it's sent
            // (the moth meshes aren't centred on their own origin)
            if (TryGetBounds(model, out var b)) model.transform.position += start - b.center;
            model.AddComponent<WingFlapAmplifier>().amount = wingFlapAngle;   // wider wing beat
            StartWings(model, wingFlapSpeed);
        }
        return moth;
    }

    /// <summary>
    /// Where a moth hovers at an orchid: measured from the orchid as it is actually drawn (its top edge),
    /// out in front of it by `outFront` meters, then moved up or down by Moth Hover Offset.
    /// </summary>
    Vector3 HoverPoint(Visit v, float outFront)
    {
        Vector3 top = v.pos + Vector3.up * OrchidSize * 0.5f;           // fallback if the orchid is gone
        float depth = OrchidSize * 0.5f;
        if (v.orchid != null && TryGetBounds(v.orchid, out var b))
        {
            top = new Vector3(b.center.x, b.max.y, b.center.z);
            depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(v.facing.x), 0f, Mathf.Abs(v.facing.z))));
        }
        return top + v.facing * (depth + outFront) + Vector3.up * mothHoverOffset;
    }

    static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        bounds = rs[0].bounds;
        foreach (var r in rs) bounds.Encapsulate(r.bounds);
        return true;
    }

    // ------------------------------------------------------------------ extra moths
    /// <summary>Runs all the time; while Extra Moths is ticked it sends out a moth every few seconds.</summary>
    IEnumerator ExtraMothLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, secondsBetweenExtraMoths));
            if (!extraMoths) continue;                          // switched off
            if (_extraMothCount >= maxExtraMoths) continue;     // enough in the air
            var target = NextOpenOrchid();
            if (target == null) continue;                       // no open orchid yet (press Space first)
            StartCoroutine(ExtraMoth(target));
        }
    }

    /// <summary>The next open orchid, taking turns between them.</summary>
    Visit NextOpenOrchid()
    {
        var open = _visits.FindAll(v => v.orchid != null && v.phase != Phase.OrchidGrowing && v.phase != Phase.Fading);
        if (open.Count == 0) return null;
        return open[_extraTargetIndex++ % open.Count];
    }

    /// <summary>One extra moth: out of the centre, to the orchid, feeds, then flies off and fades.</summary>
    IEnumerator ExtraMoth(Visit v)
    {
        if (moths == null || moths.Length == 0) yield break;
        _extraMothCount++;
        var species = moths[_mothIndex++ % moths.Length];
        var materials = new List<Material>();
        Vector3 start = anchor.TransformPoint(mothSpawnOffset);
        var moth = CreateMoth(species, start, materials);
        SwampAudio.PlayFlap();

        // hover beside the flower, off to one side so it doesn't sit on the orchid's own moth
        Vector3 side = Vector3.Cross(Vector3.up, v.facing).normalized * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        Vector3 hover = HoverPoint(v, mothWingspan * 0.9f)
                        + side * mothWingspan * UnityEngine.Random.Range(0.8f, 1.3f)
                        + Vector3.up * UnityEngine.Random.Range(-0.1f, 0.15f);

        // fly in (fading in as it leaves the centre)
        var fades = PrepareFade(moth);
        Vector3 p1 = start + Vector3.up * 1.2f + side * 1.5f + anchor.forward * 0.8f;
        Vector3 p2 = hover + v.facing * 2f + Vector3.up * 0.8f;
        yield return Fly(moth, start, p1, p2, hover, a => SetFade(fades, Mathf.Clamp01(a * 6f)));
        EndFade(fades);

        // feed: hover facing the flower with small bobs
        float fed = 0f;
        var faceFlower = Quaternion.LookRotation(-v.facing, Vector3.up);
        while (fed < extraMothFeedSeconds && v.orchid != null && v.phase != Phase.Fading)
        {
            fed += Time.deltaTime;
            Vector3 offset = Vector3.up * Mathf.Sin(fed * 2.6f) * 0.03f + side * Mathf.Sin(fed * 1.1f) * 0.03f;
            moth.transform.position = Vector3.Lerp(moth.transform.position, hover + offset, 1f - Mathf.Exp(-6f * Time.deltaTime));
            moth.transform.rotation = Quaternion.Slerp(moth.transform.rotation, faceFlower, 1f - Mathf.Exp(-4f * Time.deltaTime));
            yield return null;
        }

        // fly off up and away, fading out
        fades = PrepareFade(moth);
        Vector3 from = moth.transform.position;
        Vector3 away = from + (v.facing + side + Vector3.up).normalized * 6f + Vector3.up * 2f;
        yield return Fly(moth, from, from + v.facing * 1.5f + Vector3.up * 0.5f, away - side * 2f, away,
                         a => SetFade(fades, 1f - Mathf.SmoothStep(0f, 1f, a)));

        HideAndDestroy(moth);
        foreach (var fd in fades) if (fd.fadeMat != null) Destroy(fd.fadeMat);
        foreach (var m in materials) if (m != null) Destroy(m);
        _extraMothCount--;
    }

    /// <summary>Moves `moth` along a curve, facing where it's going. `progress` gets 0..1 each frame (for fading).</summary>
    IEnumerator Fly(GameObject moth, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Action<float> progress)
    {
        float length = Vector3.Distance(a, b) + Vector3.Distance(b, c) + Vector3.Distance(c, d);
        float duration = Mathf.Max(2f, length / Mathf.Max(0.1f, flightSpeed));
        float t = 0f;
        Vector3 prev = a;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 p = Bezier(a, b, c, d, u);
            moth.transform.position = p;
            Vector3 vel = p - prev;
            if (vel.sqrMagnitude > 1e-8f)
                moth.transform.rotation = Quaternion.Slerp(moth.transform.rotation,
                    Quaternion.LookRotation(vel.normalized, Vector3.up), 1f - Mathf.Exp(-8f * Time.deltaTime));
            prev = p;
            progress?.Invoke(Mathf.Clamp01(t));
            yield return null;
        }
    }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    void ApplyMothMaterial(GameObject model, MothSpecies species, List<Material> created)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var src = r.sharedMaterials;
            var mats = new Material[Mathf.Max(1, src.Length)];
            for (int i = 0; i < mats.Length; i++)
            {
                Material m;
                var srcMat = i < src.Length ? src[i] : null;
                if (lit != null && (species.texture != null || srcMat == null))
                {
                    m = new Material(lit);
                    if (species.texture != null) m.SetTexture("_BaseMap", species.texture);
                    m.SetFloat("_Smoothness", 0.2f);
                }
                else if (srcMat != null) m = new Material(srcMat);
                else continue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", species.tint);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);   // thin wings
                m.name = $"{species.name} (interaction)";
                created.Add(m);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
    }

    void StartWings(GameObject model, float speed)
    {
        if (flyClip == null) return;
        var animator = model.GetComponent<Animator>();       // generic FBX import adds one; it would fight the legacy clip
        if (animator != null) animator.enabled = false;
        var anim = model.GetComponent<Animation>();
        if (anim == null) anim = model.AddComponent<Animation>();
        if (anim.GetClip(flyClip.name) == null) anim.AddClip(flyClip, flyClip.name);
        anim.wrapMode = WrapMode.Loop;
        // keep flapping even when no normal camera "sees" the moth: the dome's cubemap rendering doesn't
        // count as being seen, so with the default culling the wings froze on the dome
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        anim.clip = flyClip;
        anim[flyClip.name].speed = speed;
        anim.Play(flyClip.name);
    }

    // ------------------------------------------------------------------ size & fading helpers
    /// <summary>Scales `t` so its rendered bounds' largest side equals `size` meters.</summary>
    static void FitSize(Transform t, float size, out float scale)
    {
        scale = 1f;
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (max < 1e-5f) return;
        scale = size / max;
        t.localScale *= scale;
    }

    struct FadeSlot { public Renderer r; public Material[] original; public Material fadeMat; public int index; }

    List<FadeSlot> PrepareFade(GameObject go)
    {
        var list = new List<FadeSlot>();
        if (go == null) return list;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var orig = r.sharedMaterials;
            var swapped = new Material[orig.Length];
            for (int i = 0; i < orig.Length; i++)
            {
                if (orig[i] == null) continue;
                var m = new Material(orig[i]) { name = orig[i].name + " (fade)" };
                MakeTransparent(m);
                swapped[i] = m;
                list.Add(new FadeSlot { r = r, original = orig, fadeMat = m, index = i });
            }
            r.sharedMaterials = swapped;
        }
        return list;
    }

    static void SetFade(List<FadeSlot> slots, float alpha)
    {
        foreach (var s in slots)
        {
            if (s.fadeMat == null) continue;
            if (s.fadeMat.HasProperty("_BaseColor"))
            {
                var c = s.fadeMat.GetColor("_BaseColor"); c.a = alpha; s.fadeMat.SetColor("_BaseColor", c);
            }
            if (s.fadeMat.HasProperty("_Fade")) s.fadeMat.SetFloat("_Fade", alpha);   // orchid root shader
        }
    }

    /// <summary>Back to the opaque materials (no sorting issues while the objects are fully visible).</summary>
    static void EndFade(List<FadeSlot> slots)
    {
        var done = new HashSet<Renderer>();
        foreach (var s in slots)
        {
            if (s.r != null && done.Add(s.r)) s.r.sharedMaterials = s.original;
            if (s.fadeMat != null) Destroy(s.fadeMat);
        }
    }

    static void MakeTransparent(Material m)
    {
        if (!m.HasProperty("_Surface")) return;   // non-URP-Lit shader: fade just won't apply
        m.SetFloat("_Surface", 1f);                 // Transparent
        m.SetFloat("_Blend", 0f);                   // Alpha
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>
    /// Switches an object's renderers off, then destroys it. Its materials are destroyed right after, and a
    /// renderer still switched on with a destroyed material makes the GPU Resident Drawer report
    /// "A BatchDrawCommand was submitted with an invalid ... Material ID".
    /// </summary>
    static void HideAndDestroy(GameObject go)
    {
        if (go == null) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        Destroy(go);
    }

    void OnDestroy()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = false;
        foreach (var m in _spawnedMaterials) if (m != null) Destroy(m);
        foreach (var v in _visits)
            foreach (var m in v.mothMaterials) if (m != null) Destroy(m);
    }

    void OnDrawGizmosSelected()
    {
        var a = anchor != null ? anchor : transform;
        Gizmos.color = new Color(0.9f, 0.95f, 1f, 0.6f);
        if (orchidArea != null)
        {
            Gizmos.matrix = orchidArea.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else
        {
            Gizmos.DrawWireSphere(a.position, minTreeDistance);
            Gizmos.DrawWireSphere(a.position, maxTreeDistance);
        }
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(a.TransformPoint(mothSpawnOffset), 0.05f);
    }
}
