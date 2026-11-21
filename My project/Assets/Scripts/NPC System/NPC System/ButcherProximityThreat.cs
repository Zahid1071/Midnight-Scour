using KinoGlitch;
using UnityEngine;

/// <summary>
/// On the butcher: distance-based analog glitch + whisper while he is a real threat.
/// Works whether the player is exposed or hiding (same room + distance). Skips only while captive.
/// </summary>
[DisallowMultipleComponent]
public class ButcherProximityThreat : MonoBehaviour
{
    [Header("Distance")]
    [Tooltip("Full intensity at this distance or closer.")]
    [SerializeField] float minDistance = 4f;
    [Tooltip("Effect starts at this distance.")]
    [SerializeField] float maxDistance = 10f;
    [Tooltip("If |playerY - butcherY| exceeds this, treat as different floors and skip the effect.")]
    [SerializeField] float maxFloorYDelta = 1f;

    [Header("When")]
    [Tooltip("Only while this butcher is Natural / Chase / Search / Suspicious / Capture.")]
    [SerializeField] bool requireThreatState = true;
    [Tooltip("Only while butcher and player share the same room (path-based room vote).")]
    [SerializeField] bool requireSameRoom = true;

    [Header("Analog glitch (at full intensity)")]
    [SerializeField] float maxScanLineJitter = 0.05f;
    [SerializeField] float maxHorizontalShake = 0.05f;
    [SerializeField] float maxColorDrift = 0.05f;
    [SerializeField] float maxHorizontalRipple = 0.05f;
    [SerializeField] float maxVerticalJump = 0f;

    [Header("Whisper")]
    [SerializeField] float maxWhisperIntensity = 1f;

    [Header("Feel")]
    [SerializeField] float intensitySmoothing = 5f;

    [Header("Layers")]
    [SerializeField] int captiveLayer = 12;

    NPCController _npc;
    Transform _player;
    float _currentIntensity;
    bool _owningEffects;
    float _nextRoomCheckTime;
    bool _cachedSameRoom;

    void Awake()
    {
        _npc = GetComponent<NPCController>();
    }

    void OnDisable() => ReleaseEffects();
    void OnDestroy() => ReleaseEffects();

    void Update()
    {
        bool jumpscare = IsJumpscareActive();
        float target = 0f;

        if (jumpscare)
            target = 1f; // Full proximity look on the jumpscare cam beat
        else if (TryGetThreatIntensity(out float intensity01))
            target = intensity01;

        _currentIntensity = Mathf.MoveTowards(
            _currentIntensity,
            target,
            Time.deltaTime * intensitySmoothing);

        if (!jumpscare && (_currentIntensity <= 0.001f || !IsPlayerInDangerContext()))
        {
            ReleaseEffects();
            return;
        }

        if (_currentIntensity <= 0.001f)
        {
            ReleaseEffects();
            return;
        }

        ApplyEffects(_currentIntensity);
        _owningEffects = true;
    }

    bool IsJumpscareActive()
    {
        if (_npc != null && _npc.currentState == NPCController.AIState.Capture)
            return true;

        var jm = JumpscareManager.Instance;
        if (jm == null || jm.jumpscareCam == null)
            return false;

        // Cinemachine 3 Priority is a PrioritySettings struct with .Value
        return jm.jumpscareCam.Priority.Value > 0;
    }

    bool TryGetThreatIntensity(out float intensity01)
    {
        intensity01 = 0f;

        if (_npc == null)
            return false;
        if (!IsPlayerInDangerContext())
            return false;
        if (requireThreatState && !IsThreatState(_npc.currentState))
            return false;
        if (!TryGetPlayer(out Transform playerT))
            return false;
        if (requireSameRoom && !AreInSameRoomCached(playerT))
            return false;

        Vector3 playerPos = GetProximityPosition(playerT);
        // Different floors (stairs, upper story) — no proximity glitch/whisper.
        if (Mathf.Abs(playerPos.y - transform.position.y) > maxFloorYDelta)
            return false;

        float dist = Vector3.Distance(playerPos, transform.position);
        intensity01 = 1f - Mathf.InverseLerp(minDistance, maxDistance, dist);
        return intensity01 > 0f;
    }

