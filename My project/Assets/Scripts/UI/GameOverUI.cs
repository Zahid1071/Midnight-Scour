using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Game-over: Restart / Main Menu with no lockpicks;
/// Continue / Main Menu when the player has at least one lockpick in inventory.
/// Continue = in-scene day reset (keeps inventory + puzzle progress; lockpick is kept for the padlock).
/// Once an auto-save <see cref="Checkpoint"/> exists, Continue reloads the scene from it instead.
/// </summary>
[DefaultExecutionOrder(32000)]
public class GameOverUI : MonoBehaviour
{
    public enum Choice
    {
        Restart,
        MainMenu,
        Continue,
        RetryCheckpoint
    }

    public static GameOverUI Instance { get; private set; }

    [SerializeField] string mainMenuSceneName = "Main Menu";
    [SerializeField] string reloadSceneName = "Main Scene";
    [SerializeField] InventoryItem lockpickItem;

    GameObject _root;
    TextMeshProUGUI _reasonText;
    TextMeshProUGUI _primaryLabel;
    bool _showing;
    bool _choiceMade;
    Choice _choice;
    bool _canContinue;
    bool _hasCheckpoint;

    public bool IsShowing => _showing;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public IEnumerator WaitForChoiceRoutine(CaptureReason reason)
    {
        _hasCheckpoint = Checkpoint.Exists;
        _canContinue = HasLockpickForContinue();
        EnsureUi();
        RefreshPrimaryButton();
        Show(reason);
        GameplayHintManager.Instance?.NotifyFirstPlayerDeath();

        _choiceMade = false;
        while (!_choiceMade)
            yield return null;

        Choice chosen = _choice;
        Hide();

        Time.timeScale = 1f;
        AudioListener.pause = false;
        ApplyGameOverCursor();

        if (chosen == Choice.Continue)
        {
            // Must still hold a lockpick (enables Continue). Do not consume it — needed for the padlock.
            if (!HasLockpickForContinue())
                chosen = Choice.Restart;
            else
            {
                CaptureContinue.Apply();
                yield break;
            }
        }

        PlayerControlGate.Clear();
        InventoryManager.ResetForNewGame();
        GameplayHintManager.ResetAllHints();
        CaptureContinue.ClearSpawnCache();
        CaptureContext.Clear();

        if (chosen == Choice.RetryCheckpoint)
        {
            Checkpoint.RequestRestore();
            if (!Checkpoint.RestorePending)
                chosen = Choice.Restart;
        }

        if (chosen == Choice.Restart || chosen == Choice.RetryCheckpoint)
            LoadingScreenController.LoadScene(reloadSceneName);
        else
            LoadingScreenController.LoadScene(mainMenuSceneName);
    }

    InventoryItem ResolveLockpickItem()
    {
        if (lockpickItem != null)
            return lockpickItem;

        return GameplayHintManager.Instance != null
            ? GameplayHintManager.Instance.LockpickItem
            : null;
    }

    bool HasLockpickForContinue()
    {
        InventoryItem item = ResolveLockpickItem();
        return item != null
            && InventoryManager.Instance != null
            && InventoryManager.Instance.HasItem(item);
    }

