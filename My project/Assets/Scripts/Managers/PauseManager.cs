using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseRoot;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsMenu settingsMenu;

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    [Header("Player Control (optional)")]
    [SerializeField] private MonoBehaviour[] disableOnPause;

    [Header("Cursor")]
    [SerializeField] private CursorLockMode lockModeWhilePlaying = CursorLockMode.Locked;

    public static bool IsPaused { get; private set; }

    GameObject _mainButtons;
    GameObject _promoLogo;
    GameObject _wishlistBanner;
    Vector2 _logoRestSize;
    Vector2 _logoRestPos;
    bool _logoRestCached;
    const float PauseLogoScale = 1.75f;
    const float PauseLogoLeftShift = 80f;

    void OnDestroy()
    {
        if (IsPaused)
            PlayerControlGate.Release(PlayerControlGate.ReasonPause);
    }

    void Awake()
    {
        ResolvePauseUi();
        ResolvePromoUi();
        WireSettingsBackButton();

        if (pauseRoot) pauseRoot.SetActive(false);
        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);

        // Save system removed — hide leftover Save Game button if present in prefab.
        if (pauseRoot != null)
        {
            var saveBtn = pauseRoot.transform.Find("Main Buttons/SaveGame");
            if (saveBtn == null)
            {
                foreach (var t in pauseRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "SaveGame")
                    {
                        saveBtn = t;
                        break;
                    }
                }
            }
            if (saveBtn != null)
                saveBtn.gameObject.SetActive(false);
        }

        Resume_Internal();
    }

    void LateUpdate()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        // Capture / game-over own the session — ESC must not open pause or "Continue" the run.
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            return;

        if (GameOverUI.Instance != null && GameOverUI.Instance.IsShowing)
            return;

        var demoEnd = DemoEndScreenUI.Instance;
        if (demoEnd != null && demoEnd.IsShowing)
            return;

        // The tutorial popup owns the frozen time scale until OK.
        if (TutorialPopupUI.IsShowing)
            return;

        if (IsPaused)
        {
            if (IsSettingsOpen())
                OnCloseSettings();
            else
                OnContinue();
        }
        else
        {
            Pause_Internal();
        }
    }

    void ResolvePauseUi()
    {
        if (pauseRoot == null)
            return;

        var buttons = pauseRoot.transform.Find("Main Buttons");
        if (buttons != null)
            _mainButtons = buttons.gameObject;

        if (settingsPanel == null)
        {
            foreach (var t in pauseRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Settings  Panel" || t.name == "Settings  Panel Pause")
                {
                    settingsPanel = t.gameObject;
                    break;
                }
            }
        }

        if (settingsMenu == null && settingsPanel != null)
            settingsMenu = settingsPanel.GetComponent<SettingsMenu>()
                ?? settingsPanel.GetComponentInChildren<SettingsMenu>(true);
    }

    void ResolvePromoUi()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == null)
                continue;

            if (_promoLogo == null && t.name == "Logo")
                _promoLogo = t.gameObject;
            if (_wishlistBanner == null && t.name == "Wishlist Banner")
                _wishlistBanner = t.gameObject;

            if (_promoLogo != null && _wishlistBanner != null)
                break;
        }

        SteamStoreLink.EnsureOn(_promoLogo);
        SteamStoreLink.EnsureOn(_wishlistBanner);

        CacheLogoRestLayout();
    }

    void CacheLogoRestLayout()
    {
        if (_logoRestCached || _promoLogo == null)
            return;

        var rt = _promoLogo.GetComponent<RectTransform>();
        if (rt == null)
            return;

        _logoRestSize = rt.sizeDelta;
        _logoRestPos = rt.anchoredPosition;
        _logoRestCached = true;
    }

    void SetPromoVisible(bool visible)
    {
        if (_promoLogo == null && _wishlistBanner == null)
            ResolvePromoUi();

        if (_promoLogo != null)
        {
            _promoLogo.SetActive(visible);
            if (visible)
                ApplyPauseLogoLayout();
            else if (!ShouldKeepPromoVisible())
                RestoreLogoLayout();
        }

        if (_wishlistBanner != null)
        {
            _wishlistBanner.SetActive(visible);
            if (visible)
                PrepareWishlistBannerStaticVisible();
        }
    }

    /// <summary>
    /// Banner Enter anim starts off-screen (x=-75) and loops; with timeScale 0 it freezes hidden.
    /// Pause needs a static fully-visible banner at the final slot.
    /// </summary>
    void PrepareWishlistBannerStaticVisible()
    {
        if (_wishlistBanner == null)
            return;

        var anim = _wishlistBanner.GetComponent<Animation>();
        if (anim != null)
        {
            anim.Stop();
            anim.enabled = false;
            anim.playAutomatically = false;
        }

        var rt = _wishlistBanner.GetComponent<RectTransform>();
        if (rt != null)
        {
            // Final key of Banner Enter.anim
            rt.anchoredPosition = new Vector2(75f, rt.anchoredPosition.y);
            rt.localScale = Vector3.one;
        }

        var raw = _wishlistBanner.GetComponent<UnityEngine.UI.RawImage>();
        if (raw != null)
        {
            Color c = raw.color;
            c.a = 1f;
            raw.color = c;
        }

        var img = _wishlistBanner.GetComponent<UnityEngine.UI.Image>();
        if (img != null)
        {
            Color c = img.color;
            c.a = 1f;
            img.color = c;
        }

        var cg = _wishlistBanner.GetComponent<CanvasGroup>();
        if (cg != null)
            cg.alpha = 1f;
    }

    void ApplyPauseLogoLayout()
    {
        CacheLogoRestLayout();
        if (!_logoRestCached || _promoLogo == null)
            return;

        var rt = _promoLogo.GetComponent<RectTransform>();
        if (rt == null)
            return;

        rt.sizeDelta = _logoRestSize * PauseLogoScale;
        // Top-right anchored: more negative X nudges left so the bigger logo isn't clipped.
        rt.anchoredPosition = new Vector2(_logoRestPos.x - PauseLogoLeftShift, _logoRestPos.y);
    }

    /// <summary>Demo end / resume — original prefab size and corner position.</summary>
    public void RestoreLogoLayout()
    {
        if (!_logoRestCached || _promoLogo == null)
            CacheLogoRestLayout();
        if (!_logoRestCached || _promoLogo == null)
            return;

        var rt = _promoLogo.GetComponent<RectTransform>();
        if (rt == null)
            return;

        rt.sizeDelta = _logoRestSize;
        rt.anchoredPosition = _logoRestPos;
    }

    bool ShouldKeepPromoVisible()
    {
        var demoEnd = DemoEndScreenUI.Instance;
        return demoEnd != null && demoEnd.IsShowing;
    }

    void WireSettingsBackButton()
    {
        if (settingsPanel == null)
            return;

        foreach (var button in settingsPanel.GetComponentsInChildren<Button>(true))
        {
            if (button == null || button.gameObject.name != "Back Button")
                continue;

            button.onClick.RemoveListener(OnCloseSettings);
            button.onClick.AddListener(OnCloseSettings);
            break;
        }
    }

    public void OnContinue()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured)
            || (GameOverUI.Instance != null && GameOverUI.Instance.IsShowing))
            return;

        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);
        if (pauseRoot) pauseRoot.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        Resume_Internal();
    }

    /// <summary>
    /// Closes pause UI without restoring gameplay cursor/timeScale — game-over owns those.
    /// </summary>
    public void DismissForGameOver()
    {
        if (!IsPaused && (pauseRoot == null || !pauseRoot.activeSelf))
            return;

        IsPaused = false;
        PlayerControlGate.Release(PlayerControlGate.ReasonPause);

        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);
        if (pauseRoot) pauseRoot.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        SetPromoVisible(false);

        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = false;
    }

    /// <summary>Re-enable pause-disabled gameplay scripts after Continue / leave game-over.</summary>
    public void RestoreGameplayScriptsAfterGameOver()
    {
        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = true;
    }

    public void OnOpenSettings()
    {
        if (settingsPanel == null)
        {
            ResolvePauseUi();
            if (settingsPanel == null) return;
        }

        HudManager.Instance?.ClearSubtitle();

        if (_mainButtons != null)
            _mainButtons.SetActive(false);
        else if (pauseRoot != null
                 && (settingsPanel == null || !settingsPanel.transform.IsChildOf(pauseRoot.transform)))
            pauseRoot.SetActive(false);

        if (pauseRoot != null
            && settingsPanel != null
            && settingsPanel.transform.IsChildOf(pauseRoot.transform)
            && !pauseRoot.activeSelf)
            pauseRoot.SetActive(true);

        settingsPanel.SetActive(true);
        settingsMenu?.BeginEditing();
    }

    public void OnCloseSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons != null)
            _mainButtons.SetActive(true);
        else if (pauseRoot)
            pauseRoot.SetActive(true);

        settingsMenu?.CancelEditing();
    }

    public void OnQuitToMenu()
    {
        StartCoroutine(QuitToMenuRoutine());
    }

    IEnumerator QuitToMenuRoutine()
    {
        // Leave pause so the demo-end fade / UI can run, but keep control locked.
        IsPaused = false;
        PlayerControlGate.Release(PlayerControlGate.ReasonPause);
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (settingsPanel) settingsPanel.SetActive(false);
        if (pauseRoot) pauseRoot.SetActive(false);

        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (!DemoEndScreenUI.HasBeenSeen)
        {
            var demoEnd = DemoEndScreenUI.Instance;
            if (demoEnd == null)
                demoEnd = FindFirstObjectByType<DemoEndScreenUI>(FindObjectsInactive.Include);

            if (demoEnd != null)
            {
                JumpscareManager.Instance?.EndJumpscare();
                yield return demoEnd.WaitForExitRoutine();

                // Alt+F4 during the panel → quit the app instead of loading the menu.
                if (DemoEndScreenUI.TryFinishWithApplicationQuit())
                    yield break;
            }
        }

        // Keep input blocked until the load starts so UI/interact code can't NRE on a cleared inventory.
        InventoryManager.ResetForNewGame();
        GameplayHintManager.ResetAllHints();
        LoadingScreenController.LoadScene(mainMenuSceneName);
        PlayerControlGate.Clear();
    }

    void Pause_Internal()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured)
            || (GameOverUI.Instance != null && GameOverUI.Instance.IsShowing))
            return;

        IsPaused = true;
        PlayerControlGate.Acquire(PlayerControlGate.ReasonPause);
        Time.timeScale = 0f;
        AudioListener.pause = true;

        HudManager.Instance?.ClearSubtitle();

        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);
        if (pauseRoot) pauseRoot.SetActive(true);

        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        SetPromoVisible(true);
    }

    void Resume_Internal()
    {
        IsPaused = false;
        PlayerControlGate.Release(PlayerControlGate.ReasonPause);
        Time.timeScale = 1f;
        AudioListener.pause = false;

        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = true;

        Cursor.visible = false;
        Cursor.lockState = lockModeWhilePlaying;

        if (!ShouldKeepPromoVisible())
            SetPromoVisible(false);
    }

    bool IsSettingsOpen() =>
        settingsPanel != null && settingsPanel.activeSelf && settingsPanel.activeInHierarchy;
}
