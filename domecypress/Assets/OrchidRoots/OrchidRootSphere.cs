using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Unity version of the orchid root animation from DomeCypress/roots.blend.
/// Ten root nodes appear on a sphere; from each one thick, green orchid roots creep across the surface
/// (some lift off as aerial roots), sway gently, and late in the animation a ghost orchid opens from
/// each node, growing inward toward the middle of the sphere.
///
/// The root paths and timing come from Resources/OrchidRoots/orchid_roots.json, exported from the
/// Blender file, so Unity grows the same roots in the same order. Times are Blender frames (24 fps).
/// The tube mesh is rebuilt every frame on the CPU (about 25k vertices).
/// </summary>
[DisallowMultipleComponent]
public class OrchidRootSphere : MonoBehaviour
{
    const string ResourceFolder = "OrchidRoots/";

    [Header("Data (loaded from Resources/OrchidRoots when empty)")]
    public TextAsset rootData;
    public Material rootMaterial;
    public GameObject orchidModel;
    public Texture2D orchidBaseColor;
    public Texture2D orchidNormal;

    [Header("Dome")]
    [Tooltip("Grow the roots on the INSIDE of a large sphere around the viewer (for the fulldome camera). " +
             "Off = the original Blender layout: roots on the outside of a 3.8 m sphere.")]
    public bool wrapInsideDome = true;
    [Tooltip("Radius of the root sphere around the viewer, in meters. Everything (root thickness, orchids, " +
             "sway) scales with it, so the look stays the same as in Blender.")]
    public float domeRadius = 10f;
    [Tooltip("Optional: keep the roots centered on this transform (drag in the Dome Tools camera rig).")]
    public Transform followCenter;
    [Tooltip("World-space direction of the dome's zenith (straight up for a standard planetarium tilt).")]
    public Vector3 domeUp = Vector3.up;

    [Header("Dome layout (Wrap Inside Dome)")]
    [Tooltip("Change to get a different random layout.")]
    public int seed = 7;
    [Range(3, 16)] public int nodeCount = 10;
    [Tooltip("Root nodes sit between these elevations above the dome's horizon (0 = base, 90 = top center).")]
    [Range(0f, 80f)] public float nodeElevationMin = 6f;
    [Range(0f, 80f)] public float nodeElevationMax = 38f;
    [Tooltip("Roots per node that climb toward the top of the dome.")]
    [Range(1, 4)] public int climbersPerNode = 2;
    [Tooltip("Shorter roots per node that spread out and grip around it.")]
    [Range(0, 8)] public int gripRootsPerNode = 4;
    [Tooltip("Number of ghost orchids, spaced evenly around the dome starting at north (4 = N, E, S, W).")]
    [Range(1, 8)] public int orchidCount = 4;
    [Tooltip("World-space direction that counts as north on the dome.")]
    public Vector3 domeNorth = Vector3.forward;
    [Tooltip("Turns the whole ring of orchids around the dome, in degrees (45 = the four flowers sit on the " +
             "diagonals instead of N, E, S, W).")]
    [Range(-180f, 180f)] public float orchidAzimuthOffset = 0f;
    [Tooltip("How high the orchids sit on the dome (0 = base, 90 = top center).")]
    [Range(20f, 89f)] public float orchidElevation = 60f;
    [Tooltip("Frames between one orchid opening and the next (N, then E, S, W).")]
    public float orchidStagger = 15f;
    [Tooltip("All the orchids open at the same moment (when the first one would have). Off = one after another.")]
    public bool bloomTogether = true;
    [Tooltip("Extra seconds the flowers take to open (0 = the Blender timing).")]
    public float extraBloomSeconds = 0f;
    [Tooltip("The open flowers sway gently. Off = they hold perfectly still.")]
    public bool flowerSway = false;
    [Tooltip("Elevation range where the other climbing roots (no flower) stop.")]
    [Range(30f, 89f)] public float orchidElevationMin = 86f;
    [Range(30f, 89f)] public float orchidElevationMax = 89f;
    [Tooltip("How strongly the climbing roots steer toward the top center (0 = wander freely).")]
    [Range(0f, 0.5f)] public float climbSteer = 0.004f;
    [Tooltip("Blender frame by which every climbing root reaches the top (they speed up if needed).")]
    public float climbDoneFrame = 380f;
    [Tooltip("When the orchids start opening, as a share of the root growth: 0 = as soon as the first roots " +
             "appear, 0.5 = halfway through, 1 = when the roots are done. Each flower's own root speeds up " +
             "so it still reaches the flower in time.")]
    [Range(0f, 1f)] public float orchidsStartAt = 0.5f;

    [Header("Playback")]
    public bool playOnStart = true;
    [Tooltip("1 = the Blender timing (500 frames at 24 fps, about 21 seconds). 2 = twice as fast.")]
    public float playbackSpeed = 2f;
    [Tooltip("Restart the animation after it finishes.")]
    public bool loop = false;
    public float holdAtEndSeconds = 6f;
    [Tooltip("Start part-way through, in Blender frames.")]
    public float startFrame = 0f;

    [Header("Roots")]
    [Tooltip("Multiplies the Blender grow speed.")]
    public float growSpeedMultiplier = 1f;
    public float swayStrengthMultiplier = 1f;
    [Range(4, 16)] public int sides = 8;
    public ShadowCastingMode castShadows = ShadowCastingMode.On;

