using StarterAssets;
using UnityEngine;

/// <summary>
/// Run stamina (demo). Disabled for now — kept so scene references stay valid.
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
    public float Fill01 => 0f;
    public bool IsOutOfBreath => false;

    void Awake()
    {
        _controller = GetComponent<FirstPersonController>();
        _body = GetComponent<CharacterController>();
        // Demo stamina is off for now.
        enabled = false;
        ResetMeter();
    }

    void OnDisable() => ResetMeter();

    void Update()
    {
        // Intentionally empty while the feature is parked.
    }

    void ResetMeter()
    {
        _fill = 0f;
        _restSeconds = 0f;
        _outOfBreath = false;
        if (_controller != null)
        {
            _controller.SprintLocked = false;
            _controller.SpeedLimit = float.PositiveInfinity;
        }
    }
}
