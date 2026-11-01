using FS_Atmo;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Fades an ambient sound (e.g. wind from outside) in when a door opens, and out again when it closes.
/// Put it on the door (or anywhere, with the door assigned).
/// </summary>
[DisallowMultipleComponent]
public class DoorOpenAmbience : MonoBehaviour
{
    [Tooltip("Empty = the SimpleOpenClose on this object, its children or its parents.")]
    [SerializeField] SimpleOpenClose door;

    [Header("Sound")]
    [SerializeField] AudioClip clip;
    [SerializeField, Range(0f, 1f)] float volume = 0.6f;
    [SerializeField] bool loop = true;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("Timing")]
    [Tooltip("Seconds after the door starts opening before the sound starts.")]
    [SerializeField] float delay = 0.2f;
    [SerializeField] float fadeInTime = 1.5f;
    [SerializeField] float fadeOutTime = 1.5f;
    [Tooltip("Off: once started it keeps playing even if the door is shut again.")]
    [SerializeField] bool stopOnClose = true;
    [Tooltip("Only the first opening plays it.")]
    [SerializeField] bool onlyOnce;

    [Header("Placement")]
    [Tooltip("Where the sound comes from. Empty = the door.")]
    [SerializeField] Transform soundPoint;
    [Tooltip("0 = flat (heard the same everywhere), 1 = fully from the sound point.")]
    [SerializeField, Range(0f, 1f)] float spatialBlend = 0.8f;
    [SerializeField] float minDistance = 2f;
    [SerializeField] float maxDistance = 25f;

    AudioSource _source;
    bool _wasOpen;
    bool _played;
    float _openedAt = -1f;
    float _target;

    void Awake()
    {
        if (door == null)
            door = GetComponentInChildren<SimpleOpenClose>();
        if (door == null)
            door = GetComponentInParent<SimpleOpenClose>();
        if (door == null)
            Debug.LogWarning("DoorOpenAmbience: no SimpleOpenClose found.", this);

        var go = new GameObject("Door Ambience Audio");
        go.transform.SetParent(transform, false);
        go.transform.position = soundPoint != null ? soundPoint.position
            : door != null ? door.transform.position : transform.position;
        _source = go.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.clip = clip;
        _source.loop = loop;
        _source.volume = 0f;
        _source.outputAudioMixerGroup = mixerGroup;
        _source.spatialBlend = spatialBlend;
        _source.rolloffMode = AudioRolloffMode.Logarithmic;
        _source.minDistance = minDistance;
        _source.maxDistance = maxDistance;
        _source.dopplerLevel = 0f;
    }

    void Update()
    {
        if (door == null || clip == null)
            return;

        bool open = door.objectOpen;
        if (open && !_wasOpen && !(onlyOnce && _played))
            _openedAt = Time.time;
        else if (!open && _wasOpen && stopOnClose)
        {
            _openedAt = -1f;
            _target = 0f;
        }
        _wasOpen = open;

        if (_openedAt >= 0f && Time.time >= _openedAt + delay)
        {
            _openedAt = -1f;
            _played = true;
            _target = 1f;
            if (!_source.isPlaying)
                _source.Play();
        }

        float current = volume > 0f ? _source.volume / volume : 0f;
        float time = _target > current ? fadeInTime : fadeOutTime;
        current = time > 0f ? Mathf.MoveTowards(current, _target, Time.deltaTime / time) : _target;
        _source.volume = current * volume;

        if (current <= 0f && _target <= 0f && _source.isPlaying)
            _source.Stop();
        else if (!loop && !_source.isPlaying && _target > 0f)
            _target = 0f;
    }
}
