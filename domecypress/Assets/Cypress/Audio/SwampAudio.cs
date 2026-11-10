using UnityEngine;

/// <summary>
/// Sound for Mothlight.
///  - Ambience: the night swamp loop plays all the time. It carries on from the title scene into the
///    cypress dome without restarting (this object survives the scene change).
///  - Moth wings: whenever moths fly out, call SwampAudio.PlayFlap() and one of the flapping sounds
///    is picked at random (never the same one twice in a row).
/// Added to a scene with Tools > Cypress > Audio > Add Swamp Audio (open scenes).
/// </summary>
public class SwampAudio : MonoBehaviour
{
    public static SwampAudio Instance { get; private set; }

    [Header("Ambience")]
    public AudioClip ambience;
    [Range(0f, 1f)] public float ambienceVolume = 0.8f;
    [Tooltip("Seconds to fade the ambience in at the start.")]
    public float ambienceFadeIn = 3f;

    [Header("Moth wings")]
    public AudioClip[] flapSounds;
    [Range(0f, 1f)] public float flapVolume = 1f;

    AudioSource _ambienceSource, _flapSource;
    int _lastFlap = -1;
    float _fade;

    void Awake()
    {
        // one SwampAudio for the whole show: if one is already playing from the previous scene, keep that one
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _ambienceSource = gameObject.AddComponent<AudioSource>();
        _ambienceSource.clip = ambience;
        _ambienceSource.loop = true;
        _ambienceSource.playOnAwake = false;
        _ambienceSource.spatialBlend = 0f;   // all around, not from one spot
        _ambienceSource.volume = 0f;
        if (ambience != null) _ambienceSource.Play();

        _flapSource = gameObject.AddComponent<AudioSource>();
        _flapSource.playOnAwake = false;
        _flapSource.spatialBlend = 0f;
    }

    void Update()
    {
        // gentle fade-in, then follow the Ambience Volume slider
        _fade = Mathf.MoveTowards(_fade, 1f, Time.deltaTime / Mathf.Max(0.01f, ambienceFadeIn));
        if (_ambienceSource != null) _ambienceSource.volume = ambienceVolume * _fade;
    }

    /// <summary>Plays one of the flapping sounds (safe to call even if there's no SwampAudio in the scene).</summary>
    public static void PlayFlap()
    {
        if (Instance == null || Instance.flapSounds == null || Instance.flapSounds.Length == 0) return;
        Instance.PlayRandomFlap();
    }

    void PlayRandomFlap()
    {
        int i = Random.Range(0, flapSounds.Length);
        if (flapSounds.Length > 1 && i == _lastFlap) i = (i + 1) % flapSounds.Length;   // no repeats in a row
        _lastFlap = i;
        if (flapSounds[i] != null) _flapSource.PlayOneShot(flapSounds[i], flapVolume);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