    [Header("Ghost orchids")]
    public bool showOrchids = true;
    [Tooltip("Multiplies the Blender orchid scale.")]
    public float orchidScaleMultiplier = 2f;
    [Tooltip("Orchids point toward the middle of the sphere (as in Blender). Off = they point outward.")]
    public bool orchidsFaceInward = true;
    [Range(0f, 1f)] public float orchidSmoothness = 0.35f;
    [Tooltip("Dome layout: turn each flower so its face looks at the audience in the center, with the stem " +
             "continuing along the root. Off = stem points at the center (you see the back of the flower).")]
    public bool presentFlowerToCenter = true;
    [Tooltip("Turns each flower around its stem, in degrees, if the face isn't quite square to the audience.")]
    [Range(-180f, 180f)] public float flowerFaceYaw = 17f;
    [Tooltip("Tips the flower face toward (+) or away from (-) the center, in degrees.")]
    [Range(-60f, 60f)] public float flowerFaceTilt = 0f;
    [Tooltip("Final pose: every flower faces the audience with its long lip tails pointing at the top center " +
             "of the dome (the middle of the dome master image), so the four flowers form an even pinwheel. " +
             "Off = each flower follows the direction its root was growing.")]
    public bool tailsPointToCenter = true;
    [Tooltip("Spins each flower around the axis it looks along, in degrees (fine-tunes where the tails point).")]
    [Range(-180f, 180f)] public float flowerFaceRoll = 0f;
    [Tooltip("Every flower grows with its stem toward the edge of the dome and its flower toward the centre " +
             "(the top of the dome), its face turned to the audience. Applies on top of the saved pose too " +
             "(the saved positions and sizes are kept). Off = the saved / calculated rotation as it was.")]
    public bool stemFromEdge = true;
    [Tooltip("Every orchid copies the way the Template Orchid sits on the dome (as posed by hand and saved with " +
             "Tools > Orchids > Save Play Mode Values): same angle to the dome edge, the centre and the audience, " +
             "each in its own position. Overrides Stem From Edge.")]
    public bool copyTemplateOrchid = true;
    [Tooltip("Which saved orchid (index in the saved pose, 0 = first) the others copy.")]
    public int templateOrchid = 0;
    [Tooltip("Place the orchids evenly around the dome at the compass points (4 orchids = north, east, south, " +
             "west), all at the same height, instead of the saved positions.")]
    public bool compassLayout = true;
    [Tooltip("Turns the whole ring of orchids around the dome, in degrees (0 = the first one due north).")]
    [Range(-180f, 180f)] public float compassAzimuthOffset = 10f;
    [Tooltip("How far down the dome the ring of orchids sits, in degrees above the horizon " +
             "(85 = bunched at the top, 0 = at the edge of the dome). Lower = further apart.")]
    [Range(-20f, 85f)] public float ringHeight = 4f;
    [Tooltip("Size of the orchids in the ring compared with the saved pose (1 = as posed). The posed flowers are " +
             "large and reach in toward the middle, so smaller keeps them from crowding the top of the dome.")]
    [Range(0.2f, 1.5f)] public float ringScale = 0.825f;

    [Header("Ending")]
    [Tooltip("After the last orchid has opened, the swaying fades out so everything comes to rest in its final pose.")]
    public bool settleAtEnd = true;
    [Tooltip("How long the sway takes to fade out, in seconds.")]
    public float settleSeconds = 2f;

    [Header("Hand-placed final pose")]
    [Tooltip("Play mode only: once the orchids have opened, the script stops moving them so you can drag, " +
             "rotate and scale the GhostOrchid clones by hand. Then use Tools > Orchids > Save Play Mode Values.")]
    public bool poseOrchidsByHand = false;
    [Tooltip("Open each orchid in the pose saved with Tools > Orchids > Save Play Mode Values " +
             "(Resources/OrchidRoots/orchid_final_pose.json) instead of the calculated one.")]
    public bool useSavedPose = true;
    [Tooltip("Optional: a saved pose file. Empty = Resources/OrchidRoots/orchid_final_pose.json.")]
    public TextAsset savedPose;

    [Header("Root fade")]
    [Tooltip("Once the first orchid appears, the roots (and their round nodes) fade away, leaving only the flowers.")]
    public bool fadeRootsAfterBloom = true;
    [Tooltip("Seconds to wait after the first orchid appears before the roots start fading (0 = right away).")]
    public float rootFadeDelaySeconds = 0f;
    [Tooltip("How long the fade takes, in seconds.")]
    public float rootFadeSeconds = 3f;

    [Header("Sphere")]
    [Tooltip("The sphere the roots grow on. The inward-facing orchids sit inside it, so they are hidden " +
             "from a camera outside the sphere while it's shown.")]
    public bool showSphere = false;
    public Material sphereMaterial;

    public float CurrentFrame => _frame;

    /// <summary>True once every orchid has opened and the roots have faded (or the sway has settled).</summary>
    public bool IsFinished => _data != null && _lastBloomEnd > 0f && _frame >= FinishFrame;
    /// <summary>True once every orchid is fully open.</summary>
    public bool OrchidsOpen => _data != null && _lastBloomEnd > 0f && _frame >= _lastBloomEnd;
    float FinishFrame => fadeRootsAfterBloom
        ? Mathf.Max(_endFrame, _firstBloom + _data.fps * (rootFadeDelaySeconds + rootFadeSeconds))
        : _endFrame + _data.fps * (settleAtEnd ? settleSeconds : 0f);

    /// <summary>The ghost orchid clones (one pivot per flower).</summary>
    public IEnumerable<Transform> Orchids
    {
        get { foreach (var r in _roots) if (r.orchidPivot != null) yield return r.orchidPivot; }
    }

    // ---------------------------------------------------------------- data
    [Serializable] class RootFile
    {
        public float sphereRadius, innerRadius, fps, frameEnd;
        public float growSpeed, swayStrength, swaySpeed, swayScale, orchidScale, bloomDuration;
        public RootJson[] roots;
    }
    [Serializable] class RootJson
    {
        public float start, rad, aerial, bloom; public int crownFirst; public float[] p;
        [NonSerialized] public bool tipOrchid;   // dome layout: orchid blooms at the end of this root
        [NonSerialized] public float speed = 1f; // dome layout: per-root grow speed multiplier
    }

    // saved by Tools > Orchids > Save Play Mode Values
    [Serializable] class PoseFile { public PoseOrchid[] orchids; }
    [Serializable] class PoseOrchid { public float[] localPosition, localRotation, localScale; }
    PoseOrchid[] _pose;

    MeshRenderer _rootsRenderer;
    MaterialPropertyBlock _fadeBlock;
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    class Root
    {
        public RootJson src;
        public Vector3[] basePts;   // object space, sphere centered at the origin
        public float[] u;           // 0..1 along the root, by length (Blender "Spline Parameter")
        public Vector3[] pts;       // swayed this frame
        public float[] s;           // cumulative length of pts
        public float length;        // of basePts
        public Transform orchidPivot;
        public float spin;
    }

