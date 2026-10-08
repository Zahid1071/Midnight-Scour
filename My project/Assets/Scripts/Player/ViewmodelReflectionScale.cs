using UnityEngine;

/// <summary>
/// Drives the viewmodel shader's fake flame reflection (_ViewmodelReflectScale) from this light, so
/// metal on the arms only shines while the flame is lit and flickers with it.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class ViewmodelReflectionScale : MonoBehaviour
{
    private static readonly int ScaleId = Shader.PropertyToID("_ViewmodelReflectScale");

    [Tooltip("Light intensity that gives the material's full Reflection Fill colours.")]
    [Min(0.0001f)]
    [SerializeField] private float referenceIntensity = 0.04f;

    private Light _light;

    private void OnEnable()
    {
        _light = GetComponent<Light>();
        Apply();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(ScaleId, 0f);
    }

    private void LateUpdate()
    {
        Apply();
    }

    private void Apply()
    {
        Shader.SetGlobalFloat(ScaleId, _light.enabled ? _light.intensity / referenceIntensity : 0f);
    }
}
