using StarterAssets;
using UnityEngine;

/// <summary>
/// Run stamina. Running fills the meter (full after <see cref="secondsToFull"/>); once you stop running it waits
/// <see cref="restDelay"/>, then drains (full to empty in <see cref="secondsToEmpty"/>). Filling it leaves the player
/// out of breath: the out-of-breath SFX plays, they can only walk at <see cref="exhaustedWalkSpeed"/>, and sprint is
/// locked while the meter drains over <see cref="exhaustedRecoverySeconds"/>.
/// Only switched on once the butcher window shot (the last cinematic) has handed control back.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FirstPersonController))]
public class SprintExhaustion : MonoBehaviour
{
    [SerializeField] float secondsToFull = 6f;
    [Tooltip("Seconds without running before the meter starts going down.")]
    [SerializeField] float restDelay = 3f;
    [Tooltip("Seconds the meter takes to drain from full to empty once it starts going down.")]
    [SerializeField] float secondsToEmpty = 2f;
    [SerializeField] bool onlyAfterWindowShot = true;

    [Header("Out of breath")]
    [Tooltip("Seconds from running out of breath until the meter is empty and sprint is back.")]
    [SerializeField] float exhaustedRecoverySeconds = 5f;
    [Tooltip("Top speed (walking or sneaking) while out of breath.")]
    [SerializeField] float exhaustedWalkSpeed = 1f;

    [Header("Audio")]
    [SerializeField] AudioClip outOfBreathClip;
    [SerializeField, Range(0f, 1f)] float outOfBreathVolume = 1f;

    FirstPersonController _controller;
    CharacterController _body;
    float _fill;
    float _restSeconds;
    bool _outOfBreath;

    /// <summary>0 = rested, 1 = out of breath.</summary>
    public float Fill01 => _fill;
    public bool IsOutOfBreath => _outOfBreath;

    void Awake()
    {
        _controller = GetComponent<FirstPersonController>();
        _body = GetComponent<CharacterController>();
    }

    void OnDisable() => ResetMeter();

    void Update()
    {
        if (onlyAfterWindowShot && !ButcherWindowShot.HasFinished)
        {
            if (_fill > 0f || _outOfBreath)
                ResetMeter();
            return;
        }

        if (PlayerControlGate.IsInputBlocked)
            return;

        if (!_outOfBreath && IsActuallyRunning())
        {
            _restSeconds = 0f;
            _fill = Mathf.Min(1f, _fill + Time.deltaTime / Mathf.Max(0.01f, secondsToFull));
            if (_fill >= 1f)
                RunOutOfBreath();
            return;
        }

        if (_outOfBreath)
        {
            _fill = Mathf.Max(0f, _fill - Time.deltaTime / Mathf.Max(0.01f, exhaustedRecoverySeconds));
            if (_fill <= 0f)
                Recover();
            return;
        }

        _restSeconds += Time.deltaTime;
        if (_restSeconds < restDelay)
            return;

        _fill = Mathf.Max(0f, _fill - Time.deltaTime / Mathf.Max(0.01f, secondsToEmpty));
    }

    // Shift against a wall or sprinting backwards at walking pace doesn't count as running.
    bool IsActuallyRunning()
    {
        if (!_controller.IsSprinting || _body == null)
            return false;

        Vector3 v = _body.velocity;
        return new Vector2(v.x, v.z).magnitude > _controller.MoveSpeed + 0.1f;
    }

    void RunOutOfBreath()
    {
        _outOfBreath = true;
        _restSeconds = 0f;
        _controller.SprintLocked = true;
        _controller.SpeedLimit = exhaustedWalkSpeed;

        if (outOfBreathClip == null)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(outOfBreathClip, outOfBreathVolume);
        else
            AudioSource.PlayClipAtPoint(outOfBreathClip, transform.position, outOfBreathVolume);
    }

    void Recover()
    {
        _outOfBreath = false;
        if (_controller != null)
        {
            _controller.SprintLocked = false;
            _controller.SpeedLimit = float.PositiveInfinity;
        }
    }

    void ResetMeter()
    {
        _fill = 0f;
        _restSeconds = 0f;
        if (_outOfBreath)
            Recover();
    }
}
