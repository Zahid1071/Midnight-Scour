using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A big "RUN" at the top centre of the screen while something is chasing the player: it pops in, then keeps pulsing
/// and rocking until Hide(). Builds its own overlay canvas. Show() uses the one in the scene if there is one (so its
/// look can be tweaked there), otherwise makes one with the defaults.
/// Fades away while the game is paused (a tutorial card, the pause menu) or the capture is playing.
/// </summary>
[DisallowMultipleComponent]
public class RunPromptUI : MonoBehaviour
{
    static RunPromptUI s_instance;

    [SerializeField] string textKey = "hint.run";
    [Tooltip("Used until localization is ready, or if the key is missing.")]
    [SerializeField] string fallbackText = "RUN";
    [SerializeField] Color color = new Color(0.86f, 0.08f, 0.06f, 1f);
    [SerializeField] float fontSize = 120f;
    [SerializeField] float characterSpacing = 14f;
    [Tooltip("Down from the top edge, on a 1920x1080 reference.")]
    [SerializeField] float topOffset = 130f;
    [SerializeField] int sortingOrder = 40;

    [Header("Pop in / out")]
    [SerializeField] float popInSeconds = 0.3f;
    [SerializeField] float popOvershoot = 1.35f;
    [SerializeField] float fadeOutSeconds = 0.35f;

    [Header("Loop")]
    [Tooltip("Pulses per second.")]
    [SerializeField] float pulseRate = 1.8f;
    [SerializeField] float pulseScale = 0.12f;
    [Tooltip("Rocks per second.")]
    [SerializeField] float rockRate = 0.9f;
    [SerializeField] float rockDegrees = 6f;
    [Tooltip("Pixels of random shake each frame, on top of the pulse.")]
    [SerializeField] float jitter = 2f;

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _rect;
    TextMeshProUGUI _label;
    bool _shown;
    float _shownFor;
    float _visibility;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_instance = null;

    /// <summary>Retired — RUN overlay is removed from gameplay. Kept as a no-op for any leftover callers.</summary>
    public static void Show() { }

    /// <summary>Retired — RUN overlay is removed from gameplay.</summary>
    public static void Hide()
    {
        if (s_instance != null)
            s_instance.Close();
    }

    static RunPromptUI Instance()
    {
        if (s_instance != null)
            return s_instance;
        s_instance = FindFirstObjectByType<RunPromptUI>(FindObjectsInactive.Include);
        if (s_instance == null)
            s_instance = new GameObject("Run Prompt").AddComponent<RunPromptUI>();
        if (!s_instance.gameObject.activeSelf)
            s_instance.gameObject.SetActive(true);
        return s_instance;
    }

    void Awake()
    {
        if (s_instance == null)
            s_instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void Build()
    {
        if (_canvas != null)
            return;

        _canvas = gameObject.GetComponent<Canvas>();
        if (_canvas == null)
            _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.GetComponent<CanvasGroup>();
        if (_group == null)
            _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var textGo = new GameObject("Run Text", typeof(RectTransform));
        textGo.transform.SetParent(transform, false);
        _rect = (RectTransform)textGo.transform;
        _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 1f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = new Vector2(900f, 220f);
        _rect.anchoredPosition = new Vector2(0f, -topOffset);

        _label = textGo.AddComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.enableWordWrapping = false;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.raycastTarget = false;
        _label.fontSize = fontSize;
        _label.characterSpacing = characterSpacing;
        _label.color = color;

        _canvas.enabled = false;
    }

    void Open()
    {
        Build();
        string text = Loc.Get(textKey);
        if (string.IsNullOrEmpty(text) || text == textKey)
            text = fallbackText;
        UIFont.SetText(_label, text);

        if (!_shown)
        {
            _shown = true;
            _shownFor = 0f;
        }
        _canvas.enabled = true;
    }

    void Close() => _shown = false;

    void Update()
    {
        if (_canvas == null || !_canvas.enabled)
            return;

        bool blocked = Time.timeScale <= 0f || JumpscareManager.CaptureUiActive;
        float target = _shown && !blocked ? 1f : 0f;
        float rate = target > _visibility
            ? 1f / Mathf.Max(0.01f, popInSeconds)
            : 1f / Mathf.Max(0.01f, fadeOutSeconds);
        _visibility = Mathf.MoveTowards(_visibility, target, rate * Time.unscaledDeltaTime);
        _group.alpha = _visibility;

        if (!_shown && _visibility <= 0f)
        {
            _canvas.enabled = false;
            return;
        }
        if (blocked)
            return;

        _shownFor += Time.deltaTime;
        float pop = PopScale(_shown ? Mathf.Clamp01(_shownFor / Mathf.Max(0.01f, popInSeconds)) : 1f);
        float pulse = 1f + pulseScale * Mathf.Abs(Mathf.Sin(_shownFor * Mathf.PI * pulseRate));
        float fade = _shown ? 1f : Mathf.Lerp(0.7f, 1f, _visibility);
        _rect.localScale = Vector3.one * (pop * pulse * fade);
        _rect.localRotation = Quaternion.Euler(0f, 0f, rockDegrees * Mathf.Sin(_shownFor * Mathf.PI * 2f * rockRate));
        _rect.anchoredPosition = new Vector2(0f, -topOffset) + Random.insideUnitCircle * jitter;
    }

    // From nothing up past full size and back, like a punch.
    float PopScale(float t)
    {
        if (t >= 1f)
            return 1f;
        if (t < 0.6f)
            return Mathf.Lerp(0f, popOvershoot, Mathf.SmoothStep(0f, 1f, t / 0.6f));
        return Mathf.Lerp(popOvershoot, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f));
    }
}
