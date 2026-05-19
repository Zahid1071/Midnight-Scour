using UnityEngine;

/// <summary>
/// Applies vertical camera bob on top of the standing/crouch eye height from PlayerCrouchStance.
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

    [Header("Bob Speed")]
    [SerializeField] float walkBobSpeed = 10f;
    [SerializeField] float runBobSpeed = 14f;
    [SerializeField] float sneakBobSpeed = 6f;

    [Header("Bob Amount")]
    [SerializeField] float walkBobAmountY = 0.05f;
    [SerializeField] float runBobAmountY = 0.08f;
    [SerializeField] float sneakBobAmountY = 0.03f;

    [Header("Speed Thresholds")]
    [SerializeField] float minSpeed = 0.1f;
    [SerializeField] float runThreshold = 4f;
    [SerializeField] float walkThreshold = 2f;

    [Header("Tuning")]
    [SerializeField, Range(0f, 2f)] float strength = 1f;

    Vector3 _standLocalPosition;
    float bobTimer;
    bool initialized;

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

        Vector3 basePos = EyeBasePosition();
        float bobY = 0f;

        bool canBob = strength > 0f
            && !PlayerControlGate.IsInputBlocked
            && controller.isGrounded;

        if (canBob)
        {
            float speed = new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;
            if (speed >= minSpeed)
            {
                float bobSpeed;
                float bobAmountY;
                if (speed > runThreshold)
                {
                    bobSpeed = runBobSpeed;
                    bobAmountY = runBobAmountY;
                }
                else if (speed > walkThreshold)
                {
                    bobSpeed = walkBobSpeed;
                    bobAmountY = walkBobAmountY;
                }
                else
                {
                    bobSpeed = sneakBobSpeed;
                    bobAmountY = sneakBobAmountY;
                }

                bobTimer += Time.deltaTime * bobSpeed;
                bobY = Mathf.Sin(bobTimer) * bobAmountY * strength;
            }
            else
            {
                bobTimer = 0f;
            }
        }
        else
        {
            bobTimer = 0f;
        }

        bobTarget.localPosition = basePos + new Vector3(0f, bobY, 0f);
    }

    Vector3 EyeBasePosition()
    {
        float eyeY = crouchStance != null ? crouchStance.EyeBaseLocalY : _standLocalPosition.y;
        return new Vector3(_standLocalPosition.x, eyeY, _standLocalPosition.z);
    }
}
