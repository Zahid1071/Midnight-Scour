using UnityEngine;

[DisallowMultipleComponent]
public sealed class LighterFlame : MonoBehaviour
{
    [Header("Light")]
    [SerializeField] private Light flameLight;

    [Header("Intensity")]
    [Min(0f)]
    [SerializeField] private float baseIntensity = 2f;

    [Range(0f, 1f)]
    [SerializeField] private float intensityVariation = 0.2f;

    [Header("Color (Kelvin)")]
    [SerializeField] private float dimTemperature = 1700f;
    [SerializeField] private float brightTemperature = 2100f;

    [Header("Motion")]
    [Min(0f)]
    [SerializeField] private float speed = 1.5f;

    [Min(0f)]
    [SerializeField] private float verticalWobble = 0.025f;

    [Min(0f)]
    [SerializeField] private float horizontalWobble = 0.01f;

    private Vector3 _restPos;
    private float _seed;

    private void Awake()
    {
        if (flameLight == null)
            flameLight = GetComponentInChildren<Light>();
        if (flameLight == null)
            return;

        _restPos = flameLight.transform.localPosition;
        flameLight.useColorTemperature = true;
        _seed = Random.value * 100f;
    }

    private void OnDisable()
    {
        if (flameLight != null)
            flameLight.transform.localPosition = _restPos;
    }

    private void Update()
    {
        if (flameLight == null)
            return;

        float t = Time.time * speed;
        float slow = Mathf.PerlinNoise(_seed, t);
        float shimmer = Mathf.PerlinNoise(_seed + 50f, t * 3.7f);
        float n = Mathf.Clamp01(slow * 0.7f + shimmer * 0.3f);

        flameLight.intensity = baseIntensity * (1f + (n - 0.5f) * 2f * intensityVariation);
        flameLight.colorTemperature = Mathf.Lerp(dimTemperature, brightTemperature, n);

        flameLight.transform.localPosition = _restPos + new Vector3(
            (Mathf.PerlinNoise(_seed + 10f, t) - 0.5f) * 2f * horizontalWobble,
            (Mathf.PerlinNoise(_seed + 20f, t * 1.3f) - 0.5f) * 2f * verticalWobble,
            (Mathf.PerlinNoise(_seed + 30f, t) - 0.5f) * 2f * horizontalWobble);
    }
}
