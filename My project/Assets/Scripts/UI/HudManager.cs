using System.Collections;
using TMPro;
using UnityEngine;

public class HudManager : MonoBehaviour
{
    public static HudManager Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private float defaultDuration = 3f;

    private Coroutine hideRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (subtitleText != null)
            subtitleText.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ShowSubtitle(string message, float? durationSeconds = null)
    {
        if (subtitleText == null || string.IsNullOrEmpty(message))
            return;

        if (GameSettingsManager.Instance != null && !GameSettingsManager.Instance.SubtitlesEnabled)
            return;

        float d = durationSeconds ?? defaultDuration;
        subtitleText.text = message;
        subtitleText.gameObject.SetActive(true);

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfter(d));
    }

    public void ShowGameplaySubtitle(string key, float? durationSeconds = null)
    {
        ShowSubtitle(LocalizationService.GetGameplay(key), durationSeconds);
    }

    public void ShowGameplaySubtitle(string key, float? durationSeconds, params (string name, object value)[] args)
    {
        ShowSubtitle(LocalizationService.FormatGameplay(key, args), durationSeconds);
    }

    public void ClearSubtitle()
    {
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
