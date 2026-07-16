using UnityEngine;
using UnityEngine.Localization.Settings;
using TMPro;

/// <summary>
/// UI-only selector: shows a value with left/right arrows.
/// Does NOT apply or save settings — SettingsMenu handles that.
/// </summary>
public class SettingsSelector : MonoBehaviour
{
    public enum SettingType
    {
        Resolution,
        Quality,
        Fullscreen,
        Custom,
        Language
    }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI optionText;

    [Header("Setting Configuration")]
    [SerializeField] private SettingType settingType = SettingType.Custom;

    [Tooltip("Localization keys (ui.option.*) or legacy English labels for Custom/Fullscreen.")]
    [SerializeField] private string[] options = { "Option 1", "Option 2", "Option 3" };

    private int currentIndex;
    TMP_FontAsset _forcedFont;

    void Awake()
    {
        InitializeOptions();
        ClampIndex();
        RefreshOptionText();
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        RefreshOptionText();
    }

    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    void OnLocaleChanged(UnityEngine.Localization.Locale _) => RefreshOptionText();

    public void NextOption()
    {
        currentIndex++;
        if (currentIndex >= GetOptionCount())
            currentIndex = 0;
        ClampIndex();
        RefreshOptionText();
        ApplySideEffect();
    }

    public void PreviousOption()
    {
        currentIndex--;
        if (currentIndex < 0)
            currentIndex = GetOptionCount() - 1;
        ClampIndex();
        RefreshOptionText();
        ApplySideEffect();
    }

    public void RefreshOptionText()
    {
        if (optionText == null) return;

        string value = GetCurrentOptionString();
        if (_forcedFont != null)
        {
            optionText.font = _forcedFont;
            if (_forcedFont.material != null)
                optionText.fontSharedMaterial = _forcedFont.material;
            optionText.text = value;
            optionText.ForceMeshUpdate();
            return;
        }

        UIFont.SetText(optionText, value);
    }

    /// <summary>
    /// Keep numbers/digits on a fixed font across locales (e.g. Cubic_11 for ratings).
    /// </summary>
    public void SetForcedFont(TMP_FontAsset font)
    {
        _forcedFont = font;
        RefreshOptionText();
    }

    public void SetIndex(int index)
    {
        currentIndex = Mathf.Clamp(index, 0, Mathf.Max(0, GetOptionCount() - 1));
        RefreshOptionText();
        ApplySideEffect();
    }

    public int GetCurrentIndex() => currentIndex;

    /// <summary>
    /// Reuse this selector as a left/right skipper with custom options (e.g. rating 1-5).
    /// </summary>
    public void ConfigureAsCustom(string[] customOptions, int startIndex = 0, TextMeshProUGUI label = null)
    {
        settingType = SettingType.Custom;
        options = customOptions != null && customOptions.Length > 0
            ? customOptions
            : new[] { "1", "2", "3", "4", "5" };

        if (label != null)
            optionText = label;
        else if (optionText == null)
        {
            var named = transform.Find("OptionText");
            optionText = named != null
                ? named.GetComponent<TextMeshProUGUI>()
                : GetComponentInChildren<TextMeshProUGUI>();
        }

        InitializeOptions();
        SetIndex(startIndex);
    }

    public string GetCurrentOptionString()
    {
        int count = GetOptionCount();
        if (count == 0) return "—";

        switch (settingType)
        {
            case SettingType.Resolution:
                var resolutions = GetResolutions();
                if (resolutions != null && currentIndex >= 0 && currentIndex < resolutions.Length)
                    return $"{resolutions[currentIndex].width}x{resolutions[currentIndex].height}";
                break;

            case SettingType.Quality:
                if (currentIndex >= 0 && currentIndex < QualitySettings.names.Length)
                    return LocalizeQuality(QualitySettings.names[currentIndex]);
                break;

            case SettingType.Fullscreen:
            case SettingType.Custom:
                if (options != null && currentIndex >= 0 && currentIndex < options.Length)
                    return LocalizeOption(options[currentIndex]);
                break;

            case SettingType.Language:
                return LocaleManager.GetLocaleDisplayName(currentIndex);
        }

        return "—";
    }

    static string LocalizeOption(string option)
    {
        if (string.IsNullOrWhiteSpace(option))
            return "—";

        if (option.StartsWith("ui."))
            return Loc.Get(option);

        return option switch
        {
            "Enabled" => Loc.Get("ui.option.enabled"),
            "Disabled" => Loc.Get("ui.option.disabled"),
            "Fullscreen" => Loc.Get("ui.option.fullscreen"),
            "Windowed" => Loc.Get("ui.option.windowed"),
            "Windowed Screen" => Loc.Get("ui.option.windowed"),
            "Exclusive Fullscreen" => Loc.Get("ui.option.exclusive_fullscreen"),
            "Bordered Screen" => Loc.Get("ui.option.bordered"),
            "Low" => Loc.Get("ui.option.quality.low"),
            "Medium" => Loc.Get("ui.option.quality.medium"),
            "High" => Loc.Get("ui.option.quality.high"),
            _ => option
        };
    }

    static string LocalizeQuality(string unityName) => unityName switch
    {
        "Very Low" => Loc.Get("ui.option.quality.very_low"),
        "Low" => Loc.Get("ui.option.quality.low"),
        "Medium" => Loc.Get("ui.option.quality.medium"),
        "High" => Loc.Get("ui.option.quality.high"),
        "Very High" => Loc.Get("ui.option.quality.very_high"),
        "Ultra" => Loc.Get("ui.option.quality.ultra"),
        _ => unityName
    };

    void InitializeOptions()
    {
        switch (settingType)
        {
            case SettingType.Fullscreen:
                if (options == null || options.Length == 0)
                    options = new[] { "ui.option.fullscreen", "ui.option.windowed", "ui.option.exclusive_fullscreen" };
                break;

            case SettingType.Custom:
                if (options == null || options.Length == 0)
                    options = new[] { "ui.option.enabled" };
                break;

            case SettingType.Language:
                currentIndex = LocaleManager.GetCurrentLocaleIndex();
                break;
        }
    }

    int GetOptionCount()
    {
        switch (settingType)
        {
            case SettingType.Resolution:
                var resolutions = GetResolutions();
                return resolutions != null ? resolutions.Length : 0;
            case SettingType.Quality:
                return QualitySettings.names.Length;
            case SettingType.Fullscreen:
            case SettingType.Custom:
                return options != null ? options.Length : 0;
            case SettingType.Language:
                return LocaleManager.LocaleCodes.Length;
            default:
                return 0;
        }
    }

    void ApplySideEffect()
    {
        if (settingType == SettingType.Language)
            LocaleManager.SetLocaleByIndex(currentIndex);
    }

    void ClampIndex()
    {
        int count = GetOptionCount();
        if (count > 0)
            currentIndex = Mathf.Clamp(currentIndex, 0, count - 1);
        else
            currentIndex = 0;
    }

    Resolution[] GetResolutions()
    {
        if (GameSettingsManager.Instance != null && GameSettingsManager.Instance.AvailableResolutions != null)
            return GameSettingsManager.Instance.AvailableResolutions;

        return GameSettingsManager.BuildAvailableResolutions();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (optionText == null && settingType != SettingType.Custom)
        {
            var tmp = GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) optionText = tmp;
        }
    }
#endif
}
