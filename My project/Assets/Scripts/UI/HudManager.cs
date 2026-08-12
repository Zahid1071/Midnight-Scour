using System.Collections;
using TMPro;
using UnityEngine;

public class HudManager : MonoBehaviour
{
    public static HudManager Instance { get; private set; }

    public const int PriorityNormal = 0;
    public const int PriorityHigh = 10;

    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private float defaultDuration = 6f;

    [Header("Subtitle colors")]
    [SerializeField] private Color defaultSubtitleColor = Color.white;
    [SerializeField] private Color warningSubtitleColor = new Color(1f, 0.92f, 0.35f, 1f);
    [SerializeField] private Color successSubtitleColor = new Color(0.45f, 0.95f, 0.5f, 1f);
    [SerializeField] private Color errorSubtitleColor = new Color(0.95f, 0.3f, 0.3f, 1f);

    [Header("Subtitle shake")]
    [SerializeField] private float shakeDuration = 0.22f;
    [SerializeField] private float shakeStrength = 6f;

    public Color DefaultColor => defaultSubtitleColor;
    public Color WarningColor => warningSubtitleColor;
    public Color SuccessColor => successSubtitleColor;
    public Color ErrorColor => errorSubtitleColor;

    private Coroutine hideRoutine;
    private Coroutine shakeRoutine;
    private RectTransform subtitleRect;
    private Vector2 subtitleRestPos;
    private int _activePriority;
    private float _priorityLockUntilUnscaled;
    private bool _persistent;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (subtitleText != null)
        {
            subtitleText.raycastTarget = false;
            subtitleRect = subtitleText.rectTransform;
            subtitleRestPos = subtitleRect.anchoredPosition;
            defaultSubtitleColor = subtitleText.color;
            subtitleText.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Shows a timed subtitle. Higher <paramref name="priority"/> cannot be replaced by lower
    /// priority lines until its duration ends.
    /// </summary>
    public void ShowSubtitle(string message, float? durationSeconds = null, Color? color = null, int priority = PriorityNormal)
    {
        if (subtitleText == null || string.IsNullOrEmpty(message))
            return;

        ExpirePriorityLockIfNeeded();
        if (priority < _activePriority)
            return;

        float d = durationSeconds ?? defaultDuration;
        _persistent = false;
        _activePriority = priority;
        _priorityLockUntilUnscaled = priority > PriorityNormal
            ? Time.unscaledTime + Mathf.Max(0.05f, d)
            : 0f;

        ApplySubtitle(message, color ?? defaultSubtitleColor);

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfter(d));

        PlaySubtitleShake();
    }

    /// <summary>
    /// Keeps a subtitle up until cleared or replaced. Blocked while a high-priority timed line is locked.
    /// </summary>
    public void ShowPersistentSubtitle(string message, Color? color = null, int priority = PriorityNormal)
    {
        if (subtitleText == null || string.IsNullOrEmpty(message))
            return;

        ExpirePriorityLockIfNeeded();
        if (priority < _activePriority)
            return;

        // Same persistent line already showing — don't restart shake / flicker.
        if (_persistent
            && _activePriority == priority
            && subtitleText.gameObject.activeSelf
            && subtitleText.text == message)
            return;

        _persistent = true;
        _activePriority = priority;
        _priorityLockUntilUnscaled = 0f;

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        ApplySubtitle(message, color ?? defaultSubtitleColor);
    }

    public void ClearSubtitle(int minPriorityToClear = PriorityNormal)
    {
        ExpirePriorityLockIfNeeded();
        if (_activePriority > minPriorityToClear)
            return;

        StopSubtitleShake();

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        _persistent = false;
        _activePriority = PriorityNormal;
        _priorityLockUntilUnscaled = 0f;

        if (subtitleText != null)
        {
            subtitleText.text = string.Empty;
            subtitleText.gameObject.SetActive(false);
        }
    }

    void ExpirePriorityLockIfNeeded()
    {
        if (_activePriority <= PriorityNormal)
            return;

        if (_persistent)
            return;

        if (Time.unscaledTime < _priorityLockUntilUnscaled)
            return;

        _activePriority = PriorityNormal;
        _priorityLockUntilUnscaled = 0f;
    }

    void ApplySubtitle(string message, Color color)
    {
        subtitleText.color = color;
        UIFont.SetText(subtitleText, message);
        subtitleText.gameObject.SetActive(true);
    }

    void PlaySubtitleShake()
    {
        if (subtitleRect == null)
            return;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeSubtitleRoutine());
    }

    void StopSubtitleShake()
    {
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }

        if (subtitleRect != null)
            subtitleRect.anchoredPosition = subtitleRestPos;
    }

    IEnumerator ShakeSubtitleRoutine()
    {
        subtitleRect.anchoredPosition = subtitleRestPos;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float t = 1f - (elapsed / shakeDuration);
            float offset = shakeStrength * t;
            float x = Random.Range(-offset, offset);
            float y = Random.Range(-offset, offset);
            subtitleRect.anchoredPosition = subtitleRestPos + new Vector2(x, y);
            yield return null;
        }

        subtitleRect.anchoredPosition = subtitleRestPos;
        shakeRoutine = null;
    }

    private IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        hideRoutine = null;
        _persistent = false;
        _activePriority = PriorityNormal;
        _priorityLockUntilUnscaled = 0f;

        if (subtitleText != null)
        {
            subtitleText.text = string.Empty;
            subtitleText.gameObject.SetActive(false);
        }
    }
}
