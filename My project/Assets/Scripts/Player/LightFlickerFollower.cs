using UnityEngine;

/// <summary>
/// Mirrors another light's flicker (intensity, colour temperature) at a fixed scale. Used for the
/// viewmodel-only copy of the lighter flame so the arms flicker in sync with the world light.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class LightFlickerFollower : MonoBehaviour
{
    [SerializeField] private Light source;

    [Tooltip("This light's intensity = source intensity × scale.")]
    [Min(0f)]
    [SerializeField] private float intensityScale = 0.01f;

    [Tooltip("Also copy colour / colour temperature. Off keeps this light's own colour, e.g. a less " +
             "saturated flame tint for skin seen up close.")]
    [SerializeField] private bool copyColor = true;

    private Light _light;

    private void Awake()
    {
        _light = GetComponent<Light>();
    }

    private void LateUpdate()
    {
        if (source == null)
            return;

        _light.intensity = source.intensity * intensityScale;
        if (!copyColor)
            return;
        _light.useColorTemperature = source.useColorTemperature;
        _light.colorTemperature = source.colorTemperature;
        _light.color = source.color;
    }
}
