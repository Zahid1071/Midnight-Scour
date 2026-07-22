using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;

/// <summary>
/// Scrollable demo-end feedback form. Hierarchy is authored under Demo End Panel → Feedback Form.
/// Submit posts Tension / Horror / Fun + optional text to Google Forms or a Discord webhook.
/// </summary>
[DisallowMultipleComponent]
public class DemoEndFeedbackUI : MonoBehaviour
{
    public enum DeliveryMode
    {
        GoogleForm,
        DiscordWebhook
    }

    public const string ThankYouFallback = "Thank you for playing Midnight Scour";
    public const string RatePromptFallback = "How would you rate this experience?";
    public const string TensionFallback = "Tension";
    public const string HorrorFallback = "Horror";
    public const string FunFallback = "Fun";
    public const string FeedbackOptionalFallback = "*optional* feedback area";
    public const string SubmitFallback = "Submit";
    public const string DeveloperNoteFallback =
        "Hi — as a software engineer who just graduated from Haliç University, I tried to show you in this limited release what Midnight Scour could become. Going forward, I want to bring you the full version by expanding the world and atmosphere, building on the foundations we laid here, and growing the budget. Please leave us your feedback so we can improve your experience.";

    const int RatingUnsetIndex = 0;
    const string PrefsHasSubmitted = "demo_feedback_has_submitted";

    static string[] BuildRatingOptions() => new[]
    {
        GetLoc("demo.rate_unset", "?"),
        "1", "2", "3", "4", "5"
    };

    [Header("Layout")]
    [SerializeField] RectTransform formRoot;
    [SerializeField] ScrollRect scrollRect;

    [Header("Copy")]
    [SerializeField] TextMeshProUGUI thankYouLabel;
    [SerializeField] TextMeshProUGUI ratePromptLabel;
    [SerializeField] TextMeshProUGUI tensionLabel;
    [SerializeField] TextMeshProUGUI horrorLabel;
    [SerializeField] TextMeshProUGUI funLabel;
    [SerializeField] TextMeshProUGUI feedbackHintLabel;
    [SerializeField] TextMeshProUGUI submitLabel;
    [SerializeField] TextMeshProUGUI developerNoteLabel;

    [Header("Controls")]
    [SerializeField] SettingsSelector tensionSelector;
    [SerializeField] SettingsSelector horrorSelector;
    [SerializeField] SettingsSelector funSelector;
    [SerializeField] TMP_InputField feedbackInput;
    [SerializeField] Button submitButton;

    [Header("Delivery")]
    [SerializeField] DeliveryMode deliveryMode = DeliveryMode.GoogleForm;

    [Tooltip("Paste the formResponse URL, e.g. https://docs.google.com/forms/d/e/XXXX/formResponse")]
    [SerializeField] string googleFormResponseUrl;

    [Tooltip("Google Form entry IDs like entry.123456789 — from 'Get pre-filled link'.")]
    [SerializeField] string googleEntryTension = "entry.000000000";
    [SerializeField] string googleEntryHorror = "entry.000000000";
    [SerializeField] string googleEntryFun = "entry.000000000";
    [SerializeField] string googleEntryFeedback = "entry.000000000";

    [Tooltip("Discord channel Integrations → Webhooks → Copy Webhook URL")]
    [SerializeField] string discordWebhookUrl;

    [Header("Legacy")]
    [SerializeField] GameObject legacyDeveloperText;

    [Header("Events")]
    public UnityEvent OnSubmitted;

    bool _submitted;
    bool _submitting;
    Coroutine _submitRoutine;

    public bool IsInputFocused =>
        feedbackInput != null && feedbackInput.isFocused;

    public int TensionRating => RatingFromSelector(tensionSelector);
    public int HorrorRating => RatingFromSelector(horrorSelector);
    public int FunRating => RatingFromSelector(funSelector);

    public string FeedbackText =>
        feedbackInput != null ? feedbackInput.text?.Trim() ?? string.Empty : string.Empty;

    public bool HasSubmitted => _submitted;

