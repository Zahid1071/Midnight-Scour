/*
 * =============================================================================
 *                          BRIGHTNESS APPLIER
 * =============================================================================
 *
 * PURPOSE:
 * Tiny scene-bound bridge that applies GameSettingsManager.Brightness to the
 * scene's URP Global Volume (ColorAdjustments postExposure).
 *
 * WHY A SEPARATE COMPONENT?
 * GameSettingsManager owns the brightness VALUE (singleton, DontDestroyOnLoad)
 * but the Volume/Profile is scene-specific and cannot persist across scenes.
 * This component lives in each gameplay scene that has a Volume and listens
 * to OnBrightnessChanged to keep postExposure in sync.
 *
 * SETUP:
 *   1) Add this component to your URP Global Volume GameObject
 *      (Main Menu scene & Main Scene each get their own).
 *   2) Either assign 'globalVolume' explicitly in the inspector, or leave it
 *      empty — the component auto-finds a Volume with ColorAdjustments
 *      override at OnEnable.
 *   3) GameSettingsManager handles persistence; nothing else to wire.
 * =============================================================================
 */

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class BrightnessApplier : MonoBehaviour
{
    [Tooltip("URP Global Volume to drive. Leave empty to auto-find one in the scene.")]
    [SerializeField] private Volume globalVolume;

    [Tooltip("If true and the assigned Volume has no ColorAdjustments override, " +
             "one will be added at runtime. Disable to fail loudly instead.")]
    [SerializeField] private bool createColorAdjustmentsIfMissing = true;

    private ColorAdjustments _colorAdjustments;

    private void OnEnable()
    {
        ResolveColorAdjustments();
        ApplyCurrent();
        GameSettingsManager.OnBrightnessChanged += HandleBrightnessChanged;
    }

    private void OnDisable()
    {
        GameSettingsManager.OnBrightnessChanged -= HandleBrightnessChanged;
    }

    private void HandleBrightnessChanged(float _normalized) => ApplyCurrent();

    /// <summary>
    /// Apply the current GameSettingsManager.Brightness value to the resolved
    /// ColorAdjustments override. Safe to call any time.
    /// </summary>
    public void ApplyCurrent()
    {
        if (_colorAdjustments == null) return;

        float exposure = (GameSettingsManager.Instance != null)
            ? GameSettingsManager.Instance.BrightnessExposure
            : Mathf.Lerp(GameSettingsManager.MIN_BRIGHTNESS_EXPOSURE,
                         GameSettingsManager.MAX_BRIGHTNESS_EXPOSURE,
                         GameSettingsManager.DefaultSettings.brightness);

        _colorAdjustments.postExposure.overrideState = true;
        _colorAdjustments.postExposure.value = exposure;
    }

    private void ResolveColorAdjustments()
    {
        if (globalVolume == null)
            globalVolume = AutoFindVolume();

        if (globalVolume == null)
        {
            Debug.LogWarning("[BrightnessApplier] No Volume found in scene; brightness will not apply.");
            _colorAdjustments = null;
            return;
        }

        if (globalVolume.profile == null)
        {
            globalVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Debug.Log("[BrightnessApplier] Created runtime VolumeProfile for brightness.");
        }

        if (!globalVolume.profile.TryGet(out _colorAdjustments))
        {
            if (createColorAdjustmentsIfMissing)
            {
                _colorAdjustments = globalVolume.profile.Add<ColorAdjustments>();
                _colorAdjustments.postExposure.overrideState = true;
                Debug.Log("[BrightnessApplier] Added ColorAdjustments to Volume at runtime.");
            }
            else
            {
                Debug.LogWarning("[BrightnessApplier] Volume has no ColorAdjustments and " +
                                 "createColorAdjustmentsIfMissing is false. Brightness will not apply.");
            }
        }
    }

    /// <summary>
    /// Look for a Volume in the scene that already has a ColorAdjustments override;
    /// fall back to the first Volume found.
    /// </summary>
    private static Volume AutoFindVolume()
    {
        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        if (volumes == null || volumes.Length == 0) return null;

        foreach (var v in volumes)
        {
            if (v != null && v.profile != null && v.profile.TryGet(out ColorAdjustments _))
                return v;
        }
        return volumes[0];
    }
}
