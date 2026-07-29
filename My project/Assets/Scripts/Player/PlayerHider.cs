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
    private readonly Dictionary<HidingSpot, int> _searchExposureBySpot = new();
    HidingSpot _micCaptureOverrideSpot;
    float _micCaptureOverrideChance = -1f;

    [Header("Visuals")]
    public GameObject visualRoot;

    [Header("Components to Disable")]
    public MonoBehaviour[] scriptsToDisable;

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
                _searchExposureBySpot.Remove(_lastOccupiedSpot);

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

    /// <summary>Called when Search starts in a room while the player is hidden there.</summary>
    public void NotifySearchWhileHidden(Transform roomAnchor)
    {
        if (!isHidden || currentHidingSpot == null || roomAnchor == null)
            return;

        if (currentHidingSpot.searchCheckpointOnly)
            return;

        if (!IsSpotInSearchedRoom(currentHidingSpot, roomAnchor))
            return;

        _searchExposureBySpot.TryGetValue(currentHidingSpot, out int count);
        count++;
        _searchExposureBySpot[currentHidingSpot] = count;

        GameplayHintManager.Instance?.NotifyHideSpotSearchExposure(currentHidingSpot, count);
    }

    public int GetSearchExposureCount(HidingSpot spot)
    {
        if (spot == null)
            return 0;

        _searchExposureBySpot.TryGetValue(spot, out int count);
        return count;
    }

    public float GetSearchExposureMultiplier(HidingSpot spot)
    {
        int count = GetSearchExposureCount(spot);
        if (count <= 2)
            return 1f;

        return Mathf.Pow(2f, count - 2);
    }

    public float GetSearchPickWeight(HidingSpot spot)
    {
        if (spot != null && spot == _micCaptureOverrideSpot && _micCaptureOverrideChance >= 0f)
            return 1000f;

        return GetSearchExposureMultiplier(spot);
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
    /// Mic override (if any) wins for that spot. Otherwise: first two searches never capture;
    /// from the 3rd onward <paramref name="baseChance"/> is scaled by exposure.
    /// Does not consume the mic override — call <see cref="ClearMicCaptureOverride"/> after the roll.
    /// </summary>
    public float GetCaptureChance(HidingSpot spot, float baseChance)
    {
        if (spot != null && spot == _micCaptureOverrideSpot && _micCaptureOverrideChance >= 0f)
            return _micCaptureOverrideChance;

        int count = GetSearchExposureCount(spot);
        if (count <= 2)
            return 0f;

        return Mathf.Min(1f, baseChance * GetSearchExposureMultiplier(spot));
    }

    static bool IsSpotInSearchedRoom(HidingSpot spot, Transform roomAnchor)
    {
        var stops = roomAnchor.GetComponentsInChildren<HidingSpot>(true);
        for (int i = 0; i < stops.Length; i++)
        {
            if (stops[i] == spot)
                return true;
        }

        return false;
    }
}
