using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The hand-drawn farm map. Once the player has picked it up, M opens it full screen with player input locked
/// (like a letter); any key or mouse button puts it away.
/// </summary>
[DefaultExecutionOrder(-50)]
public class FarmMapView : MonoBehaviour
{
    /// World XZ area (x, z) drawn on the map image; must match the bounds in Tools/draw_farm_map.py.
    public static readonly Rect WorldBounds = Rect.MinMaxRect(-120f, -14f, 112f, 154f);

    public static FarmMapView Instance { get; private set; }

    [SerializeField] GameObject panelRoot;
    [SerializeField] AudioClip openSFX;

    public bool HasMap { get; private set; }
    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    /// <summary>True while the map is open or for the rest of the frame after it closes.</summary>
    public static bool IsBlockingPlayerInput => Instance != null && Instance._lockHeld;

    int _openedOnFrame = -1;
    bool _lockHeld;
    bool _releaseLockAfterUpdates;
    Action _onClosed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (_lockHeld)
            PlayerControlGate.Release(PlayerControlGate.ReasonReading);

        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (IsOpen)
        {
            // Ignore the key or click that opened it.
            if (Time.frameCount > _openedOnFrame && WasAnyInputPressedThisFrame())
                Close();
            return;
        }

        if (HasMap && !PlayerControlGate.IsInputBlocked && Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            Open();
    }

    void LateUpdate()
    {
        if (!_releaseLockAfterUpdates)
            return;

        _releaseLockAfterUpdates = false;
        if (!_lockHeld)
            return;

        PlayerControlGate.Release(PlayerControlGate.ReasonReading);
        _lockHeld = false;

        var cb = _onClosed;
        _onClosed = null;
        cb?.Invoke();
    }

    /// <summary>The player found the map: keep it and show it straight away.</summary>
    public void Collect(string closedSubtitleKey)
    {
        HasMap = true;
        Open(() =>
        {
            if (!string.IsNullOrWhiteSpace(closedSubtitleKey))
                HudManager.Instance?.ShowSubtitle(Loc.Get(closedSubtitleKey), 5f);
        });
    }

    /// <summary>Checkpoint load: the map was already found, so its pickup is gone.</summary>
    public void RestoreCollected()
    {
        HasMap = true;
        foreach (var pickup in FindObjectsByType<FarmMapPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            pickup.gameObject.SetActive(false);
    }

    public void Open(Action onClosed = null)
    {
        if (panelRoot == null || IsOpen)
            return;

        _releaseLockAfterUpdates = false;
        _onClosed = onClosed;
        HudManager.Instance?.ClearSubtitle();
        UIFont.ApplyTree(panelRoot.transform);
        _openedOnFrame = Time.frameCount;
        panelRoot.SetActive(true);
        if (openSFX != null)
            SoundManager.Instance?.PlayUISound(openSFX);

        if (!_lockHeld)
        {
            PlayerControlGate.Acquire(PlayerControlGate.ReasonReading);
            _lockHeld = true;
        }
    }

    public void Close()
    {
        if (!IsOpen && !_lockHeld)
            return;

        if (panelRoot != null)
            panelRoot.SetActive(false);

        // Keep the gate held until LateUpdate so the closing key cannot act in the world this frame.
        if (_lockHeld)
            _releaseLockAfterUpdates = true;
    }

    /// <summary>Map position (0..1, origin bottom-left) of a world point.</summary>
    public static Vector2 ToMap(Vector3 world) =>
        new Vector2(Mathf.InverseLerp(WorldBounds.xMin, WorldBounds.xMax, world.x), Mathf.InverseLerp(WorldBounds.yMin, WorldBounds.yMax, world.z));

    static bool WasAnyInputPressedThisFrame()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;

        return Mouse.current != null
            && (Mouse.current.leftButton.wasPressedThisFrame
                || Mouse.current.rightButton.wasPressedThisFrame
                || Mouse.current.middleButton.wasPressedThisFrame);
    }
}
