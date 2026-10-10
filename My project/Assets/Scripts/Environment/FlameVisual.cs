using UnityEngine;

/// <summary>
/// Drives the procedural flame (Midnight Scour/Flame) shared by the lighter and candle fires: each flame gets its
/// own flicker seed, and it leans away from movement on a small spring (and stretches when carried downwards,
/// squashes when carried up).
/// </summary>
[DisallowMultipleComponent]
public sealed class FlameVisual : MonoBehaviour
{
    [Tooltip("Renderers using the flame shader. Empty = every mesh renderer underneath.")]
    [SerializeField] private Renderer[] renderers;

    [Tooltip("Brightness multiplier for the flame and its glow.")]
    [Range(0f, 3f)]
    [SerializeField] private float intensity = 1f;

    [Header("Lean")]
    [Tooltip("Tip offset per m/s of movement, as a fraction of the flame height.")]
    [Min(0f)]
    [SerializeField] private float leanPerSpeed = 0.45f;

    [Range(0f, 1.5f)]
    [SerializeField] private float maxLean = 0.8f;

    [Tooltip("Spring pulling the lean toward its target. Lower = lazier flame.")]
    [Min(0f)]
    [SerializeField] private float stiffness = 60f;

    [Tooltip("How quickly the lean's swing dies out. Lower = more overshoot.")]
    [Min(0f)]
    [SerializeField] private float damping = 6f;

    [Tooltip("Height change per m/s of vertical movement.")]
    [Min(0f)]
    [SerializeField] private float stretchPerSpeed = 0.25f;

    private const float TeleportSpeed = 25f;

    private static readonly int LeanId = Shader.PropertyToID("_Lean");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private MaterialPropertyBlock _block;
    private Vector3 _lastPosition;
    private bool _hasLastPosition;
    private Vector3 _lean;
    private Vector3 _leanVelocity;
    private float _stretch;
    private float _seed;

    public float Intensity
    {
        get => intensity;
        set => intensity = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<MeshRenderer>(true);
        _block = new MaterialPropertyBlock();
        _seed = Random.value * 100f;
    }

    private void OnEnable()
    {
        _hasLastPosition = false;
        _lean = Vector3.zero;
        _leanVelocity = Vector3.zero;
        _stretch = 0f;
        Apply();
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        Vector3 position = transform.position;
        Vector3 velocity = _hasLastPosition ? (position - _lastPosition) / dt : Vector3.zero;
        _lastPosition = position;
        _hasLastPosition = true;
        // The held flame snaps onto the lighter's socket when it is shown; that jump isn't movement.
        if (velocity.sqrMagnitude > TeleportSpeed * TeleportSpeed)
            velocity = Vector3.zero;

        Vector3 target = Vector3.ClampMagnitude(new Vector3(-velocity.x, 0f, -velocity.z) * leanPerSpeed, maxLean);
        _leanVelocity += (target - _lean) * (stiffness * dt);
        _leanVelocity *= Mathf.Exp(-damping * dt);
        _lean += _leanVelocity * dt;

        float targetStretch = Mathf.Clamp(-velocity.y * stretchPerSpeed, -0.4f, 0.6f);
        _stretch = Mathf.Lerp(_stretch, targetStretch, 1f - Mathf.Exp(-10f * dt));
        Apply();
    }

    private void Apply()
    {
        if (renderers == null || _block == null)
            return;
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;
            r.GetPropertyBlock(_block);
            _block.SetVector(LeanId, new Vector4(_lean.x, _lean.y, _lean.z, _stretch));
            _block.SetFloat(SeedId, _seed);
            _block.SetFloat(IntensityId, intensity);
            r.SetPropertyBlock(_block);
        }
    }
}
