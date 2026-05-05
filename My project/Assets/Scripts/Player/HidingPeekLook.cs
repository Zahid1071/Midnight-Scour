using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// While the player is hidden, applies limited yaw/pitch to this transform (the hiding camera rig).
/// Normal <see cref="CinemachineMouseLook"/> stays disabled; this only affects the locker/bed camera.
/// </summary>
[DisallowMultipleComponent]
public class HidingPeekLook : MonoBehaviour
{
    [SerializeField] float sensitivity = 0.06f;
    [SerializeField] float maxYawDegrees = 24f;
    [SerializeField] float maxPitchDegrees = 18f;

    PlayerHider playerHider;
    float yaw;
    float pitch;

    void Awake()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
            playerHider = p.GetComponent<PlayerHider>();
    }

    void Update()
    {
        if (playerHider == null || !playerHider.isHidden)
            return;
        if (transform.parent == null)
            return;
        if (!Application.isFocused)
            return;
        if (PlayerControlGate.IsInputBlocked)
            return;
        if (Mouse.current == null)
            return;

        Vector2 delta = Mouse.current.delta.ReadValue() * sensitivity;

        bool invertY = GameSettingsManager.Instance != null && GameSettingsManager.Instance.InvertYAxis;
        float yInput = invertY ? delta.y : -delta.y;

        yaw += delta.x;
        pitch += yInput;
        yaw = Mathf.Clamp(yaw, -maxYawDegrees, maxYawDegrees);
        pitch = Mathf.Clamp(pitch, -maxPitchDegrees, maxPitchDegrees);

        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    public void ResetPeekAngles()
    {
        yaw = 0f;
        pitch = 0f;
        transform.localRotation = Quaternion.identity;
    }
}
