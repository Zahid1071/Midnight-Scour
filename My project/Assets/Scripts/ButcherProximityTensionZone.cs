using KinoGlitch;
using UnityEngine;

/// <summary>
/// Trigger volume: while the player is inside, whisper tension + camera glitch
/// scale with distance to the butcher (closer = stronger). Keep disabled until DemoEndDirector enables it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ButcherProximityTensionZone : MonoBehaviour
{
    [Header("Distance")]
    [Tooltip("Full intensity when the player is this close to the butcher or closer.")]
    [SerializeField] float minDistance = 3f;
    [Tooltip("No intensity when the player is this far from the butcher or farther.")]
    [SerializeField] float maxDistance = 22f;

    [Header("Effects")]
    [SerializeField] float maxGlitch = 0.05f;
    [Tooltip("Peak whisper volume at closest range (same clip as hiding-spot search tension).")]
    [SerializeField] float maxWhisperIntensity = 1f;

    [Header("Feel")]
    [SerializeField] float intensitySmoothing = 5f;

    Collider _trigger;
    Transform _player;
    Transform _butcher;
    bool _active;
    bool _playerInside;
    float _currentIntensity;

    void Awake()
    {
        _trigger = GetComponent<Collider>();
        _trigger.isTrigger = true;
        SetTriggerEnabled(false);
    }

    void OnDestroy() => ClearEffects();

    public void Activate(Transform butcher)
    {
        _butcher = butcher;
        _active = true;
        SetTriggerEnabled(true);
    }

    public void Deactivate()
    {
        _active = false;
        _playerInside = false;
        _currentIntensity = 0f;
        ClearEffects();
        SetTriggerEnabled(false);
    }

    void Update()
    {
        if (!_active)
            return;

        float target = 0f;
        if (_playerInside && _butcher != null && TryGetPlayer(out Transform player))
        {
            float dist = Vector3.Distance(player.position, _butcher.position);
            target = 1f - Mathf.InverseLerp(minDistance, maxDistance, dist);
        }

        _currentIntensity = Mathf.MoveTowards(
            _currentIntensity,
            target,
            Time.deltaTime * intensitySmoothing);

        ApplyEffects(_currentIntensity);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_active || !other.CompareTag("Player"))
            return;

        _playerInside = true;
        _player = other.transform;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        _playerInside = false;
    }

    bool TryGetPlayer(out Transform player)
    {
        if (_player != null)
        {
            player = _player;
            return true;
        }

        var go = GameObject.FindWithTag("Player");
        if (go == null)
        {
            player = null;
            return false;
        }

        _player = go.transform;
        player = _player;
        return true;
    }

    void ApplyEffects(float intensity01)
    {
        ApplyGlitch(intensity01 * maxGlitch);
        SoundManager.Instance?.SetSearchTension(intensity01 * maxWhisperIntensity);
    }

    void ClearEffects()
    {
        ApplyGlitch(0f);
        SoundManager.Instance?.StopSearchTension();
    }

    static void ApplyGlitch(float value)
    {
        var glitch = Camera.main != null ? Camera.main.GetComponent<AnalogGlitchController>() : null;
        if (glitch == null)
            return;

        glitch.ScanLineJitter = value;
        glitch.HorizontalShake = value;
        glitch.ColorDrift = value;
        glitch.HorizontalRipple = value;
        glitch.VerticalJump = 0f;
    }

    void SetTriggerEnabled(bool enabled)
    {
        if (_trigger != null)
            _trigger.enabled = enabled;
    }
}
