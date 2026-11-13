using UnityEngine;
using UnityEngine.Formats.Alembic.Importer;

/// <summary>
/// The body under the sheet sitting up: his clip and the baked sheet (Alembic) run off one clock so the cloth stays on
/// him. Both hold their first frame (lying still) until Play. Skip Seconds jumps past the authored lying hold so motion
/// starts as soon as Play is called.
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
    [SerializeField] float speed = 1f;

    int _stateHash;
    float _time;
    bool _playing;
    bool _started;

    public bool IsPlaying => _playing;
    public bool Started => _started;

    float Length => sitUpClip != null ? sitUpClip.length : sheet != null ? sheet.Duration : 0f;

    void Awake()
    {
        _stateHash = Animator.StringToHash(sitUpState);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.speed = 0f;
        }
        Pose(0f);
    }

    public void Play()
    {
        if (_started)
            return;
        _started = true;
        _playing = true;
        _time = Mathf.Clamp(skipSeconds, 0f, Length);
        Pose(_time);
    }

    void Update()
    {
        if (!_playing)
            return;
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
