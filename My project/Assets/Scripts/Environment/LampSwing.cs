using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Soft pendulum sway for hanging lamps. Idle oscillation plus a thunder "push"
/// (driven by <see cref="ThunderManager"/> on strike).
/// <para>
/// <see cref="swingYawDegrees"/> is the top-down compass heading of the swing plane
/// (0 = swing along local +Z, tilting around local +X).
/// </para>
/// </summary>
[DisallowMultipleComponent]
public class LampSwing : MonoBehaviour
{
    static readonly List<LampSwing> Active = new();

    [Header("Swing Plane (top-down)")]
    [Tooltip("Yaw of the swing plane in degrees, viewed from above. 0 = along local +Z; 90 = along local +X.")]
    [Range(0f, 360f)]
    [SerializeField] float swingYawDegrees = 0f;

    [Header("Idle Sway")]
    [Tooltip("How far the lamp tips at idle, in degrees.")]
    [SerializeField] float idleAngleDegrees = 1.5f;
    [SerializeField] float idleFrequency = 0.35f;
    [Tooltip("0-1 random phase offset so multiple lamps don't sync perfectly.")]
    [SerializeField] bool randomizePhase = true;

    [Header("Thunder Push")]
    [Tooltip("Extra tip angle added when ApplyThunderImpulse(1) is called.")]
    [SerializeField] float thunderImpulseAngleDegrees = 12f;
    [SerializeField] float springStiffness = 28f;
    [SerializeField] float springDamping = 4.5f;
    [SerializeField] float maxAngleDegrees = 25f;

    [Header("Swing SFX")]
    [SerializeField] AudioClip swingSfx;
    [SerializeField] float swingSfxVolume = 1f;
    [SerializeField] Vector2 swingSfxPitchRange = new Vector2(0.95f, 1.05f);

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    Quaternion _restLocalRotation;
    float _angle;
    float _angularVelocity;
    float _phase;
    float _idleSeed;

    public float SwingYawDegrees
    {
        get => swingYawDegrees;
        set => swingYawDegrees = Mathf.Repeat(value, 360f);
    }

    public static IReadOnlyList<LampSwing> ActiveSwings => Active;

    void Awake()
    {
        _restLocalRotation = transform.localRotation;
        _phase = randomizePhase ? Random.Range(0f, Mathf.PI * 2f) : 0f;
        _idleSeed = Random.Range(0.85f, 1.15f);
    }

    void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
        transform.localRotation = _restLocalRotation;
        _angle = 0f;
        _angularVelocity = 0f;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        float idleTarget = 0f;
        if (idleAngleDegrees > 0.001f && idleFrequency > 0.001f)
        {
            float t = Time.time * idleFrequency * _idleSeed * Mathf.PI * 2f + _phase;
            idleTarget = Mathf.Sin(t) * idleAngleDegrees;
        }

        float accel = (idleTarget - _angle) * springStiffness - _angularVelocity * springDamping;
        _angularVelocity += accel * dt;
        _angle += _angularVelocity * dt;
        _angle = Mathf.Clamp(_angle, -maxAngleDegrees, maxAngleDegrees);

        Vector3 hingeAxis = Quaternion.Euler(0f, swingYawDegrees, 0f) * Vector3.right;
        transform.localRotation = _restLocalRotation * Quaternion.AngleAxis(_angle, hingeAxis);
    }

    /// <summary>
    /// Knock the lamp. Called by <see cref="ThunderManager"/> on thunder strike.
    /// </summary>
    public void ApplyThunderImpulse(float strength = 1f, float sign = 0f)
    {
        if (Mathf.Approximately(sign, 0f))
            sign = Random.value < 0.5f ? -1f : 1f;
        else
            sign = Mathf.Sign(sign);

        float impulse = thunderImpulseAngleDegrees * strength * sign;
        _angularVelocity += impulse * Mathf.Sqrt(Mathf.Max(0.01f, springStiffness));

        PlaySwingSfx();
    }

    void PlaySwingSfx()
    {
        if (swingSfx == null || SoundManager.Instance == null)
            return;

        float pitch = Random.Range(swingSfxPitchRange.x, swingSfxPitchRange.y);
        SoundManager.Instance.PlaySFXAt(swingSfx, transform.position, swingSfxVolume, pitch);
    }

#if UNITY_EDITOR
    [ContextMenu("Test Thunder Impulse")]
    void ContextTestImpulse() => ApplyThunderImpulse(1f);

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 pivot = transform.position;
        Vector3 hingeWorld = transform.TransformDirection(
            Quaternion.Euler(0f, swingYawDegrees, 0f) * Vector3.right).normalized;
        Vector3 swingDir = Vector3.Cross(Vector3.up, hingeWorld).normalized;
        if (swingDir.sqrMagnitude < 0.001f)
            swingDir = transform.forward;

        float preview = Mathf.Max(idleAngleDegrees, 3f);
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(pivot, pivot + swingDir * 0.35f);
        Gizmos.DrawLine(pivot, pivot - swingDir * 0.35f);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.7f);
        Quaternion a = Quaternion.AngleAxis(-preview, hingeWorld);
        Quaternion b = Quaternion.AngleAxis(preview, hingeWorld);
        Vector3 down = -transform.up * 0.4f;
        Gizmos.DrawLine(pivot, pivot + a * down);
        Gizmos.DrawLine(pivot, pivot + b * down);
    }
#endif
}
