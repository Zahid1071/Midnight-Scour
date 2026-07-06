using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fades in the demo thank-you panel, ducks music/SFX (keeps ambience), then after a delay
/// fades in the exit hint. Any key loads the main menu.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class DemoEndScreenUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private CanvasGroup exitHintGroup;

    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float exitHintDelay = 20f;
    [SerializeField] private float exitHintFadeDuration = 0.45f;
    [SerializeField] private float audioFadeDuration = 1.5f;

    bool _showing;
    bool _exitReady;

    public static DemoEndScreenUI Instance { get; private set; }
    public bool IsShowing => _showing;
    public bool IsExitReady => _exitReady;

    void Awake()
    {
        if (panelGroup == null)
            panelGroup = GetComponent<CanvasGroup>();

        if (!_showing)
            PrepareHiddenState();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void BeginShow()
    {
        Instance = this;
        _showing = true;
        _exitReady = false;

        PlayerControlGate.Release(PlayerControlGate.ReasonCaptured);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResetVisuals();
    }

    public IEnumerator WaitForExitRoutine()
    {
        BeginShow();

        SoundManager.Instance?.FadeOutMusicAndSfx(audioFadeDuration);

        yield return Fade(panelGroup, 0f, 1f, fadeInDuration);

        float elapsed = 0f;
        while (elapsed < exitHintDelay)
        {
            if (!PauseManager.IsPaused)
                elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (exitHintGroup != null)
            yield return Fade(exitHintGroup, 0f, 1f, exitHintFadeDuration);

        _exitReady = true;

        while (true)
        {
            if (WasAnyInputPressedThisFrame())
                break;

            yield return null;
        }
    }

    void PrepareHiddenState()
    {
        ResetVisuals();

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void ResetVisuals()
    {
        ConfigureNonBlocking(panelGroup);

        if (panelGroup != null)
            panelGroup.alpha = 0f;

        if (exitHintGroup != null)
        {
            ConfigureNonBlocking(exitHintGroup);
            exitHintGroup.alpha = 0f;
        }
    }

    IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!PauseManager.IsPaused)
                elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        group.alpha = to;
    }

    static void ConfigureNonBlocking(CanvasGroup group)
    {
        if (group == null)
            return;

        group.interactable = false;
        group.blocksRaycasts = false;
    }

    static bool WasAnyInputPressedThisFrame()
    {
        if (Input.anyKeyDown)
            return true;

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;

        if (Mouse.current == null)
            return false;

        return Mouse.current.leftButton.wasPressedThisFrame
            || Mouse.current.rightButton.wasPressedThisFrame
            || Mouse.current.middleButton.wasPressedThisFrame;
    }
}
