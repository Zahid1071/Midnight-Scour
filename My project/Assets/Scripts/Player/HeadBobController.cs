using UnityEngine;

/// <summary>
/// Outlast-style walk bob on top of the standing/crouch eye height from PlayerCrouchStance: the head drops
/// sharply on each footfall, rises softly between steps and shifts over the planted foot. The step cycle is
/// read from PlayerFootstepRhythm so every footstep sound lands on the bottom of a dip.
/// Sole writer of PlayerCameraRoot localPosition.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class HeadBobController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] CharacterController controller;
    [SerializeField] Transform bobTarget;
    [SerializeField] PlayerCrouchStance crouchStance;
    [Tooltip("Step cycle source. Without it the bob counts its own steps every Fallback Step Distance.")]
    [SerializeField] PlayerFootstepRhythm footsteps;

    [Header("Vertical drop per step (meters, top to bottom)")]
    [SerializeField] float sneakBobAmountY = 0.035f;
    [SerializeField] float walkBobAmountY = 0.06f;
    [SerializeField] float runBobAmountY = 0.06f;

    [Header("Side shift over the planted foot (meters)")]
    [SerializeField] float sneakBobAmountX = 0.015f;
    [SerializeField] float walkBobAmountX = 0.025f;
    [SerializeField] float runBobAmountX = 0.024f;

    [Header("Speeds the amounts are blended between (m/s)")]
    [SerializeField] float sneakSpeed = 1f;
    [SerializeField] float walkSpeed = 2.6f;
    [SerializeField] float runSpeed = 4f;

    [Header("Feel")]
    [Tooltip("Above 1 the drop into each footfall gets shorter and harder.")]
    [SerializeField, Range(1f, 3f)] float impactSharpness = 1.5f;
    [Tooltip("How fast the bob fades in when you start moving (1/s).")]
    [SerializeField] float blendIn = 6f;
    [Tooltip("How fast the bob settles when you stop (1/s).")]
    [SerializeField] float blendOut = 4f;
    [SerializeField] float minSpeed = 0.1f;
    [SerializeField] float fallbackStepDistance = 1.8f;

    [Header("Tuning")]
    [SerializeField, Range(0f, 2f)] float strength = 1f;

    Vector3 _standLocalPosition;
    float _phase;
    float _ownSteps;
    float _weight;
    float _amountY;
    float _amountX;
    bool initialized;

    /// <summary>Step cycle in radians: 2π per footstep, multiples of 2π on each footfall. Holds while standing.</summary>
    public float BobPhase => _phase;
    /// <summary>0 between steps, 1 on a footfall (already sharpened).</summary>
    public float StepImpact => Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(_phase * 0.5f)), impactSharpness);
    /// <summary>-1..1, which foot the head is over; flips each step.</summary>
    public float StepSide => Mathf.Cos(_phase * 0.5f);

    void Awake()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (bobTarget == null)
        {
            var tagged = transform.Find("PlayerCameraRoot");
            if (tagged != null)
                bobTarget = tagged;
        }

        if (crouchStance == null)
            crouchStance = GetComponent<PlayerCrouchStance>();
        if (footsteps == null)
            footsteps = GetComponent<PlayerFootstepRhythm>();

        CacheStandPosition();
    }

    void CacheStandPosition()
    {
        if (bobTarget == null)
            return;

        _standLocalPosition = bobTarget.localPosition;
        initialized = true;
    }

    void LateUpdate()
    {
        if (!initialized || bobTarget == null || controller == null)
            return;

        float dt = Time.deltaTime;
        float speed = new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;
        bool moving = strength > 0f
            && !PlayerControlGate.IsInputBlocked
            && controller.isGrounded
            && speed >= minSpeed;

        _weight = Mathf.MoveTowards(_weight, moving ? 1f : 0f, dt * (moving ? blendIn : blendOut));

        if (footsteps != null)
            _phase = footsteps.StepProgress * Mathf.PI * 2f;
        else if (moving)
        {
            _ownSteps += speed * dt / Mathf.Max(0.1f, fallbackStepDistance);
            _phase = _ownSteps * Mathf.PI * 2f;
        }

        // Amounts follow speed while moving and hold while settling, so stopping doesn't shrink the last step.
        if (moving)
        {
            _amountY = BlendBySpeed(speed, sneakBobAmountY, walkBobAmountY, runBobAmountY);
            _amountX = BlendBySpeed(speed, sneakBobAmountX, walkBobAmountX, runBobAmountX);
        }

        float scale = _weight * strength;
        float bobY = (0.5f - StepImpact) * _amountY * scale;
        float bobX = StepSide * _amountX * scale;

        bobTarget.localPosition = EyeBasePosition() + new Vector3(bobX, bobY, 0f);
    }

    float BlendBySpeed(float speed, float sneak, float walk, float run)
    {
        if (speed <= walkSpeed)
            return Mathf.Lerp(sneak, walk, Mathf.InverseLerp(sneakSpeed, walkSpeed, speed));
        return Mathf.Lerp(walk, run, Mathf.InverseLerp(walkSpeed, runSpeed, speed));
    }

    Vector3 EyeBasePosition()
    {
        float eyeY = crouchStance != null ? crouchStance.EyeBaseLocalY : _standLocalPosition.y;
        return new Vector3(_standLocalPosition.x, eyeY, _standLocalPosition.z);
    }
}
