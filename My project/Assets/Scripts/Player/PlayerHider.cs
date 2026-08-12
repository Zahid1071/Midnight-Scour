using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHider : MonoBehaviour
{
    public bool isHidden { get; private set; }
    public bool IsHidingEngaged { get; private set; }
    public HidingSpot CurrentHidingSpot => currentHidingSpot;

    private HidingSpot currentHidingSpot;
    private HidingSpot _lastOccupiedSpot;
    /// <summary>Times the butcher finished checking this occupied spot (capture ladder).</summary>
    private readonly Dictionary<HidingSpot, int> _searchExposureBySpot = new();
    /// <summary>Times he searched this room while the player was hidden here (pick bias).</summary>
    private readonly Dictionary<HidingSpot, int> _searchPickBiasBySpot = new();
    HidingSpot _micCaptureOverrideSpot;
    float _micCaptureOverrideChance = -1f;

    CharacterController _cc;
    bool _collisionSuspended;

    [Header("Visuals")]
    public GameObject visualRoot;

    [Header("Components to Disable")]
    public MonoBehaviour[] scriptsToDisable;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!isHidden || currentHidingSpot == null || PlayerControlGate.IsInputBlocked)
            return;

        if (Keyboard.current == null)
            return;

        bool exitHide = Keyboard.current.eKey.wasPressedThisFrame
            || Keyboard.current.spaceKey.wasPressedThisFrame;
        if (exitHide)
            currentHidingSpot.RevealPlayer(gameObject);
    }

    public bool IsHidingIn(HidingSpot spot) => isHidden && currentHidingSpot == spot;

    public void ClearSearchMemory()
    {
        _searchExposureBySpot.Clear();
        _searchPickBiasBySpot.Clear();
        _micCaptureOverrideSpot = null;
        _micCaptureOverrideChance = -1f;
        _lastOccupiedSpot = null;
    }

    public void DisableControlsOnly()
    {
        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = false;
    }

    public void EnableControls()
    {
        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = true;
    }

    public void SuspendCollision()
    {
        if (_cc == null)
            _cc = GetComponent<CharacterController>();
        if (_cc == null)
            return;

        if (_cc.enabled)
            _cc.enabled = false;

        _collisionSuspended = true;
    }

    public void RestoreCollision()
    {
        if (!_collisionSuspended)
            return;

        if (_cc == null)
            _cc = GetComponent<CharacterController>();

        Physics.SyncTransforms();
        if (_cc != null)
            _cc.enabled = true;

        _collisionSuspended = false;
        GetComponent<StarterAssets.FirstPersonController>()?.ResetMovementState();
    }

    /// <summary>Ease XZ only; Y unchanged. Call after SuspendCollision.</summary>
    public IEnumerator SmoothMoveToXZ(float targetX, float targetZ, float duration)
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(targetX, start.y, targetZ);
        duration = Mathf.Max(0.05f, duration);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            Vector3 p = Vector3.Lerp(start, end, t);
            p.y = start.y;
            transform.position = p;
            yield return null;
        }

        transform.position = new Vector3(end.x, start.y, end.z);
    }

    public void SetVisuals(bool isActive)
    {
        if (visualRoot != null) visualRoot.SetActive(isActive);
    }

    public void SetHidingEngaged(bool engaged)
    {
        IsHidingEngaged = engaged;
    }

    public void SetHiddenState(bool hidden, HidingSpot spot)
    {
        if (hidden && spot != null)
        {
            if (_lastOccupiedSpot != null && _lastOccupiedSpot != spot)
            {
                _searchExposureBySpot.Remove(_lastOccupiedSpot);
                _searchPickBiasBySpot.Remove(_lastOccupiedSpot);
            }

            _lastOccupiedSpot = spot;
        }
        else if (!hidden && currentHidingSpot != null)
        {
            _lastOccupiedSpot = currentHidingSpot;
            ClearMicCaptureOverride();
        }

        isHidden = hidden;
        currentHidingSpot = spot;
    }

    /// <summary>
    /// Room Search started while the player is hidden in that room.
    /// Raises pick bias so he is more likely to choose this spot next — does not raise capture odds.
    /// </summary>
    public void NotifySearchWhileHidden(Transform roomAnchor)
    {
        if (!isHidden || currentHidingSpot == null || roomAnchor == null)
            return;

        if (currentHidingSpot.searchCheckpointOnly)
            return;

        if (!IsSpotInSearchedRoom(currentHidingSpot, roomAnchor))
            return;

        IncrementPickBias(currentHidingSpot);
    }

    /// <summary>Called when the butcher finishes checking this occupied hide spot.</summary>
    public void NotifyOccupiedSpotSearched(HidingSpot spot)
    {
        if (!isHidden || spot == null || spot != currentHidingSpot)
            return;
        if (spot.searchCheckpointOnly)
            return;

        // Capture ladder only — pick bias is room-search pressure (same as before).
        IncrementSearchExposure(spot);
    }

    void IncrementSearchExposure(HidingSpot spot)
    {
        _searchExposureBySpot.TryGetValue(spot, out int count);
        count++;
        _searchExposureBySpot[spot] = count;

        GameplayHintManager.Instance?.NotifyHideSpotSearchExposure(spot, count);
    }

    void IncrementPickBias(HidingSpot spot)
    {
        _searchPickBiasBySpot.TryGetValue(spot, out int count);
        count++;
        _searchPickBiasBySpot[spot] = count;
    }

    public int GetSearchExposureCount(HidingSpot spot)
    {
        if (spot == null)
            return 0;

        _searchExposureBySpot.TryGetValue(spot, out int count);
        return count;
    }

    int GetPickBiasCount(HidingSpot spot)
    {
        if (spot == null)
            return 0;

        _searchPickBiasBySpot.TryGetValue(spot, out int count);
        return count;
    }

    /// <summary>
    /// Same pick weighting as before: first two room-searches while hidden stay default (weight 1);
    /// from the 3rd onward weight doubles each time so he strongly favors this spot.
    /// </summary>
    public float GetSearchPickWeight(HidingSpot spot)
    {
        if (spot != null && spot == _micCaptureOverrideSpot && _micCaptureOverrideChance >= 0f)
            return 1000f;

        int count = GetPickBiasCount(spot);
        if (count <= 2)
            return 1f;

        return Mathf.Pow(2f, count - 2);
    }

    /// <summary>
    /// Mic noise while hidden sets a one-shot capture chance for the next search check of that spot.
    /// </summary>
    public void SetMicCaptureOverride(HidingSpot spot, float chance)
    {
        if (spot == null)
            return;

        _micCaptureOverrideSpot = spot;
        _micCaptureOverrideChance = Mathf.Clamp01(chance);
    }

    public void ClearMicCaptureOverride()
    {
        _micCaptureOverrideSpot = null;
        _micCaptureOverrideChance = -1f;
    }

    public bool TryGetMicCaptureOverrideSpot(out HidingSpot spot)
    {
        spot = _micCaptureOverrideSpot;
        return spot != null && _micCaptureOverrideChance >= 0f;
    }

    /// <summary>
    /// Mic override (if any) wins for that spot. Otherwise fixed ladder by times this spot
    /// was actually checked while occupied: 0%, 0%, 25%, 50%, then 100%.
    /// Does not consume the mic override — call <see cref="ClearMicCaptureOverride"/> after the roll.
    /// </summary>
    public float GetCaptureChance(HidingSpot spot, float baseChance)
    {
        if (spot != null && spot == _micCaptureOverrideSpot && _micCaptureOverrideChance >= 0f)
            return _micCaptureOverrideChance;

        int count = GetSearchExposureCount(spot);
        return count switch
        {
            <= 0 => 0f,
            1 => 0f,
            2 => 0f,
            3 => 0.25f,
            4 => 0.5f,
            _ => 1f,
        };
    }

    static bool IsSpotInSearchedRoom(HidingSpot spot, Transform roomAnchor)
    {
        if (spot == null || roomAnchor == null)
            return false;

        RoomAnchor searched = roomAnchor.GetComponent<RoomAnchor>()
            ?? roomAnchor.GetComponentInParent<RoomAnchor>();
        RoomAnchor spotRoom = spot.GetComponentInParent<RoomAnchor>();
        if (searched != null && spotRoom != null)
            return searched == spotRoom;

        var stops = roomAnchor.GetComponentsInChildren<HidingSpot>(true);
        for (int i = 0; i < stops.Length; i++)
        {
            if (stops[i] == spot)
                return true;
        }

        return false;
    }
}