    void Awake()
    {
        if (legacyDeveloperText != null)
            legacyDeveloperText.SetActive(false);

        _submitted = PlayerPrefs.GetInt(PrefsHasSubmitted, 0) == 1;
        ConfigureRatingSelectors();
        WireSubmit();
        WireScrollRelay();
        if (_submitted)
            ApplyAlreadySubmittedLock();
        RefreshLocalizedTexts();
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        if (_submitRoutine != null)
            StopCoroutine(_submitRoutine);
    }

    void WireScrollRelay()
    {
        if (feedbackInput == null || scrollRect == null)
            return;

        // Stop the input field from consuming wheel movement for its own text scroll.
        feedbackInput.scrollSensitivity = 0f;

        var relay = feedbackInput.GetComponent<FormScrollRelay>();
        if (relay == null)
            relay = feedbackInput.gameObject.AddComponent<FormScrollRelay>();
        relay.Init(scrollRect);
    }

    void OnLocaleChanged(UnityEngine.Localization.Locale _)
    {
        RefreshLocalizedTexts();
        ConfigureRatingSelectors();
        UIFont.ApplyTree(transform);
    }

    public void PrepareForShow()
    {
        if (formRoot != null && !formRoot.gameObject.activeSelf)
            formRoot.gameObject.SetActive(true);

        if (PlayerPrefs.GetInt(PrefsHasSubmitted, 0) == 1)
            ApplyAlreadySubmittedLock();
        else
            ResetFormState();

        RefreshLocalizedTexts();
        UIFont.ApplyTree(transform);
        ApplyRatingSkipperFonts();

        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void OnSubmitClicked()
    {
        if (_submitted || _submitting)
            return;

        if (!HasDeliveryConfigured())
        {
            Debug.LogWarning(
                "[DemoEndFeedback] Delivery is not configured. Fill Google Form URL + entry IDs (or Discord webhook) on DemoEndFeedbackUI.");
            SetSubmitLabelKey("demo.submit_setup_needed", "Setup needed");
            return;
        }

        if (!HasAllRatingsSet())
        {
            SetSubmitLabelKey("demo.submit_rate_all", "Rate all");
            return;
        }

        if (_submitRoutine != null)
            StopCoroutine(_submitRoutine);

        _submitRoutine = StartCoroutine(SubmitRoutine());
    }

    IEnumerator SubmitRoutine()
    {
        _submitting = true;
        if (submitButton != null)
            submitButton.interactable = false;
        SetSubmitLabelKey("demo.submit_sending", "Sending...");

        bool ok = false;

        if (deliveryMode == DeliveryMode.GoogleForm)
            yield return PostGoogleForm(success => ok = success);
        else
            yield return PostDiscordWebhook(success => ok = success);

        _submitting = false;
        _submitRoutine = null;

        if (ok)
        {
            PlayerPrefs.SetInt(PrefsHasSubmitted, 1);
            PlayerPrefs.Save();
            ApplyAlreadySubmittedLock();
            OnSubmitted?.Invoke();
            Debug.Log(
                $"[DemoEndFeedback] Sent Tension={TensionRating} Horror={HorrorRating} Fun={FunRating} Feedback=\"{FeedbackText}\"");
        }
        else
        {
            SetSubmitLabelKey("demo.submit_retry", "Retry");
            if (submitButton != null)
                submitButton.interactable = true;
            Debug.LogWarning("[DemoEndFeedback] Submit failed — check URL / entry IDs and internet.");
        }
    }

    IEnumerator PostGoogleForm(System.Action<bool> done)
    {
        var form = new WWWForm();
        form.AddField(googleEntryTension, TensionRating.ToString());
        form.AddField(googleEntryHorror, HorrorRating.ToString());
        form.AddField(googleEntryFun, FunRating.ToString());
        form.AddField(googleEntryFeedback, FeedbackText);

        using var req = UnityWebRequest.Post(googleFormResponseUrl.Trim(), form);
        yield return req.SendWebRequest();

        // Google often returns 200 even on success; treat network errors as failure.
        bool success = req.result == UnityWebRequest.Result.Success
            || req.responseCode == 200
            || req.responseCode == 302;
        done?.Invoke(success);
    }

    IEnumerator PostDiscordWebhook(System.Action<bool> done)
    {
        string feedbackLine = string.IsNullOrEmpty(FeedbackText) ? "_(none)_" : FeedbackText;
        string body =
            "**Midnight Scour — Demo Feedback**\n" +
            $"Tension: **{TensionRating}/5**\n" +
            $"Horror: **{HorrorRating}/5**\n" +
            $"Fun: **{FunRating}/5**\n" +
            $"Feedback: {feedbackLine}";

        string json = "{\"content\":" + JsonString(body) + "}";
        byte[] raw = Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest(discordWebhookUrl.Trim(), UnityWebRequest.kHttpVerbPOST);
        req.uploadHandler = new UploadHandlerRaw(raw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        done?.Invoke(req.result == UnityWebRequest.Result.Success
            || req.responseCode == 204
            || req.responseCode == 200);
    }

    bool HasDeliveryConfigured()
    {
        if (deliveryMode == DeliveryMode.DiscordWebhook)
            return !string.IsNullOrWhiteSpace(discordWebhookUrl)
                && discordWebhookUrl.Contains("discord.com/api/webhooks");

        if (string.IsNullOrWhiteSpace(googleFormResponseUrl))
            return false;
        if (!googleFormResponseUrl.Contains("docs.google.com/forms"))
            return false;
        if (!googleFormResponseUrl.Contains("formResponse"))
            return false;

        return LooksLikeEntry(googleEntryTension)
            && LooksLikeEntry(googleEntryHorror)
            && LooksLikeEntry(googleEntryFun)
            && LooksLikeEntry(googleEntryFeedback);
    }

    static bool LooksLikeEntry(string entry) =>
        !string.IsNullOrWhiteSpace(entry)
        && entry.StartsWith("entry.")
        && entry != "entry.000000000";

    static string JsonString(string value)
    {
        if (value == null)
            value = string.Empty;
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32)
                        sb.AppendFormat("\\u{0:X4}", (int)c);
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    void ResetFormState()
    {
        _submitted = false;
        _submitting = false;

        tensionSelector?.SetIndex(RatingUnsetIndex);
        horrorSelector?.SetIndex(RatingUnsetIndex);
        funSelector?.SetIndex(RatingUnsetIndex);

        if (feedbackInput != null)
        {
            feedbackInput.text = string.Empty;
            feedbackInput.interactable = true;
        }

        SetRatingControlsInteractable(true);

        if (submitButton != null)
            submitButton.interactable = true;
    }

    void ApplyAlreadySubmittedLock()
    {
        _submitted = true;
        _submitting = false;

        if (submitButton != null)
            submitButton.interactable = false;

        if (feedbackInput != null)
            feedbackInput.interactable = false;

        SetRatingControlsInteractable(false);
        SetSubmitLabelKey("demo.submit_submitted", "Submitted");
    }

    void SetRatingControlsInteractable(bool interactable)
    {
        SetSelectorInteractable(tensionSelector, interactable);
        SetSelectorInteractable(horrorSelector, interactable);
        SetSelectorInteractable(funSelector, interactable);
    }

    static void SetSelectorInteractable(SettingsSelector selector, bool interactable)
    {
        if (selector == null)
            return;

        foreach (var button in selector.GetComponentsInChildren<Button>(true))
            button.interactable = interactable;
    }

    void ConfigureRatingSelectors()
    {
        int tIdx = tensionSelector != null ? tensionSelector.GetCurrentIndex() : RatingUnsetIndex;
        int hIdx = horrorSelector != null ? horrorSelector.GetCurrentIndex() : RatingUnsetIndex;
        int fIdx = funSelector != null ? funSelector.GetCurrentIndex() : RatingUnsetIndex;

        var options = BuildRatingOptions();
        int max = options.Length - 1;

        tensionSelector?.ConfigureAsCustom(options, Mathf.Clamp(tIdx, 0, max));
        horrorSelector?.ConfigureAsCustom(options, Mathf.Clamp(hIdx, 0, max));
        funSelector?.ConfigureAsCustom(options, Mathf.Clamp(fIdx, 0, max));
        ApplyRatingSkipperFonts();
    }

    /// <summary>
    /// Digits 1–5 are ASCII everywhere we ship; keep Cubic_11 so ratings look the same in all locales.
    /// </summary>
    void ApplyRatingSkipperFonts()
    {
        var font = UIFont.ChineseOrNull;
        if (font == null)
            return;

        tensionSelector?.SetForcedFont(font);
        horrorSelector?.SetForcedFont(font);
        funSelector?.SetForcedFont(font);
    }

    void WireSubmit()
    {
        if (submitButton == null)
            return;

        submitButton.onClick.RemoveListener(OnSubmitClicked);
        submitButton.onClick.AddListener(OnSubmitClicked);
    }

    void SetSubmitLabel(string text)
    {
        if (submitLabel != null)
            UIFont.SetText(submitLabel, text);
    }

    void SetSubmitLabelKey(string key, string fallback) =>
        SetSubmitLabel(GetLoc(key, fallback));

    void RefreshLocalizedTexts()
    {
        SetLabel(thankYouLabel, "demo.thank_you", ThankYouFallback);
        SetLabel(ratePromptLabel, "demo.rate_prompt", RatePromptFallback);
        SetLabel(tensionLabel, "demo.rate_tension", TensionFallback);
        SetLabel(horrorLabel, "demo.rate_horror", HorrorFallback);
        SetLabel(funLabel, "demo.rate_fun", FunFallback);
        SetLabel(feedbackHintLabel, "demo.feedback_optional", FeedbackOptionalFallback);
        SetLabel(developerNoteLabel, "demo.developer_note", DeveloperNoteFallback);

        FitLabelHeight(thankYouLabel, minHeight: 45f);
        FitLabelHeight(developerNoteLabel, minHeight: 100f);
        RebuildFormLayout();

        if (_submitting)
            return;

        if (_submitted)
            SetLabel(submitLabel, "demo.submit_submitted", "Submitted");
        else
            SetLabel(submitLabel, "demo.submit", SubmitFallback);

        if (feedbackInput != null && feedbackInput.placeholder is TextMeshProUGUI ph)
            UIFont.SetText(ph, GetLoc("demo.feedback_optional", FeedbackOptionalFallback));
    }

    static void SetLabel(TextMeshProUGUI label, string key, string fallback)
    {
        if (label == null)
            return;
        UIFont.SetText(label, GetLoc(key, fallback));
    }

    /// <summary>
    /// Long locales (Thai etc.) need the LayoutElement height to follow wrapped text,
    /// otherwise Overflow paints over the rows below.
    /// </summary>
    static void FitLabelHeight(TextMeshProUGUI label, float minHeight)
    {
        if (label == null)
            return;

        label.ForceMeshUpdate();
        float height = Mathf.Max(minHeight, label.preferredHeight + 8f);

        var layout = label.GetComponent<LayoutElement>();
        if (layout != null)
            layout.preferredHeight = height;

        var rt = label.rectTransform;
        var size = rt.sizeDelta;
        size.y = height;
        rt.sizeDelta = size;
    }

    void RebuildFormLayout()
    {
        var root = formRoot != null ? formRoot : transform as RectTransform;
        if (root != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    static string GetLoc(string key, string fallback)
    {
        var value = Loc.Get(key);
        if (string.IsNullOrWhiteSpace(value) || value == key)
            return fallback;
        return value;
    }

    static bool HasAllRatingsSet(SettingsSelector tension, SettingsSelector horror, SettingsSelector fun) =>
        RatingFromSelector(tension) > 0
        && RatingFromSelector(horror) > 0
        && RatingFromSelector(fun) > 0;

    bool HasAllRatingsSet() =>
        HasAllRatingsSet(tensionSelector, horrorSelector, funSelector);

    static int RatingFromSelector(SettingsSelector selector)
    {
        if (selector == null)
            return 0;

        int idx = selector.GetCurrentIndex();
        return idx <= RatingUnsetIndex ? 0 : idx;
    }
}
