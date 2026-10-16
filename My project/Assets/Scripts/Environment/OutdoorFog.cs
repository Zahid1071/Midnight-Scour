using UnityEngine;

/// <summary>
/// Fades exponential fog in when the main camera climbs out of the basement and out again when it goes back down.
/// Keep scene fog enabled (Exponential) in Lighting settings: builds strip the fog shader variants otherwise.
/// </summary>
[DisallowMultipleComponent]
public class OutdoorFog : MonoBehaviour
{
    [SerializeField] Color fogColor = new Color(0.035f, 0.045f, 0.075f);
    [SerializeField, Min(0f)] float outdoorDensity = 0.03f;

    [Tooltip("Camera height (world Y) at or below which there is no fog. Basement eye height is about -1.4.")]
    [SerializeField] float indoorEyeY = -1f;
    [Tooltip("Camera height (world Y) at or above which fog is at full density.")]
    [SerializeField] float outdoorEyeY = 0.3f;

    /// <summary>Multiplies the outdoor density; shots thin the fog with it to show something far off.</summary>
    public static float DensityScale { get; set; } = 1f;

    Camera _camera;
    float _startDensity;

    void OnEnable()
    {
        _startDensity = RenderSettings.fogDensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = fogColor;
        Apply();
    }

    void OnDisable()
    {
        RenderSettings.fogDensity = _startDensity;
    }

    void LateUpdate() => Apply();

    void Apply()
    {
        if (!_camera || !_camera.isActiveAndEnabled)
            _camera = Camera.main;
        if (!_camera)
            return;

        float outside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(indoorEyeY, outdoorEyeY, _camera.transform.position.y));
        RenderSettings.fogDensity = outdoorDensity * outside * DensityScale;
    }
}
