using UnityEngine;

/// <summary>
/// Keeps the dome stream running when Unity isn't the focused window.
/// The project had "Run In Background" off, so as soon as you clicked into the
/// Dome Viewer / NDI or Spout receiver, Unity paused and the stream froze
/// (it only updated again when you clicked back into Unity).
/// This runs automatically when Play starts - no need to add it to a GameObject.
/// </summary>
static class KeepRunningInBackground
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Enable()
    {
        Application.runInBackground = true;
    }
}
