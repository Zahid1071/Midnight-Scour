using UnityEngine;

/// <summary>
/// Look and movement sway for the held item views. Sits on the held item root under the camera and
/// offsets/rotates it around a pivot at the right hand, so the hand swings around itself instead of
/// the screen centre. Layers: spring-driven look sway and movement inertia (lag + overshoot), a
/// figure-8 walk bob in position and rotation synced to the step cycle, a landing kick and a small
/// idle drift.
/// </summary>
[DefaultExecutionOrder(-50)] // before FollowHeldItemSocket reads sockets in LateUpdate
[DisallowMultipleComponent]
public sealed class ViewmodelSway : MonoBehaviour
{
    [Header("Sources")]
    [Tooltip("Transform rotated by mouse look (PlayerCameraRoot).")]
    [SerializeField] private Transform lookSource;

    [SerializeField] private CharacterController body;

    [Header("Pivot")]
    [Tooltip("Point the sway rotates around, in the parent's (camera) space. Default: the right hand.")]
    [SerializeField] private Vector3 pivot = new(0.14f, -0.17f, 0.42f);

    [Header("Look Sway")]
    [Tooltip("Degrees of pitch/yaw lag per degree/second of look speed.")]
    [SerializeField] private float lookRotation = 0.035f;

    [Tooltip("Degrees of roll per degree/second of horizontal look speed.")]
    [SerializeField] private float lookRoll = 0.03f;

    [Tooltip("Metres of hand lag per degree/second of look speed.")]
    [SerializeField] private float lookPosition = 0.00006f;

    [SerializeField] private float maxLookRotation = 10f;
    [SerializeField] private float maxLookPosition = 0.03f;

    [Tooltip("Smooths frame-to-frame mouse spikes before they drive the sway.")]
    [Min(0.001f)]
    [SerializeField] private float lookSmoothing = 0.04f;

    [Header("Walk Bob (figure-8)")]
    [Tooltip("Speed that gives the full bob (walk speed).")]
    [Min(0.01f)]
    [SerializeField] private float referenceSpeed = 2.6f;

    [Tooltip("Bob cycles (two steps) per second at the reference speed.")]
    [SerializeField] private float bobFrequency = 1.0f;

    [Tooltip("x = side to side (once per cycle), y = up/down dip on every step, z = push forward on every step.")]
    [SerializeField] private Vector3 bobPosition = new(0.011f, 0.008f, 0.003f);

    [Tooltip("Degrees: x = pitch nod on every step, y = yaw and z = roll swinging with the side motion.")]
    [SerializeField] private Vector3 bobRotation = new(1.4f, 1.0f, 2.2f);

    [Tooltip("Bob amplitude multiplier at sprint speed and above.")]
    [SerializeField] private float sprintBobMultiplier = 1.6f;

    [Tooltip("Speed at which the sprint multiplier is fully applied.")]
    [SerializeField] private float sprintSpeed = 4f;

    [Header("Movement Inertia")]
    [Tooltip("Metres the hand trails behind per m/s of movement (x = strafe, y = vertical, z = forward).")]
    [SerializeField] private Vector3 moveLag = new(0.006f, 0.006f, 0.005f);

    [SerializeField] private float maxMoveLag = 0.03f;

    [Tooltip("Degrees of roll per m/s of strafe speed (leans into the strafe).")]
    [SerializeField] private float strafeRoll = 2.5f;

    [Tooltip("Degrees of forward tilt per m/s of forward speed.")]
    [SerializeField] private float forwardPitch = 0.8f;

    [Tooltip("Landing kick per m/s of fall speed: x = metres down, y = degrees of nod.")]
    [SerializeField] private Vector2 landingKick = new(0.0035f, 0.9f);

    [Header("Idle Drift")]
    [SerializeField] private float idlePosition = 0.0015f;
    [SerializeField] private float idleRotation = 0.6f;
    [SerializeField] private float idleSpeed = 0.35f;

    [Header("Springs")]
    [SerializeField] private float positionStiffness = 110f;
    [Range(0.1f, 1.5f)]
    [SerializeField] private float positionDamping = 0.5f;
    [SerializeField] private float rotationStiffness = 80f;
    [Range(0.1f, 1.5f)]
    [Tooltip("Lower = more overshoot and wobble after a turn.")]
    [SerializeField] private float rotationDamping = 0.4f;

    private Quaternion _lastLook;
    private bool _hasLast;
    private Vector2 _lookVelocity;
    private Vector3 _moveVelocity;
    private float _bobPhase;
    private float _bobWeight;
    private bool _wasGrounded = true;
    private float _fallSpeed;
    private float _idleSeed;

    private Vector3 _pos, _posVel;
    private Vector3 _rot, _rotVel;

    private void Awake()
    {
        _idleSeed = Random.value * 100f;
    }

    private void OnEnable()
    {
        _hasLast = false;
        _pos = _posVel = _rot = _rotVel = Vector3.zero;
        _lookVelocity = Vector2.zero;
        _moveVelocity = Vector3.zero;
        _bobWeight = 0f;
        ApplyPose(Vector3.zero, Vector3.zero);
    }

