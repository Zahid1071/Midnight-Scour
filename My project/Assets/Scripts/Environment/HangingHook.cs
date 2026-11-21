using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// A jointed hanging hook (e.g. a meat-rail trolley hook) that the player shoves aside when walking through it.
/// The CharacterController never collides with the hook (that would snag the player on a thin hook at head
/// height); instead, whenever the player's head is within reach, the hook is driven toward the player's motion
/// plus a little push away from them. The joints do the rest, so it swings and settles naturally.
/// A chain loop bursts in when the hook is pushed or knocked, then decays exponentially even while it keeps
/// swinging, until the next push or knock.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class HangingHook : MonoBehaviour
{
    [Header("Player push")]
    [Tooltip("Horizontal reach of the hook beyond the player capsule radius.")]
    [Min(0f)]
    [SerializeField] float pushRadius = 0.18f;

    [Tooltip("How far above the top of the player capsule the hook still touches the head (the camera sits near the capsule top).")]
    [SerializeField] float headReachAbove = 0.42f;

    [Tooltip("How far below the top of the player capsule the hook still counts as touching (shoulders/chest).")]
    [SerializeField] float headReachBelow = 0.6f;

    [Tooltip("How quickly the hook is brought up to the pushing speed (1/s).")]
    [Min(0f)]
    [SerializeField] float pushResponse = 9f;

    [Tooltip("Extra sideways speed away from the player when standing inside the hook (m/s at full overlap).")]
    [Min(0f)]
    [SerializeField] float separationSpeed = 0.9f;

    [Tooltip("Upper limit of the push speed, so sprinting doesn't fling the hook into the ceiling.")]
    [Min(0f)]
    [SerializeField] float maxPushSpeed = 3.5f;

    [Header("Chain loop SFX")]
    [SerializeField] AudioClip swingLoopSfx;
    [SerializeField] AudioMixerGroup output;
    [SerializeField, Range(0f, 1f)] float swingLoopMaxVolume = 0.45f;
    [Tooltip("Volume kept while the hook is still swinging after the burst has died down. Keep 0 — a quiet tail reads as a hum that cuts.")]
    [SerializeField, Range(0f, 1f)] float swingLoopTailVolume = 0f;
    [Tooltip("Hook speed (m/s) of a push or knock that gives the full burst.")]
    [Min(0.01f)]
    [SerializeField] float swingSpeedForMaxVolume = 1.4f;
    [Tooltip("How fast the burst dies (1/s). Higher = shorter chain rattle.")]
    [Min(0f)]
    [SerializeField] float swingLoopDecay = 1.6f;
    [Tooltip("How fast the volume rises to a new burst (1/s).")]
    [Min(1f)]
    [SerializeField] float swingLoopAttack = 20f;
    [Tooltip("How fast the audible volume eases down when the burst ends (1/s). Keep low so Pause isn't a hard cut.")]
    [Min(0.05f)]
    [SerializeField] float swingLoopRelease = 0.55f;
    [Tooltip("Seconds to ease volume to silence after the target hits zero (prevents Pause clicks).")]
    [Min(0.05f)]
    [SerializeField] float swingLoopFadeOutSeconds = 1.25f;
    [Tooltip("Hook speed (m/s) below which the hook counts as still and the loop fades out.")]
    [Min(0.01f)]
    [SerializeField] float swingQuietSpeed = 0.12f;
    [SerializeField] Vector2 swingLoopPitch = new Vector2(0.9f, 1.05f);
    [SerializeField] float swingLoopMinDistance = 1f;
    [SerializeField] float swingLoopMaxDistance = 12f;

    Rigidbody _rb;
    CharacterController _player;
    AudioSource _loopSource;
    float _burst;
    float _loopVolumeSmoothed;
    bool _loopStarted;
    float _passVolumeScale = 1f;

    float EffectiveLoopMaxVolume => swingLoopMaxVolume * _passVolumeScale;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _player = player.GetComponentInParent<CharacterController>();
            if (_player == null)
                _player = player.GetComponentInChildren<CharacterController>();
        }
        if (_player != null)
        {
            // The whole rig (rod + hook) ignores the player capsule; pushing is done in FixedUpdate.
            Transform rig = transform.parent != null ? transform.parent : transform;
            foreach (Collider c in rig.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(c, _player);
        }

        CreateLoopSource();
    }

    void OnDisable()
    {
        if (_loopSource != null)
        {
            _loopSource.volume = 0f;
            _loopSource.Pause();
        }
        _burst = 0f;
        _loopVolumeSmoothed = 0f;
    }

    void OnCollisionEnter(Collision collision)
    {
        Excite(collision.relativeVelocity.magnitude);
    }

    public AudioClip ChainLoopClip => swingLoopSfx;
    public float ChainLoopMaxVolume => swingLoopMaxVolume;

    /// <summary>Swing the hook as if something brushed past it, at this velocity (m/s).</summary>
    public void Knock(Vector3 velocity)
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        EnsureLoopSource();
        if (_rb != null)
        {
            _rb.WakeUp();
            // Match a player shove: set horizontal velocity so UpdateLoopVolume hears real motion.
            Vector3 v = velocity;
            v.y = 0f;
            if (v.sqrMagnitude < 0.01f)
                v = transform.right * swingSpeedForMaxVolume;
            _rb.linearVelocity = new Vector3(v.x, _rb.linearVelocity.y, v.z);
            _rb.AddForce(v, ForceMode.VelocityChange);
        }
        float speed = Mathf.Max(velocity.magnitude, swingSpeedForMaxVolume);
        Excite(speed);
        KickLoopAudible(speed);
    }

    /// <summary>
    /// Scripted pass: keep exciting the chain loop like the player walking through (velocity-driven volume).
    /// </summary>
    public System.Collections.IEnumerator KnockPassRoutine(Vector3 direction, float speed, float duration = 0.4f,
        float volumeScale = 1f)
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        EnsureLoopSource();
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f)
            direction = Vector3.right;
        direction.Normalize();
        float spd = Mathf.Max(speed, swingSpeedForMaxVolume);
        float end = Time.time + Mathf.Max(0.15f, duration);
        _passVolumeScale = Mathf.Max(1f, volumeScale);
        try
        {
            while (Time.time < end)
            {
                if (_rb != null)
                {
                    _rb.WakeUp();
                    Vector3 v = direction * spd;
                    _rb.linearVelocity = new Vector3(v.x, _rb.linearVelocity.y, v.z);
                }
                Excite(spd);
                KickLoopAudible(spd);
                yield return new WaitForFixedUpdate();
            }
        }
        finally
        {
            _passVolumeScale = 1f;
        }
    }

    /// <summary>Restart the chain burst (a push or a knock). Stronger hits give a louder burst.</summary>
    void Excite(float speed)
    {
        _burst = Mathf.Max(_burst, Mathf.Clamp01(speed / Mathf.Max(0.01f, swingSpeedForMaxVolume)));
    }

    void EnsureLoopSource()
    {
        if (_loopSource != null || swingLoopSfx == null)
            return;
        CreateLoopSource();
    }

    /// <summary>Force the same chain loop the player hears when walking through — full burst, playing now.</summary>
    void KickLoopAudible(float speed)
    {
        EnsureLoopSource();
        if (_loopSource == null)
            return;
        float level = Mathf.Clamp01(speed / Mathf.Max(0.01f, swingSpeedForMaxVolume));
        _burst = Mathf.Max(_burst, level);
        float cap = EffectiveLoopMaxVolume;
        float vol = Mathf.Max(cap * Mathf.Max(0.55f, level), cap * 0.55f);
        _loopVolumeSmoothed = Mathf.Max(_loopVolumeSmoothed, vol);
        _loopSource.volume = _loopVolumeSmoothed;
        if (!_loopSource.isPlaying)
        {
            if (_loopStarted)
                _loopSource.UnPause();
            else
            {
                _loopSource.Play();
                _loopStarted = true;
            }
        }
    }

    void FixedUpdate()
    {
        if (_player == null || !_player.enabled || !_player.gameObject.activeInHierarchy)
            return;

        Transform pt = _player.transform;
        float scale = pt.lossyScale.y;
        float radius = _player.radius * scale;
        Vector3 capsuleTop = pt.TransformPoint(_player.center) + Vector3.up * (_player.height * 0.5f * scale);

        Vector3 hookPoint = _rb.worldCenterOfMass;
        float dy = hookPoint.y - capsuleTop.y;
        Vector3 away = hookPoint - pt.position;
        away.y = 0f;
        float dist = away.magnitude;
        float reach = radius + pushRadius;

        if (dy > headReachAbove || dy < -headReachBelow || dist >= reach)
            return;

        Vector3 playerVel = _player.velocity;
        playerVel.y = 0f;
        if (dist > 0.0001f)
            away /= dist;
        else
            away = playerVel.sqrMagnitude > 0.0001f ? playerVel.normalized : pt.forward;

        float overlap = 1f - dist / reach;
        Vector3 target = Vector3.ClampMagnitude(playerVel + away * (separationSpeed * overlap), maxPushSpeed);

        Vector3 hookVel = _rb.GetPointVelocity(hookPoint);
        hookVel.y = 0f;
        Vector3 dv = target - hookVel;
        // Only push: never drag a hook that is already swinging away back toward the player.
        float towardPlayer = Vector3.Dot(dv, away);
        if (towardPlayer < 0f && Vector3.Dot(hookVel, away) > 0f)
            dv -= away * towardPlayer;

        _rb.WakeUp();
        _rb.AddForce(dv * pushResponse, ForceMode.Acceleration);
        Excite(Mathf.Max(target.magnitude, hookVel.magnitude));
    }

    void Update()
    {
        UpdateLoopVolume();
    }

    void CreateLoopSource()
    {
        if (swingLoopSfx == null || _loopSource != null)
            return;

        // Many hooks hang in the basement and they're silent most of the time, so they don't take reflection slots.
        _loopSource = SpatialAudio.Create("HookChainLoop", transform, transform.position, output, swingLoopMinDistance,
            swingLoopMaxDistance, AudioRolloffMode.Linear, false);
        if (_loopSource == null)
            return;
        _loopSource.clip = swingLoopSfx;
        _loopSource.loop = true;
        _loopSource.pitch = Random.Range(swingLoopPitch.x, swingLoopPitch.y);
        _loopSource.volume = 0f;
        // Hooks start at different points of the loop so several swinging together don't phase.
        if (swingLoopSfx.length > 0.05f)
            _loopSource.time = Random.Range(0f, swingLoopSfx.length);
    }

    void UpdateLoopVolume()
    {
        if (_loopSource == null)
            return;

        // With time frozen, ease out instead of Pause (Pause mid-loop clicks / cuts the chain).
        float dt = Time.timeScale <= 0f ? Time.unscaledDeltaTime : Time.deltaTime;
        if (Time.timeScale <= 0f)
            _burst = 0f;

        float speed = _rb != null && !_rb.IsSleeping()
            ? _rb.GetPointVelocity(_rb.worldCenterOfMass).magnitude
            : 0f;
        _burst *= Mathf.Exp(-swingLoopDecay * dt);

        // Soft motion gate: never zero the target instantly when the hook slows / sleeps.
        float moving = Time.timeScale <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(speed / swingQuietSpeed));
        if (_burst > 0.02f)
            moving = Mathf.Max(moving, 0.35f);
        float target = Mathf.Max(_burst * EffectiveLoopMaxVolume, swingLoopTailVolume) * moving;

        if (target > _loopVolumeSmoothed)
        {
            _loopVolumeSmoothed = Mathf.MoveTowards(
                _loopVolumeSmoothed, target, swingLoopAttack * dt * Mathf.Max(target, 0.05f));
        }
        else
        {
            // Linear release into silence — avoids Pause cutting mid-rattle.
            float fadeStep = EffectiveLoopMaxVolume / Mathf.Max(0.05f, swingLoopFadeOutSeconds);
            float releaseStep = Mathf.Max(swingLoopRelease * Mathf.Max(_loopVolumeSmoothed, 0.02f), fadeStep);
            _loopVolumeSmoothed = Mathf.MoveTowards(_loopVolumeSmoothed, target, releaseStep * dt);
        }

        _loopSource.volume = _loopVolumeSmoothed;

        bool audible = _loopVolumeSmoothed > 0.002f;
        if (audible && !_loopSource.isPlaying)
        {
            if (_loopStarted)
                _loopSource.UnPause();
            else
                _loopSource.Play();
            _loopStarted = true;
        }
        else if (!audible && _loopSource.isPlaying)
        {
            _loopSource.volume = 0f;
            _loopSource.Pause();
            _loopVolumeSmoothed = 0f;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Rigidbody rb = _rb != null ? _rb : GetComponent<Rigidbody>();
        if (rb == null)
            return;
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(rb.worldCenterOfMass, pushRadius);
    }
#endif
}
