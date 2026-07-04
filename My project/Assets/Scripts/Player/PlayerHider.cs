using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHider : MonoBehaviour
{
    public bool isHidden { get; private set; }
    public HidingSpot CurrentHidingSpot => currentHidingSpot;

    private HidingSpot currentHidingSpot;
    private HidingSpot _lastOccupiedSpot;
    private readonly Dictionary<HidingSpot, int> _searchExposureBySpot = new();

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

    public float GetSearchPickWeight(HidingSpot spot) => GetSearchExposureMultiplier(spot);

    public float GetCaptureChance(HidingSpot spot, float baseChance) =>
        Mathf.Min(1f, baseChance * GetSearchExposureMultiplier(spot));

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