    /// <summary>
    /// While hidden, measure from the hide spot so distance matches where the butcher is searching.
    /// </summary>
    static Vector3 GetProximityPosition(Transform playerT)
    {
        var hider = playerT.GetComponent<PlayerHider>();
        if (hider != null && hider.isHidden && hider.CurrentHidingSpot != null)
            return hider.CurrentHidingSpot.transform.position;

        return playerT.position;
    }

    bool AreInSameRoomCached(Transform playerT)
    {
        if (Time.time >= _nextRoomCheckTime)
        {
            _nextRoomCheckTime = Time.time + 0.25f;
            _cachedSameRoom = AreInSameRoom(playerT);
        }

        return _cachedSameRoom;
    }

    bool AreInSameRoom(Transform playerT)
    {
        RoomAnchor butcherRoom = _npc != null ? _npc.GetCurrentRoom() : null;
        if (butcherRoom == null)
            return false;

        string home = _npc.interaction != null && _npc.interaction.data != null
            ? _npc.interaction.data.homeID
            : string.Empty;
        if (string.IsNullOrEmpty(home) || NPCAnchorManager.Instance == null)
            return false;

        Vector3 samplePos = GetProximityPosition(playerT);
        var playerAnchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(
            samplePos, home, UnityEngine.AI.NavMesh.AllAreas, isNpcSelf: false);
        if (playerAnchor == null)
            return false;

        var playerRoom = playerAnchor.GetComponent<RoomAnchor>();
        return playerRoom != null && playerRoom == butcherRoom;
    }

    bool IsPlayerInDangerContext()
    {
        if (!TryGetPlayer(out Transform playerT))
            return false;

        // Captive / jumpscare path owns full intensity separately — don't distance-drive while bagged.
        return playerT.gameObject.layer != captiveLayer;
    }

    static bool IsThreatState(NPCController.AIState state)
    {
        return state == NPCController.AIState.Natural
            || state == NPCController.AIState.Chase
            || state == NPCController.AIState.Search
            || state == NPCController.AIState.Suspicious
            || state == NPCController.AIState.Capture;
    }

    bool TryGetPlayer(out Transform playerT)
    {
        if (_player != null)
        {
            playerT = _player;
            return true;
        }

        var go = GameObject.FindWithTag("Player");
        if (go == null)
        {
            playerT = null;
            return false;
        }

        _player = go.transform;
        playerT = _player;
        return true;
    }

    void ApplyEffects(float intensity01)
    {
        intensity01 = Mathf.Clamp01(intensity01);

        var glitch = Camera.main != null ? Camera.main.GetComponent<AnalogGlitchController>() : null;
        if (glitch != null)
        {
            glitch.ScanLineJitter = maxScanLineJitter * intensity01;
            glitch.HorizontalShake = maxHorizontalShake * intensity01;
            glitch.ColorDrift = maxColorDrift * intensity01;
            glitch.HorizontalRipple = maxHorizontalRipple * intensity01;
            glitch.VerticalJump = maxVerticalJump * intensity01;
        }

        SoundManager.Instance?.SetSearchTension(intensity01 * maxWhisperIntensity);
    }

    void ReleaseEffects()
    {
        if (!_owningEffects)
            return;

        _owningEffects = false;

        var glitch = Camera.main != null ? Camera.main.GetComponent<AnalogGlitchController>() : null;
        if (glitch != null)
        {
            glitch.ScanLineJitter = 0f;
            glitch.HorizontalShake = 0f;
            glitch.ColorDrift = 0f;
            glitch.HorizontalRipple = 0f;
            glitch.VerticalJump = 0f;
        }

        SoundManager.Instance?.StopSearchTension();
    }
}
