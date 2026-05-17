using UnityEngine;

/// <summary>
/// Applies a subtle sine-based camera bob to the Cinemachine follow target while moving on foot.
/// </summary>
[DisallowMultipleComponent]
public class HeadBobController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] CharacterController controller;
    [SerializeField] Transform bobTarget;

    [Header("Bob Speed")]
    [SerializeField] float walkBobSpeed = 10f;
    [SerializeField] float runBobSpeed = 14f;
    [SerializeField] float sneakBobSpeed = 6f;

    [Header("Bob Amount (local offset)")]
    [SerializeField] Vector3 walkBobAmount = new(0.035f, 0.05f, 0f);
    [SerializeField] Vector3 runBobAmount = new(0.05f, 0.08f, 0f);
    [SerializeField] Vector3 sneakBobAmount = new(0.02f, 0.03f, 0f);

    [Header("Speed Thresholds")]
    [SerializeField] float minSpeed = 0.1f;
    [SerializeField] float runThreshold = 4f;
    [SerializeField] float walkThreshold = 2f;

    [Header("Tuning")]
    [SerializeField, Range(0f, 2f)] float strength = 1f;
    [SerializeField] float resetSpeed = 10f;

    Vector3 defaultLocalPosition;
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

        CacheDefaultPosition();
    }

    void CacheDefaultPosition()
    {
        if (bobTarget == null)
            return;

        defaultLocalPosition = bobTarget.localPosition;
        initialized = true;
    }

    void LateUpdate()
    {
        if (!initialized || bobTarget == null || controller == null)
            return;

        if (strength <= 0f || PlayerControlGate.IsInputBlocked || !controller.isGrounded)
        {
            ResetBob();
            return;
        }

        float speed = new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;
        if (speed < minSpeed)
        {
            ResetBob();
            return;
        }

        float bobSpeed;
        Vector3 bobAmount;
        if (speed > runThreshold)
        {
            bobSpeed = runBobSpeed;
            bobAmount = runBobAmount;
        }
        else if (speed > walkThreshold)
        {
            bobSpeed = walkBobSpeed;
            bobAmount = walkBobAmount;
        }
        else
        {
            bobSpeed = sneakBobSpeed;
            bobAmount = sneakBobAmount;
        }

        bobTimer += Time.deltaTime * bobSpeed;
        float horizontal = Mathf.Cos(bobTimer * 0.5f) * bobAmount.x * strength;
        float vertical = Mathf.Sin(bobTimer) * bobAmount.y * strength;
        bobTarget.localPosition = defaultLocalPosition + new Vector3(horizontal, vertical, 0f);
    }

    void ResetBob()
    {
        bobTimer = 0f;
        bobTarget.localPosition = Vector3.Lerp(
            bobTarget.localPosition,
            defaultLocalPosition,
            Time.deltaTime * resetSpeed);
    }
}
