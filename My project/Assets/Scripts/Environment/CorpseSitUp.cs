using UnityEngine;
using UnityEngine.Formats.Alembic.Importer;

/// <summary>
/// The body under the sheet sitting up: his clip and the baked sheet (Alembic) run off one clock so the cloth stays on
/// him. Idle holds the first motion frame (after Skip Seconds) so Play doesn't snap from a different pose.
/// </summary>
[DisallowMultipleComponent]
public class CorpseSitUp : MonoBehaviour
{
    [SerializeField] Animator animator;
    [Tooltip("Base layer state with the sit-up clip.")]
    [SerializeField] string sitUpState = "Corpse SitUp";
    [SerializeField] AnimationClip sitUpClip;
    [Tooltip("The baked sheet over him; its time range matches the clip.")]
    [SerializeField] AlembicStreamPlayer sheet;
    [Tooltip("Seconds of lying-still at the start of the clip to skip when Play is called.")]
    [SerializeField] float skipSeconds = 2.4f;
    [SerializeField] float speed = 1.5f;

    int _stateHash;
    float _time;
    bool _playing;
    bool _started;

    public bool IsPlaying => _playing;
    public bool Started => _started;
    /// <summary>Clip seconds of motion after <see cref="Play"/> (before speed scaling).</summary>
    public float PlayDuration => Mathf.Max(0.05f, Length - StartTime);
    /// <summary>Real-time seconds to play the full sit-up at the current speed.</summary>
    public float PlayDurationRealtime => PlayDuration / Mathf.Max(0.01f, speed);
    /// <summary>0-1 through the sit-up motion (after skip).</summary>
    public float NormalizedProgress =>
        !_started ? 0f : Mathf.Clamp01((_time - StartTime) / PlayDuration);

    float Length => sitUpClip != null ? sitUpClip.length : sheet != null ? sheet.Duration : 0f;
    float StartTime => Mathf.Clamp(skipSeconds, 0f, Length);

    void Awake()
    {
        _stateHash = Animator.StringToHash(sitUpState);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.speed = 0f;
        }
        // Idle at the same pose Play starts on — no snap from frame 0 into mid-clip.
        _time = StartTime;
        Pose(_time);
    }

    void OnEnable()
    {
        if (!_started)
        {
            _time = StartTime;
            Pose(_time);
        }
    }

    public void Play()
    {
        if (_started)
            return;
        _started = true;
        _playing = true;
        // Continue from the held start pose (already at StartTime).
        _time = StartTime;
        Pose(_time);
    }

    /// <summary>Real-time seconds until the sit-up reaches this normalized progress (0-1).</summary>
    public float RealtimeUntil(float normalized)
    {
        float target = StartTime + PlayDuration * Mathf.Clamp01(normalized);
        float remainingClip = Mathf.Max(0f, target - _time);
        return remainingClip / Mathf.Max(0.01f, speed);
    }

    void Update()
    {
        if (!_playing)
        {
            // Keep the held start pose locked so nothing else drifts the mesh.
            if (!_started)
                Pose(StartTime);
            return;
        }
        _time = Mathf.Min(_time + Time.deltaTime * speed, Length);
        Pose(_time);
        if (_time >= Length)
            _playing = false;
    }

    void Pose(float time)
    {
        float length = Length;
        if (animator != null && length > 0f)
        {
            animator.Play(_stateHash, 0, time / length);
            animator.Update(0f);
        }
        if (sheet != null)
            sheet.UpdateImmediately(time);
    }
}