    RootFile _data;
    readonly List<Root> _roots = new List<Root>();
    Mesh _mesh;
    GameObject _rootsObject, _sphereObject;
    Material _orchidMaterial;
    float _frame, _clockStart;
    bool _playing;
    Vector3 _domeUpLocal = Vector3.up;   // dome zenith in this object's space
    float _lastBloomEnd = float.MaxValue; // Blender frame the last orchid is fully open
    float _endFrame = float.MaxValue;     // last orchid open AND every root fully grown
    float _firstBloom = float.MaxValue;   // Blender frame the first orchid starts to open

    readonly List<Vector3> _verts = new List<Vector3>(32000);
    readonly List<Vector3> _normals = new List<Vector3>(32000);
    readonly List<Vector2> _uvs = new List<Vector2>(32000);
    readonly List<int> _tris = new List<int>(200000);
    Vector3[] _nubVerts;
    int[] _nubTris;
    // ring frame scratch
    readonly List<Vector3> _ringC = new List<Vector3>(256);
    readonly List<float> _ringD = new List<float>(256);
    readonly List<float> _ringS = new List<float>(256);

    // ---------------------------------------------------------------- setup
    void Start()
    {
        if (rootData == null) rootData = Resources.Load<TextAsset>(ResourceFolder + "orchid_roots");
        if (rootMaterial == null) rootMaterial = Resources.Load<Material>(ResourceFolder + "OrchidRoot");
        if (rootMaterial == null)
        {
            var shader = Shader.Find("Cypress/OrchidRoot");
            if (shader != null) rootMaterial = new Material(shader);
        }
        if (orchidModel == null) orchidModel = Resources.Load<GameObject>(ResourceFolder + "GhostOrchid");
        if (orchidBaseColor == null) orchidBaseColor = Resources.Load<Texture2D>(ResourceFolder + "GhostOrchid_BaseColor");
        if (orchidNormal == null) orchidNormal = Resources.Load<Texture2D>(ResourceFolder + "GhostOrchid_Normal");

        if (rootData == null)
        {
            Debug.LogError("OrchidRootSphere: orchid_roots.json not found (Resources/OrchidRoots).", this);
            enabled = false;
            return;
        }
        _data = JsonUtility.FromJson<RootFile>(rootData.text);
        if (_data.fps <= 0) _data.fps = 24;
        _data.bloomDuration += Mathf.Max(0f, extraBloomSeconds) * _data.fps;   // slower opening
        if (wrapInsideDome) BuildDomeLayout();
        if (followCenter != null) transform.position = followCenter.position;

        BuildRoots();
        BuildNubTemplate();
        BuildRootRenderer();
        if (showSphere && !wrapInsideDome) BuildSphere();
        if (showOrchids) BuildOrchids();

        // hand-placed final pose for the orchids, if one was saved
        if (useSavedPose)
        {
            if (savedPose == null) savedPose = Resources.Load<TextAsset>(ResourceFolder + "orchid_final_pose");
            if (savedPose != null) _pose = JsonUtility.FromJson<PoseFile>(savedPose.text).orchids;
        }

        // when is the last flower fully open? (the sway settles after that)
        _lastBloomEnd = 0f;
        _firstBloom = float.MaxValue;
        foreach (var root in _roots)
            if (root.orchidPivot != null)
            {
                _lastBloomEnd = Mathf.Max(_lastBloomEnd, root.src.bloom + Mathf.Max(1f, _data.bloomDuration));
                _firstBloom = Mathf.Min(_firstBloom, root.src.bloom);
            }
        // opening together: they are all fully open one bloom-duration after the first one starts
        if (bloomTogether && _firstBloom < float.MaxValue)
            _lastBloomEnd = _firstBloom + Mathf.Max(1f, _data.bloomDuration);

        // the roots settle and fade only once they have all finished growing too
        _endFrame = _lastBloomEnd;
        float gsAll = _data.growSpeed * growSpeedMultiplier;
        foreach (var root in _roots)
        {
            float speed = root.src.speed > 0f ? root.src.speed : 1f;
            if (gsAll > 0f) _endFrame = Mathf.Max(_endFrame, root.src.start + root.length / (gsAll * speed));
        }

        _frame = startFrame;
        if (playOnStart) Play(startFrame);
        Rebuild();
    }

    /// <summary>Start (or restart) the animation from a Blender frame.</summary>
    public void Play(float fromFrame = 0f)
    {
        _playing = true;
        _clockStart = Time.time - fromFrame / (_data.fps * Mathf.Max(0.0001f, playbackSpeed));
    }

    public void Pause() => _playing = false;

    /// <summary>Jump to a Blender frame without playing.</summary>
    public void SetFrame(float frame)
    {
        _playing = false;
        _frame = frame;
        Rebuild();
    }

