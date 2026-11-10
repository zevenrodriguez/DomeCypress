using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ending: a couple of seconds after the moths have flown into place, the picture fades to black
/// and a title fades in. The fade and title are added to the Dome Tools "Canvas (DomeMaster)", so they
/// end up in the dome master image (the middle of the image = the top of the dome).
/// Put this on the same GameObject as OrchidRootSphere and OrchidMoths.
/// </summary>
[RequireComponent(typeof(OrchidMoths))]
public class OrchidTitleEnding : MonoBehaviour
{
    [Header("Title")]
    [Tooltip("Placeholder title text.")]
    public string title = "TITLE";
    public Color titleColor = Color.white;
    [Tooltip("Optional font. Empty = Unity's built-in font.")]
    public Font font;

    [Header("Timing (seconds)")]
    [Tooltip("Wait after the moths have flown into place.")]
    public float waitAfterMoths = 2f;
    [Tooltip("How long the fade to black takes.")]
    public float fadeOutSeconds = 2f;
    [Tooltip("How long the title takes to fade in, after the screen is black.")]
    public float titleFadeInSeconds = 1.5f;

    [Header("Where to draw")]
    [Tooltip("Name of the Dome Tools canvas that builds the dome master image.")]
    public string canvasName = "Canvas (DomeMaster)";

    OrchidMoths _moths;
    Image _black;
    Text _title;

    void Start()
    {
        _moths = GetComponent<OrchidMoths>();
        BuildUI();
        SetLook(0f, 0f);
    }

    void Update()
    {
        float sinceMoths = _moths.SecondsSinceArrived;
        if (sinceMoths < 0f) { SetLook(0f, 0f); return; }   // not yet (or the animation restarted)

        float t = sinceMoths - waitAfterMoths;
        float fade = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeOutSeconds));
        float titleAlpha = Mathf.Clamp01((t - fadeOutSeconds) / Mathf.Max(0.01f, titleFadeInSeconds));
        SetLook(Smooth(fade), Smooth(titleAlpha));
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

        // title in the middle, sized automatically to fit its box
        _title = NewRect("Title", root, new Vector2(0.15f, 0.4f), new Vector2(0.85f, 0.6f)).gameObject.AddComponent<Text>();
        _title.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _title.alignment = TextAnchor.MiddleCenter;
        _title.resizeTextForBestFit = true;
        _title.resizeTextMinSize = 10;
        _title.resizeTextMaxSize = 400;
        _title.raycastTarget = false;
    }

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
