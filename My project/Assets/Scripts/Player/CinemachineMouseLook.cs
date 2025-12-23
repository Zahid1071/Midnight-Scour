/*
 * =============================================================================
 *                    CINEMACHINE MOUSE LOOK
 * =============================================================================
 * 
 * PURPOSE:
 * Handles first-person camera look based on mouse input.
 * Reads mouse sensitivity and invert Y settings from GameSettingsManager.
 * 
 * SETUP:
 * ------
 * 1. Attach to the camera root object
 * 2. Assign the player body transform (for yaw rotation)
 * 3. Ensure GameSettingsManager exists in the scene (or as DontDestroyOnLoad)
 * 
 * SETTINGS INTEGRATION:
 * ---------------------
 * This script reads from GameSettingsManager.Instance:
 * - MouseSensitivity: multiplier for mouse input
 * - InvertYAxis: if true, vertical look is inverted
 * 
 * =============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;

public class CinemachineMouseLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerBody; // Assign PlayerCapsule
    
    [Header("Settings")]
    [Tooltip("Base sensitivity multiplied by GameSettingsManager.MouseSensitivity")]
    [SerializeField] private float baseSensitivity = 0.1f;
    
    [Tooltip("Fallback sensitivity if GameSettingsManager is not available")]
    [SerializeField] private float fallbackSensitivity = 1.5f;

    private float pitch = 0f;

    /// <summary>
    /// Current effective sensitivity (base * settings multiplier).
    /// </summary>
    public float CurrentSensitivity
    {
        get
        {
            if (GameSettingsManager.Instance != null)
                return baseSensitivity * GameSettingsManager.Instance.MouseSensitivity;
            return fallbackSensitivity;
        }
    }

    void Update()
    {
        if (!Application.isFocused) return;
        if (Mouse.current == null) return;
        
        // Don't process mouse look when game is paused
        if (PauseMenuController.IsPaused) return;

        // Get settings from GameSettingsManager
        float sensitivity = CurrentSensitivity;
        bool invertY = GameSettingsManager.Instance != null && GameSettingsManager.Instance.InvertYAxis;

        // Read mouse input
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * sensitivity;

        // Horizontal look — rotate the player (yaw)
        if (playerBody != null)
            playerBody.Rotate(Vector3.up * mouseDelta.x);

        // Vertical look — rotate this camera root (pitch)
        float yInput = invertY ? mouseDelta.y : -mouseDelta.y;
        pitch += yInput;
        pitch = Mathf.Clamp(pitch, -80f, 80f);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
