using UnityEngine;

/// <summary>
/// Occasional distant dog bark from a fixed world point (dog pen). Long irregular gaps,
/// light pitch drift, and outdoor-only so it never chatters or stacks indoors.
/// </summary>
[DisallowMultipleComponent]
public class DistantDogBarks : MonoBehaviour
{
    [SerializeField] AudioClip[] clips;
    [Tooltip("World origin of the bark. Defaults to this transform.")]
    [SerializeField] Transform emitFrom;
    [SerializeField, Min(5f)] float minGapSeconds = 55f;
    [SerializeField, Min(5f)] float maxGapSeconds = 120f;
    [Tooltip("Chance to skip a scheduled bark and wait another full gap (keeps it unpredictable).")]
    [SerializeField, Range(0f, 0.8f)] float skipChance = 0.25f;
    [SerializeField, Range(0f, 1f)] float volume = 0.5f;
    [SerializeField] Vector2 pitchRange = new Vector2(0.92f, 1.06f);
    [SerializeField] float minDistance = 18f;
    [SerializeField] float maxDistance = 85f;
    [Tooltip("Need at least this much outdoor amount (SoundManager) before a bark can fire.")]
    [SerializeField, Range(0f, 1f)] float minOutdoorAmount = 0.15f;
    [Tooltip("Eye / player Y at or below this is treated as basement — no barks.")]
    [SerializeField] float basementMaxY = -0.5f;
    [Tooltip("First bark can arrive sooner so the cue is noticeable in a playtest session.")]
    [SerializeField] float firstGapMaxSeconds = 35f;

    float _nextBarkTime;
    float _lastPitch = 1f;
    int _lastClipIndex = -1;
    Transform _player;
    bool _firstScheduled;

    void Awake()
    {
        if (clips != null)
        {
            for (int i = 0; i < clips.Length; i++)
                SoundManager.EnsureClipReady(clips[i]);
        }
    }

    void OnEnable()
    {
        ScheduleNext(initial: true);
    }

    void Update()
    {
        if (clips == null || clips.Length == 0)
            return;
        if (Time.time < _nextBarkTime)
            return;
        if (!CanPlayNow())
        {
            _nextBarkTime = Time.time + Random.Range(6f, 14f);
            return;
        }

        if (Random.value < skipChance)
        {
            ScheduleNext(initial: false);
            return;
        }

        PlayBark();
        ScheduleNext(initial: false);
    }

    bool CanPlayNow()
    {
        if (PlayerControlGate.IsInputBlocked)
            return false;
        if (SoundManager.Instance != null && SoundManager.Instance.OutdoorAmount < minOutdoorAmount)
            return false;

        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null)
                _player = p.transform;
        }

        if (_player != null && _player.position.y <= basementMaxY)
            return false;

        return true;
    }

    void PlayBark()
    {
        AudioClip clip = PickClip();
        if (clip == null)
            return;

        SoundManager.EnsureClipReady(clip);

        float pitch = Random.Range(pitchRange.x, pitchRange.y);
        if (Mathf.Abs(pitch - _lastPitch) < 0.03f)
            pitch = Mathf.Clamp(_lastPitch + (pitch >= _lastPitch ? 0.04f : -0.04f), pitchRange.x, pitchRange.y);
        _lastPitch = pitch;

        Vector3 pos = emitFrom != null ? emitFrom.position : transform.position;
        float outdoor = SoundManager.Instance != null ? SoundManager.Instance.OutdoorAmount : 1f;
        float vol = volume * Mathf.Lerp(0.4f, 1f, outdoor);

        SpatialAudio.PlayAt(clip, pos, vol, minDistance, maxDistance, null, transform, pitch, atmospheric: true);
    }

    AudioClip PickClip()
    {
        if (clips.Length == 1)
            return clips[0];

        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == _lastClipIndex)
            index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
        _lastClipIndex = index;
        return clips[index];
    }

    void ScheduleNext(bool initial)
    {
        float lo = Mathf.Min(minGapSeconds, maxGapSeconds);
        float hi = Mathf.Max(minGapSeconds, maxGapSeconds);
        if (initial && !_firstScheduled)
        {
            _firstScheduled = true;
            lo = Mathf.Min(12f, firstGapMaxSeconds);
            hi = Mathf.Max(lo, firstGapMaxSeconds);
        }
        _nextBarkTime = Time.time + Random.Range(lo, hi);
    }
}