    void Show(CaptureReason reason)
    {
        _showing = true;
        _choiceMade = false;

        // Keep ReasonCaptured so inventory/interact stay dead on this screen
        // (releasing it let clicks fire "nothing in reach" into Continue).
        if (!PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            PlayerControlGate.Acquire(PlayerControlGate.ReasonCaptured);

        // Hide latch + cinematic hide were owned by different systems; clear the latch and
        // re-assert HUD-off so inventory does not stay dead after this screen.
        GameplayHudVisibility.Instance?.ClearHidingLatch();
        GameplayHudVisibility.Instance?.HideGameplayHud();
        HudManager.Instance?.ClearSubtitle(HudManager.PriorityHigh);

        FindFirstObjectByType<PauseManager>(FindObjectsInactive.Include)?.DismissForGameOver();

        string line = Loc.Get(CaptureContext.LocalizationKey(reason));
        if (string.IsNullOrEmpty(line) || line.StartsWith("gameover."))
            line = CaptureContext.FallbackEnglish(reason);

        if (_reasonText != null)
            UIFont.SetText(_reasonText, line);

        if (_root != null)
            _root.SetActive(true);

        ApplyGameOverCursor();
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    void LateUpdate()
    {
        if (!_showing)
            return;

        // Last word on cursor (DefaultExecutionOrder 32000) — other systems must not re-lock.
        ApplyGameOverCursor();
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    static void ApplyGameOverCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Hide()
    {
        _showing = false;
        if (_root != null)
            _root.SetActive(false);

        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    void OnPrimary()
    {
        _choice = _hasCheckpoint ? Choice.RetryCheckpoint : _canContinue ? Choice.Continue : Choice.Restart;
        _choiceMade = true;
    }

    void OnMainMenu()
    {
        _choice = Choice.MainMenu;
        _choiceMade = true;
    }

    void RefreshPrimaryButton()
    {
        if (_primaryLabel == null)
            return;

        string label = _hasCheckpoint || _canContinue
            ? LocOrFallback("gameover.continue", "Continue")
            : LocOrFallback("gameover.restart", "Restart");
        UIFont.SetText(_primaryLabel, label);
    }

    void EnsureUi()
    {
        if (_root != null)
            return;

        var canvasGo = new GameObject("GameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        _root = new GameObject("GameOverRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.transform.SetParent(canvas.transform, false);
        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        var dim = _root.GetComponent<Image>();
        dim.color = new Color(0.02f, 0.01f, 0.01f, 0.92f);
        dim.raycastTarget = true;

        var titleGo = CreateText(_root.transform, "Title", "GAME OVER", 72, FontStyles.Bold,
            new Color(0.85f, 0.12f, 0.12f, 1f), new Vector2(0.5f, 0.62f), new Vector2(900f, 100f));
        UIFont.ApplyTree(titleGo.transform);

        var reasonGo = CreateText(_root.transform, "Reason", "", 32, FontStyles.Normal,
            new Color(0.92f, 0.9f, 0.88f, 1f), new Vector2(0.5f, 0.48f), new Vector2(1000f, 120f));
        _reasonText = reasonGo.GetComponent<TextMeshProUGUI>();
        _reasonText.alignment = TextAlignmentOptions.Center;
        _reasonText.enableWordWrapping = true;

        var primary = CreateButton(_root.transform, "PrimaryBtn", LocOrFallback("gameover.restart", "Restart"),
            new Vector2(0.5f, 0.32f), new Vector2(-140f, 0f), OnPrimary);
        _primaryLabel = primary.GetComponentInChildren<TextMeshProUGUI>();

        CreateButton(_root.transform, "MenuBtn", LocOrFallback("gameover.main_menu", "Main Menu"),
            new Vector2(0.5f, 0.32f), new Vector2(140f, 0f), OnMainMenu);

        _root.SetActive(false);
    }

    static string LocOrFallback(string key, string fallback)
    {
        string s = Loc.Get(key);
        if (string.IsNullOrEmpty(s) || s == key || s.StartsWith("gameover."))
            return fallback;
        return s;
    }

    static GameObject CreateText(Transform parent, string name, string text, float size, FontStyles style,
        Color color, Vector2 anchor, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }

    static GameObject CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(240f, 56f);

        var img = go.GetComponent<Image>();
        img.color = Color.white;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(0.133f, 0.129f, 0.129f, 1f);
        colors.highlightedColor = new Color(0.82f, 0.325f, 0.373f, 1f);
        colors.selectedColor = new Color(0.82f, 0.325f, 0.373f, 1f);
        colors.pressedColor = new Color(0.55f, 0.18f, 0.22f, 1f);
        colors.fadeDuration = 0.1f;
        btn.colors = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var labelGo = CreateText(go.transform, "Label", label, 26, FontStyles.Bold,
            new Color(0.95f, 0.92f, 0.9f, 1f), new Vector2(0.5f, 0.5f), new Vector2(220f, 50f));
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        UIFont.ApplyTree(go.transform);
        return go;
    }
}
