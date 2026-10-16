using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// A one-time tutorial card: an ink box with a looping clip of the action, a title, an explanation and an OK button.
/// The game freezes while it's up. Shows itself a moment after the player first strikes the lighter, or call Show().
/// Other scripts can put their own card up on it once with ShowOnce.
/// Unity can't play GIFs: convert the GIF to an MP4/WebM (Video Clip), or split it into frames (Frames).
/// </summary>
[DisallowMultipleComponent]
public class TutorialPopupUI : MonoBehaviour
{
    const string LockReason = "tutorial_popup";

    static readonly HashSet<string> s_shown = new HashSet<string>();
    static int s_openCount;
    static TutorialPopupUI s_instance;

    struct Card
    {
        public string id, titleKey, bodyKey;
        public VideoClip clip;
        public Sprite[] frames;
    }

    /// <summary>True while any tutorial popup is up.</summary>
    public static bool IsShowing => s_openCount > 0;

    [Tooltip("Shown once per run under this id.")]
    [SerializeField] string popupId = "Candles";

    [Header("Trigger")]
    [SerializeField] bool showAfterFirstLighterStrike = true;
    [Tooltip("Seconds after the lighter catches before the popup comes up.")]
    [SerializeField] float delayAfterStrike = 2f;

    [Header("Media (one of the two)")]
    [Tooltip("The GIF converted to a video (MP4/WebM). Loops silently.")]
    [SerializeField] VideoClip videoClip;
    [Tooltip("The GIF split into frames, played in order and looped. Used when no Video Clip is set.")]
    [SerializeField] Sprite[] frames;
    [SerializeField] float framesPerSecond = 12f;

    [Header("Text")]
    [SerializeField] string titleKey = "tutorial.candles.title";
    [SerializeField] string bodyKey = "tutorial.candles.body";
    [SerializeField] string okKey = "tutorial.ok";

    [Header("Scene refs")]
    [Tooltip("Toggled on and off; needs a CanvasGroup.")]
    [SerializeField] CanvasGroup panelRoot;
    [Tooltip("Pops in with a little overshoot.")]
    [SerializeField] RectTransform box;
    [SerializeField] RawImage media;
    [SerializeField] AspectRatioFitter mediaAspect;
    [SerializeField] TextMeshProUGUI titleText;
    [SerializeField] TextMeshProUGUI bodyText;
    [SerializeField] Button okButton;
    [SerializeField] TextMeshProUGUI okText;

    [Header("Feel")]
    [SerializeField] float fadeInSeconds = 0.25f;
    [SerializeField] float fadeOutSeconds = 0.18f;
    [SerializeField] float popStartScale = 0.85f;
    [Tooltip("Seconds before OK responds, so a click already in flight can't dismiss it unread.")]
    [SerializeField] float okGuardSeconds = 0.6f;
    [SerializeField] AudioClip openSfx;
    [SerializeField] AudioClip okSfx;
    [SerializeField, Range(0f, 1f)] float sfxVolume = 0.8f;

