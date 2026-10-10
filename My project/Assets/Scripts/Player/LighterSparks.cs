using UnityEngine;

/// <summary>
/// Flint sparks for the held lighter's strike: a short burst of streaks and a faint flash, moved onto the held
/// view's flame socket each time it plays. Kept outside the flame rig, whose children stay off until the lighter is lit.
/// Simulates in local space so the sparks ride along with the camera like the viewmodel does.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public sealed class LighterSparks : MonoBehaviour
{
    [SerializeField] private string socketName = "Lighter_FlameSocket";
    [Tooltip("World-space offset from the socket (the flint wheel sits just below the wick).")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, -0.004f, 0f);
    [SerializeField] private Vector2Int burstCount = new Vector2Int(6, 11);

    [Header("Flash")]
    [SerializeField] private Light flash;
    [SerializeField] private float flashIntensity = 0.8f;
    [SerializeField] private float flashTime = 0.07f;

    private ParticleSystem _particles;
    private float _flashLeft;

    private void Awake()
    {
        _particles = GetComponent<ParticleSystem>();
        if (flash != null)
            flash.enabled = false;
    }

    public void Play(HeldItemView view)
    {
        Transform socket = view != null ? view.FindSocket(socketName) : null;
        if (socket != null)
            transform.SetPositionAndRotation(socket.position + worldOffset, Quaternion.identity);
        _particles.Emit(Random.Range(burstCount.x, burstCount.y + 1));

        if (flash != null && flashTime > 0f)
        {
            _flashLeft = flashTime;
            flash.intensity = flashIntensity;
            flash.enabled = true;
        }
    }

    private void Update()
    {
        if (flash == null || _flashLeft <= 0f)
            return;
        _flashLeft -= Time.deltaTime;
        float k = Mathf.Clamp01(_flashLeft / flashTime);
        flash.intensity = flashIntensity * k * k;
        if (_flashLeft <= 0f)
            flash.enabled = false;
    }
}