    private void OnDisable()
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0f)
            return;

        Vector3 springPos = Vector3.zero;
        Vector3 springRot = Vector3.zero;
        UpdateLook(dt, ref springPos, ref springRot);

        Vector3 bobPos = Vector3.zero;
        Vector3 bobRot = Vector3.zero;
        UpdateMovement(dt, ref springPos, ref springRot, ref bobPos, ref bobRot);

        Step(ref _pos, ref _posVel, springPos, positionStiffness, positionDamping, dt);
        Step(ref _rot, ref _rotVel, springRot, rotationStiffness, rotationDamping, dt);

        float t = Time.time * idleSpeed + _idleSeed;
        Vector3 idlePos = new Vector3(Noise(t, 0f), Noise(t, 1f), 0f) * idlePosition;
        Vector3 idleRot = new Vector3(Noise(t, 2f), Noise(t, 3f), Noise(t, 4f)) * idleRotation;

        ApplyPose(_pos + bobPos + idlePos, _rot + bobRot + idleRot);
    }

    private void UpdateLook(float dt, ref Vector3 targetPos, ref Vector3 targetRot)
    {
        Transform source = lookSource != null ? lookSource : transform.parent;
        if (source == null)
            return;

        Quaternion look = source.rotation;
        Vector2 rawVelocity = Vector2.zero;
        if (_hasLast)
        {
            Vector3 delta = (Quaternion.Inverse(_lastLook) * look).eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, delta.x);
            float yaw = Mathf.DeltaAngle(0f, delta.y);
            // Teleports and cutscene snaps are not look input.
            if (Mathf.Abs(pitch) < 45f && Mathf.Abs(yaw) < 45f)
                rawVelocity = new Vector2(yaw, pitch) / dt;
        }
        _lastLook = look;
        _hasLast = true;

        _lookVelocity = Vector2.Lerp(_lookVelocity, rawVelocity, 1f - Mathf.Exp(-dt / lookSmoothing));
        float yawVel = _lookVelocity.x;
        float pitchVel = _lookVelocity.y;

        targetRot += new Vector3(
            Mathf.Clamp(-pitchVel * lookRotation, -maxLookRotation, maxLookRotation),
            Mathf.Clamp(-yawVel * lookRotation, -maxLookRotation, maxLookRotation),
            Mathf.Clamp(yawVel * lookRoll, -maxLookRotation, maxLookRotation));
        targetPos += new Vector3(
            Mathf.Clamp(-yawVel * lookPosition, -maxLookPosition, maxLookPosition),
            Mathf.Clamp(pitchVel * lookPosition, -maxLookPosition, maxLookPosition),
            0f);
    }

    private void UpdateMovement(float dt, ref Vector3 springPos, ref Vector3 springRot,
                                ref Vector3 bobPos, ref Vector3 bobRot)
    {
        if (body == null)
            return;

        // CharacterController velocity jitters frame to frame; smooth it so the bob doesn't flicker.
        // It also keeps the last move's value once nothing moves the body (cinematics), so blocked = standing.
        Vector3 rawLocal = PlayerControlGate.IsInputBlocked
            ? Vector3.zero
            : body.transform.InverseTransformDirection(body.velocity);
        _moveVelocity = Vector3.Lerp(_moveVelocity, rawLocal, 1f - Mathf.Exp(-dt * 10f));
        Vector3 v = _moveVelocity;

        bool grounded = body.isGrounded;
        if (!grounded)
            _fallSpeed = Mathf.Max(0f, -rawLocal.y);
        else if (!_wasGrounded && _fallSpeed > 1f)
        {
            _posVel.y -= _fallSpeed * landingKick.x * positionStiffness * 0.1f;
            _rotVel.x += _fallSpeed * landingKick.y * rotationStiffness * 0.1f;
            _fallSpeed = 0f;
        }
        _wasGrounded = grounded;

        float speed = new Vector2(v.x, v.z).magnitude;
        float speedFactor = speed / referenceSpeed;
        float weight = grounded ? Mathf.Clamp01(speedFactor) : 0f;
        _bobWeight = Mathf.Lerp(_bobWeight, weight, 1f - Mathf.Exp(-dt * 6f));
        _bobPhase = Mathf.Repeat(_bobPhase + dt * bobFrequency * Mathf.Clamp(speedFactor, 0.6f, 1.5f) * 2f * Mathf.PI,
                                 2f * Mathf.PI);

        float sprint = Mathf.InverseLerp(referenceSpeed, Mathf.Max(sprintSpeed, referenceSpeed + 0.01f), speed);
        float amp = _bobWeight * Mathf.Lerp(1f, sprintBobMultiplier, sprint);

        // Lissajous: side swing once per cycle, dip/nod twice (once per footstep, lowest mid-swing).
        float side = Mathf.Sin(_bobPhase);
        float step = -Mathf.Cos(_bobPhase * 2f);
        bobPos = new Vector3(side * bobPosition.x, step * bobPosition.y, -step * bobPosition.z) * amp;
        bobRot = new Vector3(-step * bobRotation.x, side * bobRotation.y, -side * bobRotation.z) * amp;

        Vector3 lag = new(-v.x * moveLag.x, -v.y * moveLag.y, -v.z * moveLag.z);
        springPos += Vector3.ClampMagnitude(lag, maxMoveLag);
        springRot.x += Mathf.Clamp(v.z * forwardPitch, -5f, 5f);
        springRot.z += Mathf.Clamp(-v.x * strafeRoll, -8f, 8f);
    }

    private static void Step(ref Vector3 x, ref Vector3 vel, Vector3 target, float k, float zeta, float dt)
    {
        float c = 2f * Mathf.Sqrt(k) * zeta;
        vel += (k * (target - x) - c * vel) * dt;
        x += vel * dt;
    }

    private static float Noise(float t, float channel) => Mathf.PerlinNoise(t, channel * 7.31f) * 2f - 1f;

    private void ApplyPose(Vector3 position, Vector3 euler)
    {
        Quaternion rot = Quaternion.Euler(euler);
        transform.localRotation = rot;
        transform.localPosition = pivot + position - rot * pivot;
    }

    private void OnDrawGizmosSelected()
    {
        if (transform.parent == null)
            return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.parent.TransformPoint(pivot), 0.02f);
    }
}
