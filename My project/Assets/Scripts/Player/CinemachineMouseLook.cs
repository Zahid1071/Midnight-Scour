using UnityEngine;
using UnityEngine.InputSystem;

public class CinemachineMouseLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerBody; // Assign PlayerCapsule

    [Header("Settings")]
    [Tooltip("Base sensitivity multiplied by GameSettingsManager.MouseSensitivityMultiplier")]
    [SerializeField] private float baseSensitivity = 0.1f;

    [Tooltip("Fallback effective sensitivity used when GameSettingsManager.Instance is null " +
             "(e.g. when the gameplay scene is launched directly from the editor without going through Main Menu).")]
    [SerializeField] private float fallbackSensitivity = 0.1f;

    [Tooltip("Pitch clamp in degrees")]
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch =  80f;

    private float pitch = 24f;
    private float _cachedMultiplier;

    /// <summary>
    /// Effective per-frame mouse-input multiplier. Reads from GameSettingsManager
    /// each frame so any live changes (e.g. while paused in the settings menu)
    /// take effect immediately the moment the slider moves.
    /// </summary>
    public float CurrentSensitivity
    {
        get
        {
            if (GameSettingsManager.Instance != null)
                return baseSensitivity * GameSettingsManager.Instance.MouseSensitivityMultiplier;
            return fallbackSensitivity;
        }
    }

    private void OnEnable()
    {
        // Cache an initial value and stay in sync with live changes.
        _cachedMultiplier = (GameSettingsManager.Instance != null)
            ? GameSettingsManager.Instance.MouseSensitivityMultiplier
            : 1f;

        GameSettingsManager.OnMouseSensitivityChanged += HandleSensitivityChanged;
    }

    private void OnDisable()
    {
        GameSettingsManager.OnMouseSensitivityChanged -= HandleSensitivityChanged;
    }

    private void HandleSensitivityChanged(float normalized)
    {
        // GameSettingsManager exposes both raw [0,1] and the camera multiplier;
        // recompute the multiplier from the latest manager state.
        if (GameSettingsManager.Instance != null)
            _cachedMultiplier = GameSettingsManager.Instance.MouseSensitivityMultiplier;
    }

    private void Update()
    {
        if (!Application.isFocused) return;
        if (Mouse.current == null) return;

        // Don't process mouse look when gameplay input is locked (pause, cinematic, capture, etc.)
        if (PlayerControlGate.IsInputBlocked) return;

        float sensitivity = CurrentSensitivity;
        bool invertY = GameSettingsManager.Instance != null && GameSettingsManager.Instance.InvertYAxis;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * sensitivity;

        // Horizontal look — rotate the player (yaw)
        if (playerBody != null)
        {
            playerBody.Rotate(Vector3.up * mouseDelta.x);
        }

        // Vertical look — rotate this camera root (pitch)
        float yInput = invertY ? mouseDelta.y : -mouseDelta.y;
        pitch = Mathf.Clamp(pitch + yInput, minPitch, maxPitch);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

}
