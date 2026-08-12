using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Minimal fullscreen read overlay — locks player input while open. Any key or mouse button closes it.</summary>
[DefaultExecutionOrder(-50)]
public class ReadPanelUI : MonoBehaviour
{
    public static ReadPanelUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI bodyText;

    int _openedOnFrame = -1;
    bool _lockHeld;
    bool _releaseLockAfterUpdates;
    Action _onClosed;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    /// <summary>True while the note is open or for the rest of the frame after it closes (prevents same-frame LMB reopen).</summary>
    public static bool IsBlockingPlayerInput => Instance != null && Instance._lockHeld;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        CloseImmediate();
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
        if (!IsOpen)
            return;

        // Ignore the interact click that opened the note on the same frame.
        if (Time.frameCount <= _openedOnFrame)
            return;

        if (WasAnyInputPressedThisFrame())
            Close();
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

    public void Show(string text, Action onClosed = null)
    {
        if (panelRoot == null || bodyText == null || string.IsNullOrWhiteSpace(text))
            return;

        _releaseLockAfterUpdates = false;
        _onClosed = onClosed;

        HudManager.Instance?.ClearSubtitle();
        UIFont.SetText(bodyText, text);
        _openedOnFrame = Time.frameCount;
        panelRoot.SetActive(true);

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

        if (bodyText != null)
            bodyText.text = string.Empty;

        if (panelRoot != null)
            panelRoot.SetActive(false);

        // Keep the gate held until LateUpdate so other Update() scripts cannot interact this frame.
        // onClosed runs after the reading lock is released (see LateUpdate).
        if (_lockHeld)
            _releaseLockAfterUpdates = true;
    }

    void CloseImmediate()
    {
        _releaseLockAfterUpdates = false;
        _onClosed = null;

        if (bodyText != null)
            bodyText.text = string.Empty;

        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (_lockHeld)
        {
            PlayerControlGate.Release(PlayerControlGate.ReasonReading);
            _lockHeld = false;
        }
    }

    static bool WasAnyInputPressedThisFrame()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;

        if (Mouse.current == null)
            return false;

        return Mouse.current.leftButton.wasPressedThisFrame
            || Mouse.current.rightButton.wasPressedThisFrame
            || Mouse.current.middleButton.wasPressedThisFrame;
    }
}
