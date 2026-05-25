using System.Collections;
using TMPro;
using UnityEngine;

public class HudManager : MonoBehaviour
{
    public static HudManager Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private float defaultDuration = 3f;

    [Header("Subtitle colors")]
    [SerializeField] private Color defaultSubtitleColor = Color.white;
    [SerializeField] private Color warningSubtitleColor = new Color(1f, 0.92f, 0.35f, 1f);
    [SerializeField] private Color successSubtitleColor = new Color(0.45f, 0.95f, 0.5f, 1f);

    [Header("Subtitle shake")]
    [SerializeField] private float shakeDuration = 0.22f;
    [SerializeField] private float shakeStrength = 6f;

    public Color DefaultColor => defaultSubtitleColor;
    public Color WarningColor => warningSubtitleColor;
    public Color SuccessColor => successSubtitleColor;

    private Coroutine hideRoutine;
    private Coroutine shakeRoutine;
    private RectTransform subtitleRect;
    private Vector2 subtitleRestPos;

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

    public void ShowSubtitle(string message, float? durationSeconds = null, Color? color = null)
    {
        if (subtitleText == null || string.IsNullOrEmpty(message))
            return;

        float d = durationSeconds ?? defaultDuration;
        subtitleText.color = color ?? defaultSubtitleColor;
        subtitleText.text = message;
        subtitleText.gameObject.SetActive(true);

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfter(d));

        PlaySubtitleShake();
    }

    public void ClearSubtitle()
    {
        StopSubtitleShake();

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (subtitleText != null)
        {
            subtitleText.text = string.Empty;
            subtitleText.gameObject.SetActive(false);
        }
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
            elapsed += Time.unscaledDeltaTime;
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
        yield return new WaitForSeconds(seconds);
        hideRoutine = null;
        if (subtitleText != null)
        {
            subtitleText.text = string.Empty;
            subtitleText.gameObject.SetActive(false);
        }
    }
}