    /// <summary>
    /// Dome layout: root nodes low on the inside of a dome-sized sphere (from the base up to mid-height),
    /// each sending a few roots climbing toward the top center plus shorter gripping roots; a ghost orchid
    /// blooms at the end of each node's main climber, near the top, pointing down at the audience.
    /// Root thickness, sway and grow speed are the Blender values scaled to the dome radius.
    /// </summary>
    void BuildDomeLayout()
    {
        float k = Mathf.Max(0.01f, domeRadius) / _data.sphereRadius;
        _data.growSpeed *= k;        // same timing as Blender at the bigger size
        _data.swayStrength *= k;
        _data.swayScale /= k;
        _data.orchidScale *= k;
        _data.sphereRadius = Mathf.Max(0.01f, domeRadius);
        float R = _data.sphereRadius;

        Vector3 z = (Quaternion.Inverse(transform.rotation) * (domeUp.sqrMagnitude > 0 ? domeUp : Vector3.up)).normalized;
        _domeUpLocal = z;
        // x0 = north along the dome's horizon, y0 = 90 degrees around from it
        Vector3 north = Quaternion.Inverse(transform.rotation) * (domeNorth.sqrMagnitude > 0 ? domeNorth : Vector3.forward);
        Vector3 x0 = north - z * Vector3.Dot(north, z);
        if (x0.sqrMagnitude < 1e-6f) x0 = Vector3.Cross(z, Mathf.Abs(z.y) < 0.9f ? Vector3.up : Vector3.right);
        x0.Normalize();
        Vector3 y0 = Vector3.Cross(z, x0).normalized;
        _domeNorthLocal = x0;
        _domeEastLocal = y0;

        var rng = new System.Random(seed);
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float step = 0.055f * k;
        float gs = _data.growSpeed;
        var roots = new List<RootJson>();

        float az0 = Rand(0f, 360f);
        var starts = new List<float>();
        for (int c = 0; c < nodeCount; c++) starts.Add(Rand(20f, 160f));
        starts.Sort();
        var order = new List<int>();
        for (int c = 0; c < nodeCount; c++) order.Add(c);
        for (int c = order.Count - 1; c > 0; c--) { int j = rng.Next(c + 1); (order[c], order[j]) = (order[j], order[c]); }

        Vector3 OnDome(float azDeg, float elDeg)
        {
            float a = azDeg * Mathf.Deg2Rad, e = elDeg * Mathf.Deg2Rad;
            return (Mathf.Cos(e) * (Mathf.Cos(a) * x0 + Mathf.Sin(a) * y0) + Mathf.Sin(e) * z).normalized;
        }

        var nodes = new Vector3[nodeCount];
        for (int c = 0; c < nodeCount; c++)
            nodes[c] = OnDome(az0 + 360f * c / nodeCount + Rand(-12f, 12f),
                Rand(Mathf.Min(nodeElevationMin, nodeElevationMax), Mathf.Max(nodeElevationMin, nodeElevationMax)));

        // orchids at compass points; each one is reached by a root from the nearest free node
        int flowers = Mathf.Min(orchidCount, nodeCount);
        var nodeTarget = new int[nodeCount];
        for (int c = 0; c < nodeCount; c++) nodeTarget[c] = -1;
        var targets = new Vector3[flowers];
        for (int j = 0; j < flowers; j++)
        {
            float az = orchidAzimuthOffset + 360f * j / flowers;
            targets[j] = OnDome(az, orchidElevation);
            Vector3 flatT = OnDome(az, 0f);
            int best = -1; float bestAngle = float.MaxValue;
            for (int c = 0; c < nodeCount; c++)
            {
                if (nodeTarget[c] >= 0) continue;
                Vector3 flatN = nodes[c] - z * Vector3.Dot(nodes[c], z);
                float ang = Vector3.Angle(flatN, flatT);
                if (ang < bestAngle) { bestAngle = ang; best = c; }
            }
            if (best >= 0) nodeTarget[best] = j;
        }

        for (int c = 0; c < nodeCount; c++)
        {
            Vector3 n0 = nodes[c];
            float nodeStart = starts[order[c]];
            int target = nodeTarget[c];

            int total = climbersPerNode + gripRootsPerNode;
            Vector3 upT = TangentToward(n0, z);
            Vector3 sideT = Vector3.Cross(n0, upT).normalized;
            // every root leaves the node center in its own direction, evenly spaced like a star;
            // the climbers take the slots facing the top of the dome
            float slot = 360f / total;
            for (int i = 0; i < total; i++)
            {
                bool climber = i < climbersPerNode;
                bool flower = climber && i == 0 && target >= 0;
                Vector3 goal = flower ? targets[target] : Vector3.zero;
                float rad = Rand(0.063f, 0.098f) * k * (climber ? 1.15f : 1f);
                float aerialRoll = (float)rng.NextDouble();
                bool aerial = !climber && aerialRoll < 0.35f;
                Vector3 d;
                float maxLen;
                float targetEl = 0f;
                if (climber)
                {
                    float a = ((i - (climbersPerNode - 1) * 0.5f) * slot + Rand(-0.15f, 0.15f) * slot) * Mathf.Deg2Rad;
                    d = (Mathf.Cos(a) * upT + Mathf.Sin(a) * sideT).normalized;
                    targetEl = Rand(Mathf.Min(orchidElevationMin, orchidElevationMax), Mathf.Max(orchidElevationMin, orchidElevationMax));
                    maxLen = R * Mathf.PI;   // stops on elevation instead
                }
                else
                {
                    float a = ((i - (climbersPerNode - 1) * 0.5f) * slot + Rand(-0.2f, 0.2f) * slot) * Mathf.Deg2Rad;
                    d = (Mathf.Cos(a) * upT + Mathf.Sin(a) * sideT).normalized;
                    maxLen = Rand(1.3f, 3.2f) * k * (rng.NextDouble() < 0.25 ? 0.6f : 1f);
                }

                // walk along the inside of the sphere
                var pts = new List<Vector3>();
                Vector3 p = n0 * R;
                float w = Rand(-0.15f, 0.15f);
                float af = Rand(0.45f, 0.8f), lift = Rand(0.35f, 1.0f) * k;
                float len = 0f;
                int guard = 0;
                while (guard++ < 4000)
                {
                    Vector3 nrm = p.normalized;
                    float f = climber ? 0f : Mathf.Clamp01(len / maxLen);
                    float h = rad * 0.8f + rad * 1.6f * Mathf.Max(0f, 1f - len / (step * 4f));
                    if (aerial && f > af) h += Mathf.Pow((f - af) / (1f - af), 1.6f) * lift;
                    pts.Add(nrm * (R - h));              // inside surface: offset toward the viewer

                    float elev = 90f - Vector3.Angle(nrm, z);
                    if (flower) { if (Vector3.Angle(nrm, goal) < 0.8f) break; }
                    else if (climber ? elev >= targetEl : len >= maxLen) break;
                    if (len > R * Mathf.PI) break;

                    // wander
                    w += Gauss(rng) * 0.07f; w *= 0.88f;
                    d = Quaternion.AngleAxis(w * 0.35f * Mathf.Rad2Deg, nrm) * d;
                    // steer: climbers toward the top center; everything away from below the horizon
                    Vector3 up = flower ? TangentToward(nrm, goal) : TangentToward(nrm, z);
                    float steer = climber ? climbSteer : 0f;
                    if (flower)   // steer harder as it closes in so it lands right on its compass point
                        steer = Mathf.Max(climbSteer, Mathf.Lerp(0.5f, 0.12f, Mathf.Clamp01(Vector3.Angle(nrm, goal) / 25f)));
                    if (elev < 2f) steer = Mathf.Max(steer, 0.3f);
                    if (steer > 0f && up.sqrMagnitude > 0f) d = Vector3.Slerp(d, up, steer);

                    p += d * step;
                    nrm = p.normalized;
                    p = nrm * R;
                    d = (d - Vector3.Dot(d, nrm) * nrm).normalized;
                    len += step;
                }

                var flat = new float[pts.Count * 3];
                for (int j = 0; j < pts.Count; j++) { flat[j * 3] = pts[j].x; flat[j * 3 + 1] = pts[j].y; flat[j * 3 + 2] = pts[j].z; }
                var rj = new RootJson
                {
                    p = flat, rad = rad, aerial = aerial ? 1f : 0f,
                    start = nodeStart + Rand(0f, 10f), crownFirst = 0,
                    tipOrchid = flower,
                };
                if (climber)
                {
                    float length = (pts.Count - 1) * step;
                    // the orchids open part-way through the root growth (Orchids Start At), one after another;
                    // a flower's root grows fast enough to reach its flower right when it opens
                    float bloomAt = Mathf.Lerp(starts[0], climbDoneFrame, orchidsStartAt) + Mathf.Max(0, target) * orchidStagger;
                    bloomAt = Mathf.Max(bloomAt, rj.start + 24f);   // give the root at least a second to grow
                    float doneBy = flower ? bloomAt : climbDoneFrame;
                    float frames = Mathf.Max(1f, doneBy - rj.start);
                    rj.speed = Mathf.Max(1f, length / gs / frames);
                    float done = rj.start + length / (gs * rj.speed);
                    rj.bloom = flower ? bloomAt : done;
                }
                roots.Add(rj);
            }
        }
        _data.roots = roots.ToArray();
    }

