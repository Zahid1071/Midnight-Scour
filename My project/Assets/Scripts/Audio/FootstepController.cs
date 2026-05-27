using UnityEngine;
using UnityEngine.Audio;

public class FootstepController : MonoBehaviour
{
    [Header("Footstep Audio")]
    [Tooltip("Optional. Sound plays at this transform; defaults to this GameObject.")]
    public Transform footAnchor;

    [Header("Walk Footstep Clips")]
    public AudioClip[] leftFootClips;
    public AudioClip[] rightFootClips;

    [Header("Run Footstep Clips (optional)")]
    [Tooltip("If empty, walk clips are used when running.")]
    public AudioClip[] runLeftFootClips;
    public AudioClip[] runRightFootClips;

    [Header("Chase Sweetener (optional)")]
    [Tooltip("Extra one-shots layered on run steps (cloth, metal, breath, etc.).")]
    public AudioClip[] chaseSweetenerClips;
    [Range(0f, 1f)]
    public float chaseSweetenerChance = 0.35f;
    [Range(0f, 1f)]
    public float chaseSweetenerVolume = 0.55f;

    [Header("Mixer / Reverb")]
    public AudioMixerGroup mixerGroup;
    [Tooltip("Reverb zone mix while walking.")]
    [Range(0f, 1f)]
    public float walkReverbZoneMix = 0f;
    [Tooltip("Reverb zone mix while running (scarier / more present in space).")]
    [Range(0f, 1f)]
    public float runReverbZoneMix = 0.85f;

    [Header("Volumes")]
    public float walkVolume = 1.0f;
    public float runVolume = 1.3f;
    public float sneakVolume = 0.4f;

    [Header("Pitch")]
    public Vector2 walkPitchRange = new Vector2(0.97f, 1.03f);
    public Vector2 runPitchRange = new Vector2(0.9f, 0.98f);

    bool _isLeftFoot = true;

    int _lastWalkLeftIndex = -1;
    int _lastWalkRightIndex = -1;
    int _lastRunLeftIndex = -1;
    int _lastRunRightIndex = -1;

    void Awake()
    {
        if (footAnchor == null)
            footAnchor = transform;
    }

    /// <param name="volume">Base volume multiplier.</param>
    /// <param name="useRunClips">Use run clip arrays + run reverb/pitch when true.</param>
    public void PlayFootstep(float volume = 1f, bool useRunClips = false)
    {
        if (SoundManager.Instance == null)
            return;

        AudioClip[] leftPool = useRunClips && runLeftFootClips != null && runLeftFootClips.Length > 0
            ? runLeftFootClips
            : leftFootClips;
        AudioClip[] rightPool = useRunClips && runRightFootClips != null && runRightFootClips.Length > 0
            ? runRightFootClips
            : rightFootClips;

        AudioClip clip = PickClip(leftPool, rightPool, useRunClips, out bool usedLeft);
        if (clip == null)
            return;

        Vector3 pos = footAnchor != null ? footAnchor.position : transform.position;
        float volumeScale = volume * Random.Range(0.96f, 1.04f);
        Vector2 pitchRange = useRunClips ? runPitchRange : walkPitchRange;
        float pitch = Random.Range(pitchRange.x, pitchRange.y);
        float reverb = useRunClips ? runReverbZoneMix : walkReverbZoneMix;

        SoundManager.Instance.PlaySFXAt(
            clip,
            pos,
            volumeScale,
            pitch,
            reverb,
            transform.root);

        if (useRunClips)
            TryPlayChaseSweetener(pos);

        _isLeftFoot = !usedLeft;
    }

    AudioClip PickClip(AudioClip[] leftPool, AudioClip[] rightPool, bool useRunClips, out bool usedLeft)
    {
        usedLeft = _isLeftFoot;

        if (_isLeftFoot && leftPool != null && leftPool.Length > 0)
        {
            if (useRunClips)
            {
                int idx = PickNonRepeatIndex(leftPool.Length, _lastRunLeftIndex);
                _lastRunLeftIndex = idx;
                return leftPool[idx];
            }

            int walkIdx = PickNonRepeatIndex(leftPool.Length, _lastWalkLeftIndex);
            _lastWalkLeftIndex = walkIdx;
            return leftPool[walkIdx];
        }

        if (!_isLeftFoot && rightPool != null && rightPool.Length > 0)
        {
            if (useRunClips)
            {
                int idx = PickNonRepeatIndex(rightPool.Length, _lastRunRightIndex);
                _lastRunRightIndex = idx;
                return rightPool[idx];
            }

            int walkIdx = PickNonRepeatIndex(rightPool.Length, _lastWalkRightIndex);
            _lastWalkRightIndex = walkIdx;
            return rightPool[walkIdx];
        }

        return null;
    }

    static int PickNonRepeatIndex(int count, int lastIndex)
    {
        if (count <= 1)
            return 0;

        int idx = Random.Range(0, count);
        if (idx == lastIndex)
            idx = (idx + 1) % count;
        return idx;
    }

    void TryPlayChaseSweetener(Vector3 position)
    {
        if (chaseSweetenerClips == null || chaseSweetenerClips.Length == 0)
            return;
        if (Random.value > chaseSweetenerChance)
            return;
        if (SoundManager.Instance == null)
            return;

        AudioClip sweet = chaseSweetenerClips[Random.Range(0, chaseSweetenerClips.Length)];
        if (sweet == null)
            return;

        SoundManager.Instance.PlaySFXAt(sweet, position, chaseSweetenerVolume, 1f, runReverbZoneMix, transform.root);
    }
}