    bool _open;
    bool _closing;
    bool _pending;
    float _savedTimeScale = 1f;
    VideoPlayer _video;
    RenderTexture _videoTexture;
    Card _card;
    Coroutine _anim;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_shown.Clear();
        s_openCount = 0;
    }

    /// <summary>A new game: every popup may show again.</summary>
    public static void ResetShown() => s_shown.Clear();

    public static IEnumerable<string> ShownIds => s_shown;

    /// <summary>Checkpoint load: popups already seen in the saved run stay seen.</summary>
    public static void MarkShown(string id)
    {
        if (!string.IsNullOrEmpty(id))
            s_shown.Add(id);
    }

    /// <summary>
    /// Puts a card with these texts (and a looping clip, or else a still image) up on the scene's popup, once per run
    /// under id, after delay seconds and once nothing else holds the player (cinematic, note, minigame, pause, another card).
    /// </summary>
    public static bool ShowOnce(string id, string titleKey, string bodyKey, VideoClip clip = null, Sprite image = null,
        float delay = 0f)
    {
        TutorialPopupUI popup = s_instance != null ? s_instance : FindFirstObjectByType<TutorialPopupUI>(FindObjectsInactive.Include);
        if (popup == null || !popup.isActiveAndEnabled || string.IsNullOrEmpty(id) || s_shown.Contains(id))
            return false;
        s_shown.Add(id);
        popup.StartCoroutine(popup.ShowWhenFree(new Card
        {
            id = id,
            titleKey = titleKey,
            bodyKey = bodyKey,
            clip = clip,
            frames = image != null ? new[] { image } : null,
        }, delay));
        return true;
    }

    Card OwnCard => new Card { id = popupId, titleKey = titleKey, bodyKey = bodyKey, clip = videoClip, frames = frames };

    void Awake()
    {
        if (s_instance == null)
            s_instance = this;
        if (okButton != null)
            okButton.onClick.AddListener(Close);
        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        if (showAfterFirstLighterStrike)
            HeldLighter.Struck += OnLighterStruck;
    }

    void OnDisable()
    {
        HeldLighter.Struck -= OnLighterStruck;
        _pending = false;
        if (_open)
            Finish();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        if (_videoTexture != null)
            _videoTexture.Release();
    }

    void OnLighterStruck()
    {
        if (_pending || _open || s_shown.Contains(popupId))
            return;
        _pending = true;
        StartCoroutine(ShowWhenFree(OwnCard, delayAfterStrike));
    }

    IEnumerator ShowWhenFree(Card card, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        // Not over a cinematic, a note, a minigame or the pause menu.
        while (PlayerControlGate.IsInputBlocked || IsShowing)
            yield return null;
        if (card.id == popupId)
            _pending = false;
        Open(card);
    }

    public void Show() => Open(OwnCard);

    void Open(Card card)
    {
        if (_open || panelRoot == null)
            return;
        _card = card;
        s_shown.Add(card.id);
        _open = true;
        _closing = false;
        s_openCount++;

        _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        PlayerControlGate.Acquire(LockReason);
        HudManager.Instance?.ClearSubtitle();
        GameplayHudVisibility.Instance?.HideGameplayHud();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        RefreshText();
        panelRoot.gameObject.SetActive(true);
        StartMedia();
        if (openSfx != null)
            SoundManager.Instance?.PlaySFX(openSfx, sfxVolume);
        if (_anim != null)
            StopCoroutine(_anim);
        _anim = StartCoroutine(OpenRoutine());
    }

    public void Close()
    {
        if (!_open || _closing)
            return;
        _closing = true;
        if (okSfx != null)
            SoundManager.Instance?.PlaySFX(okSfx, sfxVolume);
        if (_anim != null)
            StopCoroutine(_anim);
        _anim = StartCoroutine(CloseRoutine());
    }

    IEnumerator OpenRoutine()
    {
        if (okButton != null)
            okButton.interactable = false;
        for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
        {
            float u = t / fadeInSeconds;
            panelRoot.alpha = u;
            if (box != null)
                box.localScale = Vector3.one * Mathf.LerpUnclamped(popStartScale, 1f, BackOut(u));
            yield return null;
        }
        panelRoot.alpha = 1f;
        if (box != null)
            box.localScale = Vector3.one;

        float guard = okGuardSeconds - fadeInSeconds;
        if (guard > 0f)
            yield return new WaitForSecondsRealtime(guard);
        if (okButton != null)
        {
            okButton.interactable = true;
            EventSystem.current?.SetSelectedGameObject(okButton.gameObject);
        }
    }

    IEnumerator CloseRoutine()
    {
        if (okButton != null)
            okButton.interactable = false;
        float from = panelRoot.alpha;
        for (float t = 0f; t < fadeOutSeconds; t += Time.unscaledDeltaTime)
        {
            panelRoot.alpha = Mathf.Lerp(from, 0f, t / fadeOutSeconds);
            yield return null;
        }
        Finish();
    }

    void Finish()
    {
        if (!_open)
            return;
        _open = false;
        _closing = false;
        s_openCount = Mathf.Max(0, s_openCount - 1);

        StopMedia();
        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.gameObject.SetActive(false);
        }
        EventSystem.current?.SetSelectedGameObject(null);

        Time.timeScale = _savedTimeScale;
        PlayerControlGate.Release(LockReason);
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (!_open || _closing)
            return;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Sprite[] cardFrames = _card.frames;
        if (_card.clip == null && cardFrames != null && cardFrames.Length > 0 && media != null)
        {
            Sprite frame = cardFrames[(int)(Time.unscaledTime * framesPerSecond) % cardFrames.Length];
            if (frame != null)
                media.texture = frame.texture;
            media.uvRect = UvRect(frame);
        }
    }

    void RefreshText()
    {
        SetLabel(titleText, _card.titleKey);
        SetLabel(bodyText, _card.bodyKey);
        SetLabel(okText, okKey);
    }

    static void SetLabel(TextMeshProUGUI label, string key)
    {
        if (label == null || string.IsNullOrEmpty(key))
            return;
        string text = Loc.Get(key);
        if (!string.IsNullOrEmpty(text) && text != key)
            UIFont.SetText(label, text);
        else
            UIFont.Apply(label);
    }

    void StartMedia()
    {
        if (media == null)
            return;
        VideoClip clip = _card.clip;
        Sprite[] cardFrames = _card.frames;
        media.enabled = clip != null || (cardFrames != null && cardFrames.Length > 0);
        if (clip != null)
        {
            int w = Mathf.Max(2, (int)clip.width);
            int h = Mathf.Max(2, (int)clip.height);
            if (_videoTexture == null || _videoTexture.width != w || _videoTexture.height != h)
            {
                if (_videoTexture != null)
                    _videoTexture.Release();
                _videoTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Tutorial Popup Video" };
            }
            if (_video == null)
            {
                _video = media.gameObject.AddComponent<VideoPlayer>();
                _video.playOnAwake = false;
                _video.isLooping = true;
                _video.renderMode = VideoRenderMode.RenderTexture;
                _video.audioOutputMode = VideoAudioOutputMode.None;
                // The game is frozen while this is up.
                _video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            }
            _video.clip = clip;
            _video.targetTexture = _videoTexture;
            media.texture = _videoTexture;
            media.uvRect = new Rect(0f, 0f, 1f, 1f);
            SetAspect(w / (float)h);
            _video.Play();
        }
        else if (cardFrames != null && cardFrames.Length > 0 && cardFrames[0] != null)
        {
            Rect r = cardFrames[0].rect;
            SetAspect(r.width / Mathf.Max(1f, r.height));
        }
    }

    void StopMedia()
    {
        if (_video != null)
            _video.Stop();
    }

    void SetAspect(float aspect)
    {
        if (mediaAspect != null)
            mediaAspect.aspectRatio = aspect;
    }

    // Frames packed into an atlas only show their own slice.
    static Rect UvRect(Sprite sprite)
    {
        if (sprite == null)
            return new Rect(0f, 0f, 1f, 1f);
        Rect r = sprite.textureRect;
        Texture t = sprite.texture;
        return new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height);
    }

    static float BackOut(float u)
    {
        const float s = 1.70158f;
        u -= 1f;
        return u * u * ((s + 1f) * u + s) + 1f;
    }
}