    // ---------------------------------------------------------------- template orchid
    bool _hasTemplateOffset;
    Quaternion _templateOffset;
    Vector3 _templatePos;
    Vector3 _domeNorthLocal = Vector3.forward, _domeEastLocal = Vector3.right;

    /// <summary>Orchid number i at its compass point (evenly spaced around the dome), at the template's distance.</summary>
    Vector3 CompassPosition(int i)
    {
        int count = Mathf.Max(1, _pose != null ? _pose.Length : orchidCount);
        float radius = _templatePos.magnitude;
        float elevation = ringHeight;
        float az = (compassAzimuthOffset + 360f * i / count) * Mathf.Deg2Rad;
        float el = elevation * Mathf.Deg2Rad;
        Vector3 flat = Mathf.Cos(az) * _domeNorthLocal + Mathf.Sin(az) * _domeEastLocal;
        return radius * (Mathf.Cos(el) * flat + Mathf.Sin(el) * _domeUpLocal);
    }

    /// <summary>
    /// A frame tied to the dome at a position: +Z looks at the centre of the dome (the audience),
    /// +Y points up the dome toward the zenith.
    /// </summary>
    Quaternion DomeFrame(Vector3 localPos)
    {
        Vector3 outDir = localPos.normalized;
        Vector3 up = TangentToward(outDir, _domeUpLocal);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.Cross(outDir, Vector3.right);
        return Quaternion.LookRotation(-outDir, up.normalized);
    }

    /// <summary>How the template orchid is turned relative to its own dome frame (worked out once).</summary>
    bool TemplateOffset(out Quaternion offset)
    {
        if (!_hasTemplateOffset)
        {
            var t = _pose[templateOrchid];
            if (t.localPosition == null || t.localPosition.Length != 3 || t.localRotation == null || t.localRotation.Length != 4)
            {
                offset = Quaternion.identity;
                return false;
            }
            var pos = new Vector3(t.localPosition[0], t.localPosition[1], t.localPosition[2]);
            var rot = new Quaternion(t.localRotation[0], t.localRotation[1], t.localRotation[2], t.localRotation[3]);
            _templateOffset = Quaternion.Inverse(DomeFrame(pos)) * rot;
            _templatePos = pos;
            _hasTemplateOffset = true;
        }
        offset = _templateOffset;
        return true;
    }

    void OnValidate() { _hasTemplateOffset = false; }   // re-read if the template changes

    static Vector3 TangentToward(Vector3 n, Vector3 z)
    {
        Vector3 t = z - n * Vector3.Dot(z, n);
        return t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.zero;
    }

    static float Gauss(System.Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
    }

    void BuildRoots()
    {
        foreach (var r in _data.roots)
        {
            int n = r.p.Length / 3;
            var root = new Root
            {
                src = r,
                basePts = new Vector3[n],
                u = new float[n],
                pts = new Vector3[n],
                s = new float[n],
            };
            for (int i = 0; i < n; i++)
                root.basePts[i] = new Vector3(r.p[i * 3], r.p[i * 3 + 1], r.p[i * 3 + 2]);
            float total = 0;
            root.u[0] = 0;
            for (int i = 1; i < n; i++)
            {
                total += Vector3.Distance(root.basePts[i - 1], root.basePts[i]);
                root.u[i] = total;
            }
            for (int i = 1; i < n; i++) root.u[i] = total > 0 ? root.u[i] / total : 0;
            root.length = total;
            root.spin = Hash01(_roots.Count, 11, 3) * 360f;
            _roots.Add(root);
        }
    }

    void BuildRootRenderer()
    {
        _mesh = new Mesh { name = "OrchidRoots", indexFormat = IndexFormat.UInt32 };
        _mesh.MarkDynamic();
        float b = (_data.sphereRadius + 2f) * 2f;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(b, b, b));

