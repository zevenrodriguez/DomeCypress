using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fades the next scene in from black. Created by OrchidTitleEnding right before it loads the next
/// scene; it survives the scene change, puts a black layer on the new scene's dome master canvas,
/// fades it out, then removes itself.
/// </summary>
public class SceneFadeIn : MonoBehaviour
{
    public float fadeInSeconds = 2f;
    public string canvasName = "Canvas (DomeMaster)";

    Image _black;
    float _startTime = -1f;

    /// <summary>Call right before loading the next scene.</summary>
    public static void Prepare(float seconds, string canvasName)
    {
        var go = new GameObject("Scene Fade In");
        DontDestroyOnLoad(go);
        var fader = go.AddComponent<SceneFadeIn>();
        fader.fadeInSeconds = seconds;
        fader.canvasName = canvasName;
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // black over the new scene's dome master image (or the plain screen if there is none)
        Canvas canvas = null;
        var canvasObject = GameObject.Find(canvasName);
        if (canvasObject != null) canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
        }

        var rt = new GameObject("Fade In", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();
        _black = rt.gameObject.AddComponent<Image>();
        _black.color = Color.black;
        _black.raycastTarget = false;
        _startTime = Time.time;
    }

    void Update()
    {
        if (_startTime < 0f || _black == null) return;
        float t = Mathf.Clamp01((Time.time - _startTime) / Mathf.Max(0.01f, fadeInSeconds));
        t = t * t * (3f - 2f * t);
        _black.color = new Color(0f, 0f, 0f, 1f - t);
        if (t >= 1f)
        {
            Destroy(_black.gameObject);
            Destroy(gameObject);
        }
    }
}
