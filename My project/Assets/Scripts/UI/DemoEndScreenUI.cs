using System.Collections;
using UnityEngine;

/// <summary>
/// Fades in the demo end panel (scrollable feedback form), ducks music/SFX, unlocks the cursor.
/// Leaves only when something calls <see cref="OnExitClicked"/> (wire your Exit button to it).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class DemoEndScreenUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private DemoEndFeedbackUI feedbackUI;

    [Tooltip("Top-right game logo — shown only while the ending page is up.")]
    [SerializeField] private GameObject logo;

    [Tooltip("Legacy Esc hint — kept hidden. Safe to delete from the prefab.")]
    [SerializeField] private GameObject exitHint;

    [Header("Wishlist")]
    [Tooltip("Steam store / wishlist page. UTM tags help Steamworks traffic show this came from the demo.")]
    [SerializeField] private string wishlistUrl =
        "https://store.steampowered.com/app/4725700/Midnight_Scour/?utm_source=demo&utm_medium=game&utm_campaign=demo_end_screen";

    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float audioFadeDuration = 1.5f;

    bool _showing;
    bool _exitRequested;

    public static DemoEndScreenUI Instance { get; private set; }
    public bool IsShowing => _showing;
    public bool IsExitReady => _showing;

    void Awake()
    {
        if (panelGroup == null)
            panelGroup = GetComponent<CanvasGroup>();

        if (feedbackUI == null)
            feedbackUI = GetComponent<DemoEndFeedbackUI>()
                ?? GetComponentInChildren<DemoEndFeedbackUI>(true);

        if (feedbackUI == null)
            Debug.LogWarning("[DemoEndScreenUI] No DemoEndFeedbackUI found on Demo End Panel.");

        ResolveLogo();

        if (exitHint == null)
        {
            var t = transform.Find("Press any key to close");
            if (t != null)
                exitHint = t.gameObject;
        }

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
        _exitRequested = false;

        PlayerControlGate.Release(PlayerControlGate.ReasonCaptured);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResetVisuals();
        SetLogoVisible(true);
        feedbackUI?.PrepareForShow();
        ConfigureInteractive(panelGroup);

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>
    /// Wire your Exit button OnClick to this.
    /// </summary>
    public void OnExitClicked()
    {
        if (!_showing)
            return;

        _exitRequested = true;
    }

    /// <summary>
    /// Wire your Wishlist Now button OnClick to this.
    /// </summary>
    public void OnWishlistClicked()
    {
        if (string.IsNullOrWhiteSpace(wishlistUrl))
        {
            Debug.LogWarning("[DemoEndScreenUI] wishlistUrl is empty.");
            return;
        }

        Application.OpenURL(wishlistUrl.Trim());
    }

    public IEnumerator WaitForExitRoutine()
    {
        BeginShow();

        SoundManager.Instance?.FadeOutMusicAndSfx(audioFadeDuration);

        yield return Fade(panelGroup, 0f, 1f, fadeInDuration);

        while (!_exitRequested)
            yield return null;

        _showing = false;
        SetLogoVisible(false);
    }

    void PrepareHiddenState()
    {
        ResetVisuals();
        SetLogoVisible(false);

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void ResetVisuals()
    {
        ConfigureNonBlocking(panelGroup);

        if (panelGroup != null)
            panelGroup.alpha = 0f;

        if (exitHint != null)
            exitHint.SetActive(false);
    }

    void ResolveLogo()
    {
        if (logo != null)
            return;

        // Logo lives on the Canvas next to Demo End Panel (not as a child).
        var parent = transform.parent;
        if (parent != null)
        {
            var t = parent.Find("Logo");
            if (t != null)
            {
                logo = t.gameObject;
                return;
            }
        }

        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t != null && t.name == "Logo" && t.gameObject.scene == gameObject.scene)
            {
                logo = t.gameObject;
                return;
            }
        }
    }

    void SetLogoVisible(bool visible)
    {
        ResolveLogo();
        if (logo != null)
            logo.SetActive(visible);
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

    static void ConfigureInteractive(CanvasGroup group)
    {
        if (group == null)
            return;

        group.interactable = true;
        group.blocksRaycasts = true;
    }
}
