using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;

/// <summary>
/// Fades the controls panel in, holds, then fades out. Dismiss early with any key or mouse button
/// after <see cref="dismissDelay"/> seconds.
/// Wire Timeline signal to <see cref="Show"/> (or <see cref="CinematicPlayerControlLock.ShowControlsHint"/>).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class ControlsPanelHint : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeInDuration = 0.35f;
    [SerializeField] private float displayDuration = 7.5f;
    [SerializeField] private float fadeOutDuration = 0.45f;
    [SerializeField] private float dismissDelay = 5f;

    [Header("Hide during hint")]
    [Tooltip("Optional UI element hidden while the controls hint is visible, then faded back in afterward.")]
    [SerializeField] private Transform hideDuringHint;
    [SerializeField] private CanvasGroup hideDuringHintGroup;
    [SerializeField] private float companionFadeOutDuration = 0.35f;
    [SerializeField] private float companionFadeInDuration = 0.5f;

    [Header("Key badges")]
    [SerializeField] private TextMeshProUGUI interactKeyLabel;
    [SerializeField] private TextMeshProUGUI dropKeyLabel;
    [SerializeField] private TextMeshProUGUI crouchKeyLabel;
    [SerializeField] private TextMeshProUGUI runKeyLabel;

    Coroutine _routine;
    float _companionRestAlpha = 1f;
    bool _companionHiddenWithSetActive;
    float _showElapsed;

    public bool IsShowing { get; private set; }

    void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        ResolveCompanionGroup();
        ResolveKeyLabels();

        if (hideDuringHintGroup != null)
            _companionRestAlpha = hideDuringHintGroup.alpha;

        HideImmediate();
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        StartCoroutine(RefreshKeyLabelsWhenReady());
    }

    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    void OnLocaleChanged(UnityEngine.Localization.Locale _) => RefreshKeyLabels();

    void ResolveKeyLabels()
    {
        if (interactKeyLabel == null)
            interactKeyLabel = FindKeyBadgeLabel("Interact Key");

        if (dropKeyLabel == null)
            dropKeyLabel = FindKeyBadgeLabel("G Key");

        if (crouchKeyLabel == null)
            crouchKeyLabel = FindKeyBadgeLabel("CTRL key");

        if (runKeyLabel == null)
            runKeyLabel = FindKeyBadgeLabel("LShift", "Shift key", "Shift Key", "Shift");
    }

    TextMeshProUGUI FindKeyBadgeLabel(params string[] parentNames)
    {
        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp == null || tmp.transform.parent == null)
                continue;

            string parentName = tmp.transform.parent.name;
            for (int i = 0; i < parentNames.Length; i++)
            {
                if (parentName == parentNames[i])
                    return tmp;
            }
        }

        return null;
    }

    IEnumerator RefreshKeyLabelsWhenReady()
    {
        yield return LocalizationSettings.InitializationOperation;
        RefreshKeyLabels();
    }

    void RefreshKeyLabels()
    {
        // Description labels (Interact / Drop / …) also need the locale font.
        UIFont.ApplyTree(transform);

        if (interactKeyLabel != null)
            UIFont.SetText(interactKeyLabel, Loc.Get("controls.key.interact"));

        if (dropKeyLabel != null)
            UIFont.SetText(dropKeyLabel, Loc.Get("controls.key.drop"));

        if (crouchKeyLabel != null)
            UIFont.SetText(crouchKeyLabel, Loc.Get("controls.key.crouch"));

        if (runKeyLabel != null)
            UIFont.SetText(runKeyLabel, Loc.Get("controls.key.run"));

        foreach (var label in GetComponentsInChildren<LocalizedUILabel>(true))
            label.Refresh();
    }

    void ResolveCompanionGroup()
    {
        if (hideDuringHintGroup != null || hideDuringHint == null)
            return;

        hideDuringHintGroup = hideDuringHint.GetComponent<CanvasGroup>()
            ?? hideDuringHint.GetComponentInChildren<CanvasGroup>(true);
    }

    public void Show()
    {
        if (_routine != null)
            StopCoroutine(_routine);

        _routine = StartCoroutine(ShowRoutine());
    }

    IEnumerator ShowRoutine()
    {
        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);

        RefreshKeyLabels();

        IsShowing = true;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        yield return HideCompanion();

        _showElapsed = 0f;
        bool dismissRequested = false;
        yield return FadeUntilCompleteOrDismiss(canvasGroup, 0f, 1f, fadeInDuration, () => dismissRequested = true);

        if (!dismissRequested)
        {
            float elapsed = 0f;
            while (elapsed < displayDuration)
            {
                _showElapsed += Time.unscaledDeltaTime;
                if (_showElapsed >= dismissDelay && WasAnyInputPressedThisFrame())
                {
                    dismissRequested = true;
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        yield return Fade(canvasGroup, canvasGroup.alpha, 0f, fadeOutDuration);

        HideImmediate();
        yield return ShowCompanion();

        IsShowing = false;
        _routine = null;
    }

    IEnumerator FadeUntilCompleteOrDismiss(CanvasGroup group, float from, float to, float duration, System.Action onDismiss)
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
            _showElapsed += Time.unscaledDeltaTime;
            if (_showElapsed >= dismissDelay && WasAnyInputPressedThisFrame())
            {
                onDismiss?.Invoke();
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        group.alpha = to;
    }

    IEnumerator HideCompanion()
    {
        _companionHiddenWithSetActive = false;

        if (hideDuringHintGroup != null)
        {
            ConfigureCompanionNonBlocking(hideDuringHintGroup);
            yield return Fade(hideDuringHintGroup, hideDuringHintGroup.alpha, 0f, companionFadeOutDuration);
            yield break;
        }

        if (hideDuringHint != null && hideDuringHint.gameObject.activeSelf)
        {
            _companionHiddenWithSetActive = true;
            hideDuringHint.gameObject.SetActive(false);
        }
    }

    IEnumerator ShowCompanion()
    {
        if (hideDuringHintGroup != null)
        {
            if (!hideDuringHintGroup.gameObject.activeInHierarchy && hideDuringHint != null)
                hideDuringHint.gameObject.SetActive(true);

            ConfigureCompanionNonBlocking(hideDuringHintGroup);
            yield return Fade(hideDuringHintGroup, hideDuringHintGroup.alpha, _companionRestAlpha, companionFadeInDuration);
            yield break;
        }

        if (_companionHiddenWithSetActive && hideDuringHint != null)
            hideDuringHint.gameObject.SetActive(true);
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
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        group.alpha = to;
    }

    static void ConfigureCompanionNonBlocking(CanvasGroup group)
    {
        if (group == null)
            return;

        group.interactable = false;
        group.blocksRaycasts = false;
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

    /// <summary>Abort any in-progress show (e.g. jumpscare / capture cinematic).</summary>
    public void ForceHide()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        HideImmediate();
        RestoreCompanionImmediate();
    }

    void HideImmediate()
    {
        IsShowing = false;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    void RestoreCompanionImmediate()
    {
        if (_companionHiddenWithSetActive && hideDuringHint != null)
        {
            hideDuringHint.gameObject.SetActive(true);
            _companionHiddenWithSetActive = false;
        }

        if (hideDuringHintGroup != null)
            hideDuringHintGroup.alpha = _companionRestAlpha;
    }
}
