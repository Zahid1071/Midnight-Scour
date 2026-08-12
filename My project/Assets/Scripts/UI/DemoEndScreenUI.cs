using System.Collections;
using UnityEngine;

/// <summary>
/// Fades in the demo end panel (scrollable feedback form), ducks music/SFX, unlocks the cursor.
/// Leaves only when something calls <see cref="OnExitClicked"/> (wire your Exit button to it).
/// Also intercepts Alt+F4 / window close in builds so players still see this panel once.
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
    Coroutine _forceQuitDemoEndCo;

    const string SeenPrefKey = "demo_end_screen_seen";

    static bool _allowApplicationQuit;
    static bool _pendingAppQuitAfterDemoEnd;
    static bool _quitGuardRegistered;

    public static DemoEndScreenUI Instance { get; private set; }
    public bool IsShowing => _showing;
    public bool IsExitReady => _showing;

    public static bool HasBeenSeen => PlayerPrefs.GetInt(SeenPrefKey, 0) == 1;

    public static void MarkSeen()
    {
        PlayerPrefs.SetInt(SeenPrefKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Clears "seen end screen" + "feedback submitted" prefs so quit/finale can be retested.
    /// </summary>
    public static void ResetDemoProgressPrefs()
    {
        PlayerPrefs.DeleteKey(SeenPrefKey);
        PlayerPrefs.DeleteKey("demo_feedback_has_submitted");
        PlayerPrefs.Save();
        Debug.Log("[DemoEndScreenUI] Cleared demo_end_screen_seen + demo_feedback_has_submitted.");
    }

    /// <summary>
    /// If Alt+F4 requested quit during the panel, finish by quitting the app instead of loading the menu.
    /// </summary>
    public static bool TryFinishWithApplicationQuit()
    {
        if (!_pendingAppQuitAfterDemoEnd)
            return false;

        _pendingAppQuitAfterDemoEnd = false;
        _allowApplicationQuit = true;
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        return true;
    }

#if UNITY_EDITOR
    [ContextMenu("DEV: Reset Demo End / Feedback Prefs")]
    void ContextResetDemoProgressPrefs() => ResetDemoProgressPrefs();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _allowApplicationQuit = false;
        _pendingAppQuitAfterDemoEnd = false;
        _quitGuardRegistered = false;
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterQuitGuard()
    {
        if (_quitGuardRegistered)
            return;

        _quitGuardRegistered = true;
        Application.wantsToQuit += OnWantsToQuit;
    }

    static bool OnWantsToQuit()
    {
        if (_allowApplicationQuit)
            return true;

        // Already saw the end panel this install / after reset — allow normal quit.
        if (HasBeenSeen)
            return true;

        var screen = Instance;
        if (screen == null)
            screen = FindFirstObjectByType<DemoEndScreenUI>(FindObjectsInactive.Include);

        // Main menu / scenes without the panel — don't block quit.
        if (screen == null)
            return true;

        _pendingAppQuitAfterDemoEnd = true;
        screen.BeginForceQuitDemoEndFlow();
        return false;
    }

    void BeginForceQuitDemoEndFlow()
    {
        if (_forceQuitDemoEndCo != null)
            return;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        _forceQuitDemoEndCo = StartCoroutine(ForceQuitDemoEndRoutine());
    }

    IEnumerator ForceQuitDemoEndRoutine()
    {
        // Unpause so fades / UI keep running after Alt+F4.
        if (PauseManager.IsPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            PlayerControlGate.Release(PlayerControlGate.ReasonPause);
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        bool startedHere = !_showing;
        if (startedHere)
            yield return WaitForExitRoutine();
        else
        {
            // Panel already up (pause quit / finale) — wait for Exit; that owner finishes the flow.
            while (!_exitRequested)
                yield return null;
        }

        _forceQuitDemoEndCo = null;

        if (startedHere)
            TryFinishWithApplicationQuit();
    }

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
        SteamStoreLink.EnsureOn(logo);
        SteamStoreLink.EnsureOn(ResolveWishlistBanner());

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
        PlayerControlGate.Acquire(PlayerControlGate.ReasonCinematic);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResetVisuals();
        SetLogoVisible(true);
        SetWishlistBannerVisible(true);
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
        SteamStoreLink.OpenStore();
    }

    public IEnumerator WaitForExitRoutine()
    {
        BeginShow();

        SoundManager.Instance?.FadeOutMusicAndSfx(audioFadeDuration);

        yield return Fade(panelGroup, 0f, 1f, fadeInDuration);

        while (!_exitRequested)
            yield return null;

        MarkSeen();
        _showing = false;
        SetLogoVisible(false);
        SetWishlistBannerVisible(false);
    }

    void PrepareHiddenState()
    {
        ResetVisuals();
        SetLogoVisible(false);
        SetWishlistBannerVisible(false);

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
        SteamStoreLink.EnsureOn(logo);
        if (logo == null)
            return;

        // Always use the prefab layout on the demo end screen (not the enlarged pause layout).
        if (visible)
            Object.FindFirstObjectByType<PauseManager>(FindObjectsInactive.Include)?.RestoreLogoLayout();

        logo.SetActive(visible);
    }

    void SetWishlistBannerVisible(bool visible)
    {
        var banner = ResolveWishlistBanner();
        SteamStoreLink.EnsureOn(banner);
        if (banner != null)
            banner.SetActive(visible);
    }

    GameObject ResolveWishlistBanner()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "Wishlist Banner")
                return all[i].gameObject;
        }

        return null;
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
