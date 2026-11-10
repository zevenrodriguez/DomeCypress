using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ending: a couple of seconds after the moths have flown into place, the picture fades to black
/// and a title fades in. A few seconds later the next scene (CypressDome) loads. The fade and title are added to the Dome Tools "Canvas (DomeMaster)", so they
/// end up in the dome master image (the middle of the image = the top of the dome).
/// Put this on the same GameObject as OrchidRootSphere and OrchidMoths.
/// </summary>
[RequireComponent(typeof(OrchidMoths))]
public class OrchidTitleEnding : MonoBehaviour
{
    [Header("Title")]
    public string title = "Mothlight";
    public Color titleColor = Color.white;
    [Tooltip("Title font (Playfair Display Black Italic, in OrchidRoots/Fonts). Empty = Unity's built-in font.")]
    public Font font;

    [Header("Subtitle")]
    public string subtitle = "An immersive journey into the Everglades.";
    [Tooltip("Warm cream so it reads on the black background.")]
    public Color subtitleColor = new Color(0.93f, 0.86f, 0.74f);
    [Tooltip("Subtitle font (Cinzel Bold, in OrchidRoots/Fonts).")]
    public Font subtitleFont;

    [Header("Timing (seconds)")]
    [Tooltip("Wait after the moths have flown into place.")]
    public float waitAfterMoths = 2f;
    [Tooltip("How long the fade to black takes.")]
    public float fadeOutSeconds = 2f;
    [Tooltip("How long the title takes to fade in, after the screen is black.")]
    public float titleFadeInSeconds = 1.5f;

    [Header("Next scene")]
    [Tooltip("Scene to load after the title (leave empty to stay on the title).")]
    public string nextScene = "CypressDome";
    [Tooltip("How long the title stays up, once fully visible, before the next scene loads.")]
    public float secondsOnTitle = 5f;
    [Tooltip("The next scene fades in from black over this many seconds (0 = cut straight in).")]
    public float nextSceneFadeInSeconds = 2f;

    [Header("Testing")]
    [Tooltip("Tick to keep the orchids on screen: no fade, no title, no next scene. Use it while adjusting the " +
             "flowers in Play mode. (Pose Orchids By Hand on OrchidRootSphere holds the ending too.)")]
    public bool holdEnding = false;

    [Header("Where to draw")]
    [Tooltip("Name of the Dome Tools canvas that builds the dome master image.")]
    public string canvasName = "Canvas (DomeMaster)";

    OrchidMoths _moths;
    OrchidRootSphere _sphere;
    Image _black;
    Text _title, _subtitle;
    bool _loading;

    void Start()
    {
        _moths = GetComponent<OrchidMoths>();
        _sphere = GetComponent<OrchidRootSphere>();
        BuildUI();
        SetLook(0f, 0f);
    }

    void Update()
    {
        // posing the orchids by hand: hold the ending (no fade, no title, no next scene) so there's time
        if (holdEnding || (_sphere != null && _sphere.poseOrchidsByHand)) { SetLook(0f, 0f); return; }

        float sinceMoths = _moths.SecondsSinceArrived;
        if (sinceMoths < 0f) { SetLook(0f, 0f); return; }   // not yet (or the animation restarted)

        float t = sinceMoths - waitAfterMoths;
        float fade = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeOutSeconds));
        float titleAlpha = Mathf.Clamp01((t - fadeOutSeconds) / Mathf.Max(0.01f, titleFadeInSeconds));
        SetLook(Smooth(fade), Smooth(titleAlpha));

        // title fully visible for Seconds On Title -> next scene
        float titleShownFor = t - fadeOutSeconds - titleFadeInSeconds;
        if (titleShownFor >= secondsOnTitle && !_loading) LoadNextScene();
    }

    void LoadNextScene()
    {
        _loading = true;
        if (string.IsNullOrEmpty(nextScene)) return;
        if (nextSceneFadeInSeconds > 0f) SceneFadeIn.Prepare(nextSceneFadeInSeconds, canvasName);

        // normal way: the scene is listed in File > Build Profiles (Scene List)
        if (Application.CanStreamedLevelBeLoaded(nextScene))
        {
            SceneManager.LoadScene(nextScene);
            return;
        }
#if UNITY_EDITOR
        // in the Editor, still load it by its path so it works before it's added to the scene list
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets(nextScene + " t:Scene"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != nextScene) continue;
            Debug.LogWarning($"OrchidTitleEnding: '{nextScene}' isn't in the Build Profiles scene list. " +
                             "It loads here in the Editor, but add it to the list so it works in a build.", this);
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        Debug.LogError($"OrchidTitleEnding: scene '{nextScene}' not found.", this);
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);

    void SetLook(float blackAlpha, float titleAlpha)
    {
        if (_black != null) _black.color = new Color(0f, 0f, 0f, blackAlpha);
        if (_title != null)
        {
            _title.text = title;   // so edits in the Inspector show up live
            _title.color = new Color(titleColor.r, titleColor.g, titleColor.b, titleColor.a * titleAlpha);
        }
        if (_subtitle != null)
        {
            _subtitle.text = subtitle;
            _subtitle.color = new Color(subtitleColor.r, subtitleColor.g, subtitleColor.b, subtitleColor.a * titleAlpha);
        }
    }

    void BuildUI()
    {
        // draw into the dome master canvas; fall back to a plain screen overlay if it isn't in the scene
        Canvas canvas = null;
        var canvasObject = GameObject.Find(canvasName);
        if (canvasObject != null) canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning($"OrchidTitleEnding: '{canvasName}' not found, drawing the ending on the screen only.", this);
            var go = new GameObject("Ending Canvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            go.AddComponent<CanvasScaler>();
        }

        var root = NewRect("Ending", canvas.transform, Vector2.zero, Vector2.one);
        root.SetAsLastSibling();   // on top of the dome picture and the logos

        // full-frame black for the fade
        _black = NewRect("Fade", root, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
        _black.raycastTarget = false;

        // title in the middle and the subtitle under it, each sized automatically to fit its box
        _title = NewText("Title", root, new Vector2(0.15f, 0.47f), new Vector2(0.85f, 0.62f), font, 400);
        _subtitle = NewText("Subtitle", root, new Vector2(0.2f, 0.405f), new Vector2(0.8f, 0.455f), subtitleFont, 120);
    }

    static Text NewText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Font font, int maxSize)
    {
        var text = NewRect(name, parent, anchorMin, anchorMax).gameObject.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;   // italic letters can lean past the box edge
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = maxSize;
        text.raycastTarget = false;
        return text;
    }

#if UNITY_EDITOR
    // fill in the Mothlight fonts and title text when the component is added or loaded in the editor
    void Reset() => FillDefaults();
    void OnValidate() => FillDefaults();

    void FillDefaults()
    {
        if (title == "TITLE") title = "Mothlight";   // replace the old placeholder
        if (font == null)
            font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/OrchidRoots/Fonts/PlayfairDisplay-BlackItalic.ttf");
        if (subtitleFont == null)
            subtitleFont = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/OrchidRoots/Fonts/Cinzel-Bold.ttf");
    }
#endif

    static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }
}
