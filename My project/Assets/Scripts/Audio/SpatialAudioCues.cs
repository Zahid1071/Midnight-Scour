using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Direction and obstruction cues for every playing 3D source, on top of Unity's stereo panning (which can't
/// tell front from back): sounds behind the listener are dulled and a little quieter, and sounds behind walls
/// or shut doors are muffled. 2D sources (music, tension, UI, the player's breathing) and the player's own
/// sounds are left alone.
/// While this is active, SoundManager leaves its own one-shot occlusion to it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioListener))]
public class SpatialAudioCues : MonoBehaviour
{
    public static bool Active => s_instance != null && s_instance.isActiveAndEnabled;
    static SpatialAudioCues s_instance;
    static readonly HashSet<AudioSource> s_ignored = new HashSet<AudioSource>();

    /// <summary>Keeps a source out of the cues, for sounds that must hit at full strength wherever the player faces.</summary>
    public static void Ignore(AudioSource source)
    {
        s_ignored.RemoveWhere(s => s == null);
        if (source != null)
            s_ignored.Add(source);
    }

    [Header("Behind")]
    [Tooltip("Flat angle from the view direction where the behind cue starts.")]
    [SerializeField] float behindStartAngle = 95f;
    [Tooltip("Flat angle from the view direction where the behind cue is full.")]
    [SerializeField] float behindFullAngle = 165f;
    [SerializeField] float behindCutoff = 6000f;
    [SerializeField, Range(0f, 1f)] float behindGain = 0.9f;
    [Tooltip("Flat distances below this give no direction (e.g. the player's own footsteps under the camera).")]
    [SerializeField] float minFlatDistance = 0.5f;

    [Header("Obstructed (walls, shut doors)")]
    [SerializeField] LayerMask obstructionLayers = ~0;
    [SerializeField] float obstructedCutoff = 900f;
    [SerializeField, Range(0f, 1f)] float obstructedGain = 0.45f;
    [Tooltip("Sources closer than this are never obstructed.")]
    [SerializeField] float minObstructionDistance = 0.75f;
    [Tooltip("Hits this close to the source don't count: the object making the sound (a bolt's grate, a picked-up " +
             "item) or the wall/floor it sits in.")]
    [SerializeField] float sourceClearance = 1f;
    [Tooltip("Seconds between line-of-sight checks per source.")]
    [SerializeField] float obstructionInterval = 0.1f;

    [Header("Smoothing")]
    [Tooltip("Per second; how fast a source follows a change in its cue.")]
    [SerializeField] float smoothing = 8f;

    const float OpenCutoff = 22000f;

    class Tracked
    {
        public AudioSource source;
        public AudioLowPassFilter lowPass;
        public SpatialCueGain gain;
        public float cutoff;
        public float level;
        public bool obstructed;
        public float nextCheck;
        public bool primed;
    }

    readonly Dictionary<AudioSource, Tracked> _tracked = new Dictionary<AudioSource, Tracked>();
    readonly List<AudioSource> _dead = new List<AudioSource>();
    int _mask;

    void Awake()
    {
        _mask = obstructionLayers & ~LayerMask.GetMask("Player", "NPC", "UI", "Ignore Raycast", "Interactable ");
    }

    void OnEnable() => s_instance = this;

    void OnDisable()
    {
        if (s_instance == this)
            s_instance = null;
        foreach (Tracked t in _tracked.Values)
            Apply(t, OpenCutoff, 1f);
    }

    void LateUpdate()
    {
        Scan();

        Vector3 listener = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);

        foreach (Tracked t in _tracked.Values)
        {
            AudioSource src = t.source;
            if (src == null)
            {
                _dead.Add(src);
                continue;
            }
            if (!src.isActiveAndEnabled || !src.isPlaying || src.spatialBlend < 0.5f)
            {
                t.primed = false;
                continue;
            }

            Vector3 position = src.transform.position;
            Vector3 flat = position - listener;
            flat.y = 0f;
            float behind = flat.magnitude < minFlatDistance || forward.sqrMagnitude < 1e-4f
                ? 0f
                : Mathf.InverseLerp(behindStartAngle, behindFullAngle, Vector3.Angle(forward, flat));
            behind = Mathf.SmoothStep(0f, 1f, behind);

            if (!t.primed || Time.unscaledTime >= t.nextCheck)
            {
                t.nextCheck = Time.unscaledTime + obstructionInterval;
                t.obstructed = IsObstructed(listener, position, src.transform);
            }

            float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(behindCutoff), behind));
            float level = Mathf.Lerp(1f, behindGain, behind);
            if (t.obstructed)
            {
                cutoff = Mathf.Min(cutoff, obstructedCutoff);
                level *= obstructedGain;
            }

            // A source that just started takes its cue at once, so its first hit isn't heard unfiltered.
            if (!t.primed)
            {
                t.primed = true;
                t.cutoff = cutoff;
                t.level = level;
            }
            else
            {
                t.cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(t.cutoff), Mathf.Log(cutoff), k));
                t.level = Mathf.Lerp(t.level, level, k);
            }
            Apply(t, t.cutoff, t.level);
        }

        foreach (AudioSource dead in _dead)
            _tracked.Remove(dead);
        _dead.Clear();
    }

    void Scan()
    {
        Transform soundManager = SoundManager.Instance != null ? SoundManager.Instance.transform : null;
        foreach (AudioSource src in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (_tracked.ContainsKey(src) || src.spatialBlend < 0.5f || !src.isPlaying
                || src.transform.root.CompareTag("Player") || s_ignored.Contains(src))
                continue;

            // A low-pass someone else drives is theirs; SoundManager's pool hands its own over while this is active.
            AudioLowPassFilter lowPass = src.GetComponent<AudioLowPassFilter>();
            bool ours = src.GetComponent<SpatialCueGain>() != null
                        || (soundManager != null && src.transform.IsChildOf(soundManager));
            if (lowPass != null && !ours)
                continue;

            if (lowPass == null)
                lowPass = src.gameObject.AddComponent<AudioLowPassFilter>();
            SpatialCueGain gain = src.GetComponent<SpatialCueGain>();
            if (gain == null)
                gain = src.gameObject.AddComponent<SpatialCueGain>();
            _tracked.Add(src, new Tracked { source = src, lowPass = lowPass, gain = gain, cutoff = OpenCutoff, level = 1f });
        }
    }

    bool IsObstructed(Vector3 listener, Vector3 position, Transform source)
    {
        Vector3 delta = position - listener;
        float distance = delta.magnitude;
        float reach = distance - sourceClearance;
        if (distance < minObstructionDistance || reach <= 0f)
            return false;

        foreach (RaycastHit hit in Physics.RaycastAll(listener, delta / distance, reach, _mask, QueryTriggerInteraction.Ignore))
        {
            Transform h = hit.collider.transform;
            if (h.IsChildOf(source) || source.IsChildOf(h) || hit.collider.CompareTag("Player"))
                continue;
            return true;
        }
        return false;
    }

    static void Apply(Tracked t, float cutoff, float level)
    {
        if (t.lowPass != null)
        {
            t.lowPass.enabled = cutoff < OpenCutoff - 100f;
            t.lowPass.cutoffFrequency = cutoff;
        }
        if (t.gain != null)
            t.gain.Gain = level;
    }
}
