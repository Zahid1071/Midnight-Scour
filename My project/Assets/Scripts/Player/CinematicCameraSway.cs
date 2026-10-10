using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Subtle handheld feel on the player's Cinemachine camera: slow breathing drift, a side-to-side roll in step
/// with the head bob, a lean into strafes and a lagging roll into turns. Rotation only, layered after noise.
/// </summary>
[AddComponentMenu("Cinemachine/Cinematic Camera Sway")]
public class CinematicCameraSway : CinemachineExtension
{
    [SerializeField] CharacterController controller;
    [SerializeField] HeadBobController headBob;
    [Range(0f, 3f)]
    [SerializeField] float strength = 1f;

    [Header("Breathing (always on)")]
    [SerializeField] float breathePitchDegrees = 0.6f;
    [SerializeField] float breatheYawDegrees = 0.45f;
    [SerializeField] float breatheRollDegrees = 0.35f;
    [Tooltip("Breaths per second.")]
    [SerializeField] float breatheRate = 0.22f;

    [Header("Walk sway (in step with the head bob)")]
    [Tooltip("Roll at walking speed; leans over the planted foot, one full side-to-side per two steps.")]
    [SerializeField] float walkRollDegrees = 1.4f;
    [SerializeField] float walkYawDegrees = 0.6f;
    [Tooltip("Small nod on each step, on top of the head bob's height change.")]
    [SerializeField] float walkPitchDegrees = 0.5f;
    [Tooltip("Speed (m/s) at which the walk sway reaches its full amount; faster scales it up a little.")]
    [SerializeField] float walkReferenceSpeed = 3f;
    [SerializeField] float maxSpeedScale = 1.4f;

    [Header("Strafe lean")]
    [SerializeField] float strafeRollDegrees = 2.5f;
    [SerializeField] float strafeReferenceSpeed = 4f;

    [Header("Turn roll")]
    [SerializeField] float turnRollDegrees = 2.5f;
    [Tooltip("Turn speed (deg/s) that reaches the full turn roll.")]
    [SerializeField] float turnReferenceSpeed = 360f;

    [Header("Smoothing (per second)")]
    [SerializeField] float moveSmooth = 4f;
    [SerializeField] float leanSmooth = 5f;

    float _moveBlend;
    float _speedScale = 1f;
    float _phase;
    float _strafe;
    float _turn;
    float _lastYaw;
    bool _hasYaw;
    float _lastUpdateTime = -1f;
    Vector3 _offset;

    void ResolveReferences()
    {
        if (controller == null)
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
                controller = player.GetComponent<CharacterController>();
        }
        if (headBob == null && controller != null)
            headBob = controller.GetComponent<HeadBobController>();
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Noise || strength <= 0f)
            return;

        if (!Mathf.Approximately(_lastUpdateTime, Time.time))
        {
            _lastUpdateTime = Time.time;
            Tick(Time.deltaTime);
        }

        state.OrientationCorrection *= Quaternion.Euler(_offset * strength);
    }

    void Tick(float dt)
    {
        if (dt <= 0f)
            return;
        if (controller == null)
            ResolveReferences();

        float t = Time.time;
        float breathe = t * breatheRate * 2f * Mathf.PI;
        Vector3 offset = new Vector3(
            Mathf.Sin(breathe) * breathePitchDegrees + (Mathf.PerlinNoise(t * 0.3f, 1.7f) - 0.5f) * breathePitchDegrees,
            (Mathf.PerlinNoise(t * 0.25f, 5.3f) - 0.5f) * 2f * breatheYawDegrees,
            (Mathf.PerlinNoise(t * 0.2f, 9.1f) - 0.5f) * 2f * breatheRollDegrees);

        Vector3 localVelocity = Vector3.zero;
        bool grounded = false;
        float yaw = 0f;
        if (controller != null)
        {
            localVelocity = controller.transform.InverseTransformDirection(controller.velocity);
            grounded = controller.isGrounded;
            yaw = controller.transform.eulerAngles.y;
        }
        bool blocked = PlayerControlGate.IsInputBlocked;

        float speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
        bool moving = !blocked && grounded && speed > 0.1f;
        float targetBlend = moving ? Mathf.Clamp01(speed / Mathf.Max(0.01f, walkReferenceSpeed)) : 0f;
        _moveBlend = Mathf.Lerp(_moveBlend, targetBlend, 1f - Mathf.Exp(-moveSmooth * dt));
        if (moving)
        {
            _speedScale = Mathf.Lerp(_speedScale,
                Mathf.Clamp(speed / Mathf.Max(0.01f, walkReferenceSpeed), 1f, maxSpeedScale),
                1f - Mathf.Exp(-moveSmooth * dt));
            if (headBob == null)
                _phase += dt * 10f;
        }

        float sway = _moveBlend * _speedScale;
        if (headBob != null)
        {
            // Nod down into each footfall and lean over the planted foot, matching the head bob.
            offset.x += (headBob.StepImpact - 0.5f) * 2f * walkPitchDegrees * sway;
            offset.y += headBob.StepSide * walkYawDegrees * sway;
            offset.z -= headBob.StepSide * walkRollDegrees * sway;
        }
        else
        {
            offset.x += Mathf.Sin(_phase) * walkPitchDegrees * sway;
            offset.y += Mathf.Sin(_phase * 0.5f) * walkYawDegrees * sway;
            offset.z += Mathf.Sin(_phase * 0.5f) * walkRollDegrees * sway;
        }

        float strafeTarget = blocked ? 0f : Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, strafeReferenceSpeed), -1f, 1f);
        _strafe = Mathf.Lerp(_strafe, strafeTarget, 1f - Mathf.Exp(-leanSmooth * dt));
        offset.z -= _strafe * strafeRollDegrees;

        float turnRate = _hasYaw ? Mathf.DeltaAngle(_lastYaw, yaw) / dt : 0f;
        _lastYaw = yaw;
        _hasYaw = controller != null;
        float turnTarget = blocked ? 0f : Mathf.Clamp(turnRate / Mathf.Max(1f, turnReferenceSpeed), -1f, 1f);
        _turn = Mathf.Lerp(_turn, turnTarget, 1f - Mathf.Exp(-leanSmooth * dt));
        offset.z -= _turn * turnRollDegrees;

        _offset = offset;
    }
}