        _rootsObject = new GameObject("Roots");
        _rootsObject.transform.SetParent(transform, false);
        _rootsObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = _rootsObject.AddComponent<MeshRenderer>();
        _rootsRenderer = mr;
        mr.sharedMaterial = rootMaterial;
        mr.shadowCastingMode = castShadows;
    }

    void BuildSphere()
    {
        _sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _sphereObject.name = "Sphere";
        var col = _sphereObject.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _sphereObject.transform.SetParent(transform, false);
        _sphereObject.transform.localScale = Vector3.one * (_data.sphereRadius * 2f);
        var mr = _sphereObject.GetComponent<MeshRenderer>();
        if (sphereMaterial != null) mr.sharedMaterial = sphereMaterial;
        else
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var m = new Material(lit) { name = "OrchidSphere" };
                m.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f));
                m.SetFloat("_Smoothness", 0.25f);
                mr.sharedMaterial = m;
            }
        }
    }

    void BuildOrchids()
    {
        if (orchidModel == null)
        {
            Debug.LogWarning("OrchidRootSphere: GhostOrchid model not found; skipping the orchids.", this);
            return;
        }
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null && orchidBaseColor != null)
        {
            _orchidMaterial = new Material(lit) { name = "GhostOrchid" };
            _orchidMaterial.SetTexture("_BaseMap", orchidBaseColor);
            _orchidMaterial.SetColor("_BaseColor", Color.white);
            if (orchidNormal != null)
            {
                _orchidMaterial.SetTexture("_BumpMap", orchidNormal);
                _orchidMaterial.EnableKeyword("_NORMALMAP");
            }
            _orchidMaterial.SetFloat("_Metallic", 0f);
            _orchidMaterial.SetFloat("_Smoothness", orchidSmoothness);
            _orchidMaterial.SetFloat("_Cull", (float)CullMode.Off);   // thin petals: draw both sides
            _orchidMaterial.doubleSidedGI = true;
        }

        foreach (var root in _roots)
        {
            bool wanted = wrapInsideDome ? root.src.tipOrchid : root.src.crownFirst != 0;
            if (!wanted) continue;
            var pivot = new GameObject("GhostOrchid").transform;
            pivot.SetParent(transform, false);
            var model = Instantiate(orchidModel, pivot, false);   // keeps the import's own axis fix
            if (_orchidMaterial != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = _orchidMaterial;
                    r.sharedMaterials = mats;
                }
            pivot.gameObject.SetActive(false);
            root.orchidPivot = pivot;
        }
    }

    // ---------------------------------------------------------------- per frame
    void Update()
    {
        if (_data == null) return;
        if (followCenter != null) transform.position = followCenter.position;
        if (_playing)
        {
            float speed = Mathf.Max(0.0001f, playbackSpeed);
            _frame = (Time.time - _clockStart) * _data.fps * speed;
            if (loop && _frame > _data.frameEnd + holdAtEndSeconds * _data.fps)
                Play(0f);
        }
        Rebuild();
    }

    void Rebuild()
    {
        float frame = _frame;
        float seconds = Time.time;
        float growSpeed = _data.growSpeed * growSpeedMultiplier;
        float sway = _data.swayStrength * swayStrengthMultiplier * SettleWeight(frame);
        float swayW = seconds * _data.swaySpeed;

        _verts.Clear(); _normals.Clear(); _uvs.Clear(); _tris.Clear();

        // fade the roots out after the orchids have opened (the shader dissolves them with _Fade)
        float fade = RootFade(frame);
        if (_rootsRenderer != null)
        {
            _rootsRenderer.enabled = fade > 0f;
            if (_fadeBlock == null) _fadeBlock = new MaterialPropertyBlock();
            _fadeBlock.SetFloat(FadeId, fade);
            _rootsRenderer.SetPropertyBlock(_fadeBlock);
        }

        foreach (var root in _roots)
        {
            if (fade <= 0f) break;   // fully faded: no need to build the root mesh
            var r = root.src;

            // crown nub (Blender: icosphere, radius rad * 1.7, pops in over 12 frames)
            float nubT = Mathf.Clamp01((frame - r.start + 4f) / 12f);
            if (nubT > 0f) AddNub(root.basePts[0], r.rad * (wrapInsideDome ? 2.2f : 1.7f) * nubT);

            // growth: constant speed along the root, eased out
            float totalBase = root.length;
            if (totalBase <= 0f) continue;
            float t = Mathf.Clamp01((frame - r.start) * growSpeed * (r.speed > 0f ? r.speed : 1f) / totalBase);
            float grow = 1f - (1f - t) * (1f - t);
            if (grow < 0.002f) continue;

            // sway this frame (only the part that's grown, plus one point)
            int n = root.basePts.Length;
            float approxLen = grow * totalBase;
            int needed = n;
            for (int i = 1; i < n; i++)
                if (root.u[i] * totalBase > approxLen) { needed = Mathf.Min(n, i + 2); break; }

            float total = 0f;
            for (int i = 0; i < needed; i++)
            {
                root.pts[i] = SwayPoint(root.basePts[i], root.u[i], r.aerial, sway, swayW);
                if (i > 0) total += Vector3.Distance(root.pts[i - 1], root.pts[i]);
                root.s[i] = total;
            }
            // trim by factor of the full (swayed) length; extrapolate total with the base ratio
            float fullLen = needed == n ? total : totalBase * (total / Mathf.Max(1e-5f, root.u[needed - 1] * totalBase));
            float grown = Mathf.Min(grow * fullLen, total);

            _ringC.Clear(); _ringD.Clear(); _ringS.Clear();
            for (int i = 0; i < needed; i++)
            {
                if (root.s[i] >= grown)
                {
                    if (i > 0)
                    {
                        float seg = root.s[i] - root.s[i - 1];
                        float k = seg > 0 ? (grown - root.s[i - 1]) / seg : 0;
                        _ringC.Add(Vector3.Lerp(root.pts[i - 1], root.pts[i], k));
                        _ringS.Add(grown);
                    }
                    break;
                }
                _ringC.Add(root.pts[i]);
                _ringS.Add(root.s[i]);
            }
            if (_ringC.Count < 2) continue;
            float tipS = _ringS[_ringS.Count - 1];
            AddTube(r.rad, tipS);
        }

        _mesh.Clear();
        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_normals);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_tris, 0, false);
        float b = (_data.sphereRadius + 2f) * 2f;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(b, b, b));

        UpdateOrchids(frame, seconds, sway, swayW);
    }

    /// <summary>Root visibility: 1 = solid, fading to 0 once the first orchid appears (if Fade Roots After Bloom is on).</summary>
    float RootFade(float frame)
    {
        if (!fadeRootsAfterBloom || _lastBloomEnd <= 0f) return 1f;   // no orchids = nothing to wait for
        float secondsPast = (frame - _firstBloom) / _data.fps - rootFadeDelaySeconds;   // starts when the first orchid appears
        if (secondsPast <= 0f) return 1f;
        float t = Mathf.Clamp01(secondsPast / Mathf.Max(0.01f, rootFadeSeconds));
        return 1f - t * t * (3f - 2f * t);   // smooth fade
    }

    /// <summary>1 while the animation plays, fading to 0 after the last orchid opens (if Settle At End is on).</summary>
    float SettleWeight(float frame)
    {
        if (!settleAtEnd) return 1f;
        float secondsPast = (frame - _endFrame) / _data.fps;
        if (secondsPast <= 0f) return 1f;
        float t = Mathf.Clamp01(secondsPast / Mathf.Max(0.01f, settleSeconds));
        return 1f - t * t * (3f - 2f * t);   // smooth fade
    }

    Vector3 SwayPoint(Vector3 p, float u, float aerial, float strength, float w)
    {
        if (strength <= 0f || u <= 0f) return p;
        Vector3 q = p * _data.swayScale;
        var off = new Vector3(
            Noise(q + new Vector3(w, 0.37f * w, 0f)) * 2f - 1f,
            Noise(q + new Vector3(17.1f, w, 3.3f + 0.61f * w)) * 2f - 1f,
            Noise(q + new Vector3(5.7f + 0.53f * w, 41.3f, w)) * 2f - 1f);
        Vector3 n = p.normalized;
        Vector3 tang = off - n * Vector3.Dot(off, n);
        float f = Noise(q + new Vector3(29.3f, 7.7f + w, 13.1f));
        Vector3 radial = n * ((f - 0.5f) * aerial * 1.5f);
        float weight = Mathf.Pow(u, 1.5f) * (0.3f + 0.7f * aerial) * strength;
        return p + (tang + radial) * weight;
    }

    void AddTube(float rad, float tipS)
    {
        int rings = _ringC.Count;
        int baseIndex = _verts.Count;
        Vector3 prevSide = Vector3.zero;
        for (int k = 0; k < rings; k++)
        {
            Vector3 c = _ringC[k];
            Vector3 tan = k == 0 ? _ringC[1] - _ringC[0]
                        : k == rings - 1 ? _ringC[k] - _ringC[k - 1]
                        : _ringC[k + 1] - _ringC[k - 1];
            if (tan.sqrMagnitude < 1e-12f) tan = Vector3.forward;
            tan.Normalize();
            Vector3 radialDir = c.normalized;
            Vector3 side = Vector3.Cross(tan, radialDir);
            if (side.sqrMagnitude < 1e-8f) side = prevSide.sqrMagnitude > 0 ? prevSide : Vector3.Cross(tan, Vector3.up);
            side.Normalize();
            prevSide = side;
            Vector3 up = Vector3.Cross(side, tan);

            float d = tipS - _ringS[k];   // distance to the tip
            float radius = rad * Mathf.Sqrt(Mathf.Clamp01(d / (rad * 2.5f)));
            float tip = 1f - Mathf.Clamp01(d / (rad * 7f));
            for (int j = 0; j < sides; j++)
            {
                float a = j * Mathf.PI * 2f / sides;
                Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                _verts.Add(c + dir * radius);
                _normals.Add(dir);
                _uvs.Add(new Vector2(tip, _ringS[k]));
            }
        }
        for (int k = 0; k < rings - 1; k++)
        {
            int r0 = baseIndex + k * sides, r1 = r0 + sides;
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                _tris.Add(r0 + j); _tris.Add(r1 + j); _tris.Add(r0 + j1);
                _tris.Add(r0 + j1); _tris.Add(r1 + j); _tris.Add(r1 + j1);
            }
        }
    }

    void BuildNubTemplate()
    {
        const int lat = 6, lon = 10;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= lat; i++)
        {
            float th = Mathf.PI * i / lat;
            for (int j = 0; j < lon; j++)
            {
                float ph = Mathf.PI * 2f * j / lon;
                v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
            }
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = i * lon + j, b = i * lon + (j + 1) % lon, c = a + lon, d = b + lon;
                t.Add(a); t.Add(b); t.Add(c);
                t.Add(b); t.Add(d); t.Add(c);
            }
        _nubVerts = v.ToArray();
        _nubTris = t.ToArray();
    }

    void AddNub(Vector3 center, float radius)
    {
        int b = _verts.Count;
        foreach (var n in _nubVerts)
        {
            _verts.Add(center + n * radius);
            _normals.Add(n);
            _uvs.Add(Vector2.zero);
        }
        foreach (var i in _nubTris) _tris.Add(b + i);
    }

    void UpdateOrchids(float frame, float seconds, float sway, float swayW)
    {
        int orchidIndex = -1;
        foreach (var root in _roots)
        {
            if (root.orchidPivot == null) continue;
            orchidIndex++;
            var r = root.src;
            float bloomStart = bloomTogether ? _firstBloom : r.bloom;
            float t = Mathf.Clamp01((frame - bloomStart) / Mathf.Max(1f, _data.bloomDuration));
            // posing by hand: leave a fully opened orchid wherever it has been dragged
            if (poseOrchidsByHand && t >= 1f && root.orchidPivot.gameObject.activeSelf) continue;
            if (t <= 0f || !showOrchids)
            {
                if (root.orchidPivot.gameObject.activeSelf) root.orchidPivot.gameObject.SetActive(false);
                continue;
            }
            if (!root.orchidPivot.gameObject.activeSelf) root.orchidPivot.gameObject.SetActive(true);

            // ease-out-back: overshoots a little so the flower "pops" open
            float tm1 = t - 1f;
            float ease = 1f + 2.4f * tm1 * tm1 * tm1 + 1.4f * tm1 * tm1;

            Vector3 at;
            Vector3 rootDir = Vector3.zero;
            if (r.tipOrchid)
            {
                // dome layout: at the (swaying) end of the climbing root, near the top of the dome
                int last = root.basePts.Length - 1;
                at = SwayPoint(root.basePts[last], 1f, r.aerial, sway, swayW);
                if (last > 0)
                {
                    float uPrev = root.u[last - 1];
                    rootDir = at - SwayPoint(root.basePts[last - 1], uPrev, r.aerial, sway, swayW);
                }
            }
            else at = root.basePts[0];
            Vector3 dir = at.normalized;
            Vector3 axis = orchidsFaceInward ? -dir : dir;
            float radius = orchidsFaceInward && !wrapInsideDome ? _data.innerRadius : at.magnitude;
            float phase = root.spin * Mathf.Deg2Rad;
            float settle = flowerSway ? SettleWeight(frame) : 0f;   // 0 = no sway
            float swx = Mathf.Sin(seconds * 1.1f + phase) * 0.07f * Mathf.Rad2Deg * settle;
            float swz = Mathf.Sin(seconds * 0.8f + phase * 1.7f) * 0.07f * Mathf.Rad2Deg * settle;

            root.orchidPivot.localPosition = dir * radius;
            if (wrapInsideDome && presentFlowerToCenter && r.tipOrchid)
            {
                // flower face (model +Z) looks at the center of the sphere (the audience)
                Vector3 toCenter = -dir;
                Vector3 stem;
                if (tailsPointToCenter)
                {
                    // model +Y (top petal) points down the dome, so the long lip tails (model -Y)
                    // point up at the zenith = the middle of the dome master image
                    stem = -TangentToward(dir, _domeUpLocal);
                }
                else
                {
                    // the stem (model +Y) carries on in the direction the root was growing
                    stem = rootDir - toCenter * Vector3.Dot(rootDir, toCenter);
                }
                if (stem.sqrMagnitude < 1e-8f) stem = Vector3.Cross(toCenter, Vector3.right);
                var face = Quaternion.LookRotation(toCenter, stem.normalized);
                root.orchidPivot.localRotation = face
                    * Quaternion.Euler(0f, 0f, flowerFaceRoll + swz)
                    * Quaternion.Euler(-flowerFaceTilt + swx, flowerFaceYaw, 0f);
            }
            else
                root.orchidPivot.localRotation = Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.Euler(swx, root.spin, swz);
            root.orchidPivot.localScale = Vector3.one * Mathf.Max(0f, ease * _data.orchidScale * orchidScaleMultiplier);

            // a saved hand-placed pose replaces the calculated one (the flower opens right there)
            if (_pose != null && orchidIndex < _pose.Length)
            {
                var pz = _pose[orchidIndex];
                if (pz.localPosition != null && pz.localPosition.Length == 3)
                    root.orchidPivot.localPosition = new Vector3(pz.localPosition[0], pz.localPosition[1], pz.localPosition[2]);
                if (pz.localRotation != null && pz.localRotation.Length == 4)
                    root.orchidPivot.localRotation =
                        new Quaternion(pz.localRotation[0], pz.localRotation[1], pz.localRotation[2], pz.localRotation[3])
                        * Quaternion.Euler(swx, 0f, swz);
                if (pz.localScale != null && pz.localScale.Length == 3)
                    root.orchidPivot.localScale = Mathf.Max(0f, ease) *
                        new Vector3(pz.localScale[0], pz.localScale[1], pz.localScale[2]);
            }

            // stem at the dome edge, flower toward the centre: the stem base is the model's origin and the
            // flower grows along model +Y, so +Y points up the dome. The front of the flower (petals and lip)
            // is the model's +Z side, so +Z looks in at the audience in the middle of the dome.
            if (copyTemplateOrchid && _pose != null && templateOrchid >= 0 && templateOrchid < _pose.Length
                && TemplateOffset(out var offset))
            {
                if (compassLayout)
                {
                    root.orchidPivot.localPosition = CompassPosition(orchidIndex);
                    root.orchidPivot.localScale *= ringScale;
                }
                root.orchidPivot.localRotation = DomeFrame(root.orchidPivot.localPosition) * offset
                    * Quaternion.Euler(swx, 0f, swz);
            }
            else if (stemFromEdge)
            {
                Vector3 outDir = root.orchidPivot.localPosition.normalized;
                Vector3 upTheDome = TangentToward(outDir, _domeUpLocal);
                if (upTheDome.sqrMagnitude < 1e-8f) upTheDome = Vector3.Cross(outDir, Vector3.right);
                root.orchidPivot.localRotation = Quaternion.LookRotation(-outDir, upTheDome.normalized)
                    * Quaternion.Euler(0f, 0f, flowerFaceRoll + swz)
                    * Quaternion.Euler(-flowerFaceTilt + swx, flowerFaceYaw, 0f);
            }
        }
    }

    // ---------------------------------------------------------------- helpers
    static float Hash01(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Smooth 3D value noise, 0..1.</summary>
    static float Noise(Vector3 p)
    {
        int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
        float xf = p.x - xi, yf = p.y - yi, zf = p.z - zi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
        float x00 = Mathf.Lerp(Hash01(xi, yi, zi), Hash01(xi + 1, yi, zi), u);
        float x10 = Mathf.Lerp(Hash01(xi, yi + 1, zi), Hash01(xi + 1, yi + 1, zi), u);
        float x01 = Mathf.Lerp(Hash01(xi, yi, zi + 1), Hash01(xi + 1, yi, zi + 1), u);
        float x11 = Mathf.Lerp(Hash01(xi, yi + 1, zi + 1), Hash01(xi + 1, yi + 1, zi + 1), u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    void OnDestroy()
    {
        // switch the renderers off before their mesh and material go, so Unity's GPU Resident Drawer never
        // tries to draw a renderer whose material has already been destroyed (happens when the scene changes)
        foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = false;
        if (_mesh != null) Destroy(_mesh);
        if (_orchidMaterial != null) Destroy(_orchidMaterial);
    }
}
